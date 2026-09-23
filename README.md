# Graph Engineering

Graph Engineering is a local Vue graph editor backed by ASP.NET Core and SQLite. M1 provides editable Start, Model Call, and End drafts, structural validation, revision-checked saves, and JSON import/export. M2 adds local browser pairing, protected provider profiles, an explicit Responses text connection probe, and per-node profile selection. **Workflow Run remains disabled until M3.** There is no graph execution, shell node, coding-agent invocation, or simulated run.

M1 is conditionally accepted for development progression. M2 implementation and automated evidence are documented in [the M2 handoff](docs/handoffs/M2_REPORT.md); final real-provider verification is user-operated. The original HTML remains a visual/conceptual reference only.

## Install and start

Prerequisites: Windows, .NET SDK **10.0.401**, Node **24.11.1**, npm **11.6.2**, and PowerShell. Toolchain pins and actual environment details are in [ENVIRONMENT.md](docs/ENVIRONMENT.md). Installed Google Chrome is used for browser checks. Project scripts install no machine-wide prerequisites.

Stop this project's development server/tests with Ctrl+C before installing dependencies. From the repository root:

```powershell
dotnet restore GraphEngineering.slnx --locked-mode
npm ci --prefix apps/web
.\scripts\dev.ps1
```

For later launches, use only ` .\scripts\dev.ps1 `; reinstalling is unnecessary. Open **http://127.0.0.1:5173**. API health is **http://127.0.0.1:5080/api/health**. Both services bind to loopback; Vite proxies `/api` and does not enable cross-origin access. Use the exact numeric address, not an alternate hostname. Logs are in `.artifacts/dev/5080-5173/`.

The launcher builds first, refuses occupied ports, and owns its child processes through a Windows job object. Ctrl+C stops the owned services and their job descendants. Custom ports: `-ApiPort 5081 -WebPort 5174`. `-NoBuild` explicitly reuses an already built API; omit it for normal development. `-SmokeTest` checks startup then exits.

## Pair this browser

A backend launch creates a random token in `%LOCALAPPDATA%\GraphEngineering\runtime\pairing-token.txt`. The file is restricted to the current Windows user. Open it locally, without printing its contents to a captured terminal:

```powershell
notepad.exe "$env:LOCALAPPDATA\GraphEngineering\runtime\pairing-token.txt"
```

Enter the file contents in **Pairing token** at the local application and choose **Pair local browser**. Close Notepad afterward. Do not paste the token or provider credentials into Codex/chat. The backend logs the file's path only. The token rotates on backend restart; existing sessions then require pairing again. Sessions expire after eight hours. Re-pairing preserves in-memory unsaved metadata and never automatically repeats a connection test; transient API key fields clear.

For isolated manual data and separate ports:

```powershell
.\scripts\dev.ps1 -ApiPort 5081 -WebPort 5174 -DataDirectory "$PWD\.artifacts\manual-m2"
```

Open `http://127.0.0.1:5174` and, from another terminal, open its token file:

```powershell
notepad.exe "$PWD\.artifacts\manual-m2\runtime\pairing-token.txt"
```

For direct API/Vite startup, both must agree on the exact browser origin. The launcher sets `GRAPH_ENGINEERING_BROWSER_ORIGIN` and `VITE_API_TARGET` for its children and restores the parent environment. Kestrel endpoint overrides are unsupported and rejected; configure loopback listeners through `--urls`. Browser/proxy/antiforgery protection is required, including during local development. No authentication bypass flag exists.

## Add and test a model connection

1. Open **Model connections → New connection**.
2. Enter a display name, the exact model ID, and the API base URL **including its API prefix**, such as `https://provider.example/custom/v1`. The screen shows the final `/custom/v1/responses` endpoint. Do not enter the full `/responses` method URL. Queries, fragments and URL credentials are rejected.
3. Select **Bearer API key**, choose **Replace / enter credential**, and type the actual key only in this local application. For a private/loopback provider, explicitly approve that exact destination. HTTP additionally needs the unencrypted-transport acknowledgment. No-auth is permitted only for explicitly approved private/loopback destinations.
4. Choose **Save profile**. It stores settings and a Windows DPAPI-protected credential; it does not generate text. Named hosts must resolve within five seconds and pass destination policy even on save. A blank key never removes a saved credential. Use Keep, Replace or Remove explicitly.
5. Choose **Test connection** only when ready to incur possible provider usage. It sends only `Reply with GE_CONNECTION_OK.`, the saved model ID, `stream:false`, `store:false`, and the bounded output limit. It does not send the workflow or repository. The provider's retention policy remains separate from `store:false`.
6. Inspect the result category, duration and sanitized plain-text preview. A completed nonempty assistant text response verifies text for that saved connection version. Phrase matching is shown separately. HTTP 200 with malformed, HTML, incomplete or empty output fails. Streaming, tools and JSON-schema output remain untested/unimplemented.
7. Stop with Ctrl+C, restart with the same command/data directory, pair using the new token, reopen the profile, verify **Credential saved**, and explicitly test again. This is the required user-operated persistence/provider gate.

The automated fixtures do not prove compatibility with your actual provider. Until you personally complete step 7, status is **READY FOR USER PROVIDER CHECK**, with the real-provider check **NOT RUN**. Report only result category, duration and a sanitized synthetic preview; obscure internal hostnames if needed. Do not record real secret-bearing browser traces, HARs, videos, screenshots, request headers or credential-store contents.

A profile edit requires its current revision. Changing the destination or authentication requires confirmation and fresh bearer-key entry; an old key is not reused at a new destination. Name/model edits preserve the credential, while connection changes invalidate verification. Save before testing. Pending duplicate probes are rejected; refresh never retries a probe. Cancel/timeout is best effort and does not guarantee the provider stopped or waived charges.

HTTPS uses normal certificate verification; there is no trust-all switch. Redirects, metadata/link-local/multicast/unspecified addresses and the app's own endpoints are blocked. DNS is checked again at actual socket connection, and that validated IP is dialed directly. Ambient proxies, cookies and OS credentials are disabled. Deployments requiring a proxy or another wire protocol are unsupported in M2; errors do not silently select Chat Completions.

## Work with drafts

Create a workflow for an editable Start → Model Call → End graph. Add nodes, connect handles, drag cards and edit the inspector. Each Model Call can select a different saved **Provider profile**. The inspector distinguishes configured, previously text-tested and unresolved references. Selection and validation never call the provider. Missing profiles remain saveable drafts. A profile referenced by a saved workflow cannot be deleted until those references are changed and saved.

Save persists; Validate checks the current draft's structure and configuration. Missing Start/End, disconnected nodes, branches, cycles and unfinished prompts produce issues but can be saved. Malformed documents, unsupported versions, duplicate IDs and corrupt ports/references are rejected. Export includes current unsaved edits; import validates before replacement and creates a fresh workflow identity. On save errors or revision conflicts, edits remain available to retry/export. Workflow JSON contains only provider IDs, never profile endpoints, permissions, test history or credentials.

## Persistence and security boundary

SQLite is authoritative at `%LOCALAPPDATA%\GraphEngineering\workflows.db`; `GRAPH_ENGINEERING_DATA_DIR` or the launcher's `-DataDirectory` overrides the directory. Additive EF migrations run at startup and preserve M1 workflows. No global EF tool is needed. Database failure is an error, not an in-memory fallback.

Windows DPAPI CurrentUser protects credentials in a separate transactional SQLite record. Public views show only `hasCredential`. Corrupt/unreadable credentials require replacement. Copying a database to another Windows account/machine may require reentry. Removing credentials is logical deletion, not secure erasure of old SQLite pages, WALs or backups. Do not reset the data directory to repair a connection or session problem. Back up data only with the app stopped; verification uses isolated data and never inspects the live credential store.

Local security protects against unrelated web pages and unsafe provider destinations. It does not protect against an administrator or malware running as the same Windows user. HttpOnly SameSite Strict cookies, exact Host/Origin and antiforgery checks protect private API actions. Secure cookies are used on HTTPS; development HTTP is loopback only. See [SECURITY.md](docs/SECURITY.md) for the implemented boundary and [CONTRACTS.md](docs/CONTRACTS.md) for limits and update semantics.

## Verify and recover

Run the complete suite after dependencies are installed and development services are stopped:

```powershell
.\scripts\check.ps1
.\scripts\launcher-lifecycle-test.ps1 -Scenario All -FirstPort 6340 -NoBuild -Label manual-verification
```

The check script runs locked restore, backend build/xUnit, frontend type-check/lint/Vitest/build and real Playwright browser tests. `-SkipBrowser` marks those checks NOT RUN. The lifecycle harness separately tests native Ctrl+C after the ready prompt, controlled sibling partial-start failure, and abrupt launcher termination using owned processes/isolated ports. It may require the normal process-inspection permission prompt. Individual checks:

```powershell
dotnet build GraphEngineering.slnx --no-restore
dotnet test GraphEngineering.slnx --no-build --no-restore
npm run type-check --prefix apps/web
npm run lint --prefix apps/web
npm test --prefix apps/web
npm run build --prefix apps/web
npm run test:e2e --prefix apps/web
```

Browser checks require free ports 5187/5188, installed Chrome, and isolated synthetic provider ports. Evidence is in `.artifacts/m2/`. Traces/video/automatic failure screenshots are disabled; explicit screenshots are taken only with pairing/key fields cleared. `PLAYWRIGHT_CHANNEL` may select another already installed supported channel.

Windows `npm ci` can fail with `EPERM ... unlink ... rolldown-binding.win32-x64-msvc.node` while Vite/Vitest is running. Stop this project's development/tests first; the error alone does not require administrator mode or changed ACLs. To inspect possible orphan launcher services for this exact repository and chosen ports:

```powershell
.\scripts\launcher-recover.ps1 -ApiPort 5080 -WebPort 5173
```

Prefer Ctrl+C in the original terminal. Only after reviewing the displayed exact repository processes and confirming they are orphaned, use:

```powershell
.\scripts\launcher-recover.ps1 -ApiPort 5080 -WebPort 5173 -Stop
npm ci --prefix apps/web
```

This helper matches repository command paths and ports, rechecks process identity, and stops retained direct process handles. It never broadly kills node/dotnet or deletes data. Job ownership covers tested interruptions, but a small process-start-to-job-assignment window and OS failure cases remain; recovery is not a promise that every possible crash is handled. See the M2 handoff for lifecycle evidence and the corrected test-harness incident.

M2 is the only currently authorized milestone. Read `AGENTS.md`, `docs/PROGRESS.md`, `docs/tasks/M2_CODEX_PROMPT.md` and referenced documents before further work. No commit, push, merge or M3 is authorized. Sources: [M2 references](docs/M2_SOURCES.md), [original references](docs/SOURCES.md).
