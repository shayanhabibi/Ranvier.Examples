using System.Globalization;
using Ranvier;
using Ranvier.Examples.CSharp;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
using var graph = new Graph();
using var active = graph.Activate();
using var order = new OrderModel(graph);
Console.WriteLine("1. Initial order: two items at 25.00, pickup.");
using var subscription = order.ObserveSummary(Console.WriteLine);
void Step(string label, Action action, decimal? expected)
{
    Console.WriteLine(label);
    action();
    if (order.Total.Value != expected) throw new InvalidOperationException($"Unexpected total: {order.Total.Value}");
}
Step("2. Set quantity to three.", () => order.Quantity.Value = 3, 75m);
Step("3. Apply a ten percent discount.", () => order.DiscountPercent.Value = 10m, 67.50m);
Step("4. Select delivery: an address is required.", () => order.Delivery.Value = true, null);
Step("5. Correct the delivery address.", () => order.Address.Value = "42 Example Street", 75m);
Console.WriteLine("6. Submit the valid order.");
if (!order.Submit()) throw new InvalidOperationException("Expected a valid submission");
Console.WriteLine($"Confirmation total {order.Confirmation.Value!.Total:F2}");
Step("7. Reset all inputs together.", order.Reset, 50m);
if (order.Confirmation.Value is not null) throw new InvalidOperationException("Reset must clear the confirmation");
Console.WriteLine("Walkthrough passed.");
