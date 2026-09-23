# Local security boundary (M2)

This application is for one Windows user on loopback. The boundary protects private workflow/provider data and paid probe actions from unrelated web pages, rejects unsafe provider destinations, and limits accidental credential disclosure. It does not resist an administrator, malware, extensions inspecting the page, or another process running as the same Windows user. Imported graph data is untrusted configuration; it cannot create profiles, approve endpoints, supply credentials or execute anything. Run stays disabled.

## Browser and local bootstrap

Use ASP.NET Core cookie authentication, Data Protection and antiforgery. Each backend launch generates a cryptographically random 256-bit pairing token in an owner-restricted runtime directory under the configured per-user data directory. Its file path is safe to display; its contents are never logged or served over HTTP. The user opens the file locally and enters it in the pairing screen. Tests read only their isolated instance's runtime file. Pairing is bounded and throttled. Restart rotates the token and invalidates old sessions with a launch-identity claim. No account system or automatic privileged anonymous session exists.

The session cookie is HttpOnly, SameSite Strict, eight-hour absolute lifetime, nonpersistent, and Secure on HTTPS. Loopback HTTP development necessarily omits Secure; this exception never authorizes LAN binding. Antiforgery tokens live in browser memory and travel in X-GE-CSRF. All private API reads require the cookie; mutations additionally require JSON, antiforgery and an exact configured Origin. Pairing requires JSON and exact Origin plus the local token. Null/unknown origins and unknown Host authorities are rejected. API and Vite bind to loopback; no wildcard CORS or trusted forwarded headers. The health route is public. The session status route reveals only authenticated status and, to an already paired caller, an antiforgery token.

Expiry leaves unsaved safe editor/profile metadata in memory, clears transient credential input, and asks for pairing again. Re-authentication never automatically repeats a provider probe. Leaving the form clears its transient key. Secrets are not stored in Pinia, localStorage/sessionStorage, query strings, exported documents, or frontend environment variables.

## Protected storage

Windows DPAPI CurrentUser protects provider credentials before SQLite receives them. Ciphertext resides in a separate credential table and changes transactionally with profile settings. Keep, Replace and Remove are explicit operations; blank fields never imply removal. Public data exposes only hasCredential. A destination or authentication change requires explicit confirmation and bearer-key re-entry. Corruption or account/machine mismatch returns a sanitized replace-credential message, never a plaintext/default/Codex fallback.

Copying a database to another Windows account/machine may require key re-entry. Logical removal is not secure erasure of old SQLite pages, WALs or backups. Data Protection session key files are also protected for the current Windows user. No live user credential store is inspected for tests.

## Provider traffic

Only saved Responses profiles may send the fixed synthetic text probe. HTTPS with platform certificate validation is the default. Base URLs include their API prefix; append /responses exactly once. Reject userinfo, query, fragment, ambiguous/path-escape encodings and full method URLs. Explicit per-destination approval is necessary for private/loopback access, with a separate visible HTTP acknowledgment. No-auth is restricted to approved private/loopback endpoints.

Resolve and validate IP addresses at actual socket connection time, then connect to that validated address without a second unchecked DNS lookup. Reject unspecified, multicast, link-local/metadata, and the app's own API/frontend destinations, including IPv4-mapped IPv6. Never follow redirects. Disable ambient proxy, cookies and OS credentials; proxy-required deployments are unsupported in M2. Use per-request authorization from a coherent profile/credential snapshot. No shared mutable authorization headers.

Bound connection time, total timeout (5..120 seconds), response body (256 KiB), output (16..4096 tokens), and concurrency (two total, one per profile). Never retry a generation POST. Cancel is best effort; provider work/charges may already have occurred. Request store:false is a protocol request, not a guarantee about the provider's retention policy.

Only completed Responses payloads with nonempty assistant output_text count as success. Typed output is inspected intentionally. Diagnostics are bounded plain text; active secrets are redacted even if echoed. Never log upstream bodies or secret-bearing requests/headers. No inference, usage or capability evidence is fabricated. Streaming, tools and JSON-schema support remain untested/unimplemented. Automated fixture success is separate from the required user-operated provider check.

## Verification and recordings

Automated instances use isolated databases, pairing files, ports and conspicuous synthetic credentials. Browser traces, video and automatic failure screenshots are disabled for M2; explicit screenshots are taken only after pairing/key fields have been cleared. Operators must not capture real secret-bearing network sessions. See the M2 report for actual executed checks and remaining limitations; this design text is not itself verification evidence.

Existing pairing-token files have their own ACL explicitly restricted before rotation; restricting only the parent directory would leave explicit old file grants intact. Kestrel:Endpoints overrides are rejected before server startup because they could otherwise override loopback urls. Runtime directory creation restricts only runtime, not missing shared ancestors. These cases have isolated regression coverage. Provider diagnostic normalization redacts both before and after control-character filtering, before truncation; absent usage counts are omitted rather than synthesized.
