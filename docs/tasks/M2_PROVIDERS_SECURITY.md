# M2 — Provider profiles, local security, and a real connection test

## 1. Assignment and boundaries

Implement this milestone end to end in the existing Vue/C# repository. Do not rebuild M1 or stop at scaffolding. Read AGENTS.md, the current architecture/contracts/progress, M1_REPORT.md, and M1_LEAD_REVIEW.md first.

This assignment supersedes stale M1-only authorization statements, not the repository's other rules. Update those scope statements narrowly. Do not overwrite user changes or reset the working tree. No automatic commit, push, merge, deployment, or M3.

Keep the local Windows, Vue 3/TypeScript/Vue Flow, ASP.NET Core/.NET 10, EF Core/SQLite design. Preserve the current look and graph contracts. Reuse useful existing components and tests. No new workflow framework, microservices, login accounts, generic plugin system, or broad dependency upgrade.

M2 delivers:
- Local browser-session and request protections before credential handling.
- Reusable provider profiles and protected secrets.
- One implemented protocol: **OpenAI Responses-compatible text requests**.
- One explicit, non-streaming connection probe, not graph execution.
- A profile selector in Model Call inspectors.
- Tests and an honest operator handoff.

Responses is the initial choice because the user's previously shared working Codex configuration used `wire_api = "responses"`. This is historical setup context, not proof that a new direct client or every Responses feature works. Verify the application adapter against the user's actual deployment through a later user-operated synthetic probe. Do not inspect Codex auth/config files for private endpoints or keys.

Do not advertise Chat Completions as implemented. Do not silently switch protocols after an error. A deployment incompatibility is a result to report, not permission to add unreviewed fallbacks.

## 2. Baseline and lifecycle carry-over

Inspect Git status, applicable instructions, current dependencies, scripts, existing API/document contracts, and test fixtures. Record a short plan and proceed.

Run the current full check script against isolated test data before changing implementation. Stop only confirmed owned project processes when needed; never broadly terminate node.exe or dotnet.exe. Do not reinstall dependencies while a running Vite process holds their native modules. Record baseline failures or blocked checks accurately.

Address M1-C1 from the lead review. Verify normal Ctrl+C and controlled sibling-start failure using dedicated ports and owned process handles. Verify ports/listeners are released and owned descendants no longer run. Record parent-exit investigation and remaining limitations separately. An ordinary SmokeTest alone is insufficient evidence of interruption cleanup.

Avoid unrelated launcher rewrites. Keep startup/recovery commands Windows-friendly and do not change machine ACLs or install global prerequisites silently.

## 3. Define contracts before parallel work

Keep graph definitions, editor layout, provider settings, credential records, and future run state separate. No credentials, endpoint security approvals, response histories, or encrypted secret blobs belong in workflow JSON.

Define provider contracts before splitting backend/frontend work. Suggested profile fields, adapted to existing conventions:
- Stable profile ID, display name, revision, timestamps.
- Protocol identifier `openai-responses`.
- API base URL, model ID, request timeout, bounded output-token setting.
- Authentication mode: Bearer API key, or explicitly selected no-auth for an approved local/private endpoint.
- Server-only credential reference; public views expose a credential-present flag only.
- Explicit endpoint trust/transport settings owned by the local user, never by imported graphs.
- Sanitized last-test metadata tied to the tested connection configuration version.

Use optimistic concurrency for profile changes. Keep update semantics explicit. Renaming or changing model metadata must not accidentally erase a saved credential. A blank form field is not a request to delete it. Model credential changes as Keep, Replace, or Remove; reject inconsistent operations. Do not round-trip a masked placeholder as an actual key.

Do not hold a database transaction open across a provider network call. A delayed test result for an old configuration must not mark a subsequently edited profile as verified. Keep ordinary profile revision and the tested connection fingerprint/version coherent.

Profile metadata and encrypted credentials must change consistently. A failed update must not leave metadata pointing at a missing secret or silently destroy the previous working credential. Prefer the smallest transactionally coherent design; a server-side secret-store boundary does not require a separate service.

Preserve existing M1 workflow import/export. Use the existing optional provider reference rather than expanding graph JSON with connection settings. Old drafts with no provider remain editable and saveable. Unknown or deleted references are shown as unresolved, never silently rebound by display name.

## 4. Local session and inbound request security

Write a short `docs/SECURITY.md` describing the actual local threat boundary before implementing it. Address hostile web pages reaching localhost, malicious URLs/redirects, accidental secret disclosure, and untrusted imported graph data. Do not claim resistance to an administrator or malware running as the same Windows user.

Required properties:
- API and development frontend remain bound to loopback.
- Enforce exact configured Host and allowed browser origins, including the selected development ports. Reject arbitrary DNS aliases and untrusted/null origins for sensitive browser requests. Do not trust forwarded host/origin headers from arbitrary clients.
- Establish a local browser session before reading private application data, changing profiles/workflows, accessing credential operations, or triggering provider tests. Health and the narrowly defined pairing bootstrap are exceptions.
- Use supported ASP.NET Core session/authentication/data-protection and antiforgery primitives. Do not invent encryption or treat CORS alone as authorization.
- Use an HttpOnly session cookie with restrictive SameSite behavior and antiforgery checks for state-changing requests. Set Secure where HTTPS is used; document the loopback-only HTTP development exception and do not extend it to LAN hosting.
- No wildcard credentialed CORS, permissive form/text POSTs, or anonymous proxy/test endpoints.
- Session expiration must preserve unsaved editor/profile metadata, while transient key fields are cleared. Re-authentication should not silently reissue a paid provider request.

Choose and document a small bootstrap that has a real local trust source. Default: a cryptographically random launch-scoped pairing token in an owner-restricted per-user runtime file, entered once into the local pairing screen, exchanged for the browser session, and rotated on backend restart. Keep that token out of URLs, Git, normal logs, exports, screenshots, and browser persistent storage. Startup may display a non-sensitive instruction/path, not write the token into captured dev logs. Do not serve the token anonymously over HTTP. Apply bounds and throttling to bootstrap attempts.

A simpler alternative is acceptable only with documented equivalent protection and explicit trust assumptions; an endpoint handing any caller a privileged session without a local secret is not equivalent. Origin checks do not authenticate a local OS user. No OAuth, account registration, or remote identity platform is requested.

Automated tests may retrieve their own synthetic bootstrap from an isolated test instance. They must not read the user's live bootstrap or credential store. Do not add production test-bypass switches.

## 5. Windows credential storage and handling

Use an ISecretStore-style backend boundary backed by Windows DPAPI with CurrentUser protection for this Windows-first milestone. DPAPI-protected ciphertext may live in a separate credential record in the per-user SQLite database, provided it is excluded from all public/export DTOs and updated safely. Do not store plaintext keys or invent a static AES key. Do not use machine-wide protection merely to simplify access. Pin a stable compatible dependency; documentation displaying prerelease overloads is not permission to install a preview package. [S1]

Treat the browser field as transient input: the user types the key, it is sent only to the protected local API, and it is cleared on successful storage or leaving the form. Never persist it in Pinia snapshots, localStorage/sessionStorage, query strings, frontend environment bundles, telemetry, or automatic draft recovery. Never send provider requests directly from the browser. [S2]

Profile GET/list/update responses must not return the key, ciphertext, session token, or a decryptable secret reference. Prefer `hasCredential` rather than displaying any real suffix. Do not log secret-bearing request bodies, authorization headers, bootstrap values, or raw upstream bodies. Clear transient input without losing safe metadata after errors.

Keep saved credentials bound to the destination they were approved for. Changing scheme, host, port, API base path, or authentication mode must not silently reuse a stored key against the new destination. Require explicit destination confirmation and credential replacement/re-entry for that change. Name-only and model-only changes preserve the secret; connection-affecting changes invalidate prior verification.

Handle missing/corrupt/unreadable protected data with a sanitized actionable error and a replace-credential path; never fall back to plaintext, a default key, or Codex login. Document that copying the database to a different Windows account/machine may require credentials to be entered again. [S1]

Delete credentials when an unused profile is deleted or its key explicitly removed. Logical deletion is not a promise to securely erase SQLite pages, WALs, or old backups; document that boundary. No secret export/import feature is needed.

## 6. Outbound endpoint policy

User-supplied provider URLs are not an unrestricted proxy feature. Validate on the backend, both when saving and before making a request. Default to HTTPS and normal certificate validation. Never add a 'trust all certificates' fallback. [S3]

Define API base URL unambiguously: it includes any API prefix, for example `https://provider.example/custom/v1`; the adapter appends `/responses`. Preserve reverse-proxy prefixes. Do not blindly prepend/append another `/v1`. Show the resolved endpoint before saving/testing. Reject or clearly diagnose a full method URL entered as a base.

Reject credentials in userinfo, query-string credentials, fragments, unsupported schemes, and malformed or oversized URLs. For M2, reject query strings entirely rather than introducing a second secret channel. Reject path-normalization tricks that escape the approved prefix.

Local/private-network provider access is needed for this product but must be explicit. Require a local-user approval for that exact destination; require a separate visible acknowledgment for unencrypted HTTP. Imported graph files cannot grant either permission. Avoid a global 'allow all private URLs' switch.

Disable automatic redirects. A redirect is a sanitized error, not permission to resend credentials elsewhere. Do not disable TLS verification to support a self-signed deployment; let the user correct trust through a separately approved environment change. [S3]

Account for IPv4, IPv6, mapped addresses, and DNS changes when enforcing destination restrictions. A one-time hostname check followed by a new unchecked resolution is insufficient. Use maintained platform facilities and test the actual destination enforcement. Deny unspecified, multicast and link-local/metadata targets; refuse the app's own API/frontend destinations. Permit specifically approved loopback or private model-service destinations, not arbitrary internal scanning. Do not implement a generic network discovery tool.

A provider HTTP client must not leak secrets to another profile through shared mutable default headers. Build per-request authorization from the specific validated profile snapshot. Disable ambient cookies/default OS credentials and unreviewed proxy behavior. If the environment requires a proxy, report the limitation rather than silently routing secrets through it.

Bound connection time, total time, response body size, and simultaneous probes. Default to a small finite timeout with a documented user-configurable ceiling suitable for a slow self-hosted model. Do not auto-retry generation POSTs or duplicate them on browser refresh.

## 7. Responses adapter and test semantics

Implement one non-streaming text request through a backend adapter using current official Responses documentation. Do not reuse Chat Completions request/response parsing. Raw Responses output contains typed items; extract assistant output-text items intentionally rather than assuming the first item is a message. [S4]

The probe sends a fixed synthetic prompt, for example `Reply with GE_CONNECTION_OK.` It must never send the open workflow, repository files, old conversations, or credentials in the prompt. Set `stream: false` and request `store: false`; surface rejection of a privacy/control option rather than silently removing it. The provider's actual data-retention policy remains separate from this request flag.

Use the selected model ID exactly as configured. Keep reasoning/temperature/tool parameters absent by default instead of sending every possible setting. No fake model catalog. Manual model entry must work without GET /models. Model discovery, arbitrary headers, custom request JSON, streaming, structured outputs, and tools are deferred in this milestone.

A connection success requires a successful HTTP response, a valid completed Responses payload, and nonempty assistant text. HTTP 200 alone is insufficient. HTML error pages, malformed JSON, incomplete/failed responses, and missing text must not turn green. Matching the requested probe phrase can be shown separately; an otherwise valid answer differing in wording is not automatically a transport failure.

Classify failures coherently: invalid local configuration, credential missing/unreadable, session/security rejection, DNS/connectivity failure, TLS failure, timeout, rate limit, auth/access failure, rejected request/model, unexpected payload, and incomplete output. Do not label every 400 as model-not-found or every timeout as unsupported capability. Provider-specific causes that cannot be verified remain generic.

Return only capped, sanitized diagnostic metadata and a small plain-text response preview. Avoid raw upstream error bodies. Redact the active synthetic credential even when a test fixture echoes it back in an error or successful text response. Render all text safely, not with v-html.

Test records contain time, tested connection version, result category, duration, observed model/request ID when safe, and actual usage only if supplied. Do not invent token usage, cost, latency, or compatibility. Do not create Run/NodeAttempt records or a Runs page in M2.

Text capability may be labeled 'Verified by latest test' for the tested configuration. Streaming, tool calling, and JSON-schema output remain 'Not tested / not implemented in M2', never inferred from the text probe. A transient failure is not proof a feature is unsupported.

Test only a saved profile version and show when edits need saving first. Snapshot profile settings and the credential coherently; late results cannot verify a newer configuration. Repeated clicks while a request is pending must not launch duplicate probes. Cancel/timeout is best effort and does not guarantee the provider stopped work or waived charges.

## 8. Vue user experience

Add a Settings / Model connections entry without redesigning the editor. Build a profile list and create/edit form. Reuse current spacing, components, loading/errors, accessible labels, and dirty-navigation protection.

The form shows name, implemented protocol, API base URL, resolved endpoint, manual model ID, authentication mode, key Keep/Replace/Remove controls, timeout/output bounds, explicit local/private/HTTP permissions, and last test status. Do not add nonfunctional future protocol selectors or fabricated capability badges.

Save settings without automatically calling the provider. Test is a separate action, disabled for unsaved connection changes or incomplete credentials. Show that the synthetic request may incur provider usage. Successful save and successful connection are different states.

Replace 'Future provider profile ID' in Model Call inspectors with a real profile-name selector. Show model/protocol alongside the selection and offer navigation to connections while preserving/confirming unsaved work. No global default or fallback profile is necessary in M2; assign profiles per node. Two Model Call nodes may select different profiles.

Keep node configuration saveable when a profile is missing. Provider readiness checks are local and do not trigger network requests. Distinguish structure-valid, profile-configured, and previously connection-tested; none means workflow execution exists. Run remains disabled and labeled M3.

Deleting a profile referenced by a saved workflow must produce an explicit reference/conflict response rather than silently rewriting the graph. Handle unresolved references from imported/draft data honestly. Never export profile credentials, trust permissions, or endpoints with the workflow.

Keep prompts as plain configuration. Do not implement interpolation, varX bindings, expressions, model tools, agent sessions, or control-flow execution.

## 9. Required verification

Keep xUnit, real SQLite API tests, Vitest/Vue Test Utils, and Playwright. Automated provider tests use controlled local HTTP fixtures with conspicuous synthetic keys and generated fixture responses. Fixture responses are valid test evidence, not real deployment verification. No normal-application mock fallback is allowed.

Required acceptance categories:
1. Full M1 baseline and final regression; editor persistence/import/export/concurrency/keyboard safety still work under the local session.
2. M1-C1 lifecycle checks with process/port evidence and an honest abrupt-exit limitation statement.
3. Fresh and existing database migration; existing M1 workflows are preserved. Never reset user data to pass migration tests.
4. Profile create/read/update/delete with isolated SQLite, stale-edit conflicts, restart persistence, and reference handling.
5. Credential Keep/Replace/Remove semantics; metadata edits preserve the key; failed/stale updates do not destroy the working credential.
6. A real Windows DPAPI round-trip using only synthetic credentials; corruption/unavailable-store handling; no plaintext sentinel in stored DB/WAL/log/export/API-return artifacts. A fake store alone is insufficient for the Windows acceptance claim.
7. Anonymous and unapproved cross-origin profile/test access rejected; valid local pairing/session works; bad Host, null/unknown origin, missing antiforgery, and simple-form POSTs fail safely. Run security checks against the actual configured dev proxy as well as the API where applicable.
8. Endpoint policy: approved prefix preservation, no doubled v1, redirect rejection, private/HTTP explicit approval, forbidden address classes and app-self targets, DNS-change enforcement, and TLS verification. Use controlled fixtures, not real scanning.
9. Wire request/response contract: correct path, model, per-request Authorization, synthetic input, no unwanted compatibility fields, completed/nonempty text parsing including typed-item ordering.
10. Wrong key, 400/403/404/429/5xx, malformed/HTML/incomplete payload, timeout, size cap, cancellation, and sanitized error classification. No hidden auto-retry or cross-profile header leakage.
11. Echoed-synthetic-secret redaction in errors and response preview; no real-key traces or screenshots; plain-text rendering resists HTML injection.
12. A test result arriving after profile edits stays tied to the old connection version; repeated click/reload does not start another probe.
13. Browser profile selection round-trip; broken reference behavior; dirty state after save/session errors; Save never performs inference; Run stays disabled.
14. Human-operated real provider check, separately reported and never fabricated. It may remain NOT RUN when Codex has no authorized credentials.

Use isolated test data, runtime pairing material, and fixture ports. Do not store a real API key in a test environment variable, command-line argument, Playwright trace, HAR, video, screenshot, or report. Do not inspect the user's live credential DB to verify a claim. Test redaction and storage with synthetic values instead.

## 10. User-operated real-provider gate

Complete all possible implementation and automated testing first. Give the user exact startup and pairing instructions. The user then enters their actual base URL/model/key in the local app and clicks Test with the fixed synthetic probe.

The report must separate:
- automated fixture verification;
- genuine Windows storage/session verification with synthetic data;
- user-operated actual provider verification.

Without the third, state `READY FOR USER PROVIDER CHECK`. Do not ask the user to paste credentials into Codex/chat. Do not read Codex authentication files or assume ChatGPT subscription credentials are API credentials. [S2]

After a real successful test, have the user restart the backend, pair again if required, verify the profile still shows Credential saved without revealing it, and test again. They can safely report status, elapsed time, and a sanitized preview; internal hostnames may be obscured. Real secret-bearing browser/network recordings are not acceptable handoff artifacts.

If the deployment rejects the adapter, report the sanitized incompatibility and stop at that gate. Keep safe local functionality usable; do not manufacture a provider success or silently broaden the protocol scope.

## 11. Deliverables and stopping point

Update actual README startup/pairing/provider/recovery instructions, docs/CONTRACTS.md, docs/SECURITY.md, docs/ENVIRONMENT.md as needed, and docs/PROGRESS.md. Include migrations and tests. Record consequential decisions and any deviations.

Write `docs/handoffs/M2_REPORT.md` using the supplied template. Attach synthetic-only logs/screenshots under `.artifacts/m2/`. Report exact commands, exit codes, substantive test counts, remaining risks, and NOT RUN checks. Do a final focused source/diff review for unrelated edits, permissive security bypasses, credential exposure, and pretend runtime features.

Do not start M3, enable Run, invoke a coding-agent subprocess from the app, add shell nodes or loops, auto-commit, push, or merge.

References [S1]–[S5] are in `docs/M2_SOURCES.md`. These are implementation references, not evidence that this repository already complies.
