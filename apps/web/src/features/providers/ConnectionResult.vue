<script setup lang="ts">
import { computed } from 'vue'
import type { ConnectionTest } from './contracts'
const props = defineProps<{ result: ConnectionTest; currentVersion: number; dirty: boolean }>()
const reportedUsage = computed(() => {
  const usage = props.result.usage
  if (!usage) return ''
  return [
    ['Input', usage.inputTokens], ['Output', usage.outputTokens], ['Total', usage.totalTokens],
  ].filter(([, count]) => typeof count === 'number').map(([label, count]) => `${label} ${count}`).join(' · ')
})
</script>
<template>
  <section class="connection-result" aria-label="Last connection test">
    <h2>Last connection test</h2>
    <p :class="result.success && result.connectionVersion === currentVersion && !dirty ? 'success-text' : 'warning-text'">{{ result.success ? 'Text response verified' : 'Text response not verified' }} · {{ result.category }}</p>
    <p v-if="dirty || result.connectionVersion !== currentVersion" class="warning-text">This result belongs to saved connection version {{ result.connectionVersion }}. It does not verify your current edits.</p>
    <p>{{ result.message }}</p>
    <dl class="result-metadata"><dt>Tested</dt><dd>{{ new Date(result.testedAt).toLocaleString() }}</dd><dt>Duration</dt><dd>{{ result.durationMs }} ms</dd><dt>Connection version</dt><dd>{{ result.connectionVersion }}</dd><dt>Probe phrase matched</dt><dd>{{ result.phraseMatched ? 'Yes' : 'No' }}</dd><template v-if="result.observedModel"><dt>Observed model</dt><dd>{{ result.observedModel }}</dd></template><template v-if="result.requestId"><dt>Request ID</dt><dd>{{ result.requestId }}</dd></template><template v-if="reportedUsage"><dt>Reported usage</dt><dd>{{ reportedUsage }}</dd></template></dl>
    <pre v-if="result.preview" class="response-preview" aria-label="Response preview">{{ result.preview }}</pre>
    <p class="field-hint">Streaming, tools and JSON-schema output: not tested / not implemented in M2. No workflow was executed.</p>
  </section>
</template>
