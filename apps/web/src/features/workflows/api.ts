import type { WorkflowDocument, WorkflowMetadata, ValidationReport } from './document'

interface Problem { title?: string; detail?: string; errors?: Record<string, string[]> }
export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); this.name = 'ApiError' }
}
async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  let response: Response
  try {
    response = await fetch(`/api${path}`, { method, headers: body ? { 'Content-Type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined })
  } catch { throw new ApiError(0, 'Cannot reach the local API. Your draft is still here. Check that the backend is running and try again.') }
  if (!response.ok) {
    let problem: Problem = {}
    try { problem = await response.json() as Problem } catch { /* Some servers return a plain error body. */ }
    const fields = Object.entries(problem.errors ?? {}).map(([path, errors]) => `${path}: ${errors.join(' ')}`).join(' ')
    const message = response.status === 409
      ? 'Save conflict: this workflow changed in another tab. Your edits are preserved. Export your draft before reloading the latest version.'
      : [problem.detail ?? problem.title ?? `Request failed (${response.status}).`, fields].filter(Boolean).join(' ')
    throw new ApiError(response.status, message)
  }
  return response.json() as Promise<T>
}
export const workflowApi = {
  list: () => request<WorkflowMetadata[]>('/workflows'),
  get: (id: string) => request<WorkflowDocument>(`/workflows/${encodeURIComponent(id)}`),
  create: (document: WorkflowDocument) => request<WorkflowDocument>('/workflows', 'POST', { document }),
  update: (document: WorkflowDocument) => request<WorkflowDocument>(`/workflows/${document.workflow.id}`, 'PUT', { expectedRevision: document.workflow.revision, document }),
  validate: (document: WorkflowDocument) => request<ValidationReport>('/workflows/validate', 'POST', { document }),
}
