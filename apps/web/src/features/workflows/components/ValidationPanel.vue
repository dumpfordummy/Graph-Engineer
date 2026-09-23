<script setup lang="ts">
import { SCOPE } from '../document'
import { useWorkflowStore } from '../store'
const store = useWorkflowStore()
defineEmits<{ focusNode: [id: string]; focusEdge: [id: string] }>()
</script>
<template>
  <section class="validation-panel" data-testid="validation-panel" aria-label="Validation results">
    <div class="validation-heading"><h2>Validation</h2><span v-if="store.validating" role="status">Checking current draft…</span><span v-else-if="store.validationStale" class="warning-text">Draft changed · validate again</span><span v-else-if="store.validation?.valid" class="success-text">Structure valid</span><span v-else-if="store.validation" class="warning-text">{{ store.validation.issues.length }} issue{{ store.validation.issues.length === 1 ? '' : 's' }}</span><span v-else class="muted">Not checked yet</span><span class="tag">STRUCTURE ONLY</span></div>
    <p class="validation-scope">{{ store.validation?.scope ?? SCOPE }}</p>
    <div class="validation-issues" aria-live="polite"><p v-if="!store.validation" class="muted">Validate your current draft to check its connections and configuration.</p><p v-else-if="store.validation.valid" class="success-text">No structural or draft configuration issues found. This does not make the workflow executable.</p><div v-for="(issue, index) in store.validation?.issues" :key="`${issue.code}-${index}`" class="validation-issue"><span class="issue-mark">!</span><button v-if="issue.nodeId || issue.edgeId" @click="issue.nodeId ? $emit('focusNode', issue.nodeId) : $emit('focusEdge', issue.edgeId!)">{{ issue.message }} <span>↗</span></button><span v-else>{{ issue.message }}</span><code>{{ issue.path }}</code></div></div>
  </section>
</template>
