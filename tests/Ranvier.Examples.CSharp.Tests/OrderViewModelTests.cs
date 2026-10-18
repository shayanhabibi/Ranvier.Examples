using Ranvier;
using Ranvier.Examples.CSharp.Desktop;
using Xunit;
namespace Ranvier.Examples.CSharp.Tests;

public sealed class OrderViewModelTests
{
    [Fact]
    public void PropertiesAndCommandsFollowModel()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        using var order = new OrderModel(graph); using var vm = new OrderViewModel(graph, order);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.True(vm.Submit.CanExecute(null));
        vm.Quantity = 3;
        Assert.Contains(nameof(vm.TotalText), changed);
        Assert.Contains("75.00", vm.TotalText);
        vm.Delivery = true;
        Assert.False(vm.Submit.CanExecute(null));
        vm.Address = "42 Example Street";
        Assert.True(vm.Submit.CanExecute(null));
        vm.Reset.Execute(null);
        Assert.Equal(2m, vm.Quantity);
        Assert.Contains(nameof(vm.Quantity), changed);
        Assert.Equal(50m, order.Total.Value);
    }

    [Theory]
    [InlineData("Quantity")]
    [InlineData("UnitPrice")]
    [InlineData("DiscountPercent")]
    public void ClearingNumericEditorDisablesSubmission(string field)
    {
        using var graph = new Graph(); using var active = graph.Activate();
        using var order = new OrderModel(graph); using var vm = new OrderViewModel(graph, order);
        if (field == "Quantity") vm.Quantity = null;
        if (field == "UnitPrice") vm.UnitPrice = null;
        if (field == "DiscountPercent") vm.DiscountPercent = null;
        Assert.False(vm.Submit.CanExecute(null));
        Assert.Null(order.Total.Value);
        vm.Reset.Execute(null);
        Assert.True(vm.Submit.CanExecute(null));
    }

    [Fact]
    public void FractionalQuantityIsInvalid()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        using var order = new OrderModel(graph); using var vm = new OrderViewModel(graph, order);
        vm.Quantity = 1.5m;
        Assert.False(vm.Submit.CanExecute(null));
    }

    [Fact]
    public void DisposalStopsNotificationsAndPreservesModel()
    {
        using var graph = new Graph(); using var active = graph.Activate();
        using var order = new OrderModel(graph);
        var vm = new OrderViewModel(graph, order);
        int changes = 0; vm.PropertyChanged += (_, _) => changes++;
        vm.Dispose(); order.Quantity.Value = 3;
        Assert.Equal(0, changes);
        Assert.Equal(75m, order.Total.Value);
    }
}
