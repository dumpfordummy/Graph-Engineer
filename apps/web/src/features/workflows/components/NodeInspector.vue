<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useWorkflowStore, edgeDescription } from '../store'
import { nodeLabels } from '../document'
import { providerApi } from '../../providers/api'
import { connectionReadiness, type ProviderProfile } from '../../providers/contracts'
import { localSession } from '../../session/session'
import ExecutionConfig from './ExecutionConfig.vue'
const store = useWorkflowStore()
const source = ref(''), target = ref(''), connectTarget = ref('')
const profiles = ref<ProviderProfile[]>([]), profilesError = ref(''), profilesLoading = ref(false)
const selectedProfileId = computed(() => store.selectedNode?.type === 'modelCall' ? store.selectedNode.configuration.providerProfileId : null)
const selectedProfile = computed(() => profiles.value.find(profile => profile.id === selectedProfileId.value))
async function refreshProfiles() {
  profilesLoading.value = true; profilesError.value = ''
  try { profiles.value = await providerApi.list() }
  catch { profilesError.value = 'Provider profiles are unavailable. The stored reference is preserved.' }
  finally { profilesLoading.value = false }
}
onMounted(refreshProfiles)
watch(() => localSession.authenticated, authenticated => { if (authenticated) void refreshProfiles() })
const emit = defineEmits<{ focusEdge: [id: string] }>()
watch(
  () => [store.selectedEdge?.id, store.selectedEdge?.sourceNodeId, store.selectedEdge?.targetNodeId],
  () => { source.value = store.selectedEdge?.sourceNodeId ?? ''; target.value = store.selectedEdge?.targetNodeId ?? '' },
  { immediate: true },
)
watch(() => store.selectedNode?.id, () => { connectTarget.value = '' })
const outgoing = computed(() => store.document?.definition.edges.filter(edge => edge.sourceNodeId === store.selectedNode?.id) ?? [])
const sources = computed(() => store.document?.definition.nodes.filter(node => node.type !== 'end') ?? [])
const targets = computed(() => store.document?.definition.nodes.filter(node => node.type !== 'start') ?? [])
function value(event: Event) { return (event.target as HTMLInputElement).value }
function baseField(key: 'name' | 'description', event: Event) { if (store.selectedNode) store.updateNode(store.selectedNode.id, node => { node[key] = value(event) }) }
function configField(key: 'sampleInput' | 'prompt' | 'providerProfileId' | 'resultReference', event: Event) {
  if (store.selectedNode) store.updateNode(store.selectedNode.id, node => {
    const content = value(event)
    if (node.type === 'start' && key === 'sampleInput') node.configuration.sampleInput = content
    if (node.type === 'modelCall' && key === 'prompt') node.configuration.prompt = content
    if (node.type === 'modelCall' && key === 'providerProfileId') node.configuration.providerProfileId = content || null
    if (node.type === 'end' && key === 'resultReference') node.configuration.resultReference = content
  })
}
</script>
<template>
  <aside class="inspector" aria-label="Inspector">
    <div class="panel-heading"><span>Inspector</span><span class="eyebrow">{{ store.selectedNode ? nodeLabels[store.selectedNode.type] : store.selectedEdge ? 'CONNECTION' : 'WORKFLOW' }}</span></div>
    <div v-if="store.selectedNode" class="inspector-body">
      <div class="inspector-section"><div class="eyebrow">OVERVIEW</div><label>Node name<input aria-label="Node name" :value="store.selectedNode.name" maxlength="120" @input="baseField('name', $event)"></label><label>Node description<textarea aria-label="Node description" :value="store.selectedNode.description" maxlength="2000" rows="2" placeholder="What does this step do?" @input="baseField('description', $event)"></textarea></label></div>
      <div class="inspector-section"><div class="eyebrow">CONFIGURATION</div>
        <template v-if="store.selectedNode.type === 'start'"><label>Sample input<textarea aria-label="Sample input" :value="store.selectedNode.configuration.sampleInput" maxlength="50000" rows="7" placeholder="Example text for this workflow" @input="configField('sampleInput', $event)"></textarea></label><p class="field-hint">Sample text is stored with the draft. Actual run input is entered separately in the Run dialog.</p></template>
        <template v-if="store.selectedNode.type === 'modelCall'"><label>Prompt<textarea aria-label="Prompt" :value="store.selectedNode.configuration.prompt" maxlength="50000" rows="7" placeholder="Describe the task for this model step…" @input="configField('prompt', $event)"></textarea></label><label>Provider profile <span class="optional">optional</span><select aria-label="Provider profile" :value="selectedProfileId ?? ''" :disabled="profilesLoading" @change="configField('providerProfileId', $event)"><option value="">No profile selected</option><option v-if="selectedProfileId && !selectedProfile" :value="selectedProfileId">Unresolved · {{ selectedProfileId }}</option><option v-for="profile in profiles" :key="profile.id" :value="profile.id">{{ profile.name }}</option></select></label><p v-if="profilesError" class="field-hint warning-text">{{ profilesError }} <button @click="refreshProfiles">Retry</button></p><div v-if="selectedProfile" class="quiet-callout" data-testid="provider-readiness"><strong>{{ selectedProfile.modelId }}</strong><br>OpenAI Responses · text<br>{{ connectionReadiness(selectedProfile) }}</div><p v-else class="field-hint warning-text">{{ selectedProfileId ? 'Unresolved profile reference. This draft can still be saved; select a configured profile when ready.' : 'No provider selected. This draft can still be saved.' }}</p><p class="field-hint">Provider readiness is separate from graph validation. No prompt is sent by selection or validation. Run requires a saved, executable graph and an explicit confirmation.</p><RouterLink to="/settings/connections" class="button connection-settings-link">Model connections</RouterLink></template>
        <template v-if="store.selectedNode.type === 'end'"><label>Result reference<textarea aria-label="Result reference" :value="store.selectedNode.configuration.resultReference" maxlength="2000" rows="5" placeholder="Describe the intended result" @input="configField('resultReference', $event)"></textarea></label><p class="field-hint">Descriptive text only. No expression is evaluated.</p></template>
        <ExecutionConfig :node="store.selectedNode" />
      </div>
      <div v-if="store.selectedNode.type !== 'end'" class="inspector-section"><div class="eyebrow">OUTGOING CONNECTIONS</div><button v-for="edge in outgoing" :key="edge.id" class="connection-item" @click="emit('focusEdge', edge.id)">{{ edgeDescription(edge, store.document!.definition.nodes) }}</button><p v-if="!outgoing.length" class="field-hint">No outgoing connection.</p><label>Connect to<select v-model="connectTarget" aria-label="Connect to"><option value="">Choose a node</option><option v-for="node in targets" :key="node.id" :value="node.id">{{ node.name || 'Unnamed node' }} · {{ node.id.slice(0, 6) }}</option></select></label><button :disabled="!connectTarget" @click="store.connect(store.selectedNode.id, connectTarget)">Add connection</button></div>
      <div class="inspector-section"><button class="danger-text" @click="store.deleteSelection">Delete node</button><p class="field-hint mono">{{ store.selectedNode.id }}</p></div>
    </div>
    <div v-else-if="store.selectedEdge" class="inspector-body"><div class="inspector-section"><h3>Control connection</h3><p class="field-hint">Connections describe order. They do not copy conversation or data.</p><label>Source node<select v-model="source" aria-label="Source node"><option v-for="node in sources" :key="node.id" :value="node.id">{{ node.name || 'Unnamed node' }} · {{ node.id.slice(0, 6) }}</option></select></label><label>Target node<select v-model="target" aria-label="Target node"><option v-for="node in targets" :key="node.id" :value="node.id">{{ node.name || 'Unnamed node' }} · {{ node.id.slice(0, 6) }}</option></select></label><button @click="store.connect(source, target, store.selectedEdge.id)">Reconnect edge</button><p class="field-hint">You can also drag either endpoint on the canvas.</p></div><div class="inspector-section"><button class="danger-text" @click="store.deleteSelection">Delete edge</button></div></div>
    <div v-else class="inspector-body"><div class="inspector-section"><h3>Your workflow, step by step</h3><p class="field-hint">Select a node to edit its instructions, or a connection to change its direction.</p><label>Workflow description<textarea aria-label="Workflow description" :value="store.document?.workflow.description" maxlength="2000" rows="5" placeholder="What are you building?" @input="store.setMetadata('description', value($event))"></textarea></label></div><div class="inspector-section"><div class="eyebrow">DESIGN BOUNDARY</div><p class="field-hint">This editor saves a draft definition and its layout. Validation checks structure and draft configuration.</p><div class="quiet-callout">Configure explicit bindings, save, and review readiness before running.</div></div></div>
  </aside>
</template>
