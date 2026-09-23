<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
import { localSession } from '../session/session'
import { providerApi } from './api'
import { endpointPreview, newProviderSettings, profileSettings, type ProviderProfile, type ProviderWrite } from './contracts'
import ConnectionResult from './ConnectionResult.vue'

const route = useRoute(), router = useRouter()
const settings = reactive(newProviderSettings()), saved = ref<ProviderProfile | null>(null)
const credentialAction = ref<'keep' | 'replace' | 'remove'>('keep')
// The key never enters a store, saved snapshot, URL or persistent browser storage.
const apiKey = ref(''), confirmDestinationChange = ref(false)
const loading = ref(false), saving = ref(false), testing = ref(false), deleting = ref(false)
const error = ref(''), notice = ref(''), loadFailed = ref(false)
const baseline = ref(JSON.stringify(newProviderSettings()))
let hydrating = false, internalNavigation = false, alive = true, loadGeneration = 0
let probe: InstanceType<typeof window.AbortController> | null = null
const dirty = computed(() => JSON.stringify(settings) !== baseline.value || credentialAction.value !== 'keep' || !!apiKey.value)
const endpoint = computed(() => endpointPreview(settings.baseUrl))
const destinationChanged = computed(() => !!saved.value && (endpoint.value !== saved.value.resolvedEndpoint || settings.authMode !== saved.value.authMode))
const testDisabled = computed(() => !saved.value || dirty.value || saving.value || testing.value || deleting.value
  || !localSession.authenticated || saved.value.authMode === 'bearer' && !saved.value.hasCredential)
const testHint = computed(() => !saved.value ? 'Save this profile before testing.' : dirty.value ? 'Save your changes before testing the saved profile.'
  : saved.value.authMode === 'bearer' && !saved.value.hasCredential ? 'Replace and save a credential before testing.' : 'Only the saved connection version will be tested.')

function clearKey() { apiKey.value = '' }
watch(() => localSession.authenticated, authenticated => { if (!authenticated) { clearKey(); probe?.abort() } })
watch(credentialAction, action => { if (action !== 'replace') clearKey() })
watch(() => [settings.baseUrl, settings.authMode], () => {
  if (hydrating) return
  settings.allowPrivateNetwork = false; settings.allowInsecureHttp = false; confirmDestinationChange.value = false
  clearKey()
  if (settings.authMode === 'none') credentialAction.value = saved.value?.hasCredential ? 'remove' : 'keep'
  else if (saved.value && destinationChanged.value) credentialAction.value = 'replace'
}, { flush: 'sync' })

function adopt(profile: ProviderProfile | null) {
  hydrating = true
  Object.assign(settings, profile ? profileSettings(profile) : newProviderSettings())
  saved.value = profile
  baseline.value = JSON.stringify(settings)
  credentialAction.value = 'keep'; confirmDestinationChange.value = false; clearKey()
  hydrating = false
}
async function load(id: string) {
  const generation = ++loadGeneration
  clearKey(); probe?.abort(); error.value = ''; notice.value = ''; loadFailed.value = false
  if (id === 'new') { adopt(null); loading.value = false; return }
  loading.value = true
  try { const profile = await providerApi.get(id); if (alive && generation === loadGeneration) adopt(profile) }
  catch (cause) { if (alive && generation === loadGeneration) { error.value = message(cause); loadFailed.value = true } }
  finally { if (generation === loadGeneration) loading.value = false }
}
watch(() => route.params.id, id => { if (!internalNavigation && typeof id === 'string') void load(id) }, { immediate: true })

function message(cause: unknown, key = '') {
  const text = cause instanceof Error ? cause.message : 'The request failed. Your safe settings are preserved.'
  return key ? text.split(key).join('[redacted]') : text
}
function confirmLeave() {
  if (!dirty.value && !testing.value) return true
  return window.confirm(testing.value
    ? 'Leave this connection? The pending test will be cancelled locally, but the provider may still finish work or charge usage. Unsaved changes will be discarded.'
    : 'Discard unsaved changes to this provider profile? Save first to keep your settings. Transient credentials cannot be recovered.')
}
onBeforeRouteLeave(() => internalNavigation || confirmLeave())
onBeforeRouteUpdate(() => internalNavigation || confirmLeave())
function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value || testing.value) { event.preventDefault(); event.returnValue = '' } }
onMounted(() => window.addEventListener('beforeunload', beforeUnload))
onUnmounted(() => { alive = false; ++loadGeneration; clearKey(); probe?.abort(); window.removeEventListener('beforeunload', beforeUnload) })

async function save() {
  if (saving.value || deleting.value || !localSession.authenticated) return
  error.value = ''; notice.value = ''
  if (!endpoint.value) { error.value = 'Enter an HTTP(S) API base URL without a method path, query, fragment, user information or path escapes.'; clearKey(); return }
  if (credentialAction.value === 'replace' && !apiKey.value.trim()) { error.value = 'Enter the replacement key. A blank field never removes a saved credential.'; return }
  if (destinationChanged.value && (!confirmDestinationChange.value || settings.authMode === 'bearer' && credentialAction.value !== 'replace')) {
    error.value = 'Confirm the changed destination and re-enter a replacement credential for bearer authentication.'; clearKey(); return
  }
  const key = apiKey.value
  const write: ProviderWrite = { ...settings, confirmDestinationChange: confirmDestinationChange.value,
    credential: credentialAction.value === 'replace' ? { action: 'replace', value: key } : { action: credentialAction.value },
    ...(saved.value ? { expectedRevision: saved.value.revision } : {}),
  }
  saving.value = true
  try {
    const profile = await (saved.value ? providerApi.update(saved.value.id, write) : providerApi.create(write))
    if (!alive) return
    adopt(profile); notice.value = 'Profile saved on this computer. Saving did not call the provider.'
    if (route.params.id !== profile.id) {
      internalNavigation = true
      try { await router.replace(`/settings/connections/${profile.id}`) } finally { internalNavigation = false }
    }
  } catch (cause) { if (alive) error.value = message(cause, key) }
  finally { clearKey(); saving.value = false }
}
async function testConnection() {
  if (testDisabled.value || !saved.value) return
  const snapshot = saved.value
  testing.value = true; error.value = ''; notice.value = ''; probe = new window.AbortController()
  try {
    const result = await providerApi.test(snapshot, probe.signal)
    if (!alive) return
    if (saved.value?.id === snapshot.id && saved.value.connectionVersion === result.connectionVersion) saved.value = { ...saved.value, lastTest: result }
    else notice.value = `The test completed for connection version ${result.connectionVersion}. It does not verify the newer saved settings.`
  } catch (cause) { if (alive) error.value = message(cause) }
  finally { testing.value = false; probe = null; clearKey() }
}
function cancelTest() { probe?.abort() }
async function remove() {
  if (!saved.value || saving.value || testing.value || deleting.value) return
  if (!window.confirm(`Delete provider profile "${saved.value.name}" and its saved credential? Saved workflow references will block deletion.`)) return
  deleting.value = true; error.value = ''; clearKey()
  try {
    await providerApi.remove(saved.value)
    internalNavigation = true
    try { await router.push('/settings/connections') } finally { internalNavigation = false }
  } catch (cause) { if (alive) error.value = message(cause) }
  finally { deleting.value = false }
}
</script>
<template>
  <div class="list-page">
    <header class="app-header"><RouterLink to="/settings/connections" class="back-link">← Model connections</RouterLink><span class="header-divider"></span><RouterLink to="/" class="back-link">Workflows</RouterLink><span class="local-badge"><span></span> Local workspace</span></header>
    <main class="provider-editor">
      <div class="library-heading"><div><div class="eyebrow">SETTINGS · RESPONSES TEXT</div><h1>{{ saved ? saved.name : 'New model connection' }}</h1><p>{{ dirty ? 'Unsaved settings' : saved ? `Saved · revision ${saved.revision}` : 'Configure a provider profile' }}</p></div><span class="tag">M2</span></div>
      <div v-if="loading" class="loading-state" role="status">Loading profile…</div>
      <div v-if="error" role="alert" class="error-banner">{{ error }}</div>
      <div v-if="notice" role="status" class="notice-banner">{{ notice }}</div>
      <button v-if="loadFailed" @click="load(String(route.params.id))">Try again</button>
      <template v-if="!loading && !loadFailed">
        <form class="provider-form" @submit.prevent="save">
          <fieldset :disabled="saving || deleting">
            <div class="form-section"><h2>Connection settings</h2>
              <label>Profile name<input v-model="settings.name" aria-label="Profile name" maxlength="120" required autocomplete="off"></label>
              <label>Protocol<input value="OpenAI Responses (text, non-streaming)" aria-label="Protocol" readonly></label>
              <label>API base URL<input v-model="settings.baseUrl" aria-label="API base URL" maxlength="2048" placeholder="https://provider.example/v1" required autocomplete="off" spellcheck="false"></label>
              <p class="field-hint">Include the API prefix, such as /custom/v1. The backend appends /responses. Query strings, credentials in URLs, and full method URLs are rejected.</p>
              <div class="quiet-callout">Resolved endpoint: <code data-testid="resolved-endpoint">{{ endpoint ?? 'Enter a valid API base URL' }}</code></div>
              <label>Model ID<input v-model="settings.modelId" aria-label="Model ID" maxlength="200" required autocomplete="off" spellcheck="false"></label>
              <p class="field-hint">Use the exact model ID your provider supports. No model discovery request is made.</p>
              <div class="form-columns"><label>Timeout (seconds)<input v-model.number="settings.timeoutSeconds" aria-label="Timeout (seconds)" type="number" min="5" max="120" required></label><label>Maximum output tokens<input v-model.number="settings.maxOutputTokens" aria-label="Maximum output tokens" type="number" min="16" max="4096" required></label></div>
            </div>
            <div class="form-section"><h2>Authentication</h2>
              <label>Authentication<select v-model="settings.authMode" aria-label="Authentication"><option value="bearer">Bearer API key</option><option value="none">No authentication · approved local/private endpoint only</option></select></label>
              <p class="field-hint" data-testid="credential-status">{{ saved?.hasCredential ? 'Credential saved' : 'No credential saved' }}. Saved keys are never returned to the browser.</p>
              <label>Credential action<select v-model="credentialAction" aria-label="Credential action"><option value="keep">Keep existing credential</option><option v-if="settings.authMode === 'bearer'" value="replace">Replace / enter credential</option><option value="remove">Remove saved credential</option></select></label>
              <label v-if="credentialAction === 'replace'">API key<input v-model="apiKey" aria-label="API key" type="password" autocomplete="new-password" maxlength="8192" spellcheck="false" required></label>
              <p v-if="credentialAction === 'replace'" class="field-hint">Stored with Windows protection for this user. The field clears after any save attempt, session failure, or leaving this form. Never enter a masked placeholder.</p>
              <p v-if="credentialAction === 'remove'" class="warning-text field-hint">Saving will remove the credential. A bearer profile without a saved credential cannot be tested.</p>
            </div>
            <div class="form-section"><h2>Destination approval</h2><p class="field-hint">These approvals apply only to the exact destination shown above. Changing the base URL or authentication clears these acknowledgments. The backend checks the actual destination before connecting.</p>
              <label class="checkbox-label"><input v-model="settings.allowPrivateNetwork" aria-label="Allow this exact private or loopback destination" type="checkbox">I approve this exact destination for private-network or loopback access.</label>
              <label class="checkbox-label"><input v-model="settings.allowInsecureHttp" aria-label="Acknowledge unencrypted HTTP" type="checkbox">I acknowledge unencrypted HTTP for this approved private destination, including credential exposure on the network.</label>
              <p class="field-hint">HTTPS uses normal certificate verification. Redirects and the app's own API/frontend addresses are blocked. Environment proxies are not used.</p>
              <template v-if="destinationChanged"><div class="quiet-callout warning-text">Destination or authentication changed. The old credential will not be reused. Re-enter the key for bearer authentication.</div><label class="checkbox-label"><input v-model="confirmDestinationChange" aria-label="Confirm destination change" type="checkbox">I confirm this destination change and the credential selected above.</label></template>
            </div>
          </fieldset>
          <div class="form-actions"><button type="submit" class="primary" :disabled="saving || deleting || !localSession.authenticated || !dirty && !!saved">{{ saving ? 'Saving…' : 'Save profile' }}</button><span class="field-hint">Save only stores settings; it never sends inference.</span></div>
        </form>
        <section class="probe-section" aria-label="Connection probe"><h2>Test saved connection</h2><p>This sends the fixed prompt “Reply with GE_CONNECTION_OK.” to your saved provider and may incur usage charges. No workflow content is sent. The request asks for store: false; the provider's retention policy still applies.</p><p class="field-hint">{{ testHint }}</p><div class="form-actions"><button :disabled="testDisabled" @click="testConnection">{{ testing ? 'Testing…' : 'Test connection' }}</button><button v-if="testing" @click="cancelTest">Cancel test</button></div><p class="field-hint">Cancellation and timeout are best effort. The provider may still complete work or charge usage. A page refresh never retries a test.</p></section>
        <ConnectionResult v-if="saved?.lastTest" :result="saved.lastTest" :current-version="saved.connectionVersion" :dirty="dirty" />
        <section v-else class="connection-result"><h2>Text connection not tested</h2><p class="field-hint">Streaming, tools and JSON-schema output are not tested or implemented in M2. Workflow Run stays disabled until M3.</p></section>
        <div v-if="saved" class="delete-profile"><button class="danger-text" :disabled="saving || testing || deleting" @click="remove">{{ deleting ? 'Deleting…' : 'Delete profile' }}</button><p class="field-hint">Profiles referenced by saved workflows cannot be deleted. Removing a credential is logical deletion; SQLite pages and older backups may retain protected ciphertext.</p></div>
      </template>
    </main>
  </div>
</template>
