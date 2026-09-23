import { request } from '../api'
import type { ConnectionTest, ProviderProfile, ProviderWrite } from './contracts'

const path = (id: string) => `/providers/${encodeURIComponent(id)}`
export const providerApi = {
  list: () => request<ProviderProfile[]>('/providers'),
  get: (id: string) => request<ProviderProfile>(path(id)),
  create: (profile: ProviderWrite) => request<ProviderProfile>('/providers', 'POST', profile),
  update: (id: string, profile: ProviderWrite) => request<ProviderProfile>(path(id), 'PUT', profile),
  remove: (profile: ProviderProfile) => request<void>(path(profile.id), 'DELETE', { expectedRevision: profile.revision }),
  test: (profile: ProviderProfile, signal: AbortSignal) => request<ConnectionTest>(`${path(profile.id)}/test`, 'POST', {
    expectedRevision: profile.revision, connectionVersion: profile.connectionVersion,
  }, signal),
}
