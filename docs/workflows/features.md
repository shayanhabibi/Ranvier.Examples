# Reactive features and ownership

The examples deliberately spread useful patterns across complementary cores.

- **Signals and memoized derivations:** editable inputs, command availability, selected details, validation and summaries in all workflows.
- **Dynamic dependencies:** selected row/ticket and per-row validation subscribe to current inputs.
- **Drafts:** support notes/state and measurement raw values preserve local edits over refreshed upstream values.
- **Timing:** support query admission is debounced; file progress presentation is throttled. Neither mechanism owns cancellation of remote saves.
- **Asynchronous values and boundaries:** data calibration lookup exposes pending/failure states and suppresses old selections.
- **Query leases:** support search/detail acquisition, retained data, refresh status, stale invalidation and request races.
- **Mutations and reconciliation:** support session-owned saves capture identity/version and publish accepted receipts.
- **Keyed collections and projections:** file rows and measurements preserve stable identity; filtered lists and per-row captions feed observable collection adapters.
- **Incremental aggregates:** file counters/byte sums and measurement counts/mean reuse unaffected row calculations.
- **Batching:** queue admission, terminal publication, bulk conversion and source merges publish coherent state.
- **Graph dispatch:** worker and file-import results return to the owning graph thread; console runners explicitly pump it.
- **Bindings and commands:** the shared desktop host uses ReactiveBindings, tracked command eligibility and displayed command errors.
- **Owners and cleanup:** an editor owns its detail lease; the support session owns saves. Measurement rows own independent reactive roots. The workbench disposes views/models, cancels workers, awaits completion, stops HTTP and finally disposes its graph.
- **Elmish bridge:** expense review uses Mvu.withCmd, a pure immutable update function, decision history, revision-aware asynchronous commands and nested selectors. Its tests verify unchanged totals do not notify readers during navigation.

These are executable examples of core and UI behavior, not implementations of every Ranvier API. The original F#/C# order examples remain a smaller entry point for conditional dependencies, batches, bindings and effects.
