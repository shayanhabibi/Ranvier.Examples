# Correctness review of repeated interactions

Reviewed every shipped workflow on 2026-10-19, starting from commit 8a96eed. The review included independent source reviews and new tests through actual Avalonia controls, using real loopback HTTP and files where relevant.

## Findings and fixes

- **Support — async graph activation (high):** saves could reach the server, then fail local reconciliation with "No ambient graph on this thread." The callback was on the graph thread but lacked activation. Support and CSV completion callbacks now execute their actions through graph.Run.
- **Support — stale save baseline (high):** opening offline then refreshing, or reopening while an earlier session-owned save completed, left the editor at version zero or an older version. The next save conflicted. An editor-owned effect now follows accepted versions while its drafts are clean. A redundant captured-version completion write was removed to prevent delayed old replies overwriting newer baselines.
- **CSV and Elmish — selection identity (medium):** replacing a caption string or immutable decision record cleared visual selection during ordinary edits. Lists now retain stable integer keys, update captions independently, and restore selection by key after filtering. Captions release their bindings on detach and recreate them on reattach.
- **Shared commands — stale error banners (medium):** a successful recovery could keep showing an old HTTP/command error. The screen now reports errors from the current action, clears them when another action begins, and ignores superseded background failures.
- **CSV — unit-conversion range (medium):** an accepted 1000000 C reading became invalid after conversion to Fahrenheit or Kelvin, and could not convert back because invalid rows were skipped. Bounds now apply to normalized Celsius, with safe preliminary limits to prevent decimal overflow.
- **Startup — cross-workflow failure (medium):** malformed persisted CSV replaced the already-created Support tab with a global initialization error. Recoverable startup failures now remain in the affected workflow, preserving other tabs and allowing file repair/reload.
- **Shutdown — unfinished startup ownership (medium):** closing during HTTP startup completed Stopped before releasing the persisted store lock. Immediate reopening could fail. Shutdown now cancels and awaits initialization before completing resource cleanup.

## Coverage by example

- **Support:** three save/refresh/Back/reopen rounds through controls; offline/restore error recovery; failed initial detail open followed by refresh and save; reopening during an earlier save.
- **File jobs:** repeated batches, selected-row progress updates, three failed retries followed by repaired success, and filter hide/restore. No additional worker-algorithm defect was established; stable key selection also protects this view.
- **CSV:** repeated edits and C/F conversion, tab switches with visible-caption assertions, filtered repairs, F/K boundary round trips, and malformed persisted source repair through Load.
- **Elmish:** three review/undo/redo/save/reload rounds, selection retention and visible captions after tab switches. No additional pure update/revision defect was established.
- **Introductory F# and C# order editors:** five rounds each of validation, submission, edits after accepted snapshots, resubmission, malformed text and Reset. No new order-rule defect was established.
- **Window lifetime:** five immediate close/reopen rounds against the same persisted store.

## Verification

All 91 Release tests passed: 82 C# tests and 9 F# tests. Thirteen new regression cases cover repeated interactions; several deliberately run multiple rounds. The Release solution build, formatting, F# semantic diagnostics, XML documentation audit and comment hygiene checks passed. Rendered output was inspected.

A second review of the fixes identified the obsolete OpenAsync version assignment described above; it was removed. No confirmed finding was deferred. Native window-manager behavior across operating systems remains outside the headless checks.
