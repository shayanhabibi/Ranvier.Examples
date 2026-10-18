# Ranvier workflow examples implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver four runnable order-workflow examples covering reactive core and Avalonia UI integration in F# and C# for Ranvier issue #63.

**Architecture:** Two independent reactive models share a behavioral contract, each consumed by a console and desktop host. All Ranvier dependencies are direct project references into a pinned git submodule. The Partas template provides the parent solution, build CLI, test infrastructure, formatting conventions, and CI.

**Tech Stack:** .NET 10, F#, C#, Ranvier and Ranvier.CSharp source projects, Partas.Build, Fantomas, Expecto, xUnit, Avalonia Desktop and Fluent theme.

**Spec:** `docs/superpowers/specs/2026-10-19-ranvier-examples-design.md`

## Global constraints

- Instantiate the installed `partas-fsharp` template as `Ranvier.Examples`, targeting .NET 10, with the script build CLI and Fantomas enabled.
- Disable packaging and Fable, and use Markdown documentation without a generated docs site.
- Retain the template's solution, conventions, tests, AI scaffolding, and CI, adapting their contents to the real examples.
- Add `https://github.com/shayanhabibi/Ranvier.git` as the submodule at `external/Ranvier`.
- Restrict formatting, cleaning, example discovery, test discovery, and all modifications to parent-owned paths.
- Use `decimal` for money.
- Quantity must be an integer from 1 through 1000.
- Unit price must be between 0.01 and 10000 inclusive.
- Discount percentage must be between 0 and 50 inclusive.
- Delivery requires a nonblank address; pickup does not.
- Round monetary discount to two decimal places using `MidpointRounding.AwayFromZero`.
- Pickup costs zero. Delivery costs 7.50 unless the discounted subtotal is at least 100, in which case it is free.
- Defaults are quantity 2, unit price 25, discount 0, pickup, and a blank address. The default total is 50.00.
- Invalid inputs produce field errors, a non-submittable state, and an unavailable payable total instead of throwing or silently treating an invalid order as payable.
- This example is synchronous; network submission, payment, persistence, and async command orchestration are out of scope.
- Prefix every shell command with `rtk`; commands below follow this convention. Run them from the new repository root unless a working directory is stated.

## Review focus

1. Numeric editors cleared or containing malformed text must not leave a valid stale order submittable; Task 5 and Task 6 exercise editor clearing and correction.
2. Money near a rounding or free-delivery boundary must produce the same outcome in both languages; Tasks 2 and 3 pin exact decimal examples.
3. Changing the address while pickup is selected must not rerun address-dependent validation; Tasks 2 and 3 observe that derived validation stays unchanged until delivery is enabled.
4. Closing a window must release subscriptions without disposing a separately owned graph; Tasks 2, 3, 5, and 6 check ownership and exit behavior.
5. Repository operations must work from a fresh recursive clone and must not modify dependency sources; Task 7 checks setup, CLI targets, and submodule status.

## File and interface map

- `build.fsx`: template-derived restore, build, test, format, and four run commands.
- `Directory.Build.props`, `.editorconfig`, `.config/dotnet-tools.json`, `global.json`: retain template policies; add C# formatting and pinned UI package versions as needed.
- `Ranvier.Examples.slnx`: exactly one parent solution, with eight owned projects and the two Ranvier dependency projects.
- `src/Ranvier.Examples.FSharp/Order.fs`: domain records, model signals/memos, root lifetime, reset/submit/observe.
- `src/Ranvier.Examples.CSharp/Order.cs`, `OrderModel.cs`: corresponding independent C# implementation.
- `src/Ranvier.Examples.FSharp.Console/Program.fs`, `src/Ranvier.Examples.CSharp.Console/Program.cs`: deterministic explained scenarios.
- `src/Ranvier.Examples.FSharp.Desktop/Program.fs`, `App.fs`, `OrderWindow.fs`: startup, application lifetime, code-created controls and effects.
- `src/Ranvier.Examples.CSharp.Desktop/Program.cs`, `App.axaml`, `App.axaml.cs`, `OrderViewModel.cs`, `OrderWindow.axaml`, `OrderWindow.axaml.cs`: startup, XAML host, Ranvier binding adapter.
- `tests/Ranvier.Examples.FSharp.Tests/OrderTests.fs`, `Program.fs`: template-compatible Expecto suite.
- `tests/Ranvier.Examples.CSharp.Tests/OrderModelTests.cs`, `OrderViewModelTests.cs`: xUnit model and binding contract tests.
- `.github/workflows/pushmaster.yml`: adapt the generated workflow, preserving template naming unless its actual installed version differs.
- `README.md`, `docs/walkthrough.md`: setup, commands, workflow explanation, verification limitations.

Both models take an existing `Graph` and own a nested `Owner`. Hosts own and dispose the graph. Their public input nodes are `Quantity: Signal<int>`, `UnitPrice: Signal<decimal>`, `DiscountPercent: Signal<decimal>`, `Delivery: Signal<bool>`, and `Address: Signal<string>`. Computed values are `Errors` (immutable/read-only field errors), `Subtotal`, `Discount`, `DeliveryCost`, `Total` (F# `Memo<decimal option>`, C# `Memo<decimal?>`), `CanSubmit: Memo<bool>`, and `Summary: Memo<string>`. Use an additional computed address error to make conditional reads explicit.

`OrderConfirmation` is immutable and contains quantity, unit price, discount percentage, delivery selection, address, and total. `Confirmation` is a signal holding an optional/nullable immutable confirmation. `Reset(): unit/void` batches input writes and clears the confirmation. `Submit(): bool` checks `CanSubmit`, captures a confirmation when valid, and returns false without changing the confirmation when invalid. `ObserveSummary(callback): IDisposable` creates an owned effect and disposes only that subscription. `Dispose()` releases the model root and all remaining model observers.

Construct the model root while the supplied graph is active. `createRoot`/`Reactive.Root` returns the callback's result: return both owner and nodes from the callback, rather than assuming the factory automatically returns a disposal handle. Confirm the C# facade's exact root and effect overloads against the pinned source through F# semantic tooling before writing code. Keep all subsequent API choices consistent with this contract.

### Task 1: Runnable template foundation with source dependency

**Files:** generated template files, `.gitmodules`, `external/Ranvier`, both model project files, both test project files, `Ranvier.Examples.slnx`, `build.fsx`.

**Consumes:** the approved spec and existing design commit. **Produces:** a restored template-derived solution with both language test runners and a real pinned Ranvier submodule.

- [ ] Verify the new repository is clean. Work in this new repository; it is separate from the existing Ranvier checkout. Read generated `AGENTS.md` and linked F# skills before source changes. Inspect availability of its required comment-hygiene, semantic, and REPL tools; obey explicit setup permission rules if any are missing.
- [ ] Instantiate without overwriting the already committed design and plan. Preview first:

```powershell
rtk proxy dotnet new partas-fsharp -n Ranvier.Examples --output . --owner shayanhabibi --framework net10.0 --sdk 10.0 --packaging false --fable false --docs none --build-script true --fantomas true --no-restore --dry-run
rtk proxy dotnet new partas-fsharp -n Ranvier.Examples --output . --owner shayanhabibi --framework net10.0 --sdk 10.0 --packaging false --fable false --docs none --build-script true --fantomas true --no-restore
rtk git submodule add https://github.com/shayanhabibi/Ranvier.git external/Ranvier
rtk git -C external/Ranvier rev-parse HEAD
```

- [ ] Read the actual generated test project and build CLI. Retain their dependency versions and test runner configuration for F#. Create the C# library and xUnit test project targeting `net10.0`; pin all package versions. The model project references are:

```xml
<!-- src/Ranvier.Examples.FSharp/Ranvier.Examples.FSharp.fsproj -->
<ProjectReference Include="../../external/Ranvier/src/Ranvier/Ranvier.fsproj" />
<!-- src/Ranvier.Examples.CSharp/Ranvier.Examples.CSharp.csproj -->
<ProjectReference Include="../../external/Ranvier/src/Ranvier.CSharp/Ranvier.CSharp.fsproj" />
```

- [ ] Adapt build discovery to owned `src` and `tests` projects, including `.csproj`. Exclude `external` from format and clean globs before running either command. Explicitly restore `Ranvier.Examples.slnx`; do not auto-select the dependency solution. Remove generated greeting projects/tests after the new model suites are registered.
- [ ] Restore and run one F# and one C# smoke test that creates a graph, creates a signal inside an activation, updates it, and asserts the new value. Use this meaningful baseline before introducing pricing behavior. Run:

```powershell
rtk proxy dotnet fsi build.fsx -- build
rtk proxy dotnet fsi build.fsx -- test
rtk git -C external/Ranvier status --short
```

Expected: both language projects build, both runners report their smoke test, dependency working tree is clean. Commit the foundation with `chore: initialize Partas solution and Ranvier submodule`.

### Task 2: F# order model and behavioral tests

**Files:** `src/Ranvier.Examples.FSharp/Order.fs`, its `.fsproj`; `tests/Ranvier.Examples.FSharp.Tests/OrderTests.fs`, `Program.fs`, its `.fsproj`.

**Consumes:** Ranvier core and template test runner. **Produces:** the F# model interface defined above, available to F# console and desktop hosts.

- [ ] Read the installed/copied F# XML documentation and SageFs skills. Use semantic tooling or the supported REPL for overload and type questions. Write tests before implementation; initially verify missing model symbols fail compilation. Core assertion shape:

```fsharp
testCase "default order costs 50" <| fun _ ->
    use graph = new Graph ()
    use active = graph.Activate ()
    use order = new OrderModel (graph)
    Expect.equal order.Total.Value (Some 50m) "payable total"
    Expect.isTrue order.CanSubmit.Value "default order can submit"
```

- [ ] Add parameterized field cases: quantity `0`, `1001`; price `0m`, `10000.01m`; discount `-1m`, `50.01m`; delivery address `""`, `"   "`. Assert an error for the relevant field, `None` total, false eligibility, and false submission. Add inclusive boundary cases `1`, `1000`, `0.01m`, `10000m`, `0m`, `50m`.
- [ ] Implement independent signals and memos using the exact pricing rules. The rounding expression is:

```fsharp
Decimal.Round (subtotal * discountPercent / 100m, 2, MidpointRounding.AwayFromZero)
```

Use an address-validation memo whose body first reads `Delivery.Value` and reads `Address.Value` only in the delivery branch. Aggregate validation errors as values rather than exceptions. Gate payable total on all validation errors being absent.
- [ ] Add exact pricing assertions: quantity 1, price 0.05, discount 10 gives discount 0.01 and total 0.04; delivered subtotal 99.99 costs 107.49, delivered subtotal 100 costs 100; price 100 with discount 10 and delivery costs 97.50. Set a valid address for all delivery pricing tests.
- [ ] Add a subscription test, change quantity and delivery, then call reset. Assert the observed count increases by exactly one for reset, the last total is 50, and no summary shows an intermediate reset combination. Editing a pickup address must leave the address-error observer's notification count unchanged; enabling delivery must evaluate the current address.
- [ ] Test submission snapshots: submit at 50, change quantity to 3, assert confirmation remains 50 and quantity 2. Test invalid submission preserving the previous confirmation. Reset clears it.
- [ ] Test `ObserveSummary` disposal stopping callbacks. Create an unrelated signal on the same graph, dispose the model, then mutate/read that unrelated signal successfully. Make model disposal idempotent.
- [ ] Run the F# suite red/green per behavior group, then the complete build CLI test command. Run the XML doc audit where required by project guidance. Commit `feat(fsharp): add reactive order pricing model`.

### Task 3: Independent C# order model and parity tests

**Files:** `src/Ranvier.Examples.CSharp/Order.cs`, `OrderModel.cs`; `tests/Ranvier.Examples.CSharp.Tests/OrderModelTests.cs` and project references.

**Consumes:** Ranvier.CSharp facade, Task 1 test runner, shared behavioral contract. **Produces:** C# model exposing the signals, memos, methods, and snapshot described above.

- [ ] Write and run the default test before implementation:

```csharp
[Fact]
public void DefaultOrderCosts50()
{
    using var graph = new Graph();
    using var active = graph.Activate();
    using var order = new OrderModel(graph);
    Assert.Equal(50m, order.Total.Value);
    Assert.True(order.CanSubmit.Value);
}
```

- [ ] Implement immutable confirmation/error records and model-owned root using `Signal`, `Memo`, `Effect`, and `Batch` from the facade. Use the same decimal limits and `decimal.Round(subtotal * percent / 100m, 2, MidpointRounding.AwayFromZero)` calculation. Never reference the F# example model from this project.
- [ ] Add xUnit theories for quantity `0`, `1001`; price `0`, `10000.01`; discount `-1`, `50.01`; whitespace delivery addresses; and inclusive valid boundaries. Assert field error, null payable total, false eligibility, and rejected invalid submission.
- [ ] Pin the same pricing cases: 0.05 at 10% gives 0.01 discount and 0.04 total; valid delivered subtotals 99.99 and 100 give 107.49 and 100; 100 discounted 10% with delivery gives 97.50.
- [ ] Test one reset notification, conditional address-error observation, immutable submission snapshot, rejected invalid submission preserving confirmation, reset clearing confirmation, subscription disposal, idempotent model disposal, and a surviving unrelated graph signal. Run both language suites through the CLI. Commit `feat(csharp): add reactive order pricing model`.

### Task 4: Both console walkthroughs

**Files:** both console projects and `Program.fs`/`Program.cs`, solution entries, run command registration in `build.fsx`.

**Consumes:** each language's `OrderModel`. **Produces:** `run-fsharp-console` and `run-csharp-console` build CLI commands and deterministic executable walkthroughs.

- [ ] Add an F# console project referencing only its F# model and a C# console project referencing only its C# model. Each activates a graph, constructs the model, and subscribes to `Summary`. Print a numbered action label before each mutation.
- [ ] Implement the same scenario in both entry points:

```text
1. Initial: quantity 2, unit price 25, pickup => 50.00
2. Set quantity 3 => 75.00
3. Set discount 10 => 67.50
4. Select delivery with blank address => total unavailable, submit disabled
5. Set address to 42 Example Street => 75.00
6. Submit => confirmation total 75.00
7. Reset => 50.00, confirmation cleared, one coherent summary notification
```

- [ ] Format decimals with invariant two-decimal output for smoke assertions. Use disposal scopes for observer, model, graph activation, and graph. Register run commands with explicit project paths, forwarding configuration from the CLI.
- [ ] Run each command and check the ordered outputs against the scenario above. A mismatch is a failed smoke check; do not merely check exit status. Commit `feat(examples): add F# and C# console walkthroughs`.

### Task 5: F# Avalonia editor

**Files:** F# desktop project, `Program.fs`, `App.fs`, `OrderWindow.fs`, solution entry, run command.

**Consumes:** F# `OrderModel`. **Produces:** `run-fsharp-desktop`, a code-created Avalonia window demonstrating effects and event cleanup.

- [ ] Confirm current stable Avalonia packages against NuGet and official docs; pin one exact version for all UI packages across both desktop projects. Official code-only guidance: https://docs.avaloniaui.net/docs/fundamentals/coded-ui and https://docs.avaloniaui.net/docs/how-to/coded-ui-how-to . Avoid floating versions and extra MVVM packages.
- [ ] Create the host with this startup shape, adapting syntax only as required by the installed Avalonia version:

```fsharp
[<EntryPoint; STAThread>]
let main args =
    AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)
```

- [ ] Construct a Fluent-themed window after Avalonia installs its UI context. Create the graph and model there. Use numeric controls for quantity/price/discount, a delivery checkbox, an address textbox, pricing/error text blocks, and Reset/Submit buttons. UI writes go through the active graph; effects update displayed totals, errors, confirmation, and Submit enabled state.
- [ ] Provide explicit handling for an empty numeric editor. Either disallow empty commit and visibly restore the last valid numeric value, or keep a local editor-error state that disables submission until corrected. Do not allow silent submission using a stale displayed field. Quantity must reject fractional values. Show demo currency amounts with two decimal places and no implied tax calculation.
- [ ] Wire Reset so controls reflect model defaults without event-feedback loops. Register event subscription cleanup, dispose UI owner and model when the window closes, then the graph. Scope temporary activation handles to each setup or event operation.
- [ ] Build in Release and exercise: edit quantity to 3; apply discount 10; choose delivery and observe missing-address error; correct address; submit at 75.00; reset to 50.00; clear/recover a numeric editor; close/reopen with no orphan callbacks. Use available UI tooling; document any checks that cannot actually be performed. Commit `feat(fsharp): add Avalonia order editor`.

### Task 6: C# Avalonia editor and reactive binding tests

**Files:** C# desktop project, `Program.cs`, `App.axaml`, `App.axaml.cs`, `OrderViewModel.cs`, `OrderWindow.axaml`, `OrderWindow.axaml.cs`; `tests/Ranvier.Examples.CSharp.Tests/OrderViewModelTests.cs`; solution entry, run command.

**Consumes:** C# `OrderModel`, pinned Avalonia packages. **Produces:** `run-csharp-desktop`, XAML editor, and tests proving notification and command behavior.

- [ ] Create `OrderViewModel : ReactiveObject` accepting `(Graph graph, OrderModel order)`. Register existing input signals with `Bindings.Writable`; use `Bindings.Computed` for summary, errors, breakdown, and confirmation. Keep the model separately owned by the host. Expose `ReactiveCommand Reset` and `ReactiveCommand Submit`; Submit's predicate reads `order.CanSubmit.Value` plus editor validity if needed.
- [ ] Write a failing binding test: subscribe to `PropertyChanged`, change quantity through the view-model property, and assert the computed total notification and value 75. Test Submit `CanExecute(null)` changing from true to false after delivery is selected with no address, and true after correction. Verify reset updates writable properties through notifications.
- [ ] Implement command registration following the pinned facade signatures. The intended synchronous shape is:

```csharp
Reset = Bindings.Command(_ => order.Reset());
Submit = Bindings.Command(_ => { order.Submit(); }, () => order.CanSubmit.Value);
```

Use the actual `Writable` overload accepting an existing signal; confirm its signature before coding. Do not mirror every model signal into a second independently writable signal or manually raise derived property notifications.
- [ ] Bind XAML controls to view-model properties and commands; display field error values without throwing validation exceptions from model memos. Match the F# numeric-clearing behavior and demo money formatting. Subscribe to binding events only after the UI synchronization context is established. Dispose view model, model, and graph on close.
- [ ] Test view-model disposal stops property notifications while the separately owned model remains usable. Build in Release, run all C# tests, and exercise the same seven desktop steps plus numeric clearing/recovery. Commit `feat(csharp): add Avalonia reactive bindings example`.

### Task 7: Repeatable build, CI, walkthrough, and final acceptance

**Files:** `build.fsx`, generated CI workflow, `.editorconfig`, `README.md`, `docs/walkthrough.md`, any owned project configuration required by verified findings.

**Consumes:** all four hosts and both suites. **Produces:** a recursive-clone-ready, documented solution with a single build/test interface.

- [ ] Finish CLI commands `build`, `test`, `format`, `run-fsharp-console`, `run-csharp-console`, `run-fsharp-desktop`, `run-csharp-desktop`. `format --dry-format` checks owned F# using Fantomas and owned C# using `dotnet format` with explicit owned includes; exclude `external`, `obj`, and `bin`. Run C# formatting once for the parent solution rather than once per source file. Build compiles all owned hosts; test runs both suites and propagates either runner's failure status.
- [ ] Adapt CI to recursive checkout, SDK setup from `global.json`, tool restore, format checking, Release build, both test suites, and deterministic console smoke checks. Remove template package-publish operations. Run desktop compilation on CI without claiming this is an interactive UI test.
- [ ] Write README setup and run commands, link issue #63, map its four requirements to projects, and record `git -C external/Ranvier rev-parse HEAD`. Document dependency updates as an explicit submodule checkout/update followed by the complete validation commands and committing the changed gitlink; never silently follow a moving branch at build time. Include:

```powershell
rtk git submodule update --init --recursive
rtk proxy dotnet fsi build.fsx -- build
rtk proxy dotnet fsi build.fsx -- test
rtk proxy dotnet fsi build.fsx -- format --dry-format
rtk proxy dotnet fsi build.fsx -- run-fsharp-console
rtk proxy dotnet fsi build.fsx -- run-csharp-console
rtk proxy dotnet fsi build.fsx -- run-fsharp-desktop
rtk proxy dotnet fsi build.fsx -- run-csharp-desktop
```

Also show standard unwrapped `dotnet` equivalents for readers without RTK, prerequisites for desktop platforms, and the exact expected console scenario. `docs/walkthrough.md` explains how each language expresses signals, memos, conditional reads, batched reset, confirmation snapshots, and ownership; distinguish F# effects from C# reactive property/command adapters.
- [ ] Run complete Release build/tests and formatting checks, then both console scenario checks. Verify `git -C external/Ranvier status --short` is empty and the parent contains a gitlink rather than copied dependency files. Review all tracked changes against the spec and resolve concrete issues before repeating only affected verification.
- [ ] Validate recursive-clone setup in a temporary workspace under `C:/Users/shaya/RiderProjects` or an allowed temporary directory. Clone from this repository with dependency resolution using the submodule's public remote, restore/build/test, and smoke-run both consoles. Do not delete unrelated directories or rely on an absolute path back into the original Ranvier checkout.
- [ ] Record tests actually run and remaining UI limitations. Commit `docs(ci): document and verify reactive workflow examples`. The final response links the solution and README, gives run commands, reports verification evidence, and clearly states any interactive checks still outstanding.

## Plan self-review

All four issue requirements map to Tasks 2 through 6. Template setup and submodule pinning are in Task 1; owned-only operations, CI, documentation, and recursive-clone checks are in Tasks 1 and 7. Field boundaries, rounding, delivery threshold, snapshots, batching, conditional validation, and ownership have explicit tests in Tasks 2 and 3. C# property and command adapters have dedicated tests in Task 6. Numeric editor clearing and real window lifetime are explicit desktop checks in Tasks 5 and 6. All dependent host code consumes the model names and methods defined in the interface map.
