# M3 implementation references

Consulted 2026-09-24. These are primary references for mechanics, not repository verification. The lead's local limits, conservative restart policy, UI behavior, and milestone scope are product decisions. Follow the installed .NET 10/stable dependency contract; web pages can expose examples from several versions.

## Project evidence

The supplied docs/handoffs/M2_REPORT.md and the user's successful connection-test screenshot support docs/handoffs/M2_LEAD_REVIEW.md. Only a report and screenshot were reviewed; raw code, test artifacts, actual transport, and a before/after-restart pair were not independently verified.

## S1 — OpenAI text generation

`https://developers.openai.com/api/docs/guides/text`

Responses contain typed output items; text is not guaranteed at output[0].content[0].text. Separate completed assistant text from other items. A local JSON parse is not evidence of schema-constrained provider generation. This documentation describes OpenAI's API, not a guarantee about the user's compatible GLM endpoint.

## S2 — Responses migration and state

`https://developers.openai.com/api/docs/guides/migrate-to-responses`

Responses has its own request/response and conversation-state mechanisms. For this application's separate stateless node calls, explicitly request store:false and do not attach previous_response_id or hidden history. This does not establish an unrelated provider's retention policy or prove compatibility with every option.

## S3 — ASP.NET Core hosted services

`https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0`

Hosted services support background work and scoped dependencies. Unexpected process exit can skip StopAsync. The documentation's in-memory queue examples are not durable application storage; M3 deliberately uses SQLite and conservative recovery.

## S4 — SignalR JavaScript reconnect and authentication

`https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0`

`https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0`

Automatic reconnect is explicitly configured and initial connection failures need separate handling. Authenticate transport requests and enforce session lifetime, then reload authoritative state after reconnect. Do not adopt .NET 11 authentication-refresh APIs from a multi-version section into this .NET 10 project.

## S5 — SignalR security

`https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0`

CORS restrictions alone do not protect WebSocket connections; verify browser Origin as well as authenticating. Avoid sensitive URL/log payloads. M3 uses the existing paired cookie session, not provider keys or pairing tokens as transport credentials.

## S6 — JSON Pointer (RFC 6901)

`https://www.rfc-editor.org/rfc/rfc6901.html`

Use string JSON Pointers for selecting bound JSON values. Implement empty-root selection, ~0/~1 escaping, exact object keys and array-index rules, and explicit resolution failure. This is a selection syntax, not an expression language.
