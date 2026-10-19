# Real reactive workflows

The user approved building the three research-backed workflows: a support workbench, a real local file-processing monitor, and a data-quality editor. This replaces the calculator as the repository's main demonstration while retaining it as an introductory sample. Existing Partas tooling, pinned Ranvier source submodule, .NET 10, F#/C# and Avalonia constraints remain.

## Structure and decisions

Implement support/query and data-repair models in the existing F# library, and local service/contracts and file jobs in the C# library. Add a shared C# Avalonia workflow host referenced by both existing desktop launchers. This avoids duplicating domains merely to change languages. F# demonstrates native queries, drafts, async derivations and per-row dependency tracking; C# demonstrates background dispatch, keyed collections and reactive bindings/commands. Existing language-specific introductory UIs remain runnable.

The support transport is a loopback-only ASP.NET Core HTTP service with JSON-file persistence and a typed HTTP client. No external accounts, ports fixed in advance, or credentials are needed. Sample data is copied to the output, while mutable state lives in a separate writable demo directory. Tests use isolated temporary directories.

## Support workbench

A searchable ticket queue, selected detail/editor, server notes versus draft notes, dirty/conflict state, refresh, save and Back. Ranvier.Query owns accepted remote data and refresh/error metadata; page/editor scopes own detail leases and drafts; session owns the query client and saves. Debounce admits search terms; cancellation retires obsolete work. Saves capture a versioned draft, perform one HTTP mutation, reconcile retained queries, and invalidate filtered search results explicitly before ensuring the visible queue. A draft survives a refresh. An HTTP version conflict retains the draft and explains recovery. Closing the editor releases its draft and read lease; an already submitted save completes and updates the retained queue. Session disposal cancels its pending work. The server supports explicit unavailable/recovery controls for exercising real HTTP failures, not artificial progress ticks.

## File-processing monitor

Queue actual local files, compute streaming SHA-256 with bounded concurrency, publish progress/bytes and terminal results. Per-job keyed state feeds filtered/sorted collection projections and aggregates. Row observers are incremental and stable by ID. Selection has an independent detail consumer. Background workers use Graph.Dispatch; console hosts pump the owning graph, UI hosts capture the UI context. Cancel is cooperative, every job reaches a terminal state, and throttled summary presentation does not drop required terminal transitions. Failed jobs expose readable errors and can be retried. Window/session disposal cancels work and detaches observers. Arbitrary input files are opened read-only.

## Data-quality editor

Read a typed CSV fixture or supplied file, retain raw numeric input for repair, validate identifier/value/unit, normalize units, filter invalid/included rows and maintain aggregates. Each row owns writable fields/drafts and derived validation/calibration. Keyed projections feed a grid and selected-row editor. Bulk unit conversion or exclusions use one meaningful batch; counters demonstrate unaffected rows retaining cached derivations. Export writes only a chosen destination after successful validation. Refresh updates upstream values without discarding local drafts, flags conflicts and offers accept-upstream/reset. Superseded selected-row async lookups cannot replace the new selection. Invalid text remains visible and fixable rather than silently becoming an old value.

## Acceptance

Test races with controllable asynchronous services rather than wall-clock sleeps. Integration-test the real loopback API and persistence, actual hashing/cancellation/errors, CSV parse/repair/export and per-row propagation. Add headless tests for all three live workflow views, exercise commands and navigation, render and inspect screenshots. Existing 51 introductory tests remain green. Verify Release build, full test suites, formatting, F# semantic/docs checks, console scenarios and a fresh recursive checkout; dependency sources stay clean. Document a feature-to-story map and concrete failure/recovery walkthroughs.

Implementation proceeds with the user's existing native execution choice. No additional design checkpoint is required by the user's direct instruction to build the researched workflows.
