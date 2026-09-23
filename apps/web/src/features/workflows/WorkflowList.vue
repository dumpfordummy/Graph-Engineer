<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { workflowApi } from './api'
import type { WorkflowMetadata } from './document'
const workflows = ref<WorkflowMetadata[]>([]), loading = ref(true), error = ref('')
async function refresh() {
  loading.value = true; error.value = ''
  try { workflows.value = await workflowApi.list() }
  catch (cause) { error.value = cause instanceof Error ? cause.message : 'Unable to load workflows.' }
  finally { loading.value = false }
}
onMounted(refresh)
const date = (value: string) => new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
</script>

<template>
  <div class="list-page">
    <header class="app-header"><RouterLink to="/" class="brand"><span class="brand-mark">⌘</span> Graph Engineering</RouterLink><RouterLink to="/settings/connections" class="back-link">Model connections</RouterLink><span class="local-badge"><span></span> Local workspace</span></header>
    <main class="workflow-library">
      <div class="library-heading"><div><div class="eyebrow">YOUR WORKSPACE</div><h1>Workflows</h1><p>Give your next idea a structure.</p></div><RouterLink to="/workflows/new" class="button primary">＋ New workflow</RouterLink></div>
      <div class="milestone-note"><span class="tag">M2 · DESIGN</span><p>Create, connect, and save drafts on this computer. Execution becomes available in a later milestone.</p></div>
      <div v-if="error" role="alert" class="error-banner">{{ error }} <button @click="refresh">Try again</button></div>
      <div v-if="loading" class="loading-state" role="status">Loading your workflows…</div>
      <section v-else-if="!error && workflows.length === 0" class="empty-library"><span class="empty-symbol">◇</span><h2>A clear starting point</h2><p>Create a workflow with Start, Model Call, and End.<br>Make it your own, then save your first draft.</p><RouterLink to="/workflows/new" class="button">Create your first workflow</RouterLink></section>
      <section v-else-if="workflows.length" class="workflow-grid" aria-label="Saved workflows">
        <RouterLink v-for="workflow in workflows" :key="workflow.id" :to="`/workflows/${workflow.id}`" class="workflow-card">
          <div class="card-top"><span class="workflow-glyph">⌘</span><span class="tag">DRAFT</span></div>
          <h2>{{ workflow.name || 'Unnamed workflow' }}</h2><p>{{ workflow.description || 'No description yet.' }}</p>
          <div class="card-footer"><span>Revision {{ workflow.revision }}</span><time :datetime="workflow.updatedAt">{{ date(workflow.updatedAt) }}</time><span aria-hidden="true">↗</span></div>
        </RouterLink>
      </section>
      <footer class="library-footer">Stored locally with SQLite <span>·</span> Structure first. Execution later.</footer>
    </main>
  </div>
</template>
