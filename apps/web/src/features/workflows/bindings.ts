import type { WorkflowDocument, WorkflowNode } from './document'

// Follow control edges, never array order or visual coordinates. Ambiguous graphs
// expose no guesses; executable readiness supplies the authoritative diagnostics.
export function earlierModelNodes(document: WorkflowDocument, targetId: string): WorkflowNode[] {
  const starts = document.definition.nodes.filter(node => node.type === 'start')
  if (starts.length !== 1) return []
  let current: WorkflowNode = starts[0]!
  const visited = new Set<string>(), earlier: WorkflowNode[] = []
  while (!visited.has(current.id)) {
    if (current.id === targetId) return earlier
    visited.add(current.id)
    if (current.type === 'modelCall') earlier.push(current)
    const edges = document.definition.edges.filter(edge => edge.sourceNodeId === current.id)
    if (edges.length !== 1) return []
    const next = document.definition.nodes.find(node => node.id === edges[0]!.targetNodeId)
    if (!next) return []
    current = next
  }
  return []
}

export function pointerError(pointer: string): string | null {
  return pointer !== '' && (!pointer.startsWith('/') || /~(?![01])/u.test(pointer))
    ? 'Use an RFC 6901 pointer beginning with /, or leave empty for the whole value. Escape ~ as ~0 and / as ~1.' : null
}
