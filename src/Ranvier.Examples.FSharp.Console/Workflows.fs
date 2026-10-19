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
