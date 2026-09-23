import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { parseDocument, sampleDocument } from '../../workflows/document'
import { earlierModelNodes, pointerError } from '../../workflows/bindings'
import { useWorkflowStore } from '../../workflows/store'
import ExecutionConfig from '../../workflows/components/ExecutionConfig.vue'
import BindingSourceEditor from '../../workflows/components/BindingSourceEditor.vue'
import { submissionBody, validateRunInput } from '../api'
import { decodeRun, displayJson } from '../exactJson'
beforeEach(() => setActivePinia(createPinia()))

describe('explicit execution configuration', () => {
  it('loads legacy literal braces unchanged and upgrades only by explicit action', async () => {
    const store = useWorkflowStore(); store.createNew()
    const id = store.document!.definition.nodes[1]!.id
    store.updateNode(id, node => { if (node.type === 'modelCall') node.configuration.prompt = 'JSON {"x":1} and {{inputs.value}} stay literal' })
    const legacy = parseDocument(JSON.stringify(store.document))
    expect(legacy.definition.nodes[1]!.typeVersion).toBe(1)
    const wrapper = mount(ExecutionConfig, { props: { node: store.document!.definition.nodes[1]! } })
    await wrapper.get('button').trigger('click')
    const upgraded = store.document!.definition.nodes[1]!
    expect(upgraded.typeVersion).toBe(2)
    expect(upgraded.configuration).toMatchObject({ promptMode: 'literal', inputBindings: [], outputMode: 'text', prompt: 'JSON {"x":1} and {{inputs.value}} stay literal' })
    expect(store.document!.definition.nodes[2]!.typeVersion).toBe(1)
    expect(parseDocument(JSON.stringify(store.document))).toEqual(store.document)
    wrapper.unmount()
  })
  it('round-trips structured bindings and rejects additional configuration/source properties', () => {
    const store = useWorkflowStore(); store.createNew()
    store.upgradeNode(store.document!.definition.nodes[1]!.id)
    store.upgradeNode(store.document!.definition.nodes[2]!.id)
    const model = store.document!.definition.nodes[1]!
    if (model.type !== 'modelCall' || model.typeVersion !== 2) throw new Error('fixture')
    model.configuration.promptMode = 'bindings'; model.configuration.inputBindings = [{ alias: 'value', source: { kind: 'runInput', pointer: '/~0/~1/0' } }]
    expect(parseDocument(JSON.stringify(store.document))).toEqual(store.document)
    const raw = JSON.parse(JSON.stringify(store.document))
    raw.definition.nodes[1].configuration.inputBindings[0].source.expression = 'eval()'
    expect(() => parseDocument(JSON.stringify(raw))).toThrow('Unknown property')
    delete raw.definition.nodes[1].configuration.inputBindings[0].source.expression
    raw.definition.nodes[1].configuration.outputMode = 'schema'
    expect(() => parseDocument(JSON.stringify(raw))).toThrow('Expected text or jsonObject')
  })
  it('derives earlier sources from control order and preserves stable IDs after rename/delete', () => {
    const doc = sampleDocument(), model = doc.definition.nodes[1]!, end = doc.definition.nodes[2]!
    doc.definition.nodes.reverse()
    expect(earlierModelNodes(doc, end.id).map(node => node.id)).toEqual([model.id])
    model.name = 'Renamed'; expect(earlierModelNodes(doc, end.id)[0]!.name).toBe('Renamed')
    expect(earlierModelNodes(doc, model.id)).toEqual([])
    doc.definition.nodes = doc.definition.nodes.filter(node => node.id !== model.id)
    expect(earlierModelNodes(doc, end.id)).toEqual([])
  })
  it('shows an unresolved saved source without guessing a replacement and limits JSON options', async () => {
    const doc = sampleDocument(), node = doc.definition.nodes[1]!
    const wrapper = mount(BindingSourceEditor, { props: { label: 'Final result', nodes: [node], modelValue: { kind: 'nodeJson', nodeId: node.id, pointer: '/x' } } })
    expect(wrapper.text()).toContain('Unresolved')
    expect(wrapper.text()).toContain('incompatible output mode')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.get('[aria-label="Final result source"]').setValue('runInput')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([{ kind: 'runInput', pointer: '' }])
    wrapper.unmount()
  })
  it('edits aliases and pointers through the inspector and leaves literal bindings explicit', async () => {
    const store = useWorkflowStore(); store.createNew(); const id = store.document!.definition.nodes[1]!.id; store.upgradeNode(id)
    const wrapper = mount(ExecutionConfig, { props: { node: store.document!.definition.nodes[1]! } })
    await wrapper.get('[aria-label="Prompt mode"]').setValue('bindings')
    await wrapper.findAll('button').find(button => button.text() === 'Add input binding')!.trigger('click')
    await nextTick()
    await wrapper.get('[aria-label="Binding 1 alias"]').setValue('varX')
    await wrapper.get('[aria-label="Binding 1 JSON pointer"]').setValue('/value')
    await wrapper.get('[aria-label="Output interpretation"]').setValue('jsonObject')
    await wrapper.get('[aria-label="Prompt mode"]').setValue('literal')
    expect(store.document!.definition.nodes[1]!.configuration).toMatchObject({ inputBindings: [{ alias: 'varX', source: { kind: 'runInput', pointer: '/value' } }] })
    expect(wrapper.text()).toContain('Remove any bindings')
    expect(wrapper.text()).toContain('does not constrain provider generation')
    wrapper.unmount()
  })
  it('checks RFC 6901 syntax without rejecting prototype-like keys as data', () => {
    for (const pointer of ['', '/a~0b/~1', '/__proto__/constructor/0']) expect(pointerError(pointer)).toBeNull()
    for (const pointer of ['field', '#/field', '/~2', '/a~']) expect(pointerError(pointer)).not.toBeNull()
  })
})
describe('raw run input submission', () => {
  it('preserves exact large-integer and decimal tokens on the wire', () => {
    const input = '{"n":900719925474099312345,"decimal":1.234567890123456789,"x":1e100}'
    expect(submissionBody('opaque-id', 7, input)).toBe(`{"submissionId":"opaque-id","expectedRevision":7,"input":${input}}`)
  })
  it('displays exact provider numeric tokens in run values while retaining numeric state counters', () => {
    const decoded = decodeRun<{ lastSequence: number; result: unknown; input: unknown; nodes: { attempt: { resolvedInputs: unknown; outputJson: unknown } }[] }>('{"lastSequence":7,"result":{"n":900719925474099312345,"decimal":1.234567890123456789},"input":{"__proto__":{"x":1e100}},"nodes":[{"attempt":{"resolvedInputs":{"n":900719925474099312345},"outputJson":{"n":900719925474099312345}}}]}')
    expect(decoded.lastSequence).toBe(7)
    expect(displayJson(decoded.result)).toContain('900719925474099312345')
    expect(displayJson(decoded.result)).toContain('1.234567890123456789')
    expect(displayJson(decoded.input)).toContain('1e100')
    expect(displayJson(decoded.input)).toContain('__proto__')
    expect(displayJson(decoded.nodes[0]!.attempt.resolvedInputs)).toContain('900719925474099312345')
    expect(displayJson(decoded.nodes[0]!.attempt.outputJson)).toContain('900719925474099312345')
  })
  it('accepts explicit null and prototype-like own properties without prototype traversal', () => {
    expect(() => validateRunInput('{"__proto__":{"constructor":null},"a":[null,true,"{{inputs.x}}"]}')).not.toThrow()
    expect(Object.hasOwn({}, 'constructor')).toBe(false)
  })
  it('rejects duplicate escaped keys, non-objects, malformed JSON, depth, and UTF-8 limits', () => {
    for (const value of ['{"a":1,"\\u0061":2}', '[]', 'null', '{"a":NaN}', '{"a":1} extra']) expect(() => validateRunInput(value)).toThrow()
    expect(() => validateRunInput('{"x":"' + '😀'.repeat(5000) + '"}')).toThrow('16 KiB')
    expect(() => validateRunInput('{"x":'.repeat(34) + '0' + '}'.repeat(34))).toThrow('depth')
  })
})
