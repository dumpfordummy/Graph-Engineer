import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import SessionGate from '../SessionGate.vue'
import { expireSession, localSession } from '../session'
import { request } from '../../api'

const mounted: VueWrapper[] = []
beforeEach(() => { Object.assign(localSession, { authenticated: false, checked: false, csrfToken: null, error: '' }) })
afterEach(() => { mounted.splice(0).forEach(wrapper => wrapper.unmount()); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })

describe('local browser pairing', () => {
  it('pairs from transient input and keeps mounted safe draft state behind the expired-session gate', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(json({ authenticated: false, csrfToken: null }))
      .mockResolvedValueOnce(json({ paired: true }))
      .mockResolvedValueOnce(json({ authenticated: true, csrfToken: 'synthetic-csrf' }))
    vi.stubGlobal('fetch', fetcher)
    const draftComponent = defineComponent({ setup: () => ({ draft: ref('metadata') }), template: '<input v-model="draft" aria-label="Unsaved draft">' })
    const wrapper = mount(SessionGate, { slots: { default: draftComponent } })
    mounted.push(wrapper); await flushPromises()
    expect(wrapper.find('[aria-label="Unsaved draft"]').exists()).toBe(false)
    await wrapper.get('[aria-label="Pairing token"]').setValue('synthetic-pair-token')
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(fetcher).toHaveBeenNthCalledWith(2, '/api/session/pair', expect.objectContaining({ body: JSON.stringify({ token: 'synthetic-pair-token' }) }))
    const draft = wrapper.get('[aria-label="Unsaved draft"]').element
    await wrapper.get('[aria-label="Unsaved draft"]').setValue('Keep my new metadata')
    expireSession(); await flushPromises()
    expect(wrapper.get('[aria-label="Unsaved draft"]').element).toBe(draft)
    expect((draft as HTMLInputElement).value).toBe('Keep my new metadata')
    expect(wrapper.find('[inert]').exists()).toBe(true)
    expect((wrapper.get('[aria-label="Pairing token"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper.text()).toContain('A connection test will never restart automatically')
  })
  it('clears pairing input after rejection and never renders an upstream token echo', async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(json({ authenticated: false, csrfToken: null }))
      .mockResolvedValueOnce(json({ detail: 'synthetic-secret-pair-token' }, 403))
    vi.stubGlobal('fetch', fetcher)
    const wrapper = mount(SessionGate); mounted.push(wrapper); await flushPromises()
    await wrapper.get('[aria-label="Pairing token"]').setValue('synthetic-secret-pair-token')
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect((wrapper.get('[aria-label="Pairing token"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper.html()).not.toContain('synthetic-secret-pair-token')
    expect(wrapper.text()).toContain('Pairing failed')
  })
})
describe('protected request transport', () => {
  it('sends JSON and the memory-only antiforgery token to the local API', async () => {
    localSession.csrfToken = 'synthetic-csrf'
    const fetcher = vi.fn().mockResolvedValue(json({ ok: true })); vi.stubGlobal('fetch', fetcher)
    await request('/providers', 'POST', { name: 'synthetic' })
    expect(fetcher).toHaveBeenCalledWith('/api/providers', expect.objectContaining({
      headers: { 'Content-Type': 'application/json', 'X-GE-CSRF': 'synthetic-csrf' }, credentials: 'same-origin', cache: 'no-store',
    }))
  })
  it('expires a rejected session without replaying a paid probe', async () => {
    localSession.authenticated = true; localSession.csrfToken = 'synthetic-csrf'
    const fetcher = vi.fn().mockResolvedValue(json({ detail: 'Rejected' }, 401)); vi.stubGlobal('fetch', fetcher)
    await expect(request('/providers/example/test', 'POST', { expectedRevision: 1, connectionVersion: 1 })).rejects.toThrow('will not be replayed')
    expect(localSession.authenticated).toBe(false)
    expect(localSession.csrfToken).toBeNull()
    expect(fetcher).toHaveBeenCalledTimes(1)
  })
  it('retains explicit provider conflict errors instead of mislabeling them as workflow conflicts', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ detail: 'Profile is referenced by saved workflows.' }, 409)))
    await expect(request('/providers/example', 'DELETE', { expectedRevision: 1 })).rejects.toThrow('Profile is referenced by saved workflows.')
  })
})
