namespace Ranvier.Examples.FSharp

open System
open Ranvier
open Ranvier.Elmish

/// <summary>An imported expense claim in the fixture's single currency.</summary>
[<CLIMutable>]
type ExpenseClaim =
    { Id: int
      Employee: string
      Purpose: string
      Amount: decimal
      HasReceipt: bool }

/// <summary>A claim with its current review decision and rejection reason.</summary>
[<CLIMutable>]
type ExpenseDecision =
    { Claim: ExpenseClaim
      Decision: string
      Reason: string }

/// <summary>Immutable workflow state, including decision history and the revision being saved.</summary>
type internal ExpenseState =
    { Rows: ExpenseDecision list
      Past: ExpenseDecision list list
      Future: ExpenseDecision list list
      SelectedId: int
      Filter: string
      Reason: string
      Revision: int
      SavedRevision: int
      Loading: bool
      Saving: int option
      Error: string
      Status: string }

/// <summary>Inputs and command completions accepted by the expense review update function.</summary>
type internal ExpenseMsg =
    | Load
    | Loaded of Result<ExpenseClaim array, string>
    | Select of int
    | Filter of string
    | Reason of string
    | Approve
    | Reject
    | Undo
    | Redo
    | Save
    | Saved of int * Result<unit, string>

/// <summary>File operations requested by a pure state transition.</summary>
type internal ExpenseEffect =
    | ReadClaims
    | WriteDecisions of int * ExpenseDecision array

/// <summary>Pure expense review transitions and eligibility rules.</summary>
module internal ExpenseUpdate =
    /// <summary>Initial empty review ready for an import command.</summary>
    let initial =
        { Rows = []
          Past = []
          Future = []
          SelectedId = 0
          Filter = "All"
          Reason = ""
          Revision = 0
          SavedRevision = 0
          Loading = false
          Saving = None
          Error = ""
          Status = "Load expense claims" }

    /// <summary>Whether decisions changed since the last successful captured save.</summary>
    let dirty model =
        model.Revision <> model.SavedRevision

    /// <summary>Whether importing would preserve all current decisions.</summary>
    let canLoad model =
        not model.Loading
        && model.Saving.IsNone
        && not (dirty model)

    /// <summary>Whether a changed decision snapshot can be saved.</summary>
    let canSave model =
        not model.Loading
        && model.Saving.IsNone
        && dirty model

    /// <summary>Applies a message and returns immutable state plus requested file effects.</summary>
    let update msg model =
        let keep = model, []

        let decision state =
            match
                model.Rows
                |> List.tryFind (fun row -> row.Claim.Id = model.SelectedId)
            with
            | None -> keep
            | Some row when state = "Approved" && not row.Claim.HasReceipt ->
                { model with
                    Error = "Approval requires a receipt." },
                []
            | Some _ when
                state = "Rejected"
                && String.IsNullOrWhiteSpace model.Reason
                ->
                { model with
                    Error = "Rejection requires a reason." },
                []
            | Some row ->
                let reason = if state = "Rejected" then model.Reason.Trim () else ""

                if row.Decision = state && row.Reason = reason then
                    keep
                else
                    let rows =
                        model.Rows
                        |> List.map (fun item ->
                            if item.Claim.Id = model.SelectedId then
                                { item with
                                    Decision = state
                                    Reason = reason }
                            else
                                item)

                    { model with
                        Rows = rows
                        Past = model.Rows :: model.Past
                        Future = []
                        Revision = model.Revision + 1
                        Error = ""
                        Status = "Decision staged locally" },
                    []

        match msg with
        | Load when canLoad model ->
            { model with
                Loading = true
                Error = ""
                Status = "Reading claims..." },
            [ ReadClaims ]
        | Loaded (Result.Ok claims) when model.Loading ->
            { initial with
                Rows =
                    claims
                    |> Array.map (fun claim ->
                        { Claim = claim
                          Decision = "Pending"
                          Reason = "" })
                    |> Array.toList
                Status = sprintf "Loaded %d claims" claims.Length },
            []
        | Loaded (Result.Error error) when model.Loading ->
            { model with
                Loading = false
                Error = error
                Status = "Import failed" },
            []
        | Select id ->
            let reason =
                model.Rows
                |> List.tryFind (fun row -> row.Claim.Id = id)
                |> Option.map _.Reason
                |> Option.defaultValue ""

            { model with
                SelectedId = id
                Reason = reason },
            []
        | Filter value -> { model with Filter = value }, []
        | Reason value -> { model with Reason = value }, []
        | Approve when not model.Loading -> decision "Approved"
        | Reject when not model.Loading -> decision "Rejected"
        | Undo when not model.Loading ->
            match model.Past with
            | previous :: rest ->
                { model with
                    Rows = previous
                    Past = rest
                    Future = model.Rows :: model.Future
                    Revision = model.Revision + 1
                    Error = ""
                    Status = "Decision undone" },
                []
            | [] -> keep
        | Redo when not model.Loading ->
            match model.Future with
            | next :: rest ->
                { model with
                    Rows = next
                    Past = model.Rows :: model.Past
                    Future = rest
                    Revision = model.Revision + 1
                    Error = ""
                    Status = "Decision restored" },
                []
            | [] -> keep
        | Save when canSave model ->
            { model with
                Saving = Some model.Revision
                Error = ""
                Status = "Writing decision snapshot..." },
            [ WriteDecisions (model.Revision, List.toArray model.Rows) ]
        | Saved (revision, result) when model.Saving = Some revision ->
            match result with
            | Result.Ok () ->
                { model with
                    Saving = None
                    SavedRevision = revision
                    Error = ""
                    Status = "Decision snapshot saved" },
                []
            | Result.Error error ->
                { model with
                    Saving = None
                    Error = error
                    Status = "Save failed; decisions retained" },
                []
        | _ -> keep

open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks

/// <summary>An owned Elmish loop with selectors for expense review views and injectable file commands.</summary>
/// <param name="graph">Caller-owned graph; public inputs run on its owning thread.</param>
/// <param name="load">Loads an immutable claim batch; cancellation belongs to this review session.</param>
/// <param name="save">Writes the captured decision snapshot; later edits remain unsaved.</param>
type ExpenseReview(graph: Graph, load: Func<CancellationToken, Task<ExpenseClaim array>>, save: Func<ExpenseDecision array, CancellationToken, Task>)
    =
    let mutable disposed = false
    let lifetime = new CancellationTokenSource ()
    let tasks = Collections.Generic.List<Task> ()

    let validate (claims: ExpenseClaim array) =
        if isNull claims then
            raise (InvalidDataException ("Expected a JSON claim array."))

        let ids = Collections.Generic.HashSet<int> ()

        for claim in claims do
            if
                isNull (box claim)
                || claim.Id <= 0
                || not (ids.Add claim.Id)
                || String.IsNullOrWhiteSpace claim.Employee
                || String.IsNullOrWhiteSpace claim.Purpose
                || claim.Amount <= 0m
                || claim.Amount > 1000000m
            then
                raise (InvalidDataException ("Claims need unique positive IDs, employee, purpose and an amount between 0 and 1000000."))

    let command effect (dispatch: ExpenseMsg -> unit) =
        let publish msg =
            if not disposed then
                graph.Dispatch (fun () ->
                    if not disposed then
                        dispatch msg)

        let work =
            task {
                match effect with
                | ReadClaims ->
                    try
                        let! claims = load.Invoke lifetime.Token
                        validate claims
                        publish (Loaded (Result.Ok claims))
                    with error ->
                        publish (Loaded (Result.Error error.Message))
                | WriteDecisions (revision, rows) ->
                    try
                        do! save.Invoke (rows, lifetime.Token)
                        publish (Saved (revision, Result.Ok ()))
                    with error ->
                        publish (Saved (revision, Result.Error error.Message))
            }

        tasks.Add work

    let (owner,
         app,
         rows,
         selectedId,
         filter,
         reason,
         selected,
         visible,
         total,
         error,
         status,
         dirty,
         canLoad,
         canSave,
         canUndo,
         canRedo,
         loading,
         saving) =
        graph.Run (fun () ->
            createRoot (fun owner ->
                let update msg model =
                    let next, effects = ExpenseUpdate.update msg model
                    next, effects |> List.map command

                let app = Mvu.withCmd (ExpenseUpdate.initial, []) update
                let rows = app.Select _.Rows
                let selectedId = app.Select _.SelectedId
                let filter = app.Select _.Filter
                let reason = app.Select _.Reason

                let selected =
                    createMemo (fun _ ->
                        rows.Value
                        |> List.tryFind (fun row -> row.Claim.Id = selectedId.Value))

                let visible =
                    createMemo (fun _ ->
                        let mode = filter.Value

                        rows.Value
                        |> List.filter (fun row -> mode = "All" || row.Decision = mode)
                        |> List.toArray)

                let total =
                    createMemo (fun _ ->
                        rows.Value
                        |> List.sumBy (fun row -> if row.Decision = "Approved" then row.Claim.Amount else 0m))

                owner,
                app,
                rows,
                selectedId,
                filter,
                reason,
                selected,
                visible,
                total,
                app.Select _.Error,
                app.Select _.Status,
                app.Select ExpenseUpdate.dirty,
                app.Select ExpenseUpdate.canLoad,
                app.Select ExpenseUpdate.canSave,
                app.Select (fun model -> not model.Loading && not model.Past.IsEmpty),
                app.Select (fun model -> not model.Loading && not model.Future.IsEmpty),
                app.Select _.Loading,
                app.Select (fun model -> model.Saving.IsSome)))

    let dispatch msg =
        ObjectDisposedException.ThrowIf (disposed, "ExpenseReview")
        app.Dispatch msg

    /// <summary>All current decisions, including pending and filtered-out claims.</summary>
    member _.Snapshot = List.toArray rows.Value
    /// <summary>Current decision-filtered rows with stable imported claim IDs.</summary>
    member _.Visible = visible.Value

    /// <summary>Stable selected claim ID, retained when filtering hides its row.</summary>
    member _.SelectedId
        with get () = selectedId.Value
        and set value = dispatch (Select value)

    /// <summary>Decision filter: All, Pending, Approved or Rejected.</summary>
    member _.Filter
        with get () = filter.Value
        and set value = dispatch (Filter value)

    /// <summary>Reason input used by the next rejection message.</summary>
    member _.Reason
        with get () = reason.Value
        and set value = dispatch (Reason (if isNull value then "" else value))

    /// <summary>Decision of the selected claim, or an empty string without selection.</summary>
    member _.SelectedDecision =
        selected.Value
        |> Option.map _.Decision
        |> Option.defaultValue ""

    /// <summary>Selected claim details and receipt availability.</summary>
    member _.Detail =
        selected.Value
        |> Option.map (fun row ->
            sprintf
                "#%d · %s\n%s\nAmount %.2f · %s\n%s%s"
                row.Claim.Id
                row.Claim.Employee
                row.Claim.Purpose
                row.Claim.Amount
                (if row.Claim.HasReceipt then
                     "Receipt attached"
                 else
                     "Receipt missing")
                row.Decision
                (if row.Reason = "" then "" else ": " + row.Reason))
        |> Option.defaultValue "Select an expense claim"

    /// <summary>Amount of approved claims in the fixture currency.</summary>
    member _.ApprovedTotal = total.Value
    /// <summary>Most recent import, validation or save failure; empty after successful action.</summary>
    member _.Error = error.Value
    /// <summary>Current workflow command status.</summary>
    member _.Status = status.Value
    /// <summary>Whether decisions changed since the last acknowledged save revision.</summary>
    member _.IsDirty = dirty.Value
    /// <summary>Whether import is safe while preserving current unsaved decisions.</summary>
    member _.CanLoad = canLoad.Value
    /// <summary>Whether there is a changed snapshot ready to save.</summary>
    member _.CanSave = canSave.Value
    /// <summary>Whether undo history is available.</summary>
    member _.CanUndo = canUndo.Value
    /// <summary>Whether redo history is available.</summary>
    member _.CanRedo = canRedo.Value
    /// <summary>Whether the claim import command is in flight.</summary>
    member _.IsLoading = loading.Value
    /// <summary>Whether a captured decision snapshot is being written.</summary>
    member _.IsSaving = saving.Value
    /// <summary>Whether disposal cancelled this session's command lifetime.</summary>
    member _.IsDisposed = disposed
    /// <summary>Completion of commands started so far; graph hosts must still drain queued messages.</summary>
    member _.Completion = Task.WhenAll (tasks.ToArray ())

    /// <summary>Dispatches an import request when there are no unsaved decisions or pending commands.</summary>
    member _.Load() =
        dispatch Load

    /// <summary>Approves the selected claim when its receipt is attached.</summary>
    member _.Approve() =
        dispatch Approve

    /// <summary>Rejects the selected claim using the current nonblank reason.</summary>
    member _.Reject() =
        dispatch Reject

    /// <summary>Restores the previous decision batch, retaining selection and filter.</summary>
    member _.Undo() =
        dispatch Undo

    /// <summary>Reapplies the next decision batch after undo.</summary>
    member _.Redo() =
        dispatch Redo

    /// <summary>Dispatches a command that saves the current immutable decision snapshot.</summary>
    member _.Save() =
        dispatch Save

    /// <summary>Observes approved totals; unrelated navigation messages do not notify this reader.</summary>
    member _.ObserveApprovedTotal(callback: Action<decimal>) : IDisposable =
        graph.Run (fun () ->
            let child =
                createRoot (fun child ->
                    createEffect (fun () -> callback.Invoke total.Value)
                    child)

            owner.Attach child
            child :> IDisposable)

    /// <summary>Cancels owned commands and releases all selectors and observers.</summary>
    member _.Dispose() =
        if not disposed then
            disposed <- true
            lifetime.Cancel ()
            owner.Dispose ()
            let completion = Task.WhenAll (tasks.ToArray ())

            completion.ContinueWith ((fun (_: Task) -> lifetime.Dispose ()), TaskScheduler.Default)
            |> ignore

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>JSON claim import and atomic decision report output used by both runnable hosts.</summary>
[<AbstractClass; Sealed>]
type ExpenseFiles private () =
    static let json = JsonSerializerOptions (JsonSerializerDefaults.Web, WriteIndented = true)

    /// <summary>Reads a claim array from the supplied JSON file.</summary>
    static member LoadAsync(path: string, cancellation: CancellationToken) =
        task {
            let! content = File.ReadAllTextAsync (path, cancellation)
            return JsonSerializer.Deserialize<ExpenseClaim array> (content, json)
        }

    /// <summary>Replaces the output report after writing a complete snapshot in the destination directory.</summary>
    static member SaveAsync(path: string, rows: ExpenseDecision array, cancellation: CancellationToken) : Task =
        task {
            let destination = Path.GetFullPath path

            let temporary =
                destination
                + "."
                + Guid.NewGuid().ToString ("N")
                + ".tmp"

            try
                do! File.WriteAllTextAsync (temporary, JsonSerializer.Serialize (rows, json), cancellation)
                cancellation.ThrowIfCancellationRequested ()
                File.Move (temporary, destination, true)
            finally
                if File.Exists temporary then
                    File.Delete temporary
        }
