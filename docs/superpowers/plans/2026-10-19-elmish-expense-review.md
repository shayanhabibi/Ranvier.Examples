# Elmish expense review

Extend the existing examples with a fourth desktop workflow and a console scenario using the pinned Ranvier.Elmish source project.

Design: an immutable F# model and explicit messages own imported expense claims, selection/filter, review decisions, undo/redo and asynchronous load/save state. A pure transition returns effect descriptions; the Mvu.withCmd adapter interprets these as command subscriptions. Selectors expose narrow sections to the existing Avalonia binding layer. Real JSON input and atomic JSON report output keep the example grounded in local work.

Rules: approval requires a receipt; rejection requires a reason. Decision changes are undoable. Selection/filter changes do not alter decision history. Failed writes preserve decisions and permit retry. A successful asynchronous save acknowledges its captured revision, so decisions changed during that save stay dirty. Reload is blocked while unsaved decisions exist or work is in flight. Disposal cancels owned I/O and prevents late publication. This is a review example with one fixture currency, not an accounting or payment system.

Implementation (native execution, continuing the existing authorization):

1. Add failing tests for receipt/reason validation, undo/redo, narrow selector notification, real file roundtrip, failed-save retry, captured-save revision and disposed command completion.
2. Add ExpenseReview.fs and Ranvier.Elmish project reference. Probe pure transitions and bridge types in SageFs; verify persisted implementation and run focused tests.
3. Add ExpenseReviewView.cs, sample JSON, a fourth workbench tab and the F# elmish console/Partas run-elmish-console command. Test actual selection/edit/buttons in the headless window.
4. Document update/commands/selectors and their costs. Run full Release build/tests, every console scenario, formatting, semantic/XML/comment checks. Obtain one fresh whole-branch review, correct important findings, then publish under existing repository authorization.

Decision: use Ranvier.Elmish's native Elmish-compatible command shape without adding the separate Elmish NuGet package; this showcases the source bridge already supplied by the pinned dependency. Cost if a comparison with the standalone Elmish runtime is wanted: add a second host.

Completed: all four tasks. Core tests failed on missing types before implementation, pure transitions and selector isolation passed in SageFs, then five core acceptance tests and the actual-button headless journey passed. A headless keyboard failure was traced to zero layout bounds on the newly selected tab; settling layout fixed the test harness. The final Release build has no warnings/errors, and all 78 tests pass. Independent whole-branch review reported no critical/important findings. Details are recorded in docs/workflows/verification.md.
