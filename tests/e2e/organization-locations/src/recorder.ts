/**
 * Live traffic recorder for the Organization journeys.
 *
 * WHY NOT IMPORT `../../cari-acceptance-009/src/lHarness.ts`. Two reasons, and neither
 * is preference. It belongs to another lane with a single writer, and it encodes the
 * Counterparty client-contract handshake — the `X-ERP-Counterparty-Contract`
 * acknowledgment, cursor paging and the contract-version gate — none of which exist on
 * the Organization surface. Importing it would drag in exactly the protocol this module
 * has been told not to carry over. So this is the same PATTERN, written small for this
 * surface, not a second framework.
 *
 * CORRELATION IS BY REQUEST IDENTITY, NOT BY PATH. Playwright hands the same `Request`
 * object to the `request`, `response` and `requestfailed` events, so each record is
 * keyed by it. Matching "the most recent unfinished record with this pathname" looked
 * equivalent and is not: two reads of the same location that finish out of order would
 * swap their statuses, bodies and sequence numbers — and those are precisely the values
 * used to prove a fresh read happened AFTER a command.
 *
 * A RECORD IS ONLY EVIDENCE ONCE IT IS COMPLETE. `status` arrives with the response
 * HEADERS; the JSON body arrives later. A read whose headers said 200 while its body was
 * still in flight is not a completed read, so `completed()` requires both and the specs
 * poll on that rather than on a rendered heading.
 *
 * NOTHING IS MOCKED. There is no `route.fulfill` in this file. The only interception is
 * `DeliveryWithholder`, which re-issues one REAL request with `route.fetch`, reads the
 * server's REAL answer, and then aborts the BROWSER's delivery of it. That models a lost
 * reply — the only way to reach a genuinely unknown outcome on demand — and it is
 * recorded as a withheld delivery, never as a network fault.
 *
 * REDACTION IS STRUCTURAL. Only a URL's pathname and its query KEYS are ever copied, so
 * no query value can reach the evidence through this path; `authorization` and `cookie`
 * headers are never read at all.
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, type Page, type Request, type Route, type TestInfo } from '@playwright/test'

const here = path.dirname(fileURLToPath(import.meta.url))
export const EVIDENCE_ROOT = process.env.ORG_EVIDENCE_DIR ?? path.join(here, '..', 'artifacts')

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
  requestBody?: unknown
  status?: number
  responseHeaders?: Record<string, string>
  responseBody?: unknown
  /** The request produced no response at all (including a withheld delivery). */
  failed?: string
  /** True once the exchange is finished: a status plus its body, or a failure. */
  complete: boolean
  /** Monotonic ticks, so "this GET was issued after that PUT arrived" is a comparison. */
  issuedAt: number
  /**
   * The tick at which the response ARRIVED — assigned synchronously in the `response`
   * event, before any microtask.
   *
   * This is the ordering marker, and it has to be separate from `settledAt`. Body
   * completion resolves a microtask later, while the application reacts to a 204
   * immediately: measured, the fresh GET that follows a command can therefore be issued
   * BEFORE the command's body-completion tick. Comparing against that tick would skip
   * the genuine fresh read and wait for a second one that never comes — a real run
   * failing on an artefact of how we timestamp.
   */
  receivedAt?: number
  /** The tick at which the exchange finished: body in, or a failure. */
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

/**
 * An exchange that may be used as EVIDENCE: the browser is finished with it AND an
 * answer actually arrived.
 *
 * `complete` alone is not enough, and only the real stack showed why. React's
 * development double-render issues each query twice and aborts the first, so the very
 * first `/organization/locations` record is a genuine `net::ERR_ABORTED` carrying no
 * status. Indexing over `complete` records therefore made "the first list read" that
 * aborted one, and every assertion against it read `undefined`. A withheld delivery is
 * excluded here for the same reason and by design: it is an outcome the screen must
 * handle, never a read to assert a version or a body against.
 *
 * Counting ATTEMPTS is a different question with its own accessor — `issued` — which
 * deliberately still counts aborted requests, because "was this ever asked for?" is
 * exactly what the no-sites case must answer.
 */
function isAnswered(record: LiveRecord): boolean {
  return record.complete && record.failed === undefined && record.status !== undefined
}

export class LiveRecorder {
  readonly records: LiveRecord[] = []
  private readonly byRequest = new Map<Request, LiveRecord>()
  private readonly bodies: Array<Promise<unknown>> = []
  private tick = 0

  private constructor() {}

  static attach(page: Page): LiveRecorder {
    const recorder = new LiveRecorder()

    page.on('request', (request) => {
      const url = new URL(request.url())
      if (!url.pathname.startsWith('/api/v1/')) return
      let body: unknown
      if (request.method() !== 'GET') {
        try {
          body = JSON.parse(request.postData() ?? 'null')
        } catch {
          body = undefined
        }
      }
      const record: LiveRecord = {
        seq: recorder.records.length + 1,
        method: request.method(),
        path: url.pathname,
        queryKeys: [...url.searchParams.keys()],
        requestHeaders: pick(request.headers(), RECORDED_REQUEST_HEADERS),
        requestBody: body,
        complete: false,
        issuedAt: (recorder.tick += 1),
      }
      recorder.records.push(record)
      recorder.byRequest.set(request, record)
    })

    page.on('response', (response) => {
      // Resolved through the REQUEST OBJECT, so overlapping same-path reads cannot swap.
      const record = recorder.byRequest.get(response.request())
      if (record === undefined) return
      record.status = response.status()
      record.responseHeaders = pick(response.headers(), RECORDED_RESPONSE_HEADERS)
      // Synchronous: the order marker must not wait on a microtask. See `receivedAt`.
      record.receivedAt = (recorder.tick += 1)

      /**
       * A 204 carries no body, so there is nothing to wait for and it completes here.
       * Keeping it on the body path was the bug: its completion tick landed after the
       * application had already issued the fresh GET that follows the command.
       */
      if (response.status() === 204) {
        record.settledAt = record.receivedAt
        record.complete = true
        return
      }

      const body = response
        .json()
        .then((value: unknown) => {
          record.responseBody = value
        })
        .catch(() => {
          /* Unparseable or empty problem bodies are legitimately absent. */
        })
        .finally(() => {
          // For a JSON answer, headers alone are not a completed read.
          record.settledAt = (recorder.tick += 1)
          record.complete = true
        })
      recorder.bodies.push(body)
    })

    page.on('requestfailed', (request) => {
      const record = recorder.byRequest.get(request)
      if (record === undefined) return
      // A withheld delivery lands here. It is an outcome, not a hang — mark it complete
      // so `drain()` cannot wait forever on an answer that will never be delivered.
      record.failed = request.failure()?.errorText ?? 'failed'
      record.settledAt = (recorder.tick += 1)
      record.complete = true
    })

    return recorder
  }

  /** Awaits every body this recorder has started reading. */
  async drain(): Promise<void> {
    await Promise.allSettled(this.bodies)
  }

  /** Answered exchanges only — the ones that may be used as evidence. */
  completed(pattern: RegExp, method?: string): LiveRecord[] {
    return this.records.filter(
      (r) => isAnswered(r) && pattern.test(r.path) && (method === undefined || r.method === method),
    )
  }

  /** Every request, complete or not. Use for "was this ever issued?" assertions. */
  issued(pattern: RegExp, method?: string): LiveRecord[] {
    return this.records.filter(
      (r) => pattern.test(r.path) && (method === undefined || r.method === method),
    )
  }

  activeStatePuts(): LiveRecord[] {
    return this.completed(ACTIVE_STATE_RE, 'PUT')
  }

  siteReads(): LiveRecord[] {
    return this.completed(SITES_RE, 'GET')
  }

  issuedSiteReads(): LiveRecord[] {
    return this.issued(SITES_RE, 'GET')
  }

  locationPageReads(): LiveRecord[] {
    return this.completed(LOCATIONS_RE, 'GET')
  }

  locationDetailReads(): LiveRecord[] {
    return this.completed(DETAIL_RE, 'GET')
  }

  /**
   * Waits for a COMPLETED record matching `pick`, and returns it.
   *
   * This is the bounded await the acceptance cases use instead of trusting a rendered
   * heading or a receipt notice. Those render on their own schedule: the list heading
   * appears before the data lands, and the success notice appears before the fresh read
   * that follows a 204 has finished. Polling the traffic itself is what makes the
   * assertion about the exchange rather than about paint timing — and it is what stops
   * ordinary real-API latency from failing the suite.
   */
  async waitFor(
    description: string,
    select: (records: readonly LiveRecord[]) => LiveRecord | undefined,
    timeout = 20_000,
  ): Promise<LiveRecord> {
    await expect
      .poll(() => (select(this.records) === undefined ? 0 : 1), {
        timeout,
        message: `timed out waiting for ${description}`,
      })
      .toBe(1)
    return select(this.records)!
  }

  /** The nth completed request of a kind, waited for. */
  waitForNth(
    description: string,
    pattern: RegExp,
    method: string,
    index: number,
    timeout?: number,
  ): Promise<LiveRecord> {
    return this.waitFor(
      description,
      (records) =>
        records.filter(
          (r) => isAnswered(r) && r.method === method && pattern.test(r.path),
        )[index],
      timeout,
    )
  }

  /**
   * A COMPLETED read of `pattern` that was ISSUED after `tick`.
   *
   * Callers pass the command's `receivedAt`, not its `settledAt`: the client can only
   * have started this read once the answer ARRIVED, and that is the fact being asserted.
   * The read itself must still be complete — a fresh GET whose body has not landed
   * proves nothing about what the screen now shows.
   */
  waitForAfter(
    description: string,
    pattern: RegExp,
    method: string,
    tick: number,
    timeout?: number,
  ): Promise<LiveRecord> {
    return this.waitFor(
      description,
      (records) =>
        records.find(
          (r) => isAnswered(r) && r.method === method && pattern.test(r.path) && r.issuedAt > tick,
        ),
      timeout,
    )
  }

  async write(group: string, file: string): Promise<string> {
    await this.drain()
    return writeEvidence(group, file, { records: this.records })
  }
}

export const ACTIVE_STATE_RE = /^\/api\/v1\/organization\/locations\/[^/]+\/active-state$/
export const SITES_RE = /^\/api\/v1\/organization\/sites$/
export const LOCATIONS_RE = /^\/api\/v1\/organization\/locations$/
export const DETAIL_RE = /^\/api\/v1\/organization\/locations\/[0-9a-fA-F-]+$/

/**
 * Sends ONE real command, reads the server's real answer, then withholds the browser's
 * delivery of it.
 *
 * This is how a genuine unknown outcome is produced without lying about anything: the
 * command really is committed server-side, and the client really does not learn the
 * result. A synthesised 503 would exercise our error branch; this reproduces the actual
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

  /** Bounded wait for the withheld exchange to have happened. */
  async waitForWithheld(timeout = 20_000): Promise<void> {
    await expect
      .poll(() => this.withheldStatus, { timeout, message: 'timed out waiting for the withheld command' })
      .not.toBeNull()
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

/**
 * Attaches the traffic of the case that just ran.
 *
 * The recorder MUST be the one started at the beginning of that case. An earlier version
 * attached a freshly created recorder, which by construction had observed nothing — so
 * the promised diagnostic was an empty file exactly when it was needed.
 */
export async function attachEvidence(
  testInfo: TestInfo,
  recorder: LiveRecorder,
  group: string,
): Promise<void> {
  const slug = testInfo.title.replace(/[^a-z0-9]+/gi, '-').slice(0, 80)
  const file = await recorder.write(group, `${slug}.json`)
  await testInfo.attach('organization-network', { path: file, contentType: 'application/json' })
}
