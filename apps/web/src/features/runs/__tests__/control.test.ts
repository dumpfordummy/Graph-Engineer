import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import RunControl from '../RunControl.vue'
import { useWorkflowStore } from '../../workflows/store'
import { localSession } from '../../session/session'
import { runApi } from '../api'
import { ApiError } from '../../api'
import type { Readiness, RunDetail } from '../contracts'
vi.mock('../api', async importOriginal => ({ ...await importOriginal<typeof import('../api')>(), runApi: { readiness: vi.fn(), submit: vi.fn(), submission: vi.fn() } }))
beforeEach(() => {
  vi.clearAllMocks(); localSession.authenticated = true; localSession.csrfToken = 'synthetic-csrf'
  Object.defineProperty(globalThis.HTMLDialogElement.prototype, 'showModal', { configurable: true, value() { this.setAttribute('open', '') } })
  Object.defineProperty(globalThis.HTMLDialogElement.prototype, 'close', { configurable: true, value() { this.removeAttribute('open') } })
})
afterEach(() => { document.body.innerHTML = '' })
async function setup(query = '') {
  const pinia = createPinia(); setActivePinia(pinia)
  const store = useWorkflowStore(); store.createNew(); store.persisted = true; store.dirty = false; store.document!.workflow.revision = 3
  const ready: Readiness = { ready: true, issues: [], maximumModelCalls: 1, orderedNodes: store.document!.definition.nodes.map(node => ({ nodeId: node.id, name: node.name, type: node.type, providerProfileId: node.type === 'modelCall' ? 'profile' : null, profileName: node.type === 'modelCall' ? 'Fixture profile' : null, modelId: node.type === 'modelCall' ? 'fixture-model' : null, connectionVersion: node.type === 'modelCall' ? 4 : null })) }
  vi.mocked(runApi.readiness).mockResolvedValue(ready)
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/workflows/:id', component: { template: '<div />' } }, { path: '/runs/:id?', component: { template: '<div />' } }] })
  await router.push(`/workflows/${store.document!.workflow.id}${query}`); await router.isReady()
  const wrapper = mount(RunControl, { attachTo: document.body, global: { plugins: [pinia, router], stubs: { Teleport: true } } })
  await flushPromises()
  return { store, router, wrapper, ready }
}
describe('deliberate run admission UI', () => {
  it('disables Run for unsaved edits and requires explicit retention/provider approval', async () => {
    const { store, wrapper } = await setup()
    expect(wrapper.get('[aria-label="Run workflow"]').attributes('disabled')).toBeUndefined()
    await wrapper.get('[aria-label="Run workflow"]').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('saved revision 3')
    expect(wrapper.text()).toContain('Fixture profile · fixture-model · connection version 4')
    expect(wrapper.text()).toContain('Maximum 1 model calls')
    expect(wrapper.text()).toContain('without encryption')
    expect(wrapper.findAll('button').find(button => button.text() === 'Confirm and run')!.attributes('disabled')).toBeDefined()
    store.setMetadata('name', 'An unsaved edit'); await flushPromises()
    expect(wrapper.get('[aria-label="Run workflow"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Save this workflow before running.')
    expect(runApi.submit).not.toHaveBeenCalled()
    wrapper.unmount()
  })
  it('retains one submission ID after a lost response and only reconciles on explicit lookup', async () => {
    const { wrapper, router } = await setup()
    vi.mocked(runApi.submit).mockRejectedValue(new ApiError(0, 'The response was lost.'))
    vi.mocked(runApi.submission).mockResolvedValue({ id: 'existing-run' } as RunDetail)
    await wrapper.get('[aria-label="Run workflow"]').trigger('click'); await flushPromises()
    const rawInput = '{"x":900719925474099312345}'
    await wrapper.get('[aria-label="Run input JSON object"]').setValue(rawInput)
    await wrapper.get('[aria-label="Confirm provider usage and local retention"]').setValue(true)
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(runApi.submit).toHaveBeenCalledOnce()
    const submission = vi.mocked(runApi.submit).mock.calls[0]![1]
    expect(vi.mocked(runApi.submit).mock.calls[0]![3]).toBe(rawInput)
    expect(router.currentRoute.value.query.submission).toBe(submission)
    expect(wrapper.text()).toContain('Checking its status does not submit another run')
    localSession.authenticated = false; await flushPromises(); localSession.authenticated = true; await flushPromises()
    expect(runApi.submit).toHaveBeenCalledOnce()
    await wrapper.findAll('button').find(button => button.text() === 'Look up submission')!.trigger('click'); await flushPromises()
    expect(runApi.submission).toHaveBeenCalledWith(submission)
    expect(router.currentRoute.value.path).toBe('/runs/existing-run')
    expect(runApi.submit).toHaveBeenCalledOnce()
    wrapper.unmount()
  })
  it('restores an opaque pending ID from a refreshed URL without submitting or looking up automatically', async () => {
    const { wrapper } = await setup('?submission=refresh-id')
    expect(wrapper.text()).toContain('refresh-id')
    expect(wrapper.get('[aria-label="Run workflow"]').attributes('disabled')).toBeDefined()
    expect(runApi.submit).not.toHaveBeenCalled(); expect(runApi.submission).not.toHaveBeenCalled()
    vi.mocked(runApi.submission).mockRejectedValue(new ApiError(404, 'not found'))
    await wrapper.findAll('button').find(button => button.text() === 'Look up submission')!.trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('No durable run was found')
    expect(runApi.submit).not.toHaveBeenCalled()
    wrapper.unmount()
  })
  it('does not redirect a different page when a submitted request finishes after unmount', async () => {
    const { wrapper, router } = await setup()
    let resolve: ((run: RunDetail) => void) | undefined
    vi.mocked(runApi.submit).mockImplementation(() => new Promise<RunDetail>(done => { resolve = done }))
    await wrapper.get('[aria-label="Run workflow"]').trigger('click'); await flushPromises()
    await wrapper.get('[aria-label="Confirm provider usage and local retention"]').setValue(true)
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(runApi.submit).toHaveBeenCalledOnce()
    wrapper.unmount(); await router.push('/runs')
    resolve!({ id: 'completed-after-navigation' } as RunDetail); await flushPromises()
    expect(router.currentRoute.value.path).toBe('/runs')
    expect(runApi.submit).toHaveBeenCalledOnce()
  })
})
