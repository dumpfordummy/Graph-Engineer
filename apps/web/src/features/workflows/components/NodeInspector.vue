<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useWorkflowStore, edgeDescription } from '../store'
import { nodeLabels } from '../document'
const store = useWorkflowStore()
const source = ref(''), target = ref(''), connectTarget = ref('')
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
        <template v-if="store.selectedNode.type === 'start'"><label>Sample input<textarea aria-label="Sample input" :value="store.selectedNode.configuration.sampleInput" maxlength="50000" rows="7" placeholder="Example text for this workflow" @input="configField('sampleInput', $event)"></textarea></label><p class="field-hint">Sample text is stored with the draft.</p></template>
        <template v-if="store.selectedNode.type === 'modelCall'"><label>Prompt<textarea aria-label="Prompt" :value="store.selectedNode.configuration.prompt" maxlength="50000" rows="7" placeholder="Describe the task for this model step…" @input="configField('prompt', $event)"></textarea></label><label>Future provider profile ID <span class="optional">optional</span><input aria-label="Future provider profile ID" :value="store.selectedNode.configuration.providerProfileId ?? ''" maxlength="120" placeholder="No profile selected" @input="configField('providerProfileId', $event)"></label><p class="field-hint">Reference only. Provider setup and connectivity are outside M1. No prompt is sent.</p></template>
        <template v-if="store.selectedNode.type === 'end'"><label>Result reference<textarea aria-label="Result reference" :value="store.selectedNode.configuration.resultReference" maxlength="2000" rows="5" placeholder="Describe the intended result" @input="configField('resultReference', $event)"></textarea></label><p class="field-hint">Descriptive text only. No expression is evaluated.</p></template>
      </div>
      <div v-if="store.selectedNode.type !== 'end'" class="inspector-section"><div class="eyebrow">OUTGOING CONNECTIONS</div><button v-for="edge in outgoing" :key="edge.id" class="connection-item" @click="emit('focusEdge', edge.id)">{{ edgeDescription(edge, store.document!.definition.nodes) }}</button><p v-if="!outgoing.length" class="field-hint">No outgoing connection.</p><label>Connect to<select v-model="connectTarget" aria-label="Connect to"><option value="">Choose a node</option><option v-for="node in targets" :key="node.id" :value="node.id">{{ node.name || 'Unnamed node' }} · {{ node.id.slice(0, 6) }}</option></select></label><button :disabled="!connectTarget" @click="store.connect(store.selectedNode.id, connectTarget)">Add connection</button></div>
      <div class="inspector-section"><button class="danger-text" @click="store.deleteSelection">Delete node</button><p class="field-hint mono">{{ store.selectedNode.id }}</p></div>
    </div>
    <div v-else-if="store.selectedEdge" class="inspector-body"><div class="inspector-section"><h3>Control connection</h3><p class="field-hint">Connections describe order. They do not copy conversation or data.</p><label>Source node<select v-model="source" aria-label="Source node"><option v-for="node in sources" :key="node.id" :value="node.id">{{ node.name || 'Unnamed node' }} · {{ node.id.slice(0, 6) }}</option></select></label><label>Target node<select v-model="target" aria-label="Target node"><option v-for="node in targets" :key="node.id" :value="node.id">{{ node.name || 'Unnamed node' }} · {{ node.id.slice(0, 6) }}</option></select></label><button @click="store.connect(source, target, store.selectedEdge.id)">Reconnect edge</button><p class="field-hint">You can also drag either endpoint on the canvas.</p></div><div class="inspector-section"><button class="danger-text" @click="store.deleteSelection">Delete edge</button></div></div>
    <div v-else class="inspector-body"><div class="inspector-section"><h3>Your workflow, step by step</h3><p class="field-hint">Select a node to edit its instructions, or a connection to change its direction.</p><label>Workflow description<textarea aria-label="Workflow description" :value="store.document?.workflow.description" maxlength="2000" rows="5" placeholder="What are you building?" @input="store.setMetadata('description', value($event))"></textarea></label></div><div class="inspector-section"><div class="eyebrow">DESIGN BOUNDARY</div><p class="field-hint">This editor saves a draft definition and its layout. Validation checks structure and draft configuration.</p><div class="quiet-callout">Execution is unavailable in M1.</div></div></div>
  </aside>
</template>
