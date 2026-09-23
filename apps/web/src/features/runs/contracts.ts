import type { ValidationIssue, WorkflowDocument } from '../workflows/document'
import type { ConnectionTest } from '../providers/contracts'
export type RunState = 'Queued' | 'Running' | 'CancelRequested' | 'Succeeded' | 'Failed' | 'Cancelled' | 'Interrupted'
export interface ReadyNode { nodeId: string; name: string; type: string; providerProfileId: string | null; profileName: string | null; modelId: string | null; connectionVersion: number | null }
export interface Readiness { ready: boolean; issues: ValidationIssue[]; orderedNodes: ReadyNode[]; maximumModelCalls: number }
export interface RunSummary { id: string; submissionId: string; workflowId: string; workflowName: string; workflowRevision: number; state: RunState; createdAt: string; startedAt: string | null; finishedAt: string | null; lastSequence: number; failureCode: string | null }
export interface RunProfile { nodeId: string; providerProfileId: string; name: string; connectionVersion: number; protocol: string; modelId: string; baseUrl: string; authMode: string; timeoutSeconds: number; maxOutputTokens: number; allowPrivateNetwork: boolean; allowInsecureHttp: boolean }
export interface RunAttempt { id: string; startedAt: string; finishedAt: string | null; dispatchIntentAt: string | null; externalOutcome: 'NotStarted' | 'ResponseReceived' | 'Unknown'; resolvedInputs: Record<string, unknown>; prompt: string | null; outputText: string | null; outputJson: Record<string, unknown> | null; failureCode: string | null; message: string | null; observedModel: string | null; requestId: string | null; usage: ConnectionTest['usage'] }
export interface RunNode { nodeId: string; name: string; type: string; state: 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled' | 'Interrupted' | 'Skipped'; attempt: RunAttempt | null; artifactIds: string[]; skipReason: string | null }
export interface RunDetail extends RunSummary { snapshot: WorkflowDocument; input: Record<string, unknown>; profiles: RunProfile[]; nodes: RunNode[]; result: unknown }
export interface RunEvent { sequence: number; at: string; kind: string; nodeId: string | null; state: string | null; message: string | null }
export interface RunArtifact { id: string; nodeId: string; kind: string; contentType: string; byteCount: number; sha256: string; text: string }
export interface RunNotice { runId: string; eventSequence: number }
export const terminal = (state: RunState) => ['Succeeded', 'Failed', 'Cancelled', 'Interrupted'].includes(state)
