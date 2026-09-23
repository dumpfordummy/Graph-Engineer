import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { RunLiveReader, SessionHttpClient, type RunConnection } from '../live'
import type { RunDetail, RunNotice } from '../contracts'
import { sampleDocument } from '../../workflows/document'
import { ApiError } from '../../api'
import { expireSession, localSession } from '../../session/session'

class Connection implements RunConnection {
  start = vi.fn(async () => undefined)
  stop = vi.fn(async () => undefined)
  handler?: (notice: RunNotice) => void
  reconnecting?: () => void
  reconnected?: () => void
  closed?: (error?: Error) => void
  on(_name: string, handler: (notice: RunNotice) => void) { this.handler = handler }
  off = vi.fn(() => { this.handler = undefined })
  onreconnecting(handler: () => void) { this.reconnecting = handler }
  onreconnected(handler: () => void) { this.reconnected = handler }
  onclose(handler: (error?: Error) => void) { this.closed = handler }
}
function run(sequence = 1, state: RunDetail['state'] = 'Running'): RunDetail {
  return { id: 'run-one', submissionId: 'submission-one', workflowId: 'workflow', workflowName: 'Frozen workflow', workflowRevision: 1, state, createdAt: '2026-09-24T00:00:00Z', startedAt: null, finishedAt: null, lastSequence: sequence, failureCode: null, snapshot: sampleDocument(), input: {}, profiles: [{ nodeId: 'node-one', providerProfileId: 'profile-one', name: 'Synthetic profile', connectionVersion: 1, protocol: 'openai-responses', modelId: 'fixture-model', baseUrl: 'http://127.0.0.1:5999/v1', authMode: 'none', timeoutSeconds: 5, maxOutputTokens: 128, allowPrivateNetwork: true, allowInsecureHttp: true }], nodes: [], result: null }
}
function fixtures() {
  const connection = new Connection(), receive = vi.fn(), status = vi.fn()
  const api = { get: vi.fn(async () => run()), events: vi.fn(async () => ({ items: [{ sequence: 1, at: '2026-09-24T00:00:00Z', kind: 'Running', nodeId: null, state: 'Running', message: null }], lastSequence: 1, hasMore: false })) }
  return { connection, receive, status, api, reader: new RunLiveReader('run-one', receive, status, connection, api) }
}
beforeEach(() => { vi.useFakeTimers(); localSession.authenticated = true; localSession.csrfToken = 'synthetic-csrf' })
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('durable run reconciliation', () => {
  it('ignores duplicate and foreign notices and uses ordered event cursors', async () => {
    const fixture = fixtures(); await fixture.reader.start(); await flushPromises()
    fixture.api.get.mockClear(); fixture.api.events.mockClear()
    fixture.connection.handler?.({ runId: 'run-one', eventSequence: 1 })
    fixture.connection.handler?.({ runId: 'different-run', eventSequence: 2 })
    await flushPromises(); expect(fixture.api.get).not.toHaveBeenCalled()
    fixture.api.get.mockResolvedValue(run(2, 'Succeeded'))
    fixture.api.events.mockResolvedValue({ items: [{ sequence: 2, at: '', kind: 'Succeeded', nodeId: null, state: 'Succeeded', message: null }], lastSequence: 2, hasMore: false })
    fixture.connection.handler?.({ runId: 'run-one', eventSequence: 2 })
    await flushPromises()
    expect(fixture.api.events).toHaveBeenCalledWith('run-one', 1)
    expect(fixture.receive.mock.lastCall?.[1].map((event: { sequence: number }) => event.sequence)).toEqual([1, 2])
    fixture.reader.dispose(); expect(fixture.connection.off).toHaveBeenCalledOnce()
  })
  it('never replaces a newer snapshot with a delayed older state', async () => {
    const fixture = fixtures(); await fixture.reader.start(); await flushPromises()
    fixture.api.get.mockResolvedValue(run(8, 'Succeeded')); await fixture.reader.refresh()
    fixture.api.get.mockResolvedValue(run(3, 'Running')); await fixture.reader.refresh()
    expect(fixture.receive.mock.lastCall?.[0].state).toBe('Succeeded')
    expect(fixture.receive.mock.lastCall?.[0].lastSequence).toBe(8)
    fixture.reader.dispose()
  })
  it('coalesces notifications arriving during a read without overlapping requests', async () => {
    const fixture = fixtures(); await fixture.reader.start(); await flushPromises()
    let resolve: ((value: RunDetail) => void) | undefined
    fixture.api.get.mockImplementationOnce(() => new Promise<RunDetail>(done => { resolve = done }))
    const count = fixture.api.get.mock.calls.length
    fixture.connection.handler?.({ runId: 'run-one', eventSequence: 5 })
    fixture.connection.handler?.({ runId: 'run-one', eventSequence: 6 })
    expect(fixture.api.get.mock.calls.length).toBe(count + 1)
    resolve!(run(5)); await flushPromises()
    expect(fixture.api.get.mock.calls.length).toBe(count + 2)
    fixture.reader.dispose()
  })
  it('performs bounded initial retries and polls snapshots without ever submitting work', async () => {
    const fixture = fixtures(); fixture.connection.start.mockRejectedValue(new Error('offline'))
    await fixture.reader.start(); await flushPromises()
    await vi.advanceTimersByTimeAsync(14001)
    expect(fixture.connection.start).toHaveBeenCalledTimes(4)
    const attempts = fixture.connection.start.mock.calls.length
    await vi.advanceTimersByTimeAsync(60000)
    expect(fixture.connection.start).toHaveBeenCalledTimes(attempts)
    expect(fixture.api.get.mock.calls.length).toBeGreaterThan(2)
    expect(fixture.status.mock.calls.some(call => call[1] === true)).toBe(true)
    fixture.reader.dispose()
  })
  it('reconciles after actual connection callbacks and stops on unauthorized until a new paired reader', async () => {
    const fixture = fixtures(); await fixture.reader.start(); await flushPromises()
    fixture.connection.reconnecting?.(); expect(fixture.status.mock.lastCall?.[1]).toBe(true)
    fixture.api.get.mockResolvedValue(run(4)); fixture.connection.reconnected?.(); await flushPromises()
    expect(fixture.receive.mock.lastCall?.[0].lastSequence).toBe(4)
    fixture.api.get.mockRejectedValue(new ApiError(401, 'expired')); await fixture.reader.refresh()
    const count = fixture.api.get.mock.calls.length
    expect(localSession.authenticated).toBe(false)
    expect(fixture.connection.stop).toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(120000); fixture.connection.handler?.({ runId: 'run-one', eventSequence: 9 }); await flushPromises()
    expect(fixture.api.get.mock.calls.length).toBe(count)
    fixture.reader.dispose()
  })
  it('rejects updates from an in-flight request after the route reader is disposed', async () => {
    const fixture = fixtures(); let resolve: ((value: RunDetail) => void) | undefined
    fixture.api.get.mockImplementation(() => new Promise<RunDetail>(done => { resolve = done }))
    void fixture.reader.start(); await flushPromises(); fixture.reader.dispose()
    resolve!(run(4)); await flushPromises()
    expect(fixture.receive).not.toHaveBeenCalled()
    expect(fixture.api.events).not.toHaveBeenCalled()
  })
  it('keeps read-only polling after a terminal snapshot when its event fetch failed', async () => {
    const fixture = fixtures(); fixture.api.get.mockResolvedValue(run(1, 'Succeeded')); fixture.api.events.mockRejectedValue(new ApiError(0, 'temporary event failure'))
    await fixture.reader.start(); await flushPromises()
    expect(fixture.receive).not.toHaveBeenCalled()
    expect(fixture.status.mock.lastCall?.[1]).toBe(true)
    fixture.api.events.mockResolvedValue({ items: [{ sequence: 1, at: '', kind: 'Succeeded', nodeId: null, state: 'Succeeded', message: null }], lastSequence: 1, hasMore: false })
    await vi.advanceTimersByTimeAsync(15001)
    expect(fixture.receive.mock.lastCall?.[0].state).toBe('Succeeded')
    expect(fixture.receive.mock.lastCall?.[0].profiles[0].protocol).toBe('openai-responses')
    const calls = fixture.api.get.mock.calls.length
    await vi.advanceTimersByTimeAsync(60000)
    expect(fixture.api.get.mock.calls.length).toBe(calls)
    fixture.reader.dispose()
  })
  it('stops wrapped negotiation failures after the HTTP auth observer expires the session', async () => {
    const fixture = fixtures(); fixture.connection.start.mockImplementation(async () => { expireSession(); throw new Error('Wrapped negotiate failure without statusCode') })
    await fixture.reader.start(); await flushPromises(); await vi.advanceTimersByTimeAsync(120000)
    expect(fixture.connection.start).toHaveBeenCalledOnce()
    expect(localSession.authenticated).toBe(false)
    expect(fixture.connection.stop).toHaveBeenCalled()
    fixture.reader.dispose()
  })
  it.each([401, 403])('observes negotiate HTTP %i before SignalR wraps its error', async status => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status })))
    const client = new SessionHttpClient()
    await client.send({ method: 'POST', url: 'http://127.0.0.1:5999/hubs/runs/negotiate' }).catch(() => undefined)
    expect(localSession.authenticated).toBe(false)
    expect(localSession.csrfToken).toBeNull()
  })
})
