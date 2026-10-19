# Elmish expense review

Open either desktop app and select **Expense review / Elmish**, or run:

```shell
dotnet fsi build.fsx -- run-elmish-console
```

The workflow imports three expense claims from JSON. Approve Ada's train journey, select Grace's lunch and try approval: a missing receipt prevents it. Enter a rejection reason and reject instead. Undo and redo decision changes, then save the report. The sample uses one fixture currency.

The desktop copies `samples/expenses.json` to `expense-claims.json` in its runtime directory on first launch. The tab displays both that input path and the output `expense-decisions.json` path. Edit the input file externally and reload when the current review has been saved. Reload imports a fresh pending batch; the output is a decision report, rather than a session-restoration file.

The console imports the bundled JSON, verifies receipt validation and decision history, and writes `expense-decisions.json` beside its executable.

## Model, messages and update

[ExpenseReview.fs](../../src/Ranvier.Examples.FSharp/ExpenseReview.fs) contains an immutable `ExpenseState` and a discriminated union of inputs and command completions:

- Selection, filter and reason-input messages update navigation and form state.
- Approve and Reject stage decisions, validating receipt/reason requirements.
- Undo and Redo restore previous decision batches while retaining navigation.
- Load/Loaded and Save/Saved describe the asynchronous command lifecycle.

`ExpenseUpdate.update` is pure. It returns a new model and effect descriptions, such as `WriteDecisions(revision, snapshot)`. It performs no file access.

The adapter translates these descriptions into Elmish-compatible command subscriptions and starts the loop with **Ranvier.Elmish.Mvu.withCmd**. Each command receives dispatch; completion messages return through the owning graph. The bridge is referenced directly from the pinned source submodule. Its command shape is compatible with Elmish's `Cmd`; the example does not need a separate Elmish package.

## Commands and revisions

Save captures an immutable decision array and its revision. You can continue reviewing during a write. Success acknowledges only the captured revision, so later changes remain dirty. Errors retain staged decisions and enable retry. Reload is blocked while there are unsaved decisions or pending commands.

The JSON writer uses a unique temporary file in the destination directory, then replaces the report after the full write succeeds. Disposing the review cancels its file commands, prevents late publication and releases its reactive owner.

## Selectors and views

The adapter uses `app.Select` for rows, selection, filter, status and eligibility. Nested memos derive visible claims, selected details and approved totals from these narrow sections. The UI binds those values through the existing ReactiveBindings layer.

Selecting a different claim changes the immutable root model. Observed root selectors still evaluate their comparisons; unchanged selected values stop downstream propagation. The approved-total observer therefore stays quiet during navigation. This is selector isolation, not a claim that immutable MVU updates have the same cost as independent field signals.

Tests cover validation, undo/redo, selector notifications, real JSON input/output, malformed imports, failed writes, edits during a pending save, disposal and actual keyboard-invoked desktop actions.
