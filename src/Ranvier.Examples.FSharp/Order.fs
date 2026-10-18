namespace Ranvier.Examples.FSharp

open System
open System.Globalization
open Ranvier

/// <summary>A submitted order whose amounts remain fixed after later edits.</summary>
type OrderConfirmation =
    { Quantity: int
      UnitPrice: decimal
      DiscountPercent: decimal
      Delivery: bool
      Address: string
      Total: decimal }

/// <summary>Editable order pricing and validation owned by a nested reactive scope.</summary>
/// <param name="graph">An existing graph used on its owning thread; the caller retains its lifetime.</param>
type OrderModel(graph: Graph) =
    let (owner,
         quantity,
         unitPrice,
         percent,
         delivery,
         address,
         addressError,
         errors,
         subtotal,
         discount,
         deliveryCost,
         total,
         canSubmit,
         summary,
         confirmation) =
        graph.Run (fun () ->
            createRoot (fun owner ->
                let quantity = createSignal 2
                let price = createSignal 25m
                let percent = createSignal 0m
                let delivery = createSignal false
                let address = createSignal ""
                let confirmation: Signal<OrderConfirmation option> = createSignal None

                let addressError =
                    createMemo (fun _ ->
                        if
                            delivery.Value
                            && String.IsNullOrWhiteSpace address.Value
                        then
                            Some "Enter a delivery address."
                        else
                            None)

                let errors =
                    createMemo (fun _ ->
                        [ if quantity.Value < 1 || quantity.Value > 1000 then
                              "Quantity", "Quantity must be between 1 and 1000."
                          if price.Value < 0.01m || price.Value > 10000m then
                              "UnitPrice", "Unit price must be between 0.01 and 10000."
                          if percent.Value < 0m || percent.Value > 50m then
                              "DiscountPercent", "Discount must be between 0 and 50 percent."
                          match addressError.Value with
                          | Some message -> "Address", message
                          | None -> () ])

                let subtotal =
                    createMemo (fun _ ->
                        let q, p = quantity.Value, price.Value

                        if q >= 1 && q <= 1000 && p >= 0.01m && p <= 10000m then
                            decimal q * p
                        else
                            0m)

                let discount =
                    createMemo (fun _ ->
                        let d = percent.Value

                        if d >= 0m && d <= 50m then
                            Decimal.Round (subtotal.Value * d / 100m, 2, MidpointRounding.AwayFromZero)
                        else
                            0m)

                let deliveryCost =
                    createMemo (fun _ ->
                        if
                            delivery.Value
                            && subtotal.Value - discount.Value < 100m
                        then
                            7.50m
                        else
                            0m)

                let canSubmit = createMemo (fun _ -> List.isEmpty errors.Value)

                let total =
                    createMemo (fun _ ->
                        if canSubmit.Value then
                            Some (
                                subtotal.Value - discount.Value
                                + deliveryCost.Value
                            )
                        else
                            None)

                let summary =
                    createMemo (fun _ ->
                        let amount =
                            match total.Value with
                            | Some value -> value.ToString ("F2", CultureInfo.InvariantCulture)
                            | None -> "unavailable"

                        let messages = errors.Value |> List.map snd |> String.concat " "

                        (sprintf
                            "Quantity %d; unit price %s; discount %M%%; total %s. %s"
                            quantity.Value
                            (price.Value.ToString ("F2", CultureInfo.InvariantCulture))
                            percent.Value
                            amount
                            messages)
                            .Trim ())

                owner,
                quantity,
                price,
                percent,
                delivery,
                address,
                addressError,
                errors,
                subtotal,
                discount,
                deliveryCost,
                total,
                canSubmit,
                summary,
                confirmation))

    /// <summary>Quantity accepted in the inclusive range 1 through 1000.</summary>
    member _.Quantity = quantity
    /// <summary>Unit price accepted in the inclusive range 0.01 through 10000.</summary>
    member _.UnitPrice = unitPrice
    /// <summary>Discount accepted in the inclusive range 0 through 50 percent.</summary>
    member _.DiscountPercent = percent
    /// <summary>Selects delivery rather than pickup.</summary>
    member _.Delivery = delivery
    /// <summary>Address required when delivery is selected.</summary>
    member _.Address = address
    /// <summary>Address error that reads the address only when delivery is selected.</summary>
    member _.AddressError = addressError
    /// <summary>Field names and validation messages for the current inputs.</summary>
    member _.Errors = errors
    /// <summary>Quantity multiplied by unit price, or zero when either numeric input is invalid.</summary>
    member _.Subtotal = subtotal
    /// <summary>Discount rounded away from zero to two places, or zero when its numeric inputs are invalid.</summary>
    member _.Discount = discount
    /// <summary>Delivery charge before validation; discounted orders of at least 100 ship free.</summary>
    member _.DeliveryCost = deliveryCost
    /// <summary>Payable total, or None when an input is invalid.</summary>
    member _.Total = total
    /// <summary>Whether the current inputs satisfy every validation rule.</summary>
    member _.CanSubmit = canSubmit
    /// <summary>Reactive pricing summary and validation messages.</summary>
    member _.Summary = summary
    /// <summary>Last accepted snapshot, cleared by Reset.</summary>
    member _.Confirmation = confirmation

    /// <summary>Restores default inputs and clears the confirmation in one batch.</summary>
    member _.Reset() =
        graph.Run (fun () ->
            batch (fun () ->
                quantity.Value <- 2
                unitPrice.Value <- 25m
                percent.Value <- 0m
                delivery.Value <- false
                address.Value <- ""
                confirmation.Value <- None))

    /// <summary>Captures the current valid order; returns false and retains the confirmation for invalid inputs.</summary>
    member _.Submit() =
        graph.Run (fun () ->
            match total.Value with
            | None -> false
            | Some amount ->
                confirmation.Value <-
                    Some
                        { Quantity = quantity.Value
                          UnitPrice = unitPrice.Value
                          DiscountPercent = percent.Value
                          Delivery = delivery.Value
                          Address = if delivery.Value then address.Value else ""
                          Total = amount }

                true)

    /// <summary>Delivers the current summary and each settled change until the subscription or model is disposed.</summary>
    member _.ObserveSummary(callback: string -> unit) : IDisposable =
        if owner.IsDisposed then
            raise (ObjectDisposedException (nameof OrderModel))

        graph.Run (fun () ->
            let observer =
                createRoot (fun child ->
                    createEffect (fun () -> callback summary.Value)
                    child)

            owner.Attach observer
            observer :> IDisposable)

    /// <summary>Releases the model scope and its observers; repeated calls are safe.</summary>
    member _.Dispose() =
        owner.Dispose ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()
