import { DefaultHttpClient, HubConnectionBuilder, HttpTransportType, LogLevel, type HttpRequest } from '@microsoft/signalr'
import { ApiError } from '../api'
import { expireSession, localSession } from '../session/session'
import { runApi } from './api'
import { terminal, type RunDetail, type RunEvent, type RunNotice } from './contracts'

export interface RunConnection {
  start(): Promise<void>; stop(): Promise<void>
  on(name: string, handler: (notice: RunNotice) => void): void
  off(name: string, handler: (notice: RunNotice) => void): void
  onreconnecting(handler: () => void): void; onreconnected(handler: () => void): void; onclose(handler: (error?: Error) => void): void
}
function unauthorized(error: unknown) { return error instanceof ApiError && [401, 403].includes(error.status) || typeof error === 'object' && error !== null && 'statusCode' in error && [401, 403].includes(Number(error.statusCode)) }
export class SessionHttpClient extends DefaultHttpClient {
  constructor() { super({ log: () => undefined }) }
  override async send(request: HttpRequest) {
    try {
      const response = await super.send(request)
      if ([401, 403].includes(response.statusCode)) expireSession()
      return response
    } catch (error) {
      // SignalR wraps negotiation errors without retaining statusCode. Observe
      // authorization here, before that wrapper can erase the auth boundary.
      if (unauthorized(error)) expireSession()
      throw error
    }
  }
}
export function createRunConnection(): RunConnection {
  return new HubConnectionBuilder()
    .withUrl('/hubs/runs', { transport: HttpTransportType.WebSockets, withCredentials: true, headers: { 'X-GE-CSRF': localSession.csrfToken ?? '' }, httpClient: new SessionHttpClient() })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: context => !localSession.authenticated || unauthorized(context.retryReason) ? null : [0, 1000, 3000, 10000][context.previousRetryCount] ?? null })
    .configureLogging(LogLevel.None).build()
}

export class RunLiveReader {
  private disposed = false
  private blocked = false
  private connected = false
  private busy = false
  private wanted = false
  private snapshotSequence = -1
  private cursor = 0
  private retry = 0
  private pollDelay = 5000
  private poll?: ReturnType<typeof setTimeout>
  private startTimer?: ReturnType<typeof setTimeout>
  private events = new Map<number, RunEvent>()
  private latest: RunDetail | null = null
  constructor(private id: string, private receive: (run: RunDetail, events: RunEvent[]) => void, private status: (message: string, stale: boolean) => void, private connection: RunConnection = createRunConnection(), private api: Pick<typeof runApi, 'get' | 'events'> = runApi) {
    connection.on('RunChanged', this.notice)
    connection.onreconnecting(() => { if (!this.disposed && !this.blocked) { this.connected = false; status('Reconnecting · showing the last durable snapshot', true) } })
    connection.onreconnected(() => { if (!this.disposed && !this.blocked) { this.connected = true; this.retry = 0; void this.refresh() } })
    connection.onclose(error => { if (this.disposed || this.blocked) return; this.connected = false; if (unauthorized(error)) this.block(); else { status('Notifications unavailable · checking saved state', true); void this.refresh() } })
  }
  private notice = (notice: RunNotice) => {
    if (notice.runId !== this.id || !Number.isSafeInteger(notice.eventSequence) || notice.eventSequence <= Math.min(this.snapshotSequence, this.cursor)) return
    void this.refresh()
  }
  async start() { if (this.disposed || this.blocked) return; void this.refresh(); await this.connect() }
  private async connect() {
    if (this.disposed || this.blocked) return
    try { await this.connection.start(); if (this.disposed || this.blocked) { await this.connection.stop(); return } this.connected = true; this.retry = 0; await this.refresh() }
    catch (error) {
      if (this.disposed || this.blocked) return
      if (!localSession.authenticated || unauthorized(error)) { this.block(); return }
      this.status('Notifications unavailable · checking saved state', true)
      const delay = [1000, 3000, 10000][this.retry++]
      if (delay !== undefined) this.startTimer = setTimeout(() => { void this.connect() }, delay)
    }
  }
  private block() { this.blocked = true; this.connected = false; clearTimeout(this.poll); clearTimeout(this.startTimer); expireSession(); this.status('Pair again to read run updates. The run continues independently.', true); void this.connection.stop() }
  async refresh() {
    if (this.disposed || this.blocked) return
    if (this.busy) { this.wanted = true; return }
    clearTimeout(this.poll); this.busy = true
    let reconciled = false
    try {
      do {
        this.wanted = false
        const snapshot = await this.api.get(this.id)
        if (this.disposed || this.blocked) return
        if (snapshot.id === this.id && snapshot.lastSequence >= this.snapshotSequence) { this.latest = snapshot; this.snapshotSequence = snapshot.lastSequence }
        let more = true, pages = 0
        while (more && pages++ < 20) {
          const batch = await this.api.events(this.id, this.cursor)
          if (this.disposed || this.blocked) return
          let progressed = false
          for (const event of [...batch.items].sort((a, b) => a.sequence - b.sequence)) if (event.sequence > this.cursor) { this.events.set(event.sequence, event); this.cursor = event.sequence; progressed = true }
          more = batch.hasMore && progressed
        }
        if (this.latest) this.receive(this.latest, [...this.events.values()].sort((a, b) => a.sequence - b.sequence))
        this.status(this.connected ? 'Live · reconciled with saved state' : 'Notifications unavailable · saved state refreshed', !this.connected)
        this.pollDelay = 5000
        reconciled = !more
      } while (this.wanted && !this.disposed && !this.blocked)
    } catch (error) {
      if (this.disposed || this.blocked) return
      if (unauthorized(error)) { this.block(); return }
      this.status('Updates stale · connection unavailable; run outcome may have changed', true)
      this.pollDelay = Math.min(this.pollDelay * 2, 30000)
    } finally {
      this.busy = false
      if (!this.disposed && !this.blocked && (!reconciled || !this.latest || !terminal(this.latest.state))) this.poll = setTimeout(() => { void this.refresh() }, this.connected ? 15000 : this.pollDelay)
    }
  }
  dispose() { this.disposed = true; clearTimeout(this.poll); clearTimeout(this.startTimer); this.connection.off('RunChanged', this.notice); void this.connection.stop() }
}
