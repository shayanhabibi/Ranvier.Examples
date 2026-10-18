namespace Ranvier.Examples.FSharp.Desktop

open System
open System.Globalization
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Ranvier
open Ranvier.Examples.FSharp

/// <summary>Code-created order controls connected through effects and disposable event subscriptions.</summary>
type OrderWindow() as this =
    inherit Window()
    let graph = new Graph ()
    let order = new OrderModel (graph)
    let subscriptions = ResizeArray<IDisposable> ()

    let money (value: decimal) =
        value.ToString ("F2", CultureInfo.InvariantCulture)

    let text (value: string) size (color: string) =
        TextBlock (Text = value, FontSize = size, Foreground = SolidColorBrush (Color.Parse (color)), TextWrapping = TextWrapping.Wrap)

    let quantity =
        NumericUpDown (Name = "QuantityInput", Value = Nullable 2m, FormatString = "0", Increment = 1m)

    let price =
        NumericUpDown (Name = "PriceInput", Value = Nullable 25m, FormatString = "0.00", Increment = 1m)

    let discount =
        NumericUpDown (Name = "DiscountInput", Value = Nullable 0m, FormatString = "0.##", Increment = 1m)

    let delivery =
        CheckBox (Name = "DeliveryInput", Content = "Deliver this order (otherwise pickup)")

    let address = TextBox (Name = "AddressInput", PlaceholderText = "Delivery address")
    let errors = text "" 14.0 "#B42318"
    let total = text "" 38.0 "#FFFFFF"
    let breakdown = text "" 14.0 "#D1E2E4"
    let confirmation = text "" 14.0 "#122D35"

    let submit =
        Button (Name = "SubmitButton", Content = "Submit order", Padding = Thickness (22.0, 12.0))

    let reset =
        Button (Name = "ResetButton", Content = "Reset inputs", Padding = Thickness (22.0, 12.0))

    let mutable painting = false

    let panel spacing =
        StackPanel (Spacing = spacing)

    let numericValue (control: NumericUpDown) fallback =
        if control.Value.HasValue then
            control.Value.Value
        else
            fallback

    let write action =
        if not painting then
            graph.Run (action)

    do
        this.Title <- "Ranvier · F# order editor"
        this.Width <- 760.0
        this.Height <- 800.0
        this.MinWidth <- 620.0
        this.MinHeight <- 650.0
        this.Background <- SolidColorBrush (Color.Parse "#F3F5F7")
        errors.Name <- "ErrorsText"
        total.Name <- "TotalText"
        confirmation.Name <- "ConfirmationText"

        let body = panel 18.0
        body.Margin <- Thickness 36.0
        body.Children.Add (text "RANVIER EXAMPLES / F#" 12.0 "#536274")
        let heading = text "Build an order." 34.0 "#122D35"
        heading.FontWeight <- FontWeight.Bold
        body.Children.Add heading
        body.Children.Add (text "Edit the inputs. Ranvier keeps prices, validation and controls in sync." 14.0 "#536274")
        let inputs = panel 12.0

        let amounts =
            Grid (ColumnDefinitions = ColumnDefinitions ("*,*"), ColumnSpacing = 18.0)

        for index, label, control in [ 0, "Quantity · 1–1000", quantity; 1, "Unit price · 0.01–10000", price ] do
            let column = panel 6.0
            column.Children.Add (text label 14.0 "#122D35")
            column.Children.Add control
            Grid.SetColumn (column, index)
            amounts.Children.Add column

        inputs.Children.Add amounts
        inputs.Children.Add (text "Discount · 0–50%" 14.0 "#122D35")
        inputs.Children.Add discount

        inputs.Children.Add delivery
        inputs.Children.Add address
        inputs.Children.Add errors
        body.Children.Add (Border (Background = Brushes.White, CornerRadius = CornerRadius 12.0, Padding = Thickness 24.0, Child = inputs))
        let pricing = panel 10.0
        pricing.Children.Add (text "PAYABLE TOTAL · DEMO CURRENCY" 12.0 "#ADC8CB")
        pricing.Children.Add total
        pricing.Children.Add breakdown
        pricing.Children.Add (text "Delivery 7.50 · free from 100 after discount" 12.0 "#ADC8CB")

        body.Children.Add (
            Border (Background = SolidColorBrush (Color.Parse "#122D35"), CornerRadius = CornerRadius 12.0, Padding = Thickness 24.0, Child = pricing)
        )

        let actions = panel 12.0
        actions.Orientation <- Orientation.Horizontal
        actions.Children.Add submit
        actions.Children.Add reset
        body.Children.Add actions
        body.Children.Add confirmation
        body.Children.Add (text "In-memory demonstration · no payment is taken. Reset applies all defaults in one batch." 12.0 "#536274")
        this.Content <- ScrollViewer (Content = body)

        for control in [ quantity; price; discount ] do
            let handler =
                EventHandler<AvaloniaPropertyChangedEventArgs> (fun _ change ->
                    if
                        change.Property = NumericUpDown.TextProperty
                        && not painting
                    then
                        let mutable parsed = 0m

                        if not (Decimal.TryParse (control.Text, NumberStyles.Any, CultureInfo.CurrentCulture, &parsed)) then
                            control.Value <- Nullable ())

            control.PropertyChanged.AddHandler handler

            subscriptions.Add
                { new IDisposable with
                    member _.Dispose() =
                        control.PropertyChanged.RemoveHandler handler }

        subscriptions.Add (
            quantity.ValueChanged.Subscribe (fun _ ->
                write (fun () ->
                    let value = numericValue quantity 0m

                    order.Quantity.Value <-
                        if
                            value >= 1m
                            && value <= 1000m
                            && Decimal.Truncate value = value
                        then
                            int value
                        else
                            0))
        )

        subscriptions.Add (price.ValueChanged.Subscribe (fun _ -> write (fun () -> order.UnitPrice.Value <- numericValue price 0m)))
        subscriptions.Add (discount.ValueChanged.Subscribe (fun _ -> write (fun () -> order.DiscountPercent.Value <- numericValue discount -1m)))

        subscriptions.Add (
            delivery.IsCheckedChanged.Subscribe (fun _ -> write (fun () -> order.Delivery.Value <- delivery.IsChecked.GetValueOrDefault ()))
        )

        subscriptions.Add (
            address.TextChanged.Subscribe (fun _ -> write (fun () -> order.Address.Value <- if isNull address.Text then "" else address.Text))
        )

        subscriptions.Add (submit.Click.Subscribe (fun _ -> write (fun () -> order.Submit () |> ignore)))
        subscriptions.Add (reset.Click.Subscribe (fun _ -> write order.Reset))

        graph.Run (fun () ->
            createEffect (fun () ->
                painting <- true

                try
                    quantity.Value <- Nullable (decimal order.Quantity.Value)
                    price.Value <- Nullable order.UnitPrice.Value
                    discount.Value <- Nullable order.DiscountPercent.Value
                    delivery.IsChecked <- Nullable order.Delivery.Value
                    address.IsEnabled <- order.Delivery.Value
                    address.Text <- order.Address.Value

                    errors.Text <-
                        order.Errors.Value
                        |> List.map snd
                        |> String.concat "\n"

                    total.Text <-
                        order.Total.Value
                        |> Option.map money
                        |> Option.defaultValue "Unavailable"

                    breakdown.Text <-
                        sprintf
                            "Subtotal %s  −  discount %s  +  delivery %s"
                            (money order.Subtotal.Value)
                            (money order.Discount.Value)
                            (money order.DeliveryCost.Value)

                    submit.IsEnabled <- order.CanSubmit.Value

                    confirmation.Text <-
                        match order.Confirmation.Value with
                        | Some accepted -> sprintf "Submitted %d item(s) · total %s" accepted.Quantity (money accepted.Total)
                        | None -> "No order submitted"
                finally
                    painting <- false))

        this.Closed.Add (fun _ ->
            for subscription in subscriptions do
                subscription.Dispose ()

            order.Dispose ()
            graph.Dispose ())
