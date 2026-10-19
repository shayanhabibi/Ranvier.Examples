using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Ranvier;
using Ranvier.CSharp;
using Ranvier.Examples.FSharp;

namespace Ranvier.Examples.Workflows.Desktop;

public sealed class ExpenseReviewView : ReactiveScreen
{
    public ExpenseReview Model { get; }
    private readonly BoundValue<ExpenseDecision[]> rows;
    private readonly BoundValue<ExpenseDecision?> selection;
    private readonly BoundValue<string> reason, filter, detail, summary, status, failure;

    public ExpenseReviewView(Graph graph, ExpenseReview model, string input, string output) : base(graph)
    {
        Model = model;
        rows = Nodes.Computed(nameof(Rows), () => Model.Visible);
        selection = Nodes.Computed(nameof(Selected), () => Model.Snapshot.FirstOrDefault(row => row.Claim.Id == Model.SelectedId));
        reason = Nodes.Computed(nameof(Reason), () => Model.Reason);
        filter = Nodes.Computed(nameof(Filter), () => Model.Filter);
        detail = Nodes.Computed(nameof(Detail), () => Model.Detail);
        summary = Nodes.Computed(nameof(Summary), () => $"Approved {Model.ApprovedTotal:F2} · {Model.Snapshot.Length} claims"
            + (Model.IsDirty ? " · unsaved decisions" : " · saved baseline"));
        status = Nodes.Computed(nameof(Status), () => Model.Status);
        failure = Nodes.Computed(nameof(Failure), () => Model.Error);
        Load = Nodes.Command(_ => Model.Load(), () => Model.CanLoad);
        Approve = Nodes.Command(_ => Model.Approve(), () => Model.SelectedId != 0 && !Model.IsLoading);
        Reject = Nodes.Command(_ => Model.Reject(), () => Model.SelectedId != 0 && !Model.IsLoading);
        Undo = Nodes.Command(_ => Model.Undo(), () => Model.CanUndo);
        Redo = Nodes.Command(_ => Model.Redo(), () => Model.CanRedo);
        Save = Nodes.Command(_ => Model.Save(), () => Model.CanSave);
        WatchErrors(Load, Approve, Reject, Undo, Redo, Save);

        var list = new ListBox
        {
            Name = "ExpenseRows",
            MinHeight = 300,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(Rows)),
            [!ListBox.SelectedItemProperty] = new Binding(nameof(Selected)) { Mode = BindingMode.TwoWay },
            ItemTemplate = new FuncDataTemplate<ExpenseDecision>((row, _) => row is null ? null
                : Ui.Text($"#{row.Claim.Id} · {row.Claim.Employee} · {row.Claim.Amount:F2}\n{row.Claim.Purpose}\n{row.Decision}"))
        };
        var queue = Ui.Card(Ui.Stack(Ui.Text("EXPENSE CLAIMS / ELMISH", 12),
            Ui.Text("Import JSON claims, review receipts, and write a decision report.", 14),
            Ui.Choice("ExpenseFilter", nameof(Filter), ["All", "Pending", "Approved", "Rejected"]),
            list, Ui.Button("ExpenseLoad", "Reload claims", nameof(Load)),
            Ui.Text("INPUT FILE", 12), Ui.Text(input, 12),
            Ui.Text("Reload becomes available after saving all staged decisions.", 12, "#536274")));
        var editor = Ui.Card(Ui.Stack(Ui.Read(nameof(Summary), 22), Ui.Read(nameof(Detail), 16),
            Ui.Text("Reason for rejection", 12), Ui.Edit("ExpenseReason", nameof(Reason), true),
            Ui.Actions(Ui.Button("ExpenseApprove", "Approve", nameof(Approve)), Ui.Button("ExpenseReject", "Reject with reason", nameof(Reject))),
            Ui.Actions(Ui.Button("ExpenseUndo", "Undo decision", nameof(Undo)), Ui.Button("ExpenseRedo", "Redo decision", nameof(Redo))),
            Ui.Button("ExpenseSave", "Save decision snapshot", nameof(Save)),
            Ui.Read(nameof(Status)), Ui.Read(nameof(Failure), 13, "#B42318"), Ui.Read(nameof(LastError), 13, "#B42318"),
            Ui.Text("OUTPUT REPORT", 12), Ui.Text(output, 12),
            Ui.Text("Messages update an immutable model. Commands perform file I/O. Narrow selectors keep total readers quiet during selection changes.", 12, "#536274"),
            Ui.Text("A save captures its revision. You can continue reviewing while it writes; newer decisions remain unsaved.", 12, "#536274")));
        Content = new ScrollViewer { Content = Ui.Columns(queue, editor, "*,*") };
    }

    public ExpenseDecision[] Rows => rows.Value;
    public ExpenseDecision? Selected { get => selection.Value; set { if (value is not null) Nodes.Run(() => Model.SelectedId = value.Claim.Id); } }
    public string Reason { get => reason.Value; set => Nodes.Run(() => Model.Reason = value); }
    public string Filter { get => filter.Value; set { if (value is not null) Nodes.Run(() => Model.Filter = value); } }
    public string Detail => detail.Value;
    public string Summary => summary.Value;
    public string Status => status.Value;
    public string Failure => failure.Value;
    public ReactiveCommand Load { get; }
    public ReactiveCommand Approve { get; }
    public ReactiveCommand Reject { get; }
    public ReactiveCommand Undo { get; }
    public ReactiveCommand Redo { get; }
    public ReactiveCommand Save { get; }
}
