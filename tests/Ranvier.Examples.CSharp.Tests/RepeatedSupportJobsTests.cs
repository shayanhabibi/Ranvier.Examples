using Ranvier;
using Ranvier.Examples.CSharp.Support;
using Ranvier.Examples.FSharp;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class RepeatedSupportJobsTests
{
    private sealed class VersionedService : ITicketService
    {
        internal TicketRecord Ticket = new(1, "Repeated support", "Open", "Original", 1);
        internal bool Offline;
        internal TaskCompletionSource<TicketRecord>? PendingSave;
        internal int LastExpectedVersion;

        public Task<TicketRecord[]> SearchAsync(string term, CancellationToken token) => Task.FromResult(new[] { Ticket });
        public Task<TicketRecord> GetAsync(int id, CancellationToken token) => Offline
            ? Task.FromException<TicketRecord>(new HttpRequestException("Offline"))
            : Task.FromResult(Ticket);
        public Task<TicketRecord> SaveAsync(TicketUpdate update, CancellationToken token)
        {
            LastExpectedVersion = update.ExpectedVersion;
            if (PendingSave is { } pending) return pending.Task;
            if (update.ExpectedVersion != Ticket.Version)
                return Task.FromException<TicketRecord>(new InvalidOperationException("Stale version"));
            Ticket = Ticket with { Notes = update.Notes, State = update.State, Version = Ticket.Version + 1 };
            return Task.FromResult(Ticket);
        }
    }

    [Fact]
    public void ReopenedEditorCanSaveAgainAfterEarlierSessionOwnedSaveCompletes()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var service = new VersionedService(); using var desk = new SupportDesk(graph, service);
        GraphTasks.Complete(graph, desk.OpenAsync(1));
        desk.DraftNotes = "First save";
        service.PendingSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSave = desk.SaveAsync();
        desk.Back();
        GraphTasks.Complete(graph, desk.OpenAsync(1));
        service.Ticket = service.Ticket with { Notes = "First save", Version = 2 };
        service.PendingSave.SetResult(service.Ticket);
        Assert.True(GraphTasks.Complete(graph, firstSave));
        service.PendingSave = null;
        desk.DraftNotes = "Second save";
        Assert.True(GraphTasks.Complete(graph, desk.SaveAsync()));
        Assert.Equal(2, service.LastExpectedVersion);
        Assert.Equal("Second save", service.Ticket.Notes);
    }

    [Fact]
    public void EditorOpenedOfflineCanSaveAfterSuccessfulRefresh()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var service = new VersionedService { Offline = true }; using var desk = new SupportDesk(graph, service);
        Assert.Throws<HttpRequestException>(() => GraphTasks.Complete(graph, desk.OpenAsync(1)));
        service.Offline = false;
        GraphTasks.Complete(graph, desk.RefreshAsync());
        Assert.Equal(1, desk.SelectedId);
        desk.DraftNotes = "Recovered draft";
        Assert.True(GraphTasks.Complete(graph, desk.SaveAsync()));
        Assert.Equal(1, service.LastExpectedVersion);
    }
}
