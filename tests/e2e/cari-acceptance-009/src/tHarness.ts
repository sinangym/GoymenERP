/**
 * T lane harness: controlled responses delivered to the REAL application in a
 * REAL browser, at the response stage (`page.route` / `route.fulfill`).
 *
 * What is recorded, per request the browser actually issued: method, path, query
 * keys, the `X-Company-Id` and `Idempotency-Key` headers, whether the dev
 * bootstrap identity header was present, the POST body, and how the harness
 * answered (status, abort reason, or "the request was gone before delivery").
 * A request the application CANCELS never produces a delivered response; that is
 * measured through `requestfailed` and recorded as such — an abort is never
 * counted as a delivered response (C-005 / C-DESKTOP-005 evidence boundary).
 *
 * The recordings are synthetic-only. No secret exists in this lane.
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import type { Page, Request, Route, TestInfo } from '@playwright/test'
import {
  ALL_CAPS,
  DIRECTORY,
  IDENTITY,
  candidate,
  candidatePage,
  capabilitySnapshot,
  emptyListPage,
  linkedView,
  mutationView,
} from './fixtures.js'

export type Reply =
  | { readonly status: number; readonly json?: unknown; readonly headers?: Record<string, string> }
  | { readonly abort: 'failed' | 'internetdisconnected' | 'timedout' }

export interface ReplyContext {
  readonly request: Request
  readonly url: URL
  readonly method: string
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly body: unknown
  /** 1-based ordinal of this request among requests of the SAME kind. */
  readonly nth: number
}

export type Responder = (ctx: ReplyContext) => Reply | Promise<Reply>

export interface ApiRecord {
  seq: number
  at: string
  method: string
  path: string
  queryKeys: string[]
  cursor: string | null
  companyId: string | null
  idempotencyKey: string | null
  devIdentityHeader: boolean
  body: unknown
  delivered: 'fulfilled' | 'aborted-by-harness' | 'request-gone-before-delivery' | 'pending'
  status: number | null
  abortReason: string | null
  kind: RecordKind
}

export type RecordKind =
  | 'identity'
  | 'companies'
  | 'capabilities'
  | 'candidates'
  | 'link'
  | 'create'
  | 'detail'
  | 'list'
  | 'other'

export interface Held {
  readonly promise: Promise<void>
  release(): void
  readonly released: () => boolean
}

export function held(): Held {
  let release: () => void = () => {}
  let done = false
  const promise = new Promise<void>((resolve) => {
    release = () => {
      done = true
      resolve()
    }
  })
  return { promise, release: () => release(), released: () => done }
}

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Reply {
  return { status, json: body, headers }
}

export function problem(status: number, code: string | null, title = 'Hata'): Reply {
  const body: Record<string, unknown> = { type: 'about:blank', title, status, detail: 'Yerel ayrıntı metni' }
  if (code !== null) body.code = code
  return { status, json: body, headers: { 'content-type': 'application/problem+json' } }
}

export interface TLaneConfig {
  /** Evidence file slug used if the test fails before `finish()`. */
  readonly slug?: string
  /** The company the shell should commit first (localStorage preference). */
  readonly preferCompany?: string
  readonly capabilities?: (companyId: string | null, nth: number) => readonly string[]
  readonly candidates?: Responder
  readonly link?: Responder
  readonly create?: Responder
  readonly detail?: Responder
  readonly list?: Responder
}

const CANDIDATES_RE = /^\/api\/v1\/counterparties\/company-account-link-candidates$/
const LINK_RE = /^\/api\/v1\/counterparties\/[^/]+\/company-account$/
const DETAIL_RE = /^\/api\/v1\/counterparties\/[^/]+$/
const LIST_RE = /^\/api\/v1\/counterparties$/

function classify(method: string, pathname: string): RecordKind {
  if (pathname === '/api/v1/identity/me') return 'identity'
  if (pathname === '/api/v1/organization/companies') return 'companies'
  if (pathname === '/api/v1/shell/capabilities') return 'capabilities'
  if (CANDIDATES_RE.test(pathname)) return 'candidates'
  if (method === 'POST' && LINK_RE.test(pathname)) return 'link'
  if (method === 'POST' && LIST_RE.test(pathname)) return 'create'
  if (method === 'GET' && DETAIL_RE.test(pathname)) return 'detail'
  if (method === 'GET' && LIST_RE.test(pathname)) return 'list'
  return 'other'
}

export class TLane {
  readonly records: ApiRecord[] = []
  readonly failures: { url: string; errorText: string; at: string }[] = []
  private readonly counts = new Map<RecordKind, number>()
  private seq = 0

  private constructor(
    private readonly page: Page,
    private readonly config: TLaneConfig,
  ) {}

  private finished = false

  static async install(page: Page, config: TLaneConfig = {}): Promise<TLane> {
    const lane = new TLane(page, config)
    // A failing test never reaches `finish()`; the recording is still evidence, so it
    // is written when the page closes (synchronously — the test is over by then).
    page.on('close', () => {
      if (lane.finished || !process.env.CARI009_EVIDENCE_DIR) return
      const slug = config.slug ?? `unfinished-${Date.now()}`
      try {
        const file = evidencePath('T', 'network', `${slug}.json`)
        writeFileSync(
          file,
          `${JSON.stringify({ lane: 'T', slug, status: 'unfinished', records: lane.records, failures: lane.failures, closedAtUtc: new Date().toISOString() }, null, 2)}\n`,
          'utf8',
        )
      } catch {
        // evidence directory unavailable; nothing else to do at close time
      }
    })
    if (config.preferCompany) {
      const companyId = config.preferCompany
      await page.addInitScript((id: string) => {
        window.localStorage.setItem('goymen-company-id', id)
      }, companyId)
    }
    page.on('requestfailed', (request) => {
      if (!request.url().includes('/api/v1/')) return
      lane.failures.push({
        url: redact(request.url()),
        errorText: request.failure()?.errorText ?? 'unknown',
        at: new Date().toISOString(),
      })
    })
    await page.route('**/api/v1/**', (route) => lane.handle(route))
    return lane
  }

  private async handle(route: Route): Promise<void> {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method().toUpperCase()
    const kind = classify(method, url.pathname)
    const nth = (this.counts.get(kind) ?? 0) + 1
    this.counts.set(kind, nth)
    const headers = request.headers()
    let body: unknown = null
    const raw = request.postData()
    if (raw !== null) {
      try {
        body = JSON.parse(raw)
      } catch {
        body = raw
      }
    }
    const record: ApiRecord = {
      seq: ++this.seq,
      at: new Date().toISOString(),
      method,
      path: url.pathname,
      queryKeys: [...url.searchParams.keys()],
      cursor: url.searchParams.get('cursor'),
      companyId: headers['x-company-id'] ?? null,
      idempotencyKey: headers['idempotency-key'] ?? null,
      devIdentityHeader: typeof headers['x-development-user'] === 'string',
      body,
      delivered: 'pending',
      status: null,
      abortReason: null,
      kind,
    }
    this.records.push(record)

    const ctx: ReplyContext = {
      request,
      url,
      method,
      companyId: record.companyId,
      idempotencyKey: record.idempotencyKey,
      body,
      nth,
    }
    const reply = await this.reply(kind, ctx)
    if ('abort' in reply) {
      record.delivered = 'aborted-by-harness'
      record.abortReason = reply.abort
      try {
        await route.abort(reply.abort)
      } catch {
        record.delivered = 'request-gone-before-delivery'
      }
      return
    }
    try {
      await route.fulfill({
        status: reply.status,
        headers: { 'content-type': 'application/json', ...reply.headers },
        body: reply.json === undefined ? '' : JSON.stringify(reply.json),
      })
      record.delivered = 'fulfilled'
      record.status = reply.status
    } catch {
      // The application cancelled the request before the harness could deliver.
      record.delivered = 'request-gone-before-delivery'
      record.status = reply.status
    }
  }

  private async reply(kind: RecordKind, ctx: ReplyContext): Promise<Reply> {
    switch (kind) {
      case 'identity':
        return json(IDENTITY)
      case 'companies':
        return json(DIRECTORY)
      case 'capabilities': {
        const caps = this.config.capabilities?.(ctx.companyId, ctx.nth) ?? ALL_CAPS
        return json(capabilitySnapshot(ctx.companyId, caps))
      }
      case 'candidates':
        return (this.config.candidates ?? (() => json(candidatePage([candidate()]))))(ctx)
      case 'link':
        return (this.config.link ?? (() => json(linkedView(), 201)))(ctx)
      case 'create':
        return (this.config.create ?? (() => json(mutationView(), 201)))(ctx)
      case 'detail':
        return (this.config.detail ?? (() => json(mutationView())))(ctx)
      case 'list':
        return (this.config.list ?? (() => json(emptyListPage())))(ctx)
      default:
        return problem(404, null, 'unexpected')
    }
  }

  of(kind: RecordKind): ApiRecord[] {
    return this.records.filter((r) => r.kind === kind)
  }
  candidateReads(): ApiRecord[] {
    return this.of('candidates')
  }
  links(): ApiRecord[] {
    return this.of('link')
  }
  creates(): ApiRecord[] {
    return this.of('create')
  }
  detailReads(): ApiRecord[] {
    return this.of('detail')
  }
  capabilityReads(): ApiRecord[] {
    return this.of('capabilities')
  }

  /** Write the recording into the evidence tree and attach it to the test. */
  async finish(testInfo: TestInfo, slug: string): Promise<void> {
    this.finished = true
    await this.page.unrouteAll({ behavior: 'ignoreErrors' })
    const payload = {
      lane: 'T',
      slug,
      title: testInfo.title,
      status: testInfo.status ?? 'unknown',
      browserVersion: this.page.context().browser()?.version() ?? null,
      records: this.records,
      failures: this.failures,
      finishedAtUtc: new Date().toISOString(),
    }
    const file = evidencePath('T', 'network', `${slug}.json`)
    writeFileSync(file, `${JSON.stringify(payload, null, 2)}\n`, 'utf8')
    await testInfo.attach('cari009-network', { path: file, contentType: 'application/json' })
  }
}

/** Query strings are dropped from failure URLs: only the path matters for evidence. */
function redact(url: string): string {
  try {
    return new URL(url).pathname
  } catch {
    return url
  }
}

export function evidenceRoot(): string {
  const root = process.env.CARI009_EVIDENCE_DIR
  if (!root) throw new Error('CARI009_EVIDENCE_DIR is not set; evidence must go to the task evidence folder')
  return root
}

export function evidencePath(lane: 'T' | 'L', group: string, file: string): string {
  const dir = path.join(evidenceRoot(), lane, group)
  mkdirSync(dir, { recursive: true })
  return path.join(dir, file)
}

export async function shot(page: Page, lane: 'T' | 'L', slug: string, label: string): Promise<string> {
  const file = evidencePath(lane, 'screenshots', `${slug}-${label}.png`)
  await page.screenshot({ path: file, fullPage: true })
  return file
}

/** The focused element as the browser sees it, including `:focus-visible`. */
export async function activeElement(page: Page): Promise<{
  tag: string
  id: string
  role: string | null
  name: string | null
  text: string
  focusVisible: boolean
  outlineStyle: string
}> {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null
    if (!el) return { tag: '', id: '', role: null, name: null, text: '', focusVisible: false, outlineStyle: '' }
    const style = window.getComputedStyle(el)
    return {
      tag: el.tagName.toLowerCase(),
      id: el.id,
      role: el.getAttribute('role'),
      name: el.getAttribute('aria-label') ?? el.getAttribute('name'),
      text: (el.textContent ?? '').trim().slice(0, 80),
      focusVisible: el.matches(':focus-visible'),
      outlineStyle: `${style.outlineStyle} ${style.outlineWidth} / box-shadow ${style.boxShadow === 'none' ? 'none' : 'set'}`,
    }
  })
}

export function writeJsonEvidence(lane: 'T' | 'L', group: string, file: string, value: unknown): string {
  const target = evidencePath(lane, group, file)
  writeFileSync(target, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
  return target
}
