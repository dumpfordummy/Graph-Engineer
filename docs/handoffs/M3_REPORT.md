# M3 implementation report

Date/timezone: 2026-09-24, Asia/Kuala_Lumpur (UTC+08:00). Final integrated check ended at 01:35:17 local time.
Branch / starting HEAD / final HEAD: `main` / `9d8d0c3` / `9d8d0c3`. Nothing staged, committed, pushed or merged.

Preexisting work preserved: the seven supplied untracked documents `README_M3_HANDOFF.md`, `docs/M3_SOURCES.md`, `docs/handoffs/M2_LEAD_REVIEW.md`, `docs/handoffs/M3_REPORT_TEMPLATE.md`, `docs/tasks/M3_CODEX_PROMPT.md`, `docs/tasks/M3_DATA_BINDINGS.md` and `docs/tasks/M3_RUN_EXECUTION.md`. No source modifications existed at initial inspection. Historical M1/M2 handoffs and migrations were preserved. No live application database, credential store, pairing file or Codex authentication file was inspected.

## Status

Overall: **READY FOR USER WORKFLOW CHECK**.

M3 is implemented end to end. Required automated categories A01–A16 passed using isolated databases, synthetic credentials and controlled HTTP providers. This is implementation/fixture evidence, not independent audit, release approval or a real-provider result.

- M2-U1 same-profile user restart probe: **NOT RUN / unconfirmed**. The user supplied a successful real-provider text-test screenshot; it does not establish key persistence or another successful probe after restart.
- M3-U02 user real workflow: **NOT RUN**. No user-operated text run, two-node JSON run or completed real-run history restart result has been supplied.
- M4 remains unauthorized. There is no token streaming, automatic inference retry, provider tool invocation, arbitrary shell/command node, coding agent, branching, repair loop or simulated execution.

## Baseline and process safety

Before implementation, `scripts/check.ps1` completed with exit 0: **39 Core + 102 API tests, 57 frontend tests, 17 browser tests**, plus locked restore, builds, type-check and lint. Evidence: `.artifacts/m3/m2-baseline.log`. Contracts were fixed in `docs/CONTRACTS.md` before separate Core, Runs backend and frontend ownership; the root integrated provider/security/process/browser work and dependencies.

Environment: installed/pinned .NET SDK 10.0.401 and runtime/ASP.NET Core 10.0.12, Node 24.11.1, npm 11.6.2, Git 2.52.0.windows.1, PATH Codex CLI 0.106.0. No machine prerequisite/global setting changed. Normal approval controls were used for package access and Windows ACL/DPAPI/browser/process checks. Only `@microsoft/signalr` 10.0.11 was added, pinned with the npm lockfile; install reported zero audit vulnerabilities. Full environment details are in `docs/ENVIRONMENT.md`.

The corrected production launcher Job Object and retained direct-handle cleanup were retained. No name-based or parent-ancestry termination was introduced. The lifecycle regression uses a separately launched synthetic Node process outside the launcher job, verifies that it survives every cleanup scenario, then disposes only that exact owned synthetic handle. Final scenarios:

| Scenario | Result | Evidence |
|---|---|---|
| Ordinary Ctrl+C after ready/listening | PASS | Launcher exit 0, services/listeners gone, synthetic PID 38192 survived |
| Controlled partial startup failure | PASS | Isolated data path deliberately a file; launcher exit 1, sibling cleaned, synthetic PID 7932 survived |
| Abrupt owned launcher termination | PASS | Exact retained parent handle terminated; job services/listeners gone, synthetic PID 20368 survived |

Command: `scripts/launcher-lifecycle-test.ps1 -Scenario All -FirstPort 6510 -NoBuild -Label m3-owned-final`. Evidence: `.artifacts/m3/lifecycle-final.log` and `.artifacts/m3/lifecycle-m3-owned-final-20260923-172731/results.json`.

Earlier unsuccessful checks are retained and are not represented as passes:

- First integrated security attempt exposed an M3 migration-model snapshot mismatch; 42 cases failed at startup/cleanup. Startup disposal was tightened and the frozen migration model corrected. The rerun passed 73 security/transport cases. Evidence: `backend-tests/security-transport.trx` and `security-transport-fixed.trx`.
- The first normal-sandbox real-process attempt failed four cases because Windows denied applying the required owner ACL. The normal approved rerun passed all four without weakening ACLs. Logs remain under `process/1cace90ed3fe4abd9f3adbb49bc4e621`, `670aedeb074d4c3ba62fb89d6ab32d46`, `953c6490298a4b5b9f559ee4ceb82169`, and `d11f84f9c4d74ed8a2700d007baf0eb6`. All test-owned processes were cleaned through retained handles.
- Concurrent developer test builds briefly collided on the same `obj` DLL (CS2012); builds were serialized, without terminating another process.
- The first two-node browser test had an off-canvas End node behind the inspector. The test now uses the existing Fit view control; no forced click bypass was added. Evidence: `browser-initial-results.json`.
- An initial lifecycle attempt completed Ctrl+C then refused occupied ports 6462/6463. It did not stop their occupants. Evidence: `lifecycle.log`. All three scenarios passed on confirmed-free 6510–6515.
- Old migration-count and validation-scope assertions needed narrow M3 updates. The first full backend batch had 158 API passes/one stale migration-count failure; the first integrated browser batch had 20 passes/one stale scope-text assertion. Evidence: `backend-final/m3-all-backend.trx`, `final-check-initial-scope-assertion.log`, `browser-scope-assertion-results.json`. Existing semantic/no-inference checks remain intact.

Paths in this section are relative to `.artifacts/m3/` unless fully qualified. The historical M2 harness incident remains documented in M2_REPORT.md; no claim is made that the prior incident did not occur.

## Implemented behavior and contracts

Document formatVersion remains 1. Start stays typeVersion 1; Model Call and End support unchanged legacy v1 plus explicit v2. Existing drafts import/save without reinterpretation. The inspector's **Upgrade for execution** creates v2 configuration deliberately; prompts remain literal until bindings mode is chosen. Incomplete execution configuration is saveable, with actionable readiness diagnostics. Unknown properties remain rejected.

Control edges determine one sequential path, independently of array order/layout. Data transfer is explicit: run input pointer, earlier node full text, or earlier jsonObject pointer. RFC 6901 escape/index rules distinguish missing from null and treat prototype-like property names as ordinary keys. Only earlier successful nodes on the frozen path are legal. Canonical `{{inputs.alias}}` substitution is single-pass and typed; inserted content is not recursively interpreted. Numeric tokens survive beyond JavaScript's safe integer range through submission, backend resolution and run-inspector display.

jsonObject is strict local validation of a completed assistant text response: one root object, no duplicate keys, prose, fences, trailing content, invalid numbers or excess depth. It is not provider schema-constrained generation, arithmetic validation or a repair mechanism. End maps exactly one explicit binding to the typed result. Tests use an actual first result of 19 to prove the next request receives 19 rather than recomputing the sample's expected 14.

Admission requires an explicit saved revision and JSON object, validates topology/configuration/profile/input/limits, and atomically stores an immutable document/input/profile snapshot, initial node states and queued event. Provider snapshots include protocol, connection version, model, endpoint/auth/approval settings and limits, never keys/ciphertext. Run history has no cascading dependency on editable workflows or profiles. Submission IDs and the single active slot have unique SQLite constraints. Repeated identical submission returns the original run even after revision changes; mismatched reuse conflicts. Ambiguous response recovery performs protected lookup only.

A held OS ownership lease precedes directory ACL mutation, token rotation, migration, recovery and worker startup; a second file handle excludes alternate filesystem aliases. A durable background worker claims queued work using fresh scopes/contexts and runs the edge-ordered path. SQLite stores run/node/attempt state, ordered events and immutable hashed artifacts. The worker commits resolved inputs/prompt and dispatch intent before sending, rechecks a coherent current profile/credential snapshot against the pinned version immediately before dispatch, and shares the M2 transport/concurrency protections. Changing a connection stops later nodes; editing a workflow cannot rewrite a run. Active profile references prevent deletion even after the live graph removes its reference.

States and external outcome are explicit. An in-flight attempt becomes **Unknown** when dispatch intent is committed; intent does not prove delivery. A definitive local preflight failure may establish **NotStarted**; a received provider HTTP response establishes **ResponseReceived**. Cancellation records CancelRequested before aborting the owned request; late completion cannot resurrect a terminal result or start a downstream node. Queued cancellation sends no request. A browser disconnect does not cancel backend work. Shutdown stops admission. Startup reconciles every previous nonterminal run, including Queued, to Interrupted, preserves completed artifacts, and never resumes uncertain work automatically.

Output/attempt/artifact/event updates commit together. Run-local sequences are unique and allocated in the same guarded transaction as state. Notification publication follows commit and is bounded to two seconds, including a publisher that ignores cancellation. A failed or stalled notification cannot undo success or cause a provider retry. Failures before dispatch intent yield zero calls; failures after external completion stop downstream work and retain conservative recovery state.

The authenticated, read-only SignalR hub exposes no run-mutation methods. Exact Host/Origin, cookie auth and expiry closure protect the actual transport; negotiate is the only empty-body POST exception and still requires session/Origin/antiforgery. WebSockets proxy through Vite. Notices contain only run ID and event sequence. The Vue Runs view reconciles protected snapshots/events on reconnect, deduplicates notices, rejects stale HTTP state, backs off read-only polling, and stops unauthorized observation until pairing. The UI shows a frozen read-only graph, real status/counts/timestamps, resolved bindings/prompt, output, final result, usage, artifacts and ordered events.

Limits: one active run; 16 Model Calls; 32 bindings/node; 2048-character pointer; 16 KiB input; 64 KiB resolved prompt; 256 KiB provider body; 128 KiB completed text; JSON depth 32; ten-minute run deadline, with smaller remaining deadline/profile timeout applied. Existing provider timeout 5–120 seconds and output-token 16–4096 limits remain. Accepted output is never silently truncated. No previous conversation, response ID, tools, background execution or schema flags are sent.

Prompts, inputs, successful outputs and artifacts deliberately remain in local SQLite **without DPAPI encryption**; protect DB/WAL/backups accordingly. Provider credentials remain DPAPI CurrentUser protected. Raw/normalized text/metadata and decoded JSON are checked against only selected/acquired run credentials. A detected echo fails with sanitized diagnostics and no output forwarding/persistence. This is not arbitrary-secret discovery. Provider error bodies remain unreflected. Text/JSON is escaped in Vue; HTML-like output stays text.

## Verification matrix

The authoritative final `scripts/check.ps1` returned **exit 0**, ending 2026-09-24 01:35:17 +08:00. Backend: **269 tests (110 Core + 159 API)**, zero failures/skips. Frontend: **81 tests across 9 files**, zero failures/skips. Browser: **22 passed**, zero skipped/flaky/retries. Backend build: zero warnings/errors. Frontend type-check, lint and production build passed. Evidence: `.artifacts/m3/final-check.log`, `playwright-results.json`, and `playwright-report/index.html`.

| ID | Status | Actual verification and implications | Evidence under `.artifacts/m3/` |
|---|---|---|---|
| A01 | PASS | Full M2 baseline then final combined suite; existing editor/provider/session tests retained. Run-disabled assertions now specifically cover legacy/incomplete drafts. | `m2-baseline.log`, `final-check.log` |
| A02 | PASS | Fresh three-migration DB; a real M2-only schema with legacy workflow/profile/actual DPAPI ciphertext migrated without reset. Core legacy preservation, explicit UI upgrade and v2 roundtrip tests. | Final Core/API results; `backend-tests/run-worker-durability.trx`; browser two-node check |
| A03 | PASS | 110 Core tests include literal mode, exact numeric tokens, pointers/escapes/arrays/null/missing/prototype names, source ordering, nonrecursive interpolation and explicit End. Real worker uses distinct profiles. | `final-check.log`; RunExecution/RunDurability and frontend tests |
| A04 | PASS | Real worker/controlled HTTP: one full-text call and two independent-profile JSON calls. First fixture returns 19; second request contains only `Multiply 19 by ten.`. Browser recipe separately records 7→14→140 with exactly two requests. | `backend-tests/run-worker-privacy.trx`, `m3-browser-requests.json`, `run-two-node.png` |
| A05 | PASS | Invalid JSON/fences/duplicate keys/root arrays/trailing content, incomplete/refusal/tools-only/malformed/oversized payloads and missing selected output fail; downstream count zero. | RunExecutionTests; `run-failed.png`; final check |
| A06 | PASS | Concurrent identical IDs return one run; mismatch/busy conflicts; replay after edit returns original. Browser discards a real accepted POST response, refreshes and looks up the retained ID: exactly one provider call. | RunExecutionTests, RunControl tests; browser ambiguous-submission case |
| A07 | PASS | Held first request while live prompt/layout/reference/profile changes occur. Old snapshot retained, next changed-version node NotStarted, active-profile deletion conflicts. Completed history survives profile removal. | `backend-tests/run-worker-durability.trx`; final browser frozen-design check |
| A08 | PASS | Queue cancellation zero calls; observed active-call cancellation/completion race one call, downstream skipped, repeated cancel stable; provider timeout Unknown with no retry; partial successful output retained. | RunExecutionTests; `run-cancelled.png`; final check |
| A09 | PASS | Actual dotnet process replacement: completed two-call run identical after re-pair; observed in-flight crash and provider-completed/SQLite-write-blocked crash recover Interrupted, never replay/downstream. | `test-results/m3-process-tests.trx`, isolated `process/*/backend.log`, final check |
| A10 | PASS | Two real backends/different ports/same data directory: competitor exits, token unchanged, owner finishes exactly one call. Different isolated data directory independently healthy. | ProcessExecutionTests and final check |
| A11 | PASS | Chrome refresh and offline state during held request; test-owned Vite proxy replacement drops actual WebSocket while backend continues. Authenticated transport reconnects, missed state reconciles, provider count stays two. Unit tests cover duplicate/out-of-order notice/snapshot and failed terminal-event catch-up. | `m3-browser-requests.json` (five actual hub connections, two before outage), frontend tests, browser results |
| A12 | PASS | Private run/readiness/artifact/submission/events and mutations reject anonymous/invalid-CSRF/Origin through direct API and Vite. Actual hub WebSocket rejects anonymous/foreign/missing Origin, bad Host and query token. Real hub closes at cookie expiry; re-pair/browser restart does not submit work. | `backend-tests/security-transport-fixed.trx`, HubSecurityTests, final browser security checks |
| A13 | PASS | Real DPAPI baseline retained. Raw/control-split/escaped nested/property-name secret echoes fail; full text and byte caps enforced. Browser asserts synthetic key absent from run/artifacts/workflow/DB/WAL/log/page; HTML remains text. Notice payload has only two fields. | ExecutionTransport/RunDurability/HubSecurity tests; final secret-echo browser case; `run-literal-untrusted.png` |
| A14 | PASS | SQLite triggers fail intent commit (zero calls) and output commit (one call, no output/downstream/replay). Actual crash after provider completion tested. Publisher inspects committed DB, then throws; another publisher never completes. Both preserve success and exact request count. | RunDurabilityTests, ProcessExecutionTests, final check |
| A15 | PASS | Ordinary Ctrl+C, partial-start failure and abrupt launcher termination using owned handles; separately owned untargeted Node process survives each. | `lifecycle-final.log`, `lifecycle-m3-owned-final-20260923-172731/results.json` |
| A16 | PASS | Browser upgrades/configures two-node bindings, rejects dirty Run, requires explicit input/usage confirmation, displays real statuses/inspector/final/history, cancellation/failure, immutable snapshot after edit, and retained import/keyboard guards. Synthetic screenshots visually inspected. | `playwright-results.json`, `run-two-node.png`, `run-failed.png`, `run-cancelled.png`, existing editor screenshots |
| U01 | NOT RUN | Successful user text-probe screenshot supplied; no confirmation of same-profile restart/key persistence/repeated probe. Not inferred from fixtures. | No user restart evidence |
| U02 | NOT RUN | No user-operated real text/JSON workflow or completed-history restart result supplied. | No real workflow evidence |

No successful fixture run is presented as a live model's output. Network request counts come from controlled HTTP fixtures, separately from UI dispatch-intent/attempt counts. Screenshots contain synthetic data only; automatic trace/video/HAR/failure screenshots remain disabled.

## User evidence — separate

M2-U1: performed by/date, restarted same profile, no key reentry, saved-key status, repeat result/duration/connection version: **NOT RUN / unconfirmed**. The successful text-test screenshot is acknowledged as supplied evidence only; provider identity, billing and persistence were not independently established.

M3-U02: performed by/date, protocol/model alias, saved revision, actual text outcome, two-node outcome/values, second resolved input, run/attempt IDs, refresh/reconnect and completed-history restart: **NOT RUN**. The user enters actual details only in the local app and supplies sanitized observations personally. No live credential material belongs in this report.

## Change review and limitations

Changed areas: Core strict v2 parsing/binding/planning; API Runs tables/migration/worker/endpoints/privacy; shared protected Responses transport; local ownership/session/SignalR integration; Vue execution configuration/confirmation/history/frozen inspector; targeted backend/frontend/browser/lifecycle verification; README/contracts/security/environment/progress/handoff. Existing editor layout and provider forms were extended without replacing the UI. The new migration is additive; historical migrations and M2 report remain unchanged. Only SignalR's stable browser package and its lockfile dependencies were added.

Final review corrected migration snapshot drift/startup-handle disposal, outdated migration/scope assertions, decoded credential reconstruction, missing protocol snapshot, publication backpressure, UTC pagination, wrapped negotiation authorization errors, terminal-event reconciliation, late-navigation races and the in-flight Unknown label. `git diff --check` passed. Git's CRLF normalization advisories are informational; no whitespace failure remains. All changes remain unstaged/uncommitted.

Residual limits: Windows/current-user trust; no resistance to same-user malware/admin access; no general secret scanner; local run data/backups are plaintext; no guarantee of remote cancellation, billing outcome or exactly-once external execution; unsupported non-Responses/proxy-required deployments; JSON local validity does not guarantee arithmetic or business correctness. The existing small launcher start-to-job-assignment window remains documented in M2. M3 does not add a sandbox for external programs, since shell/agent execution is out of scope. No known failing in-scope automated check remains.

## Exact operator steps

From this repository root, with its development/test services stopped:

```powershell
dotnet restore GraphEngineering.slnx --locked-mode
npm ci --prefix apps/web
.\scripts\dev.ps1
```

Routine subsequent launches need only `scripts/dev.ps1`; avoid `npm ci` while this project's Vite/Vitest holds Rolldown. Open `http://127.0.0.1:5173`. In a second terminal open the local pairing file without printing it:

```powershell
notepad.exe "$env:LOCALAPPDATA\GraphEngineering\runtime\pairing-token.txt"
```

Enter its contents at **Pairing token → Pair local browser**, then close Notepad. Actual credentials go only into **Model connections**. The M2-U1 check is still the user's same-profile test, Ctrl+C/restart/re-pair, verify Credential saved, then explicit retest without key reentry.

For U02, start with **New workflow**, upgrade Model Call and End explicitly, select a saved profile, use literal `Reply with a short greeting.`, Text output, and End → Earlier node text → that Model Call. Save, Run, input `{}`, confirm listed usage/retention and submit once. Inspect Succeeded, actual prompt/text/final result.

For the two-node example, add a second Model Call, reconnect the path Start → Double value → Multiply by ten → End using the inspector's Target node/Reconnect edge and Connect to/Add connection controls. Upgrade both calls/End, choose profiles independently, then configure:

| Node | Mode/source/output |
|---|---|
| Double value | Explicit bindings; alias `x`, Run input pointer `/x`; JSON object — local validation |
| Multiply by ten | Explicit bindings; alias `varX`, Earlier node JSON → Double value, pointer `/varX`; JSON object — local validation |
| End | Earlier node JSON → Multiply by ten; empty pointer selects whole object |

Prompts:

```text
Take the number {{inputs.x}}, multiply it by 2, and return only one JSON object with a numeric varX property. No explanation or markdown.

Take the number {{inputs.varX}}, multiply it by 10, and return only one JSON object with a numeric varY property. No explanation or markdown.
```

Save, review Run readiness, enter `{"x":7}` in Run, confirm maximum two provider calls and local retention, and submit once. Inspect actual first output, second resolved binding/prompt, then final output. Expected arithmetic 14/140 is not guaranteed by the application. Refresh/reopen the same run, Ctrl+C/restart/re-pair, and reopen completed history without submitting again. The complete click-by-click recipe is also in README.md.

Isolated manual launch:

```powershell
.\scripts\dev.ps1 -ApiPort 5081 -WebPort 5174 -DataDirectory "$PWD\.artifacts\manual-m3"
# Second terminal; browser address is http://127.0.0.1:5174
notepad.exe "$PWD\.artifacts\manual-m3\runtime\pairing-token.txt"
```

Automated verification, synthetic fixtures only:

```powershell
.\scripts\check.ps1
.\scripts\launcher-lifecycle-test.ps1 -Scenario All -FirstPort 6510 -NoBuild -Label manual-m3
dotnet test tests/GraphEngineering.Api.Tests/GraphEngineering.Api.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~ProcessExecutionTests
dotnet test tests/GraphEngineering.Api.Tests/GraphEngineering.Api.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~HubSecurityTests
npm --prefix apps/web run test:e2e -- --grep 'explicit two-node|literal text is unmodified|invalid JSON fails|cancelling an observed'
```

Require free verification ports. If the harness refuses an occupied port, select another isolated range; do not stop its occupant by name. Prefer Ctrl+C in the original launcher terminal. `scripts/launcher-recover.ps1 -ApiPort 5080 -WebPort 5173` is a read-only inventory; use its `-Stop` only after confirming the displayed exact project processes are orphaned. Never delete live data or broadly terminate node/dotnet. Interrupted runs remain inspectable and require an explicit new submission if the user decides to repeat work.

## Lead gate

Open evidence: M2-U1 same-profile before/after restart probe and M3-U02 real literal/JSON workflow plus completed-history restart. The lead/user reviews those sanitized observations with this automated evidence. Current status is **READY FOR USER WORKFLOW CHECK**, not real-provider acceptance or authorization for M4.
