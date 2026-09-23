<script setup lang="ts">
import { computed } from 'vue'
import type { BindingSource, WorkflowNode } from '../document'
import { pointerError } from '../bindings'
const props = defineProps<{ modelValue: BindingSource | null; nodes: WorkflowNode[]; label: string; allowEmpty?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [source: BindingSource | null] }>()
const choices = computed(() => props.nodes.filter(node => props.modelValue?.kind !== 'nodeJson' || node.type === 'modelCall' && node.typeVersion === 2 && node.configuration.outputMode === 'jsonObject'))
const missing = computed(() => props.modelValue && props.modelValue.kind !== 'runInput' && !choices.value.some(node => node.id === (props.modelValue as { nodeId: string }).nodeId))
function setKind(event: Event) {
  const kind = (event.target as HTMLInputElement).value
  emit('update:modelValue', kind === '' ? null : kind === 'runInput' ? { kind, pointer: '' } : kind === 'nodeText' ? { kind, nodeId: '' } : { kind: 'nodeJson', nodeId: '', pointer: '' })
}
function setPointer(event: Event) { if (props.modelValue && props.modelValue.kind !== 'nodeText') emit('update:modelValue', { ...props.modelValue, pointer: (event.target as HTMLInputElement).value }) }
function setNode(event: Event) { if (props.modelValue && props.modelValue.kind !== 'runInput') emit('update:modelValue', { ...props.modelValue, nodeId: (event.target as HTMLInputElement).value }) }
</script>
<template>
  <div class="binding-source">
    <label>{{ label }} source<select :aria-label="`${label} source`" :value="modelValue?.kind ?? ''" @change="setKind"><option v-if="allowEmpty" value="">Choose an explicit result</option><option value="runInput">Run input</option><option value="nodeText">Earlier node text</option><option value="nodeJson">Earlier node JSON</option></select></label>
    <template v-if="modelValue && modelValue.kind !== 'runInput'"><label>{{ label }} node<select :aria-label="`${label} node`" :value="modelValue.nodeId" @change="setNode"><option value="">Choose an earlier node</option><option v-if="missing && modelValue.nodeId" :value="modelValue.nodeId">Unresolved · {{ modelValue.nodeId }}</option><option v-for="node in choices" :key="node.id" :value="node.id">{{ node.name || 'Unnamed node' }} · {{ node.id.slice(0, 6) }}</option></select></label><p v-if="missing && modelValue.nodeId" class="field-hint warning-text">This source is missing, later in the control path, or has an incompatible output mode. Choose an eligible earlier node.</p></template>
    <template v-if="modelValue && modelValue.kind !== 'nodeText'"><label>{{ label }} JSON pointer<input :aria-label="`${label} JSON pointer`" :value="modelValue.pointer" maxlength="2000" placeholder="/field (empty = whole value)" @input="setPointer"></label><p v-if="pointerError(modelValue.pointer)" class="field-hint warning-text">{{ pointerError(modelValue.pointer) }}</p></template>
  </div>
</template>
