namespace Ranvier.Examples.ShopReplay

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Persistence

/// <summary>Local shipping rules, with amounts in cents.</summary>
type Rates =
    { FreeShippingThreshold: int
      DomesticShipping: int
      InternationalShipping: int
      Stock: int }

/// <summary>An asynchronous file-backed gateway with gates that arrange the demonstration's response order.</summary>
type ShippingService(ratesPath: string, failFirst: bool) =
    let gates =
        Array.init 2 (fun _ -> TaskCompletionSource<unit> (TaskCreationOptions.RunContinuationsAsynchronously))

    let mutable calls = 0
    /// <summary>The number of live requests started.</summary>
    member _.Calls = Volatile.Read (&calls)

    /// <summary>Allows the selected request to complete after its file read.</summary>
    member _.Release revision =
        gates[revision - 1].SetResult ()

    /// <summary>Reads shipping rules asynchronously, then waits for the request's release gate.</summary>
    member _.Request(cart: Cart) =
        task {
            Interlocked.Increment (&calls) |> ignore
            let! json = File.ReadAllTextAsync ratesPath
            let rates = JsonSerializer.Deserialize<Rates> json
            do! gates[cart.Revision - 1].Task

            if failFirst && cart.Revision = 1 then
                invalidOp "Shipping service unavailable"

            let subtotal =
                cart.Lines
                |> Array.sumBy (fun line -> line.Quantity * line.UnitPrice)

            return
                { Revision = cart.Revision
                  Stock = rates.Stock
                  Shipping =
                    if subtotal >= rates.FreeShippingThreshold then 0
                    elif cart.Country = "AU" then rates.DomesticShipping
                    else rates.InternationalShipping }
        }

/// <summary>Records the production incident and verifies its traced diagnostic replay.</summary>
module Program =
    let private wait (task: Task) =
        task.GetAwaiter().GetResult ()

    let private result (task: Task<'T>) =
        task.GetAwaiter().GetResult ()

    let private require condition message =
        if not condition then
            invalidOp message

    let private makeGraph (session: PersistSession) journal =
        new Graph (
            { GraphOptions.Default with
                Dispatcher = Some (ManualDispatcher ())
                Journal = Some (session.Journal journal) }
        )

    let private pumpUntil (graph: Graph) completed =
        let clock = Stopwatch.StartNew ()

        while not (completed ())
              && clock.Elapsed < TimeSpan.FromSeconds 15.0 do
            graph.Pump () |> ignore

            if not (completed ()) then
                Thread.Sleep 1

        require (completed ()) "Timed out waiting for the shipping response"

    let private tracedLibrary =
        typeof<Graph>.Assembly.GetCustomAttributes<AssemblyMetadataAttribute> ()
        |> Seq.exists (fun attribute ->
            attribute.Key = "RanvierTrace"
            && attribute.Value = "true")

    let private printCheckout (state: Checkout) =
        printfn
            "cart=%d quote=%d %s subtotal=%d discount=%d shipping=%d total=%d"
            state.Revision
            state.QuoteRevision
            state.Phase
            state.Subtotal
            state.Discount
            state.Shipping
            state.Total

    let private verifyIncident failFirst (observations: Checkout array) =
        require (observations.Length = 5) "Expected checkpoint, two pending edits and two completions"

        require
            (observations[0].Revision = 0
             && observations[0].Phase = "pending")
            "Checkpoint did not restore pending"

        require
            (observations[1].Revision = 1
             && observations[1].Phase = "pending")
            "First request did not stay pending"

        require
            (observations[2].Revision = 2
             && observations[2].Phase = "pending")
            "Overlapping request did not stay pending"

        require
            (observations[3].QuoteRevision = 2
             && observations[3].Total = 6750)
            "New quote was not applied first"

        let last = observations[4]

        if failFirst then
            require (last.Phase = "failed:Shipping service unavailable") "Recorded failure was not reproduced"
        else
            require
                (last.Revision = 2
                 && last.QuoteRevision = 1
                 && last.Total = 7650)
                "Stale quote bug was not reproduced"

    let private capture directory failFirst =
        require (not tracedLibrary) "Capture must use an untraced Release build"
#if DEBUG
        invalidOp "Capture must use Release"
#endif
        require (not (Directory.Exists directory)) "Choose a new capture directory"
        Directory.CreateDirectory directory |> ignore
        let live = Path.Combine (directory, "live")
        let store = FileStateStore live
        let session = Persist.loadAsync store Shop.keys |> result
        use journal = new FileJournal (Path.Combine (directory, "journal.jsonl"))
        use graph = makeGraph session journal

        let service =
            ShippingService (Path.Combine (AppContext.BaseDirectory, "samples", "shop", "rates.json"), failFirst)

        let shop = Shop.build graph session service.Request

        shop.Cart.Value <- Shop.baseline
        session.FlushAsync () |> wait
        require (service.Calls = 0) "Checkpoint must precede all async requests"
        Recording.copyCheckpoint live (Path.Combine (directory, "checkpoint"))
        let start = shop.Observations.Count - 1

        shop.Cart.Value <- { shop.Cart.Value with Revision = 1 }

        shop.Cart.Value <-
            { shop.Cart.Value with
                Revision = 2
                Lines =
                    [| { Sku = "tea"
                         Quantity = 3
                         UnitPrice = 2500 } |] }

        require (service.Calls = 2) "Both shipping requests must be in flight"
        service.Release 2
        pumpUntil graph (fun () -> shop.Checkout.Value.QuoteRevision = 2)
        service.Release 1
        pumpUntil graph (fun () -> shop.Observations.Count >= start + 5)
        pumpUntil graph (fun () -> shop.Flights |> Seq.forall _.IsCompleted)
        Task.WhenAll (shop.Flights) |> wait
        session.FlushAsync () |> wait

        let observed = shop.Observations |> Seq.skip start |> Seq.toArray
        verifyIncident failFirst observed

        let manifest =
            { Format = 1
              Producer = "Release-untraced"
              Observations = observed }

        File.WriteAllText (Path.Combine (directory, "capture.json"), JsonSerializer.Serialize manifest)
        observed |> Array.iter printCheckout
        printfn "Captured %d live async requests; production tracing=false" service.Calls

    let private replay directory =
#if RANVIER_TRACE
        require tracedLibrary "Replay requires the traced Ranvier assembly"

        let capture =
            JsonSerializer.Deserialize<Capture> (File.ReadAllText (Path.Combine (directory, "capture.json")))

        require
            (capture.Format = 1
             && capture.Producer = "Release-untraced")
            "Unsupported capture"

        let entries = Recording.readJournal (Path.Combine (directory, "journal.jsonl"))
        let checkpoint = Path.Combine (directory, "checkpoint")
        require (Directory.Exists checkpoint) "Missing checkpoint"

        let scratch =
            Path.Combine (
                Path.GetTempPath (),
                "ranvier-shop-replay-"
                + Guid.NewGuid().ToString ("N")
            )

        try
            Recording.copyCheckpoint checkpoint scratch

            let session =
                Persist.loadAsync (FileStateStore scratch) Shop.keys
                |> result

            require session.Position.IsSome "Checkpoint has no journal position"
            let journal = MemoryJournal "shop"
            use graph = makeGraph session journal
            let mutable calls = 0

            let offline _ : Task<Quote> =
                calls <- calls + 1
                invalidOp "Diagnostic replay attempted a live service call"

            let shop = Shop.build graph session offline
            Persist.replay ReplayMode.Diagnostic session graph entries
            session.FlushAsync () |> wait
            require (calls = 0 && shop.Flights.Count = 0) "Replay started live work"
            let observed = shop.Observations.ToArray ()
            require (observed = capture.Observations) "Replay differs from production observations"

            let failed =
                observed[observed.Length - 1].Phase.StartsWith ("failed:", StringComparison.Ordinal)

            verifyIncident failed observed
            observed |> Array.iter printCheckout
            let why = Trace.why graph shop.Receipt |> Trace.render graph
            let snapshot = Trace.snapshot graph |> Trace.render graph
            File.WriteAllText (Path.Combine (directory, "replay-why.txt"), why)
            File.WriteAllText (Path.Combine (directory, "replay-snapshot.txt"), snapshot)

            Trace.dump graph (Path.Combine (directory, "replay-trace.jsonl"))
            |> ignore

            printfn "%s" why
            printfn "PASS: %d observations match; live calls=0; tracing=true; checkpoint position=%A" observed.Length session.Position
        finally
            if Directory.Exists scratch then
                Directory.Delete (scratch, true)
#else
        ignore directory
        invalidOp "Replay requires Debug -p:RanvierTrace=true"
#endif

    /// <summary>Captures to a new directory or replays an existing recording; returns nonzero on a failed assertion.</summary>
    [<EntryPoint>]
    let main args =
        try
            match args with
            | [| "capture"; directory |] -> capture (Path.GetFullPath directory) false
            | [| "capture-failure"; directory |] -> capture (Path.GetFullPath directory) true
            | [| "replay"; directory |] -> replay (Path.GetFullPath directory)
            | _ -> invalidArg "args" "Usage: capture|capture-failure|replay <recording-directory>"

            0
        with e ->
            eprintfn "%s: %s" (e.GetType().Name) e.Message
            1
