module internal Ranvier.Examples.FSharp.Console.Workflows

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Examples.FSharp
open Ranvier.Examples.CSharp.Support

let private complete (graph: Graph) (work: Task) =
    let deadline = DateTime.UtcNow.AddSeconds 30.

    while not work.IsCompleted do
        graph.Pump () |> ignore

        if DateTime.UtcNow > deadline then
            failwith "Workflow timed out"

        Thread.Sleep 1

    graph.Pump () |> ignore
    work.GetAwaiter().GetResult ()

let data () =
    use graph = new Graph ()
    use active = graph.Activate ()

    use model =
        new DataQualityModel (graph, Func<string, CancellationToken, Task<string>> (fun station _ -> Task.FromResult (station + " calibration")))

    complete graph (model.LoadAsync (Path.Combine (AppContext.BaseDirectory, "samples", "measurements.csv")))
    printfn "%s" model.Summary
    let unaffected = model.GetRow(2).EvaluationCount
    model.GetRow(4).RawText <- "19"
    model.GetRow(5).Unit <- "C"

    if
        model.InvalidCount <> 0
        || model.MeanCelsius <> 20.26m
    then
        failwith "Repair failed"

    if model.GetRow(2).EvaluationCount <> unaffected then
        failwith "Unrelated row recalculated"

    model.ApplyUnitToIncluded "F"
    let output = Path.Combine (AppContext.BaseDirectory, "repaired.csv")
    complete graph (model.ExportAsync output)
    printfn "%s\nExported %s" model.Summary output

let elmish () =
    use graph = new Graph ()
    use active = graph.Activate ()
    let input = Path.Combine (AppContext.BaseDirectory, "samples", "expenses.json")
    let output = Path.Combine (AppContext.BaseDirectory, "expense-decisions.json")

    use review =
        new ExpenseReview (
            graph,
            Func<_, _> (fun token -> ExpenseFiles.LoadAsync (input, token)),
            Func<_, _, _> (fun rows token -> ExpenseFiles.SaveAsync (output, rows, token))
        )

    review.Load ()
    complete graph review.Completion

    if review.Snapshot.Length <> 3 then
        failwith review.Error

    let mutable notifications = 0

    use observer =
        review.ObserveApprovedTotal (
            Action<decimal> (fun total ->
                notifications <- notifications + 1
                printfn "Approved total: %.2f" total)
        )

    review.SelectedId <- 1
    review.Filter <- "Pending"

    if notifications <> 1 then
        failwith "Navigation notified the approved-total reader"

    review.Approve ()
    review.SelectedId <- 2
    review.Approve ()

    if not (review.Error.Contains ("receipt")) then
        failwith "Receipt validation failed"

    review.Reason <- "Please provide the missing receipt."
    review.Reject ()
    review.Undo ()
    review.Redo ()

    if
        review.SelectedDecision <> "Rejected"
        || review.ApprovedTotal <> 86m
    then
        failwith "Review history failed"

    review.Save ()
    complete graph review.Completion

    if
        review.IsDirty
        || review.Error <> ""
        || not (File.Exists output)
    then
        failwith review.Error

    printfn "Elmish decisions saved to %s; %d total notifications." output notifications

let support () =
    use graph = new Graph ()
    use active = graph.Activate ()

    let folder =
        Path.Combine (Path.GetTempPath (), "ranvier-support-" + Guid.NewGuid().ToString ("N"))

    let start = SupportApi.StartAsync folder
    complete graph start
    let api = start.Result

    try
        use http = new TicketHttpClient (api.BaseAddress)
        use desk = new SupportDesk (graph, http)
        complete graph (desk.SearchAsync "")
        complete graph (desk.OpenAsync 1)
        desk.DraftNotes <- "Investigated the customer's proxy configuration."
        let current = http.GetAsync (1, CancellationToken.None)
        complete graph current
        let ticket = current.Result

        complete
            graph
            (http.SaveAsync (TicketUpdate (1, ticket.Notes + "\nColleague supplied logs.", ticket.State, ticket.Version), CancellationToken.None))

        complete graph (desk.RefreshAsync ())

        if not desk.HasConflict || not desk.IsDirty then
            failwith "Refresh discarded the draft"

        printfn "Refresh preserved the draft and detected a server version conflict."
        desk.Rebase ()
        let save = desk.SaveAsync ()
        desk.Back ()
        complete graph save

        if not save.Result then
            failwith desk.Message

        printfn "Save completed after navigation: %s" desk.Message
        api.Available <- false

        try
            complete graph (desk.RefreshQueueAsync ())
            failwith "Expected the disconnected service to reject refresh"
        with :? System.Net.Http.HttpRequestException ->
            ()

        printfn "Disconnected: %s" desk.QueueStatus
        api.Available <- true
        complete graph (desk.RefreshQueueAsync ())
        printfn "Recovered: %d tickets" desk.Queue.Length
    finally
        complete graph (api.DisposeAsync().AsTask ())
