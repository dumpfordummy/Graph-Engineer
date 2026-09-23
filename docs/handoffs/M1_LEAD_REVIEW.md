# M1 lead review and M2 authorization

Date: 2026-09-23

## Decision

Conditionally accept the persistent editor milestone for continued development and authorize M2 as defined in `docs/tasks/M2_PROVIDERS_SECURITY.md`.

This is a review of the user's supplied screenshot and M1_REPORT.md, including its post-handoff repair addendum. It is not an independent source-code audit, rerun of tests, security certification, production release, Git commit, or merge approval. The named raw artifacts and source files were not supplied to the reviewer.

## Evidence accepted at the report level

The report records 39 Core tests, 15 real-SQLite API tests, 37 frontend tests, and 12 browser tests, with no skipped checks in the full run. It describes a real API process replacement against the same SQLite database, preservation of graph/configuration/layout, malformed-import handling, optimistic concurrency, delayed-save protection, and prompt keyboard safety.

The screenshot visibly contains a node palette, editable canvas, inspector, save/import/export/validation controls, and a disabled Run button. It supports visual acceptance of the current design direction. It does not independently demonstrate persistence, API behavior, or test results.

The post-handoff dependency repair reran frontend checks and a startup smoke test. Backend tests and Playwright were explicitly not rerun after that repair; the report says source and dependency versions were unchanged. Preserve that qualification.

## Carry-over M1-C1 — process lifecycle

The repair addendum records a repository Vite process still holding a native dependency file and an API process remaining after their original parent exited. Only identified project processes were stopped. Reinstallation succeeded without changing package manifests or lockfile hashes.

That establishes the reported install failure and repair. It does not establish whether the current launcher cleans up all owned processes on ordinary Ctrl+C, a sibling startup failure, or abrupt parent exit. A normal successful SmokeTest is not equivalent to those cases.

Before accepting real application credentials in M2:
- Recheck the current launcher, preserving user work and unrelated processes.
- Reproduce ordinary Ctrl+C shutdown and a controlled partial-start failure on isolated ports.
- Fix in-scope defects and add regression evidence.
- Investigate the reported parent-exit case. Implement narrowly scoped cleanup when practical; explicitly document unsupported abrupt-exit behavior and an exact-project recovery procedure if it remains.
- Do not promise cleanup after every possible OS/process failure.

Run the complete M1 baseline before M2 edits and the full regression suite after M2 integration. Distinguish environment blocks from test failures.

## UI direction

Retain the current canvas and inspector. No redesign or new visual framework is requested. Replace the inspector's future provider-profile ID textbox with a real profile selector in M2.

The screenshot's prompt referring to a previous `varX` response is currently ordinary text. M3 must introduce explicit input/output bindings; prose and a control edge must not implicitly copy arbitrary prior context.

## Authorization changes

Update only the stale M1-only authorization and matching status statements in AGENTS.md, MILESTONES.md and PROGRESS.md after inspecting their current contents. Record M1 as conditionally accepted for progression and M2 as the only active implementation milestone. Preserve the original M1 report and its evidence.

M3, graph execution, coding-agent invocation, shell nodes, approval execution, arbitrary expressions, and automatic Git operations remain unauthorized.
