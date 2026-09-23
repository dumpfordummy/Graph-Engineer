import { test, expect, apiBase, sampleDocument } from './fixture'
import { Buffer } from 'node:buffer'

test('a fresh SQLite database starts, reports health, and lists no user data', async ({ request }) => {
  const health = await request.get(`${apiBase}/health`)
  expect(health.ok()).toBeTruthy()
  expect(await health.json()).toEqual({ status: 'ok' })
  const list = await request.get(`${apiBase}/workflows`)
  expect(list.ok()).toBeTruthy()
  expect(await list.json()).toEqual([])
})

test('semantic errors are actionable and saveable; broken ports are rejected', async ({ request }) => {
  const document = sampleDocument('Incomplete but saveable draft')
  document.definition.nodes = document.definition.nodes.filter(node => node.id === 'model-1')
  document.definition.edges = []
  document.layout.nodes = document.layout.nodes.filter(node => node.nodeId === 'model-1')
  const validation = await request.post(`${apiBase}/workflows/validate`, { data: { document } })
  expect(validation.status()).toBe(200)
  const report = await validation.json()
  expect(report.structurallyValid).toBe(true)
  expect(report.valid).toBe(false)
  expect(JSON.stringify(report.issues)).toMatch(/start/i)
  expect(JSON.stringify(report.issues)).toMatch(/end/i)
  expect(report.issues.every((issue: { path: string; message: string }) => issue.path && issue.message)).toBeTruthy()
  expect(report.scope).toMatch(/execution readiness.*checked separately before a run/i)
  expect((await request.post(`${apiBase}/workflows`, { data: { document } })).status()).toBe(201)

  const broken = sampleDocument('Broken port')
  broken.definition.edges[0]!.sourcePort = 'invalid-port'
  const brokenReport = await request.post(`${apiBase}/workflows/validate`, { data: { document: broken } })
  expect((await brokenReport.json()).structurallyValid).toBe(false)
  const rejected = await request.post(`${apiBase}/workflows`, { data: { document: broken } })
  expect(rejected.status()).toBe(422)
  expect(Object.keys((await rejected.json()).errors).some(path => path.includes('sourcePort'))).toBeTruthy()
})

test('validation diagnoses disconnected nodes, branches, and cycles in the current document', async ({ request }) => {
  const disconnected = sampleDocument()
  disconnected.definition.edges = []
  const disconnectedResponse = await request.post(`${apiBase}/workflows/validate`, { data: { document: disconnected } })
  expect(JSON.stringify((await disconnectedResponse.json()).issues)).toMatch(/disconnect|unreachable/i)

  const branch = sampleDocument()
  branch.definition.edges.push({ id: 'branch-edge', sourceNodeId: 'start-1', sourcePort: 'out', targetNodeId: 'end-1', targetPort: 'in' })
  const branchResponse = await request.post(`${apiBase}/workflows/validate`, { data: { document: branch } })
  expect(JSON.stringify((await branchResponse.json()).issues)).toMatch(/branch/i)

  const cycle = sampleDocument()
  cycle.definition.edges.push({ id: 'cycle-edge', sourceNodeId: 'model-1', sourcePort: 'out', targetNodeId: 'model-1', targetPort: 'in' })
  const cycleResponse = await request.post(`${apiBase}/workflows/validate`, { data: { document: cycle } })
  expect(JSON.stringify((await cycleResponse.json()).issues)).toMatch(/cycle/i)
})

test('simultaneous updates use one atomic revision check and preserve the winner', async ({ request }) => {
  const created = await request.post(`${apiBase}/workflows`, { data: { document: sampleDocument('Concurrency fixture') } })
  const original = await created.json()
  const first = structuredClone(original)
  const second = structuredClone(original)
  first.workflow.name = 'Winning candidate A'
  second.workflow.name = 'Winning candidate B'
  const responses = await Promise.all([first, second].map(document => request.put(`${apiBase}/workflows/${original.workflow.id}`, {
    data: { expectedRevision: original.workflow.revision, document },
  })))
  expect(responses.map(response => response.status()).sort()).toEqual([200, 409])
  const winner = await responses.find(response => response.status() === 200)!.json()
  const stored = await (await request.get(`${apiBase}/workflows/${original.workflow.id}`)).json()
  expect(stored).toEqual(winner)
  expect(stored.workflow.revision).toBe(original.workflow.revision + 1)
})

test('malformed documents, unsupported versions, duplicate IDs and excessive payloads are rejected', async ({ request }) => {
  const malformed = await request.post(`${apiBase}/workflows`, { data: Buffer.from('{"document":'), headers: { 'Content-Type': 'application/json' } })
  expect(malformed.status()).toBe(400)
  for (const alter of [
    (document: ReturnType<typeof sampleDocument>) => { document.formatVersion = 99 },
    (document: ReturnType<typeof sampleDocument>) => { document.definition.nodes[0]!.type = 'unknown' },
    (document: ReturnType<typeof sampleDocument>) => { document.definition.nodes[0]!.typeVersion = 99 },
    (document: ReturnType<typeof sampleDocument>) => { document.definition.nodes[1]!.id = document.definition.nodes[0]!.id },
  ]) {
    const document = sampleDocument()
    alter(document)
    const response = await request.post(`${apiBase}/workflows`, { data: { document } })
    expect(response.status()).toBe(422)
    expect(Object.keys((await response.json()).errors).length).toBeGreaterThan(0)
  }
  const oversized = await request.post(`${apiBase}/workflows`, {
    data: JSON.stringify({ document: sampleDocument(), excess: 'x'.repeat(1_048_576) }),
    headers: { 'Content-Type': 'application/json' },
  })
  expect(oversized.status()).toBe(413)
})
