/**
 * Live traffic recorder for the Organization journeys.
 *
 * WHY NOT IMPORT `../../cari-acceptance-009/src/lHarness.ts`. Two reasons, and neither
 * is preference. It belongs to another lane with a single writer, and it encodes the
 * Counterparty client-contract handshake — the `X-ERP-Counterparty-Contract`
 * acknowledgment, cursor paging and the contract-version gate — none of which exist on
 * the Organization surface. Importing it would drag exactly the protocol this module
 * has been told repeatedly not to carry over. So this is the same PATTERN, written
 * small for this surface, not a second framework.
 *
 * NOTHING IS MOCKED. There is no `route.fulfill` in this file. The only interception is
 * `DeliveryWithholder`, which re-issues one REAL request with `route.fetch`, reads the
 * server's REAL answer, and then aborts the BROWSER's delivery of it. That models a
 * lost reply — the only way to reach a genuinely unknown outcome on demand — and it is
 * recorded as a withheld delivery, never as a network fault.
 *
 * REDACTION IS STRUCTURAL. Only a URL's pathname and its query KEYS are ever copied, so
 * no value can reach the evidence through this path; `authorization` and `cookie`
 * headers are never read at all.
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import type { Page, Route, TestInfo } from '@playwright/test'

const here = path.dirname(fileURLToPath(import.meta.url))
export const EVIDENCE_ROOT = process.env.ORG_EVIDENCE_DIR ?? path.join(here, '..', 'artifacts')

/** Request/response headers this suite is allowed to record. */
const RECORDED_REQUEST_HEADERS = [
  'x-company-id',
  'idempotency-key',
  'x-development-user',
  'x-correlation-id',
] as const
const RECORDED_RESPONSE_HEADERS = [
  'idempotency-replayed',
  'idempotency-original-correlation-id',
  'x-correlation-id',
] as const

export interface LiveRecord {
  seq: number
  method: string
  /** Pathname only — never a query string. */
  path: string
  /** Query KEYS only, never values. */
  queryKeys: string[]
  requestHeaders: Record<string, string>
  /** The PUT body. Organization command bodies carry no personal data. */
  requestBody?: unknown
  status?: number
  responseHeaders?: Record<string, string>
  /** Present only for JSON reads; the Organization DTOs are structural, not personal. */
  responseBody?: unknown
  /** Set when the browser's delivery of a real answer was deliberately withheld. */
  withheld?: 'delivery-withheld'
  issuedAt: number
  settledAt?: number
}

function pick(headers: Record<string, string>, allowed: readonly string[]): Record<string, string> {
  const out: Record<string, string> = {}
  for (const key of allowed) {
    const value = headers[key]
    if (value !== undefined) out[key] = value
  }
  return out
}

export class LiveRecorder {
  readonly records: LiveRecord[] = []
  private seq = 0

  private constructor(private readonly page: Page) {}

  static async attach(page: Page): Promise<LiveRecorder> {
    const recorder = new LiveRecorder(page)
    page.on('request', (request) => {
      if (!request.url().includes('/api/v1/')) return
      const url = new URL(request.url())
      let body: unknown
      if (request.method() === 'PUT') {
        try {
          body = JSON.parse(request.postData() ?? 'null')
        } catch {
          body = undefined
        }
      }
      recorder.records.push({
        seq: (recorder.seq += 1),
        method: request.method(),
        path: url.pathname,
        queryKeys: [...url.searchParams.keys()],
        requestHeaders: pick(request.headers(), RECORDED_REQUEST_HEADERS),
        requestBody: body,
        issuedAt: (recorder.seq += 1),
      })
    })
    page.on('response', (response) => {
      const url = new URL(response.url())
      if (!url.pathname.includes('/api/v1/')) return
      const record = [...recorder.records]
        .reverse()
        .find((r) => r.path === url.pathname && r.status === undefined)
      if (record === undefined) return
      record.status = response.status()
      record.responseHeaders = pick(response.headers(), RECORDED_RESPONSE_HEADERS)
      record.settledAt = (recorder.seq += 1)
      void response
        .json()
        .then((value: unknown) => {
          record.responseBody = value
        })
        .catch(() => {
          /* 204 and problem bodies that do not parse are simply absent. */
        })
    })
    return recorder
  }

  /** Every request to a path, in issue order. */
  forPath(pattern: RegExp, method?: string): LiveRecord[] {
    return this.records.filter(
      (r) => pattern.test(r.path) && (method === undefined || r.method === method),
    )
  }

  /** The command requests this journey issued. */
  activeStatePuts(): LiveRecord[] {
    return this.forPath(/\/api\/v1\/organization\/locations\/[^/]+\/active-state$/, 'PUT')
  }

  siteReads(): LiveRecord[] {
    return this.forPath(/\/api\/v1\/organization\/sites$/, 'GET')
  }

  locationPageReads(): LiveRecord[] {
    return this.forPath(/\/api\/v1\/organization\/locations$/, 'GET')
  }

  locationDetailReads(): LiveRecord[] {
    return this.forPath(/\/api\/v1\/organization\/locations\/[0-9a-fA-F-]+$/, 'GET')
  }

  write(group: string, file: string): string {
    return writeEvidence(group, file, { records: this.records })
  }
}

/**
 * Sends ONE real command, reads the server's real answer, then withholds the browser's
 * delivery of it.
 *
 * This is how a genuine unknown outcome is produced without lying about anything: the
 * command really is committed server-side, and the client really does not learn the
 * result. A synthesised 503 would test our error branch; this tests the actual
 * situation the idempotency key exists for.
 */
export class DeliveryWithholder {
  private armed = true
  withheldStatus: number | null = null
  withheldKey: string | null = null

  constructor(
    private readonly page: Page,
    private readonly pattern: RegExp,
  ) {}

  async arm(): Promise<void> {
    await this.page.route(
      (url) => this.pattern.test(url.pathname),
      async (route: Route) => {
        if (!this.armed || route.request().method() !== 'PUT') {
          await route.continue()
          return
        }
        this.armed = false
        // The request goes to the REAL API and its REAL answer is read...
        const response = await route.fetch()
        this.withheldStatus = response.status()
        this.withheldKey = route.request().headers()['idempotency-key'] ?? null
        // ...and only the browser's delivery of it is dropped.
        await route.abort('failed')
      },
    )
  }
}

export function writeEvidence(group: string, file: string, value: unknown): string {
  const dir = path.join(EVIDENCE_ROOT, group)
  mkdirSync(dir, { recursive: true })
  const target = path.join(dir, file)
  writeFileSync(target, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
  return target
}

export async function shot(page: Page, group: string, label: string): Promise<string> {
  const dir = path.join(EVIDENCE_ROOT, group)
  mkdirSync(dir, { recursive: true })
  const target = path.join(dir, `${label}.png`)
  await page.screenshot({ path: target, fullPage: true })
  return target
}

/** Attaches the recorded traffic to the Playwright report of a failing case. */
export async function attachEvidence(
  testInfo: TestInfo,
  recorder: LiveRecorder,
  group: string,
): Promise<void> {
  const file = recorder.write(group, `${testInfo.title.replace(/[^a-z0-9]+/gi, '-')}.json`)
  await testInfo.attach('organization-network', { path: file, contentType: 'application/json' })
}
