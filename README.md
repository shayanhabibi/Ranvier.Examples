# Ranvier.Examples

Real workflows for [Ranvier issue #63](https://github.com/shayanhabibi/Ranvier/issues/63), created from the Partas `partas-fsharp` solution template.

- **Support workbench:** search a local HTTP ticket service, edit drafts, reconcile server versions, recover from disconnection, and keep saves alive after leaving the editor.
- **File processing:** stream real files through SHA-256 with bounded concurrency, reactive keyed lists, cancellation, retry and throttled progress.
- **Data quality:** import a CSV, repair malformed measurements, inspect calibration certificates, convert units together, preserve drafts across reloads, and export valid rows.
- **Elmish expense review:** use an immutable model, messages, file commands and selectors to review claims with validation, undo/redo and revision-aware saves.
- **Shop incident replay:** persist a cart, capture overlapping async shipping requests in an untraced production build, then reproduce the stale-response bug in a traced development build with zero live service calls.

Both desktop launchers open the same four-tab workbench. The core examples use idiomatic F# and C#: [SupportDesk.fs](src/Ranvier.Examples.FSharp/SupportDesk.fs), [FileJobMonitor.cs](src/Ranvier.Examples.CSharp/Jobs/FileJobMonitor.cs), [DataQuality.fs](src/Ranvier.Examples.FSharp/DataQuality.fs), and [ExpenseReview.fs](src/Ranvier.Examples.FSharp/ExpenseReview.fs). The shared Avalonia host makes these models usable together; it does not duplicate each core in both languages.

## Setup

Install the .NET 10 SDK. Clone recursively, then build from the repository directory:

```shell
git clone --recurse-submodules https://github.com/shayanhabibi/Ranvier.Examples.git
cd Ranvier.Examples
dotnet fsi build.fsx -- build
dotnet fsi build.fsx -- test
```

After an ordinary clone, run `git submodule update --init --recursive`. Open [Ranvier.Examples.slnx](Ranvier.Examples.slnx) in Rider or another .NET IDE.

Ranvier is a source dependency at `external/Ranvier`, pinned to **6066b933bf220027e942751b05474a40d5c0414a**. Projects reference Ranvier, Ranvier.CSharp, Ranvier.Query, Ranvier.Elmish and Ranvier.Persistence directly. No Ranvier package release is required. The first restore needs internet access.

## Run

```shell
# Either launcher opens all four workflows
dotnet fsi build.fsx -- run-fsharp-desktop
dotnet fsi build.fsx -- run-csharp-desktop

# Real HTTP ticket scenario
dotnet fsi build.fsx -- run-fsharp-console

# Real file hashing, failure and retry
dotnet fsi build.fsx -- run-csharp-console

# CSV repair and export
dotnet fsi build.fsx -- run-data-console

# Elmish review, undo/redo and JSON report
dotnet fsi build.fsx -- run-elmish-console
```

Console scenarios verify their outcomes and fail on unexpected behavior. The support console uses an isolated temporary ticket store; the data console writes `repaired.csv` beside its executable. File hashing reads inputs without modifying them.

Desktop apps require a graphical session and Avalonia's [platform prerequisites](https://docs.avaloniaui.net/docs/supported-platforms). They use Avalonia Desktop **12.1.4**. Runtime files live under `LocalApplicationData/Ranvier.Examples/workflows`: the source CSV is copied there on first launch, exported data defaults to `repaired.csv`, and tickets persist under `support/tickets.json`. One service owns that store at a time; close the first workbench before launching another with the same directory.

## Explore the workflows

Read the hands-on guides:

- [Support: requests, drafts, conflicts and mutation ownership](docs/workflows/support.md)
- [Files: worker admission, graph dispatch and terminal events](docs/workflows/jobs.md)
- [Data: incremental projections, raw drafts and asynchronous selection](docs/workflows/data.md)
- [Elmish: immutable updates, commands, undo/redo and selectors](docs/workflows/elmish.md)
- [Shop: persisted production incidents and traced async replay](docs/workflows/shop-replay.md)
- [Reactive feature map and ownership](docs/workflows/features.md)
- [Repeated-interaction correctness review and regression coverage](docs/workflows/correctness-review.md)
- [Implementation and verification postmortem](docs/workflows/postmortem.md)

The fixtures in [samples](samples) include two repairable invalid readings and local calibration records. No credentials or external service setup are needed.

The original matching F#/C# order examples remain available as introductory material:

```shell
dotnet run --project src/Ranvier.Examples.FSharp.Console -- intro
dotnet run --project src/Ranvier.Examples.CSharp.Console -- intro
dotnet run --project src/Ranvier.Examples.FSharp.Desktop -- --intro
dotnet run --project src/Ranvier.Examples.CSharp.Desktop -- --intro
```

Their [introductory walkthrough](docs/walkthrough.md) covers signals, conditional dependencies, batching and accepted submission snapshots.

## Verify

```shell
dotnet fsi build.fsx -- build
dotnet fsi build.fsx -- test
dotnet fsi build.fsx -- format --dry-format
```

Release is the build CLI default; `--quick` skips restore. Formatting covers owned F#/C# files and excludes submodule/generated sources.

Tests cover persistence and version conflicts over real loopback HTTP, request races, navigation during saves, real file digests, worker limits, cancellation, retry, raw CSV validation, incremental row calculations, reload conflicts, exports, property bindings and actual headless Avalonia controls. Rendered previews are written under the C# test output directory. Headless checks do not establish native window-manager behavior on every OS.

CI checks formatting, builds the solution, runs both test suites and all console workflows following recursive checkout.

## Update Ranvier

Check out the desired revision inside `external/Ranvier`, run the checks and all console workflows above, then exercise the desktop workbench. Commit the changed gitlink with any adaptations and update the pinned revision in this README. Builds use the committed source revision and never update it automatically.

License: MIT.
