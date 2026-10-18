using Ranvier;
using Ranvier.Examples.CSharp;
using Xunit;
namespace Ranvier.Examples.CSharp.Tests;

public sealed class OrderModelTests
{
    [Fact]
    public void InvalidExtremeNumbersKeepBreakdownReadable() => WithOrder((_, o) =>
    {
        o.UnitPrice.Value = decimal.MaxValue;
        Assert.Null(o.Total.Value);
        Assert.Equal(0m, o.Subtotal.Value);
        Assert.Equal(0m, o.Discount.Value);
        o.Reset();
        o.DiscountPercent.Value = decimal.MaxValue;
        Assert.Null(o.Total.Value);
        Assert.Equal(50m, o.Subtotal.Value);
        Assert.Equal(0m, o.Discount.Value);
    });
    private static void WithOrder(Action<Graph, OrderModel> check)
    {
        using var graph = new Graph();
        using var active = graph.Activate();
        using var order = new OrderModel(graph);
        check(graph, order);
    }

    [Fact]
    public void DefaultOrderCosts50() => WithOrder((_, o) =>
    {
        Assert.Equal(50m, o.Total.Value);
        Assert.True(o.CanSubmit.Value);
    });

    [Theory]
    [InlineData("Quantity", "0")]
    [InlineData("Quantity", "1001")]
    [InlineData("UnitPrice", "0")]
    [InlineData("UnitPrice", "10000.01")]
    [InlineData("DiscountPercent", "-1")]
    [InlineData("DiscountPercent", "50.01")]
    public void InvalidFieldsPreventSubmission(string field, string text) => WithOrder((_, o) =>
    {
        decimal value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        if (field == "Quantity") o.Quantity.Value = (int)value;
        if (field == "UnitPrice") o.UnitPrice.Value = value;
        if (field == "DiscountPercent") o.DiscountPercent.Value = value;
        Assert.Contains(o.Errors.Value, e => e.Field == field);
        Assert.Null(o.Total.Value);
        Assert.False(o.CanSubmit.Value);
        Assert.False(o.Submit());
    });

    [Theory]
    [InlineData(1, "0.01", "0")]
    [InlineData(1000, "10000", "50")]
    public void InclusiveLimitsAreValid(int quantity, string price, string discount) => WithOrder((_, o) =>
    {
        o.Quantity.Value = quantity;
        o.UnitPrice.Value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        o.DiscountPercent.Value = decimal.Parse(discount, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(o.CanSubmit.Value);
    });

    [Theory]
    [InlineData("0.05", "10", false, "0.04")]
    [InlineData("99.99", "0", true, "107.49")]
    [InlineData("100", "0", true, "100")]
    [InlineData("100", "10", true, "97.50")]
    public void RoundingAndDeliveryThreshold(string price, string discount, bool delivery, string expected) => WithOrder((_, o) =>
    {
        o.Quantity.Value = 1;
        o.UnitPrice.Value = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        o.DiscountPercent.Value = decimal.Parse(discount, System.Globalization.CultureInfo.InvariantCulture);
        o.Delivery.Value = delivery;
        o.Address.Value = "42 Example Street";
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), o.Total.Value);
    });

    [Fact]
    public void AddressValidationTracksDeliveryBranch() => WithOrder((_, o) =>
    {
        int calls = 0;
        using var observer = Ranvier.CSharp.Reactive.Root(owner =>
        {
            Ranvier.CSharp.Reactive.Effect(() => { var ignored = o.AddressError.Value; calls++; });
            return owner;
        });
        o.Address.Value = " ";
        Assert.Equal(1, calls);
        o.Delivery.Value = true;
        Assert.Equal(2, calls);
        Assert.Null(o.Total.Value);
        o.Address.Value = "42 Example Street";
        Assert.True(o.CanSubmit.Value);
        o.Address.Value = "";
        o.Delivery.Value = false;
        Assert.True(o.CanSubmit.Value);
    });

    [Fact]
    public void ResetPublishesOneCoherentSummary() => WithOrder((_, o) =>
    {
        o.Quantity.Value = 3; o.DiscountPercent.Value = 10;
        o.Delivery.Value = true; o.Address.Value = "42 Example Street";
        var seen = new List<string>();
        using var subscription = o.ObserveSummary(seen.Add);
        int before = seen.Count;
        o.Reset();
        Assert.Equal(before + 1, seen.Count);
        Assert.Equal(50m, o.Total.Value);
        Assert.Equal(o.Summary.Value, seen[^1]);
    });

    [Fact]
    public void SubmissionCapturesSnapshotAndInvalidSubmissionPreservesIt() => WithOrder((_, o) =>
    {
        Assert.True(o.Submit());
        var accepted = o.Confirmation.Value;
        o.Quantity.Value = 3;
        Assert.Equal(2, accepted!.Quantity);
        Assert.Equal(50m, accepted.Total);
        o.Quantity.Value = 0;
        Assert.False(o.Submit());
        Assert.Equal(accepted, o.Confirmation.Value);
        o.Reset();
        Assert.Null(o.Confirmation.Value);
    });

    [Fact]
    public void DisposingObserversAndModelLeavesGraphUsable() => WithOrder((_, o) =>
    {
        var unrelated = Ranvier.CSharp.Reactive.Signal(1);
        int calls = 0;
        var subscription = o.ObserveSummary(_ => calls++);
        subscription.Dispose();
        o.Quantity.Value = 3;
        Assert.Equal(1, calls);
        using var live = o.ObserveSummary(_ => calls++);
        o.Dispose(); o.Dispose();
        unrelated.Value = 2;
        Assert.Equal(2, unrelated.Value);
        Assert.Equal(2, calls);
    });
}

