import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import ProviderEditor from '../ProviderEditor.vue'
import ConnectionResult from '../ConnectionResult.vue'
import { providerApi } from '../api'
import { endpointPreview, connectionReadiness } from '../contracts'
import { localSession } from '../../session/session'
import { profile, result } from './fixtures'

vi.mock('../api', () => ({ providerApi: { get: vi.fn(), list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn(), test: vi.fn() } }))
const mounted: VueWrapper[] = []
beforeEach(() => {
  vi.resetAllMocks()
  Object.assign(localSession, { authenticated: true, csrfToken: 'synthetic-csrf', checked: true, error: '' })
  vi.mocked(providerApi.get).mockResolvedValue(profile())
  vi.mocked(providerApi.create).mockResolvedValue(profile())
  vi.mocked(providerApi.update).mockResolvedValue(profile({ revision: 2 }))
  vi.mocked(providerApi.test).mockResolvedValue(result())
  vi.spyOn(window, 'confirm').mockReturnValue(false)
})
afterEach(() => { mounted.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks() })
async function editor(id = 'profile-alpha') {
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/settings/connections/:id', component: ProviderEditor },
    { path: '/settings/connections', component: { template: '<div>Profile list</div>' } },
  ] })
  await router.push(`/settings/connections/${id}`); await router.isReady()
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] } })
  mounted.push(wrapper); await flushPromises()
  return { wrapper, router }
}
function button(wrapper: VueWrapper, text: string) { return wrapper.findAll('button').find(item => item.text() === text)! }
async function field(wrapper: VueWrapper, label: string, value: string | boolean) { await wrapper.get(`[aria-label="${label}"]`).setValue(value) }

describe('provider settings and secret lifecycle', () => {
  it('saves a new profile without probing and clears the transient replacement key', async () => {
    const { wrapper } = await editor('new')
    await field(wrapper, 'Profile name', 'Fixture provider')
    await field(wrapper, 'API base URL', 'https://provider.example/custom/v1')
    await field(wrapper, 'Model ID', 'fixture-model')
    await field(wrapper, 'Credential action', 'replace')
    await field(wrapper, 'API key', 'SYNTHETIC-ONLY-provider-key')
    expect(wrapper.get('[data-testid="resolved-endpoint"]').text()).toBe('https://provider.example/custom/v1/responses')
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(providerApi.create).toHaveBeenCalledWith(expect.objectContaining({ credential: { action: 'replace', value: 'SYNTHETIC-ONLY-provider-key' } }))
    expect(providerApi.test).not.toHaveBeenCalled()
    expect(wrapper.find('[aria-label="API key"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Credential saved')
    expect(wrapper.text()).toContain('Saving did not call the provider')
  })
  it('keeps saved credentials on metadata edits and sends explicit Remove only when selected', async () => {
    const { wrapper } = await editor()
    await field(wrapper, 'Model ID', 'different-model')
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(providerApi.update).toHaveBeenLastCalledWith('profile-alpha', expect.objectContaining({ expectedRevision: 1, credential: { action: 'keep' } }))
    await field(wrapper, 'Credential action', 'remove')
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(providerApi.update).toHaveBeenLastCalledWith('profile-alpha', expect.objectContaining({ credential: { action: 'remove' } }))
  })
  it('preserves safe metadata on save errors while clearing and redacting the transient key', async () => {
    const { wrapper } = await editor()
    await field(wrapper, 'Profile name', 'Keep my unsaved metadata')
    await field(wrapper, 'Credential action', 'replace')
    await field(wrapper, 'API key', 'SYNTHETIC-ONLY-error-key')
    vi.mocked(providerApi.update).mockRejectedValue(new Error('Refused SYNTHETIC-ONLY-error-key'))
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect((wrapper.get('[aria-label="Profile name"]').element as HTMLInputElement).value).toBe('Keep my unsaved metadata')
    expect((wrapper.get('[aria-label="API key"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper.text()).toContain('Refused [redacted]')
    expect(wrapper.html()).not.toContain('SYNTHETIC-ONLY-error-key')
    expect(button(wrapper, 'Test connection').attributes('disabled')).toBeDefined()
  })
  it('clears transient keys on session expiration and retains metadata without replaying a test', async () => {
    const { wrapper } = await editor()
    await field(wrapper, 'Profile name', 'Retained safe draft')
    await field(wrapper, 'Credential action', 'replace')
    await field(wrapper, 'API key', 'SYNTHETIC-ONLY-expiration-key')
    localSession.authenticated = false; await flushPromises()
    expect((wrapper.get('[aria-label="API key"]').element as HTMLInputElement).value).toBe('')
    expect((wrapper.get('[aria-label="Profile name"]').element as HTMLInputElement).value).toBe('Retained safe draft')
    localSession.authenticated = true; await flushPromises()
    expect(providerApi.test).not.toHaveBeenCalled()
  })
  it('resets destination approvals and requires confirmation with a replacement key after endpoint changes', async () => {
    vi.mocked(providerApi.get).mockResolvedValue(profile({ allowPrivateNetwork: true, allowInsecureHttp: true }))
    const { wrapper } = await editor()
    await field(wrapper, 'API base URL', 'https://different.example/v1')
    expect((wrapper.get('[aria-label="Allow this exact private or loopback destination"]').element as HTMLInputElement).checked).toBe(false)
    expect((wrapper.get('[aria-label="Acknowledge unencrypted HTTP"]').element as HTMLInputElement).checked).toBe(false)
    await field(wrapper, 'API key', 'SYNTHETIC-ONLY-reentered-key')
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(providerApi.update).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Confirm the changed destination')
    await field(wrapper, 'Confirm destination change', true)
    await field(wrapper, 'API key', 'SYNTHETIC-ONLY-reentered-key')
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(providerApi.update).toHaveBeenCalledWith('profile-alpha', expect.objectContaining({ confirmDestinationChange: true, credential: { action: 'replace', value: 'SYNTHETIC-ONLY-reentered-key' } }))
  })
  it('blocks dirty navigation when discard is declined', async () => {
    const { wrapper, router } = await editor()
    await field(wrapper, 'Profile name', 'Unsaved')
    await router.push('/settings/connections')
    expect(router.currentRoute.value.path).toBe('/settings/connections/profile-alpha')
    expect((wrapper.get('[aria-label="Profile name"]').element as HTMLInputElement).value).toBe('Unsaved')
    expect(window.confirm).toHaveBeenCalled()
  })
  it('shows referenced-profile deletion conflicts without losing the form', async () => {
    const { wrapper } = await editor()
    vi.mocked(window.confirm).mockReturnValue(true)
    vi.mocked(providerApi.remove).mockRejectedValue(new Error('Saved workflows reference this profile.'))
    await button(wrapper, 'Delete profile').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Saved workflows reference this profile.')
    expect(wrapper.find('[aria-label="Profile name"]').exists()).toBe(true)
  })
})

describe('saved connection probes', () => {
  it('disables incomplete and unsaved probes and prevents duplicate requests while pending', async () => {
    const { wrapper } = await editor()
    await field(wrapper, 'Profile name', 'Unsaved')
    expect(button(wrapper, 'Test connection').attributes('disabled')).toBeDefined()
    await field(wrapper, 'Profile name', 'Fixture provider')
    let finish!: (value: ReturnType<typeof result>) => void
    vi.mocked(providerApi.test).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    await button(wrapper, 'Test connection').trigger('click')
    await button(wrapper, 'Testing…').trigger('click')
    expect(providerApi.test).toHaveBeenCalledTimes(1)
    expect(button(wrapper, 'Testing…').attributes('disabled')).toBeDefined()
    finish(result()); await flushPromises()
    expect(wrapper.text()).toContain('Text response verified')
  })
  it('does not let a late result verify a newly saved connection version', async () => {
    const { wrapper } = await editor()
    let finish!: (value: ReturnType<typeof result>) => void
    vi.mocked(providerApi.test).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    await button(wrapper, 'Test connection').trigger('click')
    await field(wrapper, 'Model ID', 'new-model')
    vi.mocked(providerApi.update).mockResolvedValue(profile({ modelId: 'new-model', revision: 2, connectionVersion: 2 }))
    await wrapper.get('form').trigger('submit'); await flushPromises()
    finish(result()); await flushPromises()
    expect(wrapper.text()).toContain('It does not verify the newer saved settings')
    expect(wrapper.text()).not.toContain('Text response verified')
  })
  it('does not enable probing a bearer profile with no saved credential', async () => {
    vi.mocked(providerApi.get).mockResolvedValue(profile({ hasCredential: false }))
    const { wrapper } = await editor()
    expect(button(wrapper, 'Test connection').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Replace and save a credential before testing')
    expect(providerApi.test).not.toHaveBeenCalled()
  })
  it('renders upstream preview as text, showing actual usage only when supplied', () => {
    const wrapper = mount(ConnectionResult, { props: { result: result({ preview: '<img src=x onerror="alert(1)">', usage: { outputTokens: 7 } }), currentVersion: 1, dirty: false } })
    mounted.push(wrapper)
    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.get('[aria-label="Response preview"]').text()).toBe('<img src=x onerror="alert(1)">')
    expect(wrapper.text()).toContain('Output 7')
    expect(wrapper.text()).not.toContain('Input 0')
    expect(wrapper.text()).not.toContain('Total 0')
  })
  it('ignores nullable usage fields at runtime and separates supplied counts explicitly', async () => {
    // Defensive handling for older API responses; the current contract omits unavailable fields.
    const partialUsage = JSON.parse('{"inputTokens":null,"outputTokens":7,"totalTokens":null}') as NonNullable<ReturnType<typeof result>['usage']>
    const wrapper = mount(ConnectionResult, { props: { result: result({ usage: partialUsage }), currentVersion: 1, dirty: false } })
    mounted.push(wrapper)
    expect(wrapper.findAll('dd').at(-1)!.text()).toBe('Output 7')
    await wrapper.setProps({ result: result({ usage: { inputTokens: 9, outputTokens: 6, totalTokens: 15 } }) })
    expect(wrapper.findAll('dd').at(-1)!.text()).toBe('Input 9 · Output 6 · Total 15')
    await wrapper.setProps({ result: result({ usage: {} }) })
    expect(wrapper.text()).not.toContain('Reported usage')
  })
})

describe('local display contract', () => {
  it('preserves prefixes without adding another v1 and rejects unsafe base URL previews', () => {
    expect(endpointPreview('https://provider.example/custom/v1/')).toBe('https://provider.example/custom/v1/responses')
    for (const value of ['https://u:p@provider.example', 'https://provider.example/v1/responses', 'https://provider.example/v1?key=x', 'https://provider.example/#x', 'https://provider.example/a/../v1', 'https://provider.example/%2e%2e/v1']) expect(endpointPreview(value)).toBeNull()
  })
  it('distinguishes configured profiles from a verified matching connection version', () => {
    expect(connectionReadiness(profile())).toBe('Configured · not tested')
    expect(connectionReadiness(profile({ lastTest: result() }))).toBe('Text verified by latest test')
    expect(connectionReadiness(profile({ connectionVersion: 2, lastTest: result() }))).not.toBe('Text verified by latest test')
    expect(connectionReadiness(profile({ hasCredential: false }))).toBe('Credential required')
  })
})
