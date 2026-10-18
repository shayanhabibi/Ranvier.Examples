# Ranvier.Examples

Four practical order workflows for [Ranvier issue #63](https://github.com/shayanhabibi/Ranvier/issues/63), created with the Partas `partas-fsharp` solution template.

- **F# core:** [reactive model](src/Ranvier.Examples.FSharp/Order.fs) and [console walkthrough](src/Ranvier.Examples.FSharp.Console/Program.fs).
- **C# core:** [independent reactive model](src/Ranvier.Examples.CSharp/OrderModel.cs) and [console walkthrough](src/Ranvier.Examples.CSharp.Console/Program.cs).
- **F# UI:** [Avalonia editor](src/Ranvier.Examples.FSharp.Desktop/OrderWindow.fs), using controls, effects and disposable event subscriptions.
- **C# UI:** [Avalonia editor](src/Ranvier.Examples.CSharp.Desktop/OrderWindow.axaml) and [view model](src/Ranvier.Examples.CSharp.Desktop/OrderViewModel.cs), using reactive property bindings and commands.

Each language's console and desktop app share its model. The two models implement the same rules independently so readers can learn either API directly.

## Setup

Install the .NET 10 SDK. Run commands from this directory; `global.json` selects a compatible .NET 10 SDK.

When cloning this repository, use Git's `--recurse-submodules` option. After an ordinary clone, initialize the dependency:

```shell
git submodule update --init --recursive
dotnet fsi build.fsx -- build
dotnet fsi build.fsx -- test
```

The build CLI restores solution dependencies and the local Fantomas tool. It requires internet access on the first run. Open [Ranvier.Examples.slnx](Ranvier.Examples.slnx) in Rider or another .NET IDE.

Ranvier is a source dependency at `external/Ranvier`, pinned to **305d00908d3f7cfbc015bd3df051c650886377dc**. The F# model references `Ranvier.fsproj`; the C# model references `Ranvier.CSharp.fsproj`. A Ranvier NuGet release is not required.

## Run the examples

```shell
dotnet fsi build.fsx -- run-fsharp-console
dotnet fsi build.fsx -- run-csharp-console
dotnet fsi build.fsx -- run-fsharp-desktop
dotnet fsi build.fsx -- run-csharp-desktop
```

Equivalent direct commands:

```shell
dotnet run --project src/Ranvier.Examples.FSharp.Console -c Release
dotnet run --project src/Ranvier.Examples.CSharp.Console -c Release
dotnet run --project src/Ranvier.Examples.FSharp.Desktop -c Release
dotnet run --project src/Ranvier.Examples.CSharp.Desktop -c Release
```

Console applications execute a deterministic scenario and exit. Both report totals of **50.00 → 75.00 → 67.50 → unavailable → 75.00**, submit a **75.00** snapshot, reset to **50.00**, and print `Walkthrough passed.` They check their outcomes and exit unsuccessfully if the behavior differs.

Desktop applications require a graphical Windows, macOS or Linux session and Avalonia's [platform prerequisites](https://docs.avaloniaui.net/docs/supported-platforms). They use Avalonia Desktop **12.1.4**. The automated headless tests run without a display server.

## Try the desktop workflow

1. Start at quantity 2, unit price 25, pickup: total **50.00**.
2. Set quantity to 3 and discount to 10: total **67.50**.
3. Select delivery. The blank address makes the payable total unavailable and disables Submit.
4. Enter `42 Example Street`: total **75.00**, including delivery.
5. Submit. The accepted snapshot stays fixed while you edit the current order.
6. Reset. All fields return to their defaults together, and the confirmation clears.
7. Clear a numeric field or enter malformed text. The order becomes invalid; correcting the value or resetting restores eligibility.

All amounts are demo currency values. Quantity must be 1–1000, unit price 0.01–10000, and discount 0–50%. Discount rounds to two places away from zero. Delivery costs 7.50, becoming free at a discounted subtotal of 100; pickup is free. This is an in-memory demonstration with no tax calculation, external service, payment, or persistence.

Read [the walkthrough](docs/walkthrough.md) for graph setup, dynamic dependencies, batching and ownership.

## Build and verify

```shell
dotnet fsi build.fsx -- build
dotnet fsi build.fsx -- test
dotnet fsi build.fsx -- format --dry-format
```

The default configuration is Release; use `-c Debug` to change it. `--quick` skips the CLI's restore stage when dependencies and tools are already restored. `format` applies formatting; `format --dry-format` checks it. The template-derived CLI uses Fantomas for owned F# files and the SDK whitespace formatter for owned C# files. It excludes dependency and generated sources.

Tests cover both models, C# property/command bindings, and both actual Avalonia windows under the headless platform. UI tests exercise editing, address validation, keyboard Submit/Reset, empty and malformed numeric text, and close the windows. They also render initial window previews into the C# test project's `bin/Release/net10.0/order-fsharp.png` and `order-csharp.png`. Headless checks verify controls and bindings; native window-manager behavior and platform-specific rendering still deserve manual checking on each target platform.

CI checks formatting, builds the complete solution, runs both suites, and smoke-runs both consoles after recursive submodule checkout.

## Update Ranvier deliberately

From this repository:

```shell
git -C external/Ranvier fetch origin
git -C external/Ranvier checkout --detach origin/HEAD
git -C external/Ranvier rev-parse HEAD
dotnet fsi build.fsx -- build
dotnet fsi build.fsx -- test
dotnet fsi build.fsx -- format --dry-format
dotnet fsi build.fsx -- run-fsharp-console
dotnet fsi build.fsx -- run-csharp-console
```

Then update the pinned revision in this README, review any upstream API changes, exercise both desktop apps, and commit the changed `external/Ranvier` gitlink together with any example adaptations. Builds always use the committed revision and never update the dependency automatically.

## Repository layout

`src/` contains two models and four runnable applications. `tests/` contains the F# behavioral suite and C# model/binding/headless suites. `external/Ranvier/` is the dependency submodule. The Partas build entry point is `build.fsx`; all owned projects live in `Ranvier.Examples.slnx`.

License: MIT.
