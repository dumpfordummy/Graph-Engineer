import type { WorkflowDocument, WorkflowMetadata, ValidationReport } from './document'
import { request } from '../api'
export { ApiError } from '../api'

export const workflowApi = {
  list: () => request<WorkflowMetadata[]>('/workflows'),
  get: (id: string) => request<WorkflowDocument>(`/workflows/${encodeURIComponent(id)}`),
  create: (document: WorkflowDocument) => request<WorkflowDocument>('/workflows', 'POST', { document }),
  update: (document: WorkflowDocument) => request<WorkflowDocument>(`/workflows/${document.workflow.id}`, 'PUT', { expectedRevision: document.workflow.revision, document }),
  validate: (document: WorkflowDocument) => request<ValidationReport>('/workflows/validate', 'POST', { document }),
}
