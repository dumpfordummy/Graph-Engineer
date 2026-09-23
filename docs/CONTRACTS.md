# M1 document and HTTP contract

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
- Only `start`, `modelCall`, and `end`, each at typeVersion 1, are supported. `start` has only output `out`; `modelCall` has input `in` and output `out`; `end` has only input `in`. Edges require existing endpoints and legal port directions. Identical source/port/target/port connections are rejected as duplicate connections.
- Maximum request/import UTF-8 size is 1 MiB (1,048,576 bytes); at most 200 nodes and 400 edges. Names: 120 characters; descriptions: 2000; prompt/sampleInput: 50,000; resultReference: 2000; providerProfileId: 120. Empty names/configuration strings are structurally allowed and reported by semantic validation where appropriate.
- Unsupported versions/types, unknown/missing properties, wrong JSON types, duplicate IDs, invalid ports/references, duplicate connections, and invalid layout are rejected on create/update with 422. Invalid JSON, including duplicate JSON property names, receives 400. Oversized HTTP bodies receive 413. Errors have field paths.
- Exactly one Start and End, a connected/reachable linear path, no branching/merging or cycles, nonempty workflow/node names, and a nonempty Model Call prompt are semantic checks. Semantic errors may be saved as drafts. A missing provider profile is allowed and does not cause a structural or semantic error in M1.
- No expression evaluation, provider lookup, credential fields, runtime state, or executable readiness exists. Validation always includes an explicit M1 scope explanation.

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

Validation report: `{ "structurallyValid": true, "valid": false, "issues": [{ "code": "missing_start", "severity": "error", "message": "Add exactly one Start node.", "path": "definition.nodes", "nodeId": "optional", "edgeId": "optional" }], "scope": "Structure and draft configuration only. Provider configuration and execution readiness are not checked in M1. Execution is unavailable." }`. `valid` includes semantic checks. Issues use optional node/edge references to focus the editor.

Failures use `application/problem+json`: `{ "type": "about:blank", "title": "...", "status": 422, "detail": "...", "errors": { "definition.edges[0].sourcePort": ["..."] } }`. `errors` is present when field errors are available. Stale updates return 409 with an atomic revision check and leave storage unchanged. Missing records return 404. Unavailable persistence returns an honest server error, never in-memory success.

## Client lifecycle

New drafts have a client-generated UUID, revision 0, and a real editable Start → Model Call → End sample. Save POSTs a new draft and adopts the server identity/revision; subsequent saves PUT with the last known revision. Preserve current edits on errors/conflicts and during an in-flight save. A save response must not overwrite edits made after its request began.

Imports first validate JSON/shape locally, then call server validation on the entire imported document; semantic errors are allowed, structural errors are not. Only after validation and any dirty-work confirmation may the editor replace its draft. Assign fresh workflow UUID, revision 0, and fresh dates; preserve node/edge IDs, configuration, and layout. Imported IDs can never target an existing persisted workflow. Export serializes only this contract, including the current unsaved draft.
