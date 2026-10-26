namespace Ranvier.Examples.ShopReplay

open System.Text.Json
open System.Threading.Tasks
open Ranvier
open Ranvier.Persistence

/// <summary>A cart line priced in Australian cents.</summary>
type Line =
    { Sku: string
      Quantity: int
      UnitPrice: int }

/// <summary>The persisted cart; each quote request uses a new revision.</summary>
type Cart =
    { Revision: int
      Lines: Line array
      Coupon: string
      Country: string }

/// <summary>A shipping and stock response for a cart revision; shipping is in cents.</summary>
type Quote =
    { Revision: int
      Shipping: int
      Stock: int }

/// <summary>The customer-visible checkout state, with monetary amounts in cents.</summary>
type Checkout =
    { Revision: int
      QuoteRevision: int
      Phase: string
      Subtotal: int
      Discount: int
      Shipping: int
      Total: int }

/// <summary>The shop graph and its observed checkout transitions.</summary>
type Shop =
    { Cart: Signal<Cart>
      Quote: AsyncSource<Quote>
      Checkout: Memo<Checkout>
      Observations: ResizeArray<Checkout>
      Receipt: Effect
      Flights: ResizeArray<Task> }

/// <summary>Builds the same shop in capture and diagnostic replay.</summary>
module Shop =
    /// <summary>A versioned JSON codec for the sample's public record types.</summary>
    let jsonCodec<'T> name : JournalCodec<'T> =
        { Name = name
          Version = 1
          Encode = fun value -> JsonSerializer.Serialize value
          Decode =
            fun text ->
                try
                    let value = JsonSerializer.Deserialize<'T> text

                    if isNull (box value) then
                        Error "Expected a JSON value"
                    else
                        Ok value
                with e ->
                    Error e.Message }

    /// <summary>The cart journal encoding.</summary>
    let cartCodec = jsonCodec<Cart> "shop.cart"
    /// <summary>The shipping response journal encoding.</summary>
    let quoteCodec = jsonCodec<Quote> "shop.quote"
    /// <summary>The cart's store key and codec.</summary>
    let cartKey = Persist.key "cart" (PersistCodec.ofJournal cartCodec)
    /// <summary>The shipping source's store key and codec.</summary>
    let quoteKey = Persist.sourceKey "shipping" (PersistCodec.ofJournal quoteCodec)
    /// <summary>Every persisted input used by the shop graph.</summary>
    let keys: IPersistKey list = [ cartKey; quoteKey ]

    /// <summary>The fallback for a new session.</summary>
    let emptyCart =
        { Revision = 0
          Lines = [||]
          Coupon = ""
          Country = "AU" }

    /// <summary>The saved cart before either shipping request starts.</summary>
    let baseline =
        { emptyCart with
            Lines =
                [| { Sku = "tea"
                     Quantity = 1
                     UnitPrice = 2500 } |]
            Coupon = "SAVE10" }

    /// <summary>Creates persisted inputs, the async gateway, pricing memos and a receipt observer.</summary>
    /// <remarks>Call on the graph thread. Diagnostic replay supplies shipping results and skips the gateway.</remarks>
    let build (graph: Graph) (session: PersistSession) (request: Cart -> Task<Quote>) =
        use active = graph.Activate ()

        let cart =
            Persist.signal session cartKey emptyCart
            |> Journal.key "cart" cartCodec

        let quote =
            Persist.source session quoteKey
            |> Journal.keySource "shipping" quoteCodec

        let flights = ResizeArray<Task> ()

        Trace.named "request-shipping" (fun () ->
            createEffect (fun () ->
                let current = cart.Value

                if current.Revision > 0 then
                    let work = Journal.work graph
                    Journal.name "shipping" work

                    if
                        Journal.replaying graph
                        <> Some ReplayMode.Diagnostic
                    then
                        let flight =
                            task {
                                let! outcome =
                                    task {
                                        try
                                            let! result = request current
                                            return Ok result
                                        with e ->
                                            return Error e
                                    }
                                // Deliberate bug: a late response replaces a newer revision's quote.
                                Journal.produce work (fun () ->
                                    match outcome with
                                    | Ok result -> quote.Settle result
                                    | Error e -> quote.Fail e)
                            }

                        flights.Add flight))

        let subtotal =
            Trace.named "subtotal" (fun () ->
                createMemo (fun _ ->
                    cart.Value.Lines
                    |> Array.sumBy (fun line -> line.Quantity * line.UnitPrice)))

        let discount =
            Trace.named "discount" (fun () ->
                createMemo (fun _ ->
                    if cart.Value.Coupon = "SAVE10" then
                        subtotal.Value / 10
                    else
                        0))

        let checkout =
            Trace.named "checkout" (fun () ->
                createMemo (fun _ ->
                    let current = cart.Value

                    let result phase revision shipping =
                        { Revision = current.Revision
                          QuoteRevision = revision
                          Phase = phase
                          Subtotal = subtotal.Value
                          Discount = discount.Value
                          Shipping = shipping
                          Total = subtotal.Value - discount.Value + shipping }

                    match quote.TryValue with
                    | Reading.Pending -> result "pending" -1 0
                    | Reading.Failed e -> result ("failed:" + e.Message) -1 0
                    | Reading.Ready response ->
                        let quantity = current.Lines |> Array.sumBy _.Quantity

                        result
                            (if response.Stock < quantity then
                                 "out-of-stock"
                             else
                                 "ready")
                            response.Revision
                            response.Shipping))

        let observations = ResizeArray<Checkout> ()

        let receipt =
            Trace.named "receipt" (fun () -> new Effect (graph, fun () -> observations.Add checkout.Value))

        { Cart = cart
          Quote = quote
          Checkout = checkout
          Observations = observations
          Receipt = receipt
          Flights = flights }
