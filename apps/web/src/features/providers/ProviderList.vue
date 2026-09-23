<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { providerApi } from './api'
import { connectionReadiness, type ProviderProfile } from './contracts'
import { localSession } from '../session/session'
const profiles = ref<ProviderProfile[]>([]), loading = ref(true), error = ref('')
async function refresh() {
  loading.value = true; error.value = ''
  try { profiles.value = await providerApi.list() }
  catch (cause) { error.value = cause instanceof Error ? cause.message : 'Unable to load model connections.' }
  finally { loading.value = false }
}
onMounted(refresh)
watch(() => localSession.authenticated, value => { if (value) void refresh() })
</script>
<template>
  <div class="list-page">
    <header class="app-header"><RouterLink to="/" class="brand"><span class="brand-mark">⌘</span> Graph Engineering</RouterLink><RouterLink to="/">Workflows</RouterLink><span class="local-badge"><span></span> Local workspace</span></header>
    <main class="workflow-library">
      <div class="library-heading"><div><div class="eyebrow">SETTINGS</div><h1>Model connections</h1><p>Save a provider profile, then test its text connection explicitly.</p></div><RouterLink to="/settings/connections/new" class="button primary">＋ New connection</RouterLink></div>
      <div class="milestone-note"><span class="tag">M2 · CONNECTIONS</span><p>Credentials are protected for this Windows user. Workflow Run remains unavailable until M3.</p></div>
      <div v-if="error" role="alert" class="error-banner">{{ error }} <button @click="refresh">Try again</button></div>
      <div v-if="loading" role="status" class="loading-state">Loading model connections…</div>
      <section v-else-if="!error && !profiles.length" class="empty-library"><span class="empty-symbol">◇</span><h2>Connect your first model</h2><p>Enter the provider's API base URL and model ID.<br>Saving a profile never sends a model request.</p><RouterLink to="/settings/connections/new" class="button">Create connection</RouterLink></section>
      <section v-else-if="profiles.length" class="workflow-grid" aria-label="Provider profiles">
        <RouterLink v-for="profile in profiles" :key="profile.id" :to="`/settings/connections/${profile.id}`" class="workflow-card">
          <div class="card-top"><span class="workflow-glyph">✦</span><span class="tag">RESPONSES</span></div>
          <h2>{{ profile.name }}</h2><p>{{ profile.modelId }}<br>{{ profile.hasCredential ? 'Credential saved' : profile.authMode === 'none' ? 'Explicit no-auth connection' : 'Credential required' }}</p>
          <div class="card-footer"><span>{{ connectionReadiness(profile) }}</span><span aria-hidden="true">↗</span></div>
        </RouterLink>
      </section>
      <footer class="library-footer">Text probe only <span>·</span> Streaming, tools and JSON schema are not implemented in M2.</footer>
    </main>
  </div>
</template>
