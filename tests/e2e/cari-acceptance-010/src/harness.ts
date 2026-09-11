import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import type { Page, Request, Route, TestInfo, CDPSession } from '@playwright/test'
import { runDir, origin } from '../paths'
import { IDENTITY, DIRECTORY, WITHOUT_DETAIL, capabilitySnapshot, candidate, candidatePage, linkedView, emptyListPage } from '../../cari-acceptance-009/src/fixtures'
import type { ProbeState } from '../browser-entry'

export { held, json, problem } from './replies'
export type { Reply } from './replies'
import type { Reply } from './replies'
export type Kind = 'identity' | 'companies' | 'capabilities' | 'candidates' | 'link' | 'list' | 'unexpected'
export interface Record010 {
  seq: number; kind: Kind; method: string; path: string; cursor: string | null; q: string | null;
  company: string | null; key: string | null; roles: number | null; payloadKeys: string[]; payloadSha256: string | null;
  status: number | null; transport: string; browserFinished: boolean; browserCancelled: boolean
  fixture: string | null; responseSha256: string | null; replayedHeader: boolean | null; originalCorrelationPresent: boolean | null
  /** The handshake, both directions. The acceptance turns on it, so the evidence carries it. */
  contractDeclaration: string | null; contractAck: string | null
}
export interface Request010 { url: URL; company: string | null; nth: number; roles: number | null; key: string | null }
/** The client-contract handshake, mirrored from the backend filter. */
export const CONTRACT_HEADER = 'x-erp-counterparty-contract'
export const CONTRACT_VERSION = 'optional-trade-roles-v1'
/** Sentinel for a single reply that must arrive WITHOUT the acknowledgment. */
export const NO_ACK = '__silent__'

export interface LaneOptions {
  slug: string; company: string;
  /** Model an OLD node: valid bodies, never an acknowledgment. */
  silentServer?: boolean;
  identity?: () => unknown;
  candidates?: (request: Request010) => Reply | Promise<Reply>;
  link?: (request: Request010) => Reply | Promise<Reply>;
}
const kindOf = (method: string, url: URL): Kind => {
  if (url.pathname === '/api/v1/identity/me') return 'identity'
  if (url.pathname === '/api/v1/organization/companies') return 'companies'
  if (url.pathname === '/api/v1/shell/capabilities') return 'capabilities'
  if (url.pathname.endsWith('/company-account-link-candidates')) return 'candidates'
  if (method === 'POST' && url.pathname.endsWith('/company-account')) return 'link'
  if (method === 'GET' && url.pathname === '/api/v1/counterparties') return 'list'
  return 'unexpected'
}
/** Test evidence goes to the CURRENT run's T/ directory; earlier runs are never touched. */
export function save(name: string, value: unknown) {
  const dir = path.join(runDir, 'T')
  mkdirSync(dir, { recursive: true })
  const stamp = new Date().toISOString().replaceAll(':', '-')
  writeFileSync(path.join(dir, name + '-' + stamp + '.json'), JSON.stringify(value, null, 2) + '\n')
}
export class Lane {
  readonly records: Record010[] = []
  externalBlocked = 0
  private finished = false
  private readonly requests = new WeakMap<Request, Record010>()
  private readonly counts = new Map<Kind, number>()
  private constructor(readonly page: Page, readonly options: LaneOptions) {}
  static async install(page: Page, options: LaneOptions) {
    const lane = new Lane(page, options)
    await page.addInitScript((company) => localStorage.setItem('goymen-company-id', company), options.company)
    page.on('requestfinished', (r) => { const row = lane.requests.get(r); if (row) row.browserFinished = true })
    page.on('response', async (response) => {
      const row = lane.requests.get(response.request())
      if (row?.kind !== 'link') return
      row.replayedHeader = (await response.headerValue('idempotency-replayed')) === 'true'
      row.originalCorrelationPresent = (await response.headerValue('idempotency-original-correlation-id')) !== null
    })
    page.on('requestfailed', (r) => {
      const row = lane.requests.get(r)
      if (row) row.browserCancelled = (r.failure()?.errorText ?? '').includes('ABORTED')
    })
    // One context-wide guard: all API requests are synthetic, every off-origin target is blocked.
    await page.context().route('**/*', async (route) => {
      const url = new URL(route.request().url())
      if (url.origin !== origin) { lane.externalBlocked += 1; await route.abort('blockedbyclient'); return }
      if (!url.pathname.startsWith('/api/')) { await route.continue(); return }
      await lane.handle(route)
    })
    page.on('close', () => {
      if (!lane.finished) save(options.slug + '-unfinished', {
        task_id: 'C-DESKTOP-010', lane: 'T', status: 'unfinished', records: lane.records,
        externalBlocked: lane.externalBlocked, at: new Date().toISOString(),
      })
    })
    return lane
  }
  private async handle(route: Route) {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const kind = kindOf(method, url)
    const nth = (this.counts.get(kind) ?? 0) + 1
    this.counts.set(kind, nth)
    // Only these two non-credential headers and the synthetic roles field are read.
    const company = await request.headerValue('x-company-id')
    const key = await request.headerValue('idempotency-key')
    const parsed: unknown = kind === 'link' ? request.postDataJSON() : null
    const roles = typeof parsed === 'object' && parsed !== null && 'roles' in parsed && typeof parsed.roles === 'number' ? parsed.roles : null
    const payloadKeys = typeof parsed === 'object' && parsed !== null ? Object.keys(parsed).sort() : []
    const payloadSha256 = kind === 'link' ? createHash('sha256').update(request.postData() ?? '').digest('hex') : null
    const row: Record010 = {
      seq: this.records.length + 1, kind, method, path: url.pathname, cursor: url.searchParams.get('cursor'),
      q: url.searchParams.get('q'), company, key, roles, payloadKeys, payloadSha256, status: null, transport: 'pending',
      browserFinished: false, browserCancelled: false,
      fixture: null, responseSha256: null, replayedHeader: null, originalCorrelationPresent: null,
      contractDeclaration: request.headers()[CONTRACT_HEADER] ?? null, contractAck: null,
    }
    this.records.push(row)
    this.requests.set(request, row)
    const ctx = { url, company, nth, roles, key }
    let reply: Reply
    switch (kind) {
      case 'identity': reply = { status: 200, body: this.options.identity?.() ?? IDENTITY }; break
      case 'companies': reply = { status: 200, body: DIRECTORY }; break
      case 'capabilities': reply = { status: 200, body: capabilitySnapshot(company, WITHOUT_DETAIL) }; break
      case 'candidates': reply = await (this.options.candidates?.(ctx) ?? { status: 200, body: candidatePage([candidate()]) }); break
      case 'link': reply = await (this.options.link?.(ctx) ?? { status: 201, body: linkedView(roles ?? 2) }); break
      case 'list': reply = { status: 200, body: emptyListPage() }; break
      default: reply = { status: 599, body: { title: 'Unexpected controlled API request' } }
    }
    try {
      if ('abort' in reply) { row.transport = 'harness-abort'; await route.abort('failed') }
      else {
        row.status = reply.status
        row.fixture = reply.fixture ?? null
        row.responseSha256 = createHash('sha256').update(JSON.stringify(reply.body)).digest('hex')
        /**
         * A COMPATIBLE controlled server acknowledges, because that is what the real
         * one does: the client-contract filter echoes the declaration on every
         * exchange it accepts (PR #230 `080e7a1`). Without this the write gate never
         * opens and every controlled link is refused before it is sent — these lanes
         * would then be measuring the gate instead of the behaviour they describe.
         *
         * A lane that wants an OLD node says so with `silentServer`, and a single
         * reply can opt out with `NO_ACK`. Both are explicit; silence is never the
         * accident.
         */
        const acknowledged = reply.headers?.[CONTRACT_HEADER] === NO_ACK || this.options.silentServer === true
          ? undefined
          : CONTRACT_VERSION
        const headers = { ...reply.headers }
        if (headers[CONTRACT_HEADER] === NO_ACK) delete headers[CONTRACT_HEADER]
        if (acknowledged !== undefined) headers[CONTRACT_HEADER] = acknowledged
        row.contractAck = headers[CONTRACT_HEADER] ?? null
        await route.fulfill({ status: reply.status, contentType: 'application/json', headers,
          body: JSON.stringify(reply.body) })
        row.transport = 'route-fulfilled'
      }
    } catch { row.transport = 'route-no-longer-available' }
  }
  of(kind: Kind) { return this.records.filter((r) => r.kind === kind) }
  /** Marks the lane as finished without writing evidence (warm-up); a closing page then writes no "unfinished" record. */
  dispose() { this.finished = true }
  async finish(info: TestInfo, facts: Record<string, unknown>) {
    this.finished = true
    save(this.options.slug, {
      task_id: 'C-DESKTOP-010', lane: 'T', title: info.title, status: info.status,
      browser: this.page.context().browser()?.version(), controlledResponsesOnly: true,
      records: this.records, externalBlocked: this.externalBlocked, facts, finishedAtUtc: new Date().toISOString(),
    })
  }
}
export function shell(page: Page): Promise<ProbeState> { return page.evaluate(() => window.c010.read()) }
export async function refetch(page: Page, which: 'identity' | 'candidates') {
  await page.evaluate((kind) => { if (kind === 'identity') window.c010.refetchIdentity(); else window.c010.refetchCandidates() }, which)
}
/** Test transport only: native browser fetch still receives the actual controlled response.
 * Ignore abort for the selected operation, to force the production callback to face a late result.
 * `notFragment` excludes URLs that also contain that fragment (e.g. select the cursorless read only).
 * Merely fulfilling a Playwright route is NOT counted as application consumption.
 */
export async function insensitiveTransport(page: Page, match: { method: string; fragment: string; notFragment?: string }) {
  await page.addInitScript((rule) => {
    const events: { event: string; status?: number; aborted?: boolean }[] = []
    window.c010Transport = { events }
    const original = window.fetch.bind(window)
    window.fetch = async (input, init) => {
      const url = input instanceof Request ? input.url : String(input)
      const method = init?.method ?? (input instanceof Request ? input.method : 'GET')
      const selected = method === rule.method && url.includes(rule.fragment) && !(rule.notFragment !== undefined && url.includes(rule.notFragment))
      if (!selected) return original(input, init)
      const signal = init?.signal
      events.push({ event: 'request-start', aborted: signal?.aborted ?? false })
      signal?.addEventListener('abort', () => events.push({ event: 'production-abort-observed' }), { once: true })
      const response = await original(input, { ...init, signal: undefined })
      events.push({ event: 'native-response', status: response.status, aborted: signal?.aborted ?? false })
      const originalJson = response.json.bind(response)
      response.json = async () => {
        const value: unknown = await originalJson()
        events.push({ event: 'production-json-resolved', status: response.status, aborted: signal?.aborted ?? false })
        return value
      }
      return response
    }
  }, match)
}
export function transportEvents(page: Page) { return page.evaluate(() => window.c010Transport.events) }

export class CoverageWitness {
  private constructor(private readonly session: CDPSession) {}
  static async start(page: Page) {
    const session = await page.context().newCDPSession(page)
    await session.send('Debugger.enable')
    await session.send('Profiler.enable')
    await session.send('Profiler.startPreciseCoverage', { callCount: true, detailed: true })
    return new CoverageWitness(session)
  }
  /** `needle` is matched in the SERVED (transformed) source: a string literally, a RegExp when the transform may reformat it. */
  async finish(points: { file: string; needle: string | RegExp; label: string }[]) {
    const coverage = await this.session.send('Profiler.takePreciseCoverage')
    const measurements: { file: string; label: string; offset: number; count: number; rangeStart: number; rangeEnd: number }[] = []
    for (const point of points) {
      const script = coverage.result.find((s) => s.url.split('?')[0].endsWith('/' + point.file))
      if (!script) throw new Error('Production coverage script absent: ' + point.file)
      const source = (await this.session.send('Debugger.getScriptSource', { scriptId: script.scriptId })).scriptSource
      const offset = typeof point.needle === 'string' ? source.indexOf(point.needle) : source.search(point.needle)
      if (offset < 0) throw new Error('Production coverage marker absent: ' + point.label)
      const ranges = script.functions.flatMap((fn) => fn.ranges)
        .filter((r) => r.startOffset <= offset && r.endOffset > offset)
        .sort((a, b) => (a.endOffset - a.startOffset) - (b.endOffset - b.startOffset))
      if (!ranges.length) throw new Error('Production coverage range absent: ' + point.label)
      measurements.push({ file: point.file, label: point.label, offset, count: ranges[0].count,
        rangeStart: ranges[0].startOffset, rangeEnd: ranges[0].endOffset })
    }
    await this.session.send('Profiler.stopPreciseCoverage')
    await this.session.detach()
    return measurements
  }
}
