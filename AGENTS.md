# Repository working rules

## Mission and current scope

Build Graph Engineering: a local-first visual workflow editor and execution application for engineers. Use Vue, not React. Read docs/PROJECT_BRIEF.md and docs/ARCHITECTURE.md before implementation. Read docs/PROGRESS.md and the assigned task before each milestone.

M2 is conditionally accepted for development progression. The current authorized implementation scope is **M3 only**, defined in docs/tasks/M3_RUN_EXECUTION.md, docs/tasks/M3_DATA_BINDINGS.md and docs/tasks/M3_CODEX_PROMPT.md, against isolated fixtures. The supplied real-provider text-test screenshot does not confirm the same-profile restart check. Future milestones provide direction, not permission to implement extra features. Deliver working code, not only a plan. Stop at the milestone gate and report evidence.

## Work process

- Inspect the working directory, existing files, Git root/status, repository instructions, and tool versions before editing. Do not assume an empty repository.
- Preserve existing user changes. Do not reset, clean, overwrite unrelated files, modify global Codex settings, or create commits/pushes without explicit authorization.
- State a short implementation plan, then execute it. Resolve minor implementation choices and record consequential decisions. Ask only for genuinely blocking access or a destructive decision.
- Use current stable, mutually compatible dependencies. Pin the selected toolchain and commit lockfiles when the user later commits. Do not introduce preview frameworks or unexplained downgrades.
- Install project dependencies and test tooling only through the normal permission process. Missing SDKs or blocked network access must be reported accurately; never bypass sandbox restrictions.
- Work in bounded pieces: contracts first, then backend and frontend, then integration, then review. Parallel workers may own non-overlapping files after contracts are fixed. One owner integrates changes and dependency updates. Use sequential work when delegation is unavailable.
- Run the application and relevant tests after changes. Do not replace substantive tests with trivial tests, skip failing tests to get green results, or claim unexecuted checks passed.
- End with changed-file summary, actual commands/results, acceptance evidence, remaining risks, and exact local startup instructions. Write the milestone handoff and update PROGRESS.md.

## Architecture constraints

- Frontend: Vue 3, TypeScript, Vite, Vue Flow, Vue Router, Pinia. Use npm for this initial repository unless an existing package manager already governs it.
- Backend: C#, ASP.NET Core on .NET 10 LTS. Persistence: EF Core with SQLite for the local single-user release.
- Keep a small monolith. No microservices, message broker, Kubernetes, event-sourcing framework, plugin marketplace, or generic enterprise framework.
- Separate workflow definition, editor layout, and runtime state. Vue Flow objects must not become the backend execution contract.
- Keep the engine generic. FreeSpin Converter, Match RTP, and similar domain examples become templates later, not built-in application semantics.
- Backend persistence is authoritative. Browser storage is not the workflow database. Do not implement fake persistence or timer-driven fake execution.
- Future model calls, agent processes, and commands execute through backend adapters, never directly from the browser.

## Security and correctness

- Never put real credentials in source code, prompts, browser storage, exported workflows, URLs, logs, screenshots, tests, or handoff reports. Use synthetic test secrets only.
- Do not read or copy Codex authentication files. Developer Codex authentication is separate from app provider credentials.
- Bind local development services to loopback. Do not enable wildcard CORS or expose command execution to the network.
- Do not use `v-html` or equivalent raw HTML rendering for user/model-controlled text. Validate imported documents server-side as well as client-side.
- M3 permits sequential Responses workflow execution through the protected backend adapter. No shell nodes, arbitrary command execution, external repository modifications, coding-agent invocation, automatic inference retry, tools, branching, repair loops or M4 work. Actual provider credentials and real-provider/workflow checks remain user-operated.
- Treat repository scripts, model outputs, and imported graphs as untrusted. A workspace path or Git worktree is not an operating-system sandbox.
- Cancellation is not rollback. A crashed or disconnected worker is not proof that a side effect did not happen. Never promise exactly-once external execution.

## Code and testing

Use small cohesive components and feature folders, explicit contracts, clear names, and normal error handling. Avoid speculative interfaces, redundant abstractions, commented-out code, and comments that merely restate the code. Explain non-obvious invariants where needed.

Use xUnit for backend tests, Vitest and Vue Test Utils for frontend tests, and Playwright for browser verification. API integration tests must exercise SQLite rather than substituting EF's nonrelational InMemory provider. Keep fixture data isolated from user application data.

Report checks as PASS, FAIL, or NOT RUN with reasons. Distinguish implemented, experimentally verified, and still proposed behavior. No automatic merge or release.
