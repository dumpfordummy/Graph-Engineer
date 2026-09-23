# M3 — First real, durable workflow execution

Status: authorized for implementation and isolated synthetic verification, 2026-09-24.

Read AGENTS.md, current ARCHITECTURE.md/CONTRACTS.md/PROGRESS.md, M2_REPORT.md, M2_LEAD_REVIEW.md, M3_DATA_BINDINGS.md, and M3_REPORT_TEMPLATE.md first. Inspect the actual code; the lead has reviewed reports/screenshots, not the repository.

## 1. Outcome and boundaries

Make Start -> Model Call -> End and a linear sequence of Model Calls execute through the EXISTING protected Responses connection path. The user must be able to start a saved graph with explicit input, inspect genuine node results, cancel, refresh/reconnect, and reopen history after restart without automatic model replay.

Keep the local Windows, Vue/TypeScript/Vue Flow, ASP.NET Core/.NET 10, EF Core/SQLite modular monolith. Preserve working UI, migrations, sessions, credential protection, destination validation, and launcher ownership rules. Do not upgrade unrelated dependencies or add architectural layers without need.

Authorized: explicit text/JSON bindings, execution validation, immutable snapshots, one durable worker, attempts/artifacts/events, run APIs, Runs UI, authenticated SignalR notifications, cancellation, bounded execution, conservative restart behavior, tests/documentation.

Not authorized: provider token streaming, Chat Completions/fallback protocols, model tools, schema-constrained generation, function execution, coding agents, shell commands, repository workspaces, approvals, branching, merges, loops, semantic repair, automatic retries, parallel graph execution, subworkflows, scheduling, RAG, cloud/multi-user deployment, automatic commits or merges. Runtime does not spawn processes; isolated test harnesses can launch their own services.

## 2. Work sequence and safety

1. Inspect Git status, instructions, current contracts, toolchain, and running OWNED services. Preserve existing changes. Record the actual baseline; do not infer current HEAD from the previous report.
2. Run the current full M2 check using isolated data. Distinguish environmental failure from application regression; use normal approvals, never security bypasses.
3. Inspect the corrected lifecycle harness. Preserve retained-handle/exact-identity termination; parent ancestry or matching process names are not ownership. Add/retain an untargeted synthetic-process regression. Do not stop Windows services or arbitrary processes. Process enumeration, when necessary, is read-only.
4. Define versioned data/HTTP/state-machine contracts, then implement the worker against controlled providers. Build the Vue binding editor and Runs UI against those contracts. Parallel development is allowed only with non-overlapping ownership and one integration owner.
5. Run end-to-end and fault tests. Review the integrated diff and finish the full milestone, not just a plan/scaffold.

M2-U1 (the user's real post-restart connection check) may remain open during synthetic development. Codex must not run the live provider to close it. The human supplies its result; lack of real credentials does not prevent fixture implementation.

## 3. Run admission and immutable inputs

Run only an explicitly saved, executable revision. The browser's edited canvas is not an execution request. Disable Run while unsaved changes exist and offer Save first; saving/importing/validating never triggers inference. Run opens an input/confirmation drawer showing workflow revision, ordered node names, selected profile/model per node, maximum model-call count, and local-storage/provider-usage disclosure.

At start, the server validates strict input shape, graph topology, supported node versions, prompt bindings, End result binding, resolvable configured profiles, and local limits. Do not require or perform another connection probe. A previous M2 probe is advisory, not an executable authorization or proof of JSON behavior. Structural validity, local readiness, and real execution outcomes are distinct.

Inside a short transaction, verify the expected workflow revision, take coherent nonsecret provider references/settings and connection versions, persist the immutable definition, separate layout snapshot, input object, execution settings, initial node states, and durable Queued run record. No model call before commit. No database transaction spans network I/O.

Snapshot profile ID/version, display name, protocol, requested model, limits, and necessary approved-destination metadata. Never put credentials/ciphertext in snapshots, nodes, events, artifacts, or workflow exports. All runtime details are authenticated. Graph export still contains design data and profile references only, not history, trust approvals, or endpoints.

Original workflow edits, renames, or deletion must not rewrite or cascade-delete historical run evidence. Render history from its snapshot, not from the latest draft. Schema/migration tests must cover this. Existing workflow deletion behavior may remain restrictive, but never cascade away runs.

Use a client-generated opaque submission ID with a unique database constraint and request fingerprint. Repeating an accepted submission with the SAME payload returns the original run ID and starts no new work; different payload with the same ID is a conflict. Check existing submissions before rejecting a duplicate because the run is now active or the draft has since changed. Concurrent double submissions must be safe. A lost HTTP response, refresh, pairing, or SignalR reconnect does not resubmit execution. Provide a protected lookup by submission ID or equivalent reconciliation via history.

M3 admits at most one nonterminal run application-wide. A distinct concurrent submission receives a clear busy conflict rather than creating an unbounded queue. Queued remains a durable state between admission and worker claim. Run again after a terminal state creates a NEW submission/run with an explicit new-call confirmation; it is not a resume operation.

## 4. Durable worker and provider adapter

Use one hosted/background service, not an HTTP-request task or browser-driven scheduler. Acquire exclusive per-data-directory process ownership BEFORE any shared-data mutation, pairing-token rotation, migration, crash reconciliation, or worker dispatch; a second backend with different ports against the same data directory must fail cleanly without interrupting or dispatching the first backend's work. Use a held OS ownership primitive, not merely the presence of a stale marker file.

SQLite owns pending work. A channel/semaphore may wake the worker but cannot be the only queue. Recheck durable pending records so a lost wake-up cannot strand a new run. Claim/update work transactionally. Use fresh DI scopes/DbContexts for units of work; do not share a request DbContext or hold it across unrelated concurrent tasks. Hosted-service mechanics and shutdown constraints are documented by Microsoft; durable storage is our application requirement. [S3]

Execute one node at a time in the validated edge order. Node coordinates and object-array order are irrelevant. Start and End are local steps. Resolve explicit inputs from the input snapshot/successful artifacts; record resolved bindings and the final prompt before provider dispatch. Missing values fail BEFORE contacting the next provider.

Extract/reuse the M2 protected transport/parser rather than creating a second permissive HttpClient. Keep the synthetic connection probe as a separate operation with its fixed prompt and preview behavior. Runtime calls send the resolved prompt as Responses input with the snapshot's exact model, stream:false, store:false, and bounded max_output_tokens. No previous_response_id, Conversations API, tool definitions, provider background mode, or hidden history. [S1,S2]

Retain destination/address enforcement at actual dial, TLS checks, redirect/proxy restrictions, per-request authorization, concurrency controls, and safe error categories. Reusing the adapter must not weaken M2 tests or let mixed probes/runs bypass outbound limits. Do not send unverified compatibility/reasoning options silently.

Immediately before dispatch, acquire a coherent current credential/profile snapshot and require its connection version to match the run's pinned version. If it changed, fail safely as configuration_changed before sending that node; do not silently use the new settings or an old key with a new endpoint. Once dispatch is admitted against a coherent snapshot, a subsequent profile edit cannot recall that in-flight request; disclose that boundary. Later nodes recheck. Do not archive old keys to make snapshots replayable. Reject deletion of a profile still referenced by an active run, even if its live workflow reference was removed; historical snapshots should not require credentials to remain readable.

Persist the node's dispatch intent/status before external work. Parse completed typed assistant text, not output[0] by assumption. Ignore reasoning items as reasoning, never promote them to the visible result. Unsupported tool output is not executable. M2's preview must not truncate runtime input/output. Apply text/jsonObject validation from M3_DATA_BINDINGS.md, then commit success, artifacts, and event metadata atomically. [S1]

No automatic model retry after timeout, 429/5xx, invalid JSON, or transport uncertainty. Inspect SDK/HTTP resilience defaults and disable inference retries. A run that stops after a possibly-sent request must not call it again invisibly. Record actual supplied usage only; omit unavailable values. No invented price or elapsed-time-based token estimate.

## 5. State, attempts, artifacts, and transaction rules

Use explicit state transitions. Suggested names are normative semantics, not a requirement to rename compatible existing conventions:

| Run state | Allowed next states |
|---|---|
| Queued | Running, Cancelled, Interrupted |
| Running | Succeeded, Failed, CancelRequested, Interrupted |
| CancelRequested | Cancelled, Interrupted |
| Succeeded / Failed / Cancelled / Interrupted | Terminal; no replay/resurrection |

Timeout is Failed with a specific reason. Interrupted identifies service shutdown/restart or uncertain processing that cannot safely continue. Network/provider failure may be Failed while external outcome remains unknown; terminal app state does not mean no provider work occurred.

Node states include Pending, Running, Succeeded, Failed, Cancelled, Interrupted, and Skipped. Keep one attempt per node in M3; attempt records must still be independently identified for future repair support. Preserve succeeded nodes after later failure; never show unexecuted nodes as successful. Record skipped reasons. Separate failure before dispatch from possibly-sent work with an external-outcome/dispatch field such as NotStarted, ResponseReceived, or Unknown. Do not represent a committed dispatch-intent timestamp as proof the network request reached the provider.

Persist immutable run snapshots; node IDs/attempt number and timestamps; resolved inputs; permitted full prompt/assistant text and parsed JSON; final result; safe provider metadata; and ordered RunEvents. Bounded artifact content may remain in SQLite for this milestone; no object store or empty artifact framework is required. Include content type/length and stable ID/hash where useful. Never persist HTTP headers, keys, or arbitrary raw provider bodies.

Each event receives a monotonically increasing run-local sequence under the SAME transaction as the state/artifact change. Enforce uniqueness. Publish only committed notifications. Do not calculate max+1 unsafely outside transactional concurrency control. A publish failure cannot roll back committed success or cause a model re-call. No event-sourcing platform is required.

If recording dispatch intent fails, do not call the provider. If output persistence fails after the external request, do not dispatch downstream or re-call the node. Stop that run/worker as necessary; keep durable intent for conservative recovery. Avoid a misleading Succeeded response from a purely in-memory result.

## 6. Cancellation, deadlines, and restart

Cancellation is an authenticated, CSRF-protected, idempotent mutation. Queued cancellation terminates without a provider request. For Running, atomically record CancelRequested, signal the owned request token, and wait for bounded local request completion/abort before reporting Cancelled. Check the durable cancel flag before each node/dispatch and before terminal-success commit. A completion racing with cancellation uses guarded transitions: late data cannot resurrect a cancelled run or start downstream work. Record which transition won.

Cancellation means no further LOCAL progression; it does not guarantee remote computation/charges stopped and is not rollback. Timeout, user cancellation, and host shutdown require distinct reasons. A browser disconnect, reload, route change, or session expiry is NOT cancellation: the backend continues within the admitted limits. Re-pairing permits observation, not automatic new work.

Initial limits (our design defaults, not provider guarantees): one admitted active run; at most 16 Model Calls on the path; at most 32 bindings per node; run-input JSON at most 16 KiB; resolved prompt at most 64 KiB UTF-8; upstream body at most the existing M2 256 KiB limit; extracted assistant text at most 128 KiB; bounded JSON depth (32 is a suitable default); ten-minute run deadline. Keep existing provider token/timeout ceilings and enforce the smaller remaining deadline per call. Enforce byte limits before dispatch/allocating unbounded payloads. Never silently truncate a successful value. Existing stricter limits prevail and must be documented. These bounds may be tuned only explicitly, not removed because the developer's subscription has generous usage.

On graceful backend shutdown, stop admitting work and conservatively terminate active local processing. On next startup, after acquiring exclusive ownership, reconcile ALL previous nonterminal runs to Interrupted, including previously Queued work; mark unfinished nodes appropriately and keep completed artifacts. Do not auto-resume queued work or uncertain calls. This intentionally conservative M3 policy avoids surprise work after restart.

Do not rely solely on StopAsync: unexpected process exit can skip it. Test a real isolated backend process replacement after a controlled provider observed a request, and another failure after provider completion before local success persistence. No automatic provider call or downstream node may occur after restart. [S3]

## 7. APIs and live notifications

Define endpoints in docs/CONTRACTS.md before implementation. At minimum provide protected start, paginated workflow/run history, run detail/snapshot, node attempt/artifact detail, ordered events after a cursor, submission reconciliation, and cancellation. Start responds promptly with persisted acceptance/run ID, not a long-held inference response. Use appropriate 409/validation/auth/busy results with safe ProblemDetails. GET never starts work.

All reads require the current paired session. Mutations retain exact Host/Origin, JSON/body bounds, and X-GE-CSRF enforcement. Run/artifact IDs alone grant no access. Do not add an anonymous data/debug/export endpoint.

Add a small authenticated, read-only SignalR hub and the Vite /hubs WebSocket proxy. Notifications can be just runId and committed eventSequence; clients retrieve protected authoritative details. No model tokens or full prompts in broadcasts. No hub method starts/cancels runs, edits profiles, or changes workflow definitions.

Cookie authentication must apply to negotiation AND the actual transport, with session-expiry closure enforced/tested. Validate exact browser Origin on WebSockets; CORS alone does not protect them. Narrowly integrate SignalR negotiation/transport methods with existing JSON/antiforgery middleware instead of disabling security across /api or /hubs. Browser WebSocket upgrades cannot depend on custom Authorization headers. Do not place provider keys or pairing tokens in transport URLs. [S4,S5]

Use a stable SignalR JS package compatible with the existing .NET 10 server. Do not adopt .NET 11 authentication-refresh features from multi-version documentation. Configure bounded reconnect plus initial-start failure handling. Rejoin subscriptions and fetch a fresh snapshot/missed events on reconnect; deduplicate by sequence or use notifications only as invalidations. Handle dropped, duplicated, or out-of-order notices and HTTP responses. Render database truth; no percentage-complete fiction or event-only state store. [S4]

A lost live connection shows a reconnecting/stale indicator. Read-only refresh/polling is permitted as a bounded fallback; it must not become inference retry. Stop unauthorized reconnect/data refresh until paired again. Dispose page subscriptions and avoid duplicate event listeners.

## 8. Vue experience and privacy

Keep the existing canvas and inspector. Add an explicit Design / Runs separation, per-workflow history, and run detail using the frozen graph. The run view is read-only and shows revision, start/end/duration, actual status, cancellation, and actual call/attempt counts. Live state must not dirty the editable document.

Selected run nodes expose resolved bindings, rendered prompt, output text/parsed JSON, actual model/usage where supplied, error category, timestamps, and event history. Loading/unknown/skipped states remain honest. Missing profiles disable new execution but must not break historical inspection. Preserve prompts, selections, and save-conflict behavior while adding binding controls.

Treat input and model text as untrusted. Render plain text/escaped JSON, never v-html; do not auto-execute links, scripts, or commands. No prompt/output/credential bodies in diagnostics or SignalR trace logging. Prompts and outputs are deliberately retained in the protected local application database; M3 does not claim they have DPAPI encryption merely because API keys do. Document retention/backup sensitivity; do not add cloud telemetry or publish company data.

Use the active credentials already acquired for this run to extend sentinel/redaction tests. If an active credential is detected in model text or normalized metadata, fail safely as sensitive_output, persist only a sanitized diagnostic, and do not forward it. Never silently change a JSON value via redaction and continue as if it were the original result. Do not scan the user's other credentials to build a redaction list, and do not claim arbitrary-secret detection is complete. Provider error bodies remain unreflected.

## 9. Required verification

Use xUnit, real SQLite, Vitest/Vue Test Utils, and Playwright; preserve meaningful M1/M2 tests. New execution success must be obtained through the REAL worker and a controlled HTTP Responses fixture that records synthetic requests. Timers/fake success in normal app code are prohibited. Test synchronization should use controllable barriers, not arbitrary sleeps. Injection points belong only to tests and must not expose runtime debug bypasses.

Report each category as PASS, FAIL, or NOT RUN with the actual command/artifact. Do not invent test totals or discard inconvenient earlier attempts.

| ID | Acceptance evidence |
|---|---|
| A01 | Full M2 baseline and final integrated regression; old editor/provider/session functionality retained. Update old unconditional Run-disabled assertions only to distinguish M2 legacy/incomplete from M3 executable graphs; keep equivalent no-inference-on-save/import checks. |
| A02 | Fresh and M2-only SQLite migration; old graph/profile/credential data preserved; legacy document import and explicit execution-config upgrade; new version round-trip. |
| A03 | Literal prompt, typed bindings, two different node profiles, End result mapping, JSON Pointer escapes/indexes/null/missing/large numbers, no recursive substitution/prototype traversal, no future/self/cross-run refs. |
| A04 | Real worker makes exact ordered fixture requests for one-node text and two-node JSON workflows; second body contains actual first output; only selected inputs/history included; outputs/usage persisted. |
| A05 | Invalid/incomplete/oversized/refusal/unsupported output, bad JSON/fences/duplicates and missing bound fields fail honestly; downstream call count is zero. |
| A06 | Same submission concurrent/repeated returns same run; mismatch conflicts; distinct simultaneous admission is busy; lost POST response reconciles; refresh/re-pair never creates another dispatch. |
| A07 | Edit saved graph/profile between nodes: frozen prompts/layout remain old; changed connection version prevents later dispatch; destination/credential coherence retained; active-profile deletion conflicts. |
| A08 | Queue/active cancellation, timeout, cancellation-vs-completion race; no downstream calls, no terminal resurrection, accurate external-outcome and partial completed results. |
| A09 | Real service restart with a completed run preserves history; restart during a possibly-sent request and after provider completion/before success persistence yields Interrupted, with unchanged upstream request count and no automatic replay. |
| A10 | Exclusive same-data-directory worker ownership tested across two backend processes; only the owner schedules/reconciles work; different isolated data directories can run independently. |
| A11 | Browser refresh/offline/reconnect while delayed fixture request continues; no second request; missed/duplicate/out-of-order notifications recover database state; show genuine SignalR transport via Vite, not only mocked events. |
| A12 | Unauthorized REST reads/writes, invalid CSRF/Host/Origin, unauthenticated/cross-origin actual hub connection, session expiry/re-pair, no query credential exposure. Existing M2 provider security suite still passes. |
| A13 | Secret echo, HTML/script data, output caps, body/log/artifact protection, no secret in snapshot/export/notification; actual DPAPI synthetic tests retained. |
| A14 | Transaction failure before dispatch yields zero calls; failure after external completion prevents downstream/replay; committed events precede publication; failed publication cannot undo success. |
| A15 | Launcher/cleanup regression with retained handles and an untargeted synthetic process surviving; no ownership inference from arbitrary PID ancestry. Keep any incident report honest. |
| A16 | Browser: dirty-save gate, Run input confirmation, two-node binding editor, statuses/inspector/final output/history, cancellation and failure UI, snapshot different from edited draft, retained keyboard/import safeguards. |
| U01 | User-operated M2 restart probe; actual-profile key survives without reentry. Separate evidence, never fixture-inferred. |
| U02 | User-operated real text run, then the two-node JSON example; inspect resolved second-node input and final output, refresh/reopen same run, restart/re-pair and reopen completed history. No automated live-provider test. |

Automated traces/HAR/video/automatic failure screenshots stay disabled for any secret-bearing flow. Capture synthetic-only explicit screenshots after entry fields clear; keep artifacts isolated and ignored. Do not read/default to the live application data directory. Process fault tests terminate only retained test-owned handles.

## 10. Deliverables and final gate

Deliver real application code/tests/migrations and updated README, CONTRACTS.md, SECURITY.md, ENVIRONMENT.md as necessary, PROGRESS.md, plus docs/handoffs/M3_REPORT.md from the template. Record actual frontend/backend contracts, version upgrade behavior, limits, external-outcome semantics, and recovery instructions. Preserve historical M2 evidence and append later user checks separately.

Review the diff for scope creep, unsafe transport shortcuts, unbounded queues/payloads, accidental auto-retry, lack of snapshot isolation, overly broad process cleanup, secrets, and fake execution. Run the integrated check after final fixes. No automatic stage/commit/push/merge.

Without U02, report READY FOR USER WORKFLOW CHECK only if all required automated checks pass; otherwise report the precise blocker(s). Show U01 separately as PASS/FAIL/NOT RUN. Never claim a real model succeeded because the fixture did.

Provide exact existing-repository startup/pairing steps and the actual UI recipe for the synthetic arithmetic example. The user enters real credentials through the local app only. Do not start M4 or test company repositories.

References [S1]–[S6] are in docs/M3_SOURCES.md. They support platform mechanics, not claims that this implementation already complies.
