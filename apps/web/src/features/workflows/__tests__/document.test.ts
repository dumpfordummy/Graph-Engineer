import { describe, expect, it } from 'vitest'
import { assertDocument, freshIdentity, parseDocument, sampleDocument } from '../document'
import type { WorkflowDocument } from '../document'

describe('versioned workflow serialization', () => {
  it('round-trips only the document contract and preserves configuration, IDs and layout', () => {
    const document = sampleDocument()
    document.definition.nodes[1]!.description = '<script>alert("text only")</script>'
    document.layout.nodes[1]!.x = -340
    document.layout.viewport = { x: 10, y: -50, zoom: 1.3 }
    expect(parseDocument(JSON.stringify(document))).toEqual(document)
    expect(JSON.stringify(document)).not.toMatch(/selected|__vf|position|runtime|status/)
  })
  it('creates an independent import identity without changing graph content', () => {
    const original = sampleDocument()
    original.workflow.revision = 18
    const imported = freshIdentity(original)
    expect(imported.workflow.id).not.toBe(original.workflow.id)
    expect(imported.workflow.revision).toBe(0)
    expect(imported.definition).toEqual(original.definition)
    expect(imported.layout).toEqual(original.layout)
    expect(original.workflow.revision).toBe(18)
  })
  it.each<[string, (document: WorkflowDocument) => void, string]>([
    ['unsupported format', doc => { Object.assign(doc, { formatVersion: 2 }) }, 'formatVersion'],
    ['unsupported type', doc => { Object.assign(doc.definition.nodes[0]!, { type: 'shell' }) }, '.type'],
    ['unsupported type version', doc => { Object.assign(doc.definition.nodes[0]!, { typeVersion: 2 }) }, '.typeVersion'],
    ['missing metadata', doc => { delete (doc.workflow as Partial<typeof doc.workflow>).createdAt }, '.createdAt'],
    ['unknown runtime state', doc => { Object.assign(doc, { runtime: {} }) }, 'runtime'],
    ['credential property', doc => { Object.assign(doc.definition.nodes[1]!.configuration, { apiKey: 'synthetic-only' }) }, 'apiKey'],
    ['duplicate node ID', doc => { doc.definition.nodes[1]!.id = doc.definition.nodes[0]!.id }, 'Duplicate node ID'],
    ['duplicate edge ID', doc => { doc.definition.edges[1]!.id = doc.definition.edges[0]!.id }, 'Duplicate edge ID'],
    ['missing source', doc => { doc.definition.edges[0]!.sourceNodeId = 'missing' }, 'sourceNodeId'],
    ['illegal source direction', doc => { doc.definition.edges[0]!.sourceNodeId = doc.definition.nodes[2]!.id }, 'sourceNodeId'],
    ['unknown port', doc => { Object.assign(doc.definition.edges[0]!, { targetPort: 'wrong' }) }, 'targetPort'],
    ['duplicate connection', doc => { doc.definition.edges.push({ ...doc.definition.edges[0]!, id: 'another-edge' }) }, 'Duplicate connection'],
    ['missing layout entry', doc => { doc.layout.nodes.pop() }, 'layout.nodes'],
    ['duplicate layout entry', doc => { doc.layout.nodes[1]!.nodeId = doc.layout.nodes[0]!.nodeId }, 'layout.nodes'],
    ['invalid viewport', doc => { doc.layout.viewport.zoom = 0 }, 'layout.viewport.zoom'],
    ['invalid revision', doc => { doc.workflow.revision = -1 }, 'workflow.revision'],
    ['oversized prompt', doc => { Object.assign(doc.definition.nodes[1]!.configuration, { prompt: 'x'.repeat(50001) }) }, 'prompt'],
  ])('rejects %s with a field-addressable reason', (_label, mutate, expected) => {
    const document = sampleDocument()
    mutate(document)
    expect(() => parseDocument(JSON.stringify(document))).toThrow(expected)
  })
  it('allows incomplete semantic drafts without pretending they are executable', () => {
    const document = sampleDocument()
    document.definition.nodes = []; document.definition.edges = []; document.layout.nodes = []
    document.workflow.name = ''
    expect(() => assertDocument(document)).not.toThrow()
  })
  it('rejects duplicate escaped JSON properties at any nesting depth', () => {
    const text = JSON.stringify(sampleDocument()).replace('"formatVersion":1', '"formatVersion":1,"format\\u0056ersion":1')
    expect(() => parseDocument(text)).toThrow('Duplicate JSON property')
    const nested = JSON.stringify(sampleDocument()).replace('"sampleInput":', '"sampleInput":"first","sampleInput":')
    expect(() => parseDocument(nested)).toThrow('Duplicate JSON property')
  })
  it('accepts escaped braces and quotes inside prompts as text', () => {
    const document = sampleDocument()
    Object.assign(document.definition.nodes[1]!.configuration, { prompt: 'Use {"name":"x", "value": [1,2]} with \\ escapes.' })
    expect(parseDocument(JSON.stringify(document))).toEqual(document)
  })
  it('rejects malformed JSON, nonfinite positions, and excessive UTF-8 size', () => {
    expect(() => parseDocument('{bad json')).toThrow()
    const document = sampleDocument()
    document.layout.nodes[0]!.x = Infinity
    expect(() => assertDocument(document)).toThrow('layout.nodes[0].x')
    expect(() => parseDocument('é'.repeat(530000))).toThrow('1 MiB')
  })
})
