using System.Globalization;
using Ranvier;
using Ranvier.CSharp;

namespace Ranvier.Examples.CSharp.Desktop;

/// <summary>Reactive XAML properties and commands over a separately owned order model.</summary>
public sealed class OrderViewModel : ReactiveObject
{
    private readonly BoundSignal<int> quantity;
    private readonly BoundSignal<decimal> price;
    private readonly BoundSignal<decimal> discount;
    private readonly BoundSignal<bool> delivery;
    private readonly BoundSignal<string> address;
    private readonly BoundValue<string> total;
    private readonly BoundValue<string> breakdown;
    private readonly BoundValue<string> errors;
    private readonly BoundValue<string> confirmation;

    public OrderViewModel(Graph graph, OrderModel order) : base(graph)
    {
        quantity = Bindings.Writable(nameof(Quantity), order.Quantity);
        price = Bindings.Writable(nameof(UnitPrice), order.UnitPrice);
        discount = Bindings.Writable(nameof(DiscountPercent), order.DiscountPercent);
        delivery = Bindings.Writable(nameof(Delivery), order.Delivery);
        address = Bindings.Writable(nameof(Address), order.Address);
        total = Bindings.Computed(nameof(TotalText), () => order.Total.Value is { } amount ? Money(amount) : "Unavailable");
        breakdown = Bindings.Computed(nameof(Breakdown), () =>
            $"Subtotal {Money(order.Subtotal.Value)}  −  discount {Money(order.Discount.Value)}  +  delivery {Money(order.DeliveryCost.Value)}");
        errors = Bindings.Computed(nameof(Errors), () => string.Join("\n", order.Errors.Value.Select(e => e.Message)));
        confirmation = Bindings.Computed(nameof(Confirmation), () =>
            order.Confirmation.Value is { } accepted ? $"Submitted {accepted.Quantity} item(s) · total {Money(accepted.Total)}" : "No order submitted");
        Reset = Bindings.Command(_ => order.Reset());
        Submit = Bindings.Command(_ => { order.Submit(); }, () => order.CanSubmit.Value);
    }

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    public decimal? Quantity
    {
        get => quantity.Value;
        set => quantity.Value = value is >= 1 and <= 1000 && decimal.Truncate(value.Value) == value.Value ? (int)value.Value : 0;
    }
    public decimal? UnitPrice { get => price.Value; set => price.Value = value ?? 0m; }
    public decimal? DiscountPercent { get => discount.Value; set => discount.Value = value ?? -1m; }
    public bool Delivery { get => delivery.Value; set => delivery.Value = value; }
    public string Address { get => address.Value; set => address.Value = value ?? ""; }
    public string TotalText => total.Value;
    public string Breakdown => breakdown.Value;
    public string Errors => errors.Value;
    public string Confirmation => confirmation.Value;
    public ReactiveCommand Reset { get; }
    public ReactiveCommand Submit { get; }
}
