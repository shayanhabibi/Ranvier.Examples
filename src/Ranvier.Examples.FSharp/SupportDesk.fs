namespace Ranvier.Examples.FSharp

open System
open System.Threading
open System.Threading.Tasks
open Ranvier
open Ranvier.Query
open Ranvier.Examples.CSharp.Support

type private TicketEditor = {
    Owner: Owner
    Lease: QueryLease<TicketRecord>
    Notes: Editable<string>
    State: Editable<string>
    Version: Signal<int>
}

/// <summary>A shared ticket queue and independently owned editor over versioned remote records.</summary>
/// <param name="graph">The caller-owned graph, accessed on its owning thread.</param>
/// <param name="service">The asynchronous ticket transport.</param>
type SupportDesk(graph: Graph, service: ITicketService) =
    let mutable disposed = false
    let root, client, searchText, admitted, currentQueue, lastQueue, editor, saving, message, receipt, repair =
        graph.Run(fun () -> createRoot(fun owner ->
            let client = new QueryClient(graph)
            owner.Attach client
            let search = createSignal ""
            let admitted = debounce (TimeSpan.FromMilliseconds 250.) (fun () -> search.Value.Trim()) graph
            let queue: Signal<QueryLease<TicketRecord array> option> = createSignal None
            let editor: Signal<TicketEditor option> = createSignal None
            let accepted: Signal<TicketRecord array> = createSignal [||]
            let receipt: Signal<TicketRecord option> = createSignal None
            owner, client, search, admitted, queue, accepted, editor, createSignal false, createSignal "Ready", receipt, createSignal false))
    let searches = client.Define(fun (term: string) token -> service.SearchAsync(term, token))
    let details = client.Define(fun (id: int) token -> service.GetAsync(id, token))
    let mutable queueKey = None

    let post action =
        let completed = TaskCompletionSource<_>(TaskCreationOptions.RunContinuationsAsynchronously)
        if disposed then completed.TrySetCanceled() |> ignore
        else
            try graph.Dispatch(fun () ->
                if disposed then completed.TrySetCanceled() |> ignore
                else
                    try completed.TrySetResult(action()) |> ignore
                    with error -> completed.TrySetException(error) |> ignore)
            with error -> completed.TrySetException(error) |> ignore
        completed.Task

    let search term =
        if disposed then Task.FromCanceled<TicketRecord array>(CancellationToken(true))
        else graph.Run(fun () ->
            match currentQueue.Value with
            | Some lease when queueKey = Some term -> lease.EnsureAsync()
            | previous ->
                let lease = searches.AcquireOwned(term, root)
                queueKey <- Some term
                currentQueue.Value <- Some lease
                previous |> Option.iter _.Dispose()
                lease.EnsureAsync())

    let observe (work: Task<_>) =
        work.ContinueWith(fun (finished: Task<_>) -> if finished.IsFaulted then finished.Exception |> ignore) |> ignore

    let back () =
        graph.Run(fun () ->
            let old = editor.Value
            editor.Value <- None
            old |> Option.iter(fun e -> e.Owner.Dispose()))

    let hasData (e: TicketEditor) = e.Lease.State.Data.IsSome

    let queue, status, detailSummary =
        graph.Run(fun () -> createRoot(fun owner ->
            root.Attach owner
            createEffect(fun () ->
                let term = admitted.Value
                untrack(fun () -> search term |> observe))
            createEffect(fun () ->
                match currentQueue.Value with
                | Some lease -> lease.State.Data |> Option.iter(fun data -> lastQueue.Value <- data)
                | None -> ())
            let queue = createMemo(fun _ ->
                match currentQueue.Value with
                | Some lease -> lease.State.Data |> Option.defaultWith(fun () -> lastQueue.Value)
                | None -> lastQueue.Value)
            let status = createMemo(fun _ ->
                match currentQueue.Value with
                | None -> "No queue"
                | Some lease ->
                    let state = lease.State
                    match state.Error with
                    | Some error -> "Retaining accepted queue: " + error.Message
                    | None when state.FetchStatus = FetchStatus.Fetching -> "Loading queue..."
                    | None when state.IsStale -> "Queue is stale; refresh to load"
                    | None -> sprintf "%d tickets" queue.Value.Length)
            let summary = createBoundary (fun _ -> "Loading ticket...") (fun error _ -> "Ticket unavailable: " + error.Message) (fun () ->
                match editor.Value with
                | None -> "Select a ticket"
                | Some selected -> sprintf "#%d · %s" selected.Lease.Value.Id selected.Lease.Value.Title)
            queue, status, summary))

    /// <summary>Search input admitted after a 250ms quiet period.</summary>
    member _.SearchText with get() = searchText.Value and set value = searchText.Value <- if isNull value then "" else value
    /// <summary>Accepted queue retained while a replacement request is pending.</summary>
    member _.Queue = queue.Value
    /// <summary>Queue loading, stale and failure metadata.</summary>
    member _.QueueStatus = status.Value
    /// <summary>Boundary-rendered selected ticket title.</summary>
    member _.DetailSummary = detailSummary.Value
    /// <summary>Selected ticket ID, or zero after Back.</summary>
    member _.SelectedId = editor.Value |> Option.map(fun e -> e.Lease.State.Data |> Option.map _.Id |> Option.defaultValue 0) |> Option.defaultValue 0
    /// <summary>Accepted server notes, independent of the local draft.</summary>
    member _.ServerNotes = editor.Value |> Option.bind(fun e -> e.Lease.State.Data) |> Option.map _.Notes |> Option.defaultValue ""
    /// <summary>Local notes retained across server refresh until explicitly accepted or reset.</summary>
    member _.DraftNotes
        with get() = match editor.Value with Some e when hasData e -> e.Notes.Value | _ -> ""
        and set value = match editor.Value with Some e when hasData e -> e.Notes.Value <- (if isNull value then "" else value) | _ -> ()
    /// <summary>Local workflow state, independent of the accepted server state.</summary>
    member _.DraftState
        with get() = match editor.Value with Some e when hasData e -> e.State.Value | _ -> "Open"
        and set value = match editor.Value with Some e when hasData e -> e.State.Value <- value | _ -> ()
    /// <summary>Whether local fields have been edited.</summary>
    member _.IsDirty = match editor.Value with Some e when hasData e -> e.Notes.IsEdited || e.State.IsEdited | _ -> false
    /// <summary>Whether a refreshed server version differs from the draft's baseline.</summary>
    member _.HasConflict = match editor.Value with Some e when hasData e -> e.Lease.Value.Version <> e.Version.Value | _ -> false
    /// <summary>Whether a client-owned mutation is in flight.</summary>
    member _.IsSaving = saving.Value
    /// <summary>Whether a valid edited record can be submitted.</summary>
    member this.CanSave = this.SelectedId <> 0 && this.IsDirty && not saving.Value && not repair.Value && this.DraftNotes.Length <= 4000
    /// <summary>Last command result or failure.</summary>
    member _.Message = message.Value
    /// <summary>The last successful server receipt, including after a local reconciliation failure.</summary>
    member _.LastSaved = receipt.Value |> Option.toObj

    /// <summary>Immediately searches; UI typing uses the timed SearchText admission instead.</summary>
    member _.SearchAsync(term: string) = graph.Run(fun () -> searchText.Value <- term; search term)
    /// <summary>Explicitly refreshes the current queue while retaining accepted data.</summary>
    member _.RefreshQueueAsync() = graph.Run(fun () ->
        match currentQueue.Value with Some lease -> lease.RefreshAsync() | None -> search searchText.Value)

    /// <summary>Creates an editor-owned detail lease and drafts for the selected ticket.</summary>
    member _.OpenAsync(id: int) =
        back()
        let selected = graph.Run(fun () ->
            createRoot(fun owner ->
                root.Attach owner
                let lease = details.AcquireOwned(id, owner)
                let value = {
                    Owner = owner; Lease = lease
                    Notes = createDraft(fun _ -> lease.Value.Notes)
                    State = createDraft(fun _ -> lease.Value.State)
                    Version = createSignal 0 }
                editor.Value <- Some value
                value))
        let request = selected.Lease.EnsureAsync()
        task {
            let! accepted = request
            let! _ = post(fun () -> if not selected.Owner.IsDisposed then selected.Version.Value <- accepted.Version)
            return ()
        }

    /// <summary>Refreshes detail and queue, preserving drafts and exposing changed versions.</summary>
    member _.RefreshAsync() =
        let detailTask, queueTask = graph.Run(fun () ->
            let detailTask = match editor.Value with Some e -> e.Lease.RefreshAsync() :> Task | None -> Task.CompletedTask
            let queueTask = match currentQueue.Value with Some lease -> lease.RefreshAsync() :> Task | None -> Task.CompletedTask
            detailTask, queueTask)
        task {
            do! Task.WhenAll(detailTask, queueTask)
            let! _ = post(fun () -> repair.Value <- false)
            return ()
        }

    /// <summary>Discards local edits and accepts the current server baseline.</summary>
    member _.AcceptUpstream() = graph.Run(fun () ->
        match editor.Value with
        | Some e when hasData e -> batch(fun () -> e.Notes.Reset(); e.State.Reset(); e.Version.Value <- e.Lease.Value.Version)
        | _ -> ())

    /// <summary>Keeps local edits while explicitly rebasing them onto the refreshed server version.</summary>
    member _.Rebase() = graph.Run(fun () ->
        match editor.Value with Some e when hasData e -> e.Version.Value <- e.Lease.Value.Version | _ -> ())

    /// <summary>Saves a captured draft in the client scope; Back releases the editor without cancelling this write.</summary>
    member this.SaveAsync() =
        if not this.CanSave then Task.FromResult false
        else
            let selected = editor.Value.Value
            let captured = TicketUpdate(selected.Lease.Value.Id, selected.Notes.Value, selected.State.Value, selected.Version.Value)
            let mutation = graph.Run(fun () ->
                saving.Value <- true
                message.Value <- "Saving..."
                client.Mutate(captured, (fun input token -> service.SaveAsync(input, token)), (fun saved -> [
                    details.SetIfLoaded(saved.Id, saved)
                    searches.InvalidateWhere(fun _ -> true)
                ])))
            task {
                try
                    let! outcome = mutation
                    let! accepted = post(fun () ->
                        saving.Value <- false
                        match outcome with
                        | MutationOutcome.Applied saved ->
                            receipt.Value <- Some saved
                            message.Value <- sprintf "Saved #%d version %d" saved.Id saved.Version
                            if not selected.Owner.IsDisposed then
                                batch(fun () ->
                                    selected.Version.Value <- saved.Version
                                    if selected.Notes.Value = captured.Notes then selected.Notes.Reset()
                                    if selected.State.Value = captured.State then selected.State.Reset())
                            true
                        | MutationOutcome.RequestFailed error -> message.Value <- error.Message; false
                        | MutationOutcome.ReconciliationFailed(saved, error) ->
                            receipt.Value <- Some saved
                            repair.Value <- true
                            message.Value <- "Saved on server. Refresh to repair local state: " + error.Message
                            false)
                    if accepted then
                        try
                            let! refresh = post(fun () -> match currentQueue.Value with Some lease -> lease.EnsureAsync() | None -> Task.FromResult [||])
                            let! _ = refresh
                            ()
                        with _ -> ()
                    return accepted
                with error ->
                    let! _ = post(fun () -> saving.Value <- false; message.Value <- error.Message)
                    return false
            }

    /// <summary>Closes the editor and releases its drafts and detail lease.</summary>
    member _.Back() = back()
    /// <summary>Cancels session-owned requests and releases all observers and leases.</summary>
    member _.Dispose() = if not disposed then disposed <- true; root.Dispose()
    interface IDisposable with member this.Dispose() = this.Dispose()
