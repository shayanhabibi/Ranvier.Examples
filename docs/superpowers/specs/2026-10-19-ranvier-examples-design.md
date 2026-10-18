# Ranvier.Examples design

## Purpose and agreed scope

Create a separate solution addressing [Ranvier issue #63](https://github.com/shayanhabibi/Ranvier/issues/63): practical examples of reactive core workflows and UI workflows in both F# and C#.

The user approved four runnable examples: F# and C# console applications for order pricing and validation, and F# and C# Avalonia applications extending the same workflow with editable orders and reactive commands. The user explicitly requires their Partas project template and permits a Ranvier git submodule with direct source project references.

Success means a fresh recursive clone can build, test, and run all four examples without a Ranvier package release, external service, or credentials. Readers can follow inputs through reactive derivations to output and see how lifetime and batching work.

## Approach and alternatives

Use two independently implemented language-specific models, each shared by its console and desktop application. This keeps the F# examples idiomatic and lets C# readers learn through the C# facade. Keep their pricing rules and scenario expectations equivalent through tests.

A single F# model reused from C# would reduce duplication but would not demonstrate C# core usage. Four completely independent applications would repeat pricing and validation code within each language and make behavioral drift harder to detect. The chosen split keeps UI code out of the model while retaining useful language comparisons.

## Template and repository

Instantiate the installed `partas-fsharp` template as `Ranvier.Examples`, targeting .NET 10, with the script build CLI and Fantomas enabled. Disable packaging and Fable, and use Markdown documentation without a generated docs site. Retain the template's solution, conventions, tests, AI scaffolding, and CI, adapting their contents to the real examples. Remove generated greeting code and its tests once replaced.

The intended owned projects are:

- `src/Ranvier.Examples.FSharp`: reactive order model using the F# core API.
- `src/Ranvier.Examples.CSharp`: equivalent model using `Ranvier.CSharp`.
- `src/Ranvier.Examples.FSharp.Console`: scripted, readable walkthrough.
- `src/Ranvier.Examples.CSharp.Console`: equivalent scripted walkthrough.
- `src/Ranvier.Examples.FSharp.Desktop`: Avalonia controls and F# event/effect wiring.
- `src/Ranvier.Examples.CSharp.Desktop`: Avalonia XAML and reactive view-model bindings.
- `tests/Ranvier.Examples.FSharp.Tests`: F# model and lifecycle tests.
- `tests/Ranvier.Examples.CSharp.Tests`: equivalent C# tests.

Project filenames match their containing directories. Both console applications run a deterministic scenario and terminate successfully; neither requires interactive terminal input. Both desktop applications expose the same meaningful actions through controls.

## Ranvier dependency

Add `https://github.com/shayanhabibi/Ranvier.git` as the submodule at `external/Ranvier`. Pin the current upstream default-branch revision when implementing and record that exact revision in the README. Use a normal remote-backed submodule so other machines can initialize it; do not depend on another RiderProjects checkout or copy its uncommitted files.

The F# model references `external/Ranvier/src/Ranvier/Ranvier.fsproj`; the C# model references `external/Ranvier/src/Ranvier.CSharp/Ranvier.CSharp.fsproj`. The solution can include these dependency projects for navigation. Run/build selection must explicitly name the parent solution rather than discover the submodule's solution.

Restrict formatting, cleaning, example discovery, test discovery, and all modifications to parent-owned paths. Submodule dependency builds may produce their normal ignored build outputs. Do not format, clean, run the tests of, or patch Ranvier as a side effect of the parent CLI. Preserve Ranvier's own MSBuild settings, including its trace configuration.

Document recursive clone, initialization after an ordinary clone, and an explicit update-and-test procedure. CI uses recursive checkout. Package switching is outside this first implementation.

## Order workflow and rules

Use a small editable order with independently writable quantity, unit price, discount percentage, and delivery method. Include a delivery address field so switching between pickup and delivery demonstrates conditional dependencies. Use `decimal` for money.

Define the same rules in both languages:

- Quantity must be an integer from 1 through 1000.
- Unit price must be between 0.01 and 10000 inclusive.
- Discount percentage must be between 0 and 50 inclusive.
- Delivery requires a nonblank address; pickup does not.
- Subtotal is quantity multiplied by unit price. Discount is subtotal multiplied by the percentage divided by 100. Round monetary discount to two decimal places using `MidpointRounding.AwayFromZero`.
- Pickup costs zero. Delivery costs 7.50 unless the discounted subtotal is at least 100, in which case it is free.
- Total is subtotal minus the rounded discount plus delivery cost.

Defaults are quantity 2, unit price 25, discount 0, pickup, and a blank address. The default total is 50.00. Reset restores all defaults in one batch. Invalid inputs produce field errors, a non-submittable state, and an unavailable payable total instead of throwing or silently treating an invalid order as payable.

Signals hold inputs. Memos derive field validation, subtotal, discount, delivery cost, total, and submit eligibility. Derivation must read the address only for delivery. Observable effects and binding consumers keep the relevant derivations live. No host manually recomputes totals or raises derived-property notifications after edits.

Submitting a valid order records an in-memory confirmation containing a snapshot of its inputs and total. Later edits do not alter that confirmation. Invalid submission is guarded in the model as well as disabled in the UI. This example is synchronous; network submission, payment, persistence, and async command orchestration are out of scope.

## Console examples

Each program creates and activates its graph, constructs its language-specific model, and attaches a summary effect. It prints a short explanation alongside each scripted action: initial pricing, quantity change, discount, delivery with a missing address, corrected address, valid submission, and a batched reset.

Make reactive propagation visible through the resulting summaries. Use an observable summary depending on several reset fields so the reset scenario and test can show one final notification rather than intermediate states. Dispose the subscription/model and graph before exiting.

## Desktop examples

Use Avalonia Desktop on .NET 10. Choose and pin a currently supported stable Avalonia version during implementation, confirming its official documentation. Do not add a web frontend or another UI framework.

Present quantity, unit price, discount, pickup/delivery selection, address, field errors, pricing breakdown, latest confirmation, Reset, and Submit. Numeric input controls must prevent malformed numeric text from becoming unhandled exceptions; the model still validates allowed numeric ranges. Format money with two decimal places and label the values as demo currency amounts without assuming a particular country's tax rules.

The F# desktop app creates controls directly and demonstrates graph effects and event subscriptions. The C# desktop app uses the existing `Ranvier.CSharp` property bindings and reactive command adapters, with a thin view model around its model. Both use the same model commands and pricing rules as their console counterparts.

Create the graph on Avalonia's UI thread after the UI synchronization context is available. Keep node creation and mutations within the appropriate graph activation. Dispose view-owned adapters, subscriptions, and effects when the window closes, then dispose its model and graph. Do not leave a setup callback's short activation handle responsible for the application's lifetime.

## Build CLI, CI, and documentation

Keep the template's Partas.Build CLI as the entry point for build, test, and format. Adapt it to cover both `.fsproj` and `.csproj` projects explicitly if template discovery does not include both. A build must compile the desktop hosts as well as the models and console programs. Test must run both language suites; failures propagate as a nonzero exit code.

Provide discoverable run commands for all four applications through the build CLI and document their equivalent `dotnet run --project` commands. Scope Fantomas to owned F# files and use the SDK's C# formatter for owned C# files. CI performs recursive checkout, restores tools/dependencies, checks formatting, builds in Release, runs both suites, and smoke-runs both consoles. Desktop interaction is verified separately and is not implied by a successful compilation.

The README links issue #63 and maps each requirement to runnable projects. Include prerequisites, submodule setup, all four run commands, expected console results, a desktop walkthrough, and an explanation of graph ownership, conditional dependencies, batching, and differences between the two UI integration styles. Each model and app should be understandable without reading Ranvier internals.

## Acceptance and verification

Tests exercise behavior through each language's actual Ranvier model:

- Default pricing, edits, discount rounding, and the free-delivery threshold.
- Each invalid field, delivery address validation, and pickup removing that requirement.
- Reactive eligibility and summary changes without manually recalculating derived values.
- A batched reset delivers only the final coherent observed summary.
- Valid submission captures a stable snapshot; invalid submission produces no confirmation.
- Disposing the model/owner stops its observers while leaving a separately owned graph usable.
- C# binding property notifications and command eligibility follow model updates.

Use tests with meaningful outcome assertions rather than duplicating every internal formula in assertion helpers. Run the complete suites through the build CLI, and verify a Release build as well as console output. Inspect and exercise both desktop apps where the available UI tooling permits; otherwise report the exact manual checks that remain and do not claim interactive verification.

Implementation completion requires all four runnable hosts, tests for both models, functioning template-derived commands and CI configuration, a pinned submodule, and usable setup/walkthrough documentation. A scaffold or console-only subset does not complete issue #63's agreed scope.

## Scope boundaries

This repository teaches practical adoption. It does not add Ranvier APIs, benchmark frameworks, persistence, authentication, real order processing, or production payment integrations. It does not publish packages or create a hosted remote as part of initial implementation.
