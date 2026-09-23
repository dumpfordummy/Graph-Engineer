<script setup lang="ts">
import { computed } from 'vue'
import { Handle, Position, VueFlow, type Node, type Edge } from '@vue-flow/core'
import type { RunDetail } from './contracts'
const props = defineProps<{ run: RunDetail; selectedNodeId: string }>()
const emit = defineEmits<{ select: [nodeId: string] }>()
const nodes = computed<Node[]>(() => props.run.snapshot.definition.nodes.map(node => {
  const position = props.run.snapshot.layout.nodes.find(entry => entry.nodeId === node.id)
  return { id: node.id, type: 'default', position: { x: position?.x ?? 0, y: position?.y ?? 0 }, data: { name: node.name, kind: node.type, state: props.run.nodes.find(entry => entry.nodeId === node.id)?.state ?? 'Pending' }, selected: node.id === props.selectedNodeId, draggable: false, connectable: false }
}))
const edges = computed<Edge[]>(() => props.run.snapshot.definition.edges.map(edge => ({ id: edge.id, source: edge.sourceNodeId, target: edge.targetNodeId, updatable: false, selectable: false })))
</script>
<template>
  <section class="frozen-graph" aria-label="Frozen workflow graph">
    <div class="frozen-graph-heading"><h2>Frozen workflow graph</h2><span>Saved layout · select a node to inspect its attempt</span></div>
    <VueFlow :id="`frozen-${run.id}`" :nodes="nodes" :edges="edges" :nodes-draggable="false" :nodes-connectable="false" :edges-updatable="false" :delete-key-code="null" :zoom-on-scroll="false" :zoom-on-double-click="false" :min-zoom="0.15" :max-zoom="2" fit-view-on-init @node-click="emit('select', $event.node.id)">
      <template #node-default="{ id, data }"><Handle v-if="data.kind !== 'start'" type="target" :position="Position.Left" :connectable="false" /><button class="frozen-node" :aria-label="`Inspect frozen node ${data.name}`" :aria-pressed="selectedNodeId === id" @click="emit('select', id)"><strong>{{ data.name }}</strong><span>{{ data.state }}</span></button><Handle v-if="data.kind !== 'end'" type="source" :position="Position.Right" :connectable="false" /></template>
    </VueFlow>
  </section>
</template>
