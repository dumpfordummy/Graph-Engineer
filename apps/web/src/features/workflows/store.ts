import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { workflowApi } from './api'
import { copyDocument, freshIdentity, makeNode, parseDocument, sampleDocument } from './document'
import type { NodeType, ValidationReport, Viewport, WorkflowDocument, WorkflowEdge, WorkflowNode } from './document'

export function isTextEntry(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && Boolean(target.closest('input, textarea, select, [contenteditable="true"], [role="textbox"]'))
}
export const useWorkflowStore = defineStore('workflow', () => {
  const document = ref<WorkflowDocument | null>(null)
  const persisted = ref(false), dirty = ref(false), loading = ref(false), saving = ref(false), validating = ref(false), importing = ref(false)
  const error = ref(''), notice = ref('')
  const selection = ref<{ kind: 'node' | 'edge'; id: string } | null>(null)
  const validation = ref<ValidationReport | null>(null), validationStale = ref(false)
  let generation = 0, editVersion = 0, validateRequest = 0
  const selectedNode = computed(() => selection.value?.kind === 'node' ? document.value?.definition.nodes.find(node => node.id === selection.value?.id) : undefined)
  const selectedEdge = computed(() => selection.value?.kind === 'edge' ? document.value?.definition.edges.find(edge => edge.id === selection.value?.id) : undefined)
  const status = computed(() => saving.value ? 'Saving…' : error.value ? 'Save needed' : dirty.value ? 'Unsaved changes' : persisted.value ? `Saved · revision ${document.value?.workflow.revision}` : 'New draft')
  function changed() { dirty.value = true; editVersion++; validationStale.value = validation.value !== null; notice.value = '' }
  function replace(next: WorkflowDocument, saved: boolean) {
    generation++; editVersion = 0
    document.value = next; persisted.value = saved; dirty.value = !saved
    error.value = ''; notice.value = ''; selection.value = null; validation.value = null; validationStale.value = false
  }
  function createNew() { replace(sampleDocument(), false) }
  async function load(id: string) {
    const requestGeneration = ++generation
    loading.value = true; error.value = ''; document.value = null; dirty.value = false; selection.value = null
    try { const result = await workflowApi.get(id); if (generation === requestGeneration) replace(result, true) }
    catch (cause) { if (generation === requestGeneration) error.value = message(cause) }
    finally { if (generation === requestGeneration || generation === requestGeneration + 1) loading.value = false }
  }
  function setMetadata(key: 'name' | 'description', value: string) { if (document.value) { document.value.workflow[key] = value; changed() } }
  function updateNode(id: string, update: (node: WorkflowNode) => void) {
    const node = document.value?.definition.nodes.find(node => node.id === id)
    if (node) { update(node); changed() }
  }
  function addNode(type: NodeType, position?: { x: number; y: number }) {
    if (!document.value) return
    if (document.value.definition.nodes.length >= 200) { error.value = 'A document can contain at most 200 nodes.'; return }
    const node = makeNode(type)
    document.value.definition.nodes.push(node)
    document.value.layout.nodes.push({ nodeId: node.id, ...(position ?? { x: 160 + (document.value.definition.nodes.length % 3) * 240, y: 330 }) })
    selection.value = { kind: 'node', id: node.id }; changed()
    return node.id
  }
  function moveNodes(positions: { id: string; x: number; y: number }[]) {
    if (!document.value) return
    let moved = false
    for (const position of positions) {
      const layout = document.value.layout.nodes.find(node => node.nodeId === position.id)
      if (layout && (layout.x !== position.x || layout.y !== position.y)) { layout.x = position.x; layout.y = position.y; moved = true }
    }
    if (moved) changed()
  }
  function setViewport(viewport: Viewport) {
    if (!document.value || Math.abs(document.value.layout.viewport.x - viewport.x) < 0.01 && Math.abs(document.value.layout.viewport.y - viewport.y) < 0.01 && Math.abs(document.value.layout.viewport.zoom - viewport.zoom) < 0.0001) return
    document.value.layout.viewport = { ...viewport }; changed()
  }
  function connect(source: string, target: string, edgeId?: string): boolean {
    if (!document.value) return false
    const sourceNode = document.value.definition.nodes.find(node => node.id === source)
    const targetNode = document.value.definition.nodes.find(node => node.id === target)
    if (!sourceNode || !targetNode || sourceNode.type === 'end' || targetNode.type === 'start') return false
    if (document.value.definition.edges.some(edge => edge.id !== edgeId && edge.sourceNodeId === source && edge.targetNodeId === target)) {
      notice.value = 'These ports are already connected.'; return false
    }
    const edge = edgeId ? document.value.definition.edges.find(edge => edge.id === edgeId) : undefined
    if (edgeId && !edge) return false
    if (!edge && document.value.definition.edges.length >= 400) { error.value = 'A document can contain at most 400 edges.'; return false }
    if (edge) { edge.sourceNodeId = source; edge.targetNodeId = target }
    else document.value.definition.edges.push({ id: crypto.randomUUID(), sourceNodeId: source, sourcePort: 'out', targetNodeId: target, targetPort: 'in' })
    changed(); return true
  }
  function deleteItems(nodeIds: string[], edgeIds: string[] = []) {
    if (!document.value || !nodeIds.length && !edgeIds.length) return
    document.value.definition.nodes = document.value.definition.nodes.filter(node => !nodeIds.includes(node.id))
    document.value.definition.edges = document.value.definition.edges.filter(edge => !edgeIds.includes(edge.id) && !nodeIds.includes(edge.sourceNodeId) && !nodeIds.includes(edge.targetNodeId))
    document.value.layout.nodes = document.value.layout.nodes.filter(node => !nodeIds.includes(node.nodeId))
    selection.value = null; changed()
  }
  function deleteSelection() { if (selection.value) deleteItems(selection.value.kind === 'node' ? [selection.value.id] : [], selection.value.kind === 'edge' ? [selection.value.id] : []) }
  async function save(): Promise<boolean> {
    if (!document.value || saving.value) return false
    const snapshot = copyDocument(document.value), version = editVersion, requestGeneration = generation
    saving.value = true; error.value = ''; notice.value = ''
    try {
      const result = await (persisted.value ? workflowApi.update(snapshot) : workflowApi.create(snapshot))
      if (generation !== requestGeneration || !document.value) return false
      // The server owns identity/revision/timestamps, but edits typed during a save
      // still own their fields. The next save uses the new concurrency revision.
      document.value.workflow = { ...document.value.workflow, id: result.workflow.id, revision: result.workflow.revision, createdAt: result.workflow.createdAt, updatedAt: result.workflow.updatedAt }
      persisted.value = true; dirty.value = version !== editVersion
      notice.value = dirty.value ? 'Saved the previous changes. Newer edits still need saving.' : 'Draft saved to this computer.'
      return true
    } catch (cause) { if (generation === requestGeneration) error.value = message(cause); return false }
    finally { saving.value = false }
  }
  async function validate() {
    if (!document.value) return
    const request = ++validateRequest, requestGeneration = generation, version = editVersion
    validating.value = true; error.value = ''
    try {
      const report = await workflowApi.validate(copyDocument(document.value))
      if (requestGeneration === generation && request === validateRequest) { validation.value = report; validationStale.value = version !== editVersion }
    } catch (cause) { if (requestGeneration === generation) error.value = message(cause) }
    finally { if (request === validateRequest) validating.value = false }
  }
  async function importText(text: string, confirmReplace: () => boolean): Promise<boolean> {
    if (importing.value || saving.value) return false
    const requestGeneration = generation
    importing.value = true; error.value = ''
    try {
      const parsed = parseDocument(text)
      const report = await workflowApi.validate(parsed)
      if (!report.structurallyValid) throw new Error(report.issues.map(issue => `${issue.path}: ${issue.message}`).join(' '))
      if (requestGeneration !== generation) return false
      if (dirty.value && !confirmReplace()) return false
      replace(freshIdentity(parsed), false)
      validation.value = report
      notice.value = 'Imported as a new draft. Save to create a separate workflow.'
      return true
    } catch (cause) { if (requestGeneration === generation) error.value = `Import rejected. ${message(cause)}`; return false }
    finally { importing.value = false }
  }
  function selectNode(id: string) { selection.value = { kind: 'node', id } }
  function selectEdge(id: string) { selection.value = { kind: 'edge', id } }
  return { document, persisted, dirty, loading, saving, validating, importing, error, notice, selection, selectedNode, selectedEdge, validation, validationStale, status, createNew, load, setMetadata, updateNode, addNode, moveNodes, setViewport, connect, deleteItems, deleteSelection, save, validate, importText, selectNode, selectEdge }
})
function message(cause: unknown): string { return cause instanceof Error ? cause.message : 'An unexpected error occurred. Your draft is preserved.' }
export function edgeDescription(edge: WorkflowEdge, nodes: WorkflowNode[]): string {
  return `${nodes.find(node => node.id === edge.sourceNodeId)?.name ?? edge.sourceNodeId} → ${nodes.find(node => node.id === edge.targetNodeId)?.name ?? edge.targetNodeId}`
}
