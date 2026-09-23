/* eslint-disable vue/one-component-per-file -- Local component doubles isolate the camera integration. */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useVueFlow } from '@vue-flow/core'
import GraphCanvas from '../components/GraphCanvas.vue'
import { useWorkflowStore } from '../store'

// These camera adapters deliberately do not emit viewportChangeEnd. Vue Flow
// has the same behavior for controls, fitView, and minimap camera operations.
vi.mock('@vue-flow/core', async () => {
  const { defineComponent, h, ref } = await import('vue')
  const viewport = ref({ x: 0, y: 0, zoom: 1 })
  const flow = {
    viewport,
    viewportRef: ref(null),
    zoomIn: async () => { viewport.value = { ...viewport.value, zoom: viewport.value.zoom * 1.2 } },
    zoomOut: async () => { viewport.value = { ...viewport.value, zoom: viewport.value.zoom / 1.2 } },
    fitView: async () => { viewport.value = { x: 35, y: 50, zoom: 0.65 } },
  }
  return {
    MarkerType: { ArrowClosed: 'arrowclosed' },
    useVueFlow: () => flow,
    VueFlow: defineComponent({ name: 'VueFlow', emits: ['init'], setup(_props, { slots }) { return () => h('div', slots.default?.()) } }),
  }
})
vi.mock('@vue-flow/controls', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    Controls: defineComponent({ setup(_props, { slots }) { return () => h('div', Object.values(slots).flatMap(slot => slot?.())) } }),
    ControlButton: defineComponent({ setup(_props, { slots }) { return () => h('button', slots.default?.()) } }),
  }
})
vi.mock('@vue-flow/background', () => ({ Background: { template: '<div />' } }))
vi.mock('@vue-flow/minimap', () => ({ MiniMap: { template: '<div />' } }))
vi.mock('../components/WorkflowNode.vue', () => ({ default: { template: '<div />' } }))

beforeEach(() => { setActivePinia(createPinia()); useVueFlow().viewport.value = { x: 0, y: 0, zoom: 1 } })
describe('canvas camera persistence', () => {
  it('keeps persisted initialization clean and saves control camera changes without end events', async () => {
    const store = useWorkflowStore(); store.createNew(); store.persisted = true; store.dirty = false
    const initial = { ...store.document!.layout.viewport }
    const wrapper = mount(GraphCanvas)
    const flow = useVueFlow()
    flow.viewport.value = initial
    expect(store.dirty).toBe(false)
    expect(store.document!.layout.viewport).toEqual(initial)
    wrapper.findComponent({ name: 'VueFlow' }).vm.$emit('init')
    await wrapper.get('[aria-label="Zoom out"]').trigger('click')
    expect(store.dirty).toBe(true)
    expect(store.document!.layout.viewport.zoom).toBeCloseTo(initial.zoom / 1.2)
    await wrapper.get('[aria-label="Fit view"]').trigger('click')
    expect(store.document!.layout.viewport).toEqual({ x: 35, y: 50, zoom: 0.65 })
    wrapper.unmount()
  })
  it('persists minimap camera updates and validation focus without an end event', () => {
    const store = useWorkflowStore(); store.createNew(); store.persisted = true; store.dirty = false
    const wrapper = mount(GraphCanvas)
    wrapper.findComponent({ name: 'VueFlow' }).vm.$emit('init')
    const flow = useVueFlow()
    flow.viewport.value = { x: -160, y: 80, zoom: 0.7 }
    expect(store.document!.layout.viewport).toEqual({ x: -160, y: 80, zoom: 0.7 })
    expect(store.dirty).toBe(true)
    store.dirty = false
    wrapper.vm.focusNode(store.document!.definition.nodes[0]!.id)
    expect(store.document!.layout.viewport).toEqual({ x: 35, y: 50, zoom: 0.65 })
    expect(store.dirty).toBe(true)
    wrapper.unmount()
  })
})
