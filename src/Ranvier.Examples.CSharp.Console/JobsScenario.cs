using Ranvier;
using Ranvier.Examples.CSharp.Jobs;

internal static class JobsScenario
{
    internal static void Run()
    {
        using var graph = new Graph();
        using var active = graph.Activate();
        using var monitor = new FileJobMonitor(graph, 2);
        string samples = Path.Combine(AppContext.BaseDirectory, "samples");
        monitor.Queue(Directory.GetFiles(Path.Combine(samples, "jobs")).Append(Path.Combine(samples, "measurements.csv")));
        Complete(graph, monitor.Completion);
        foreach (var row in monitor.Snapshot)
            Console.WriteLine($"{row.Id}: {row.State} {row.Bytes} bytes SHA256 {row.Digest}");
        if (monitor.Succeeded != 3 || monitor.PeakWorkers > 2) throw new InvalidOperationException("Worker invariant failed.");
        string missing = Path.Combine(Path.GetTempPath(), "ranvier-job-" + Guid.NewGuid() + ".txt");
        try
        {
            int id = monitor.Queue(new[] { missing })[0];
            Complete(graph, monitor.Completion);
            if (monitor.Failed != 1) throw new InvalidOperationException("Expected the missing-file failure.");
            File.WriteAllText(missing, "Recovered input");
            monitor.Retry(id);
            Complete(graph, monitor.Completion);
            if (monitor.Failed != 0 || monitor.Succeeded != 4) throw new InvalidOperationException("Retry failed.");
            Console.WriteLine($"Retried the same job ID; {monitor.TerminalEvents.Count} terminal events retained.");
        }
        finally { File.Delete(missing); }
    }

    private static void Complete(Graph graph, Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted)
        {
            graph.Pump();
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Thread.Sleep(1);
        }
        graph.Pump();
        task.GetAwaiter().GetResult();
    }
}
