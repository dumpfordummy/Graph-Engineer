import { expireSession, localSession } from './session/session'

interface Problem { title?: string; detail?: string; code?: string; errors?: Record<string, string[]> }
export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string) { super(message); this.name = 'ApiError' }
}
export async function request<T>(path: string, method = 'GET', body?: unknown, signal?: AbortSignal): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (method !== 'GET' && localSession.csrfToken) headers['X-GE-CSRF'] = localSession.csrfToken
  let response: Response
  try {
    response = await fetch(`/api${path}`, {
      method, headers, credentials: 'same-origin', cache: 'no-store',
      body: body === undefined ? undefined : JSON.stringify(body), signal,
    })
  } catch {
    if (signal?.aborted) throw new ApiError(0, 'Request cancelled. The provider may still finish work or charge for usage.')
    throw new ApiError(0, 'Cannot reach the local API. Your draft is still here. Check that the backend is running and try again.')
  }
  if (!response.ok) {
    let problem: Problem = {}
    try { problem = await response.json() as Problem } catch { /* Never show raw upstream or proxy bodies. */ }
    if (response.status === 401 || response.status === 403) expireSession()
    const fields = Object.entries(problem.errors ?? {}).map(([field, errors]) => `${field}: ${errors.join(' ')}`).join(' ')
    const message = response.status === 401 || response.status === 403
      ? 'Local access expired or was rejected. Pair again; your safe draft metadata is preserved. The request will not be replayed.'
      : response.status === 409 && path.startsWith('/workflows')
        ? 'Save conflict: this workflow changed in another tab. Your edits are preserved. Export your draft before reloading the latest version.'
        : [problem.detail ?? problem.title ?? `Request failed (${response.status}).`, fields].filter(Boolean).join(' ')
    throw new ApiError(response.status, message, problem.code)
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>
}
