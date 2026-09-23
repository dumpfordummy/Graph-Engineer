<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useWorkflowStore } from '../workflows/store'
import { localSession } from '../session/session'
import { ApiError } from '../api'
import { runApi, validateRunInput } from './api'
import type { Readiness } from './contracts'
const store = useWorkflowStore(), router = useRouter(), route = useRoute()
const readiness = ref<Readiness | null>(null), loading = ref(false), error = ref(''), checking = ref(false), confirming = ref(false)
const showReadiness = ref(false), open = ref(typeof route.query.submission === 'string'), input = ref('{}'), accepted = ref(false)
const submissionId = ref(typeof route.query.submission === 'string' ? route.query.submission : '')
const attempted = ref(Boolean(submissionId.value)), dialog = ref<InstanceType<typeof globalThis.HTMLDialogElement> | null>(null)
let generation = 0, alive = true
onUnmounted(() => { alive = false; generation++ })
const disabledReason = computed(() => !store.persisted || store.dirty ? 'Save this workflow before running.' : loading.value ? 'Checking saved execution readiness…' : !readiness.value?.ready ? 'Configure the saved workflow for execution. See Run readiness.' : '')
watch(() => [store.document?.workflow.id, store.document?.workflow.revision, store.dirty, store.persisted, localSession.authenticated], async () => {
  const current = ++generation
  readiness.value = null; error.value = ''
  if (!store.persisted || store.dirty || !store.document || !localSession.authenticated) { loading.value = false; return }
  loading.value = true
  try { const result = await runApi.readiness(store.document.workflow.id); if (current === generation) readiness.value = result }
  catch (cause) { if (current === generation) error.value = cause instanceof Error ? cause.message : 'Readiness is unavailable.' }
  finally { if (current === generation) loading.value = false }
}, { immediate: true })
watch(() => [open.value, localSession.authenticated], async () => { await nextTick(); if (open.value && localSession.authenticated && dialog.value && !dialog.value.open) dialog.value.showModal() }, { immediate: true })
function begin() { error.value = ''; accepted.value = false; open.value = true }
function close() { if (confirming.value) return; open.value = false; dialog.value?.close() }
async function inspectSubmission() {
  if (!submissionId.value || checking.value) return
  checking.value = true; error.value = ''
  try { const run = await runApi.submission(submissionId.value); if (alive && open.value) await router.push(`/runs/${run.id}`) }
  catch (cause) { if (alive) error.value = cause instanceof ApiError && cause.status === 404 ? 'No durable run was found for this submission. Do not assume a connection failure cancelled work. Check Runs and use a new deliberate submission only when this outcome is settled.' : cause instanceof Error ? cause.message : 'Submission lookup failed.' }
  finally { checking.value = false }
}
async function submit() {
  if (!store.document || disabledReason.value || !accepted.value || confirming.value || attempted.value) return
  error.value = ''
  try { validateRunInput(input.value) } catch (cause) { error.value = cause instanceof Error ? cause.message : 'Invalid JSON input.'; return }
  confirming.value = true
  const workflowId = store.document.workflow.id, revision = store.document.workflow.revision
  submissionId.value = crypto.randomUUID(); attempted.value = true
  // Only an opaque ID enters the URL. Reload and re-pair can look it up but can
  // never replay this POST or put run input into browser storage.
  try {
    await router.replace({ path: route.path, query: { ...route.query, submission: submissionId.value } })
    if (!alive) return
    const run = await runApi.submit(workflowId, submissionId.value, revision, input.value)
    if (alive) await router.push(`/runs/${run.id}`)
  } catch (cause) {
    if (!alive) return
    error.value = cause instanceof Error ? cause.message : 'The submission outcome is uncertain.'
    if (cause instanceof ApiError && [400, 409, 413, 422].includes(cause.status)) {
      attempted.value = false; submissionId.value = ''
      await router.replace({ path: route.path, query: {} })
    }
  } finally { confirming.value = false }
}
</script>
<template>
  <div class="run-control">
    <button class="run-button" aria-label="Run workflow" :disabled="Boolean(disabledReason) || attempted" :title="disabledReason || 'Review the saved revision and confirm provider usage'" @click="begin">▷ Run</button>
    <button class="readiness-button" :aria-expanded="showReadiness" @click="showReadiness = !showReadiness">Run readiness</button>
    <button v-if="attempted" @click="begin">Check submission</button>
    <div v-if="showReadiness" class="readiness-popover" role="region" aria-label="Run readiness"><p>{{ disabledReason || 'Saved workflow is ready for an explicit run.' }}</p><p v-if="error" role="alert">{{ error }}</p><ul v-if="readiness?.issues.length"><li v-for="(issue, index) in readiness.issues" :key="index"><button v-if="issue.nodeId" @click="store.selectNode(issue.nodeId)">{{ issue.message }}</button><span v-else>{{ issue.message }}</span></li></ul></div>
  </div>
  <Teleport to="body">
    <dialog v-if="open && localSession.authenticated" ref="dialog" class="run-dialog" aria-labelledby="run-title" @cancel.prevent="close">
      <form @submit.prevent="submit">
        <div class="run-dialog-heading"><h2 id="run-title">Confirm workflow run</h2><button type="button" :disabled="confirming" aria-label="Close run confirmation" @click="close">×</button></div>
        <p><strong>{{ store.document?.workflow.name }}</strong> · saved revision {{ store.document?.workflow.revision }}</p>
        <template v-if="!attempted">
          <ol class="run-plan"><li v-for="node in readiness?.orderedNodes" :key="node.nodeId"><strong>{{ node.name }}</strong><span v-if="node.type === 'modelCall'">{{ node.profileName }} · {{ node.modelId }} · connection version {{ node.connectionVersion }}</span><span v-else>Local {{ node.type }} step</span></li></ol>
          <p>Maximum {{ readiness?.maximumModelCalls ?? 0 }} model calls. Provider usage may incur charges. No automatic inference retries occur.</p>
          <label>Run input JSON object<textarea v-model="input" aria-label="Run input JSON object" rows="6" spellcheck="false"></textarea></label>
          <p class="field-hint">Start sample text is not run input. Input, resolved prompts, successful outputs and artifacts remain in the local SQLite database without encryption. Exported workflow definitions do not include run data.</p>
          <label class="checkbox-line"><input v-model="accepted" type="checkbox" aria-label="Confirm provider usage and local retention">I approve the listed provider calls and local retention.</label>
          <p v-if="disabledReason" class="warning-text">{{ disabledReason }}</p>
        </template>
        <template v-else><p>The submission ID is retained for reconciliation. Checking its status does not submit another run.</p><code class="mono">{{ submissionId }}</code><p class="field-hint">A missing response does not establish whether work began. Browser refresh, reconnect and pairing never replay the submission.</p></template>
        <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
        <div class="run-dialog-actions"><button v-if="attempted" type="button" :disabled="checking || confirming" @click="inspectSubmission">{{ checking ? 'Checking…' : 'Look up submission' }}</button><button v-else class="primary" type="submit" :disabled="!accepted || Boolean(disabledReason) || confirming">{{ confirming ? 'Submitting…' : 'Confirm and run' }}</button><button type="button" :disabled="confirming" @click="close">Close</button><RouterLink to="/runs">Runs history</RouterLink></div>
      </form>
    </dialog>
  </Teleport>
</template>
