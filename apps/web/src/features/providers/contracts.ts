export interface ConnectionTest {
  testedAt: string
  connectionVersion: number
  category: string
  success: boolean
  durationMs: number
  message: string
  preview: string | null
  phraseMatched: boolean
  observedModel: string | null
  requestId: string | null
  usage: { inputTokens?: number; outputTokens?: number; totalTokens?: number } | null
}
export interface ProviderSettings {
  name: string
  protocol: 'openai-responses'
  baseUrl: string
  modelId: string
  authMode: 'bearer' | 'none'
  timeoutSeconds: number
  maxOutputTokens: number
  allowPrivateNetwork: boolean
  allowInsecureHttp: boolean
}
export interface ProviderProfile extends ProviderSettings {
  id: string
  revision: number
  connectionVersion: number
  createdAt: string
  updatedAt: string
  resolvedEndpoint: string
  hasCredential: boolean
  lastTest: ConnectionTest | null
}
export interface ProviderWrite extends ProviderSettings {
  credential: { action: 'keep' | 'replace' | 'remove'; value?: string }
  confirmDestinationChange: boolean
  expectedRevision?: number
}
export const newProviderSettings = (): ProviderSettings => ({
  name: '', protocol: 'openai-responses', baseUrl: '', modelId: '', authMode: 'bearer',
  timeoutSeconds: 30, maxOutputTokens: 128, allowPrivateNetwork: false, allowInsecureHttp: false,
})
export function profileSettings(profile: ProviderProfile): ProviderSettings {
  return {
    name: profile.name, protocol: profile.protocol, baseUrl: profile.baseUrl, modelId: profile.modelId,
    authMode: profile.authMode, timeoutSeconds: profile.timeoutSeconds, maxOutputTokens: profile.maxOutputTokens,
    allowPrivateNetwork: profile.allowPrivateNetwork, allowInsecureHttp: profile.allowInsecureHttp,
  }
}
export function connectionReadiness(profile: ProviderProfile) {
  if (profile.authMode === 'bearer' && !profile.hasCredential) return 'Credential required'
  if (profile.lastTest?.connectionVersion === profile.connectionVersion && profile.lastTest.success) return 'Text verified by latest test'
  return profile.lastTest ? 'Configured · latest test did not verify text' : 'Configured · not tested'
}
export function endpointPreview(baseUrl: string): string | null {
  try {
    if (!baseUrl || baseUrl.length > 2048 || /[\\\s]/.test(baseUrl) || /(?:^|\/)\.{1,2}(?:\/|$)|%2e|%2f|%5c/i.test(baseUrl)) return null
    const url = new URL(baseUrl)
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) return null
    const path = url.pathname.replace(/\/+$/, '')
    if (path.toLowerCase().endsWith('/responses')) return null
    return `${url.origin}${path}/responses`
  } catch { return null }
}
