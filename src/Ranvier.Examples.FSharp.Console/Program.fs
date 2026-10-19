module Ranvier.Examples.FSharp.Console.Program

open System
open System.Globalization
open Ranvier
open Ranvier.Examples.FSharp

let intro () =
    CultureInfo.CurrentCulture <- CultureInfo.InvariantCulture
    use graph = new Graph ()
    use active = graph.Activate ()
    use order = new OrderModel (graph)
    Console.WriteLine ("1. Initial order: two items at 25.00, pickup.")
    use subscription = order.ObserveSummary (Console.WriteLine)

    let step (label: string) action expected =
        Console.WriteLine (label)
        action ()

        if order.Total.Value <> expected then
            failwithf "Unexpected total: %A" order.Total.Value

    step "2. Set quantity to three." (fun () -> order.Quantity.Value <- 3) (Some 75m)
    step "3. Apply a ten percent discount." (fun () -> order.DiscountPercent.Value <- 10m) (Some 67.50m)
    step "4. Select delivery: an address is required." (fun () -> order.Delivery.Value <- true) None
    step "5. Correct the delivery address." (fun () -> order.Address.Value <- "42 Example Street") (Some 75m)
    Console.WriteLine ("6. Submit the valid order.")

    if not (order.Submit ()) then
        failwith "Expected a valid submission"

    Console.WriteLine (sprintf "Confirmation total %s" (order.Confirmation.Value.Value.Total.ToString ("F2")))
    step "7. Reset all inputs together." order.Reset (Some 50m)

    if order.Confirmation.Value.IsSome then
        failwith "Reset must clear the confirmation"

    Console.WriteLine ("Walkthrough passed.")
    0

[<EntryPoint>]
let main args =
    match Array.tryHead args with
    | Some "intro" -> intro ()
    | Some "data" ->
        Workflows.data ()
        0
    | Some "elmish" ->
        Workflows.elmish ()
        0
    | _ ->
        Workflows.support ()
        0
