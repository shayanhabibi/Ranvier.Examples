using System.Security.Cryptography;
using Ranvier;
using Ranvier.CSharp;
using Ranvier.Examples.CSharp.Jobs;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class FileJobMonitorTests
{
    [Fact]
    public void HashesActualFilesWithBoundedWorkersAndReactiveSummary()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "job-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string[] paths = Enumerable.Range(1, 4).Select(i => Path.Combine(folder, $"input-{i}.txt")).ToArray();
        foreach (string path in paths) File.WriteAllText(path, new string('x', 100_000));
        using var graph = new Graph(); using var active = graph.Activate();
        using var jobs = new FileJobMonitor(graph, 2);
        jobs.Queue(paths);
        GraphTasks.Complete(graph, jobs.Completion);
        Assert.Equal(4, jobs.Succeeded);
        Assert.Equal(0, jobs.Failed);
        Assert.InRange(jobs.PeakWorkers, 1, 2);
        string expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths[0])));
        Assert.All(jobs.Snapshot, row => Assert.Equal(expected, row.Digest));
        Assert.Equal(400_000, jobs.TotalBytes);
        jobs.Filter = "Completed";
        Assert.Equal(4, jobs.VisibleCount);
        jobs.Filter = "Failed";
        Assert.Equal(0, jobs.VisibleCount);
        Directory.Delete(folder, true);
    }

    [Fact]
    public void MissingFileFailsAndRetryRunsTheRepairedPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "job-tests", Guid.NewGuid() + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var graph = new Graph(); using var active = graph.Activate();
        using var jobs = new FileJobMonitor(graph, 1);
        int id = jobs.Queue([path]).Single();
        GraphTasks.Complete(graph, jobs.Completion);
        Assert.Equal(1, jobs.Failed);
        Assert.NotEmpty(jobs.Snapshot.Single().Error);
        File.WriteAllText(path, "repaired input");
        jobs.Retry(id);
        GraphTasks.Complete(graph, jobs.Completion);
        Assert.Equal(1, jobs.Succeeded);
        Assert.Equal(2, jobs.Snapshot.Single().Attempt);
        File.Delete(path);
    }

    [Fact]
    public void QueuedCancellationSettlesAndTerminalEventsArePreserved()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "job-tests", Guid.NewGuid() + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', 4_000_000));
        using var graph = new Graph(); using var active = graph.Activate();
        using var jobs = new FileJobMonitor(graph, 1);
        int[] ids = jobs.Queue([path, path, path]);
        jobs.Cancel(ids[2]);
        GraphTasks.Complete(graph, jobs.Completion);
        Assert.Equal(1, jobs.Cancelled);
        Assert.Equal(3, jobs.TerminalEvents.Count);
        Assert.Equal(3, jobs.TerminalEvents.Select(e => e.Id).Distinct().Count());
        File.Delete(path);
    }

    [Fact]
    public void DisposingStopsPublicationsAndCancelsOutstandingWork()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "job-tests", Guid.NewGuid() + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', 2_000_000));
        using var graph = new Graph(); using var active = graph.Activate();
        var jobs = new FileJobMonitor(graph, 1);
        jobs.Queue([path, path]);
        var completion = jobs.Completion;
        jobs.Dispose();
        GraphTasks.Complete(graph, completion);
        Assert.True(jobs.IsDisposed);
        File.Delete(path);
    }
}
