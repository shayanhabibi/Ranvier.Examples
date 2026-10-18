using System.Globalization;
using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;

namespace Ranvier.Examples.CSharp;

/// <summary>A field and its current validation message.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>A submitted order whose amounts remain fixed after later edits.</summary>
public sealed record OrderConfirmation(int Quantity, decimal UnitPrice, decimal DiscountPercent,
    bool Delivery, string Address, decimal Total);

/// <summary>Editable order pricing and validation used on the supplied graph's thread.</summary>
public sealed class OrderModel : IDisposable
{
    private readonly Graph graph;
    private readonly Owner owner;

    /// <summary>Creates a nested scope; the caller retains ownership of the graph.</summary>
    public OrderModel(Graph graph)
    {
        this.graph = graph;
        using var active = graph.Activate();
        owner = Root(scope =>
        {
            Quantity = Signal(2);
            UnitPrice = Signal(25m);
            DiscountPercent = Signal(0m);
            Delivery = Signal(false);
            Address = Signal("");
            Confirmation = Signal<OrderConfirmation?>(null);
            AddressError = Memo<string?>(() => Delivery.Value && string.IsNullOrWhiteSpace(Address.Value)
                ? "Enter a delivery address." : null);
            Errors = Memo<IReadOnlyList<FieldError>>(() =>
            {
                var errors = new List<FieldError>();
                if (Quantity.Value < 1 || Quantity.Value > 1000)
                    errors.Add(new("Quantity", "Quantity must be between 1 and 1000."));
                if (UnitPrice.Value < 0.01m || UnitPrice.Value > 10000m)
                    errors.Add(new("UnitPrice", "Unit price must be between 0.01 and 10000."));
                if (DiscountPercent.Value < 0m || DiscountPercent.Value > 50m)
                    errors.Add(new("DiscountPercent", "Discount must be between 0 and 50 percent."));
                if (AddressError.Value is { } error) errors.Add(new("Address", error));
                return errors.AsReadOnly();
            });
            Subtotal = Memo(() => Quantity.Value is >= 1 and <= 1000 && UnitPrice.Value is >= 0.01m and <= 10000m
                ? Quantity.Value * UnitPrice.Value : 0m);
            Discount = Memo(() => DiscountPercent.Value is >= 0m and <= 50m
                ? decimal.Round(Subtotal.Value * DiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero) : 0m);
            DeliveryCost = Memo(() => Delivery.Value && Subtotal.Value - Discount.Value < 100m ? 7.50m : 0m);
            CanSubmit = Memo(() => Errors.Value.Count == 0);
            Total = Memo<decimal?>(() => CanSubmit.Value ? Subtotal.Value - Discount.Value + DeliveryCost.Value : null);
            Summary = Memo(() =>
            {
                string amount = Total.Value?.ToString("F2", CultureInfo.InvariantCulture) ?? "unavailable";
                return FormattableString.Invariant($"Quantity {Quantity.Value}; unit price {UnitPrice.Value:F2}; discount {DiscountPercent.Value}%; total {amount}. {string.Join(" ", Errors.Value.Select(e => e.Message))}").Trim();
            });
            return scope;
        });
    }

    /// <summary>Quantity accepted from 1 through 1000.</summary>
    public Signal<int> Quantity { get; private set; } = null!;
    /// <summary>Unit price accepted from 0.01 through 10000.</summary>
    public Signal<decimal> UnitPrice { get; private set; } = null!;
    /// <summary>Discount accepted from 0 through 50 percent.</summary>
    public Signal<decimal> DiscountPercent { get; private set; } = null!;
    /// <summary>Selects delivery rather than pickup.</summary>
    public Signal<bool> Delivery { get; private set; } = null!;
    /// <summary>Address required when delivery is selected.</summary>
    public Signal<string> Address { get; private set; } = null!;
    /// <summary>Validation that reads the address only when delivery is selected.</summary>
    public Memo<string?> AddressError { get; private set; } = null!;
    /// <summary>Current field validation messages.</summary>
    public Memo<IReadOnlyList<FieldError>> Errors { get; private set; } = null!;
    /// <summary>Quantity multiplied by unit price, or zero when either numeric input is invalid.</summary>
    public Memo<decimal> Subtotal { get; private set; } = null!;
    /// <summary>Discount rounded away from zero to two places, or zero when its numeric inputs are invalid.</summary>
    public Memo<decimal> Discount { get; private set; } = null!;
    /// <summary>Delivery charge before validation; discounted subtotals of at least 100 ship free.</summary>
    public Memo<decimal> DeliveryCost { get; private set; } = null!;
    /// <summary>Payable total, or null when an input is invalid.</summary>
    public Memo<decimal?> Total { get; private set; } = null!;
    /// <summary>Whether all input fields satisfy validation.</summary>
    public Memo<bool> CanSubmit { get; private set; } = null!;
    /// <summary>Reactive pricing summary and validation messages.</summary>
    public Memo<string> Summary { get; private set; } = null!;
    /// <summary>Last submitted snapshot, cleared by Reset.</summary>
    public Signal<OrderConfirmation?> Confirmation { get; private set; } = null!;

    /// <summary>Restores defaults and clears the confirmation in one batch.</summary>
    public void Reset()
    {
        using var active = graph.Activate();
        Batch(() =>
        {
            Quantity.Value = 2; UnitPrice.Value = 25m; DiscountPercent.Value = 0m;
            Delivery.Value = false; Address.Value = ""; Confirmation.Value = null;
        });
    }

    /// <summary>Captures a valid snapshot, or returns false while retaining the previous confirmation.</summary>
    public bool Submit()
    {
        using var active = graph.Activate();
        if (Total.Value is not { } total) return false;
        Confirmation.Value = new(Quantity.Value, UnitPrice.Value, DiscountPercent.Value,
            Delivery.Value, Delivery.Value ? Address.Value : "", total);
        return true;
    }

    /// <summary>Delivers the current summary and settled changes until the subscription or model is disposed.</summary>
    public IDisposable ObserveSummary(Action<string> callback)
    {
        ObjectDisposedException.ThrowIf(owner.IsDisposed, this);
        using var active = graph.Activate();
        var observer = Root(child =>
        {
            Effect(() => callback(Summary.Value));
            return child;
        });
        owner.Attach(observer);
        return observer;
    }

    /// <summary>Releases the model scope and observers; repeated calls are safe.</summary>
    public void Dispose() => owner.Dispose();
}

