<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { checkSession, localSession, pairSession } from './session'

const token = ref(''), pending = ref(false), error = ref(''), unlocked = ref(false)
const tokenInput = ref<HTMLInputElement | null>(null)
let interval: number | undefined
watch(() => localSession.authenticated, async authenticated => {
  if (authenticated) unlocked.value = true
  else { token.value = ''; await nextTick(); tokenInput.value?.focus() }
}, { immediate: true })
async function pair() {
  if (pending.value || !token.value.trim()) return
  pending.value = true; error.value = ''
  const entered = token.value
  token.value = ''
  try { await pairSession(entered) }
  catch (cause) { error.value = cause instanceof Error && (cause.message.startsWith('Pairing') || cause.message.startsWith('Too many')) ? cause.message : 'Unable to pair. Check that the local backend is running and try again.' }
  finally { pending.value = false }
}
function focusCheck() { void checkSession() }
onMounted(() => {
  void checkSession()
  window.addEventListener('focus', focusCheck)
  interval = window.setInterval(() => { if (localSession.authenticated) void checkSession() }, 60_000)
})
onUnmounted(() => { token.value = ''; window.removeEventListener('focus', focusCheck); window.clearInterval(interval) })
</script>
<template>
  <div v-if="unlocked" :inert="!localSession.authenticated || undefined"><slot /></div>
  <div v-if="!localSession.authenticated" class="pairing-overlay" role="dialog" aria-modal="true" aria-labelledby="pairing-title">
    <form class="pairing-card" @submit.prevent="pair">
      <span class="brand-mark">⌘</span><div class="eyebrow">GRAPH ENGINEERING · LOCAL ACCESS</div>
      <h1 id="pairing-title">{{ unlocked ? 'Pair again to continue' : 'Pair this browser' }}</h1>
      <p>Open the pairing-token.txt file at the runtime path shown by the backend, then enter its current contents here. The token changes when the backend restarts.</p>
      <p v-if="unlocked" class="quiet-callout">Your unsaved workflow and profile settings are still here. Transient key fields are cleared. A connection test will never restart automatically.</p>
      <div v-if="error || localSession.error" role="alert" class="error-banner">{{ error || localSession.error }}</div>
      <label>Pairing token<input ref="tokenInput" v-model="token" aria-label="Pairing token" type="password" autocomplete="off" maxlength="256" :disabled="pending" spellcheck="false" autofocus></label>
      <button class="primary" :disabled="pending || !token.trim()">{{ pending ? 'Pairing…' : 'Pair local browser' }}</button>
      <p class="field-hint">Use this computer's local token only. Do not paste provider credentials here or into chat.</p>
    </form>
  </div>
</template>
