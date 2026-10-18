# AGENTS.md

## Agent Source Comments

Use the plugin `comment-hygiene@roboz0r` to check for common mistakes in agent source comments.
If it is not setup in your environment, ask your USER for permission to set it up.

For `///` XML documentation comments, read the `fsharp-xml-docs` skill
(`.claude/skills/fsharp-xml-docs/SKILL.md`) before writing, reviewing or fixing them. It covers the
tag set and ships an audit script (`scripts/audit.fsx`) for FS3390 and convention checks.

## F# semantics — use `fslangmcp`, not grep

The repo ships an `fslangmcp` MCP server (`.mcp.json`, FsLangMCP over FSAC +
FSharp.Compiler.Service). It loads `Ranvier.Examples.slnx`, so it sees exactly the projects the
solution references.

Requires the `fslangmcp`, `fsautocomplete` and `fantomas` global tools; `fslangmcp
--bootstrap-tools` installs the pinned set.

If your environment doesn't have them, ask your USER for permission to set it up.

**Answer semantic questions about F# code with it. Reach for grep only for prose, JSON, `.mts`
tooling and other non-F# files.** Textual search in large source bases over-matches badly:
short binding names `create` can recur in thousands of members, and `find` resolves the real symbol instead.

- `find` — definitions and cross-project use sites, each tagged `definition`/`reference` with a
  coverage block. **Run `check` first.** When the workspace does not type-check, `find` returns
  `outcome="not_found"` *with* `coverage.complete: true` — a confidently wrong negative, not the
  indeterminate answer the coverage contract implies. Verified: `find "VirtualFileSystem"` matches
  on a clean tree and reports not_found on the same tree with unrelated compile errors. Never
  conclude "no usages" from a `find` taken while `check` says `errors`.
- `check` — fresh whole-workspace type-check verdict (`clean`/`errors`) with structured
  diagnostics, no build artifacts. Roughly 8s. An incremental `dotnet build Ranvier.Examples.slnx` is
  about as fast, so prefer `check` for the structured diagnostics, not for speed.
- `fcs_refactor_impact` — run this *before* changing any public signature. Returns blast radius,
  whether the symbol is public API (i.e. a breaking change), covering tests, and a verify list.
- `fcs_tests_for_symbol` — which tests cover a symbol, resolved to the enclosing Expecto
  `testCase` name. Only finds direct call sites; much of the suite exercises the library
  indirectly through the wire, so an empty result means "not called by name", not "untested".
- `fcs_public_api` — stable-ordered public surface, for diffing API before/after a change.
- `fcs_nuget_types` / `fcs_nuget_members` / `fcs_referenced_symbols` — inspect referenced
  assemblies without unpacking packages.

Pass `projectPath` explicitly on `fcs_*` calls when several agents run at once; caches are keyed
per resolved `.fsproj`. `fcs_dead_code` on some projects is dominated by generated-file
internals — treat its output as candidates to filter, not a work list.

## F# experiments — use `sagefs`; the build CLI stays the gate

The repo ships a `sagefs` MCP server (`.mcp.json`, SageFs live F# REPL with a project loaded)
and its playbook as the `sagefs` skill (`.claude/skills/sagefs/SKILL.md`). Read the skill before
any F# change, then apply the limits below: the skill treats the REPL as the whole inner loop, and
for this layout it is not.

Requires the `sagefs` global tool (`dotnet tool install --global SageFs`) and the .NET 10 SDK.
`sagefs mcp` starts the daemon if one is not running.

If your environment doesn't have it, ask your USER for permission to set it up.

### When to use it

- **Type and overload questions.** `check_fsharp_code` returns compiler diagnostics for a snippet
  in milliseconds. A CE, an overload set or an inferred type is answered by evaluating it and
  printing the result, not by a scratch project and a rebuild.
- **Shape questions.** Build a value (a pipeline, a parse result, a record) and walk it, or reflect
  over a type to read its members, rather than guessing an API.
- **Pure logic and pure tests.** Redefine a function in the session until a case passes, then
  write it to the file. Tests that touch no process, file system path or console run here fine.

### When to use the build CLI instead

- **Anything that starts a process.** Code compiled into the project that uses `use` inside
  `task { }` fails in a session with `MissingMethodException: TaskBuilderBase.Using`: the session
  loads the SDK's `FSharp.Core`, and the project was compiled against the NuGet one
  ([SageFs#141](https://github.com/WillEhrendreich/SageFs/issues/141)). Partas.Build's process
  runner is written that way, so a `run` step fails in the REPL and passes under `dotnet`.
- **Tests that locate files.** `AppContext.BaseDirectory` is SageFs's host directory in a session,
  and `Assembly.Location` is a shadow copy
  ([SageFs#142](https://github.com/WillEhrendreich/SageFs/issues/142)). A helper that walks up to
  the repository root must read an environment variable first, or fail in the REPL.
- **Release-only compiler errors.** The REPL compiles like Debug. An `inline` CE member that
  applies a function-typed alias compiles in Debug and fails in Release with `FS1118`.
- **The whole suite, and the final gate.** Run them through the build CLI. A filtered run is never
  an acceptance check.

On a small solution an incremental `dotnet build` takes seconds, not minutes; the REPL saves the
most on experiments that would otherwise need a rebuild per attempt.

### Session setup

- Build once, then `create_session` naming one `.fsproj` for the working directory (a git worktree
  is its own session), and poll `get_fsi_status` until Ready. A small project is ready in about
  10 seconds.
- Load the test project to reach both the library and the tests.
- Two sessions in one directory need `switch_session` to route. Switch after the target reports
  Ready: a switch made during warmup can be lost
  ([SageFs#140](https://github.com/WillEhrendreich/SageFs/issues/140)).
- Reproduce with `send_fsharp_code`, fix it there, write the fix to the file, run
  `hard_reset_fsi_session rebuild=true`, and check it again. The reset discards every session
  definition: re-send any helpers you still need.
- `stop_session` every session you created.

### Running tests in a session

`runTestsInAssemblyWithCLIArgs` finds no tests in a session. Pass the test values directly:

```fsharp
Expecto.Tests.runTestsWithCLIArgs [] [| "--filter-test-case"; "hello greets by name" |] Ranvier.Examples.Tests.Say.tests;;
```

The result is the exit code plus Expecto's console output, colour codes included. To read which
tests failed and why, run with a printer that collects them:

```fsharp
let failures = System.Collections.Generic.List<string>();;
let printer =
    { Expecto.Impl.TestPrinters.silent with
        failed = (fun name msg _ -> async { lock failures (fun () -> failures.Add $"{name}: {msg.Trim()}") })
        exn = (fun name e _ -> async { lock failures (fun () -> failures.Add $"{name}: {e.GetType().Name}: {e.Message}") }) };;
Expecto.Impl.runEval { Expecto.Impl.ExpectoConfig.defaultConfig with printer = printer; runInParallel = false } Ranvier.Examples.Tests.Say.tests
|> Async.RunSynchronously;;
String.concat "\n" failures;;
```

### What to watch out for

- **Tools missing at startup.** The stdio bridge can drop the connection right after
  `initialize` ([SageFs#138](https://github.com/WillEhrendreich/SageFs/issues/138)), leaving the
  server's instructions present and its tools absent. Restarting the client usually recovers.
  With the daemon already running, pointing `.mcp.json` at `http://localhost:37749/` bypasses the
  bridge.
- **`sagefs check` reporting the SDK missing.** It misreads `global.json` roll-forward
  ([SageFs#139](https://github.com/WillEhrendreich/SageFs/issues/139)); trust `dotnet --version`.
- **Name clashes in the session.** A binding can resolve to something already in scope, a SageFs
  helper included. When a type error names a type you never wrote, rename the binding.
- **Errors are cheap.** A failed statement discards only itself. Fix it and resend; do not reset.
- When the REPL fails for a reason outside your code, report the tool, the input and the exact
  error, then use `dotnet` for that one step.
- Ask your USER before stopping, restarting or reinstalling the SageFs daemon.
