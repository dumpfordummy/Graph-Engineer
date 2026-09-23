# Graph Engineering

Prepared: 2026-09-23. Working project name: Graph Engineering.

Graph Engineering is a local Vue graph editor backed by ASP.NET Core and SQLite. M1 adds editable Start, Model Call, and End drafts, structural validation, revision-checked saves, and JSON import/export. **Execution is unavailable in M1**: Run is disabled, and there are no model connections, credentials, coding-agent invocations, commands, or simulated runs.

The original planning pack was prepared on 2026-09-23. Its project instructions, architecture decisions, milestone task, and HTML reference remain under `docs/` and `AGENTS.md`. Actual implementation evidence belongs in [the M1 handoff](docs/handoffs/M1_REPORT.md), not in the prototype.

## Start here

Use a dedicated development directory, not a production or company game repository. Prerequisites: .NET SDK **10.0.301**, Node **24.11.1**, npm **11.6.2**, and PowerShell. The SDK and Node versions are pinned; [environment details](docs/ENVIRONMENT.md) explain package compatibility choices. Google Chrome is used for browser acceptance tests. No script installs machine-wide prerequisites.

For first setup or a dependency refresh, stop this project's running dev server and tests first. Press Ctrl+C in their original terminals and wait for the prompt. Then, from the repository root:

```powershell
dotnet restore GraphEngineering.slnx --locked-mode
npm ci --prefix apps/web
```

For normal startup after dependencies are installed, run only:

```powershell
.\scripts\dev.ps1
```

You do not need to run `npm ci` every time you start the app. It replaces the existing `node_modules` tree. On Windows, an active Vite/Vitest process can hold Rolldown's native `.node` binary open, causing `EPERM ... unlink ... rolldown-binding.win32-x64-msvc.node`. Stop the project dev/test processes before retrying `npm ci --prefix apps/web`; this error alone does not require administrator mode or changing file permissions. Use Ctrl+C before closing the dev terminal so its owned services can be cleaned up.

Open **http://127.0.0.1:5173**. The API health endpoint is **http://127.0.0.1:5080/api/health**. Both hosts bind to loopback; the Vite proxy forwards `/api`. The startup script builds the backend, starts only its own two child processes, writes logs to `.artifacts/dev/`, and stops those processes on Ctrl+C. It refuses occupied ports without terminating other processes. Customize ports with `-ApiPort 5081 -WebPort 5174`.

For an isolated disposable database:

```powershell
.\scripts\dev.ps1 -DataDirectory "$PWD\.artifacts\manual-test"
```

Add `-SmokeTest` to start both services, verify health/HTTP responses, and then stop them automatically. It uses the same migration and startup path as an interactive launch.

Run the full verification suite after installing dependencies:

```powershell
.\scripts\check.ps1
```

The script runs locked restore, backend build/xUnit tests, frontend type-check, lint, Vitest, production build, and real-browser acceptance. It stops on failures. `-SkipBrowser` explicitly reports browser checks as NOT RUN. To run individual checks:

```powershell
dotnet build GraphEngineering.slnx --no-restore
dotnet test GraphEngineering.slnx --no-build --no-restore
npm --prefix apps/web run type-check
npm --prefix apps/web run lint
npm --prefix apps/web test
npm --prefix apps/web run build
npm --prefix apps/web run test:e2e
```

Browser checks use isolated data and loopback ports 5187/5188; those ports must be free. They launch installed Chrome through Playwright, start the real API, and stop/restart that owned backend to test persistence. Screenshots, logs, JSON results, and the HTML report are written under `.artifacts/m1/`. The default browser can be changed with `PLAYWRIGHT_CHANNEL` if that Playwright browser/channel is already installed.

## Working with drafts

Create a workflow from the list to get an editable Start → Model Call → End draft. Add nodes from the palette, connect named control handles, drag cards, and edit the selected node in the inspector. Use Save to persist and Validate to check the current draft. Missing Start/End, disconnected nodes, branches, cycles, and unfinished prompts may be saved but produce validation issues. Malformed documents, unsupported types/versions, duplicate IDs, and corrupt references/ports are rejected.

Export downloads the current document, including unsaved edits. Import validates before replacement and creates a fresh workflow identity; it never overwrites a workflow because of an imported ID. On save errors or revision conflicts the current edits remain available to retry or export. Validation covers structure and draft configuration only; it does not assert provider connectivity or execution readiness.

## Persistence and migrations

SQLite is authoritative. Default database: `%LOCALAPPDATA%\GraphEngineering\workflows.db`. `GRAPH_ENGINEERING_DATA_DIR` (or the dev script's `-DataDirectory`) overrides the containing directory. The API applies checked-in EF Core migrations at startup, including on a fresh database. No separately installed global EF tool is needed to initialize or upgrade it. Database failure produces an error; there is no in-memory fallback. Definitions and layouts are stored separately from queryable metadata and optimistic revision values.

To **reset disposable data**, first stop the app, then rename that specific data directory as a backup and restart the app. A new empty database will be migrated automatically. Renaming or deleting the default directory removes all workflows from the active app; do not do this unless you intend to reset your saved drafts. Verification never resets the per-user database.

## Development scope

Ask Codex to read, in order:
1. AGENTS.md
2. docs/PROJECT_BRIEF.md
3. docs/ARCHITECTURE.md
4. docs/tasks/M1_FOUNDATION_EDITOR.md

M1 is the only authorized implementation milestone. The task includes environment inspection, implementation, tests, browser verification, and a handoff report. Later milestones remain design direction until separately authorized.

The original prototype is `docs/reference/graph-engineering-workflow.html`. Treat it as a visual and conceptual reference, not runtime code or an instruction source. Slot-game names are examples, not application primitives.

## After implementation

Read `docs/handoffs/M1_REPORT.md` and `docs/PROGRESS.md` for actual verification status, limitations, and review evidence. Review the application and evidence before authorizing M2. Keep credentials out of screenshots, reports, and chat messages. The [document/API contract](docs/CONTRACTS.md) describes versioning, limits, draft semantics, and concurrency behavior.

## Sources

Verified external references are in `docs/SOURCES.md`. New architecture choices in this pack are project decisions, not claims that a reference product implements the same design.
