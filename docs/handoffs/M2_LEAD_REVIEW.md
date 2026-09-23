# M2 lead review — 2026-09-24

## Basis and decision

Reviewed the supplied M2_REPORT.md and the user's connection-test screenshot. The repository, raw TRX files, lifecycle artifacts, and actual network traffic were not independently inspected or rerun.

**Decision: conditionally accepted for development progression; M3 is authorized against isolated fixtures.** Real-provider restart verification remains open. This is not release approval or a code-security audit.

## Evidence recorded

- The report records 141 backend tests (39 Core + 102 API), 57 frontend tests, and 17 browser tests passing, with type-check, lint, production build, and zero-warning/error backend build. These are reported results, not independently reproduced results. See M2_REPORT.md, Status and Verification.
- The report describes actual Windows DPAPI/session tests with synthetic secrets; additive SQLite migration; versioned provider records; and no graph execution. See Implemented behavior and the acceptance table.
- The screenshot separately shows a saved connection test with completed assistant text GE_CONNECTION_OK, phrase match Yes, observed model GLM-5.2, duration 865 ms, connection version 1, and reported usage Total 102. Its displayed local timestamp is 9/24/2026, 12:34:34 AM. This establishes one visible successful text probe, not upstream model identity, general reliability, token-streaming/tool support, or a before/after-restart pair.
- The earlier report's real-provider NOT RUN statement reflects its authoring time. Preserve that historical report; append later user evidence instead of claiming Codex performed the live check.

## Open human check — M2-U1

Using the existing launcher, stop with Ctrl+C, wait for shutdown, restart, and pair using the new local token. Reopen the SAME saved profile WITHOUT re-entering the key. Confirm Credential saved and explicitly test it again. Record only date, result, duration, connection version, and a harmless preview. If this screenshot already followed a restart, the user must state that and confirm no key reentry; do not assume it.

Codex must not open the live credential database or perform this real-provider check. M3 synthetic development may proceed while this evidence is outstanding; real M3 acceptance depends on closing it.

## Process-safety finding — M2-S1

The report's lifecycle section records an earlier harness stopping MoNotificationUx.exe, an unrelated Windows Update notification process. Parent-PID ancestry was incorrectly used to infer ownership; PID reuse was considered plausible, not proven. Do not characterize this as harmless or claim every unrelated process was preserved.

The report says the harness was corrected to retain direct handles, verify identities, and keep descendant enumeration read-only, and that subsequent lifecycle cases passed. Retain that correction. In M3, inspect those guards and add/retain an isolated regression in which a deliberately untargeted synthetic process survives cleanup. Do not experiment on Windows services or infer ownership from a reused PID/parent relationship. Do not attempt OS repair without evidence and user authorization.

The documented process-creation/job-assignment interval remains a limitation, not a promise of universal crash cleanup.

## Authorized next work

Implement only docs/tasks/M3_RUN_EXECUTION.md and docs/tasks/M3_DATA_BINDINGS.md. Preserve existing provider security and editor behavior. Model connections remain Responses-only; M3 adds local text/JSON-object handling, not tool use or provider JSON-schema guarantees. Coding-agent and shell nodes remain M4.
