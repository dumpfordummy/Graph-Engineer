# Graph Engineering

Graph Engineering is a local Vue graph editor backed by ASP.NET Core and SQLite. M1 provides editable Start, Model Call, and End drafts, structural validation, revision-checked saves, and JSON import/export. M2 adds local browser pairing, protected provider profiles, an explicit Responses text connection probe, and per-node profile selection. M3 adds explicit data bindings, sequential Responses runs, immutable SQLite history, cancellation and authenticated live notifications. Legacy or incomplete drafts remain non-runnable until explicitly configured and saved. There are no shell nodes, coding agents, model tools, automatic inference retries, token streaming, branching, repair loops or simulated runs.

M2 is conditionally accepted for progression to M3 against isolated fixtures. A successful user text-probe screenshot was supplied; the same-profile before/after-restart probe remains unconfirmed. M3 evidence and the user workflow gate are documented in [the M3 handoff](docs/handoffs/M3_REPORT.md). The original HTML remains a visual/conceptual reference only.

## Install and start

Prerequisites: Windows, .NET SDK **10.0.401**, Node **24.11.1**, npm **11.6.2**, and PowerShell. Toolchain pins and actual environment details are in [ENVIRONMENT.md](docs/ENVIRONMENT.md). Installed Google Chrome is used for browser checks. Project scripts install no machine-wide prerequisites.

Stop this project's development server/tests with Ctrl+C before installing dependencies. From the repository root:

```powershell
dotnet restore GraphEngineering.slnx --locked-mode
npm ci --prefix apps/web
.\scripts\dev.ps1
```

For later launches, use only ` .\scripts\dev.ps1 `; reinstalling is unnecessary. Open **http://127.0.0.1:5173**. API health is **http://127.0.0.1:5080/api/health**. Both services bind to loopback; Vite proxies `/api` and `/hubs` WebSockets and does not enable cross-origin access. Use the exact numeric address, not an alternate hostname. Logs are in `.artifacts/dev/5080-5173/`.

The launcher builds first, refuses occupied ports, and owns its child processes through a Windows job object. Ctrl+C stops the owned services and their job descendants. Custom ports: `-ApiPort 5081 -WebPort 5174`. `-NoBuild` explicitly reuses an already built API; omit it for normal development. `-SmokeTest` checks startup then exits.

## Pair this browser

A backend launch creates a random token in `%LOCALAPPDATA%\GraphEngineering\runtime\pairing-token.txt`. The file is restricted to the current Windows user. Open it locally, without printing its contents to a captured terminal:

```powershell
notepad.exe "$env:LOCALAPPDATA\GraphEngineering\runtime\pairing-token.txt"
```

Enter the file contents in **Pairing token** at the local application and choose **Pair local browser**. Close Notepad afterward. Do not paste the token or provider credentials into Codex/chat. The backend logs the file's path only. The token rotates on backend restart; existing sessions then require pairing again. Sessions expire after eight hours. Re-pairing preserves in-memory unsaved metadata and never automatically repeats a connection test or submits a workflow run; transient API key fields clear.

For isolated manual data and separate ports:

```powershell
.\scripts\dev.ps1 -ApiPort 5081 -WebPort 5174 -DataDirectory "$PWD\.artifacts\manual-m3"
```

Open `http://127.0.0.1:5174` and, from another terminal, open its token file:

```powershell
notepad.exe "$PWD\.artifacts\manual-m3\runtime\pairing-token.txt"
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

The automated fixtures do not prove compatibility with your actual provider. The supplied successful text-test screenshot does not confirm step 7. M2-U1 remains **NOT RUN / unconfirmed** until that same-profile restart result is supplied; it is separate from the M3 workflow check. Report only result category, duration and a sanitized synthetic preview; obscure internal hostnames if needed. Do not record real secret-bearing browser traces, HARs, videos, screenshots, request headers or credential-store contents.

A profile edit requires its current revision. Changing the destination or authentication requires confirmation and fresh bearer-key entry; an old key is not reused at a new destination. Name/model edits preserve the credential, while connection changes invalidate verification. Save before testing. Pending duplicate probes are rejected; refresh never retries a probe. Cancel/timeout is best effort and does not guarantee the provider stopped or waived charges.

HTTPS uses normal certificate verification; there is no trust-all switch. Redirects, metadata/link-local/multicast/unspecified addresses and the app's own endpoints are blocked. DNS is checked again at actual socket connection, and that validated IP is dialed directly. Ambient proxies, cookies and OS credentials are disabled. Deployments requiring a proxy or another wire protocol are unsupported; errors do not silently select Chat Completions.

## Work with drafts

Create a workflow for an editable Start → Model Call → End graph. Add nodes, connect handles, drag cards and edit the inspector. Each Model Call can select a different saved **Provider profile**. The inspector distinguishes configured, previously text-tested and unresolved references. Selection and validation never call the provider. Missing profiles remain saveable drafts. A profile referenced by a saved workflow or any active run cannot be deleted. Editing the current workflow does not remove its active run snapshot reference.

Save persists; Validate checks the current draft's structure and configuration. Missing Start/End, disconnected nodes, branches, cycles and unfinished prompts produce issues but can be saved. Malformed documents, unsupported versions, duplicate IDs and corrupt ports/references are rejected. Export includes current unsaved edits; import validates before replacement and creates a fresh workflow identity. On save errors or revision conflicts, edits remain available to retry/export. Workflow JSON contains only provider IDs, never profile endpoints, permissions, test history or credentials.

## Persistence and security boundary

SQLite is authoritative at `%LOCALAPPDATA%\GraphEngineering\workflows.db`; `GRAPH_ENGINEERING_DATA_DIR` or the launcher's `-DataDirectory` overrides the directory. Additive EF migrations run at startup and preserve existing workflows, profiles and protected credentials. No global EF tool is needed. Database failure is an error, not an in-memory fallback.

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

Browser checks require free ports 5187/5188, installed Chrome, and isolated synthetic provider ports. Current evidence is in `.artifacts/m3/`; historical M2 evidence is retained. Traces/video/automatic failure screenshots are disabled; explicit screenshots are taken only with pairing/key fields cleared. `PLAYWRIGHT_CHANNEL` may select another already installed supported channel.

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

M3 is the only currently authorized milestone. Read `AGENTS.md`, `docs/PROGRESS.md`, `docs/tasks/M3_CODEX_PROMPT.md` and referenced documents before further work. No staging, commit, push, merge or M4 is authorized. Sources: [M3 references](docs/M3_SOURCES.md), [M2 references](docs/M2_SOURCES.md), [original references](docs/SOURCES.md).

## Run a saved workflow (user-operated provider check)

Start with a small literal-text workflow:

1. Choose **New workflow**. Select its Model Call, choose **Upgrade for execution**, select your saved **Provider profile**, keep **Prompt mode → Literal text**, and enter `Reply with a short greeting.` Keep **Output interpretation → Text**. Braces in literal prompts are sent unchanged.
2. Select End, choose **Upgrade for execution**, then **Final result source → Earlier node text** and select that Model Call. Its descriptive Result reference is not an expression.
3. Choose **Save**, inspect **Run readiness**, then **Run**. Enter `{}` as **Run input JSON object**. Start's sample text is not runtime input. Review the saved revision, ordered profiles/models and maximum call count, confirm provider usage and local retention, then **Confirm and run**.
4. In **Runs**, select the model node. Inspect its concrete prompt, completed text, timestamps, actual supplied usage and external outcome. Verify **Succeeded** and the final result. Refresh/reopen this same run without submitting another one.

Then configure the two-node JSON example:

1. Create a separate new workflow. Rename its first Model Call **Double value**. Add another **Model Call**, name it **Multiply by ten**, and use **Fit view** if needed. Select the existing Double value → End connection and change **Target node** to Multiply by ten, then **Reconnect edge**. Select Multiply by ten, choose **Connect to → End**, then **Add connection**. The path must be Start → Double value → Multiply by ten → End; remove any extra connections.
2. Upgrade Double value, independently choose a saved provider profile, choose **Explicit bindings**, and **Add input binding**. Alias `x`, source **Run input**, JSON pointer `/x`. Set its prompt to `Take the number {{inputs.x}}, multiply it by 2, and return only one JSON object with a numeric varX property. No explanation or markdown.` Choose **JSON object — local validation**.
3. Upgrade Multiply by ten, choose its provider independently, choose **Explicit bindings**, and add alias `varX`, source **Earlier node JSON**, node **Double value**, pointer `/varX`. Prompt: `Take the number {{inputs.varX}}, multiply it by 10, and return only one JSON object with a numeric varY property. No explanation or markdown.` Choose **JSON object — local validation**.
4. Upgrade End. Choose **Earlier node JSON**, node **Multiply by ten**, and leave its JSON pointer empty to select the whole object. Save, review readiness, and open Run. Enter `{"x":7}`, review the two possible model calls, confirm usage/retention, and submit once.
5. Inspect Double value's actual JSON and Multiply by ten's **Resolved inputs** and **Resolved prompt**. Expected arithmetic is `{"varX":14}` followed by `{"varY":140}`; the application does not compute, repair or guarantee it. The second request must use the first returned value. Invalid/fenced/duplicate JSON stops the run without another inference attempt.
6. Open **Frozen workflow and run input** and inspect the saved revision. Editing the current design cannot rewrite this history. Refresh and reopen the same completed run, then stop the launcher with Ctrl+C. Restart with the same data directory, pair using the rotated token, and reopen the completed run from **Runs**. This must not send a new provider request. Report only sanitized outcome/revision/values; no keys or internal URLs.

Run requires a saved, clean, executable revision. Save/import/profile selection never sends inference. Only one active run is admitted; a distinct submission while busy returns a conflict. A lost submission response retains its opaque ID in the page URL: use **Look up submission** or **Runs history**, rather than submitting again. Re-pairing or reconnecting only resumes observation. Connection changes during a run stop later nodes with `configuration_changed`; changing a name alone does not change a connection version.

Cancel records an explicit request and stops further local progression; an already dispatched provider request may still complete or incur charges. On backend replacement all unfinished runs, including queued work, become **Interrupted**. They are never resumed automatically. Inspect the durable history and decide explicitly whether a new run is appropriate. Do not delete the database to recover from an interrupted run.

Run input, resolved prompts, successful output and artifacts are retained in SQLite **without DPAPI encryption**. Protect the database, WAL and backups as sensitive content. Workflow export contains definitions only. Limits are 16 model calls/run, 32 bindings/node, 16 KiB input, 64 KiB rendered prompt, 256 KiB provider body, 128 KiB completed text, JSON depth 32 and a ten-minute run deadline. Provider timeout/token limits still apply. Full accepted values are retained; there is no silent successful truncation.

Focused synthetic recovery/security commands (in addition to the full check):

```powershell
dotnet test tests/GraphEngineering.Api.Tests/GraphEngineering.Api.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~ProcessExecutionTests
dotnet test tests/GraphEngineering.Api.Tests/GraphEngineering.Api.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~HubSecurityTests
npm --prefix apps/web run test:e2e -- --grep 'explicit two-node|literal text is unmodified|invalid JSON fails|cancelling an observed'
```

These tests create only isolated fixtures and stop only processes they own. The user-operated real workflow result is **NOT RUN** until supplied. Automated fixture success does not establish real-provider mathematical accuracy, billing or retention behavior.
