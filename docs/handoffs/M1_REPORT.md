# M1 implementation report

Date: 2026-09-23. Branch: `main`. Starting HEAD: `e9ce367` (Initial commit). All implementation changes are uncommitted; no commit, push, merge, or M2 work was performed.

## Result

**Implemented. M1 is ready for lead review.** The complete integrated check script passed on 2026-09-23 at approximately 22:30 Asia/Kuala_Lumpur. All required acceptance categories were exercised. Acceptance remains a human milestone decision; M2 has not been authorized.

Completed scope: Vue 3/TypeScript/Vue Flow editor, workflow library, per-type inspector, SQLite-backed ASP.NET Core API, strict versioned import/export, optimistic concurrency, current-draft validation, error/dirty/loading states, disabled Run, migrations, automated tests, and Windows startup/check scripts.

Not implemented, as required: provider setup, credentials, model connections, real or simulated execution, coding agents, shell execution, runtime records, routing loops, approval execution, and later milestones. The HTML prototype remains a visual/conceptual reference only.

## Environment

Windows 10.0.26200 win-x64; .NET SDK 10.0.301, installed .NET/ASP.NET runtime 10.0.9; Node 24.11.1; npm 11.6.2; Git 2.52.0.windows.1. npm-path Codex CLI 0.106.0; app-bundled CLI 0.155.0-alpha.16.3, inspected as metadata only. No authentication files were read.

See [ENVIRONMENT.md](../ENVIRONMENT.md) for actual dependency versions and compatibility decisions. `global.json`, `.node-version`, exact package/project versions, `apps/web/package-lock.json`, and four `packages.lock.json` files pin the selected tools/dependencies. No required SDK or browser was missing. Installed Chrome provides the Playwright browser. Registry access used normal approval after an initial sandbox EACCES; no machine-wide prerequisites were installed.

## Local operation

From the repository root in PowerShell:

```powershell
dotnet restore GraphEngineering.slnx --locked-mode
npm ci --prefix apps/web
.\scripts\dev.ps1
```

Application: `http://127.0.0.1:5173`. API health: `http://127.0.0.1:5080/api/health`. Vite proxies `/api` to the loopback backend. Ctrl+C stops only the two directly owned processes. The script refuses occupied ports and writes `.artifacts/dev/` logs.

Full verification:

```powershell
.\scripts\check.ps1
```

Individual commands:

```powershell
dotnet build GraphEngineering.slnx --no-restore
dotnet test GraphEngineering.slnx --no-build --no-restore
npm --prefix apps/web run type-check
npm --prefix apps/web run lint
npm --prefix apps/web test
npm --prefix apps/web run build
npm --prefix apps/web run test:e2e
```

Startup smoke test, using disposable data and dedicated ports:

```powershell
.\scripts\dev.ps1 -ApiPort 5090 -WebPort 5190 -DataDirectory "$PWD\.artifacts\m1\dev-smoke-data" -SmokeTest
```

Default database: `%LOCALAPPDATA%\GraphEngineering\workflows.db`. Set `GRAPH_ENGINEERING_DATA_DIR` or pass `-DataDirectory` to select another containing directory. Checked-in EF Core migrations run during startup. A fresh directory creates a fresh migrated SQLite database; an existing directory preserves saved workflows. Database failures never switch to in-memory storage. See the README for explicit backup/reset instructions; no user database was reset during verification.

## Verification evidence

Final integrated run: **PASS**, `.\scripts\check.ps1`, exit code 0. Full command transcript: `.artifacts/m1/check.log`. The initial sandbox run stopped at locked restore with NU1301 because NuGet socket access was blocked; `.artifacts/m1/check-sandbox-blocked.log` preserves that result. A normal approved retry restored successfully and ran every check. No failing check was skipped or replaced. No required M1 check remains NOT RUN.

| Check | Status and actual method/result | Evidence |
|---|---|---|
| Backend build | PASS, `dotnet build GraphEngineering.slnx --no-restore`, zero warnings/errors | `.artifacts/m1/check.log` |
| Backend tests | PASS, `dotnet test GraphEngineering.slnx --no-build --no-restore -- RunConfiguration.TreatNoTestsAsError=true`: 39 Core and 15 real SQLite API tests, zero skipped | `.artifacts/m1/check.log` |
| Frontend checks | PASS, `npm --prefix apps/web run type-check`, `run lint`, `run test`, `run build`: 37 Vitest/Vue Test Utils cases across four files, production bundle 340.86 kB JS / 117.28 kB gzip | `.artifacts/m1/check.log` |
| Browser checks | PASS, `npm --prefix apps/web run test:e2e`: 12 tests in 7.6 seconds; 12 expected, zero skipped/unexpected/flaky. Includes real canvas editing, connection creation/reconnection, two model nodes, dirty protection and camera persistence | `.artifacts/m1/playwright-results.json` and HTML report |
| Backend process restart | PASS: owned API PID 23380 terminated, PID 3460 started against the same SQLite file; complete saved document compared through HTTP GET and browser export after reload | `.artifacts/m1/backend-restart.json` |
| Structural and semantic validation | PASS, Core/API and real-HTTP checks cover missing Start/End, disconnects/unreachable End, invalid ports/references, branches/merges/cycles, unsupported versions/types, duplicate properties/IDs, Unicode/shape/size limits | Core/API tests and Playwright API specs |
| Node deletion/text safety | PASS in browser: deleting a model removes its incident edges; Delete/Backspace in Prompt edits text without deleting the node | `apps/web/e2e/editor.spec.ts` and final browser report |
| Save failure/concurrency | PASS in browser/API: aborted save keeps edits; real concurrent writers produce one winner and HTTP 409; stale client keeps edits; delayed response preserves newer typing | API tests and browser specs |
| Import/export | PASS in browser: malformed JSON/unknown format/unknown type preserve current draft; valid import round-trips graph/layout under a new identity and leaves original stored document unchanged | Browser specs and frontend tests |
| Dirty navigation/import | PASS in browser: native confirmation dismissed for navigation and valid import; unsaved document retained | Browser cancellation case |
| Browser console/network | PASS in final normal flow; no unexpected console, page, failed-request, or HTTP API errors | Browser normal-flow assertion and final report |
| Visual/responsive inspection | PASS, screenshots inspected at 1440×900 and 1280×720; smaller canvas uses Fit view, asserts every card within bounds, saves camera, and preserves it across restart | Editor screenshots and main browser acceptance case |
| No execution/simulation | PASS, disabled Run asserted in browser; focused source review found no execution endpoints/adapters, browser-storage database, raw HTML rendering, or simulation timers | Source review and browser specs |
| Windows startup script | PASS, smoke command above started real API/Vite, verified both HTTP responses, exited 0 and stopped owned processes | `.artifacts/m1/dev-smoke.log` |
| Dependency audit | PASS, `npm audit --prefix apps/web --json`: zero known vulnerabilities | Environment notes; exact lockfile retained |

The final restart used `.artifacts/m1/e2e-1790173796110-0-chromium/workflows.db` at `2026-09-23T14:29:59.960Z`. This was a real operating-system process replacement, not a browser refresh or an in-memory repository reconstruction. The browser compared workflow metadata, stable node/edge IDs, configuration, connectivity, positions and viewport exactly after the restart.

The browser fixture uses a new `.artifacts/m1/e2e-…/` directory per worker and loopback ports 5187/5188. It directly owns API and Vite process handles, avoiding Windows npm/cmd teardown ambiguity. API xUnit tests use unique temporary SQLite directories and verify applied migrations, matching schema snapshot, failure responses, and cleanup. No test substitutes EF InMemory for SQLite. Network aborts and delayed responses exist only inside explicitly named acceptance tests; the application has no fake runtime behavior.

## Review

Changed files and purpose:

- `src/GraphEngineering.Core/`: UI-independent document records, strict JSON boundary/shape checks, pure graph validation.
- `src/GraphEngineering.Api/`: HTTP endpoints, ProblemDetails, loopback host, body limit, EF SQLite persistence, migration and schema snapshot.
- `tests/GraphEngineering.Core.Tests/`, `tests/GraphEngineering.Api.Tests/`: isolated fixtures and substantive Core/API tests.
- `apps/web/`: Vue/Pinia/Router editor and library, Vue Flow canvas, inspectors, validation panel, styles, frontend tests, browser acceptance fixture/specs, pinned npm manifest/lockfile.
- `GraphEngineering.slnx`, `Directory.Build.props`, `global.json`, `.node-version`: solution, strict build settings, dependency lockfile generation, pinned tools.
- `scripts/dev.ps1`, `scripts/check.ps1`: Windows-friendly startup, smoke mode, and fail-fast checks. Missing tests also fail the check script.
- `README.md`, `docs/ENVIRONMENT.md`, `docs/CONTRACTS.md`, `docs/PROGRESS.md`, this report: operation, contracts, environment, and handoff evidence. `.gitignore` adds application/build/test data exclusions.

Contract and persistence decisions:

- Version 1 separates workflow metadata, definition, and layout. Named ports are `out` and `in`. Every node has a type version. No Vue Flow internals or runtime state are serialized.
- Strict shape errors reject saves; incomplete graph semantics are saveable and clearly diagnosed. Provider references are descriptive identifiers only. M1 validation does not assert executable readiness.
- Metadata/revision are queryable; graph and layout occupy separate JSON columns. Conditional revision update and result read share one SQLite transaction, so a save response cannot accidentally acknowledge another writer's revision.
- Import checks local structure and server validation before replacing the current draft. Imported identity never targets an existing workflow. Creation always generates a fresh server identity.
- Save responses update server-owned identity/revision/timestamps while preserving edits typed after the request began. Failed/conflicting saves keep local edits. Late responses do not redirect an unmounted editor.

Focused review fixes: atomic save response race; strict date/Unicode parsing; actual cycle-edge diagnostics; native edge reconnect inspector synchronization; delayed-save navigation handling; Windows EventLog failure replaced by console logging; camera changes observed after Vue Flow initialization so controls/minimap/focus persist while opening a saved draft stays clean. The camera fix has component regression tests and passed the final browser restart test. `git diff --check` passed; source review found no TODO implementation placeholders, swallowed save success, browser-storage database, raw HTML rendering, or executable/simulated runtime.

No unrelated existing user changes were reset. The initial user-provided planning files and reference remain intact; the README was expanded for the implemented application. No global settings, real credentials, or external repositories were modified.

Deviations from assigned scope: none. Dependency compatibility selections are documented rather than silently downgrading tools. Installed Chrome is used instead of downloading another browser. Execution remains outside M1.

Remaining known M1 defects: none found by the completed tests and focused review. The installed .NET runtime is an older servicing patch; machine updates were not silently performed. This is a local development milestone, with later provider/session/execution security intentionally unimplemented. Browser verification used installed Chrome on this Windows machine; other browsers and release packaging are not part of this M1 acceptance claim.

## Visual evidence

- Editor at 1440×900: `.artifacts/m1/editor-1440.png`.
- Editor at 1280×720 after fitting and saving the camera: `.artifacts/m1/editor-1280.png`.
- Validation error state: `.artifacts/m1/validation-error.png`.
- Playwright HTML report: `.artifacts/m1/playwright-report/index.html`.
- Playwright JSON results: `.artifacts/m1/playwright-results.json`.
- Backend restart identity/database record: `.artifacts/m1/backend-restart.json`.
- Test service logs: `.artifacts/m1/e2e-…/backend.log` and `frontend.log`.
- Startup smoke transcript: `.artifacts/m1/dev-smoke.log`.
- Complete successful verification transcript: `.artifacts/m1/check.log`.

Artifacts contain synthetic test content only and are ignored by Git.

## Milestone decision

Recommended status: **Ready for lead review**. No known M1 acceptance blocker remains. All ten required acceptance categories have implementation and actual verification evidence above. Human acceptance and a separate instruction are required before any M2 work. No automatic merge or release.

## Post-handoff dependency installation repair — 2026-09-23

The user reported `npm ci --prefix apps/web` failing with Windows EPERM while unlinking `@rolldown/binding-win32-x64-msvc/rolldown-binding.win32-x64-msvc.node`. Read-only process/module inspection confirmed that this repository's Vite process, PID 29592, had that exact file loaded. Its file ACL allowed the user full control; this was an active native-module lock. The related API PID 30220 also remained running after its original parent had exited.

Only those two exact project processes were stopped after checking their command lines. A normal approved `npm ci --prefix apps/web` then passed: 335 packages installed, zero audit vulnerabilities. SHA-256 hashes of package.json and package-lock.json were identical before and after repair. No permission changes, broad process termination, dependency version changes, application code changes, or user database reset were needed.

Verification after repair:

- PASS — `npm --prefix apps/web run type-check`.
- PASS — `npm --prefix apps/web run test`: all 37 cases across four files.
- PASS — `npm --prefix apps/web run build`.
- PASS — `.\scripts\dev.ps1 -ApiPort 5090 -WebPort 5190 -DataDirectory "$PWD\.artifacts\m1\install-repair-smoke" -SmokeTest`: backend build with zero warnings/errors; API health and frontend HTTP responses verified; owned smoke processes stopped.
- NOT RUN again — backend test suite and Playwright acceptance: application source and locked dependency versions were unchanged; their earlier full passing results above remain the milestone evidence.

README now distinguishes first setup/dependency refresh from normal startup and explains stopping dev/test processes before npm reinstalls. Project services were left stopped, ready for the user's next `.\scripts\dev.ps1` launch.
