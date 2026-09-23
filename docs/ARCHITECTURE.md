# Architecture decisions — v1

This document describes the target architecture. Implement only the subset authorized by the current milestone.

## Shape and dependencies

Use a local modular monolith. Vue owns presentation and draft editing. ASP.NET Core owns persistence, validation, secrets, and execution. Vue Flow is an editor component, not a runtime scheduler.

Suggested initial repository layout:

```text
apps/web/
src/GraphEngineering.Api/
src/GraphEngineering.Core/
tests/GraphEngineering.Core.Tests/
tests/GraphEngineering.Api.Tests/
scripts/
docs/
```

Create only useful projects. Core contains document contracts and pure graph validation. Api contains HTTP endpoints, EF Core/SQLite persistence, and feature-specific infrastructure. Do not add empty Application, Infrastructure, Domain, Contracts, Workers, and SharedKernel projects preemptively.

Use Vite's development proxy for /api and /hubs (WebSockets). Bind both development hosts to loopback. Use the installed, supported Node LTS that meets the selected Vite requirements. Pin an installed .NET 10 SDK in global.json and record actual versions in docs/ENVIRONMENT.md. No .NET preview or silently falling back to an unsupported target.

## Separate data models

A saved editable document has:
- formatVersion: serialization version, initially 1.
- workflow metadata: stable ID, name, description, draft revision, timestamps.
- definition: stable node IDs, node type and type version, configuration, and edges with named ports.
- layout: node positions and viewport; this does not define execution order.

Keep runtime status, outputs, attempt counts, and approvals out of the editable document. Do not persist the complete Vue Flow object with framework internals.

M1 may store definition and layout as separate JSON fields on one relational workflow record. This is preferable to prematurely normalizing every graph property. Keep metadata and a concurrency revision queryable. EF migrations must be checked in by the implementation and usable on a fresh database.

A saved draft may be semantically incomplete. Malformed payloads, duplicate IDs, unsupported document versions/types, and corrupt references are structural errors and must be rejected. A missing Start, a disconnected node, or an unfinished node configuration can be saved as a draft but must be reported by validation. Do not confuse 'draft saved' with 'ready to run'.

Use optimistic concurrency: clients supply the expected draft revision; a stale save returns a conflict, not silent last-write-wins. Editing from two browser tabs must not silently erase work.

## Initial graph semantics

M1 supports Start, Model Call, and End. Start has one control output and no input. Model Call has one control input and one output. End has one control input and no output.

The accepted executable shape for the first real runner is a connected linear path with exactly one Start and one End. Multiple Model Call nodes may appear on that path. Drafts can be temporarily incomplete. Report unsupported branches and cycles rather than accidentally treating them as supported execution features.

Control edges select ordering. Data mappings will use explicit workflow-input or previous-node-output references, not JavaScript expressions or arbitrary code. Resolve data at execution time and record the resolved inputs. M1 can persist configuration without building an expression engine.

Separate structural validation from provider checks and executable readiness. In M1 a structurally valid graph still has no runnable implementation. Display that boundary clearly.

## Provider and runner boundaries — from M2 onward

Direct model profiles include a protocol adapter, base URL, model ID, timeouts, supported options, and secret reference. Implement one complete protocol adapter before advertising another. Never assume that an 'OpenAI-compatible' endpoint implements every parameter or tool feature. Capability checks need separate results for text responses, streaming, and structured output/tool use where actually tested.

User-supplied endpoints require URL and redirect validation. Default to HTTPS; local/self-hosted HTTP and private-network destinations need explicit local configuration. Do not disable TLS validation or permit redirects that leak credentials to another host. Do not log authentication headers or credential-bearing URLs.

Use a server-side secret-store abstraction with Windows OS-protected storage for the Windows-first release. Plan a migration path before supporting other OSes. Graph export must not include secrets. A secret must not be retrievable through a normal profile GET. Masked placeholders must not overwrite existing secrets.

The developer's interactive Codex login is not an app API key. A local Codex runner may reuse supported saved CLI authentication under the user's account, without reading/copying its auth file. Direct OpenAI API billing is separate from ChatGPT subscription billing.

When implementing the coding-agent runner, test the installed Codex interface and document its actual capabilities. A bounded subprocess using `codex exec --json` is a candidate for the first batch adapter. Use the richer app-server protocol only when interactive approval/session requirements justify it. Never parse terminal decoration as the integration contract or rely on unverified flags.

## Execution and evidence — from M3 onward

Starting a run creates an immutable workflow snapshot, input snapshot, run ID, and durable pending run record. Use a single background worker initially. The database owns work and state; an in-memory channel can wake the worker, not replace durable records.

Track Run, NodeAttempt, RunEvent, and Artifact metadata. Events have a run-local sequence and are persisted before being published. SignalR is a notification transport; reconnecting clients fetch missed events or a fresh authoritative state. A disconnected browser must not terminate a run.

Separate transient request retry from semantic repair iterations. Each has an explicit limit, timeout, and attempt record. Output validation failure does not count as a successful node. A new attempt must not overwrite prior evidence.

On restart, mark work whose completion cannot be established as Interrupted or NeedsReview. Do not automatically repeat a model request, build script, or file modification with uncertain effects. First implement conservative recovery; automatic recovery requires evidence of safety, not optimism.

Waiting-for-approval state is durable. Decisions bind to the run snapshot, node attempt, and artifact/commit hashes under review. Apply each decision with an atomic state transition. Duplicate submissions must not dispatch the next side effect twice. Changed artifacts invalidate approval.

## Workspace and security boundary — before command execution

Local-only is an initial deployment limit, not a complete authentication design. Before credentials or execution are exposed, add a local session bootstrap, anti-forgery/origin protections, and strict host validation. Do not expose unauthenticated execution endpoints even on localhost.

The runner accepts only configured workspaces and permitted commands. Use structured arguments rather than concatenated shell strings. Shell scripts and builds execute repository code; allowlisting `dotnet` or using a Git worktree is not a sandbox. Start with a disposable synthetic repository and a restricted process environment.

Separate child-process environment from backend secrets; pass only necessary values. Do not allow a generated graph or model output to grant itself additional permissions. Serialize writers per workspace. Later parallelism requires independent workspaces plus a controlled integration step and actual OS-level isolation appropriate to the workload.

Cancellation must terminate/wait for owned child processes where possible and clearly record partial effects. Never represent cancellation as a clean Git rollback. Preserve stderr, exit code, timeout reason, and capped/redacted output. Logs and retained artifacts also require retention and deletion controls before wider deployment.

## Testing boundaries

Core tests cover graph/document invariants. API tests use the real SQLite provider with isolated temporary data. Frontend tests cover editing, serialization, errors, and dirty-state behavior. Playwright drives the real frontend and API for acceptance tests.

Fakes are allowed inside automated tests and explicitly labeled development fixtures. They must never masquerade as a real provider response, a passed command, or a successful execution in the application UI.
