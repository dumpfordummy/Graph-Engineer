<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { localSession } from '../session/session'
import { runApi } from './api'
import { RunLiveReader } from './live'
import { displayJson } from './exactJson'
import FrozenRunGraph from './FrozenRunGraph.vue'
import { terminal, type RunArtifact, type RunDetail, type RunEvent, type RunSummary } from './contracts'
const route = useRoute()
const items = ref<RunSummary[]>([]), nextCursor = ref<string | null>(null), historyLoading = ref(false)
const detail = ref<RunDetail | null>(null), events = ref<RunEvent[]>([]), error = ref(''), liveStatus = ref('Loading saved state…'), stale = ref(true), cancelling = ref(false)
const selectedNodeId = ref(''), artifact = ref<RunArtifact | null>(null), artifactLoading = ref(false)
const selectedNode = computed(() => detail.value?.nodes.find(node => node.nodeId === selectedNodeId.value))
const selectedProfile = computed(() => detail.value?.profiles.find(profile => profile.nodeId === selectedNodeId.value))
const modelAttempts = computed(() => detail.value?.nodes.filter(node => node.type === 'modelCall' && node.attempt).length ?? 0)
const dispatchIntents = computed(() => detail.value?.nodes.filter(node => node.type === 'modelCall' && node.attempt?.dispatchIntentAt).length ?? 0)
const responsesReceived = computed(() => detail.value?.nodes.filter(node => node.type === 'modelCall' && node.attempt?.externalOutcome === 'ResponseReceived').length ?? 0)
const workflowFilter = computed(() => typeof route.query.workflowId === 'string' ? route.query.workflowId : undefined)
let reader: RunLiveReader | undefined, generation = 0, artifactGeneration = 0, historyGeneration = 0
function pretty(value: unknown) { return displayJson(value) }
function date(value: string | null) { return value ? new Date(value).toLocaleString() : '—' }
function duration(start: string | null, end: string | null) { return start && end ? `${Math.max(0, Date.parse(end) - Date.parse(start)) / 1000}s` : 'not complete' }
async function history(more = false) {
  if (!localSession.authenticated) return
  const current = ++historyGeneration
  historyLoading.value = true; error.value = ''
  try {
    const result = await runApi.list(workflowFilter.value, more ? nextCursor.value ?? undefined : undefined)
    if (current !== historyGeneration) return
    items.value = more ? [...items.value, ...result.items.filter(item => !items.value.some(existing => existing.id === item.id))] : result.items
    nextCursor.value = result.nextCursor
  } catch (cause) { if (current === historyGeneration) error.value = cause instanceof Error ? cause.message : 'Run history is unavailable.' }
  finally { if (current === historyGeneration) historyLoading.value = false }
}
watch(() => [route.params.id, workflowFilter.value, localSession.authenticated], () => {
  const current = ++generation
  reader?.dispose(); reader = undefined; ++artifactGeneration; ++historyGeneration
  error.value = ''; artifact.value = null
  if (!localSession.authenticated) { stale.value = true; liveStatus.value = 'Pair again to read updates. Runs continue independently.'; return }
  if (typeof route.params.id !== 'string') { detail.value = null; void history(); return }
  const id = route.params.id
  if (detail.value?.id !== id) { detail.value = null; events.value = []; selectedNodeId.value = '' }
  reader = new RunLiveReader(id, (snapshot, orderedEvents) => {
    if (current !== generation) return
    detail.value = snapshot; events.value = orderedEvents
    if (!snapshot.nodes.some(node => node.nodeId === selectedNodeId.value)) selectedNodeId.value = snapshot.nodes.find(node => node.type === 'modelCall')?.nodeId ?? snapshot.nodes[0]?.nodeId ?? ''
  }, (status, isStale) => { if (current === generation) { liveStatus.value = status; stale.value = isStale } })
  void reader.start()
}, { immediate: true })
watch(selectedNodeId, () => { ++artifactGeneration; artifact.value = null; artifactLoading.value = false })
onUnmounted(() => { ++generation; ++artifactGeneration; ++historyGeneration; reader?.dispose() })
async function cancel() {
  if (!detail.value || terminal(detail.value.state) || cancelling.value) return
  const current = generation, id = detail.value.id
  cancelling.value = true; error.value = ''
  try { await runApi.cancel(id); if (current === generation) await reader?.refresh() }
  catch (cause) { if (current === generation) error.value = cause instanceof Error ? cause.message : 'Cancellation outcome is uncertain. Refresh saved state before taking another action.' }
  finally { cancelling.value = false }
}
async function loadArtifact(id: string) {
  if (!detail.value) return
  const current = ++artifactGeneration, runId = detail.value.id
  artifactLoading.value = true; error.value = ''
  try { const value = await runApi.artifact(runId, id); if (current === artifactGeneration) artifact.value = value }
  catch (cause) { if (current === artifactGeneration) error.value = cause instanceof Error ? cause.message : 'Artifact unavailable.' }
  finally { if (current === artifactGeneration) artifactLoading.value = false }
}
</script>
<template>
  <div class="runs-page">
    <header class="app-header"><RouterLink to="/" class="brand"><span class="brand-mark">⌘</span> Graph Engineering</RouterLink><RouterLink to="/" class="back-link">Workflows</RouterLink><RouterLink to="/settings/connections" class="back-link">Model connections</RouterLink><RouterLink to="/runs" class="back-link">Runs</RouterLink><span class="local-badge"><span></span> Local workspace</span></header>
    <main class="runs-main">
      <div v-if="error" class="error-banner" role="alert">{{ error }}</div>
      <template v-if="!route.params.id">
        <div class="library-heading"><div><div class="eyebrow">DURABLE HISTORY</div><h1>Runs</h1><p>Each run keeps its saved workflow revision and explicit inputs.</p><p v-if="workflowFilter" class="field-hint">Showing this workflow's history. <RouterLink to="/runs">Show all runs</RouterLink></p></div><button :disabled="historyLoading" @click="history()">Refresh history</button></div>
        <p class="field-hint retention-note">Input, prompts, outputs and artifacts persist locally without encryption. Workflow export contains definitions only. A browser disconnect does not cancel a run.</p>
        <p v-if="historyLoading" role="status">Loading run history…</p><p v-else-if="!items.length">No runs found. Open a saved workflow to review readiness and submit one.</p>
        <div class="runs-list"><RouterLink v-for="run in items" :key="run.id" :to="`/runs/${run.id}`" class="run-history-item"><div><strong>{{ run.workflowName }}</strong><span>Revision {{ run.workflowRevision }} · {{ date(run.createdAt) }}</span></div><span class="run-state" :data-state="run.state">{{ run.state }}</span><code>{{ run.id.slice(0, 8) }}</code></RouterLink></div>
        <button v-if="nextCursor" :disabled="historyLoading" @click="history(true)">Load older runs</button>
      </template>
      <template v-else>
        <div class="run-status-strip" :class="{ stale }" role="status" data-testid="run-connection-status"><span>{{ liveStatus }}</span><button @click="reader?.refresh()">Refresh saved state</button></div>
        <template v-if="detail">
          <div class="run-detail-heading"><div><div class="eyebrow">IMMUTABLE RUN · REVISION {{ detail.workflowRevision }}</div><h1>{{ detail.workflowName }}</h1><p class="mono">{{ detail.id }}</p></div><span class="run-state" :data-state="detail.state" data-testid="run-state">{{ detail.state }}</span><button v-if="!terminal(detail.state)" class="danger-text" :disabled="cancelling || detail.state === 'CancelRequested'" @click="cancel">{{ cancelling || detail.state === 'CancelRequested' ? 'Cancellation requested' : 'Cancel run' }}</button><RouterLink :to="`/workflows/${detail.workflowId}`" class="button">Open current workflow</RouterLink></div>
          <p v-if="detail.failureCode" role="alert" class="error-banner">{{ detail.failureCode }}</p>
          <p v-if="['Cancelled', 'CancelRequested', 'Interrupted'].includes(detail.state)" class="quiet-callout">Cancellation and interruption are not rollback. A remote provider may have processed a dispatched request or charged for it. Interrupted runs are never resumed automatically.</p>
          <div class="run-timestamps"><span>Created {{ date(detail.createdAt) }}</span><span>Started {{ date(detail.startedAt) }}</span><span>Finished {{ date(detail.finishedAt) }}</span><span>Duration {{ duration(detail.startedAt, detail.finishedAt) }}</span><span>Event sequence {{ detail.lastSequence }}</span></div>
          <p class="run-call-counts" data-testid="run-call-counts">Model attempts: {{ modelAttempts }} · Durable dispatch intents: {{ dispatchIntents }} · Responses received: {{ responsesReceived }}. An intent is not proof that a request reached the provider.</p>
          <FrozenRunGraph :run="detail" :selected-node-id="selectedNodeId" @select="selectedNodeId = $event" />
          <section v-if="detail.state === 'Succeeded'" class="run-result" aria-label="Final run result"><h2>Final result</h2><pre data-testid="run-result">{{ typeof detail.result === 'string' ? detail.result : pretty(detail.result) }}</pre></section>
          <div class="run-content-grid">
            <section class="run-node-list" aria-label="Run nodes"><h2>Ordered steps</h2><button v-for="(node, index) in detail.nodes" :key="node.nodeId" :class="{ selected: selectedNodeId === node.nodeId }" :aria-pressed="selectedNodeId === node.nodeId" @click="selectedNodeId = node.nodeId"><span>{{ index + 1 }}. {{ node.name }}</span><small>{{ node.state }}</small></button></section>
            <section class="run-inspector" aria-label="Run inspector">
              <template v-if="selectedNode"><div class="run-inspector-heading"><h2>{{ selectedNode.name }}</h2><span class="run-state" :data-state="selectedNode.state">{{ selectedNode.state }}</span></div><p v-if="selectedNode.skipReason" class="field-hint">{{ selectedNode.skipReason }}</p>
                <div v-if="selectedProfile" class="run-profile"><strong>{{ selectedProfile.name }}</strong> · {{ selectedProfile.modelId }} · connection version {{ selectedProfile.connectionVersion }}<p>{{ selectedProfile.protocol }} · {{ selectedProfile.baseUrl }}</p><p>{{ selectedProfile.timeoutSeconds }}s timeout · {{ selectedProfile.maxOutputTokens }} output tokens maximum</p></div>
                <template v-if="selectedNode.attempt"><p class="field-hint">Attempt {{ selectedNode.attempt.id }}<br>External outcome: <strong>{{ selectedNode.attempt.externalOutcome }}</strong><br>Started: {{ date(selectedNode.attempt.startedAt) }} · Duration: {{ duration(selectedNode.attempt.startedAt, selectedNode.attempt.finishedAt) }}<br>Dispatch intent: {{ date(selectedNode.attempt.dispatchIntentAt) }} · Finished: {{ date(selectedNode.attempt.finishedAt) }}</p><p v-if="selectedNode.attempt.failureCode || selectedNode.attempt.message" role="alert">{{ selectedNode.attempt.failureCode }} · {{ selectedNode.attempt.message }}</p>
                  <h3>Resolved inputs</h3><pre data-testid="resolved-inputs">{{ pretty(selectedNode.attempt.resolvedInputs) }}</pre>
                  <template v-if="selectedNode.attempt.prompt !== null"><h3>Resolved prompt</h3><pre data-testid="resolved-prompt">{{ selectedNode.attempt.prompt }}</pre></template>
                  <template v-if="selectedNode.attempt.outputText !== null"><h3>Completed assistant text</h3><pre data-testid="output-text">{{ selectedNode.attempt.outputText }}</pre></template>
                  <template v-if="selectedNode.attempt.outputJson !== null"><h3>JSON object — local validation</h3><pre>{{ pretty(selectedNode.attempt.outputJson) }}</pre></template>
                  <p v-if="selectedNode.attempt.observedModel || selectedNode.attempt.requestId" class="field-hint">Observed model: {{ selectedNode.attempt.observedModel ?? 'not supplied' }} · Request ID: {{ selectedNode.attempt.requestId ?? 'not supplied' }}</p><p v-if="selectedNode.attempt.usage" class="field-hint">Reported usage: {{ pretty(selectedNode.attempt.usage) }}</p>
                </template><p v-else class="field-hint">No attempt has been started for this node.</p>
                <div v-if="selectedNode.artifactIds.length" class="run-artifacts"><h3>Immutable artifacts</h3><button v-for="(id, index) in selectedNode.artifactIds" :key="id" :disabled="artifactLoading" @click="loadArtifact(id)">Inspect artifact {{ index + 1 }}</button></div>
                <div v-if="artifact" class="artifact-content"><h3>{{ artifact.kind }}</h3><p class="field-hint">{{ artifact.contentType }} · {{ artifact.byteCount }} bytes</p><p class="mono">SHA-256 {{ artifact.sha256 }}</p><pre>{{ artifact.text }}</pre></div>
              </template>
            </section>
          </div>
          <details class="run-snapshot"><summary>Frozen workflow and run input</summary><h3>Run input</h3><pre>{{ pretty(detail.input) }}</pre><h3>Workflow snapshot</h3><pre>{{ pretty(detail.snapshot) }}</pre><h3>Provider snapshots</h3><pre>{{ pretty(detail.profiles) }}</pre></details>
          <details class="run-events" open><summary>Ordered durable events ({{ events.length }})</summary><ol><li v-for="event in events" :key="event.sequence"><span class="mono">#{{ event.sequence }} · {{ date(event.at) }}</span><strong>{{ event.kind }}</strong><span>{{ event.state }} {{ event.message }}</span></li></ol></details>
        </template>
      </template>
    </main>
  </div>
</template>
