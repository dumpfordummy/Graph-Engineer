# M3 data contract and acceptance example

This is a new lead design decision, not a description of functionality already implemented. Inspect current contracts, then agree exact wire property names before frontend/backend implementation. Preserve these semantics even if current naming requires adaptation.

## 1. Control is not data

Edges determine execution order on a single linear path. They do not forward conversations, entire provider payloads, all upstream outputs, or instructions automatically.

A run receives an explicit JSON object through the Run dialog. Start exposes that immutable object without inference. Old Start sample text remains a sample; do not silently treat it as runtime input. No JSON Schema editor is required in M3.

Model Call supports:
- A saved provider-profile reference.
- The existing prompt, with an explicit prompt mode: literal or bindings.
- Named input bindings when bindings mode is selected.
- Output interpretation: text or jsonObject.

Literal mode sends the prompt unchanged and cannot silently use configured bindings. Existing prompts retain literal semantics, including braces, until the user explicitly upgrades/configures them. A graph connection alone never supplies the previous result.

## 2. Binding sources

Each binding maps an alias to one of these structured sources:

| Source | Required values | Result |
|---|---|---|
| Run input | JSON Pointer | Selected value from the immutable input object |
| Previous node text | Stable node ID | Entire completed assistant text from a successful earlier Model Call |
| Previous node JSON | Stable node ID and JSON Pointer | Selected value from a successful earlier jsonObject result |

Use RFC 6901 string JSON Pointers, not JSONPath, JavaScript, C#, or URI-fragment syntax. The empty pointer selects the root; /varX selects a field. Correctly handle ~0, ~1, array indexes, and missing/null differences. Reject malformed pointers, unresolved paths, duplicate object keys, and out-of-range indexes. Treat key names as data; never traverse JavaScript object prototypes. [S6]

Only earlier nodes on this run's validated control path are legal sources. Reject self, future, foreign-workflow, and unknown references. Reordering/reconnecting a graph recomputes validity. Renaming nodes does not break stable-ID bindings; deleting a referenced node leaves a clear diagnostic, not a guessed replacement. JSON references to a text-mode node are invalid.

## 3. Prompt substitution

In bindings mode, canonical placeholders have the form {{inputs.alias}}. Aliases are case-sensitive ASCII identifiers, starting with a letter or underscore, with at most 64 characters. Require every canonical placeholder to have exactly one binding. Unused bindings can be warnings. Other brace sequences remain literal, allowing normal JSON examples.

String values are inserted as text. Numbers, booleans, null, objects, and arrays are inserted as compact JSON using invariant formatting. Preserve numeric JSON values instead of coercing every number through a JavaScript double. No functions, arithmetic, conditions, property-expression evaluation, arbitrary templates, or recursive interpolation. An inserted value containing another placeholder remains data and is never expanded again.

Show a source picker and alias in the inspector, not only a raw configuration textarea. The user chooses the upstream node/source and may enter a JSON Pointer. A run inspector shows the concrete resolved value and final prompt. A design-time preview can resolve supplied sample input but must mark unavailable upstream results unresolved; never substitute another run's cached output.

## 4. Output interpretation

text: accept bounded, completed, nonempty assistant text using the shared Responses adapter. Retain the full permitted text, not the M2 500-character connection preview.

jsonObject: apply a STRICT local JSON parser to that same text. Require one complete root object; reject duplicate keys, malformed/trailing content, markdown fences, unsupported/depth-excess payloads, and invalid numbers. Preserve the permitted original text plus the parsed value. A valid JSON object does not prove mathematical or business correctness.

Do not silently strip prose/fences, repair JSON, coerce values, or call the model again. Label this feature “JSON object — local validation.” Do not send provider schema/JSON-mode options, mark provider structured-output capability verified, or imply generation is constrained by a schema. Invalid output fails the node and stops downstream calls.

End has one explicit result binding using the same source selector. Its value becomes the typed final run result. End does not call a model. No implicit selection of the last node: require the result binding in executable readiness. A schema-valid draft may remain incomplete and saveable.

## 5. Versioning and compatibility

Inspect the existing document format and type-version parser. Version the new execution configuration using the existing node-version mechanism, or a new document version if necessary; record the exact choice in docs/CONTRACTS.md before implementation. Do not silently reinterpret old version-1 prompts or relax unknown-field validation globally.

Old M1/M2 workflows and exports must remain loadable/editable with their metadata, prompts, profile IDs, edges, and layout preserved. Provide an explicit upgrade/configuration path. An old document that lacks execution bindings can remain non-runnable with actionable diagnostics. Test legacy import, explicit upgrade, new export/import, and stale-save behavior. Never reset the database to obtain a clean schema.

## 6. Manual two-node example

This is a configuration recipe, NOT an importable document with assumed schema or real provider IDs. Document the app's actual click steps once implemented.

Graph: Start -> Double value -> Multiply by ten -> End.
Run input: {"x":7}.

Double value:
- Type: Model Call; choose the user's saved profile in the local app.
- Mode: bindings; alias x from Run input, pointer /x.
- Prompt: “Take the number {{inputs.x}}, multiply it by 2, and return only one JSON object with a numeric varX property. No explanation or markdown.”
- Output: jsonObject.

Multiply by ten:
- Type: Model Call; choose a saved profile independently.
- Mode: bindings; alias varX from Double value JSON, pointer /varX.
- Prompt: “Take the number {{inputs.varX}}, multiply it by 10, and return only one JSON object with a numeric varY property. No explanation or markdown.”
- Output: jsonObject.

End: result from Multiply by ten JSON, empty pointer (whole object).

Expected arithmetic: first {"varX":14}, then {"varY":140}. This is a user-check expectation, not a guaranteed model result or built-in arithmetic evaluator. The critical evidence is that the SECOND request actually contains the first node's returned value, no unresolved placeholder, and no unrelated history. Automated controlled fixtures verify exact request bodies and counts. The user's real model behavior is recorded separately.

Also test a simple literal-text Start -> Model Call -> End workflow before this JSON example, so model formatting and basic transport failures are not conflated.

References [S1]–[S6] are in docs/M3_SOURCES.md.
