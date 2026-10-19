using Ranvier;
using Ranvier.Examples.CSharp.Support;
using Ranvier.Examples.FSharp;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

internal static class GraphTasks
{
    internal static void Complete(Graph graph, Task task)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < timeout) { graph.Pump(); Thread.Sleep(1); }
        graph.Pump();
        Assert.True(task.IsCompleted, "Work did not settle while pumping the graph.");
        task.GetAwaiter().GetResult();
    }
    internal static T Complete<T>(Graph graph, Task<T> task) { Complete(graph, (Task)task); return task.GetAwaiter().GetResult(); }
}

internal sealed class ControlledTicketService : ITicketService
{
    internal TicketRecord Ticket = new(1, "Worker export", "Open", "Original note", 1);
    internal Func<string, Task<TicketRecord[]>>? Search;
    internal TaskCompletionSource<TicketRecord>? Save;
    internal int Writes;
    public Task<TicketRecord[]> SearchAsync(string term, CancellationToken token) => Search?.Invoke(term) ?? Task.FromResult(new[] { Ticket });
    public Task<TicketRecord> GetAsync(int id, CancellationToken token) => Task.FromResult(Ticket with { Id = id });
    public Task<TicketRecord> SaveAsync(TicketUpdate update, CancellationToken token)
    {
        Writes++;
        return Save?.Task ?? Task.FromResult(Ticket = Ticket with { Notes = update.Notes, State = update.State, Version = Ticket.Version + 1 });
    }
}

public sealed class SupportDeskTests
{
    [Fact]
    public void RefreshPreservesDraftAndExposesChangedBaseline()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var service = new ControlledTicketService(); using var desk = new SupportDesk(graph, service);
        GraphTasks.Complete(graph, desk.OpenAsync(1));
        desk.DraftNotes = "My unsaved investigation";
        service.Ticket = service.Ticket with { Notes = "Colleague changed server note", Version = 2 };
        GraphTasks.Complete(graph, desk.RefreshAsync());
        Assert.Equal("My unsaved investigation", desk.DraftNotes);
        Assert.Equal("Colleague changed server note", desk.ServerNotes);
        Assert.True(desk.IsDirty);
        Assert.True(desk.HasConflict);
        desk.AcceptUpstream();
        Assert.False(desk.IsDirty);
        Assert.False(desk.HasConflict);
        Assert.Equal(service.Ticket.Notes, desk.DraftNotes);
    }

    [Fact]
    public void OlderSearchCannotReplaceNewerQueue()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var service = new ControlledTicketService();
        var a = new TaskCompletionSource<TicketRecord[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = new TaskCompletionSource<TicketRecord[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Search = term => term == "A" ? a.Task : term == "B" ? b.Task : Task.FromResult(new[] { service.Ticket });
        using var desk = new SupportDesk(graph, service);
        var old = desk.SearchAsync("A");
        var current = desk.SearchAsync("B");
        b.SetResult([new(2, "B", "Open", "", 1)]);
        GraphTasks.Complete(graph, current);
        a.SetResult([new(1, "A", "Open", "", 1)]);
        graph.Pump();
        Assert.Equal(2, Assert.Single(desk.Queue).Id);
        Assert.True(old.IsCanceled);
    }

    [Fact]
    public void SessionOwnedSaveFinishesAfterBackAndRefreshesRetainedQueue()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var service = new ControlledTicketService();
        using var desk = new SupportDesk(graph, service);
        GraphTasks.Complete(graph, desk.SearchAsync(""));
        GraphTasks.Complete(graph, desk.OpenAsync(1));
        desk.DraftNotes = "Captured for save";
        service.Save = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = desk.SaveAsync();
        desk.Back();
        Assert.Equal(0, desk.SelectedId);
        service.Ticket = service.Ticket with { Notes = "Captured for save", Version = 2 };
        service.Save.SetResult(service.Ticket);
        Assert.True(GraphTasks.Complete(graph, save));
        Assert.Equal("Captured for save", Assert.Single(desk.Queue).Notes);
        Assert.Equal(1, service.Writes);
    }

    [Fact]
    public void FailedRefreshRetainsAcceptedQueueAndCanBeRetried()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var service = new ControlledTicketService(); using var desk = new SupportDesk(graph, service);
        GraphTasks.Complete(graph, desk.SearchAsync(""));
        service.Search = _ => Task.FromException<TicketRecord[]>(new HttpRequestException("Connection unavailable"));
        Assert.Throws<HttpRequestException>(() => { GraphTasks.Complete(graph, desk.RefreshQueueAsync()); });
        Assert.NotEmpty(desk.Queue);
        Assert.Contains("unavailable", desk.QueueStatus, StringComparison.OrdinalIgnoreCase);
        service.Search = _ => Task.FromResult(new[] { service.Ticket });
        GraphTasks.Complete(graph, desk.RefreshQueueAsync());
        Assert.DoesNotContain("unavailable", desk.QueueStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClosingSessionCancelsWaitersAndRejectsLatePublication()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var service = new ControlledTicketService();
        var delayed = new TaskCompletionSource<TicketRecord[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Search = _ => delayed.Task;
        var desk = new SupportDesk(graph, service);
        var request = desk.SearchAsync("late");
        desk.Dispose();
        delayed.SetResult([service.Ticket]);
        graph.Pump();
        Assert.True(request.IsCanceled);
    }
}
