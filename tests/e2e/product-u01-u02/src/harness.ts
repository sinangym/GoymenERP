/**
 * The controlled-T lane for the Product screens, in the shape of the Cari 010 lane:
 * every `/api/` request of the page is answered by a Playwright route with a
 * synthetic body, every off-origin request is blocked, and what the browser
 * actually sent is RECORDED — method, path, query names, the two non-credential
 * headers the contract cares about, payload keys and a SHA of the body. Bodies,
 * cookies, storage and environment never reach the evidence.
 *
 * What it proves: the unchanged frontend, in a real Chromium, against the pinned
 * wire shapes. What it does not prove: a live API, SQL, a real login, a real
 * capability publication. Those are the ENV/backend acceptance this suite waits for.
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import type { Page, Request, Route, TestInfo } from '@playwright/test'
import { runDir, origin } from '../paths'
import {
  IDENTITY, DIRECTORY, A_ID, ALL_PRODUCT_CAPS, capabilitySnapshot,
  codeGroups, referenceOptions, productPage, productCard, mutation, productCore,
} from './fixtures'
import type { ProbeState, PendingProbe } from '../browser-entry'

export { held, json, problem } from '../../cari-acceptance-010/src/replies'
import type { Reply } from '../../cari-acceptance-010/src/replies'
export type { Reply }

export type Kind = 'identity' | 'companies' | 'capabilities' | 'groups' | 'options' | 'list' | 'card' | 'create' | 'edit' | 'unexpected'

export interface RecordU {
  seq: number; kind: Kind; method: string; path: string
  /** Query parameter NAMES only, sorted; `q` and `cursor` values are never recorded. */
  queryNames: string[]
  company: string | null; key: string | null
  payloadKeys: string[]; payloadSha256: string | null
  status: number | null; transport: string; browserFinished: boolean; browserCancelled: boolean
  fixture: string | null; responseSha256: string | null
}
export interface RequestU { url: URL; company: string | null; nth: number; key: string | null; body: unknown }

export interface LaneOptions {
  slug: string
  /** The committed company the shell boots into; Product is global, so it must never reach a Product request. */
  company?: string
  /** What the server publishes; default is all four Product ids. */
  capabilities?: () => readonly string[]
  /** The capability generation the server answers with; a re-entry after a real 403 may legitimately see a new one. */
  generation?: () => string
  companies?: () => Reply
  groups?: (request: RequestU) => Reply | Promise<Reply>
  options?: (request: RequestU) => Reply | Promise<Reply>
  list?: (request: RequestU) => Reply | Promise<Reply>
  card?: (request: RequestU) => Reply | Promise<Reply>
  create?: (request: RequestU) => Reply | Promise<Reply>
  edit?: (request: RequestU) => Reply | Promise<Reply>
}

const kindOf = (method: string, url: URL): Kind => {
  const p = url.pathname
  if (p === '/api/v1/identity/me') return 'identity'
  if (p === '/api/v1/organization/companies') return 'companies'
  if (p === '/api/v1/shell/capabilities') return 'capabilities'
  if (p === '/api/v1/products/code-groups') return 'groups'
  if (p === '/api/v1/products/reference-options') return 'options'
  if (method === 'POST' && p === '/api/v1/products') return 'create'
  if (method === 'PATCH' && /^\/api\/v1\/products\/[^/]+\/draft$/.test(p)) return 'edit'
  if (method === 'GET' && p === '/api/v1/products') return 'list'
  if (method === 'GET' && /^\/api\/v1\/products\/[^/]+$/.test(p)) return 'card'
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
  readonly records: RecordU[] = []
  externalBlocked = 0
  private finished = false
  private readonly requests = new WeakMap<Request, RecordU>()
  private readonly counts = new Map<Kind, number>()
  private constructor(readonly page: Page, readonly options: LaneOptions) {}

  static async install(page: Page, options: LaneOptions) {
    const lane = new Lane(page, options)
    await page.addInitScript((company) => localStorage.setItem('goymen-company-id', company), options.company ?? A_ID)
    page.on('requestfinished', (r) => { const row = lane.requests.get(r); if (row) row.browserFinished = true })
    page.on('requestfailed', (r) => {
      const row = lane.requests.get(r)
      if (row) row.browserCancelled = (r.failure()?.errorText ?? '').includes('ABORTED')
    })
    await page.context().route('**/*', async (route) => {
      const url = new URL(route.request().url())
      if (url.origin !== origin) { lane.externalBlocked += 1; await route.abort('blockedbyclient'); return }
      if (!url.pathname.startsWith('/api/')) { await route.continue(); return }
      await lane.handle(route)
    })
    page.on('close', () => {
      if (!lane.finished) save(options.slug + '-unfinished', {
        task_id: 'U-01-U-02', lane: 'T', status: 'unfinished', records: lane.records,
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
    const company = await request.headerValue('x-company-id')
    const key = await request.headerValue('idempotency-key')
    const raw = request.postData()
    let body: unknown = null
    if (raw !== null) { try { body = JSON.parse(raw) } catch { body = null } }
    const payloadKeys = typeof body === 'object' && body !== null ? Object.keys(body).sort() : []
    const row: RecordU = {
      seq: this.records.length + 1, kind, method, path: url.pathname,
      queryNames: [...url.searchParams.keys()].sort(),
      company, key, payloadKeys,
      payloadSha256: raw === null ? null : createHash('sha256').update(raw).digest('hex'),
      status: null, transport: 'pending', browserFinished: false, browserCancelled: false,
      fixture: null, responseSha256: null,
    }
    this.records.push(row)
    this.requests.set(request, row)
    const ctx: RequestU = { url, company, nth, key, body }
    let reply: Reply
    switch (kind) {
      case 'identity': reply = { status: 200, body: IDENTITY }; break
      case 'companies': reply = this.options.companies?.() ?? { status: 200, body: DIRECTORY }; break
      case 'capabilities':
        reply = { status: 200, body: capabilitySnapshot(this.options.capabilities?.() ?? ALL_PRODUCT_CAPS, this.options.generation?.() ?? 'gen-1') }
        break
      case 'groups': reply = await (this.options.groups?.(ctx) ?? { status: 200, body: codeGroups() }); break
      case 'options': reply = await (this.options.options?.(ctx) ?? { status: 200, body: referenceOptions() }); break
      case 'list': reply = await (this.options.list?.(ctx) ?? { status: 200, body: productPage() }); break
      case 'card': reply = await (this.options.card?.(ctx) ?? { status: 200, body: productCard() }); break
      case 'create': reply = await (this.options.create?.(ctx) ?? { status: 201, body: mutation() }); break
      case 'edit':
        reply = await (this.options.edit?.(ctx) ?? { status: 200, body: mutation({ card: productCard({ product: productCore({ version: '8' }) }) }) })
        break
      default: reply = { status: 599, body: { title: 'Unexpected controlled API request' } }
    }
    try {
      if ('abort' in reply) { row.transport = 'harness-abort'; await route.abort('failed') }
      else {
        row.status = reply.status
        row.fixture = reply.fixture ?? null
        row.responseSha256 = createHash('sha256').update(JSON.stringify(reply.body)).digest('hex')
        await route.fulfill({ status: reply.status, contentType: 'application/json', headers: reply.headers, body: JSON.stringify(reply.body) })
        row.transport = 'route-fulfilled'
      }
    } catch { row.transport = 'route-no-longer-available' }
  }

  of(kind: Kind) { return this.records.filter((r) => r.kind === kind) }
  /** Every Product request, i.e. everything the shell's own bootstrap did not send. */
  product() { return this.records.filter((r) => !['identity', 'companies', 'capabilities', 'unexpected'].includes(r.kind)) }
  dispose() { this.finished = true }
  async finish(info: TestInfo, facts: Record<string, unknown>) {
    this.finished = true
    save(this.options.slug, {
      task_id: 'U-01-U-02', lane: 'T', title: info.title, status: info.status,
      browser: this.page.context().browser()?.version(), controlledResponsesOnly: true,
      records: this.records, externalBlocked: this.externalBlocked, facts, finishedAtUtc: new Date().toISOString(),
      note: 'Not ERP acceptance: no live API/SQL/login/capability publication.',
    })
  }
}

export function shell(page: Page): Promise<ProbeState> { return page.evaluate(() => window.urun.read()) }
export function pending(page: Page, scope: 'create' | { edit: string }): Promise<PendingProbe | null> {
  return page.evaluate((s) => window.urun.pending(s), scope)
}
/** An in-app navigation, as a sidebar link would do it; never a reload. */
export function go(page: Page, path: string): Promise<void> {
  return page.evaluate((p) => window.urun.navigate(p), path)
}
