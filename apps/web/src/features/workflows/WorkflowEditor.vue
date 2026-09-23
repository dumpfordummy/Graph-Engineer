<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
import { MAX_BYTES, nodeLabels } from './document'
import type { NodeType } from './document'
import { useWorkflowStore } from './store'
import GraphCanvas from './components/GraphCanvas.vue'
import NodeInspector from './components/NodeInspector.vue'
import ValidationPanel from './components/ValidationPanel.vue'
const store = useWorkflowStore(), route = useRoute(), router = useRouter()
const canvas = ref<InstanceType<typeof GraphCanvas> | null>(null), importFile = ref<HTMLInputElement | null>(null)
let internalNavigation = false, alive = true
const kinds: NodeType[] = ['start', 'modelCall', 'end']
const descriptions: Record<NodeType, string> = { start: 'Begin with an input', modelCall: 'Describe a model task', end: 'Define the final result' }
function confirmLeave() { return !store.dirty || window.confirm('Discard unsaved changes to this workflow? Export or save first to keep your draft.') }
onBeforeRouteLeave(() => internalNavigation || confirmLeave())
onBeforeRouteUpdate(() => internalNavigation || confirmLeave())
watch(() => route.params.id, id => {
  if (internalNavigation) return
  if (id === 'new') store.createNew()
  else if (typeof id === 'string') void store.load(id)
}, { immediate: true })
async function updateRoute(id: string) {
  if (!alive || route.params.id === id) return
  internalNavigation = true
  try { await router.replace(`/workflows/${id}`) } finally { internalNavigation = false }
}
async function save() { if (await store.save() && store.document) await updateRoute(store.document.workflow.id) }
function exportJson() {
  if (!store.document) return
  const url = URL.createObjectURL(new Blob([JSON.stringify(store.document, null, 2)], { type: 'application/json' }))
  const anchor = document.createElement('a')
  anchor.href = url; anchor.download = `${store.document.workflow.name.replace(/[^a-zA-Z0-9_-]/g, '-').slice(0, 80) || 'workflow'}.json`
  anchor.click(); URL.revokeObjectURL(url)
}
async function importJson(event: Event) {
  const input = event.target as HTMLInputElement, file = input.files?.[0]
  if (!file) return
  try {
    if (file.size > MAX_BYTES) { store.error = 'Import rejected. Import exceeds the 1 MiB document limit.'; return }
    if (await store.importText(await file.text(), confirmLeave)) await updateRoute('new')
  } catch (cause) { store.error = cause instanceof Error ? cause.message : 'Unable to read the import file. Your draft is preserved.' }
  finally { input.value = '' }
}
function beforeUnload(event: BeforeUnloadEvent) { if (store.dirty) { event.preventDefault(); event.returnValue = '' } }
onMounted(() => window.addEventListener('beforeunload', beforeUnload))
onUnmounted(() => { alive = false; window.removeEventListener('beforeunload', beforeUnload) })
</script>
<template>
  <div class="editor-page">
    <header class="editor-header"><RouterLink to="/" class="back-link" aria-label="Back to workflows">← <span>Workflows</span></RouterLink><span class="header-divider"></span><span class="brand-small">Graph Engineering</span><span class="local-badge"><span></span> Local workspace</span></header>
    <div v-if="store.loading" class="loading-state" role="status">Loading workflow…</div>
    <template v-else-if="store.document">
      <div class="editor-toolbar"><div class="workflow-title"><label class="sr-only" for="workflow-name">Workflow name</label><input id="workflow-name" :value="store.document.workflow.name" aria-label="Workflow name" maxlength="120" @input="store.setMetadata('name', ($event.target as HTMLInputElement).value)"><span data-testid="save-status" role="status" class="save-status" :class="{ unsaved: store.dirty }"><span></span>{{ store.status }}</span></div><div class="toolbar-actions"><button :disabled="store.saving || store.importing" @click="importFile?.click()">Import JSON</button><button @click="exportJson">Export JSON</button><button :disabled="store.validating" @click="store.validate">{{ store.validating ? 'Validating…' : 'Validate' }}</button><button class="primary" :disabled="store.saving || store.importing || (!store.dirty && store.persisted)" @click="save">{{ store.saving ? 'Saving…' : 'Save' }}</button><button class="run-button" disabled aria-label="Run unavailable in M1" title="Execution is unavailable in M1. Real execution begins in M3.">▷ Run <span>M3</span></button></div><input ref="importFile" data-testid="import-file" type="file" accept=".json,application/json" hidden @change="importJson"></div>
      <div v-if="store.error" class="error-banner editor-banner" role="alert">{{ store.error }}<button aria-label="Dismiss error" @click="store.error = ''">×</button></div>
      <div v-else-if="store.notice" class="notice-banner editor-banner" role="status">{{ store.notice }}</div>
      <div class="editor-workspace"><aside class="node-palette" aria-label="Node library"><div class="panel-heading">Node library <span class="count">3</span></div><div class="palette-body"><div class="eyebrow">BUILDING BLOCKS</div><button v-for="type in kinds" :key="type" class="palette-node" :aria-label="`Add ${nodeLabels[type]}`" @click="canvas?.addNode(type)"><span class="palette-icon" :class="type">{{ type === 'start' ? '↗' : type === 'end' ? '■' : '✦' }}</span><span><strong>{{ nodeLabels[type] }}</strong><small>{{ descriptions[type] }}</small></span><span class="add-mark">＋</span></button><div class="palette-note"><span>↗</span><p>Add a step, connect its ports, then configure it in the inspector.</p></div></div><div class="palette-footer"><span class="tag">M1 · DESIGN</span><p>Drafts stay on this computer.<br>Execution is unavailable.</p></div></aside><GraphCanvas ref="canvas" :key="store.document.workflow.id" /><NodeInspector @focus-edge="canvas?.focusEdge($event)" /></div>
      <ValidationPanel @focus-node="canvas?.focusNode($event)" @focus-edge="canvas?.focusEdge($event)" />
    </template>
    <div v-else class="load-error"><h1>Workflow unavailable</h1><p role="alert">{{ store.error }}</p><button @click="store.load(String(route.params.id))">Try again</button><RouterLink to="/" class="button">Back to workflows</RouterLink></div>
  </div>
</template>
