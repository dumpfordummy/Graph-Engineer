import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { workflowApi } from '../api'
import { copyDocument, sampleDocument, SCOPE } from '../document'
import type { WorkflowDocument } from '../document'
import { isTextEntry, useWorkflowStore } from '../store'

const valid = { structurallyValid: true, valid: true, issues: [], scope: SCOPE }
beforeEach(() => { setActivePinia(createPinia()) })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(r => { resolve = r })
  return { promise, resolve }
}
describe('editable draft lifecycle', () => {
  it('removes all incident edges and positions when deleting a node', () => {
    const store = useWorkflowStore(); store.createNew()
    const model = store.document!.definition.nodes[1]!
    store.selectNode(model.id); store.deleteSelection()
    expect(store.document!.definition.nodes).toHaveLength(2)
    expect(store.document!.definition.edges).toHaveLength(0)
    expect(store.document!.layout.nodes).toHaveLength(2)
    expect(store.dirty).toBe(true)
  })
  it('preserves node IDs while editing and reconnects edges without changing edge IDs', () => {
    const store = useWorkflowStore(); store.createNew()
    const edge = store.document!.definition.edges[1]!, edgeId = edge.id
    const id = store.addNode('modelCall', { x: 800, y: 200 })!
    store.updateNode(id, node => { node.name = 'Second model'; if (node.type === 'modelCall') node.configuration.prompt = 'Inspect the summary.' })
    expect(store.connect(edge.sourceNodeId, id, edgeId)).toBe(true)
    expect(store.document!.definition.edges.find(edge => edge.id === edgeId)?.targetNodeId).toBe(id)
    expect(store.document!.definition.nodes.find(node => node.id === id)?.name).toBe('Second model')
    expect(store.connect(edge.sourceNodeId, id)).toBe(false)
  })
  it('preserves unsaved content after failed and conflicting saves', async () => {
    const store = useWorkflowStore(); store.createNew(); store.setMetadata('name', 'Precious edits')
    vi.spyOn(workflowApi, 'create').mockRejectedValue(new Error('Save conflict: newer revision'))
    const before = copyDocument(store.document!)
    expect(await store.save()).toBe(false)
    expect(store.document).toEqual(before); expect(store.dirty).toBe(true)
    expect(store.error).toContain('Save conflict'); expect(store.saving).toBe(false)
  })
  it('adopts server revision but preserves edits typed during an in-flight save', async () => {
    const store = useWorkflowStore(); store.createNew()
    const response = deferred<WorkflowDocument>()
    vi.spyOn(workflowApi, 'create').mockReturnValue(response.promise)
    const first = copyDocument(store.document!)
    const pending = store.save()
    store.setMetadata('name', 'Typed while saving')
    response.resolve({ ...first, workflow: { ...first.workflow, id: crypto.randomUUID(), revision: 1 } })
    await pending
    expect(store.document!.workflow.name).toBe('Typed while saving')
    expect(store.document!.workflow.revision).toBe(1)
    expect(store.dirty).toBe(true); expect(store.persisted).toBe(true)
    const update = vi.spyOn(workflowApi, 'update').mockImplementation(async doc => ({ ...doc, workflow: { ...doc.workflow, revision: 2 } }))
    await store.save()
    expect(update.mock.calls[0]![0].workflow.revision).toBe(1)
    expect(store.document!.workflow.revision).toBe(2); expect(store.dirty).toBe(false)
  })
  it('does not overwrite a newer document when an old save completes', async () => {
    const store = useWorkflowStore(); store.createNew()
    const response = deferred<WorkflowDocument>(), saved = copyDocument(store.document!)
    vi.spyOn(workflowApi, 'create').mockReturnValue(response.promise)
    const pending = store.save()
    store.createNew(); const newId = store.document!.workflow.id
    response.resolve(saved)
    expect(await pending).toBe(false)
    expect(store.document!.workflow.id).toBe(newId)
  })
  it('validates current unsaved edits and marks reports stale after later edits', async () => {
    const store = useWorkflowStore(); store.createNew(); store.setMetadata('name', 'Current draft')
    const validate = vi.spyOn(workflowApi, 'validate').mockResolvedValue(valid)
    await store.validate()
    expect(validate.mock.calls[0]![0].workflow.name).toBe('Current draft')
    expect(store.validationStale).toBe(false)
    store.addNode('end'); expect(store.validationStale).toBe(true)
  })
  it('retains existing draft on malformed imports, structural rejection and cancellation', async () => {
    const store = useWorkflowStore(); store.createNew()
    const before = copyDocument(store.document!)
    const validate = vi.spyOn(workflowApi, 'validate').mockResolvedValue(valid)
    expect(await store.importText('{', () => true)).toBe(false)
    expect(validate).not.toHaveBeenCalled(); expect(store.document).toEqual(before)
    validate.mockResolvedValueOnce({ ...valid, structurallyValid: false, valid: false, issues: [{ code: 'bad_shape', severity: 'error', path: 'definition', message: 'Invalid shape.' }] })
    expect(await store.importText(JSON.stringify(sampleDocument()), () => true)).toBe(false)
    expect(store.document).toEqual(before)
    expect(await store.importText(JSON.stringify(sampleDocument()), () => false)).toBe(false)
    expect(store.document).toEqual(before)
  })
  it('imports as a new identity and POSTs without overwriting the imported workflow', async () => {
    const store = useWorkflowStore(); store.createNew()
    const imported = sampleDocument(); imported.workflow.revision = 9
    vi.spyOn(workflowApi, 'validate').mockResolvedValue(valid)
    expect(await store.importText(JSON.stringify(imported), () => true)).toBe(true)
    expect(store.document!.workflow.id).not.toBe(imported.workflow.id)
    expect(store.document!.workflow.revision).toBe(0); expect(store.persisted).toBe(false)
    expect(store.document!.definition).toEqual(imported.definition)
    const create = vi.spyOn(workflowApi, 'create').mockImplementation(async doc => doc)
    const update = vi.spyOn(workflowApi, 'update')
    await store.save(); expect(create).toHaveBeenCalledOnce(); expect(update).not.toHaveBeenCalled()
  })
  it('recognizes text fields, selectors and nested contenteditable targets for shortcut protection', () => {
    for (const tag of ['input', 'textarea', 'select']) expect(isTextEntry(document.createElement(tag))).toBe(true)
    const parent = document.createElement('div'), child = document.createElement('span')
    parent.contentEditable = 'true'; parent.setAttribute('contenteditable', 'true'); parent.append(child)
    expect(isTextEntry(child)).toBe(true)
    expect(isTextEntry(document.createElement('button'))).toBe(false)
  })
})
