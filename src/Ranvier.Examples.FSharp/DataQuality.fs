namespace Ranvier.Examples.FSharp

open System
open System.IO
open System.Globalization
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualBasic.FileIO
open Ranvier

/// <summary>One source CSV row before local edits.</summary>
type MeasurementRecord =
    { Id: int
      Station: string
      RawText: string
      Unit: string
      Included: bool }

/// <summary>Independent row drafts and a cached validation/normalization calculation.</summary>
type MeasurementRow(graph: Graph, initial: MeasurementRecord) =
    let mutable evaluations = 0

    let owner, baseline, anchor, raw, unit, included, missing, calculated, display =
        graph.Run (fun () ->
            createRoot (fun owner ->
                let baseline = createSignal initial
                let anchor = createSignal initial
                let missing = createSignal false
                let raw = createDraft (fun _ -> baseline.Value.RawText)
                let unit = createDraft (fun _ -> baseline.Value.Unit)
                let included = createDraft (fun _ -> baseline.Value.Included)

                let calculated =
                    createMemo (fun _ ->
                        evaluations <- evaluations + 1
                        let mutable number = 0m
                        let text = raw.Value
                        let unit = unit.Value

                        if missing.Value then
                            None, "Row was removed from the source; accept upstream to remove it."
                        elif
                            not (
                                Decimal.TryParse (
                                    text,
                                    NumberStyles.AllowLeadingSign
                                    ||| NumberStyles.AllowDecimalPoint
                                    ||| NumberStyles.AllowLeadingWhite
                                    ||| NumberStyles.AllowTrailingWhite,
                                    CultureInfo.InvariantCulture,
                                    &number
                                )
                            )
                        then
                            None, "Enter a numeric value using a decimal point."
                        elif number < -2000000m || number > 2000000m then
                            None, "Normalized temperature must be between -273.15 and 1000000 C."
                        else
                            let celsius =
                                match unit with
                                | "C" -> Some number
                                | "F" -> Some ((number - 32m) * 5m / 9m)
                                | "K" -> Some (number - 273.15m)
                                | _ -> None

                            match celsius with
                            | None -> None, "Unit must be C, F or K."
                            | Some value when value < -273.15m -> None, "Temperature is below absolute zero."
                            | Some value when value > 1000000m -> None, "Normalized temperature must be at most 1000000 C."
                            | Some value -> Some value, "")

                let display =
                    createMemo (fun _ ->
                        let value, error = calculated.Value

                        let result =
                            match value with
                            | Some number ->
                                number.ToString ("0.###", CultureInfo.InvariantCulture)
                                + " C"
                            | None -> error

                        sprintf
                            "#%d · %s · %s %s → %s%s"
                            initial.Id
                            baseline.Value.Station
                            raw.Value
                            unit.Value
                            result
                            (if included.Value then "" else " [excluded]"))

                owner, baseline, anchor, raw, unit, included, missing, calculated, display))

    /// <summary>Stable imported row identifier.</summary>
    member _.Id = initial.Id
    /// <summary>Station name from the current accepted source row.</summary>
    member _.Station = baseline.Value.Station

    /// <summary>Raw editable text retained even when numeric validation fails.</summary>
    member _.RawText
        with get () = raw.Value
        and set (value: string) = raw.Value <- (if isNull value then "" else value)

    /// <summary>Editable temperature unit C, F or K.</summary>
    member _.Unit
        with get () = unit.Value
        and set (value: string) =
            unit.Value <-
                (if isNull value then
                     ""
                 else
                     value.Trim().ToUpperInvariant ())

    /// <summary>Whether this row participates in aggregates and export.</summary>
    member _.Included
        with get () = included.Value
        and set value = included.Value <- value

    /// <summary>Validation message for the current drafts.</summary>
    member _.Error = snd calculated.Value
    /// <summary>Whether the row currently normalizes to a physical temperature.</summary>
    member _.IsValid = (fst calculated.Value).IsSome
    /// <summary>Normalized Celsius, or zero for invalid rows; consult IsValid before aggregation.</summary>
    member _.Celsius = fst calculated.Value |> Option.defaultValue 0m
    /// <summary>Computed row caption used by reactive list projections.</summary>
    member _.Display = display.Value
    /// <summary>Number of normalization executions, useful when inspecting incremental work.</summary>
    member _.EvaluationCount = evaluations
    /// <summary>Whether any local field has been edited.</summary>
    member _.IsEdited = raw.IsEdited || unit.IsEdited || included.IsEdited

    /// <summary>Whether edited fields sit over a changed or removed source row.</summary>
    member this.HasConflict =
        this.IsEdited
        && (baseline.Value <> anchor.Value || missing.Value)

    /// <summary>Whether an upstream reload removed this locally edited row.</summary>
    member _.SourceMissing = missing.Value

    /// <summary>Updates upstream values while retaining edited drafts.</summary>
    member this.Reload(value: MeasurementRecord) =
        let clean = not this.IsEdited
        baseline.Value <- value
        missing.Value <- false

        if clean then
            anchor.Value <- value

    /// <summary>Marks an edited row absent from a newer source snapshot.</summary>
    member _.MarkMissing() =
        missing.Value <- true

    /// <summary>Discards local edits and accepts the current source values.</summary>
    member _.AcceptUpstream() =
        batch (fun () ->
            raw.Reset ()
            unit.Reset ()
            included.Reset ()
            anchor.Value <- baseline.Value)

    /// <summary>Releases the row's reactive scope.</summary>
    member _.Dispose() =
        owner.Dispose ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>CSV repair, incremental per-row statistics, bulk conversion and independent selection lookup.</summary>
/// <param name="graph">Caller-owned graph used on its owning thread.</param>
/// <param name="lookup">Asynchronous selected-station calibration lookup; superseded results are retired.</param>
type DataQualityModel(graph: Graph, lookup: Func<string, CancellationToken, Task<string>>) =
    let mutable disposed = false
    let mutable loadVersion = 0
    let mutable pendingLoad = new CancellationTokenSource ()

    let root, rows, filter, selected, status, path =
        graph.Run (fun () ->
            createRoot (fun owner ->
                let rows = createKeyedCollection (fun (row: MeasurementRow) -> row.Id)
                owner, rows, createSignal "All", createSignal 0, createSignal "Load a measurement CSV", createSignal ""))

    let visible, captions, invalid, valid, edited, sum, mean, summary, calibration =
        graph.Run (fun () ->
            createRoot (fun owner ->
                root.Attach owner

                let visible =
                    rows.Rows
                    |> Projection.filter (fun row ->
                        match filter.Value with
                        | "Invalid" -> not row.IsValid
                        | "Included" -> row.Included
                        | "Edited" -> row.IsEdited
                        | _ -> true)

                let captions = visible |> Projection.map (fun row -> row.Display)

                let included =
                    rows.Rows
                    |> Projection.filter (fun row -> row.Included)

                let validRows =
                    included
                    |> Projection.filter (fun row -> row.IsValid)

                let invalid =
                    included
                    |> Projection.countBy (fun row -> not row.IsValid)

                let valid = validRows |> Projection.countBy (fun _ -> true)

                let edited =
                    rows.Rows
                    |> Projection.countBy (fun row -> row.IsEdited)

                let sum =
                    validRows
                    |> Projection.sumBy (fun row -> row.Celsius)

                let mean =
                    createMemo (fun _ ->
                        if valid.Value = 0 then
                            0m
                        else
                            sum.Value / decimal valid.Value)

                let summary =
                    createMemo (fun _ ->
                        sprintf
                            "%d included valid · %d included invalid · %d edited · mean %s C"
                            valid.Value
                            invalid.Value
                            edited.Value
                            (mean.Value.ToString ("0.###", CultureInfo.InvariantCulture)))

                let request =
                    createAsync (fun _ cancellation ->
                        match rows.Rows.TryGet selected.Value with
                        | None -> Task.FromResult "Select a row to inspect its calibration record"
                        | Some row -> lookup.Invoke (row.Station, cancellation))

                let calibration =
                    createBoundary (fun _ -> "Loading calibration...") (fun error _ -> "Calibration unavailable: " + error.Message) (fun () ->
                        request.Value)

                visible, captions, invalid, valid, edited, sum, mean, summary, calibration))

    let post action =
        let doneTask =
            TaskCompletionSource<_> (TaskCreationOptions.RunContinuationsAsynchronously)

        if disposed then
            doneTask.TrySetCanceled () |> ignore
        else
            try
                graph.Dispatch (fun () ->
                    if disposed then
                        doneTask.TrySetCanceled () |> ignore
                    else
                        try
                            doneTask.TrySetResult (graph.Run action) |> ignore
                        with error ->
                            doneTask.TrySetException (error) |> ignore)
            with error ->
                doneTask.TrySetException (error) |> ignore

        doneTask.Task

    let parse (file: string) (token: CancellationToken) =
        use parser = new TextFieldParser (file)
        parser.TextFieldType <- FieldType.Delimited
        parser.SetDelimiters [| "," |]
        parser.HasFieldsEnclosedInQuotes <- true
        let header = if parser.EndOfData then [||] else parser.ReadFields ()

        if
            header
            <> [| "id"; "station"; "value"; "unit"; "included" |]
        then
            raise (InvalidDataException ("Expected CSV header: id,station,value,unit,included"))

        let result = ResizeArray<MeasurementRecord> ()
        let ids = Collections.Generic.HashSet<int> ()

        while not parser.EndOfData do
            token.ThrowIfCancellationRequested ()
            let fields = parser.ReadFields ()

            if fields.Length <> 5 then
                raise (InvalidDataException (sprintf "CSV line %d must have five fields." parser.LineNumber))

            let mutable id = 0
            let mutable included = false

            if
                not (Int32.TryParse (fields.[0], &id))
                || id < 1
                || not (ids.Add id)
            then
                raise (InvalidDataException (sprintf "CSV line %d needs a unique positive ID." parser.LineNumber))

            if not (Boolean.TryParse (fields.[4], &included)) then
                raise (InvalidDataException ("Included must be true or false."))

            if String.IsNullOrWhiteSpace fields.[1] then
                raise (InvalidDataException ("Station must be nonblank."))

            result.Add
                { Id = id
                  Station = fields.[1]
                  RawText = fields.[2]
                  Unit = fields.[3].ToUpperInvariant ()
                  Included = included }

        result.ToArray ()

    /// <summary>Stable keyed row source with independently reactive row fields.</summary>
    member _.Rows = rows.Rows
    /// <summary>Filtered rows, preserving source keys.</summary>
    member _.Visible = visible
    /// <summary>Per-row caption projection, suitable for a change-reader-backed list.</summary>
    member _.Captions = captions
    /// <summary>Visible row IDs in displayed order.</summary>
    member _.VisibleIds = visible.Keys
    /// <summary>Current source path.</summary>
    member _.SourcePath = path.Value

    /// <summary>Filters All, Invalid, Included or Edited rows.</summary>
    member _.Filter
        with get () = filter.Value
        and set value = filter.Value <- value

    /// <summary>Selected stable row key, or zero for no selection.</summary>
    member _.SelectedId
        with get () = selected.Value
        and set value = selected.Value <- value

    /// <summary>Boundary-rendered asynchronous calibration for the current selection.</summary>
    member _.Calibration = calibration.Value
    /// <summary>Current import/export state or failure.</summary>
    member _.Status = status.Value
    /// <summary>Number of included invalid rows.</summary>
    member _.InvalidCount = invalid.Value
    /// <summary>Number of included valid rows.</summary>
    member _.ValidCount = valid.Value
    /// <summary>Number of locally edited rows.</summary>
    member _.EditedCount = edited.Value
    /// <summary>Mean of included valid normalized values, or zero when none are valid.</summary>
    member _.MeanCelsius = mean.Value
    /// <summary>Coherent derived summary observed by both console and UI hosts.</summary>
    member _.Summary = summary.Value

    /// <summary>Returns the independently editable row with the requested key.</summary>
    member _.GetRow(id: int) =
        rows.Rows.Get id

    /// <summary>Imports CSV data off-thread and merges accepted rows without losing matching drafts.</summary>
    member _.LoadAsync(file: string) =
        pendingLoad.Cancel ()
        pendingLoad.Dispose ()
        pendingLoad <- new CancellationTokenSource ()
        let token = pendingLoad.Token
        loadVersion <- loadVersion + 1
        let version = loadVersion
        status.Value <- "Reading CSV..."

        task {
            try
                let! imported = Task.Run ((fun () -> parse file token), token)

                let! _ =
                    post (fun () ->
                        token.ThrowIfCancellationRequested ()

                        if version = loadVersion then
                            batch (fun () ->
                                let ids = imported |> Array.map _.Id |> Set.ofArray

                                for id in rows.Rows.Keys do
                                    if not (ids.Contains id) then
                                        let row = rows.Rows.Get id

                                        if row.IsEdited then
                                            row.MarkMissing ()
                                        else
                                            rows.Remove id |> ignore
                                            row.Dispose ()

                                for source in imported do
                                    match rows.Rows.TryGet source.Id with
                                    | Some row -> row.Reload source
                                    | None ->
                                        let row = new MeasurementRow (graph, source)
                                        root.Attach row
                                        rows.AddOrUpdate row

                                path.Value <- Path.GetFullPath file
                                status.Value <- sprintf "Loaded %d rows from %s" imported.Length (Path.GetFileName file)))

                return ()
            with error ->
                if not disposed then
                    let! _ =
                        post (fun () ->
                            if version = loadVersion then
                                status.Value <- "Import failed: " + error.Message)

                    ()

                return raise error
        }

    /// <summary>Reloads the accepted path, retaining edits and exposing changed source baselines.</summary>
    member this.ReloadAsync() =
        if path.Value = "" then
            Task.FromException<unit> (InvalidOperationException ("Load a CSV first."))
        else
            this.LoadAsync (path.Value)

    /// <summary>Converts included valid rows in one batch; excluded and invalid rows retain their inputs.</summary>
    member _.ApplyUnitToIncluded(target: string) =
        if not (List.contains target [ "C"; "F"; "K" ]) then
            invalidArg "target" "Choose C, F or K."

        batch (fun () ->
            for id in rows.Rows.Keys do
                let row = rows.Rows.Get id

                if row.Included && row.IsValid then
                    let value =
                        match target with
                        | "F" -> row.Celsius * 9m / 5m + 32m
                        | "K" -> row.Celsius + 273.15m
                        | _ -> row.Celsius

                    row.RawText <- value.ToString (CultureInfo.InvariantCulture)
                    row.Unit <- target)

    /// <summary>Excludes invalid rows together while leaving their raw values available for repair.</summary>
    member _.ExcludeInvalid() =
        batch (fun () ->
            for id in rows.Rows.Keys do
                let row = rows.Rows.Get id in

                if not row.IsValid then
                    row.Included <- false)

    /// <summary>Discards a row's edits, or removes its orphaned draft after an upstream deletion.</summary>
    member _.AcceptUpstream(id: int) =
        let row = rows.Rows.Get id

        if row.SourceMissing then
            rows.Remove id |> ignore
            row.Dispose ()
        else
            row.AcceptUpstream ()

    /// <summary>Exports an immutable snapshot of included valid rows; invalid included rows prevent writing.</summary>
    member _.ExportAsync(destination: string) : Task =
        if invalid.Value <> 0 then
            Task.FromException (InvalidOperationException ("Repair or exclude invalid included rows before export."))
        else
            let quote (value: string) =
                "\"" + value.Replace ("\"", "\"\"") + "\""

            let lines =
                rows.Rows.Keys
                |> Array.map rows.Rows.Get
                |> Array.filter _.Included
                |> Array.map (fun row -> String.concat "," [ string row.Id; quote row.Station; quote row.RawText; quote row.Unit; "true" ])

            let content =
                "id,station,value,unit,included\n"
                + String.concat "\n" lines
                + "\n"

            task {
                do! File.WriteAllTextAsync (destination, content)
                let! _ = post (fun () -> status.Value <- sprintf "Exported %d rows to %s" lines.Length destination)
                return ()
            }
            :> Task

    /// <summary>Observes the current summary and settled changes until subscription or model disposal.</summary>
    member _.ObserveSummary(callback: Action<string>) : IDisposable =
        graph.Run (fun () ->
            let child =
                createRoot (fun owner ->
                    createEffect (fun () -> callback.Invoke summary.Value)
                    owner)

            root.Attach child
            child :> IDisposable)

    /// <summary>Cancels import/lookup work and releases row scopes and projections.</summary>
    member _.Dispose() =
        if not disposed then
            disposed <- true
            pendingLoad.Cancel ()
            pendingLoad.Dispose ()
            root.Dispose ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()
