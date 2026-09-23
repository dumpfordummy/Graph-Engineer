# Product brief

## Product decision

Graph Engineering lets an engineer design, configure, execute, inspect, and approve a workflow made of model calls, coding agents, deterministic tool steps, routes, and human gates.

The first audience is one engineer working locally on Windows. Keep the core generic so other engineering workflows can be added. The first domain template will be the user's slot-game development process, but the first application smoke test must use synthetic inputs and a disposable example repository.

## Design references

Use Dify as the reference for node configuration and explicit inputs, n8n as the reference for execution inspection, and Flowise as the reference for agent/tool/control-flow distinctions. These are behavioral references. Create original components and styling; do not copy their source, branding, or assets.

The original HTML has an inspectable workflow with Overview, Inputs, Prompt, Output, and Routing. Preserve the useful inspector idea, not its hardcoded execution simulation. Keep prompts and detailed results out of oversized canvas cards.

## Target experience

An engineer configures a model connection or coding-agent runner, chooses a workspace, creates a graph, edits each node's input/output contract and instructions, validates it, and runs a saved version. During a run, the engineer sees actual status, resolved inputs, outputs, tool evidence, errors, and attempts. Human approval pauses execution at defined boundaries.

Design and Runs are different views. Editing a draft must not change an already-started run. A connection expresses control flow; input mappings explicitly select data. Connecting two nodes must not implicitly copy the entire prior conversation.

## First delivery target

M1 produces an editor backed by SQLite. The user can create a Start → Model Call → End draft, configure it, change its layout, save it, restart the app, reopen it, and validate graph structure. The Run button is disabled with an honest milestone explanation. There are no real or simulated executions in M1.

M2 adds provider setup. M3 establishes the first real model-call run. M4 adds durable approval, command execution, and a local coding-agent adapter. M5 adds bounded repair routing and the first engineering template.

The complete pilot is Entry approval → Converter → Build + Tests → Reviewer → Exit approval, with a bounded route back to the converter. Git merging remains a human action outside the initial application.

## Decisions for v1

- Local web application, one trusted local user, loopback binding. No SaaS, accounts, tenant management, billing, or remote workers.
- Vue 3 + TypeScript + Vue Flow frontend; ASP.NET Core/.NET 10 backend; SQLite persistence.
- Sequential execution first. Parallel code-writing agents and multi-workspace scheduling are later work.
- Generic node types with domain templates. No hardcoded slot mechanics in the editor or scheduler.
- Two separate execution concepts: a direct model call via a provider adapter, and a coding agent via a runner adapter. An API key and URL alone do not define a coding-agent environment.
- A provider profile stores endpoint/protocol/model metadata plus a secret reference. A runner profile stores executable, workspace policy, and supported capabilities. They are not interchangeable.
- No automatic Git merges, branch-protection bypasses, deployment, or certification claims.

## UX direction

Desktop-first neutral dark theme with accessible contrast and visible keyboard focus. A compact left node library, central canvas, right inspector, and bottom validation/result panel. Canvas cards show a name, type, short description, and meaningful warnings. In M1 they show configuration state, not execution state.

Use actual empty, loading, error, unsaved, saving, and saved states. Preserve unsaved changes on network errors. Confirm navigation when unsaved work would be lost. Do not add decorative dashboards with invented counts or fake run histories.

## Success criterion

We will judge the application by whether a real engineer can diagnose and safely continue a real workflow, not by graph animation or the number of node types. Every milestone must have an observable end-to-end acceptance test.
