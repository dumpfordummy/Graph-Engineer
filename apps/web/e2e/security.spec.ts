import { test, expect, apiBase } from './fixture'

for (const origin of ['http://127.0.0.1:5187', 'http://127.0.0.1:5188']) {
  test(`local session protections apply through ${origin.endsWith('5188') ? 'the actual Vite proxy' : 'the direct API'}`, async ({ playwright, backend }) => {
    const anonymous = await playwright.request.newContext({ extraHTTPHeaders: { Origin: 'http://127.0.0.1:5188' } })
    const session = await backend.session()
    const paired = await playwright.request.newContext({ storageState: session.state,
      extraHTTPHeaders: { Origin: 'http://127.0.0.1:5188', 'X-GE-CSRF': session.csrfToken } })
    try {
      expect((await anonymous.get(`${origin}/api/providers`)).status()).toBe(401)
      expect((await anonymous.post(`${origin}/api/providers/${crypto.randomUUID()}/test`, { data: { expectedRevision: 1, connectionVersion: 1 } })).status()).toBe(401)
      expect((await paired.get(`${origin}/api/providers`)).status()).toBe(200)
      const runId = crypto.randomUUID()
      for (const route of ['/runs', `/runs/${runId}`, `/runs/${runId}/events`, `/runs/${runId}/artifacts/${crypto.randomUUID()}`, `/runs/submissions/${crypto.randomUUID()}`, `/workflows/${crypto.randomUUID()}/readiness`]) {
        expect((await anonymous.get(`${origin}/api${route}`)).status()).toBe(401)
        expect((await paired.get(`${origin}/api${route}`, { headers: { Origin: 'http://hostile.example' } })).status()).toBe(403)
      }
      for (const [route, data] of [[`/runs/${runId}/cancel`, {}], [`/workflows/${crypto.randomUUID()}/runs`, { submissionId: crypto.randomUUID(), expectedRevision: 1, input: {} }]] as const) {
        expect((await anonymous.post(`${origin}/api${route}`, { data })).status()).toBe(401)
        expect((await paired.post(`${origin}/api${route}`, { data, headers: { 'X-GE-CSRF': '' } })).status()).toBe(403)
        expect((await paired.post(`${origin}/api${route}`, { data, headers: { Origin: 'null' } })).status()).toBe(403)
      }
      expect((await anonymous.post(`${origin}/hubs/runs/negotiate?negotiateVersion=1`)).status()).toBe(401)
      expect((await paired.post(`${origin}/hubs/runs/negotiate?negotiateVersion=1`, { headers: { 'X-GE-CSRF': '' } })).status()).toBe(403)
      for (const hostileOrigin of ['http://hostile.example', 'null', 'http://localhost:5188']) {
        expect((await paired.get(`${origin}/api/providers`, { headers: { Origin: hostileOrigin } })).status()).toBe(403)
        expect((await paired.post(`${origin}/api/providers/${crypto.randomUUID()}/test`, {
          data: { expectedRevision: 1, connectionVersion: 1 }, headers: { Origin: hostileOrigin },
        })).status()).toBe(403)
      }
      expect((await paired.get(`${origin}/api/providers`, { headers: { Host: 'rebinding.example:5188', 'X-Forwarded-Host': '127.0.0.1:5188' } })).status()).toBe(403)
      expect((await paired.post(`${origin}/api/session/logout`, { data: {}, headers: { 'X-GE-CSRF': '' } })).status()).toBe(403)
      expect((await paired.post(`${origin}/api/providers`, { form: { name: 'simple-form-must-fail' } })).status()).toBe(415)
      const result = await paired.get(`${origin}/api/providers`)
      expect(result.headers()['access-control-allow-origin']).toBeUndefined()
      expect(result.headers()['cache-control']).toContain('no-store')
      // Rejected requests did not revoke the valid session or create a profile.
      expect((await paired.get(`${apiBase}/workflows`)).status()).toBe(200)
    } finally { await anonymous.dispose(); await paired.dispose() }
  })
}
