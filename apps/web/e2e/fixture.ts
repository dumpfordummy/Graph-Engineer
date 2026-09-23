import { test as base, expect, type APIRequestContext } from '@playwright/test'
import { spawn, type ChildProcess } from 'node:child_process'
import { once } from 'node:events'
import { createWriteStream } from 'node:fs'
import { access, mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { setTimeout as delay } from 'node:timers/promises'

export const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
export const artifacts = path.join(repositoryRoot, '.artifacts/m2')
export const apiBase = 'http://127.0.0.1:5187/api'

type Session = { state: Awaited<ReturnType<APIRequestContext['storageState']>>; csrfToken: string }
type Backend = {
  restart: () => Promise<{ previousPid: number; nextPid: number }>
  dataDirectory: string
  session: () => Promise<Session>
  pairRequest: (request: APIRequestContext) => Promise<string>
}

export const test = base.extend<object, { backend: Backend }>({
  request: async ({ playwright, backend }, use) => {
    const session = await backend.session()
    const request = await playwright.request.newContext({
      storageState: session.state,
      extraHTTPHeaders: { Origin: 'http://127.0.0.1:5188', 'X-GE-CSRF': session.csrfToken },
    })
    await use(request)
    await request.dispose()
  },
  page: async ({ page, backend }, use) => {
    await page.context().addCookies((await backend.session()).state.cookies)
    await use(page)
  },
  backend: [async ({ browserName, playwright }, use, workerInfo) => {
    const dataDirectory = path.join(artifacts, `e2e-${Date.now()}-${workerInfo.workerIndex}-${browserName}`)
    await mkdir(dataDirectory, { recursive: true })
    const dll = path.join(repositoryRoot, 'src/GraphEngineering.Api/bin/Debug/net10.0/GraphEngineering.Api.dll')
    await access(dll).catch(() => {
      throw new Error('Build the API in Debug before browser verification: dotnet build GraphEngineering.slnx')
    })
    const log = createWriteStream(path.join(dataDirectory, 'backend.log'), { flags: 'a' })
    const frontendLog = createWriteStream(path.join(dataDirectory, 'frontend.log'), { flags: 'a' })
    let processHandle: ChildProcess | undefined
    let frontendProcess: ChildProcess | undefined
    let currentSession: Session | undefined
    async function pairRequest(request: APIRequestContext) {
      const token = (await readFile(path.join(dataDirectory, 'runtime/pairing-token.txt'), 'utf8')).trim()
      const result = await request.post(`${apiBase}/session/pair`, { data: { token }, headers: { Origin: 'http://127.0.0.1:5188' } })
      if (!result.ok()) throw new Error(`Isolated pairing failed with status ${result.status()}`)
      const status = await (await request.get(`${apiBase}/session`)).json() as { csrfToken: string }
      return status.csrfToken
    }
    async function session(): Promise<Session> {
      if (currentSession) return currentSession
      const bootstrap = await playwright.request.newContext({ extraHTTPHeaders: { Origin: 'http://127.0.0.1:5188' } })
      try {
        const csrfToken = await pairRequest(bootstrap)
        currentSession = { csrfToken, state: await bootstrap.storageState() }
        return currentSession
      } finally { await bootstrap.dispose() }
    }

    async function start() {
      processHandle = spawn('dotnet', [dll], {
        cwd: repositoryRoot,
        windowsHide: true,
        env: {
          ...process.env,
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: 'http://127.0.0.1:5187',
          GRAPH_ENGINEERING_DATA_DIR: dataDirectory,
          GRAPH_ENGINEERING_BROWSER_ORIGIN: 'http://127.0.0.1:5188',
        },
        stdio: ['ignore', 'pipe', 'pipe'],
      })
      processHandle.stdout?.pipe(log, { end: false })
      processHandle.stderr?.pipe(log, { end: false })
      let startError: Error | undefined
      processHandle.on('error', error => { startError = error })
      for (let attempt = 0; attempt < 120; attempt++) {
        if (startError) throw startError
        if (processHandle.exitCode !== null) {
          throw new Error(`Owned backend exited ${processHandle.exitCode}. See ${dataDirectory}/backend.log`)
        }
        const healthy = await fetch(`${apiBase}/health`).then(response => response.ok).catch(() => false)
        if (healthy) return processHandle.pid!
        await delay(250)
      }
      throw new Error(`Owned backend health timeout. See ${dataDirectory}/backend.log`)
    }

    async function stop(child: ChildProcess | undefined = processHandle) {
      if (!child || child.exitCode !== null || child.signalCode !== null) return
      const exited = once(child, 'exit')
      child.kill('SIGTERM')
      await Promise.race([
        exited,
        delay(10_000, undefined, { ref: false }).then(() => { throw new Error('Owned application process did not exit within 10 seconds') }),
      ])
    }

    async function startFrontend() {
      const frontendUrl = 'http://127.0.0.1:5188'
      if (await fetch(frontendUrl).then(() => true).catch(() => false)) throw new Error('Port 5188 is already occupied. Stop that service before browser checks.')
      const webRoot = path.join(repositoryRoot, 'apps/web')
      // Direct child ownership avoids npm/cmd process trees on Windows and permits precise cleanup.
      frontendProcess = spawn(process.execPath, [path.join(webRoot, 'node_modules/vite/bin/vite.js'), '--host', '127.0.0.1', '--port', '5188', '--strictPort'], {
        cwd: webRoot,
        windowsHide: true,
        env: { ...process.env, VITE_API_TARGET: 'http://127.0.0.1:5187' },
        stdio: ['ignore', 'pipe', 'pipe'],
      })
      frontendProcess.stdout?.pipe(frontendLog, { end: false })
      frontendProcess.stderr?.pipe(frontendLog, { end: false })
      let startError: Error | undefined
      frontendProcess.on('error', error => { startError = error })
      for (let attempt = 0; attempt < 120; attempt++) {
        if (startError) throw startError
        if (frontendProcess.exitCode !== null) throw new Error(`Owned frontend exited ${frontendProcess.exitCode}. See ${dataDirectory}/frontend.log`)
        if (await fetch(frontendUrl).then(response => response.ok).catch(() => false)) return
        await delay(250)
      }
      throw new Error(`Owned frontend startup timeout. See ${dataDirectory}/frontend.log`)
    }

    // Refuse to borrow an existing service: this suite must own the tested backend and database.
    const occupied = await fetch(`${apiBase}/health`).then(() => true).catch(() => false)
    if (occupied) throw new Error('Port 5187 is already serving an API. Stop that service before running browser checks.')
    try {
      await start()
      await startFrontend()
      await use({
        dataDirectory,
        session,
        pairRequest,
        restart: async () => {
          const previousPid = processHandle!.pid!
          await stop()
          const nextPid = await start()
          currentSession = undefined
          const evidence = { previousPid, nextPid, dataDirectory, restartedAt: new Date().toISOString() }
          await writeFile(path.join(artifacts, 'backend-restart.json'), JSON.stringify(evidence, null, 2))
          return { previousPid, nextPid }
        },
      })
    } finally {
      await stop(frontendProcess)
      await stop()
      log.end()
      frontendLog.end()
    }
  }, { scope: 'worker', auto: true }],
})

export { expect }

export function sampleDocument(name = 'Acceptance workflow') {
  const timestamp = new Date().toISOString()
  return {
    formatVersion: 1,
    workflow: { id: crypto.randomUUID(), name, description: 'Synthetic browser acceptance fixture', revision: 0, createdAt: timestamp, updatedAt: timestamp },
    definition: {
      nodes: [
        { id: 'start-1', type: 'start', typeVersion: 1, name: 'Start', description: 'Input', configuration: { sampleInput: 'A synthetic input' } },
        { id: 'model-1', type: 'modelCall', typeVersion: 1, name: 'First model', description: 'Draft transformation', configuration: { prompt: 'Summarize the supplied text.', providerProfileId: null } },
        { id: 'end-1', type: 'end', typeVersion: 1, name: 'End', description: 'Result', configuration: { resultReference: 'model-1 output, descriptive only' } },
      ],
      edges: [
        { id: 'edge-1', sourceNodeId: 'start-1', sourcePort: 'out', targetNodeId: 'model-1', targetPort: 'in' },
        { id: 'edge-2', sourceNodeId: 'model-1', sourcePort: 'out', targetNodeId: 'end-1', targetPort: 'in' },
      ],
    },
    layout: {
      nodes: [{ nodeId: 'start-1', x: 50, y: 170 }, { nodeId: 'model-1', x: 360, y: 170 }, { nodeId: 'end-1', x: 670, y: 170 }],
      viewport: { x: 0, y: 0, zoom: 1 },
    },
  }
}
