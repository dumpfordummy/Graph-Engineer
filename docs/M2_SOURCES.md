# M2 implementation references

Reviewed 2026-09-23. The assignment's acceptance criteria are project decisions. These primary sources support the underlying API and security mechanisms; they do not verify the submitted application.

[S1] Microsoft — ProtectedData and Windows DPAPI.
`https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata?view=windowsdesktop-10.0`
`https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata`

ProtectedData wraps Windows DPAPI. CurrentUser protection uses the user's credentials; it is not a defense against processes already running with that user's authority. Account/machine/profile conditions matter when moving protected data. The documentation may show prerelease package overloads; use stable dependencies compatible with the project's pinned runtime.

[S2] OpenAI — API authentication.
`https://developers.openai.com/api/reference/overview`

API credentials are secrets, belong on the server, and are used with Bearer authentication. Do not embed them in frontend code or reuse unrelated application login files.

[S3] OWASP — Server-Side Request Forgery Prevention Cheat Sheet.
`https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html`

Use constrained destinations and validate addresses/URLs. Redirects and DNS changes can undermine a naive initial check. The project's exact-destination approvals and redirect rejection implement a deliberately narrow local-provider policy.

[S4] OpenAI — Migrate to Responses.
`https://developers.openai.com/api/docs/guides/migrate-to-responses`

Responses uses typed input/output items rather than the Chat Completions messages/choices contract. Streaming events and structured-output fields also differ. For M2, support and advertise only the implemented non-streaming text probe. A store:false request is not a general guarantee about another provider's retention policy.

[S5] Microsoft / OWASP — Antiforgery and CSRF prevention.
`https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0`
`https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html`

Use established antiforgery mechanisms and origin controls for browser-issued state-changing requests. Authentication/session establishment is a distinct requirement; CORS is not a substitute for it.

## Project review basis

User-provided M1_REPORT.md and screenshot, received in this conversation. See M1_LEAD_REVIEW.md for which evidence was available and which repository/log checks were not independently performed.
