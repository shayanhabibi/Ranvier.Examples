# Real reactive workflows implementation plan

Execution completed on 2026-10-19. See [verification and implementation decisions](../../workflows/verification.md). The checklists below preserve the original task brief.

> **For agentic workers:** Use superpowers:executing-plans for native task-by-task implementation. Track results in the plan's progress ledger.

**Goal:** Make the support, job and data-repair stories real runnable workflows.
**Architecture:** Separate reactive cores with a shared thin Avalonia host, real HTTP/file adapters, deterministic test doubles for races.
**Tech stack:** Existing .NET10/Partas/Ranvier source dependencies, ASP.NET Core loopback HTTP, Avalonia12.
**Spec:** docs/superpowers/specs/2026-10-19-real-workflows-design.md

## Global constraints
Keep existing intro examples and tests. Never patch the dependency. All shell commands use rtk. Keep mutable test data isolated. Native execution authorization persists.

## Review focus
Late replies after selection/navigation, saves after editor disposal, arbitrary file errors/cancellation, raw invalid CSV cells, per-row incremental recomputation and cross-thread graph access.

### Task 1: Domain transport and API contracts
- [ ] Add C# TicketRecord/TicketUpdate/measurement DTOs and loopback API/client under src/Ranvier.Examples.CSharp/Support/.
- [ ] Add real versioned persistence and unavailable/recovery handling.
- [ ] Tests: save/reload persists; stale version returns conflict; unavailable returns 503; search returns matching tickets; bad IDs return 404. Verify red/green via dotnet test.
- [ ] Add fixtures copied to output and update solution/framework references. Commit.

### Task 2: F# support model
- [ ] Add src/Ranvier.Examples.FSharp/SupportDesk.fs and project reference to Ranvier.Query.
- [ ] Interface: SupportDesk(Graph, ticket service), SearchText/Queue/Selected/DraftNotes/ServerNotes/IsDirty/HasConflict/Status; SearchAsync, OpenAsync, RefreshAsync, SaveAsync, Back, Dispose.
- [ ] Tests: A then B with A finishing last retains B; refresh preserves draft; save then Back updates queue; failed refresh keeps accepted data; version conflicts keep edits; session disposal prevents late publication.
- [ ] Use query leases, deferred edits/reconciliation, native drafts, boundaries and timed search; verify signatures with semantic tooling/REPL. Commit after suite.

### Task 3: C# file jobs
- [ ] Add src/Ranvier.Examples.CSharp/Jobs/FileJobMonitor.cs and tests.
- [ ] Interface: FileJobMonitor(Graph, concurrency), keyed Jobs and filtered projection, selection, aggregate counts; Queue(paths), Cancel(id), Retry(id), Completion, Dispose.
- [ ] Tests: actual fixture SHA256 matches; bounded workers; missing path becomes failed row; cancellation settles; filters/aggregates update; terminal changes survive throttled presentation; unrelated rows retain identity.
- [ ] Stream actual bytes and dispatch graph writes; pump in headless console/tests. Commit.

### Task 4: F# data repair
- [ ] Add src/Ranvier.Examples.FSharp/DataQuality.fs with typed CSV loading and row model.
- [ ] Interface: DataQualityModel(Graph), keyed rows and filtered projection, selected-row edits, Summary; LoadAsync(path), ApplyUnitToIncluded(unit), ReloadAsync, AcceptUpstream, ExportAsync(path), Dispose.
- [ ] Tests: invalid raw numeric text retained; valid repair updates normalized aggregate; one row edit leaves unrelated calculation counter unchanged; batch creates coherent summary; reload preserves draft and exposes conflict; invalid export rejected; valid export round-trips.
- [ ] Use per-row drafts/memos and incremental collections; asynchronous selected lookup gets generation/cancellation guards. Commit.

### Task 5: Shared desktop workflows and console stories
- [ ] Add src/Ranvier.Examples.Workflows.Desktop/ with SupportView, JobsView, DataQualityView and WorkflowWindow, referencing both core libraries.
- [ ] Change desktop startup to expose all three workflows; keep intro reachable with --intro.
- [ ] Add console scenarios under FSharp.Console for support/data and CSharp.Console for jobs. Scenarios perform real operations and verify outcomes.
- [ ] Add build CLI run commands and explicit project paths. Headless test each workflow and inspect screenshots. Commit.

### Task 6: Documentation, independent review and full verification
- [ ] Update README and per-workflow walkthroughs/feature map, fixture guidance and failure cases.
- [ ] Run full Release solution build, unfiltered suites, formatting/docs/semantic checks, every console story and recursive-clone checks.
- [ ] Request one fresh independent whole-branch review; reproduce/fix important findings with red/green tests, then complete suites.
- [ ] Record results and decisions. Commit completed work; publish within the user's existing repository authorization.
