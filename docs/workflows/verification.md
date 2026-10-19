# Verification record

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
