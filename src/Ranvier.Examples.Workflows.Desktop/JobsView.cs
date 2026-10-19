using Avalonia.Controls;
using Avalonia.Data;
using Ranvier;
using Ranvier.CSharp;
using Ranvier.Examples.CSharp.Jobs;

namespace Ranvier.Examples.Workflows.Desktop;

public sealed class JobsView : ReactiveScreen
{
    public FileJobMonitor Model { get; }
    private readonly Signal<string> paths;
    private readonly BoundValue<string> summary, progress, detail, filter;
    private readonly BoundValue<FileJob?> selected;

    public JobsView(Graph graph, FileJobMonitor model) : base(graph)
    {
        Model = model;
        paths = Nodes.Run(() => Reactive.Signal(string.Join(Environment.NewLine, SamplePaths())));
        Nodes.Computed(nameof(Paths), () => paths.Value);
        summary = Nodes.Computed(nameof(Summary), () => Model.Summary);
        progress = Nodes.Computed(nameof(Progress), () => Model.ProgressText);
        detail = Nodes.Computed(nameof(Detail), () => Model.SelectedDetail);
        filter = Nodes.Computed(nameof(Filter), () => Model.Filter);
        selected = Nodes.Computed(nameof(SelectedJob), () => Model.Jobs.Rows.TryGetValue(Model.SelectedId, out var job) ? job : null);
        QueueFiles = Command(_ => Model.Queue(Paths.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        Samples = Command(_ => QueueSamples());
        Cancel = Command(_ => Model.Cancel(Model.SelectedId), () => SelectedJob?.State is "Queued" or "Running");
        Retry = Command(_ => Model.Retry(Model.SelectedId), () => SelectedJob?.State is "Failed" or "Cancelled");
        var list = Own(new StableKeyList(graph, () => Model.Visible.Keys, () => Model.SelectedId,
            id => Nodes.Run(() => Model.SelectedId = id),
            id => Model.Jobs.Rows.TryGetValue(id, out var row) ? row.ToString() : ""));
        list.Name = "FileJobs";
        list.MinHeight = 360;
        var inputs = Ui.Card(Ui.Stack(Ui.Text("READ-ONLY INPUT FILES", 12), Ui.Edit("JobPaths", nameof(Paths), true),
            Ui.Text("One absolute path per line. Files are streamed through SHA-256 with two worker slots.", 12, "#536274"),
            Ui.Actions(Ui.Button("QueueFiles", "Queue files", nameof(QueueFiles)), Ui.Button("QueueSamples", "Queue sample files", nameof(Samples))),
            Ui.Choice("JobFilter", nameof(Filter), ["All", "Queued", "Running", "Completed", "Failed", "Cancelled"]),
            list));
        var results = Ui.Card(Ui.Stack(Ui.Read(nameof(Summary), 22), Ui.Read(nameof(Progress)),
            Ui.Text("SELECTED JOB", 12), Ui.Read(nameof(Detail)),
            Ui.Actions(Ui.Button("CancelJob", "Cancel selected", nameof(Cancel)), Ui.Button("RetryJob", "Retry failed / cancelled", nameof(Retry))),
            Ui.Read(nameof(LastError), 13, "#B42318"),
            Ui.Text("Progress presentation is throttled. Every terminal transition is kept independently; retries reuse the stable job ID.", 12, "#536274")));
        Content = new ScrollViewer { Content = Ui.Columns(inputs, results, "*,*") };
    }
    public string Paths { get => paths.Value; set => Nodes.Run(() => paths.Value = value ?? ""); }
    public string Summary => summary.Value;
    public string Progress => progress.Value;
    public string Detail => detail.Value;
    public string Filter { get => filter.Value; set { if (value is not null) Nodes.Run(() => Model.Filter = value); } }
    public FileJob? SelectedJob { get => selected.Value; set => Nodes.Run(() => Model.SelectedId = value?.Id ?? 0); }
    public ReactiveCommand QueueFiles { get; }
    public ReactiveCommand Samples { get; }
    public ReactiveCommand Cancel { get; }
    public ReactiveCommand Retry { get; }
    private static string[] SamplePaths() => [
        Path.Combine(AppContext.BaseDirectory, "samples", "jobs", "import-notes.txt"),
        Path.Combine(AppContext.BaseDirectory, "samples", "jobs", "release-checklist.txt"),
        Path.Combine(AppContext.BaseDirectory, "samples", "measurements.csv") ];
    public void QueueSamples() => Nodes.Run(() => Model.Queue(SamplePaths()));
}
