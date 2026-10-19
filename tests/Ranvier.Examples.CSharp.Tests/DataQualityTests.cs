using Ranvier;
using Ranvier.Examples.FSharp;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class DataQualityTests
{
    private static string Input(string rows)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data-tests", Guid.NewGuid() + ".csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "id,station,value,unit,included\n" + rows);
        return path;
    }
    private static Task<string> Calibration(string station, CancellationToken token) => Task.FromResult(station + ": inspected 2026-10-01");

    [Fact]
    public void RepairRetainsRawTextAndUpdatesOnlyAffectedRowDerivations()
    {
        string path = Input("1,North,not-a-number,C,true\n2,South,68,F,true\n");
        using var graph = new Graph(); using var active = graph.Activate();
        using var data = new DataQualityModel(graph, Calibration);
        GraphTasks.Complete(graph, data.LoadAsync(path));
        var row = data.GetRow(1);
        var other = data.GetRow(2);
        Assert.Equal("not-a-number", row.RawText);
        Assert.Equal(1, data.InvalidCount);
        Assert.Equal(20m, data.MeanCelsius);
        int unrelated = other.EvaluationCount;
        row.RawText = "10";
        Assert.Equal(0, data.InvalidCount);
        Assert.Equal(15m, data.MeanCelsius);
        Assert.Equal(unrelated, other.EvaluationCount);
        File.Delete(path);
    }

    [Fact]
    public void BulkConversionPreservesMeaningAndPublishesCoherently()
    {
        string path = Input("1,North,10,C,true\n2,South,20,C,true\n3,West,999,C,false\n");
        using var graph = new Graph(); using var active = graph.Activate();
        using var data = new DataQualityModel(graph, Calibration);
        GraphTasks.Complete(graph, data.LoadAsync(path));
        var summaries = new List<string>();
        using var subscription = data.ObserveSummary(summaries.Add);
        int before = summaries.Count;
        data.ApplyUnitToIncluded("F");
        Assert.Equal(15m, data.MeanCelsius);
        Assert.Equal("50", data.GetRow(1).RawText);
        Assert.Equal("68", data.GetRow(2).RawText);
        Assert.Equal("C", data.GetRow(3).Unit);
        Assert.Equal(before + 1, summaries.Count);
        File.Delete(path);
    }

    [Fact]
    public void ReloadPreservesDraftAndAcceptUpstreamResetsIt()
    {
        string path = Input("1,North,10,C,true\n");
        using var graph = new Graph(); using var active = graph.Activate();
        using var data = new DataQualityModel(graph, Calibration);
        GraphTasks.Complete(graph, data.LoadAsync(path));
        data.GetRow(1).RawText = "15";
        File.WriteAllText(path, "id,station,value,unit,included\n1,North,20,C,true\n");
        GraphTasks.Complete(graph, data.ReloadAsync());
        Assert.Equal("15", data.GetRow(1).RawText);
        Assert.True(data.GetRow(1).HasConflict);
        data.AcceptUpstream(1);
        Assert.Equal("20", data.GetRow(1).RawText);
        Assert.False(data.GetRow(1).HasConflict);
        File.Delete(path);
    }

    [Fact]
    public void InvalidExportIsRejectedThenRepairedExportRoundTripsQuotedStations()
    {
        string path = Input("1,\"North, annex\",bad,C,true\n");
        string output = path + ".out.csv";
        using var graph = new Graph(); using var active = graph.Activate();
        using var data = new DataQualityModel(graph, Calibration);
        GraphTasks.Complete(graph, data.LoadAsync(path));
        Assert.Throws<InvalidOperationException>(() => { GraphTasks.Complete(graph, data.ExportAsync(output)); });
        Assert.False(File.Exists(output));
        data.GetRow(1).RawText = "12.5";
        GraphTasks.Complete(graph, data.ExportAsync(output));
        using var reread = new DataQualityModel(graph, Calibration);
        GraphTasks.Complete(graph, reread.LoadAsync(output));
        Assert.Equal("North, annex", reread.GetRow(1).Station);
        Assert.Equal(12.5m, reread.MeanCelsius);
        File.Delete(path); File.Delete(output);
    }

    [Fact]
    public void PreviousSelectionLookupCannotReplaceCurrentCalibration()
    {
        string path = Input("1,North,10,C,true\n2,South,20,C,true\n");
        var north = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var south = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var graph = new Graph(); using var active = graph.Activate();
        using var data = new DataQualityModel(graph, (station, _) => station == "North" ? north.Task : south.Task);
        GraphTasks.Complete(graph, data.LoadAsync(path));
        data.SelectedId = 1;
        _ = data.Calibration;
        data.SelectedId = 2;
        _ = data.Calibration;
        south.SetResult("South certificate");
        GraphTasks.Complete(graph, south.Task);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!data.Calibration.Contains("South certificate") && DateTime.UtcNow < deadline) { graph.Pump(); Thread.Sleep(1); }
        north.SetResult("Obsolete North certificate");
        graph.Pump();
        Assert.Contains("South certificate", data.Calibration);
        File.Delete(path);
    }
}
