<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'
import { nodeLabels } from '../document'
import type { WorkflowNode } from '../document'
defineProps<{ data: { node: WorkflowNode }; selected?: boolean }>()
</script>
<template>
  <div class="graph-node" :class="[data.node.type, { selected }]" data-testid="workflow-node" :data-node-id="data.node.id">
    <Handle v-if="data.node.type !== 'start'" id="in" type="target" :position="Position.Left" :aria-label="`${data.node.name} input`" />
    <div class="node-topline"><span class="node-icon" aria-hidden="true">{{ data.node.type === 'start' ? '↗' : data.node.type === 'end' ? '■' : '✦' }}</span><span class="node-type">{{ nodeLabels[data.node.type] }}</span><span class="node-type-version">v{{ data.node.typeVersion }}</span></div>
    <strong>{{ data.node.name || 'Unnamed node' }}</strong><p>{{ data.node.description || 'Select to configure this node.' }}</p>
    <div v-if="data.node.type === 'modelCall' && !data.node.configuration.prompt.trim()" class="node-warning">△ Add a prompt</div>
    <Handle v-if="data.node.type !== 'end'" id="out" type="source" :position="Position.Right" :aria-label="`${data.node.name} output`" />
  </div>
</template>
