using Ranvier;
using Ranvier.Examples.FSharp;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class ExpenseReviewTests
{
    private static ExpenseClaim[] Claims() => [
        new(1, "Ada", "Client workshop train", 86m, true),
        new(2, "Grace", "Team lunch", 54m, false),
        new(3, "Linus", "Replacement adapter", 32m, true) ];

    private static ExpenseReview Create(Graph graph, Func<ExpenseDecision[], CancellationToken, Task>? save = null) =>
        new(graph, _ => Task.FromResult(Claims()), save ?? ((_, _) => Task.CompletedTask));

    [Fact]
    public void DecisionsValidateAndUndoRedoPreserveNavigation()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        using var review = Create(graph);
        review.Load(); GraphTasks.Complete(graph, review.Completion);
        review.SelectedId = 2;
        review.Approve();
        Assert.Equal("Pending", review.SelectedDecision);
        Assert.Contains("receipt", review.Error, StringComparison.OrdinalIgnoreCase);
        review.Reject();
        Assert.Contains("reason", review.Error, StringComparison.OrdinalIgnoreCase);
        review.Reason = "Receipt required";
        review.Reject();
        Assert.Equal("Rejected", review.SelectedDecision);
        review.SelectedId = 1; review.Filter = "Pending";
        review.Undo();
        Assert.Equal(1, review.SelectedId);
        Assert.Equal("Pending", review.Filter);
        Assert.Equal("Pending", review.Snapshot.Single(row => row.Claim.Id == 2).Decision);
        review.Redo();
        Assert.Equal("Rejected", review.Snapshot.Single(row => row.Claim.Id == 2).Decision);
        review.Approve();
        Assert.False(review.CanRedo);
        Assert.Equal(86m, review.ApprovedTotal);
    }

    [Fact]
    public void SelectionDoesNotNotifyApprovedTotalReaders()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        using var review = Create(graph);
        review.Load(); GraphTasks.Complete(graph, review.Completion);
        var totals = new List<decimal>();
        using var subscription = review.ObserveApprovedTotal(totals.Add);
        review.SelectedId = 1; review.Filter = "Pending"; review.Reason = "Reviewed";
        Assert.Single(totals);
        review.Approve();
        Assert.Equal(new[] { 0m, 86m }, totals);
    }

    [Fact]
    public void SaveAcknowledgesCapturedRevisionAndFailureAllowsRetry()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExpenseDecision[]? captured = null;
        int calls = 0;
        using var review = Create(graph, (rows, _) =>
        {
            captured = rows;
            return ++calls == 1 ? Task.FromException(new IOException("Disk unavailable")) : pending.Task;
        });
        review.Load(); GraphTasks.Complete(graph, review.Completion);
        review.SelectedId = 1; review.Approve();
        review.Save(); GraphTasks.Complete(graph, review.Completion);
        Assert.Contains("Disk unavailable", review.Error);
        Assert.True(review.CanSave);
        review.Save();
        review.SelectedId = 3; review.Approve();
        pending.SetResult(); GraphTasks.Complete(graph, review.Completion);
        Assert.True(review.IsDirty);
        Assert.Equal("Pending", captured!.Single(row => row.Claim.Id == 3).Decision);
        Assert.Equal(118m, review.ApprovedTotal);
    }

    [Fact]
    public void RealJsonCommandsRoundTripAndMalformedReloadRetainsAcceptedRows()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "expense-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "claims.json"), output = Path.Combine(folder, "decisions.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "samples", "expenses.json"), input);
        using var graph = new Graph(); using var active = graph.Activate();
        using var review = new ExpenseReview(graph, token => ExpenseFiles.LoadAsync(input, token),
            (rows, token) => ExpenseFiles.SaveAsync(output, rows, token));
        review.Load(); GraphTasks.Complete(graph, review.Completion);
        review.SelectedId = 1; review.Approve();
        review.Save(); GraphTasks.Complete(graph, review.Completion);
        Assert.False(review.IsDirty);
        Assert.Contains("\"Approved\"", File.ReadAllText(output));
        File.WriteAllText(input, "[{\"id\":1}]");
        review.Load(); GraphTasks.Complete(graph, review.Completion);
        Assert.NotEmpty(review.Error);
        Assert.Equal(3, review.Snapshot.Length);
        Assert.Equal(86m, review.ApprovedTotal);
        Directory.Delete(folder, true);
    }

    [Fact]
    public void DisposalIgnoresUncooperativeLateLoad()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        var load = new TaskCompletionSource<ExpenseClaim[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var review = new ExpenseReview(graph, _ => load.Task, (_, _) => Task.CompletedTask);
        review.Load();
        var completion = review.Completion;
        review.Dispose();
        load.SetResult(Claims());
        GraphTasks.Complete(graph, completion);
        Assert.True(review.IsDisposed);
    }
}
