// Run values are display-only data. Keep numeric JSON tokens independently of
// JavaScript numbers so the inspector cannot report a rounded provider result.
class ExactNumber { constructor(readonly token: string) {} }
function exactValue(text: string): unknown {
  const tokens = text.match(/"(?:\\.|[^"\\])*"|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|[{}[\]:,]|true|false|null/g) ?? []
  let index = 0
  function value(): unknown {
    const token = tokens[index++]!
    if (token === '{') {
      const result: Record<string, unknown> = Object.create(null)
      while (tokens[index] !== '}') {
        const key = JSON.parse(tokens[index++]!) as string
        index++; result[key] = value()
        if (tokens[index] !== ',') break
        index++
      }
      index++; return result
    }
    if (token === '[') {
      const result: unknown[] = []
      while (tokens[index] !== ']') { result.push(value()); if (tokens[index] !== ',') break; index++ }
      index++; return result
    }
    if (/^-?\d/.test(token)) return new ExactNumber(token)
    return JSON.parse(token)
  }
  return value()
}

export function displayJson(value: unknown, depth = 0): string {
  if (value instanceof ExactNumber) return value.token
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? ''
  const entries = Array.isArray(value) ? value.map(entry => displayJson(entry, depth + 1)) : Object.entries(value).map(([key, entry]) => `${JSON.stringify(key)}: ${displayJson(entry, depth + 1)}`)
  const [open, close] = Array.isArray(value) ? ['[', ']'] : ['{', '}']
  if (!entries.length) return `${open}${close}`
  return `${open}\n${'  '.repeat(depth + 1)}${entries.join(`,\n${'  '.repeat(depth + 1)}`)}\n${'  '.repeat(depth)}${close}`
}

export function decodeRun<T>(text: string): T {
  // Normal parsing keeps contract counters/IDs conventional. Only arbitrary run
  // values use exact display tokens; never serialize these objects into requests.
  const normal = JSON.parse(text)
  const exact = exactValue(text) as Record<string, unknown>
  if (Object.hasOwn(normal, 'input')) normal.input = exact.input
  if (Object.hasOwn(normal, 'result')) normal.result = exact.result
  if (Array.isArray(normal.nodes) && Array.isArray(exact.nodes)) {
    normal.nodes.forEach((node: { attempt?: Record<string, unknown> | null }, index: number) => {
      const source = (exact.nodes as { attempt?: Record<string, unknown> | null }[])[index]?.attempt
      if (node.attempt && source) { node.attempt.resolvedInputs = source.resolvedInputs; node.attempt.outputJson = source.outputJson }
    })
  }
  return normal as T
}
