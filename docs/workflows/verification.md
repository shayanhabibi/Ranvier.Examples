# Verification record

## Repeated-interaction correctness review

All 91 Release tests passed after the [correctness review](correctness-review.md): 82 C# plus 9 F# tests, including 13 new regression cases with repeated desktop interactions, tab switching, retry/recovery and close/reopen rounds. The review reproduced and fixed seven categories of defects; the report records each finding, its impact and the checks performed.

## Elmish addition

Verified on 2026-10-19 after adding expense review:

- Complete Release build: zero warnings and errors.
- Unfiltered Release tests: 69 C# plus 9 F# tests passed (78 total).
- New Elmish console scenario passed with real JSON import/report output and exactly two approved-total notifications.
- Retained support, data and file-processing console workflows passed.
- New headless journey uses actual selection, reason editing and keyboard-invoked Approve/Undo/Redo/Reject/Save buttons; rendered preview inspected.
- Formatting, semantic workspace diagnostics, XML documentation and comment hygiene checks passed.
- Persisted model and selector behavior rechecked in a rebuilt SageFs session; owned sessions stopped.
- Independent review: no critical or important findings. Reason input remains a draft for the next rejection when decision history changes, as documented by the API.

Decision: use the pinned Ranvier.Elmish bridge and its Elmish-compatible command shape. A separate standalone Elmish-runtime comparison would require another host. No review findings were deferred.

## Original three-workflow baseline

Verified on Windows with .NET SDK 10.0.402 and the pinned Ranvier source revision on 2026-10-19.

- Complete Release solution build: zero warnings and zero errors.
- Unfiltered Release tests: 63 C# and 9 F# tests passed.
- All three real console scenarios passed: HTTP conflict/rebase/save after navigation/disconnect/recovery; file hashes/failure/retry; CSV repair/incremental reuse/bulk conversion/export.
- Formatting check passed for owned F# and C# sources.
- F# semantic workspace check through fslangmcp: zero errors and zero warnings.
- F# XML documentation audit: zero findings.
- Comment hygiene check: no review markers.
- Fresh recursive clone checked out the pinned submodule and built the complete Release solution with zero warnings/errors.
- Both retained introductory console scenarios passed.
- Actual headless desktop controls selected tickets/measurements and edited drafts; the rendered data view was inspected.

An independent whole-branch review identified store ownership, hidden command errors, nonreactive Reload eligibility, decimal-comma parsing and a stale clean-row baseline. Regression tests reproduced these findings; all were corrected before the full suite above.

Implementation decisions:

- Use three complementary language-idiomatic cores with one shared UI instead of duplicating all three cores in both languages. Cost if another language variant is wanted: add that variant separately.
- Start search work untracked after reading the admitted input, keeping request bookkeeping out of the search effect's dependencies. Race tests cover the resulting request lifetime.
- Give a running service exclusive ownership of its persisted store. A second workbench using that directory reports an initialization error; concurrent clients should use the same service rather than independent cached stores.

No review findings were deferred. Native window-manager behavior across Windows, macOS and Linux remains a manual platform check.
