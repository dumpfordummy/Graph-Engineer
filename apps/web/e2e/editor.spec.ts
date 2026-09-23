import type { APIRequestContext, Page } from '@playwright/test'
import { Buffer } from 'node:buffer'
import path from 'node:path'
import { test, expect, apiBase, artifacts, sampleDocument } from './fixture'

type Document = ReturnType<typeof sampleDocument>

async function exportDocument(page: Page): Promise<Document> {
  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Export JSON', exact: true }).click()
  const download = await downloading
  const stream = await download.createReadStream()
  if (!stream) throw new Error('The JSON export did not provide a readable download')
  const chunks: Buffer[] = []
  for await (const chunk of stream) chunks.push(Buffer.from(chunk))
  return JSON.parse(Buffer.concat(chunks).toString('utf8')) as Document
}

async function seed(request: APIRequestContext, name: string) {
  const response = await request.post(`${apiBase}/workflows`, { data: { document: sampleDocument(name) } })
  expect(response.status()).toBe(201)
  return await response.json() as Document
}

async function save(page: Page) {
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeEnabled()
  const response = page.waitForResponse(result => /\/api\/workflows(?:\/[^/]+)?$/.test(result.url()) && ['POST', 'PUT'].includes(result.request().method()))
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const saved = await response
  expect(saved.ok()).toBeTruthy()
  await expect(page.getByTestId('save-status')).toHaveText(/^Saved · revision \d+$/)
  return await saved.json() as Document
}

async function selectNode(page: Page, id: string) {
  await page.locator(`[data-testid="workflow-node"][data-node-id="${id}"]`).click()
}

async function connect(page: Page, sourceId: string, targetId: string) {
  const source = page.locator(`.vue-flow__node[data-id="${sourceId}"] .vue-flow__handle.source`)
  const target = page.locator(`.vue-flow__node[data-id="${targetId}"] .vue-flow__handle.target`)
  const from = await source.boundingBox()
  const to = await target.boundingBox()
  if (!from || !to) throw new Error('Graph connection handles are not visible')
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2)
  await page.mouse.down()
  await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 15 })
  await page.mouse.up()
}

test('create, configure and connect two models; save, reload and restart the real backend', async ({ page, request, backend }) => {
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()) })
  page.on('requestfailed', request => errors.push(`${request.method()} ${request.url()} ${request.failure()?.errorText}`))
  page.on('response', response => { if (response.url().includes('/api/') && response.status() >= 400) errors.push(`${response.status()} ${response.url()}`) })
  await page.goto('/')
  await page.getByRole('link', { name: /New workflow/ }).click()
  await page.getByLabel('Workflow name', { exact: true }).fill('Two stage engineering draft')
  await page.getByLabel('Workflow description', { exact: true }).fill('Synthetic persistence acceptance: metadata, definition, and layout.')
  const original = await exportDocument(page)
  const firstModel = original.definition.nodes.find(node => node.type === 'modelCall')!
  const start = original.definition.nodes.find(node => node.type === 'start')!
  const end = original.definition.nodes.find(node => node.type === 'end')!
  await selectNode(page, start.id)
  await page.getByLabel('Sample input', { exact: true }).fill('Synthetic invoice 42: verify the item count.')
  await selectNode(page, firstModel.id)
  await page.getByLabel('Node name', { exact: true }).fill('Extract facts')
  await page.getByLabel('Node description', { exact: true }).fill('First persisted model description')
  await page.getByLabel('Prompt', { exact: true }).fill('Extract a short list of facts from the sample input.')
  await expect(page.getByLabel('Provider profile', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Add Model Call', exact: true }).click()
  const added = await exportDocument(page)
  const secondModel = added.definition.nodes.find(node => !original.definition.nodes.some(previous => previous.id === node.id))!
  await selectNode(page, secondModel.id)
  await page.getByLabel('Node name', { exact: true }).fill('Review facts')
  await page.getByLabel('Prompt', { exact: true }).fill('Review the facts for internal consistency. This is only draft text.')

  const modelElement = page.locator(`[data-testid="workflow-node"][data-node-id="${secondModel.id}"]`)
  const beforeDrag = await modelElement.boundingBox()
  if (!beforeDrag) throw new Error('Added model is not visible on the canvas')
  await page.mouse.move(beforeDrag.x + 65, beforeDrag.y + 30)
  await page.mouse.down()
  await page.mouse.move(beforeDrag.x + 65, beforeDrag.y + 130, { steps: 15 })
  await page.mouse.up()
  const afterDrag = await exportDocument(page)
  expect(afterDrag.layout.nodes.find(node => node.nodeId === secondModel.id)).not.toEqual(added.layout.nodes.find(node => node.nodeId === secondModel.id))

  await selectNode(page, firstModel.id)
  await page.getByRole('button', { name: 'Extract facts → End', exact: true }).click()
  await page.getByLabel('Target node', { exact: true }).selectOption(secondModel.id)
  await page.getByRole('button', { name: 'Reconnect edge', exact: true }).click()
  await page.locator('.vue-flow__controls-fitview').click()
  await connect(page, secondModel.id, end.id)
  await selectNode(page, end.id)
  await page.getByLabel('Result reference', { exact: true }).fill('Review facts output (descriptive only)')
  const draft = await exportDocument(page)
  expect(draft.definition.nodes).toHaveLength(4)
  expect(draft.definition.edges).toHaveLength(3)
  expect(draft.definition.edges).toEqual(expect.arrayContaining([
    expect.objectContaining({ sourceNodeId: start.id, targetNodeId: firstModel.id }),
    expect.objectContaining({ sourceNodeId: firstModel.id, targetNodeId: secondModel.id }),
    expect.objectContaining({ sourceNodeId: secondModel.id, targetNodeId: end.id }),
  ]))
  await page.getByRole('button', { name: 'Validate', exact: true }).click()
  await expect(page.getByTestId('validation-panel').getByText('Structure valid', { exact: true })).toBeVisible()
  await expect(page.getByTestId('validation-panel')).toContainText('No structural or draft configuration issues found.')
  await expect(page.getByRole('button', { name: /Run/ })).toBeDisabled()
  let persisted = await save(page)
  expect(persisted.definition).toEqual(draft.definition)
  expect(persisted.layout).toEqual(draft.layout)
  await page.reload()
  await expect(page.getByLabel('Workflow name', { exact: true })).toHaveValue('Two stage engineering draft')
  const reloaded = await exportDocument(page)
  expect(reloaded).toEqual(persisted)
  await selectNode(page, firstModel.id)
  await page.getByRole('button', { name: 'Validate', exact: true }).click()
  await expect(page.getByTestId('validation-panel').getByText('Structure valid', { exact: true })).toBeVisible()
  await page.screenshot({ path: path.join(artifacts, 'editor-1440.png'), fullPage: true })
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.getByRole('button', { name: 'Zoom out', exact: true }).click()
  await expect(page.getByTestId('save-status')).toHaveText('Unsaved changes')
  await page.getByRole('button', { name: 'Fit view', exact: true }).click()
  await expect.poll(async () => {
    const canvasBounds = await page.getByRole('region', { name: 'Workflow graph', exact: true }).boundingBox()
    if (!canvasBounds) return false
    const cards = await page.getByTestId('workflow-node').all()
    if (cards.length !== 4) return false
    for (const card of cards) {
      const box = await card.boundingBox()
      if (!box || box.x < canvasBounds.x || box.y < canvasBounds.y || box.x + box.width > canvasBounds.x + canvasBounds.width || box.y + box.height > canvasBounds.y + canvasBounds.height) return false
    }
    return true
  }, { message: 'Every graph card should fit inside the resized canvas after Fit view' }).toBe(true)
  persisted = await save(page)
  expect(persisted.definition).toEqual(draft.definition)
  expect(persisted.layout.nodes).toEqual(draft.layout.nodes)
  await page.getByRole('button', { name: 'Validate', exact: true }).click()
  await expect(page.getByTestId('validation-panel').getByText('Structure valid', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeVisible()
  await expect(page.getByTestId('workflow-node')).toHaveCount(4)
  await page.screenshot({ path: path.join(artifacts, 'editor-1280.png'), fullPage: true })

  const restart = await backend.restart()
  expect(restart.nextPid).not.toBe(restart.previousPid)
  await backend.pairRequest(request)
  await page.context().addCookies((await backend.session()).state.cookies)
  const retrieved = await request.get(`${apiBase}/workflows/${persisted.workflow.id}`)
  expect(await retrieved.json()).toEqual(persisted)
  await page.reload()
  await expect(page.getByLabel('Workflow name', { exact: true })).toHaveValue(persisted.workflow.name)
  expect(await exportDocument(page)).toEqual(persisted)
  expect(errors).toEqual([])
})

test('editing text cannot delete graph nodes; node deletion removes incident edges and validates unsaved draft', async ({ page, request }) => {
  const document = await seed(request, 'Deletion and validation fixture')
  await page.goto(`/workflows/${document.workflow.id}`)
  await selectNode(page, 'model-1')
  const prompt = page.getByLabel('Prompt', { exact: true })
  await prompt.fill('Keep this model while deleting characters')
  await prompt.press('End')
  await prompt.press('Backspace')
  await prompt.press('Home')
  await prompt.press('Delete')
  await expect(page.getByTestId('workflow-node')).toHaveCount(3)
  await page.getByRole('button', { name: 'Delete node', exact: true }).click()
  const edited = await exportDocument(page)
  expect(edited.definition.nodes.map(node => node.id)).toEqual(['start-1', 'end-1'])
  expect(edited.definition.edges).toEqual([])
  const stored = await (await request.get(`${apiBase}/workflows/${document.workflow.id}`)).json() as Document
  expect(stored.definition.nodes).toHaveLength(3)
  await page.getByRole('button', { name: 'Validate', exact: true }).click()
  await expect(page.getByTestId('validation-panel')).toContainText('This node is not reachable from Start.')
  await page.screenshot({ path: path.join(artifacts, 'validation-error.png'), fullPage: true })
  const saved = await save(page)
  expect(saved.definition).toEqual(edited.definition)
})

test('a simulated save network failure preserves edits and a later real save recovers', async ({ page, request }) => {
  const document = await seed(request, 'Network failure fixture')
  await page.goto(`/workflows/${document.workflow.id}`)
  await page.getByLabel('Workflow name', { exact: true }).fill('Unsaved work must survive a failed request')
  const endpoint = `**/api/workflows/${document.workflow.id}`
  await page.route(endpoint, route => route.request().method() === 'PUT' ? route.abort('failed') : route.continue())
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Cannot reach the local API')
  await expect(page.getByTestId('save-status')).toContainText(/Save needed|unsaved/i)
  await expect(page.getByLabel('Workflow name', { exact: true })).toHaveValue('Unsaved work must survive a failed request')
  expect((await exportDocument(page)).workflow.revision).toBe(document.workflow.revision)
  const unchanged = await (await request.get(`${apiBase}/workflows/${document.workflow.id}`)).json() as Document
  expect(unchanged.workflow.name).toBe(document.workflow.name)
  await page.unroute(endpoint)
  await save(page)
  await page.reload()
  await expect(page.getByLabel('Workflow name', { exact: true })).toHaveValue('Unsaved work must survive a failed request')
})

test('two browser clients surface a real revision conflict without losing the stale client edits', async ({ page, context, request }) => {
  const document = await seed(request, 'Two browser clients fixture')
  const stalePage = await context.newPage()
  await page.goto(`/workflows/${document.workflow.id}`)
  await stalePage.goto(`/workflows/${document.workflow.id}`)
  await expect(stalePage.getByLabel('Workflow name', { exact: true })).toHaveValue(document.workflow.name)
  await page.getByLabel('Workflow name', { exact: true }).fill('Saved in first browser')
  await save(page)
  await stalePage.getByLabel('Workflow name', { exact: true }).fill('Unsaved edit from stale browser')
  const conflict = stalePage.waitForResponse(response => response.url().endsWith(`/api/workflows/${document.workflow.id}`) && response.request().method() === 'PUT')
  await stalePage.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await conflict).status()).toBe(409)
  await expect(stalePage.getByLabel('Workflow name', { exact: true })).toHaveValue('Unsaved edit from stale browser')
  await expect(stalePage.getByRole('alert')).toContainText(/conflict/i)
  await expect(stalePage.getByTestId('save-status')).toContainText(/Save needed|unsaved/i)
  const stored = await (await request.get(`${apiBase}/workflows/${document.workflow.id}`)).json() as Document
  expect(stored.workflow.name).toBe('Saved in first browser')
  await stalePage.close()
})

test('edits made while a real save response is delayed remain unsaved and are preserved', async ({ page, request }) => {
  const document = await seed(request, 'Save timing fixture')
  await page.goto(`/workflows/${document.workflow.id}`)
  await page.getByLabel('Workflow name', { exact: true }).fill('Snapshot sent to the server')
  let releaseResponse!: () => void
  let announceStored!: () => void
  const responseGate = new Promise<void>(resolve => { releaseResponse = resolve })
  const stored = new Promise<void>(resolve => { announceStored = resolve })
  const endpoint = `**/api/workflows/${document.workflow.id}`
  await page.route(endpoint, async route => {
    if (route.request().method() !== 'PUT') return route.continue()
    const response = await route.fetch()
    announceStored()
    await responseGate
    await route.fulfill({ response })
  })
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await stored
  await page.getByLabel('Workflow name', { exact: true }).fill('Newer edit made before save response arrived')
  releaseResponse()
  await expect(page.getByTestId('save-status')).toContainText(/unsaved/i)
  const current = await exportDocument(page)
  expect(current.workflow.name).toBe('Newer edit made before save response arrived')
  expect(current.workflow.revision).toBe(document.workflow.revision + 1)
  const serverDocument = await (await request.get(`${apiBase}/workflows/${document.workflow.id}`)).json() as Document
  expect(serverDocument.workflow.name).toBe('Snapshot sent to the server')
  await page.unroute(endpoint)
  await save(page)
})

test('cancelled navigation and cancelled valid import both preserve unsaved work', async ({ page, request }) => {
  const document = await seed(request, 'Unsaved navigation protection')
  await page.goto(`/workflows/${document.workflow.id}`)
  await page.getByLabel('Workflow name', { exact: true }).fill('Keep my unsaved draft')
  const before = await exportDocument(page)
  const navigationDialog = page.waitForEvent('dialog')
  const navigate = page.getByRole('link', { name: 'Back to workflows', exact: true }).click()
  const confirmation = await navigationDialog
  expect(confirmation.type()).toBe('confirm')
  expect(confirmation.message()).toContain('Discard unsaved changes')
  await confirmation.dismiss()
  await navigate
  await expect(page).toHaveURL(new RegExp(`/workflows/${document.workflow.id}$`))
  expect(await exportDocument(page)).toEqual(before)

  const importDialog = page.waitForEvent('dialog')
  const upload = page.getByTestId('import-file').setInputFiles({ name: 'different-workflow.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(sampleDocument('Replacement must be cancelled'))) })
  const replaceConfirmation = await importDialog
  expect(replaceConfirmation.type()).toBe('confirm')
  await replaceConfirmation.dismiss()
  await upload
  expect(await exportDocument(page)).toEqual(before)
  expect(await (await request.get(`${apiBase}/workflows/${document.workflow.id}`)).json()).toEqual(document)
  await save(page)
})

test('export and import preserve supported content with a fresh identity; rejected imports preserve the draft', async ({ page, request }) => {
  const original = await seed(request, 'Round trip fixture')
  await page.goto(`/workflows/${original.workflow.id}`)
  const exported = await exportDocument(page)
  expect(Object.keys(exported).sort()).toEqual(['definition', 'formatVersion', 'layout', 'workflow'])
  expect(JSON.stringify(exported)).not.toMatch(/"(?:selected|dragging|computedPosition|runtime|credentials|apiKey)"/)
  for (const invalidText of ['{"formatVersion":', JSON.stringify({ ...exported, formatVersion: 99 }), JSON.stringify({ ...exported, definition: { ...exported.definition, nodes: exported.definition.nodes.map((node, index) => index === 0 ? { ...node, type: 'future-node' } : node) } })]) {
    await page.getByTestId('import-file').setInputFiles({ name: 'invalid.json', mimeType: 'application/json', buffer: Buffer.from(invalidText) })
    await expect(page.getByRole('alert')).toBeVisible()
    expect(await exportDocument(page)).toEqual(exported)
  }
  await page.getByTestId('import-file').setInputFiles({ name: 'round-trip.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(exported)) })
  await expect(page.getByTestId('save-status')).toContainText(/unsaved/i)
  const imported = await exportDocument(page)
  expect(imported.workflow.id).not.toBe(original.workflow.id)
  expect(imported.workflow.revision).toBe(0)
  expect(imported.definition).toEqual(original.definition)
  expect(imported.layout).toEqual(original.layout)
  const savedImport = await save(page)
  expect(savedImport.workflow.id).not.toBe(original.workflow.id)
  expect(await (await request.get(`${apiBase}/workflows/${original.workflow.id}`)).json()).toEqual(original)
  expect(savedImport.definition).toEqual(original.definition)
  expect(savedImport.layout).toEqual(original.layout)
})
