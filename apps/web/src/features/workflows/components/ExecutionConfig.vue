<script setup lang="ts">
import { computed } from 'vue'
import { useWorkflowStore } from '../store'
import type { BindingSource, WorkflowNode } from '../document'
import { earlierModelNodes } from '../bindings'
import BindingSourceEditor from './BindingSourceEditor.vue'
const props = defineProps<{ node: WorkflowNode }>()
const store = useWorkflowStore()
const earlier = computed(() => store.document ? earlierModelNodes(store.document, props.node.id) : [])
function setMode(event: Event) { store.updateNode(props.node.id, node => { if (node.type === 'modelCall' && node.typeVersion === 2) node.configuration.promptMode = (event.target as HTMLInputElement).value as 'literal' | 'bindings' }) }
function setOutput(event: Event) { store.updateNode(props.node.id, node => { if (node.type === 'modelCall' && node.typeVersion === 2) node.configuration.outputMode = (event.target as HTMLInputElement).value as 'text' | 'jsonObject' }) }
function alias(index: number, event: Event) { store.updateNode(props.node.id, node => { if (node.type === 'modelCall' && node.typeVersion === 2) node.configuration.inputBindings[index]!.alias = (event.target as HTMLInputElement).value }) }
function source(index: number, value: BindingSource | null) { if (value) store.updateNode(props.node.id, node => { if (node.type === 'modelCall' && node.typeVersion === 2) node.configuration.inputBindings[index]!.source = value }) }
function add() { store.updateNode(props.node.id, node => { if (node.type === 'modelCall' && node.typeVersion === 2 && node.configuration.inputBindings.length < 32) node.configuration.inputBindings.push({ alias: '', source: { kind: 'runInput', pointer: '' } }) }) }
function remove(index: number) { store.updateNode(props.node.id, node => { if (node.type === 'modelCall' && node.typeVersion === 2) node.configuration.inputBindings.splice(index, 1) }) }
function result(value: BindingSource | null) { store.updateNode(props.node.id, node => { if (node.type === 'end' && node.typeVersion === 2) node.configuration.resultBinding = value }) }
</script>
<template>
  <div v-if="node.type !== 'start'" class="execution-config">
    <template v-if="node.typeVersion === 1"><p class="field-hint">Legacy version 1 preserves its original text. Upgrade this node explicitly to configure execution; the prompt stays literal.</p><button @click="store.upgradeNode(node.id)">Upgrade for execution</button></template>
    <template v-else-if="node.type === 'modelCall'">
      <label>Prompt mode<select aria-label="Prompt mode" :value="node.configuration.promptMode" @change="setMode"><option value="literal">Literal text</option><option value="bindings">Explicit bindings</option></select></label>
      <p v-if="node.configuration.promptMode === 'literal'" class="field-hint">The prompt is sent unchanged, including braces. Remove any bindings before running in literal mode.</p>
      <p v-else class="field-hint">Use <code v-pre>{{inputs.alias}}</code> to insert only the selected value. Connections determine order; they never transfer data.</p>
      <div v-for="(binding, index) in node.configuration.inputBindings" :key="index" class="binding-entry">
        <label>Binding alias<input :aria-label="`Binding ${index + 1} alias`" :value="binding.alias" maxlength="64" placeholder="value" @input="alias(index, $event)"></label>
        <p v-if="!/^[_a-zA-Z][_a-zA-Z0-9]{0,63}$/.test(binding.alias)" class="field-hint warning-text">Use an ASCII name starting with a letter or underscore.</p>
        <BindingSourceEditor :label="`Binding ${index + 1}`" :model-value="binding.source" :nodes="earlier" @update:model-value="source(index, $event)" />
        <button :aria-label="`Remove binding ${index + 1}`" @click="remove(index)">Remove binding</button>
      </div>
      <button :disabled="node.configuration.inputBindings.length >= 32" @click="add">Add input binding</button>
      <label>Output interpretation<select aria-label="Output interpretation" :value="node.configuration.outputMode" @change="setOutput"><option value="text">Text</option><option value="jsonObject">JSON object — local validation</option></select></label>
      <p v-if="node.configuration.outputMode === 'jsonObject'" class="field-hint">Requires one strict JSON object. Invalid output stops the run; no repair or retry. This does not constrain provider generation.</p>
    </template>
    <template v-else-if="node.type === 'end'"><BindingSourceEditor label="Final result" :model-value="node.configuration.resultBinding" :nodes="earlier" allow-empty @update:model-value="result" /><p class="field-hint">This explicit selection becomes the typed final result. No model call occurs at End.</p></template>
  </div>
</template>
