import type { ConnectionTest, ProviderProfile } from '../contracts'

export const profile = (overrides: Partial<ProviderProfile> = {}): ProviderProfile => ({
  id: 'profile-alpha', name: 'Fixture provider', revision: 1, connectionVersion: 1,
  createdAt: '2026-09-23T00:00:00Z', updatedAt: '2026-09-23T00:00:00Z', protocol: 'openai-responses',
  baseUrl: 'https://provider.example/custom/v1', resolvedEndpoint: 'https://provider.example/custom/v1/responses',
  modelId: 'fixture-model', authMode: 'bearer', timeoutSeconds: 30, maxOutputTokens: 128,
  allowPrivateNetwork: false, allowInsecureHttp: false, hasCredential: true, lastTest: null, ...overrides,
})
export const result = (overrides: Partial<ConnectionTest> = {}): ConnectionTest => ({
  testedAt: '2026-09-23T00:00:00Z', connectionVersion: 1, category: 'success', success: true,
  durationMs: 25, message: 'Completed text response.', preview: 'GE_CONNECTION_OK', phraseMatched: true,
  observedModel: 'fixture-model', requestId: null, usage: null, ...overrides,
})
