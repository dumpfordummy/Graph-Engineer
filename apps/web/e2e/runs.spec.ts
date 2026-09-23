import { createServer } from 'node:http'
import { once } from 'node:events'
import { Buffer } from 'node:buffer'
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import type { APIRequestContext, Page } from '@playwright/test'
import type { WorkflowDocument } from '../src/features/workflows/document'
import { test as appTest, expect, apiBase, artifacts, sampleDocument } from './fixture'

const key = 'SYNTHETIC_M3_BROWSER_KEY_NOT_LIVE'
type Call = { body: Record<string, unknown>; authorization: string | undefined }
type Provider = { baseUrl: string; calls: Call[]; outputs: string[]; hold: boolean; release: () => void }
const test = appTest.extend<{ provider: Provider }>({
  provider: async ({ backend }, use) => {
    await backend.session()
    let release!: () => void
    const barrier = new Promise<void>(resolve => { release = resolve })
    const provider: Provider = { baseUrl: '', calls: [], outputs: ['{"varX":14}', '{"varY":140}'], hold: false, release }
    const server = createServer(async (request, response) => {
      const chunks: Buffer[] = []
      for await (const chunk of request) chunks.push(Buffer.from(chunk))
      const index = provider.calls.length
      provider.calls.push({ body: JSON.parse(Buffer.concat(chunks).toString()) as Record<string, unknown>, authorization: request.headers.authorization })
      if (provider.hold) await barrier
      response.writeHead(200, { 'Content-Type': 'application/json', 'x-request-id': `synthetic-run-${index}` })
      response.end(JSON.stringify({ object: 'response', status: 'completed', model: `fixture-${index}`,
        output: [{ type: 'reasoning', summary: [] }, { type: 'message', role: 'assistant', status: 'completed', content: [{ type: 'output_text', text: provider.outputs[index] ?? 'synthetic fallback text' }] }],
        usage: { input_tokens: 7, output_tokens: 4, total_tokens: 11 } }))
    })
    server.listen(0, '127.0.0.1'); await once(server, 'listening')
    const address = server.address()
    if (!address || typeof address === 'string') throw new Error('Synthetic provider address missing')
    provider.baseUrl = `http://127.0.0.1:${address.port}/v1`
    try { await use(provider) }
    finally { release(); server.closeAllConnections(); await new Promise<void>(resolve => server.close(() => resolve())) }
  },
})

async function profile(request: APIRequestContext, provider: Provider, name: string) {
  const response = await request.post(`${apiBase}/providers`, { data: { name, protocol: 'openai-responses', baseUrl: provider.baseUrl,
    modelId: name, authMode: 'bearer', timeoutSeconds: 30, maxOutputTokens: 128, allowPrivateNetwork: true, allowInsecureHttp: true,
    credential: { action: 'replace', value: key }, confirmDestinationChange: false } })
  expect(response.status()).toBe(201)
  return (await response.json() as { id: string }).id
}
async function seed(request: APIRequestContext, provider: Provider, name: string, two = false, legacy = false) {
  const first = await profile(request, provider, `${name}-first`)
  const second = two ? await profile(request, provider, `${name}-second`) : first
  const document = sampleDocument(name) as WorkflowDocument
  document.definition.nodes[1] = { id: 'model-1', type: 'modelCall', typeVersion: 1, name: 'Double value', description: '', configuration: { prompt: 'Take {{inputs.x}} and double it.', providerProfileId: first } }
  if (two) {
    document.definition.nodes.splice(2, 0, { id: 'model-2', type: 'modelCall', typeVersion: 1, name: 'Multiply by ten', description: '', configuration: { prompt: 'Multiply {{inputs.varX}} by ten.', providerProfileId: second } })
    document.definition.edges[1]!.targetNodeId = 'model-2'
    document.definition.edges.push({ id: 'edge-3', sourceNodeId: 'model-2', sourcePort: 'out', targetNodeId: 'end-1', targetPort: 'in' })
    document.layout.nodes.push({ nodeId: 'model-2', x: 600, y: 180 }); document.layout.nodes[2]!.x = 900
  }
  if (!legacy) document.definition.nodes = document.definition.nodes.map(node => node.type === 'modelCall' ? {
    ...node, typeVersion: 2, configuration: { ...node.configuration, prompt: two ? node.configuration.prompt : 'Literal text with {{inputs.untouched}}', promptMode: two ? 'bindings' : 'literal', outputMode: two ? 'jsonObject' : 'text',
      inputBindings: two ? [{ alias: node.id === 'model-1' ? 'x' : 'varX', source: node.id === 'model-1' ? { kind: 'runInput', pointer: '/x' } : { kind: 'nodeJson', nodeId: 'model-1', pointer: '/varX' } }] : [] },
  } : node.type === 'end' ? { ...node, typeVersion: 2, configuration: { ...node.configuration, resultBinding: two ? { kind: 'nodeJson', nodeId: 'model-2', pointer: '' } : { kind: 'nodeText', nodeId: 'model-1' } } } : node)
  const response = await request.post(`${apiBase}/workflows`, { data: { document } })
  expect(response.status()).toBe(201)
  return await response.json() as WorkflowDocument
}
async function confirmRun(page: Page, input = '{}') {
  await expect(page.getByRole('button', { name: 'Run workflow', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: 'Run workflow', exact: true }).click()
  await page.getByLabel('Run input JSON object', { exact: true }).fill(input)
  await page.getByLabel('Confirm provider usage and local retention', { exact: true }).check()
  await page.getByRole('button', { name: 'Confirm and run', exact: true }).click()
}
async function pair(page: Page, directory: string) {
  const token = (await readFile(path.join(directory, 'runtime/pairing-token.txt'), 'utf8')).trim()
  await expect(page.getByLabel('Pairing token', { exact: true })).toBeVisible()
  await page.getByLabel('Pairing token', { exact: true }).evaluate((input, value) => {
    const field = input as HTMLInputElement; field.value = value; field.dispatchEvent(new Event('input', { bubbles: true }))
  }, token)
  await page.getByRole('button', { name: 'Pair local browser', exact: true }).click()
  await expect(page.getByLabel('Pairing token', { exact: true })).toHaveCount(0)
}

test('explicit two-node configuration, real SignalR reconnect, immutable history and restart preserve exactly two calls', async ({ page, provider, request, backend }) => {
  const document = await seed(request, provider, 'M3 explicit two-node browser', true, true)
  const sockets: string[] = []
  page.on('websocket', socket => { sockets.push(socket.url()) })
  await page.goto(`/workflows/${document.workflow.id}`)
  await expect(page.getByRole('button', { name: 'Run workflow', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: 'Fit view', exact: true }).click()
  for (const [id, alias, kind, pointer] of [['model-1', 'x', 'runInput', '/x'], ['model-2', 'varX', 'nodeJson', '/varX']]) {
    await page.locator(`[data-node-id="${id}"]`).click()
    await page.getByRole('button', { name: 'Upgrade for execution', exact: true }).click()
    await page.getByLabel('Prompt mode', { exact: true }).selectOption('bindings')
    await page.getByRole('button', { name: 'Add input binding', exact: true }).click()
    await page.getByLabel('Binding 1 alias', { exact: true }).fill(alias!)
    await page.getByLabel('Binding 1 source', { exact: true }).selectOption(kind!)
    if (kind === 'nodeJson') await page.getByLabel('Binding 1 node', { exact: true }).selectOption('model-1')
    await page.getByLabel('Binding 1 JSON pointer', { exact: true }).fill(pointer!)
    await page.getByLabel('Output interpretation', { exact: true }).selectOption('jsonObject')
  }
  await page.locator('[data-node-id="end-1"]').click()
  await page.getByRole('button', { name: 'Upgrade for execution', exact: true }).click()
  await page.getByLabel('Final result source', { exact: true }).selectOption('nodeJson')
  await page.getByLabel('Final result node', { exact: true }).selectOption('model-2')
  await expect(page.getByRole('button', { name: 'Run workflow', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByTestId('save-status')).toContainText('Saved · revision 2')
  expect(provider.calls).toHaveLength(0)
  provider.hold = true
  await confirmRun(page, '{"x":7,"unselected":"DO_NOT_FORWARD","large":9007199254740993}')
  await expect(page).toHaveURL(/\/runs\/[0-9a-f-]+$/)
  const runUrl = page.url()
  await expect.poll(() => provider.calls.length).toBe(1)
  await expect(page.getByTestId('run-state')).toHaveText('Running')
  await expect(page.getByTestId('run-connection-status')).toContainText('Live')
  expect(sockets.some(url => url.includes('/hubs/runs'))).toBe(true)
  expect(sockets.every(url => !url.includes('access_token') && !url.includes(key))).toBe(true)
  await page.reload()
  await expect(page.getByTestId('run-state')).toHaveText('Running')
  expect(provider.calls).toHaveLength(1)
  const connectionsBeforeOutage = sockets.filter(url => url.includes('/hubs/runs')).length
  await page.context().setOffline(true)
  await page.getByRole('button', { name: 'Refresh saved state', exact: true }).click()
  await expect(page.getByTestId('run-connection-status')).toContainText(/stale|Reconnecting|unavailable/i)
  // Drop the actual proxy transport using only this fixture's owned Vite handle.
  // The backend and its provider request continue independently.
  await backend.restartFrontend()
  provider.release()
  await expect.poll(() => provider.calls.length).toBe(2)
  await page.context().setOffline(false)
  await expect(page.getByTestId('run-state')).toHaveText('Succeeded', { timeout: 20000 })
  await expect(page.getByTestId('run-connection-status')).toContainText('Live', { timeout: 20000 })
  expect(sockets.filter(url => url.includes('/hubs/runs')).length).toBeGreaterThan(connectionsBeforeOutage)
  await expect(page.getByTestId('run-result')).toContainText('140')
  await page.getByRole('region', { name: 'Run nodes', exact: true }).getByRole('button', { name: /Multiply by ten/ }).click()
  await expect(page.getByTestId('resolved-prompt')).toHaveText('Multiply 14 by ten.')
  await expect(page.getByTestId('resolved-inputs')).toContainText('14')
  expect(provider.calls.map(call => call.body)).toEqual([
    { model: 'M3 explicit two-node browser-first', input: 'Take 7 and double it.', stream: false, store: false, max_output_tokens: 128 },
    { model: 'M3 explicit two-node browser-second', input: 'Multiply 14 by ten.', stream: false, store: false, max_output_tokens: 128 },
  ])
  expect(provider.calls.every(call => call.authorization === `Bearer ${key}`)).toBe(true)
  const frozen = await (await request.get(`${apiBase}${new URL(runUrl).pathname}`)).text()
  expect(frozen).toContain('9007199254740993'); expect(frozen).not.toContain(key)
  await page.screenshot({ path: path.join(artifacts, 'run-two-node.png'), fullPage: true })
  await page.getByRole('link', { name: 'Open current workflow', exact: true }).click()
  await page.locator('[data-node-id="model-2"]').click()
  await page.getByLabel('Prompt', { exact: true }).fill('A later edited prompt')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByTestId('save-status')).toContainText('Saved · revision 3')
  await page.goto(runUrl)
  await page.getByRole('region', { name: 'Run nodes', exact: true }).getByRole('button', { name: /Multiply by ten/ }).click()
  await expect(page.getByTestId('resolved-prompt')).toHaveText('Multiply 14 by ten.')
  const replacement = await backend.restart()
  expect(replacement.previousPid).not.toBe(replacement.nextPid)
  await page.reload(); await pair(page, backend.dataDirectory)
  await expect(page.getByTestId('run-state')).toHaveText('Succeeded')
  await expect(page.getByTestId('run-result')).toContainText('140')
  expect(provider.calls).toHaveLength(2)
  await writeFile(path.join(artifacts, 'm3-browser-requests.json'), JSON.stringify({ actualRequestCount: provider.calls.length, requests: provider.calls.map(call => call.body),
    hubConnections: sockets.filter(url => url.includes('/hubs/runs')).map(url => { const parsed = new URL(url); return parsed.origin + parsed.pathname }), connectionsBeforeOutage, replacement }, null, 2))
})

test('literal text is unmodified, HTML stays text, and a lost POST response only reconciles its submission', async ({ page, provider, request }) => {
  provider.outputs = ['<img src=x onerror="window.m3Injected=true"> literal output']
  const document = await seed(request, provider, 'M3 ambiguous submission')
  await page.goto(`/workflows/${document.workflow.id}`)
  await page.route(`**/api/workflows/${document.workflow.id}/runs`, async route => { await route.fetch(); await route.abort('failed') })
  await confirmRun(page)
  await expect(page.getByRole('button', { name: 'Look up submission', exact: true })).toBeEnabled()
  expect(page.url()).toContain('submission=')
  await page.reload()
  await page.getByRole('button', { name: 'Look up submission', exact: true }).click()
  await expect(page.getByTestId('run-state')).toHaveText('Succeeded')
  await expect(page.getByTestId('run-result')).toContainText('<img')
  await expect(page.getByTestId('run-result').locator('img')).toHaveCount(0)
  expect(await page.evaluate(() => 'm3Injected' in window)).toBe(false)
  expect(provider.calls).toHaveLength(1)
  expect(provider.calls[0]!.body.input).toBe('Literal text with {{inputs.untouched}}')
  await page.screenshot({ path: path.join(artifacts, 'run-literal-untrusted.png'), fullPage: true })
})

test('invalid JSON fails honestly and skips the downstream model in the Runs view', async ({ page, provider, request }) => {
  provider.outputs = ['```json\n{"varX":14}\n```']
  const document = await seed(request, provider, 'M3 invalid output browser', true)
  await page.goto(`/workflows/${document.workflow.id}`); await confirmRun(page, '{"x":7}')
  await expect(page.getByTestId('run-state')).toHaveText('Failed')
  await expect(page.getByRole('region', { name: 'Run nodes', exact: true })).toContainText('Skipped')
  expect(provider.calls).toHaveLength(1)
  await page.screenshot({ path: path.join(artifacts, 'run-failed.png'), fullPage: true })
})

test('cancelling an observed delayed call never starts its downstream node or resurrects success', async ({ page, provider, request }) => {
  provider.hold = true
  const document = await seed(request, provider, 'M3 cancellation browser', true)
  await page.goto(`/workflows/${document.workflow.id}`); await confirmRun(page, '{"x":7}')
  await expect.poll(() => provider.calls.length).toBe(1)
  await page.getByRole('button', { name: 'Cancel run', exact: true }).click()
  await expect(page.getByTestId('run-state')).toHaveText('Cancelled')
  provider.release()
  await page.reload()
  await expect(page.getByTestId('run-state')).toHaveText('Cancelled')
  expect(provider.calls).toHaveLength(1)
  await expect(page.getByRole('region', { name: 'Run nodes', exact: true })).toContainText('Skipped')
  await page.screenshot({ path: path.join(artifacts, 'run-cancelled.png'), fullPage: true })
})

test('an echoed synthetic credential fails with no output in API artifacts database logs or browser', async ({ page, provider, request, backend }) => {
  provider.outputs = [key]
  const document = await seed(request, provider, 'M3 sensitive output browser')
  await page.goto(`/workflows/${document.workflow.id}`); await confirmRun(page)
  await expect(page.getByTestId('run-state')).toHaveText('Failed')
  await expect(page.getByRole('main')).toContainText('sensitive_output')
  expect(provider.calls).toHaveLength(1)
  expect(await page.locator('body').innerText()).not.toContain(key)
  const url = `${apiBase}${new URL(page.url()).pathname}`
  const raw = await (await request.get(url)).text()
  expect(raw).not.toContain(key)
  const run = JSON.parse(raw) as { nodes: { artifactIds: string[] }[] }
  for (const node of run.nodes) for (const id of node.artifactIds) expect(await (await request.get(`${url}/artifacts/${id}`)).text()).not.toContain(key)
  expect(await (await request.get(`${apiBase}/workflows/${document.workflow.id}`)).text()).not.toContain(key)
  for (const suffix of ['', '-wal']) {
    const content = await readFile(path.join(backend.dataDirectory, `workflows.db${suffix}`)).catch(() => Buffer.alloc(0))
    expect(content.includes(Buffer.from(key))).toBe(false)
  }
  expect(await readFile(path.join(backend.dataDirectory, 'backend.log'), 'utf8')).not.toContain(key)
})
