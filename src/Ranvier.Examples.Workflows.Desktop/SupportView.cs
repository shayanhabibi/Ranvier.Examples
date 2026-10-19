using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Ranvier;
using Ranvier.CSharp;
using Ranvier.Examples.CSharp.Support;
using Ranvier.Examples.FSharp;

namespace Ranvier.Examples.Workflows.Desktop;

public sealed class SupportView : ReactiveScreen
{
    public SupportDesk Model { get; }
    private readonly BoundValue<TicketRecord[]> tickets;
    private readonly BoundValue<TicketRecord?> selected;
    private readonly BoundValue<string> search, draft, state, server, heading, status, feedback, conflict;
    private readonly BoundValue<bool> editing;
    private readonly Signal<bool> online;
    private readonly BoundValue<string> connection;

    public SupportView(Graph graph, SupportDesk model, SupportApi api, Func<Task> externalUpdate) : base(graph)
    {
        Model = model;
        online = Nodes.Run(() => Reactive.Signal(true));
        tickets = Nodes.Computed(nameof(Tickets), () => Model.Queue);
        selected = Nodes.Computed(nameof(SelectedTicket), () => Model.Queue.FirstOrDefault(t => t.Id == Model.SelectedId));
        search = Nodes.Computed(nameof(Search), () => Model.SearchText);
        draft = Nodes.Computed(nameof(Draft), () => Model.DraftNotes);
        state = Nodes.Computed(nameof(State), () => Model.DraftState);
        server = Nodes.Computed(nameof(ServerNotes), () => Model.ServerNotes);
        heading = Nodes.Computed(nameof(Heading), () => Model.DetailSummary);
        status = Nodes.Computed(nameof(Status), () => Model.QueueStatus);
        feedback = Nodes.Computed(nameof(Feedback), () => Model.Message);
        editing = Nodes.Computed(nameof(Editing), () => Model.SelectedId != 0);
        conflict = Nodes.Computed(nameof(Conflict), () => Model.HasConflict ? "The server changed. Your draft is preserved; choose which baseline to use."
            : Model.IsDirty ? "Unsaved local draft" : "Draft follows the accepted record");
        connection = Nodes.Computed(nameof(ConnectionAction), () => online.Value ? "Disconnect API" : "Restore API");
        Save = Nodes.Command(async (_, _) => { await Model.SaveAsync(); }, () => Model.CanSave);
        Refresh = Nodes.Command(async (_, _) => { await Model.RefreshAsync(); });
        Back = Nodes.Command(_ => Model.Back());
        Accept = Nodes.Command(_ => Model.AcceptUpstream(), () => Model.SelectedId != 0);
        Rebase = Nodes.Command(_ => Model.Rebase(), () => Model.HasConflict);
        ExternalUpdate = Nodes.Command(async (_, _) => await externalUpdate(), () => Model.SelectedId != 0);
        Connection = Nodes.Command(_ =>
        {
            online.Value = !online.Value;
            api.Available = online.Value;
            Fire(async () => { await Model.RefreshQueueAsync(); });
        });

        var list = new ListBox
        {
            Name = "SupportQueue",
            MinHeight = 330,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(Tickets)),
            [!ListBox.SelectedItemProperty] = new Binding(nameof(SelectedTicket)) { Mode = BindingMode.TwoWay }
        };
        var queue = Ui.Card(Ui.Stack(Ui.Text("TICKET QUEUE", 12),
            Ui.Edit("SupportSearch", nameof(Search)), Ui.Read(nameof(Status), 12), list,
            Ui.Button("SupportConnection", "", nameof(Connection))));
        var apiButton = ((StackPanel)queue.Child!).Children.OfType<Button>().Single();
        apiButton.Bind(ContentControl.ContentProperty, new Binding(nameof(ConnectionAction)));
        var draftBox = Ui.Edit("SupportDraft", nameof(Draft), true);
        draftBox.Bind(IsEnabledProperty, new Binding(nameof(Editing)));
        var editor = Ui.Card(Ui.Stack(
            Ui.Read(nameof(Heading), 22), Ui.Text("Accepted server note", 12), Ui.Read(nameof(ServerNotes)),
            Ui.Text("Your investigation draft", 12), draftBox,
            Ui.Choice("SupportState", nameof(State), ["Open", "Investigating", "Resolved"]),
            Ui.Read(nameof(Conflict), 13, "#8A4F00"),
            Ui.Actions(Ui.Button("SupportSave", "Save draft", nameof(Save)), Ui.Button("SupportBack", "Back to queue", nameof(Back)), Ui.Button("SupportRefresh", "Refresh", nameof(Refresh))),
            Ui.Actions(Ui.Button("SupportAccept", "Use server values", nameof(Accept)), Ui.Button("SupportRebase", "Keep draft / use new version", nameof(Rebase))),
            Ui.Button("SupportExternal", "Apply colleague update through API", nameof(ExternalUpdate)),
            Ui.Read(nameof(Feedback), 13), Ui.Read(nameof(LastError), 13, "#B42318"),
            Ui.Text("Save a draft, then go Back. The session owns the save while the editor and its detail lease are released.", 12, "#536274")));
        WatchErrors(Save, Refresh, Back, Accept, Rebase, ExternalUpdate, Connection);
        Content = new ScrollViewer { Content = Ui.Columns(queue, editor) };
    }

    public TicketRecord[] Tickets => tickets.Value;
    public TicketRecord? SelectedTicket
    {
        get => selected.Value;
        set { if (value is not null && value.Id != Model.SelectedId) Fire(async () => await Model.OpenAsync(value.Id)); }
    }
    public string Search { get => search.Value; set => Nodes.Run(() => Model.SearchText = value); }
    public string Draft { get => draft.Value; set => Nodes.Run(() => Model.DraftNotes = value); }
    public string State { get => state.Value; set { if (value is not null) Nodes.Run(() => Model.DraftState = value); } }
    public string ServerNotes => server.Value;
    public string Heading => heading.Value;
    public string Status => status.Value;
    public string Feedback => feedback.Value;
    public string Conflict => conflict.Value;
    public bool Editing => editing.Value;
    public string ConnectionAction => connection.Value;
    public ReactiveCommand Save { get; }
    public ReactiveCommand Refresh { get; }
    public ReactiveCommand Back { get; }
    public ReactiveCommand Accept { get; }
    public ReactiveCommand Rebase { get; }
    public ReactiveCommand ExternalUpdate { get; }
    public ReactiveCommand Connection { get; }
}
