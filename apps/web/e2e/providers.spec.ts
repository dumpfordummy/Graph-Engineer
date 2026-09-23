import { createServer } from 'node:http'
import { once } from 'node:events'
import { Buffer } from 'node:buffer'
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import type { Page } from '@playwright/test'
import { test as appTest, expect, apiBase, artifacts, sampleDocument } from './fixture'

const syntheticKey = 'SYNTHETIC_GE_BROWSER_KEY_NOT_A_REAL_CREDENTIAL'
type WireCall = { path: string; authorization?: string; body: Record<string, unknown> }
type Provider = { baseUrl: string; calls: WireCall[]; delayMs: number }
const test = appTest.extend<{ provider: Provider }>({
  provider: async ({ backend }, use) => {
    // Tie the fixture's lifetime to the isolated application worker.
    await backend.session();
    const provider: Provider = { baseUrl: '', calls: [], delayMs: 0 }
    const server = createServer(async (request, response) => {
      const chunks: Buffer[] = []
      for await (const chunk of request) chunks.push(Buffer.from(chunk))
      provider.calls.push({ path: request.url!, authorization: request.headers.authorization, body: JSON.parse(Buffer.concat(chunks).toString()) as Record<string, unknown> })
      if (provider.delayMs) await new Promise(resolve => setTimeout(resolve, provider.delayMs))
      response.writeHead(200, { 'Content-Type': 'application/json', 'x-request-id': 'synthetic-browser-request' })
      response.end(JSON.stringify({ object: 'response', status: 'completed', model: 'fixture-browser-model',
        output: [{ type: 'reasoning', summary: [] }, { type: 'message', role: 'assistant', status: 'completed', content: [{ type: 'output_text', text: `GE_CONNECTION_OK ${syntheticKey} <img src=x onerror="window.fixtureInjected=true">` }] }],
        usage: { input_tokens: 9, output_tokens: 6, total_tokens: 15 } }))
    })
    server.listen(0, '127.0.0.1')
    await once(server, 'listening')
    const address = server.address()
    if (!address || typeof address === 'string') throw new Error('Fixture listener unavailable')
    provider.baseUrl = `http://127.0.0.1:${address.port}/custom/v1`
    try { await use(provider) }
    finally { server.closeAllConnections(); await new Promise<void>(resolve => server.close(() => resolve())) }
  },
})

async function createInBrowser(page: Page, provider: Provider, name: string) {
  await page.goto('/settings/connections/new')
  await page.getByLabel('Profile name', { exact: true }).fill(name)
  await page.getByLabel('API base URL', { exact: true }).fill(provider.baseUrl)
  await page.getByLabel('Model ID', { exact: true }).fill('fixture-browser-model')
  await page.getByLabel('Credential action', { exact: true }).selectOption('replace')
  await page.getByLabel('API key', { exact: true }).fill(syntheticKey)
  await page.getByLabel('Allow this exact private or loopback destination', { exact: true }).check()
  await page.getByLabel('Acknowledge unencrypted HTTP', { exact: true }).check()
  await expect(page.getByTestId('resolved-endpoint')).toHaveText(`${provider.baseUrl}/responses`)
  const saving = page.waitForResponse(response => response.url().endsWith('/api/providers') && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save profile', exact: true }).click()
  const response = await saving
  expect(response.status()).toBe(201)
  await expect(page.getByTestId('credential-status')).toContainText('Credential saved')
  expect(await response.text()).not.toContain(syntheticKey)
  await expect(page.getByLabel('API key', { exact: true })).toHaveCount(0)
  return await response.json() as { id: string; revision: number; connectionVersion: number; hasCredential: boolean }
}

async function pairInBrowser(page: Page, dataDirectory: string) {
  const token = (await readFile(path.join(dataDirectory, 'runtime/pairing-token.txt'), 'utf8')).trim()
  await expect(page.getByRole('dialog')).toBeVisible()
  // Keep the runtime token out of Playwright's human-readable Fill-step titles and artifacts.
  await page.getByLabel('Pairing token', { exact: true }).evaluate((input, value) => {
    const element = input as HTMLInputElement
    element.value = value
    element.dispatchEvent(new Event('input', { bubbles: true }))
  }, token)
  await page.getByRole('button', { name: 'Pair local browser', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
}

test('profile save, explicit probe, redaction, duplicate prevention and protected restart are real', async ({ page, provider, backend, request }) => {
  await page.context().clearCookies()
  await page.goto('/settings/connections')
  await pairInBrowser(page, backend.dataDirectory)
  const profile = await createInBrowser(page, provider, 'Browser fixture connection')
  expect(provider.calls).toHaveLength(0)
  provider.delayMs = 400
  await page.getByRole('button', { name: 'Test connection', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Testing…', exact: true })).toBeDisabled()
  await expect(page.getByRole('region', { name: 'Last connection test' })).toContainText('Text response verified')
  expect(provider.calls).toHaveLength(1)
  expect(provider.calls[0]).toEqual({ path: '/custom/v1/responses', authorization: `Bearer ${syntheticKey}`,
    body: { model: 'fixture-browser-model', input: 'Reply with GE_CONNECTION_OK.', stream: false, store: false, max_output_tokens: 128 } })
  const preview = page.getByLabel('Response preview', { exact: true })
  await expect(preview).toContainText('[redacted]')
  await expect(preview).not.toContainText(syntheticKey)
  await expect(preview.locator('img')).toHaveCount(0)
  expect(await page.evaluate(() => 'fixtureInjected' in window)).toBe(false)
  await page.screenshot({ path: path.join(artifacts, 'provider-tested.png'), fullPage: true })
  await page.reload()
  await expect(page.getByTestId('credential-status')).toContainText('Credential saved')
  expect(provider.calls).toHaveLength(1)
  const restart = await backend.restart()
  expect(restart.previousPid).not.toBe(restart.nextPid)
  await page.reload()
  await pairInBrowser(page, backend.dataDirectory)
  await expect(page.getByTestId('credential-status')).toContainText('Credential saved')
  const repeatedProbe = page.waitForResponse(response => response.url().endsWith(`/api/providers/${profile.id}/test`))
  await page.getByRole('button', { name: 'Test connection', exact: true }).click()
  expect((await repeatedProbe).status()).toBe(200)
  await expect(page.getByRole('region', { name: 'Last connection test' })).toContainText('Text response verified')
  expect(provider.calls).toHaveLength(2)
  await backend.pairRequest(request)
  const publicProfile = await (await request.get(`${apiBase}/providers/${profile.id}`)).text()
  expect(publicProfile).not.toContain(syntheticKey)
  for (const suffix of ['', '-wal']) {
    const bytes = await readFile(path.join(backend.dataDirectory, `workflows.db${suffix}`)).catch(() => Buffer.alloc(0))
    expect(bytes.includes(Buffer.from(syntheticKey))).toBe(false)
  }
  expect(await readFile(path.join(backend.dataDirectory, 'backend.log'), 'utf8')).not.toContain(syntheticKey)
})

test('two real profile selections round-trip without inference and saved references block deletion', async ({ page, provider, request }) => {
  const first = await createInBrowser(page, provider, 'Per-node first model')
  const second = await createInBrowser(page, provider, 'Per-node second model')
  const document = sampleDocument('M2 per-node references')
  document.definition.nodes.splice(2, 0, { id: 'model-2', type: 'modelCall', typeVersion: 1, name: 'Second model', description: '', configuration: { prompt: 'A second synthetic prompt', providerProfileId: null } })
  document.layout.nodes.push({ nodeId: 'model-2', x: 430, y: 350 })
  const seeded = await (await request.post(`${apiBase}/workflows`, { data: { document } })).json() as typeof document
  await page.goto(`/workflows/${seeded.workflow.id}`)
  await page.locator('[data-node-id="model-1"]').click()
  await page.getByLabel('Provider profile', { exact: true }).selectOption(first.id)
  await expect(page.getByTestId('provider-readiness')).toContainText('Configured · not tested')
  await page.locator('[data-node-id="model-2"]').click()
  await page.getByLabel('Provider profile', { exact: true }).selectOption(second.id)
  const saved = page.waitForResponse(response => response.url().endsWith(`/api/workflows/${seeded.workflow.id}`) && response.request().method() === 'PUT')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await saved).status()).toBe(200)
  await page.reload()
  await page.locator('[data-node-id="model-1"]').click()
  await expect(page.getByLabel('Provider profile', { exact: true })).toHaveValue(first.id)
  await page.locator('[data-node-id="model-2"]').click()
  await expect(page.getByLabel('Provider profile', { exact: true })).toHaveValue(second.id)
  await expect(page.getByRole('button', { name: 'Run workflow', exact: true })).toBeDisabled()
  expect(provider.calls).toHaveLength(0)
  const removed = await request.delete(`${apiBase}/providers/${first.id}`, { data: { expectedRevision: first.revision } })
  expect(removed.status()).toBe(409)
  expect((await removed.json()).title).toBe('Profile is referenced')
  const exported = await (await request.get(`${apiBase}/workflows/${seeded.workflow.id}`)).text()
  expect(exported).not.toMatch(/baseUrl|allowPrivateNetwork|ciphertext|SYNTHETIC_GE|lastTest/)
  const missing = sampleDocument('Unknown profile remains a safe draft')
  const unknown = crypto.randomUUID()
  Object.assign(missing.definition.nodes[1]!.configuration, { providerProfileId: unknown })
  const broken = await (await request.post(`${apiBase}/workflows`, { data: { document: missing } })).json() as typeof missing
  await page.goto(`/workflows/${broken.workflow.id}`)
  await page.locator('[data-node-id="model-1"]').click()
  await expect(page.getByLabel('Provider profile', { exact: true })).toHaveValue(unknown)
  await expect(page.getByText(/Unresolved profile reference\. This draft/)).toBeVisible()
  await page.screenshot({ path: path.join(artifacts, 'unresolved-profile.png'), fullPage: true })
})

test('session rejection preserves profile metadata and graph drafts while clearing transient keys', async ({ page, provider, request, backend }) => {
  const profile = await createInBrowser(page, provider, 'Session recovery fixture')
  await page.getByLabel('Profile name', { exact: true }).fill('Preserve safe unsaved name')
  await page.getByLabel('Credential action', { exact: true }).selectOption('replace')
  await page.getByLabel('API key', { exact: true }).fill('SYNTHETIC_TRANSIENT_REPLACEMENT')
  await page.context().clearCookies()
  await page.getByRole('button', { name: 'Save profile', exact: true }).click()
  await pairInBrowser(page, backend.dataDirectory)
  await expect(page.getByLabel('Profile name', { exact: true })).toHaveValue('Preserve safe unsaved name')
  await expect(page.getByLabel('API key', { exact: true })).toHaveValue('')
  await page.getByLabel('Credential action', { exact: true }).selectOption('keep')
  await page.getByRole('button', { name: 'Save profile', exact: true }).click()
  await expect(page.getByText('Profile saved on this computer. Saving did not call the provider.')).toBeVisible()
  expect(provider.calls).toHaveLength(0)
  const stored = await (await request.get(`${apiBase}/providers/${profile.id}`)).json() as { name: string; hasCredential: boolean }
  expect(stored).toMatchObject({ name: 'Preserve safe unsaved name', hasCredential: true })
  const document = await (await request.post(`${apiBase}/workflows`, { data: { document: sampleDocument('Session graph fixture') } })).json() as ReturnType<typeof sampleDocument>
  await page.goto(`/workflows/${document.workflow.id}`)
  await page.getByLabel('Workflow name', { exact: true }).fill('Unsaved graph after session rejection')
  await page.context().clearCookies()
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await pairInBrowser(page, backend.dataDirectory)
  await expect(page.getByLabel('Workflow name', { exact: true })).toHaveValue('Unsaved graph after session rejection')
  await expect(page.getByTestId('save-status')).toContainText(/unsaved|Save needed/i)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByTestId('save-status')).toContainText('Saved · revision 2')
})
