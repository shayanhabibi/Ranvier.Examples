using Avalonia.Controls;
using Avalonia.Data;
using Ranvier;
using Ranvier.CSharp;
using Ranvier.Examples.FSharp;

namespace Ranvier.Examples.Workflows.Desktop;

public sealed class DataQualityView : ReactiveScreen
{
    public DataQualityModel Model { get; }
    private readonly Signal<string> source, destination;
    private readonly BoundValue<string> summary, status, calibration, raw, unit, rowError, rowInfo, filter;
    private readonly BoundValue<bool> included, editing;
    private readonly BoundValue<int> selectedIndex;
    private MeasurementRow? Current => Model.Rows.TryGetValue(Model.SelectedId, out var row) ? row : null;

    public DataQualityView(Graph graph, DataQualityModel model, string sourcePath, string outputPath) : base(graph)
    {
        Model = model;
        source = Nodes.Run(() => Reactive.Signal(sourcePath));
        destination = Nodes.Run(() => Reactive.Signal(outputPath));
        Nodes.Computed(nameof(Source), () => source.Value);
        Nodes.Computed(nameof(Destination), () => destination.Value);
        summary = Nodes.Computed(nameof(Summary), () => Model.Summary);
        status = Nodes.Computed(nameof(Status), () => Model.Status);
        calibration = Nodes.Computed(nameof(Calibration), () => Model.Calibration);
        raw = Nodes.Computed(nameof(Raw), () => Current?.RawText ?? "");
        unit = Nodes.Computed(nameof(Unit), () => Current?.Unit ?? "");
        included = Nodes.Computed(nameof(Included), () => Current?.Included ?? false);
        editing = Nodes.Computed(nameof(Editing), () => Current is not null);
        rowError = Nodes.Computed(nameof(RowError), () => Current?.Error ?? "");
        rowInfo = Nodes.Computed(nameof(RowInfo), () =>
        {
            var row = Current;
            if (row is null) return "Select a measurement";
            _ = row.Display;
            return $"#{row.Id} · {row.Station} · {row.EvaluationCount} normalization executions"
                + (row.HasConflict ? "\nSource changed; your draft is preserved." : row.IsEdited ? "\nLocal draft" : "\nAccepted source values");
        });
        filter = Nodes.Computed(nameof(Filter), () => Model.Filter);
        selectedIndex = Nodes.Computed(nameof(SelectedIndex), () => Array.IndexOf(Model.VisibleIds, Model.SelectedId));
        Load = Command(async (_, _) => await Model.LoadAsync(Source));
        Reload = Command(async (_, _) => await Model.ReloadAsync(), () => Model.SourcePath.Length != 0);
        Export = Command(async (_, _) => await Model.ExportAsync(Destination),
            () => Model.InvalidCount == 0 && Model.ValidCount > 0 && !string.IsNullOrWhiteSpace(destination.Value));
        Celsius = Command(_ => Model.ApplyUnitToIncluded("C"));
        Fahrenheit = Command(_ => Model.ApplyUnitToIncluded("F"));
        Kelvin = Command(_ => Model.ApplyUnitToIncluded("K"));
        Exclude = Command(_ => Model.ExcludeInvalid());
        Accept = Command(_ => { if (Current is not null) Model.AcceptUpstream(Model.SelectedId); }, () => Current is not null);
        var list = Own(new StableKeyList(graph, () => Model.VisibleIds, () => Model.SelectedId,
            id => Nodes.Run(() => Model.SelectedId = id),
            id => Model.Rows.TryGetValue(id, out var row) ? row.Display : ""));
        list.Name = "MeasurementRows";
        list.MinHeight = 340;
        var data = Ui.Card(Ui.Stack(Ui.Text("SOURCE CSV", 12), Ui.Edit("CsvSource", nameof(Source)),
            Ui.Actions(Ui.Button("CsvLoad", "Load", nameof(Load)), Ui.Button("CsvReload", "Reload source", nameof(Reload))),
            Ui.Read(nameof(Status), 12), Ui.Choice("CsvFilter", nameof(Filter), ["All", "Invalid", "Included", "Edited"]), list));
        var editor = Ui.Card(Ui.Stack(Ui.Read(nameof(Summary), 21), Ui.Read(nameof(RowInfo), 13),
            Ui.Text("Raw reading", 12), Ui.Edit("MeasurementValue", nameof(Raw)),
            Ui.Text("Unit · C, F or K", 12), Ui.Edit("MeasurementUnit", nameof(Unit)),
            new CheckBox { Name = "MeasurementIncluded", Content = "Include in statistics and export", [!CheckBox.IsCheckedProperty] = new Binding(nameof(Included)) { Mode = BindingMode.TwoWay } },
            Ui.Read(nameof(RowError), 13, "#B42318"), Ui.Read(nameof(Calibration), 13),
            Ui.Button("AcceptMeasurement", "Use source values for row", nameof(Accept)),
            Ui.Text("Convert included valid rows together", 12),
            Ui.Actions(Ui.Button("ToCelsius", "To C", nameof(Celsius)), Ui.Button("ToFahrenheit", "To F", nameof(Fahrenheit)), Ui.Button("ToKelvin", "To K", nameof(Kelvin))),
            Ui.Button("ExcludeInvalid", "Exclude invalid rows", nameof(Exclude)),
            Ui.Text("EXPORT DESTINATION", 12), Ui.Edit("CsvDestination", nameof(Destination)),
            Ui.Button("CsvExport", "Export included rows", nameof(Export)), Ui.Read(nameof(LastError), 13, "#B42318"),
            Ui.Text("Raw invalid text stays editable. Reload preserves local drafts. Unchanged rows reuse their cached calculations.", 12, "#536274")));
        Content = new ScrollViewer { Content = Ui.Columns(data, editor, "*,*") };
    }
    public string Source { get => source.Value; set => Nodes.Run(() => source.Value = value ?? ""); }
    public string Destination { get => destination.Value; set => Nodes.Run(() => destination.Value = value ?? ""); }
    public string Summary => summary.Value;
    public string Status => status.Value;
    public string Calibration => calibration.Value;
    public string Raw { get => raw.Value; set { if (Current is not null) Nodes.Run(() => Current!.RawText = value); } }
    public string Unit { get => unit.Value; set { if (Current is not null) Nodes.Run(() => Current!.Unit = value); } }
    public bool Included { get => included.Value; set { if (Current is not null) Nodes.Run(() => Current!.Included = value); } }
    public bool Editing => editing.Value;
    public string RowError => rowError.Value;
    public string RowInfo => rowInfo.Value;
    public string Filter { get => filter.Value; set { if (value is not null) Nodes.Run(() => Model.Filter = value); } }
    public int SelectedIndex
    {
        get => selectedIndex.Value;
        set { var ids = Model.VisibleIds; if (value >= 0 && value < ids.Length) Nodes.Run(() => Model.SelectedId = ids[value]); }
    }
    public ReactiveCommand Load { get; }
    public ReactiveCommand Reload { get; }
    public ReactiveCommand Export { get; }
    public ReactiveCommand Celsius { get; }
    public ReactiveCommand Fahrenheit { get; }
    public ReactiveCommand Kelvin { get; }
    public ReactiveCommand Exclude { get; }
    public ReactiveCommand Accept { get; }
}
