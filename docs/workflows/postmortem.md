# Postmortem: example workflows failed under repeated use

Date: 2026-10-19

## Responsibility and scope

These were implementation and verification failures in Ranvier.Examples. The confirmed defects did not establish a correctness bug in Ranvier. The dependency stayed pinned to `305d00908d3f7cfbc015bd3df051c650886377dc` and was not modified.

In particular, Ranvier's [threading documentation](../../external/Ranvier/docs/content/guide/threading.md) already distinguishes thread affinity from ambient graph activation. I failed to apply that documented contract correctly. Suggesting an upstream API issue before establishing an upstream defect was an attribution mistake. No Ranvier issue was opened.

## What happened

The expanded examples and Elmish addition passed their original builds, tests and console walkthroughs. At commit `8a96eed`, 78 tests passed. I reported those results with more confidence in interactive robustness than the coverage justified.

The user then reported flakiness beyond the first interaction. A review using repeated desktop actions reproduced failures that the original tests missed. Commit [c746dea](https://github.com/shayanhabibi/Ranvier.Examples/commit/c746deab6a07e9aee2551364255617080eb8ddf4) corrected them and added 13 regression cases. The resulting 91-test suite and Linux CI passed.

The [correctness review](correctness-review.md) records the individual defects and fixes. Their effects included:

- A ticket could be saved remotely while local completion failed, leaving misleading status and a stale baseline.
- Editing CSV or expense rows could clear the visible selection.
- An offline editor or one reopened during a save could submit an outdated expected version.
- Successful recovery could leave an old error banner visible.
- Unit conversion could turn an accepted boundary reading into an invalid value.
- A malformed CSV could remove an unrelated Support tab.
- Early shutdown could report completion while startup still held the store lock.

## Why the mistakes escaped

**The test environment supplied context the application did not.** Model tests kept `graph.Activate()` active across the scenario. That masked completion callbacks which reached the owning thread but called ambient helpers without activating the graph. The actual UI command path exposed the failure.

**Model assertions substituted for interaction assertions.** Some headless tests changed model properties or invoked model methods directly. Those checks established domain outcomes, but bypassed list replacement, selection notifications, control bindings and command execution. Correct model state could coexist with broken visible selection.

**Coverage concentrated on a completed first journey.** Refresh, navigation, retry and shutdown had individual tests, but the suite did not sufficiently compose or repeat them. It missed offline-open followed by recovery/save, reopen during an earlier save, failed retries followed by success, and immediate close/reopen.

**Boundary validation was not checked across transformations.** Raw-value limits were tested independently of conversion. A valid Celsius input could convert outside the raw limit, then become ineligible for conversion back.

**Source review did not compensate for missing execution evidence.** Independent reviews also missed these paths. A review returning no findings was not evidence that repeated user interactions had been exercised. I should have reported that limitation.

## What changed

The fixes now have regression coverage through actual controls and keyboard-invoked buttons. Tests repeat save/refresh/navigation, review/undo/redo/reload, conversion, retry and close/reopen sequences. They check visible selection and captions after tab switches as well as model state. File and HTTP paths use real I/O where relevant.

Async callbacks explicitly activate their graph. Clean ticket baselines follow current accepted data. Lists use stable keys with independently reactive captions. Errors follow the current action, conversion limits apply to normalized values, startup failures stay within their workflow, and shutdown awaits initialization.

A rendering regression introduced during the selection fix was caught by the added visual assertions and corrected before publication. Fixes need the same interaction and rendering scrutiny as the original implementation.

## Verification standard going forward

Before describing a workflow as robust, exercise its actual entry points over multiple rounds, including failure followed by recovery and overlapping operations. Assert the controls the user sees, not only the underlying model. Keep activation scopes representative of the host rather than supplying a permanent ambient context for convenience.

Report build, model, control-interaction and platform evidence separately. Passing tests establish the covered behavior; they do not establish that untested journeys work.

Attribute a failure upstream only after checking the documented contract and reproducing a dependency defect independently of application mistakes. Here, the evidence supported fixing the examples and recording this postmortem.
