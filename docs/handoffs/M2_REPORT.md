# M2 implementation report

Date: implementation and final full suite 2026-09-23; handoff finalized 2026-09-24 (Asia/Kuala_Lumpur). Branch: main. Starting HEAD: d973bf8 (Init project). Dirty-worktree baseline: six untracked user-provided instruction/reference files — README_M2_HANDOFF.md, docs/M2_SOURCES.md, docs/handoffs/M1_LEAD_REVIEW.md, docs/handoffs/M2_REPORT_TEMPLATE.md, docs/tasks/M2_CODEX_PROMPT.md, docs/tasks/M2_PROVIDERS_SECURITY.md. Those inputs were preserved. No commits, pushes or merges were made.

## Status

**READY FOR USER PROVIDER CHECK**

- Implementation: complete for M2, with limitations recorded below; Run remains disabled.
- Automated fixture verification: PASS — full integrated check exit 0, 39 Core + 102 API xUnit tests, 57 frontend tests, 17 real-browser checks; type-check, lint and production build passed. Backend build had zero warnings/errors.
- Windows DPAPI/session verification: PASS using actual Windows facilities, isolated data and synthetic secrets. No fake secret store substitutes for the actual DPAPI acceptance tests.
- Actual user-provider verification: **NOT RUN**. The user has not yet entered actual provider details and personally run the before/after-restart probe. No real credential, live credential database, or Codex authentication file was accessed.

Fixture success is not proof of real deployment compatibility. Final acceptance remains a lead/user decision.

## M1 baseline and lifecycle carry-over

Baseline command: ` .\scripts\check.ps1 `. It was run before feature implementation. The original SDK pin 10.0.301 was unavailable; installed .NET 10 SDK is 10.0.401. Only the pin was updated to that installed stable SDK. Sandbox NuGet restore was denied socket access; the normal approval path allowed restore. The next build found the existing orphan API PID 30024 locking this exact repository's DLL. Its command/port and absent parent 12912 were verified before it was stopped. The subsequent baseline passed with exit 0: 39 Core + 15 SQLite API tests, 37 frontend tests and 12 Playwright checks, including a real backend restart. Evidence: `.artifacts/m2/m1-baseline.log`; initial SDK, network and locked-build attempts are preserved in neighboring `m1-baseline-*.log` files.

M1-C1 implementation: `dev.ps1` assigns its exact newly created API/Vite process handles to a non-inheritable Windows job object with kill-on-close. It preserves normal build/start behavior, saves/restores the configured browser origin and existing launcher environment values, isolates logs by port, and has explicit `-NoBuild` support for testing an existing build. No broad node/dotnet termination is used. A read-only-by-default recovery helper matches exact repository command paths and requested ports, rechecks process identity, and stops retained direct handles only when explicitly invoked with `-Stop`.

Ordinary Ctrl+C was tested after the normal ready prompt by sending native CTRL_C_EVENT to an isolated hidden launcher console. Controlled partial-start failure uses an isolated data-directory path that is a regular file, causing API startup to fail after both service processes are created. Abrupt-parent testing terminates only the retained launcher handle. The reviewed run on 6340–6345 passed all three, with both listeners released, verified direct services gone and read-only observed descendants gone. No ambiguous identities were present. Evidence: `.artifacts/m2/lifecycle-ownership-safe-20260923-155309/results.json`; detailed investigation, PID/port/exit-code evidence and earlier attempts are in `.artifacts/m2/lifecycle-report.md`. The final integrated-build rerun also passed all three cases with exit 0 on ports 6350–6355: `.artifacts/m2/final-lifecycle.log` and `.artifacts/m2/lifecycle-final-integrated-20260923-160133/results.json`. It completed 2026-09-24 00:02:21 local time.

The original launcher demonstrably orphaned services on forced parent exit. Initial Ctrl+C attempts blocked by temporarily uncompilable parallel sources are recorded as failed attempts and are **NOT RUN as interruption evidence**. SmokeTest alone was not used to claim Ctrl+C acceptance. Environment restoration, exact-project recovery, script parsing and the ownership regression fixture also passed.

**Test-harness incident:** an earlier harness inferred ownership from parent-PID ancestry and stopped unrelated `MoNotificationUx.exe` PID 27480, a Windows Update notification process. Its reported parent matched a newly observed console PID; creation time was not captured, so PID reuse is plausible but unproven. Testing stopped and the harness was corrected/reviewed. Cleanup now terminates only retained launcher/direct-service handles after command/parent/creation identity checks; descendant enumeration is strictly read-only. No attempt was made to restart or otherwise modify Windows Update. This incident is retained here rather than claiming all unrelated processes were preserved.

Final cleanup also identified preexisting orphan Vite PID 24004 from absent parent 12912, matching this repository and port 5173. Its retained handle, creation time and exact command were checked before stopping it, releasing the remaining original Rolldown lock/default frontend port. `.artifacts/m2/orphan-cleanup.log` records this. Final default-port recovery inspection found no matching launcher services. User files and application data were not reset.

A small interval remains between process creation and successful job assignment; these checks do not promise cleanup after every OS failure or host restriction. Recovery instructions below cover confirmed exact-project leftovers.

## Implemented behavior

Provider CRUD uses stable IDs, revisions, timestamps and separate connection versions. Metadata and DPAPI ciphertext change transactionally. Keep/Replace/Remove are explicit, blank or masked replacements are rejected, stale/failed saves preserve the previous credential, and destination/auth changes require confirmation and new bearer-key entry. Profile reads expose `hasCredential`, not the key, suffix, ciphertext or storage reference. Deleting a profile used by a saved workflow returns an explicit 409 conflict.

Local access uses a 256-bit launch token in an owner-restricted per-user runtime file, ASP.NET Core cookie authentication/Data Protection, exact configured Host/Origin enforcement and antiforgery. Pairing is bounded to 1 KiB and ten attempts per minute. Cookies are HttpOnly/SameSite Strict, nonpersistent, eight-hour absolute lifetime, Secure on HTTPS; the loopback HTTP development exception is documented. Restart rotates pairing and rejects previous launch cookies. Private reads and writes require a session; mutations also require JSON, the configured Origin and X-GE-CSRF. Health and narrow session/pairing bootstrap routes are the exceptions. Vite CORS is disabled. Kestrel endpoint overrides are rejected before they can override loopback configuration.

Provider URLs include their API prefix and append `/responses` exactly once. Save validates syntax and resolves named hosts with a five-second limit, without sending provider HTTP requests. Probing resolves again and validates all returned addresses before dialing one of those exact IPs. Explicit private/loopback approval and separate HTTP acknowledgment are required; no-auth is limited to approved private destinations. Unsafe URL components/path escapes, app-self endpoints, unspecified/multicast/link-local and known metadata/control addresses are blocked. Redirects, ambient proxies, OS credentials and cookies are disabled; TLS verification remains enabled.

The backend sends the fixed synthetic prompt `Reply with GE_CONNECTION_OK.`, exact model, `stream:false`, `store:false`, and bounded `max_output_tokens`. The text parser requires a completed Responses object and completed nonempty typed assistant output_text; it does not assume the first output item is a message. The adapter returns coherent safe categories for configuration/credential/network/TLS/timeout/provider/payload/incomplete/cancelled results. Error bodies are not reflected. Preview/model/request metadata redact the active credential before and after control normalization and before truncation. Usage is reported only for actual supplied numeric counts. A late result cannot verify a changed connection version. One probe per profile and two total are allowed; there is no automatic generation retry.

The existing Vue editor remains intact. Model connections provides accessible list/create/edit, explicit save/test, approvals, transient password fields and safe last-test display. Model Call nodes now select real named profiles independently; unknown imported IDs remain unresolved and saveable. Graph structure validity, profile configuration and prior text verification remain separate. Session errors retain safe unsaved profile/graph metadata and clear transient keys; pairing does not replay a paid test. Run remains disabled and labeled M3. There are no runtime attempts, run pages, shell nodes, agent invocation, model tools or graph execution.

The additive `20260923000200_ProviderProfiles` migration creates ProviderProfiles/ProviderCredentials in the existing SQLite database. Tests construct the original M1-only schema, seed a full workflow, apply M2 and verify unchanged metadata/definition/layout. The historical short-timestamp M1 migration ID was preserved. Both fresh migration and pending-model-change checks pass.

## Architecture and security decisions

- Bootstrap trust is possession of the current local runtime token, not CORS or Origin alone. The token is never anonymously served, logged, placed in a URL or persisted by the browser. Tests pair only with their own isolated launch files.
- DPAPI uses CurrentUser, with a stable exact 10.0.12 dependency. Session Data Protection keys are also CurrentUser protected. The app does not claim resistance to same-user malware or administrators.
- Credential ciphertext and profile settings share one SQLite transaction; a joined query obtains a coherent snapshot before the provider call. No database transaction spans network I/O. Existing keys are not silently reused for changed destinations.
- Probe limits: 24 KiB local provider JSON, 256 KiB upstream response, 500-character preview; connect timeout at most ten seconds, total timeout configurable 5–120 seconds, output 16–4096 tokens. Replacement keys are visible ASCII, up to 8192 characters, with no whitespace/mask placeholders.
- Copying the DB across Windows accounts/machines may require key reentry; cross-account portability was **NOT RUN** because the assignment requires current-user protection, not real-account credential migration. Logical deletion does not securely erase SQLite pages, WALs or backups.
- Proxy-required deployments and non-Responses protocols are unsupported in M2. Cancellation cannot guarantee provider work stopped or charges were avoided. `store:false` is a request option, not a provider retention guarantee.
- Browser trace/video/automatic failure screenshots are disabled. Explicit screenshots show synthetic data only after key/pairing fields clear. All verification secrets are conspicuous synthetic fixtures, never actual provider details.

## Verification

The final `scripts/check.ps1` completed with **exit 0**. Backend: **141 tests** (39 Core, 102 API = 15 M1 API + 19 local security + 68 providers), zero failures/skips. Frontend: **57 tests**, six files, zero failures. Browser: **17 tests**, zero failures, no retries. Type-check/lint/build passed. See `.artifacts/m2/final-check.log`, `.artifacts/m2/playwright-results.json`, and `.artifacts/m2/playwright-report/index.html`.

| Acceptance category from M2 task | Status | Actual method / evidence | Synthetic-only evidence path |
|---|---|---|---|
| Full M1 baseline and final regression | PASS | Baseline 54 backend/37 frontend/12 browser; final 141/57/17; both complete checks exit 0 | `m1-baseline.log`, `final-check.log` |
| Launcher lifecycle | PASS, with documented harness incident | Ordinary ready-state Ctrl+C, controlled partial startup and exact abrupt parent exit; listeners/processes gone; environment/recovery checks | `lifecycle-report.md`, `final-lifecycle.log`, timestamped lifecycle results |
| Fresh/existing DB migration | PASS | Real SQLite startup plus M1-only schema/seed migration; workflow content preserved; no pending model changes | `backend-tests/m2-provider-tests.trx`, final check |
| Profile CRUD/concurrency/restart | PASS | Real API requests; stale revisions and concurrent winners; disposed-host and real-process restarts; reference conflicts | Provider TRX, browser results, `backend-restart.json` |
| Credential Keep/Replace/Remove and failures | PASS | Real DPAPI, atomic replacement races, failed-store/stale-save preservation, destination reentry and explicit no-auth removal | Provider TRX |
| Actual Windows DPAPI / sentinel checks | PASS | CurrentUser roundtrip/corrupt/empty ciphertext; synthetic sentinel absent from isolated DB/WAL/API/profile/export/log outputs | Provider TRX, browser results |
| Inbound session/Host/Origin/antiforgery | PASS | 19 security tests; cookie restart/expiry/Secure/ACL cases; actual direct API and Vite proxy browser checks | `backend-tests/m2-session-tests.trx` plus final check, browser results |
| Outbound destination/redirect/DNS/TLS policy | PASS | Prefix/address classes/mapped IPs/self/approvals; save DNS plus changed-address actual-dial enforcement; controlled redirect and self-signed TLS fixtures | Provider TRX |
| Responses wire request / typed output | PASS | Actual fixture request path, headers, exact model/prompt/control flags; reasoning before completed assistant text | Provider TRX, browser results |
| Provider errors/timeout/limits/cancel | PASS | 400/401/403/404/429/500/503/redirect; malformed/HTML/incomplete/oversized/ambiguous JSON; timeout/cancel; no hidden retry | Provider TRX |
| Echoed-secret redaction and rendering | PASS | Exact/control-split echoes in text/model/header and persisted result; partial usage omission; Vue plaintext HTML injection fixture | Provider TRX, 57 frontend tests, `provider-tested.png` |
| Stale test results / duplicate probe prevention | PASS | Delayed probe with intervening update; duplicate admission; pending click and reload tests; no auto replay | Provider TRX, frontend/browser results |
| Browser profile selection / no Run | PASS | Two nodes/two profiles persisted; unresolved ID retained; saved references block delete; metadata retained after session errors; Save never inferred | Browser results, `unresolved-profile.png` |
| User-operated real provider check | **NOT RUN** | User has not entered actual provider details or personally tested before/after restart | No real-provider artifact exists |

Evidence paths in the table are relative to `.artifacts/m2/`. Focused provider TRX contains 68 tests; focused session TRX contains 34 tests (15 M1 API + 19 security). The final complete check is authoritative after the last startup fix.

A first integrated attempt failed eighteen security cases because a restrictive ACL had been applied to a missing shared temporary ancestor. The isolated fixture now uses unique flat roots and startup creates ordinary ancestors before restricting runtime only. Existing shared/global ACLs were not changed to force success. The initial failure remains in `final-check-initial-acl-fixture-failure.log`; final rerun passed. Earlier browser integration failures were a probe assertion reading the previous persisted result too early, and Vite's default CORS header; synchronization and explicit `cors:false` corrected those before the final 17/17 pass.

Visual inspection of the actual provider form/result and retained editor screenshots passed: clear form fields, visible destination and usage disclosure, credential-present status without key, plain-text redacted preview, existing canvas/inspector layout and disabled M3 Run. Evidence: `provider-tested.png`, `unresolved-profile.png`, `editor-1440.png`, `editor-1280.png`.

## Real-provider check — separate evidence

Performed by/date: **NOT RUN**. No user-operated result has been supplied.

Protocol: implemented `openai-responses`; actual model alias and destination classification are not yet supplied. No actual URL, API key, token or credential-store contents are included here.

Before restart: result category/duration/sanitized preview **NOT RUN**.
After restart: actual-profile persistence/key status/second probe **NOT RUN**.

Automated fixtures verified these mechanisms, but neither those fixtures nor a preexisting Codex setup establishes compatibility with the user's provider.

## Changes and scope review

- Backend: new Security and Providers feature folders; additive SQLite migration/snapshot and profile/credential mapping; Program integration; unchanged workflow document shape. Only graph-validation scope wording changed in Core.
- Frontend: shared protected API helper, session gate, model-connection screens/contracts/tests, named provider selector and local readiness. Existing Vue Flow editing/persistence/import/export behavior retained.
- Verification: real-pairing API factory, 19 security and 68 provider tests, retained M1 API tests, new frontend/browser coverage and synthetic controlled provider fixtures. Browser evidence moved to M2; recordings disabled and backend TRX kept separate from browser output cleanup.
- Operations/docs: narrow scope updates, full README startup/pairing/provider/recovery instructions, environment/security/contracts/progress/report, job-object launcher cleanup and exact-project recovery/lifecycle harness.
- Dependencies: only ProtectedData 10.0.12 added; API/test lockfiles updated. SDK pin changed from unavailable 10.0.301 to installed 10.0.401. No frontend upgrades, machine prerequisite install or global Codex configuration change. First SDK use printed its standard per-user development-certificate initialization; no trust-store change was requested or applied.
- User's six untracked assignment/reference files preserved. The working tree intentionally retains all implementation changes uncommitted. No live application database was read/reset for testing.
- Final Git status: 29 tracked files modified and 25 untracked status entries (some are directories), including the six preexisting user inputs. Generated evidence remains ignored under .artifacts/m2; nothing was staged or committed.

Final focused review checked auth boundaries, destination enforcement, transactional credentials, stale results, record/export projections, recording settings, lifecycle ownership, existing-user changes, and absence of M3 execution/fallback code. It found and fixed pairing-file ACL retention, Kestrel listener override, control-normalization redaction and partial-usage presentation issues, each with regression coverage. `git diff --check` passed. Remaining limitations are the explicit Windows/local trust boundary, user-provider gate, unsupported proxy/protocol capabilities and narrow launcher assignment interval; no known failing in-scope automated check remains.

## Exact operator steps

From the repository root, with development/test services stopped:

```powershell
dotnet restore GraphEngineering.slnx --locked-mode
npm ci --prefix apps/web
.\scripts\dev.ps1
```

Open `http://127.0.0.1:5173`. In a second terminal open, do not print, the pairing file:

```powershell
notepad.exe "$env:LOCALAPPDATA\GraphEngineering\runtime\pairing-token.txt"
```

Enter its contents into **Pairing token → Pair local browser**, then close Notepad. Open **Model connections → New connection**. Enter the actual API base URL including prefix, exact model ID, authentication mode and key through this local screen only. For private/HTTP destinations explicitly approve the exact destination/transport. Choose **Save profile**, verify **Credential saved**, then choose **Test connection** after reading the usage notice. Record only the safe category/duration/synthetic preview.

Stop the launcher with Ctrl+C and wait for its prompt. Start again using ` .\scripts\dev.ps1 `, reopen the rotated pairing file, pair, reopen the saved profile and verify **Credential saved**, then explicitly test again. Re-pairing never automatically issues another probe. If the deployment rejects Responses/options/model, retain its sanitized result and stop at this gate; do not add a fallback protocol.

For an isolated manual instance:

```powershell
.\scripts\dev.ps1 -ApiPort 5081 -WebPort 5174 -DataDirectory "$PWD\.artifacts\manual-m2"
# In another terminal:
notepad.exe "$PWD\.artifacts\manual-m2\runtime\pairing-token.txt"
```

Open `http://127.0.0.1:5174`. Verification commands:

```powershell
.\scripts\check.ps1
.\scripts\launcher-lifecycle-test.ps1 -Scenario All -FirstPort 6350 -NoBuild -Label manual-verification
```

Recovery without deleting data:

```powershell
.\scripts\launcher-recover.ps1 -ApiPort 5080 -WebPort 5173
# Only after inspecting and confirming those exact project processes are orphaned:
.\scripts\launcher-recover.ps1 -ApiPort 5080 -WebPort 5173 -Stop
```

Use Ctrl+C in the original terminal when possible. Do not run npm ci while this project's Vite/Vitest holds Rolldown. Never broadly kill node/dotnet, delete the default data directory, paste credentials into chat, or capture real secret-bearing recordings.

## Next gate

The user performs the actual Responses connection check before and after restart and reports only sanitized evidence. The lead reviews M2 with that result and the recorded limitations/incident. Until then: **READY FOR USER PROVIDER CHECK**. No M3 work, commit, push, merge or release is authorized or performed.
