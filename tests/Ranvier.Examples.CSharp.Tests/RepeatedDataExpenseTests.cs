using Ranvier;
using Ranvier.Examples.FSharp;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class RepeatedDataExpenseTests
{
    [Theory]
    [InlineData("F")]
    [InlineData("K")]
    public void ValidMeasurementCanConvertAndBackAtAcceptedUpperLimit(string target)
    {
        string input = Path.GetTempFileName();
        try
        {
            File.WriteAllText(input, "id,station,value,unit,included\n1,North,1000000,C,true\n");
            using var graph = new Graph();
            using var active = graph.Activate();
            using var model = new DataQualityModel(graph, (station, _) => Task.FromResult(station));
            GraphTasks.Complete(graph, model.LoadAsync(input));
            Assert.True(model.GetRow(1).IsValid);
            model.ApplyUnitToIncluded(target);
            Assert.True(model.GetRow(1).IsValid, model.GetRow(1).Error);
            Assert.Equal(1000000m, model.MeanCelsius);
            model.ApplyUnitToIncluded("C");
            Assert.Equal("C", model.GetRow(1).Unit);
            Assert.Equal(1000000m, model.MeanCelsius);
        }
        finally { File.Delete(input); }
    }

}
