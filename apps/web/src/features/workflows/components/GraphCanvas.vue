<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { VueFlow, useVueFlow, MarkerType } from '@vue-flow/core'
import type { Connection, Edge, EdgeUpdateEvent, Node, NodeDragEvent } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls, ControlButton } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import WorkflowNodeCard from './WorkflowNode.vue'
import { isTextEntry, useWorkflowStore } from '../store'
import type { NodeType } from '../document'
const store = useWorkflowStore()
const flow = useVueFlow('workflow-canvas')
let viewportInitialized = false
function initializeViewport() { viewportInitialized = true }
const nodes = computed<Node[]>(() => store.document?.definition.nodes.map(node => {
  const position = store.document!.layout.nodes.find(position => position.nodeId === node.id)!
  return { id: node.id, type: 'workflow', position: { x: position.x, y: position.y }, data: { node }, selected: store.selection?.kind === 'node' && store.selection.id === node.id }
}) ?? [])
const edges = computed<Edge[]>(() => store.document?.definition.edges.map(edge => ({
  id: edge.id, source: edge.sourceNodeId, sourceHandle: edge.sourcePort, target: edge.targetNodeId, targetHandle: edge.targetPort,
  type: 'smoothstep', updatable: true, selected: store.selection?.kind === 'edge' && store.selection.id === edge.id,
  markerEnd: MarkerType.ArrowClosed, label: undefined,
})) ?? [])
function connect(connection: Connection) { if (connection.source && connection.target) store.connect(connection.source, connection.target) }
function reconnect(event: EdgeUpdateEvent) { if (event.connection.source && event.connection.target) store.connect(event.connection.source, event.connection.target, event.edge.id) }
function moved(event: NodeDragEvent) { store.moveNodes(event.nodes.map(node => ({ id: node.id, ...node.position }))) }
function keydown(event: KeyboardEvent) {
  if (isTextEntry(event.target) || event.ctrlKey || event.metaKey || event.altKey) return
  if ((event.key === 'Delete' || event.key === 'Backspace') && store.selection) { event.preventDefault(); store.deleteSelection() }
}
function focusNode(id: string) { store.selectNode(id); void flow.fitView({ nodes: [id], padding: 1.5, maxZoom: 1.1, duration: 0 }) }
function focusEdge(id: string) {
  const edge = store.document?.definition.edges.find(edge => edge.id === id)
  store.selectEdge(id)
  if (edge) void flow.fitView({ nodes: [edge.sourceNodeId, edge.targetNodeId], padding: 0.4, maxZoom: 1, duration: 0 })
}
function addNode(type: NodeType) {
  const bounds = flow.viewportRef.value?.getBoundingClientRect()
  const position = bounds ? flow.screenToFlowCoordinate({ x: bounds.x + bounds.width / 2 - 105, y: bounds.y + bounds.height / 2 }) : undefined
  store.addNode(type, position)
}
// Vue Flow omits viewportChangeEnd for controls, fitView, and minimap gestures.
// Observe the camera itself, after its initial persisted layout has been restored.
watch(
  () => ({ ...flow.viewport.value }),
  viewport => { if (viewportInitialized) store.setViewport(viewport) },
  { flush: 'sync' },
)
onMounted(() => window.addEventListener('keydown', keydown))
onUnmounted(() => window.removeEventListener('keydown', keydown))
defineExpose({ focusNode, focusEdge, addNode, fit: () => flow.fitView({ padding: 0.2, duration: 0 }) })
</script>
<template>
  <section class="canvas-region" aria-label="Workflow graph">
    <div class="canvas-caption"><span class="status-dot"></span> DESIGN CANVAS <span>{{ nodes.length }} nodes · {{ edges.length }} connections</span></div>
    <VueFlow id="workflow-canvas" :nodes="nodes" :edges="edges" :default-viewport="store.document?.layout.viewport" :min-zoom="0.1" :max-zoom="4" :delete-key-code="null" :edges-updatable="true" :connection-radius="28" :snap-to-grid="true" :snap-grid="[10, 10]" :is-valid-connection="connection => connection.sourceHandle === 'out' && connection.targetHandle === 'in'" @init="initializeViewport" @node-click="store.selectNode($event.node.id)" @edge-click="store.selectEdge($event.edge.id)" @pane-click="store.selection = null" @connect="connect" @edge-update="reconnect" @node-drag-stop="moved" @selection-drag-stop="moved">
      <Background pattern-color="#35393e" :gap="20" :size="1" />
      <Controls position="bottom-left" :show-interactive="false">
        <template #control-zoom-in><ControlButton class="vue-flow__controls-zoomin" aria-label="Zoom in" title="Zoom in" :disabled="flow.viewport.value.zoom >= 4" @click="flow.zoomIn()">＋</ControlButton></template>
        <template #control-zoom-out><ControlButton class="vue-flow__controls-zoomout" aria-label="Zoom out" title="Zoom out" :disabled="flow.viewport.value.zoom <= 0.1" @click="flow.zoomOut()">−</ControlButton></template>
        <template #control-fit-view><ControlButton class="vue-flow__controls-fitview" aria-label="Fit view" title="Fit view" @click="flow.fitView({ padding: 0.2, duration: 0 })">⛶</ControlButton></template>
      </Controls>
      <MiniMap position="bottom-right" :pannable="true" :zoomable="true" :node-color="'#626975'" :mask-color="'rgba(17,19,21,0.7)'" />
      <template #node-workflow="props"><WorkflowNodeCard v-bind="props" /></template>
    </VueFlow>
    <div v-if="nodes.length === 0" class="canvas-empty">Add a node from the library to get started.</div>
    <div class="canvas-help">Drag to arrange <span>·</span> Connect the ports <span>·</span> Delete to remove</div>
  </section>
</template>
