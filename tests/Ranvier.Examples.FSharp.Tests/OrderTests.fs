module Ranvier.Examples.FSharp.Tests.OrderTests

open System
open Expecto
open Ranvier
open Ranvier.Examples.FSharp

let private withOrder f =
    use graph = new Graph ()
    use active = graph.Activate ()
    use order = new OrderModel (graph)
    f graph order

[<Tests>]
let tests =
    testList
        "F# order"
        [ testCase "default order costs 50"
          <| fun _ ->
              withOrder
              <| fun _ order ->
                  Expect.equal order.Total.Value (Some 50m) "payable total"
                  Expect.isTrue order.CanSubmit.Value "eligibility"
          testCase "field limits invalidate submission"
          <| fun _ ->
              for field, change in
                  [ "Quantity", (fun (o: OrderModel) -> o.Quantity.Value <- 0)
                    "Quantity", (fun o -> o.Quantity.Value <- 1001)
                    "UnitPrice", (fun o -> o.UnitPrice.Value <- 0m)
                    "UnitPrice", (fun o -> o.UnitPrice.Value <- 10000.01m)
                    "DiscountPercent", (fun o -> o.DiscountPercent.Value <- -1m)
                    "DiscountPercent", (fun o -> o.DiscountPercent.Value <- 50.01m) ] do
                  withOrder
                  <| fun _ order ->
                      change order

                      Expect.isTrue
                          (order.Errors.Value
                           |> List.exists (fun (name, _) -> name = field))
                          field

                      Expect.equal order.Total.Value None "invalid total"
                      Expect.isFalse order.CanSubmit.Value "invalid eligibility"
                      Expect.isFalse (order.Submit ()) "submission rejected"
          testCase "inclusive numeric limits are valid"
          <| fun _ ->
              for q, p, d in [ 1, 0.01m, 0m; 1000, 10000m, 50m ] do
                  withOrder
                  <| fun _ order ->
                      batch (fun () ->
                          order.Quantity.Value <- q
                          order.UnitPrice.Value <- p
                          order.DiscountPercent.Value <- d)

                      Expect.isTrue order.CanSubmit.Value "boundary accepted"
          testCase "rounding and delivery threshold"
          <| fun _ ->
              for price, discount, delivery, expected in
                  [ 0.05m, 10m, false, 0.04m
                    99.99m, 0m, true, 107.49m
                    100m, 0m, true, 100m
                    100m, 10m, true, 97.50m ] do
                  withOrder
                  <| fun _ order ->
                      batch (fun () ->
                          order.Quantity.Value <- 1
                          order.UnitPrice.Value <- price
                          order.DiscountPercent.Value <- discount
                          order.Delivery.Value <- delivery
                          order.Address.Value <- "42 Example Street")

                      Expect.equal order.Total.Value (Some expected) "exact money"
          testCase "address validation follows delivery branch"
          <| fun _ ->
              withOrder
              <| fun _ order ->
                  let mutable changes = 0

                  let observer =
                      createRoot (fun owner ->
                          createEffect (fun () ->
                              order.AddressError.Value |> ignore
                              changes <- changes + 1)

                          owner)

                  use _observer = observer
                  order.Address.Value <- " "
                  Expect.equal changes 1 "pickup does not depend on address"
                  order.Delivery.Value <- true
                  Expect.equal changes 2 "delivery reads current address"
                  Expect.equal order.Total.Value None "missing address prevents submission"
                  order.Address.Value <- "42 Example Street"
                  Expect.isTrue order.CanSubmit.Value "address corrected"
                  order.Address.Value <- ""
                  order.Delivery.Value <- false
                  Expect.isTrue order.CanSubmit.Value "pickup needs no address"
          testCase "reset publishes one coherent summary"
          <| fun _ ->
              withOrder
              <| fun _ order ->
                  order.Quantity.Value <- 3
                  order.DiscountPercent.Value <- 10m
                  order.Delivery.Value <- true
                  order.Address.Value <- "42 Example Street"
                  let seen = ResizeArray<string> ()
                  use _subscription = order.ObserveSummary (seen.Add)
                  let before = seen.Count
                  order.Reset ()
                  Expect.equal seen.Count (before + 1) "one notification"
                  Expect.equal order.Total.Value (Some 50m) "defaults restored"
                  Expect.equal seen.[seen.Count - 1] order.Summary.Value "final summary"
          testCase "submission is a stable snapshot and invalid submit preserves it"
          <| fun _ ->
              withOrder
              <| fun _ order ->
                  Expect.isTrue (order.Submit ()) "valid submission"
                  let accepted = order.Confirmation.Value
                  order.Quantity.Value <- 3
                  Expect.equal accepted.Value.Quantity 2 "snapshot quantity"
                  Expect.equal accepted.Value.Total 50m "snapshot total"
                  order.Quantity.Value <- 0
                  Expect.isFalse (order.Submit ()) "invalid submission"
                  Expect.equal order.Confirmation.Value accepted "preserved"
                  order.Reset ()
                  Expect.equal order.Confirmation.Value None "reset clears confirmation"
          testCase "subscription and model disposal preserve unrelated graph state"
          <| fun _ ->
              withOrder
              <| fun graph order ->
                  let unrelated = createSignal 1
                  let mutable calls = 0
                  let subscription = order.ObserveSummary (fun _ -> calls <- calls + 1)
                  subscription.Dispose ()
                  order.Quantity.Value <- 3
                  Expect.equal calls 1 "disposed observer silent"
                  let live = order.ObserveSummary (fun _ -> calls <- calls + 1)
                  order.Dispose ()
                  order.Dispose ()
                  unrelated.Value <- 2
                  Expect.equal unrelated.Value 2 "graph survives"
                  Expect.equal calls 2 "model observers stopped"
                  live.Dispose () ]
