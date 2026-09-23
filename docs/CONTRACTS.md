# Document, provider and HTTP contracts

## M3 execution contract (fixed before parallel implementation)

The document remains formatVersion 1. Start remains typeVersion 1. Model Call and End accept both the unchanged legacy typeVersion 1 and explicit typeVersion 2. Upgrading is a user action; legacy prompts keep literal semantics. Version 2 Model Call configuration is `{prompt, providerProfileId?, promptMode: 'literal'|'bindings', inputBindings: [{alias, source}], outputMode: 'text'|'jsonObject'}`. Version 2 End configuration is `{resultReference: string, resultBinding: BindingSource|null}`. A null End binding is a saveable incomplete draft. Unknown properties are still rejected at every level.

`BindingSource` is exactly one of `{kind:'runInput', pointer:string}`, `{kind:'nodeText', nodeId:string}`, or `{kind:'nodeJson', nodeId:string, pointer:string}`. Aliases are ASCII identifiers, 1–64 characters. Pointers are bounded to 2048 characters and use RFC 6901; the empty pointer selects the root. Node sources must refer to an earlier Model Call in the validated linear control path. JSON sources require jsonObject output. Literal mode requires no bindings; bindings mode substitutes only `{{inputs.alias}}`, once, without evaluating inserted data. Strings insert directly; all other values use compact JSON preserving numeric tokens. No edge carries implicit data.

All run routes are private and mutations require the existing Origin, JSON and antiforgery protections. Run and submission IDs are UUIDs. State names are case-sensitive PascalCase.

| Method/path | Request | Response |
|---|---|---|
| GET `/api/workflows/{id}/readiness` | none | `{ready, issues: ValidationIssue[], orderedNodes: ReadyNode[], maximumModelCalls}` for the currently saved revision |
| POST `/api/workflows/{id}/runs` | `{submissionId, expectedRevision, input: <JSON object>}` | 202 `RunDetail`; an identical replay returns the original run before busy/revision checks; conflicting reuse returns 409 |
| GET `/api/runs/submissions/{submissionId}` | none | `RunDetail` or 404; never submits work |
| GET `/api/runs?workflowId={optional}&before={optional ISO date}&limit=20` | none | `{items: RunSummary[], nextCursor: string|null}`; limit 1–100 |
| GET `/api/runs/{id}` | none | `RunDetail` |
| GET `/api/runs/{id}/events?after=0&limit=200` | none | `{items: RunEvent[], lastSequence, hasMore}`; after is an exclusive run-local sequence |
| GET `/api/runs/{id}/artifacts/{artifactId}` | none | `{id, nodeId, kind, contentType, byteCount, sha256, text}` |
| POST `/api/runs/{id}/cancel` | `{}` | `RunDetail`; repeated cancellation is idempotent |

`ReadyNode = {nodeId,name,type,providerProfileId:string|null,profileName:string|null,modelId:string|null,connectionVersion:number|null}`.

`RunSummary = {id,submissionId,workflowId,workflowName,workflowRevision,state,createdAt,startedAt:string|null,finishedAt:string|null,lastSequence,failureCode:string|null}`.

`RunDetail` extends RunSummary with `{snapshot: WorkflowDocument, input: JSON object, profiles: RunProfile[], nodes: RunNode[], result: JSON value|null}`. `RunProfile = {nodeId,providerProfileId,name,connectionVersion,protocol,modelId,baseUrl,authMode,timeoutSeconds,maxOutputTokens,allowPrivateNetwork,allowInsecureHttp}` contains no key/ciphertext. `RunNode = {nodeId,name,type,state,attempt: RunAttempt|null,artifactIds:string[],skipReason:string|null}`. `RunAttempt = {id,startedAt,finishedAt:string|null,dispatchIntentAt:string|null,externalOutcome:'NotStarted'|'ResponseReceived'|'Unknown',resolvedInputs: JSON object,prompt:string|null,outputText:string|null,outputJson:JSON object|null,failureCode:string|null,message:string|null,observedModel:string|null,requestId:string|null,usage:TestUsage|null}`. `RunEvent = {sequence,at,kind,nodeId:string|null,state:string|null,message:string|null}`. Attempts and content in the detail are bounded; artifact endpoints provide immutable independently hashed content. Explicit final null is distinguished by a Succeeded run state.

The browser retains the raw run-input JSON text while submitting; it must not round-trip numeric tokens through JavaScript numbers. A submission ID is allocated once for an explicit confirmation and retained for protected reconciliation after an ambiguous response. Refresh, reconnect and re-pair never automatically submit a new run.

Run states are Queued, Running, CancelRequested, Succeeded, Failed, Cancelled and Interrupted. Node states are Pending, Running, Succeeded, Failed, Cancelled, Interrupted and Skipped. One nonterminal run is admitted globally; one sequential worker claims durable queued work. Snapshots are immutable and have no cascading dependency on editable workflows or credential records. Startup ownership is acquired before pairing rotation, migration and recovery; previous nonterminal runs become Interrupted and are never resumed automatically.

Limits: 16 Model Calls/run, 32 bindings/node, run-input UTF-8 16 KiB, resolved prompt 64 KiB, upstream body 256 KiB, assistant text 128 KiB, JSON depth 32, total deadline ten minutes. There are no automatic inference retries. Every dispatched call uses the snapshotted profile connection version and the current coherent credential record; changes fail before sending. Full permitted text is retained. jsonObject output is strict local validation, not a provider schema promise.

SignalR `/hubs/runs` is read-only, cookie authenticated, exact-Origin protected, and closes on authentication expiration. `RunChanged` notices contain only `{runId,eventSequence}` and are published after durable commit. There are no client hub mutation methods or query-string bearer tokens. The client reconciles REST snapshots/events after reconnect and treats notices as invalidations, with bounded retry/polling and a stale indicator. `/hubs/runs/negotiate` is the only narrow empty-body POST exception; it still requires session, exact Origin and X-GE-CSRF. Vite proxies `/hubs` with WebSocket support.

Run input, resolved prompts, successful outputs and artifacts deliberately persist in the local SQLite database without DPAPI encryption. Credentials remain separately DPAPI protected. Exports contain definitions only. Provider cancellation cannot establish whether remote work or billing stopped; persisted dispatch intent alone is not proof a request was sent.

Version 1 is independent of Vue Flow. JSON uses camelCase and case-sensitive property/type names. Unknown properties are rejected so runtime records, credentials, and framework objects cannot silently enter the document. All listed properties are required except `providerProfileId`, which may be omitted or null. Semantic incompleteness is saveable; malformed structure is not.

```json
{
  "formatVersion": 1,
  "workflow": {
    "id": "11111111-1111-4111-8111-111111111111",
    "name": "Example workflow",
    "description": "A draft",
    "revision": 0,
    "createdAt": "2026-09-23T00:00:00Z",
    "updatedAt": "2026-09-23T00:00:00Z"
  },
  "definition": {
    "nodes": [
      { "id": "start-1", "type": "start", "typeVersion": 1, "name": "Start", "description": "", "configuration": { "sampleInput": "Example input" } },
      { "id": "model-1", "type": "modelCall", "typeVersion": 1, "name": "Model Call", "description": "", "configuration": { "prompt": "Summarize the input.", "providerProfileId": null } },
      { "id": "end-1", "type": "end", "typeVersion": 1, "name": "End", "description": "", "configuration": { "resultReference": "model-1 output (descriptive only)" } }
    ],
    "edges": [
      { "id": "edge-1", "sourceNodeId": "start-1", "sourcePort": "out", "targetNodeId": "model-1", "targetPort": "in" },
      { "id": "edge-2", "sourceNodeId": "model-1", "sourcePort": "out", "targetNodeId": "end-1", "targetPort": "in" }
    ]
  },
  "layout": {
    "nodes": [
      { "nodeId": "start-1", "x": 80, "y": 180 },
      { "nodeId": "model-1", "x": 400, "y": 180 },
      { "nodeId": "end-1", "x": 720, "y": 180 }
    ],
    "viewport": { "x": 0, "y": 0, "zoom": 1 }
  }
}
```

## Invariants and limits

- Workflow ID is a UUID; revision is a nonnegative safe integer. Dates are ISO-8601 UTC timestamps. Creation/update times and persisted revisions are server-owned.
- Node and edge IDs are stable, nonempty strings of up to 100 characters. Node IDs and edge IDs are each unique. A layout entry is required exactly once per node. Positions and viewport coordinates are finite numbers in [-100000, 100000]; zoom is in [0.1, 4].
- Only `start` at typeVersion 1 and `modelCall`/`end` at typeVersion 1 or 2 are supported. `start` has only output `out`; `modelCall` has input `in` and output `out`; `end` has only input `in`. Edges require existing endpoints and legal port directions. Identical source/port/target/port connections are rejected as duplicate connections.
- Maximum request/import UTF-8 size is 1 MiB (1,048,576 bytes); at most 200 nodes and 400 edges. Names: 120 characters; descriptions: 2000; prompt/sampleInput: 50,000; resultReference: 2000; providerProfileId: 120. Empty names/configuration strings are structurally allowed and reported by semantic validation where appropriate.
- Unsupported versions/types, unknown/missing properties, wrong JSON types, duplicate IDs, invalid ports/references, duplicate connections, and invalid layout are rejected on create/update with 422. Invalid JSON, including duplicate JSON property names, receives 400. Oversized HTTP bodies receive 413. Errors have field paths.
- Exactly one Start and End, a connected/reachable linear path, no branching/merging or cycles, nonempty workflow/node names, and a nonempty Model Call prompt are semantic checks. Semantic errors may be saved as drafts. A missing provider profile is allowed and does not cause a structural or semantic error; the M2 inspector reports provider readiness separately.
- Graph validation performs no expression evaluation, provider lookup, credential handling, runtime changes, or executable-readiness checks. Its scope is stated explicitly; provider selection never sends a probe.

## HTTP API

Base `/api`; JSON throughout. No browser-storage persistence. Local development uses a Vite proxy, without cross-origin CORS.

| Method/path | Request | Success |
|---|---|---|
| GET `/health` | none | 200 `{ "status": "ok" }` after database connectivity check |
| GET `/workflows` | none | 200 array of workflow metadata (same shape as `workflow`) ordered by updatedAt descending |
| POST `/workflows` | `{ "document": <v1> }` | 201 full saved document; always generates a fresh UUID, revision 1, and server timestamps |
| GET `/workflows/{id}` | none | 200 full saved document; 404 if missing |
| PUT `/workflows/{id}` | `{ "expectedRevision": 1, "document": <v1> }` | 200 full saved document with incremented revision; route ID must match document ID, document revision must equal expectedRevision |
| POST `/workflows/validate` | `{ "document": <v1> }` | 200 validation report, including structural failures; no persistence |

Validation report: `{ "structurallyValid": true, "valid": false, "issues": [{ "code": "missing_start", "severity": "error", "message": "Add exactly one Start node.", "path": "definition.nodes", "nodeId": "optional", "edgeId": "optional" }], "scope": "Structure and draft configuration only. Provider configuration and execution readiness are checked separately before a run." }`. `valid` includes semantic checks. Issues use optional node/edge references to focus the editor.

Failures use `application/problem+json`: `{ "type": "about:blank", "title": "...", "status": 422, "detail": "...", "errors": { "definition.edges[0].sourcePort": ["..."] } }`. `errors` is present when field errors are available. Stale updates return 409 with an atomic revision check and leave storage unchanged. Missing records return 404. Unavailable persistence returns an honest server error, never in-memory success.

## Client lifecycle

New drafts have a client-generated UUID, revision 0, and a real editable Start → Model Call → End sample. Save POSTs a new draft and adopts the server identity/revision; subsequent saves PUT with the last known revision. Preserve current edits on errors/conflicts and during an in-flight save. A save response must not overwrite edits made after its request began.

Imports first validate JSON/shape locally, then call server validation on the entire imported document; semantic errors are allowed, structural errors are not. Only after validation and any dirty-work confirmation may the editor replace its draft. Assign fresh workflow UUID, revision 0, and fresh dates; preserve node/edge IDs, configuration, and layout. Imported IDs can never target an existing persisted workflow. Export serializes only this contract, including the current unsaved draft.

## M2 provider and session contracts (fixed before parallel implementation)

Workflow formatVersion 1 is unchanged. Model Call configuration.providerProfileId is an optional stable ID only; unknown IDs remain saveable/unresolved. Endpoints, approvals, credentials and test history never enter workflow JSON.

All private /api routes require the local session. GET /api/health and GET /api/session plus POST /api/session/pair are the narrow bootstrap exceptions. GET /api/session returns { authenticated: boolean, csrfToken: string | null }. Pair takes { token: string } and returns { paired: true }; then GET session obtains the antiforgery token for the new identity. POST /api/session/logout is protected. State-changing requests use application/json, exact allowed Origin and X-GE-CSRF; auth failure is 401, security/antiforgery rejection 403, unsupported media 415. Problem responses use existing problem+json with a stable code extension where useful. No automatic paid-request replay.

Configuration: GRAPH_ENGINEERING_DATA_DIR keeps its M1 meaning. GRAPH_ENGINEERING_BROWSER_ORIGIN defaults to http://127.0.0.1:5173 and must be one exact loopback HTTP(S) origin. API urls supplies exact API authorities/origins (default http://127.0.0.1:5080). Only those Host authorities and browser origins are accepted, including the dev proxy's original Host. Forwarded headers are not trusted. A launch token is placed at <data-directory>/runtime/pairing-token.txt with owner-restricted permissions; startup prints its path only. Tests read only their own isolated instance file.

Provider routes:

- GET /api/providers -> ProviderProfile[]; GET /api/providers/{id} -> ProviderProfile.
- POST /api/providers takes ProviderWrite and returns 201 ProviderProfile.
- PUT /api/providers/{id} takes ProviderWrite plus expectedRevision and returns ProviderProfile; stale = 409.
- DELETE /api/providers/{id} takes JSON { expectedRevision }; returns 204; stale, saved-workflow or active-run references = 409 with safe conflict detail (no graph rewrite).
- POST /api/providers/{id}/test takes { expectedRevision, connectionVersion }; returns a TestResult. Only a saved matching version can be tested. Duplicate in-flight profile/global limit = 409/429. No automatic retries. Network/provider failures are sanitized TestResult categories, never raw bodies.

ProviderProfile = { id: string, name: string, revision: number, connectionVersion: number, createdAt: ISO date, updatedAt: ISO date, protocol: 'openai-responses', baseUrl: string, resolvedEndpoint: string, modelId: string, authMode: 'bearer' | 'none', timeoutSeconds: number, maxOutputTokens: number, allowPrivateNetwork: boolean, allowInsecureHttp: boolean, hasCredential: boolean, lastTest: TestResult | null }.

ProviderWrite = { name, protocol, baseUrl, modelId, authMode, timeoutSeconds, maxOutputTokens, allowPrivateNetwork, allowInsecureHttp, credential: { action: 'keep' | 'replace' | 'remove', value?: string }, confirmDestinationChange: boolean, expectedRevision?: number }. All settings are explicit. Name 1..120, model 1..200, base URL <=2048, timeout 5..120 seconds (default 30), output 16..4096 (default 128). Credential replacement 1..8192 printable non-whitespace characters, rejecting masking placeholders; value must be absent for keep/remove. Bearer may be saved without a credential for later setup but cannot probe. No-auth requires explicitly approved private/loopback destination. HTTP requires both private approval and separate insecure acknowledgment. Approvals bind to exact canonical scheme/host/port/base path and are explicitly reconfirmed when it changes. Destination/auth changes require confirmDestinationChange and replacement/re-entry for bearer; never silently reuse the old credential. Creation is the initial explicit approval. Name-only change keeps connectionVersion; changes to model, limits, permissions, destination/auth, or credential increment it and invalidate last verification.

TestResult = { testedAt: ISO date, connectionVersion: number, category: string, success: boolean, durationMs: number, message: string, preview: string | null, phraseMatched: boolean, observedModel: string | null, requestId: string | null, usage: { inputTokens?: number, outputTokens?: number, totalTokens?: number } | null }. Categories: success, invalid_configuration, credential_missing, credential_unreadable, connectivity, tls, timeout, rate_limit, auth_access, rejected_request, unexpected_payload, incomplete_output, cancelled. Only bounded safe strings and actual supplied usage are returned. The active key is redacted before truncation. Last result is persisted only if connectionVersion still matches; an old response may be returned with its old version but cannot verify current settings. Test results do not increment profile revision. Connection probes create no runtime records.

Persistence: separate ProviderProfiles and ProviderCredentials tables in the existing SQLite database. Only DPAPI CurrentUser ciphertext is stored in credential records. Public DTOs never include ciphertext/reference/key/suffix. Metadata and ciphertext use one transaction; coherent snapshots are taken before network I/O, no open transaction during the probe. Referenced profile deletion is rejected inside a write transaction. Existing M1 rows are preserved by additive migration.

Backend integration ownership: provider feature supplies AddProviders() service registration and MapProviderEndpoints() route mapping; root owns Program and Security. Provider destination policy reads urls and GRAPH_ENGINEERING_BROWSER_ORIGIN from IConfiguration to prohibit app-self destinations. Root owns project/package lock updates. Tests use a shared LocalApiFactory (root-owned) with CreatePairedClientAsync() that performs real isolated file pairing; there is no production bypass.

Save performs a bounded five-second DNS resolution for named provider hosts and rejects unresolved or forbidden addresses without sending provider HTTP requests. Probe connection resolves again and pins the checked address to its socket. Provider JSON requests are capped at 24 KiB; responses at 256 KiB; diagnostic previews at 500 characters. Replacement keys are visible ASCII, 1..8192 characters, with whitespace and mask placeholders rejected. Kestrel:Endpoints overrides are rejected before startup so they cannot override loopback urls. Existing pairing files have explicit ACLs restricted before token rotation, in addition to the owner-only runtime directory.
