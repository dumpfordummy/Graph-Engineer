export type NodeType = 'start' | 'modelCall' | 'end'
export interface WorkflowMetadata { id: string; name: string; description: string; revision: number; createdAt: string; updatedAt: string }
export type WorkflowNode =
  | { id: string; type: 'start'; typeVersion: 1; name: string; description: string; configuration: { sampleInput: string } }
  | { id: string; type: 'modelCall'; typeVersion: 1; name: string; description: string; configuration: { prompt: string; providerProfileId?: string | null } }
  | { id: string; type: 'end'; typeVersion: 1; name: string; description: string; configuration: { resultReference: string } }
export interface WorkflowEdge { id: string; sourceNodeId: string; sourcePort: 'out'; targetNodeId: string; targetPort: 'in' }
export interface Viewport { x: number; y: number; zoom: number }
export interface WorkflowDocument {
  formatVersion: 1
  workflow: WorkflowMetadata
  definition: { nodes: WorkflowNode[]; edges: WorkflowEdge[] }
  layout: { nodes: { nodeId: string; x: number; y: number }[]; viewport: Viewport }
}
export interface ValidationIssue { code: string; severity: string; message: string; path: string; nodeId?: string; edgeId?: string }
export interface ValidationReport { structurallyValid: boolean; valid: boolean; issues: ValidationIssue[]; scope: string }
export const SCOPE = 'Structure and draft configuration only. Provider configuration and execution readiness are not checked in M1. Execution is unavailable.'
export const MAX_BYTES = 1_048_576
export const nodeLabels: Record<NodeType, string> = { start: 'Start', modelCall: 'Model Call', end: 'End' }
export function copyDocument(document: WorkflowDocument): WorkflowDocument { return JSON.parse(JSON.stringify(document)) as WorkflowDocument }
export function makeNode(type: 'start'): Extract<WorkflowNode, { type: 'start' }>
export function makeNode(type: 'modelCall'): Extract<WorkflowNode, { type: 'modelCall' }>
export function makeNode(type: 'end'): Extract<WorkflowNode, { type: 'end' }>
export function makeNode(type: NodeType): WorkflowNode
export function makeNode(type: NodeType): WorkflowNode {
  const base = { id: crypto.randomUUID(), typeVersion: 1 as const, name: nodeLabels[type], description: '' }
  if (type === 'start') return { ...base, type, configuration: { sampleInput: '' } }
  if (type === 'end') return { ...base, type, configuration: { resultReference: '' } }
  return { ...base, type, configuration: { prompt: '', providerProfileId: null } }
}
export function freshIdentity(document: WorkflowDocument): WorkflowDocument {
  const next = copyDocument(document)
  const now = new Date().toISOString()
  next.workflow = { ...next.workflow, id: crypto.randomUUID(), revision: 0, createdAt: now, updatedAt: now }
  return next
}
export function sampleDocument(): WorkflowDocument {
  const start = makeNode('start'), model = makeNode('modelCall'), end = makeNode('end')
  start.description = 'Provide sample text for this draft.'
  start.configuration.sampleInput = 'A small example to design your workflow.'
  model.description = 'Describe the transformation you need.'
  model.configuration.prompt = 'Summarize the input in a short paragraph.'
  end.description = 'Describe the intended result.'
  end.configuration.resultReference = 'Previous model output (descriptive only)'
  const now = new Date().toISOString()
  return {
    formatVersion: 1,
    workflow: { id: crypto.randomUUID(), name: 'Untitled workflow', description: '', revision: 0, createdAt: now, updatedAt: now },
    definition: { nodes: [start, model, end], edges: [
      { id: crypto.randomUUID(), sourceNodeId: start.id, sourcePort: 'out', targetNodeId: model.id, targetPort: 'in' },
      { id: crypto.randomUUID(), sourceNodeId: model.id, sourcePort: 'out', targetNodeId: end.id, targetPort: 'in' },
    ] },
    layout: { nodes: [start, model, end].map((node, index) => ({ nodeId: node.id, x: 60 + index * 300, y: 140 })), viewport: { x: 0, y: 0, zoom: 0.8 } },
  }
}

// JSON.parse otherwise silently keeps the last duplicate property. Walk JSON tokens
// after parsing syntax to reject ambiguous documents before any state is replaced.
function rejectDuplicateProperties(text: string) {
  const tokens = text.match(/"(?:\\.|[^"\\])*"|[{}[\]:,]|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null/g) ?? []
  let index = 0
  function value() {
    const token = tokens[index++]
    if (token === '{') {
      const keys = new Set<string>()
      while (tokens[index] !== '}') {
        const key = JSON.parse(tokens[index++]!) as string
        if (keys.has(key)) throw new Error(`Duplicate JSON property: ${key}`)
        keys.add(key)
        index++
        value()
        if (tokens[index] !== ',') break
        index++
      }
      index++
    } else if (token === '[') {
      while (tokens[index] !== ']') {
        value()
        if (tokens[index] !== ',') break
        index++
      }
      index++
    }
  }
  value()
}

export function parseDocument(text: string): WorkflowDocument {
  if (new TextEncoder().encode(text).byteLength > MAX_BYTES) throw new Error('Import exceeds the 1 MiB document limit.')
  const raw: unknown = JSON.parse(text)
  rejectDuplicateProperties(text)
  assertDocument(raw)
  return raw
}

export function assertDocument(raw: unknown): asserts raw is WorkflowDocument {
  const fail = (path: string, message: string): never => { throw new Error(`${path}: ${message}`) }
  function object(value: unknown, path: string, keys: string[], optional: string[] = []): Record<string, unknown> {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return fail(path, 'Expected an object.')
    const record = value as Record<string, unknown>
    for (const key of keys) if (!Object.hasOwn(record, key)) fail(`${path}.${key}`, 'Required property is missing.')
    for (const key of Object.keys(record)) if (!keys.includes(key) && !optional.includes(key)) fail(`${path}.${key}`, 'Unknown property is not allowed.')
    return record
  }
  function string(value: unknown, path: string, max: number, nonempty = false): asserts value is string {
    if (typeof value !== 'string' || value.length > max || (nonempty && value.trim().length === 0)) fail(path, `Expected ${nonempty ? 'a nonempty' : 'a'} string up to ${max} characters.`)
  }
  function number(value: unknown, path: string, min = -100000, max = 100000) {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max) fail(path, `Expected a finite number from ${min} to ${max}.`)
  }
  function array(value: unknown, path: string, max: number): unknown[] {
    if (!Array.isArray(value) || value.length > max) return fail(path, `Expected an array of at most ${max} entries.`)
    return value
  }
  const doc = object(raw, 'document', ['formatVersion', 'workflow', 'definition', 'layout'])
  if (doc.formatVersion !== 1) fail('formatVersion', 'Only document format version 1 is supported.')
  const meta = object(doc.workflow, 'workflow', ['id', 'name', 'description', 'revision', 'createdAt', 'updatedAt'])
  string(meta.id, 'workflow.id', 36, true)
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(meta.id)) fail('workflow.id', 'Expected a UUID.')
  string(meta.name, 'workflow.name', 120); string(meta.description, 'workflow.description', 2000)
  if (!Number.isSafeInteger(meta.revision) || (meta.revision as number) < 0) fail('workflow.revision', 'Expected a nonnegative safe integer.')
  for (const key of ['createdAt', 'updatedAt']) {
    string(meta[key], `workflow.${key}`, 40)
    if (!/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|\+00:00)$/.test(meta[key]) || !Number.isFinite(Date.parse(meta[key]))) fail(`workflow.${key}`, 'Expected an ISO-8601 UTC timestamp.')
  }
  const def = object(doc.definition, 'definition', ['nodes', 'edges'])
  const nodes = new Map<string, string>()
  array(def.nodes, 'definition.nodes', 200).forEach((entry, i) => {
    const path = `definition.nodes[${i}]`
    const node = object(entry, path, ['id', 'type', 'typeVersion', 'name', 'description', 'configuration'])
    string(node.id, `${path}.id`, 100, true)
    if (nodes.has(node.id)) fail(`${path}.id`, 'Duplicate node ID.')
    if (node.type !== 'start' && node.type !== 'modelCall' && node.type !== 'end') fail(`${path}.type`, 'Unsupported node type.')
    if (node.typeVersion !== 1) fail(`${path}.typeVersion`, 'Only node type version 1 is supported.')
    nodes.set(node.id, node.type as string)
    string(node.name, `${path}.name`, 120); string(node.description, `${path}.description`, 2000)
    const field = node.type === 'start' ? 'sampleInput' : node.type === 'modelCall' ? 'prompt' : 'resultReference'
    const config = object(node.configuration, `${path}.configuration`, [field], node.type === 'modelCall' ? ['providerProfileId'] : [])
    string(config[field], `${path}.configuration.${field}`, field === 'resultReference' ? 2000 : 50000)
    if (Object.hasOwn(config, 'providerProfileId') && config.providerProfileId !== null) string(config.providerProfileId, `${path}.configuration.providerProfileId`, 120)
  })
  const edgeIds = new Set<string>(), connections = new Set<string>()
  array(def.edges, 'definition.edges', 400).forEach((entry, i) => {
    const path = `definition.edges[${i}]`
    const edge = object(entry, path, ['id', 'sourceNodeId', 'sourcePort', 'targetNodeId', 'targetPort'])
    string(edge.id, `${path}.id`, 100, true)
    if (edgeIds.has(edge.id)) fail(`${path}.id`, 'Duplicate edge ID.')
    edgeIds.add(edge.id)
    string(edge.sourceNodeId, `${path}.sourceNodeId`, 100, true); string(edge.targetNodeId, `${path}.targetNodeId`, 100, true)
    if (!nodes.has(edge.sourceNodeId) || nodes.get(edge.sourceNodeId) === 'end') fail(`${path}.sourceNodeId`, 'Source must be an existing Start or Model Call node.')
    if (!nodes.has(edge.targetNodeId) || nodes.get(edge.targetNodeId) === 'start') fail(`${path}.targetNodeId`, 'Target must be an existing Model Call or End node.')
    if (edge.sourcePort !== 'out') fail(`${path}.sourcePort`, 'Source port must be out.')
    if (edge.targetPort !== 'in') fail(`${path}.targetPort`, 'Target port must be in.')
    const connection = JSON.stringify([edge.sourceNodeId, edge.targetNodeId])
    if (connections.has(connection)) fail(path, 'Duplicate connection.')
    connections.add(connection)
  })
  const layout = object(doc.layout, 'layout', ['nodes', 'viewport'])
  const positions = new Set<string>()
  array(layout.nodes, 'layout.nodes', 200).forEach((entry, i) => {
    const path = `layout.nodes[${i}]`
    const position = object(entry, path, ['nodeId', 'x', 'y'])
    string(position.nodeId, `${path}.nodeId`, 100, true)
    if (!nodes.has(position.nodeId) || positions.has(position.nodeId)) fail(`${path}.nodeId`, 'Each existing node needs exactly one layout entry.')
    positions.add(position.nodeId)
    number(position.x, `${path}.x`); number(position.y, `${path}.y`)
  })
  if (positions.size !== nodes.size) fail('layout.nodes', 'Every node needs a layout entry.')
  const viewport = object(layout.viewport, 'layout.viewport', ['x', 'y', 'zoom'])
  number(viewport.x, 'layout.viewport.x'); number(viewport.y, 'layout.viewport.y'); number(viewport.zoom, 'layout.viewport.zoom', 0.1, 4)
}
