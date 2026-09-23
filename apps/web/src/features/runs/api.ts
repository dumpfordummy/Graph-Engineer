import { request } from '../api'
import { rejectDuplicateProperties } from '../workflows/document'
import type { Readiness, RunArtifact, RunDetail, RunEvent, RunSummary } from './contracts'
import { decodeRun } from './exactJson'

export function validateRunInput(text: string): void {
  if (new TextEncoder().encode(text).byteLength > 16_384) throw new Error('Run input exceeds the 16 KiB UTF-8 limit.')
  const value: unknown = JSON.parse(text)
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw new Error('Run input must be a JSON object.')
  rejectDuplicateProperties(text)
  function depth(value: unknown, level: number) {
    if (level > 32) throw new Error('Run input exceeds the maximum JSON depth of 32.')
    if (value !== null && typeof value === 'object') for (const entry of Object.values(value)) depth(entry, level + 1)
  }
  depth(value, 0)
}

// Validation inspects a copy; the original numeric tokens go onto the wire.
export function submissionBody(submissionId: string, expectedRevision: number, rawInput: string): string {
  validateRunInput(rawInput)
  return `{"submissionId":${JSON.stringify(submissionId)},"expectedRevision":${JSON.stringify(expectedRevision)},"input":${rawInput}}`
}
export const runApi = {
  readiness: (workflowId: string) => request<Readiness>(`/workflows/${encodeURIComponent(workflowId)}/readiness`),
  submit: (workflowId: string, submissionId: string, expectedRevision: number, input: string) => request<RunDetail>(`/workflows/${encodeURIComponent(workflowId)}/runs`, 'POST', submissionBody(submissionId, expectedRevision, input), undefined, true, decodeRun),
  submission: (id: string) => request<RunDetail>(`/runs/submissions/${encodeURIComponent(id)}`, 'GET', undefined, undefined, false, decodeRun),
  get: (id: string) => request<RunDetail>(`/runs/${encodeURIComponent(id)}`, 'GET', undefined, undefined, false, decodeRun),
  list: (workflowId?: string, before?: string) => { const query = new URLSearchParams({ limit: '20' }); if (workflowId) query.set('workflowId', workflowId); if (before) query.set('before', before); return request<{ items: RunSummary[]; nextCursor: string | null }>(`/runs?${query}`) },
  events: (id: string, after: number) => request<{ items: RunEvent[]; lastSequence: number; hasMore: boolean }>(`/runs/${encodeURIComponent(id)}/events?after=${after}&limit=200`),
  artifact: (id: string, artifactId: string) => request<RunArtifact>(`/runs/${encodeURIComponent(id)}/artifacts/${encodeURIComponent(artifactId)}`),
  cancel: (id: string) => request<RunDetail>(`/runs/${encodeURIComponent(id)}/cancel`, 'POST', {}, undefined, false, decodeRun),
}
