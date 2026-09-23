# M1 task — build a persistent, editable graph

## Assignment

Implement M1 end to end. Do not stop after analysis, scaffolding, or creating documentation. Stop after the acceptance gate and report. Do not implement provider connections, credentials, real/simulated runs, shell execution, coding-agent invocation, routing loops, approvals, or later milestones.

Read AGENTS.md, PROJECT_BRIEF.md, and ARCHITECTURE.md first. Read the prototype as a visual reference only. Its illustrative runs and domain-specific prompts are not M1 requirements. The current architecture documents take precedence over the prototype for this application.

## A. Inspect and pin the environment

Inspect existing files, Git root/status and instructions, and versions of git, dotnet, Node, and npm. Check Codex availability/version only as environment metadata; do not invoke a nested coding-agent task or read authentication files.

Use .NET 10 LTS and a supported Node LTS compatible with the chosen stable Vite/Vue tooling. Record actual tool/package versions and missing prerequisites in docs/ENVIRONMENT.md. Pin SDK/package versions and lockfiles. Do not assume preinstalled versions or silently downgrade. Use the normal permission workflow for project dependency installation. A missing SDK is a reportable blocker, not permission for a machine-wide install.

Create the minimal structure from ARCHITECTURE.md, respecting existing code. Use Vue 3 Composition API with TypeScript, Vue Flow, Router and Pinia. Use ASP.NET Core, EF Core/SQLite and migrations. Do not add a generic workflow framework or a large UI-kit dependency merely to create three forms. Modest scoped CSS and reusable local components are sufficient for M1.

## B. Define the document contract before building screens

Define a version-1, UI-independent saved document with workflow metadata, stable node/edge IDs, definition and layout stored separately. Include a draft revision for optimistic concurrency. Node types are Start, Model Call, and End; each has an explicit type version.

Start configuration can contain a sample text input. Model Call configuration contains a user-editable name/description, prompt, and optional future provider-profile reference; no key or endpoint credential fields. End contains a simple result reference or descriptive placeholder configuration, not executable code. Persist this configuration without inventing a template interpreter.

Define named control ports and legal direction for each type. Keep positions, selection, Vue component references, and transient run statuses out of the execution definition. Only persist intended layout properties, not framework internals.

A missing provider must not stop draft editing or saving. Structure validation must say that provider configuration and execution readiness are not checked in M1. Do not label this workflow executable.

## C. Implement real persistence and validation

Provide API endpoints to list workflows, create, retrieve, update with expected revision, and validate the current unsaved document. Add a health endpoint. Use coherent JSON contracts and meaningful ProblemDetails-style errors.

Store data in a local per-user application directory, not a tracked repository file; tests use isolated temporary paths. Add EF migrations and a repeatable initialization command or documented development startup migration strategy. Preserve stored data across application restarts.

Reject malformed JSON/document shape, unsupported format/type versions, duplicate IDs, corrupt edge references/ports, and oversized payloads with field-addressable errors. Validate the current draft for exactly one Start/End, connectivity, reachable End, unsupported branches, and unsupported cycles. Incomplete semantics can be saved as drafts. Document and test the distinction between rejected serialization and an invalid-but-saveable draft.

Save must check expectedRevision atomically. Return a conflict for stale updates and leave the server document unchanged. The client must retain its unsaved work after a network error or conflict.

Do not use browser localStorage as the workflow database. Do not silently substitute an in-memory repository when SQLite fails. Do not swallow save errors.

## D. Build the actual editor

Workflow list: create a new workflow, display saved workflows, open an existing one. Do not generate fake run statistics.

Editor layout: toolbar; left node palette; center Vue Flow canvas; right inspector; bottom validation panel. Apply a coherent desktop-first dark theme, accessible contrast, keyboard focus, readable labels and loading/error states. Check at 1440×900 and 1280×720. Preserve the graph if panels need to collapse at smaller widths.

Canvas: add the three supported node types, select, drag, connect/reconnect, delete nodes/edges, pan, zoom, fit view, and show a minimap. Start with a new sample draft Start → Model Call → End. It must be real editable graph data, not hardcoded visual markup. Deleting a node removes its attached edges. Do not fire graph deletion shortcuts while typing in an input or textarea.

Inspector: edits the selected node's supported fields and immediately updates the draft. Name/description/prompt fields must remain understandable. Show appropriate fields per type; do not present nonfunctional configuration controls as working features.

Toolbar: editable workflow name, Save, Validate, Export JSON, Import JSON, and a disabled Run button labeled with the execution milestone limitation. Show unsaved/saving/saved/error states and protect unsaved changes during navigation/import.

Import/export: export the versioned UI-independent document, without secrets, runtime records, or Vue internals. Validate imports before replacing the current draft. A failed import must leave the existing document unchanged. Importing creates a new local workflow identity; never overwrite another saved workflow merely because an imported document carries its ID. Preserve configuration, connectivity and layout on a valid round trip.

Validation: run against the current edited document, not just the last saved copy. Display actionable errors and select/focus the affected node or edge where appropriate. Structural validity must not imply successful model connectivity or executable readiness.

## E. Tests and observable acceptance criteria

Implement meaningful backend unit tests, API integration tests with real SQLite, and frontend tests. Use Playwright against the real application and API; not exclusively mocked HTTP requests.

Required checks:
1. A fresh local database and application start successfully; health and workflow list work.
2. Create/edit/save/reload a workflow with two Model Call nodes in a linear path; configuration, IDs, connections, positions and metadata survive.
3. Stop and restart the backend against the same database and verify persistence, not just a browser refresh. Record the method used.
4. Validation reports a missing Start/End, disconnected node, invalid edge/port, unsupported branch, and cycle with useful diagnostics. Draft saving behaves according to the documented structural/semantic boundary.
5. Node deletion removes incident edges. Editing prompt text with Delete/Backspace does not delete the node.
6. Simulated save failure preserves unsaved edits. Two stale clients cannot silently overwrite one another; stale save returns a conflict.
7. Export/import round-trips supported content. Malformed, unknown-version, and unknown-type imports are rejected without damaging the current draft or overwriting another workflow.
8. Frontend type-check, lint, tests and production build pass; backend build and tests pass.
9. Browser inspection has no unexpected console errors or unhandled failed API requests during the normal save/load flow. Capture screenshots of the editor and a validation error state in .artifacts/m1/.
10. Run is visibly disabled. No timer pretends to run nodes; no real model call, secret handling, or target-repository modification exists.

When tools, downloads, or browser execution are blocked, record NOT RUN and the exact blocker. Do not claim full acceptance. Complete all independent work still possible without bypassing permissions.

## F. Deliverables and stopping point

Provide scripts/dev.ps1 and scripts/check.ps1 (or equally explicit Windows-friendly equivalents) with correct exit handling and documented prerequisites. A new developer should not need to guess the startup or check commands. The dev script must not kill unrelated processes or install machine-wide dependencies silently.

Update README.md with actual implemented startup, test, migration, data-location and reset instructions, while retaining the distinction between M1 and the future runtime. Explain any destructive reset command; do not execute it on user data during validation.

Write docs/handoffs/M1_REPORT.md using its template. Include real command results, acceptance evidence, screenshots, file-change summary, contract decisions and deviations, unresolved defects, and unrun checks. Update docs/PROGRESS.md accurately.

Perform a final focused review for unrelated changes, weak tests, swallowed errors, fabricated behavior, and leaked data. Fix in-scope defects. Do not begin M2, auto-commit, push, or merge. End by giving the user exact commands to start the application and the location of the handoff report.
