/**
 * L lane harness: LIVE API through the Vite proxy. The browser talks to the
 * C-DESKTOP-006 loopback API exactly as a user's browser would.
 *
 * `route.fulfill` is never called here and there is no mock of any kind — no
 * synthesised status, body, replay header or acknowledgment exists in this lane. The
 * one thing that is not pure observation is `DeliveryWithholder` below: it re-issues a
 * single real POST to the real API with `route.fetch`, and once that real 201 and its
 * real identity have been seen it aborts the BROWSER's delivery of it. That models a
 * lost reply, which is the only way to reach a genuine unknown outcome on demand, and
 * the evidence records it as a withheld delivery rather than as a network fault.
 *
 * Every `/api/v1/**` request and response is recorded: method, path, query keys,
 * status, the company header, the idempotency key (the attempt identity), the POST
 * body, and the replay/correlation response headers. Structural redaction: any
 * `authorization` or `cookie` header is never copied, and query values other than
 * their keys are dropped.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import type { APIRequestContext, APIResponse, Page, TestInfo } from '@playwright/test'

export interface LiveRecord {
  seq: number
  /**
   * A single monotonic counter stamped at each stage of the exchange, so "this GET had
   * FINISHED before that POST was issued" is a comparison rather than an inference from
   * wall-clock timestamps that can tie.
   *
   * The three stages are kept apart because they are not the same event. Playwright's
   * `response` is the arrival of HEADERS; `requestfinished` is the body being fully
   * received. A read whose headers said 200 while its body was still coming is NOT a
   * completed read, and treating it as one would let a write go out on a pre-check that
   * had not actually answered — and would let the quiescent barrier report zero
   * outstanding while bytes were still on the wire.
   */
  requestTick: number
  headersTick: number | null
  /**
   * The real end of the network TRANSFER — `requestfinished`, or `requestfailed`.
   *
   * This is the one to order against. The recorder's own body copy is a second, separate
   * round trip (`response.text()` → `Network.getResponseBody`), and its latency belongs
   * to the recorder, not to the application: the honest sequence
   *   GET transfer ends → the app validates it → the same-key POST goes out
   *   → the recorder's copy of that GET body finally resolves
   * is perfectly ordinary, and ordering on the copy would report it as the POST having
   * overtaken its own pre-check.
   */
  transferTick: number | null
  transferOutcome: 'pending' | 'finished' | 'failed'
  /** When the recorder's own body copy settled. Never used to order against the app. */
  captureTick: number | null
  /** Whether the response body was actually captured. A failed read has no body. */
  bodyState: 'pending' | 'complete' | 'unavailable'
  /** Both the transfer and the capture are terminal. This is what quiescence drains on. */
  settledTick: number | null
  at: string
  method: string
  path: string
  queryKeys: string[]
  companyId: string | null
  idempotencyKey: string | null
  devIdentityHeader: string | null
  /**
   * The client-contract handshake, both directions (PR #230 `080e7a1`).
   *
   * Recorded because the acceptance turns on it: a write may only follow a fresh
   * successful GET that came back acknowledged, and "no acknowledgment" is the whole
   * evidence that a node is old. Neither half can be read back from a body.
   */
  contractDeclaration: string | null
  contractAck: string | null
  requestBody: unknown
  status: number | null
  responseHeaders: Record<string, string>
  responseBody: unknown
  failure: string | null
}

/**
 * The C-DESKTOP-006 `environment.json`, as its publisher actually writes it.
 *
 * MIRRORS, never invents: `tools/cari-acceptance-006/publish.py`. The earlier shape
 * here was a flat manifest of our own naming — `syntheticActorIds.browser`,
 * `browserDataPrefix`, `credentialFreeVerifyCommand`, a `safeBootstrapMethodNoSecrets`
 * block — and none of those keys exist in what C006 publishes. Reading them would
 * have produced `undefined` at every use, which is the quiet kind of wrong: the lane
 * would have run against defaults instead of against the owned environment.
 *
 * Only the fields this lane consumes are declared. The envelope carries more
 * (`backendTests`, `frontendReviewReceipt`, `sources`, `resources`, `host`); they are
 * the publisher's evidence, not ours to re-interpret.
 */
export interface EnvironmentManifest {
  readonly contractId: string
  readonly taskId: string
  readonly readyForBrowser: boolean
  readonly preparedAtUtc: string
  readonly runId: string
  readonly instanceId: string
  readonly apiBaseUrl: string
  readonly databaseName: string
  readonly fixtureHash: string
  readonly baselineSha256: string
  readonly smokeSha256: string
  readonly companyAId: string
  readonly companyBId: string
  /** One synthetic actor for the whole lane; sent as `X-Development-User`. */
  readonly actor: string
  /** Every record this lane creates is named with it, so a stray row is attributable. */
  readonly browserPrefix: string
  readonly clientContractHeader: string
  readonly clientContractVersion: string
  /**
   * What the publisher itself observed before handing the environment over: a fresh
   * acknowledged GET per company. The lane asserts these rather than assuming them.
   */
  readonly freshGetObservations: readonly {
    readonly companyId: string
    readonly status: number
    readonly ack: string
    readonly observedAtUtc: string
  }[]
  /** Lifecycle entry points. Owned by the publisher; this lane only reports them. */
  readonly commands: {
    readonly control: string
    readonly start: string
    readonly verify: string
    readonly stop: string
    readonly releaseCheck: string
    readonly teardown: string
  }
  readonly browserAcceptance: string
  readonly erpAccepted: boolean
  readonly productionActivated: boolean
  readonly topology: string
}

/** The handshake version this lane speaks; the envelope must name the same one. */
const CLIENT_CONTRACT_VERSION = 'optional-trade-roles-v1'

const CAPTURED_RESPONSE_HEADERS = ['idempotency-replayed', 'idempotency-original-correlation-id', 'location', 'content-type', 'x-correlation-id', 'traceparent', 'x-erp-counterparty-contract']

export function readManifest(): EnvironmentManifest {
  const file = process.env.CARI009_ENVIRONMENT_MANIFEST
  if (!file) throw new Error('CARI009_ENVIRONMENT_MANIFEST is not set; the L lane needs the C-DESKTOP-006 environment.json')
  const manifest = JSON.parse(readFileSync(file, 'utf8')) as EnvironmentManifest
  if (manifest.readyForBrowser !== true) throw new Error('environment manifest is not readyForBrowser')

  /**
   * Fail loudly on a manifest this lane cannot honestly run against.
   *
   * Every one of these is a value the lane would otherwise silently substitute a
   * default for — and a run against a default is a run against something other than
   * the owned environment, which is exactly what must not be reported as acceptance.
   */
  for (const field of [
    'apiBaseUrl',
    'actor',
    'browserPrefix',
    'companyAId',
    'companyBId',
    'clientContractHeader',
    'clientContractVersion',
    'runId',
    'instanceId',
  ] as const) {
    const value = manifest[field]
    if (typeof value !== 'string' || value.length === 0) {
      throw new Error(`environment manifest is missing ${field}; the L lane will not substitute a default`)
    }
  }
  if (manifest.erpAccepted !== false || manifest.productionActivated !== false) {
    throw new Error('environment manifest claims ERP/production acceptance; the L lane refuses to run against it')
  }
  return manifest
}

/**
 * A read that actually ANSWERED: its TRANSFER finished, the recorder's copy of its body
 * was taken, it succeeded and it was acknowledged. Headers alone are not an answer.
 *
 * Note what this does NOT do: it does not order anything. Use `transferTick` for that —
 * the capture is the recorder's own second round trip and may legitimately land after
 * the write the read authorised.
 */
export function isCompletedRead(record: LiveRecord, contractVersion = CLIENT_CONTRACT_VERSION): boolean {
  return (
    record.transferOutcome === 'finished' &&
    record.bodyState === 'complete' &&
    record.status === 200 &&
    record.contractAck === contractVersion
  )
}

/** A page payload the reader could actually use — not merely 200 with something in it. */
export function isCandidatePagePayload(body: unknown): body is { items: { id?: string; code?: string }[] } {
  return typeof body === 'object' && body !== null && Array.isArray((body as { items?: unknown }).items)
}

/** A completed read whose body is a usable page. This is what a pre-check must be. */
export function isCompletedPageRead(record: LiveRecord, contractVersion = CLIENT_CONTRACT_VERSION): boolean {
  return isCompletedRead(record, contractVersion) && isCandidatePagePayload(record.responseBody)
}

export class LiveRecorder {
  readonly records: LiveRecord[] = []
  private seq = 0
  private tick = 0
  /**
   * Keyed by the Request OBJECT, which is the only exact identity available.
   *
   * The previous key was `method + url + request._guid`, reaching into a private
   * Playwright field with a silent `?? ''` fallback. Had that field ever been absent,
   * two concurrent requests to the same URL would have collided: one record orphaned
   * (forever unanswered, so the quiescent barrier could never drain) and the other
   * stamped with the wrong response — corrupting the very tick ordering the retry proof
   * stands on.
   */
  private readonly byRequest = new WeakMap<object, LiveRecord>()
  /** Raw counts of the page events actually received — observed, never inferred. */
  private readonly events = { request: 0, response: 0, requestFinished: 0, requestFailed: 0 }
  /** Ticks at which the main frame committed a navigation. Diagnostic material only. */
  private readonly navigationTicks: number[] = []

  private constructor(private readonly page: Page) {}

  static attach(page: Page): LiveRecorder {
    const recorder = new LiveRecorder(page)
    page.on('request', (request) => {
      const url = new URL(request.url())
      if (!url.pathname.startsWith('/api/v1/')) return
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
      const record: LiveRecord = {
        seq: ++recorder.seq,
        requestTick: ++recorder.tick,
        headersTick: null,
        transferTick: null,
        transferOutcome: 'pending',
        captureTick: null,
        bodyState: 'pending',
        settledTick: null,
        at: new Date().toISOString(),
        method: request.method().toUpperCase(),
        path: url.pathname,
        queryKeys: [...url.searchParams.keys()],
        companyId: headers['x-company-id'] ?? null,
        idempotencyKey: headers['idempotency-key'] ?? null,
        devIdentityHeader: headers['x-development-user'] ?? null,
        // Named headers only, exactly like the rest of this recorder: `authorization`
        // and `cookie` are never copied, here or anywhere.
        contractDeclaration: headers['x-erp-counterparty-contract'] ?? null,
        contractAck: null,
        requestBody: body,
        status: null,
        responseHeaders: {},
        responseBody: null,
        failure: null,
      }
      recorder.events.request += 1
      recorder.records.push(record)
      recorder.byRequest.set(request, record)
    })
    page.on('response', async (response) => {
      const request = response.request()
      const record = recorder.byRequest.get(request)
      if (!record) return
      recorder.events.response += 1
      record.status = response.status()
      record.headersTick = ++recorder.tick
      const headers = response.headers()
      record.contractAck = headers['x-erp-counterparty-contract'] ?? null
      for (const name of CAPTURED_RESPONSE_HEADERS) {
        if (headers[name] !== undefined) record.responseHeaders[name] = headers[name]
      }
      try {
        const text = await response.text()
        record.responseBody = text.length > 0 ? safeJson(text) : null
        record.bodyState = 'complete'
      } catch {
        // The copy could not be taken — a navigation or a cancellation took it. That is
        // not a completed read, and it must never be able to become one.
        record.responseBody = null
        record.bodyState = 'unavailable'
      }
      record.captureTick = ++recorder.tick
      recorder.settle(record)
    })
    page.on('requestfinished', (request) => {
      const record = recorder.byRequest.get(request)
      if (!record) return
      recorder.events.requestFinished += 1
      record.transferOutcome = 'finished'
      record.transferTick = ++recorder.tick
      recorder.settle(record)
    })
    page.on('requestfailed', (request) => {
      const record = recorder.byRequest.get(request)
      if (!record) return
      recorder.events.requestFailed += 1
      record.failure = request.failure()?.errorText ?? null
      record.transferOutcome = 'failed'
      record.transferTick = ++recorder.tick
      // A failed exchange has no body to wait for; leaving the capture pending would keep
      // the quiescent barrier from ever draining.
      if (record.bodyState === 'pending') record.bodyState = 'unavailable'
      if (record.captureTick === null) record.captureTick = ++recorder.tick
      recorder.settle(record)
    })
    page.on('framenavigated', (frame) => {
      // Main frame only: a committed navigation replaces the document, which is why a
      // request issued by the old one may never be heard from again. Recorded as a tick
      // so an outstanding record can SAY that, without it counting for anything.
      if (frame === page.mainFrame()) recorder.navigationTicks.push(++recorder.tick)
    })
    return recorder
  }

  of(predicate: (record: LiveRecord) => boolean): LiveRecord[] {
    return this.records.filter(predicate)
  }
  creates(): LiveRecord[] {
    return this.of((r) => r.method === 'POST' && r.path === '/api/v1/counterparties')
  }
  links(): LiveRecord[] {
    return this.of((r) => r.method === 'POST' && /^\/api\/v1\/counterparties\/[^/]+\/company-account$/.test(r.path))
  }
  candidateReads(): LiveRecord[] {
    return this.of((r) => r.method === 'GET' && r.path === '/api/v1/counterparties/company-account-link-candidates')
  }
  detailReads(partyId?: string): LiveRecord[] {
    return this.of(
      (r) =>
        r.method === 'GET' &&
        /^\/api\/v1\/counterparties\/[^/]+$/.test(r.path) &&
        !r.path.endsWith('company-account-link-candidates') &&
        (partyId === undefined || r.path.endsWith(`/${partyId}`)),
    )
  }
  listReads(): LiveRecord[] {
    return this.of((r) => r.method === 'GET' && r.path === '/api/v1/counterparties')
  }

  /**
   * Stamps the terminal tick once BOTH the transfer and the body capture have settled.
   *
   * Quiescence drains on this and on nothing weaker: a transfer that ended while the
   * recorder was still copying its body is not a finished exchange for the purpose of
   * bracketing a snapshot.
   */
  private settle(record: LiveRecord): void {
    if (record.settledTick === null && record.transferTick !== null && record.captureTick !== null) {
      record.settledTick = ++this.tick
    }
  }

  /**
   * What was observed about a record that never settled. Description, not a verdict.
   */
  private describe(record: LiveRecord): OutstandingRecord {
    const parts: string[] = []
    if (record.headersTick === null) parts.push('no response headers observed')
    else parts.push('headers observed')
    if (record.transferTick === null) parts.push('no terminal transfer event (requestfinished/requestfailed) observed')
    if (record.bodyState === 'pending') parts.push('body capture not settled')
    if (this.navigationTicks.some((t) => t > record.requestTick)) {
      parts.push(
        'a committed main-frame navigation was observed after this request started — DIAGNOSTIC ONLY: it does not establish that the request terminated, and says nothing about work the server may still have in flight',
      )
    }
    return {
      seq: record.seq, method: record.method, path: record.path, companyId: record.companyId,
      requestTick: record.requestTick, headersTick: record.headersTick, transferTick: record.transferTick,
      captureTick: record.captureTick, transferOutcome: record.transferOutcome, bodyState: record.bodyState,
      status: record.status, failure: record.failure, diagnosis: parts.join('; '),
    }
  }

  /** The page events actually received, as counts. Observed, never inferred. */
  observedEvents(): { request: number; response: number; requestFinished: number; requestFailed: number } {
    return { ...this.events }
  }

  /** Raw state of everything still unaccounted for — safe to call at any time. */
  outstanding(): OutstandingRecord[] {
    return this.inFlight().map((r) => this.describe(r))
  }

  /** The event counter as it stands now — the instant a test action happened. */
  currentTick(): number {
    return this.tick
  }

  /**
   * Requests still in flight — measured on the TERMINAL stage, not on headers.
   *
   * `status !== null` only means headers arrived; a body can still be streaming behind
   * it. Draining on that would let the barrier report zero outstanding while bytes were
   * still on the wire, which is exactly what the snapshot it brackets cannot tolerate.
   */
  inFlight(): LiveRecord[] {
    return this.records.filter((r) => r.settledTick === null)
  }

  /**
   * Waits until the browser is quiet, then states that — and only that.
   *
   * The environment's snapshot reader runs several queries; its before/after comparison
   * only means something once mutations have stopped and nothing is still outstanding.
   * This is the browser half of that barrier. It is not evidence of a server effect or
   * of its absence: that claim belongs to the snapshot.
   */
  async quiesce({ settleMs = 1500, timeoutMs = 30_000 }: { settleMs?: number; timeoutMs?: number } = {}): Promise<QuiescentBarrier> {
    const started = Date.now()
    let lastBusyAt = Date.now()
    for (;;) {
      const markedBusy = (await this.page.locator('[aria-busy="true"]').count()) > 0
      // Re-read AFTER the awaited round trip: a request issued during it would have been
      // invisible to a reading taken before, and the barrier would have returned anyway.
      if (markedBusy || this.inFlight().length > 0) lastBusyAt = Date.now()
      const settledFor = Date.now() - lastBusyAt
      const quietWindowHeld = settledFor >= settleMs
      const timedOut = Date.now() - started > timeoutMs
      if (quietWindowHeld || timedOut) {
        const outstanding = this.outstanding()
        if (outstanding.length > 0 && !timedOut) {
          lastBusyAt = Date.now()
          continue
        }
        /**
         * ACCEPTANCE NEEDS ALL THREE, and a timeout is never one of them.
         *
         * The previous shape returned `accepted: outstanding.length === 0` from the
         * timeout arm too — so a run that expired with a busy DOM, or before the quiet
         * window had ever elapsed, was reported as quiet merely because nothing happened
         * to be outstanding at that instant. A timeout is a diagnostic, not a pass.
         */
        const accepted = quietWindowHeld && !markedBusy && outstanding.length === 0
        /**
         * Returned, never thrown.
         *
         * The first live run threw here, which killed the evidence writer that ran after
         * it — the network and measurement records for a journey that had already written
         * to the database were simply lost. The verdict belongs to the caller's
         * assertion; this method's job is to report, and to report completely.
         *
         * `accepted` is false whenever anything is outstanding. Nothing is discounted:
         * a request whose document was navigated away from is described in `diagnosis`
         * and still counted, because "we stopped hearing about it" is not evidence that
         * it finished, still less that the server has nothing in flight for it.
         */
        return {
          accepted,
          timedOut,
          quietWindowHeld,
          markedBusyAtDecision: markedBusy,
          requiredQuietMs: settleMs,
          observedAtUtc: new Date().toISOString(),
          settledForMs: settledFor,
          inFlightAtBarrier: outstanding.length,
          outstanding,
          observedEvents: { ...this.events },
          totalApiRecords: this.records.length,
          pageWritesIssued: this.records.filter((r) => r.method === 'POST').length,
          meaning:
            'Browser quiescence only: no PAGE-originated /api/v1 request outstanding and nothing marked busy for the settle window. Requests issued on a separate APIRequestContext are not visible here. NOT a claim about database state, which the environment snapshot measures.',
        }
      }
      await this.page.waitForTimeout(100)
    }
  }

  /**
   * Bounded wait for everything issued since `sinceTick` to reach a terminal transfer AND
   * a settled capture.
   *
   * This is what a test owes a navigation it is about to perform. In the first live run
   * the journey switched company — which makes the shell issue scoped reads for the new
   * company — and then immediately called `page.goto`, abandoning those reads mid-flight;
   * the browser never heard about them again and the barrier at the end could never
   * drain. Waiting for them BEFORE navigating is the honest fix: it neither invents a
   * terminal event nor narrows the scenario.
   *
   * `sinceTick` must be taken BEFORE the user action that triggers the reads. Taking it
   * afterwards would leave the action's own requests outside the interval.
   *
   * AN EMPTY PENDING SET IS NOT AN ANSWER, AND NEITHER IS "IT STARTED".
   *
   * Returning as soon as nothing was pending was wrong: that is the state at the first
   * instant after a click, when the picker already holds its new value and the shell's
   * capability chain has not yet issued the scoped read.
   *
   * Requiring only that a matching read STARTED was also wrong, for a sharper reason.
   * StrictMode issues two copies of a mount's first read and cancels one. The cancelled
   * copy is terminal, so it settles; it carries the new company and the right path, so it
   * matches. A wait that counted it would again find an empty pending set at the exact
   * moment the real read had not yet been issued, and let the navigation through — the
   * original collision, reached by a different route.
   *
   * So the condition is COMPLETION: the read this wait exists for must have finished its
   * transfer, had its body captured, answered 200, been acknowledged, and carried a usable
   * page. Only then does an empty pending set mean the work is actually done.
   */
  async settleSince(
    sinceTick: number,
    {
      timeoutMs = 15_000,
      expectCompleted,
      minimumCompleted = 0,
      contractVersion = CLIENT_CONTRACT_VERSION,
    }: {
      timeoutMs?: number
      expectCompleted?: (record: LiveRecord) => boolean
      minimumCompleted?: number
      contractVersion?: string
    } = {},
  ): Promise<SettleSinceResult> {
    const started = Date.now()
    const brief = (r: LiveRecord) => ({ seq: r.seq, method: r.method, path: r.path, companyId: r.companyId, status: r.status })
    const near = (r: LiveRecord) => ({
      seq: r.seq, path: r.path, companyId: r.companyId, status: r.status,
      transferOutcome: r.transferOutcome, bodyState: r.bodyState, failure: r.failure,
    })
    for (;;) {
      const since = this.records.filter((r) => r.requestTick > sinceTick)
      const identified = expectCompleted === undefined ? [] : since.filter(expectCompleted)
      /**
       * COMPLETED, by the same definition the write gate uses: the transfer finished, the
       * body was captured, the status was 200, it was acknowledged, and the payload is a
       * usable page. A cancelled copy, a header-only read whose body never landed, and a
       * response that is not the page this wait is about all fail it.
       */
      const completed = identified.filter((r) => isCompletedPageRead(r, contractVersion))
      const pending = since.filter((r) => r.settledTick === null)
      const enough = completed.length >= minimumCompleted
      if (enough && pending.length === 0) {
        return { settled: true, completedExpected: enough, matched: completed.map(brief), matchedIncomplete: [], outstanding: [] }
      }
      if (Date.now() - started > timeoutMs) {
        return {
          settled: false,
          completedExpected: enough,
          matched: completed.map(brief),
          matchedIncomplete: identified.filter((r) => !isCompletedPageRead(r, contractVersion)).map(near),
          outstanding: pending.map((r) => this.describe(r)),
        }
      }
      await this.page.waitForTimeout(100)
    }
  }

  async finish(testInfo: TestInfo, slug: string, extra: Record<string, unknown> = {}): Promise<string> {
    // Give the last responses a moment to be parsed.
    await this.page.waitForTimeout(300)
    const payload = {
      lane: 'L',
      slug,
      title: testInfo.title,
      /**
       * The MEASUREMENT phase, not the verdict.
       *
       * `TestInfo.status` is only available after the test has finished, and this runs
       * inside the body — so it was always `undefined` and every record claimed the run
       * status was "unknown". The final result belongs to the runner's own report
       * (`results.json` / `junit.xml`); nothing here invents a PASS.
       */
      phase: 'measurement',
      finalTestResult: 'not available inside the test body; see the runner report',
      statusAtWrite: testInfo.status ?? null,
      browserVersion: this.page.context().browser()?.version() ?? null,
      redaction: 'authorization/cookie headers never copied; query values dropped (keys only)',
      records: this.records,
      ...extra,
      finishedAtUtc: new Date().toISOString(),
    }
    const file = lEvidencePath('network', `${slug}.json`)
    writeFileSync(file, `${JSON.stringify(payload, null, 2)}\n`, 'utf8')
    await testInfo.attach('cari009-live-network', { path: file, contentType: 'application/json' })
    return file
  }
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return text.slice(0, 500)
  }
}

export function lEvidencePath(group: string, file: string): string {
  const root = process.env.CARI009_EVIDENCE_DIR
  if (!root) throw new Error('CARI009_EVIDENCE_DIR is not set')
  const dir = path.join(root, 'L', group)
  mkdirSync(dir, { recursive: true })
  return path.join(dir, file)
}

export function writeLJson(group: string, file: string, value: unknown): string {
  const target = lEvidencePath(group, file)
  writeFileSync(target, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
  return target
}

export async function lShot(page: Page, slug: string, label: string): Promise<string> {
  const file = lEvidencePath('screenshots', `${slug}-${label}.png`)
  await page.screenshot({ path: file, fullPage: true })
  return file
}

/**
 * A REAL upstream exchange the browser was not allowed to receive.
 *
 * This is how the journey reaches an unknown outcome without anyone pretending: the
 * request is re-issued to the real API through the same proxy, with the same method,
 * path, company, idempotency key and body the application produced. Only after a real
 * 201 carrying the real acknowledgment and the real Party/account identity has been
 * SEEN is the browser's delivery cut.
 *
 * What this is NOT, and the marker says so in the evidence: the browser did not see a
 * 201, and no natural network fault occurred. The response was withheld by the test
 * after the server had already committed — which is exactly the situation a real user
 * hits when a reply is lost in transit, and the only way to produce it on demand
 * without a mock.
 */
export type WithheldMarker =
  /** A real 201 with the acknowledgment and the real identity was read, then withheld. */
  | 'upstream_success_then_delivery_abort'
  /** The upstream exchange never completed: whether anything committed is UNKNOWN. */
  | 'upstream_not_reached'
  /** The upstream answered, but not with the success this journey requires. */
  | 'upstream_unexpected_result'

export interface WithheldDelivery {
  /**
   * Computed from what the upstream actually returned, never assumed.
   *
   * This is the field a reader keys on to decide whether a snapshot reference is
   * POSTCOMMIT, so a single literal value would have labelled a request that never
   * committed as a post-commit success.
   */
  readonly marker: WithheldMarker
  readonly label: string
  readonly at: string
  readonly method: string
  readonly path: string
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly contractDeclaration: string | null
  /** The real upstream result. Recorded because the attempt's truth depends on it. */
  readonly upstreamStatus: number
  readonly contractAck: string | null
  readonly replayed: string | null
  readonly location: string | null
  /** Identity only — never the whole body. */
  readonly partyId: string | null
  readonly partyCode: string | null
  readonly accounts: readonly { readonly id: string; readonly companyId: string; readonly roles: number; readonly version: string }[]
  readonly deliveredToBrowser: false
  readonly browserOutcome: 'aborted-by-test'
  /** Set when the upstream was NOT the expected success; the spec fails on it. */
  readonly upstreamError: string | null
}

interface WithholdTarget {
  readonly label: string
  readonly method: string
  readonly companyId: string
  readonly match: (pathname: string) => boolean
  done: boolean
}

/** The response headers worth keeping from a withheld exchange — an allowlist, like everywhere else. */
const WITHHELD_HEADERS = ['x-erp-counterparty-contract', 'idempotency-replayed', 'idempotency-original-correlation-id', 'location'] as const

/**
 * Withholds the delivery of the FIRST real POST matching each target, once each.
 *
 * Everything else — including the same-key retry that follows — is passed through
 * untouched with `route.continue()`, so the retry is an ordinary browser request that
 * reaches the real API by itself. Nothing here ever fulfils a response: there is no
 * mock 201, no fabricated replay header and no invented body anywhere in this lane.
 */
export class DeliveryWithholder {
  readonly withheld: WithheldDelivery[] = []
  readonly passedThrough: { at: string; label: string; method: string; path: string; idempotencyKey: string | null }[] = []
  private readonly targets: WithholdTarget[]

  private constructor(targets: WithholdTarget[]) {
    this.targets = targets
  }

  static async install(
    page: Page,
    targets: readonly { label: string; method: string; companyId: string; match: (pathname: string) => boolean }[],
  ): Promise<DeliveryWithholder> {
    const withholder = new DeliveryWithholder(targets.map((t) => ({ ...t, done: false })))
    await page.route(
      (url) => url.pathname.startsWith('/api/v1/counterparties'),
      async (route) => {
        const request = route.request()
        const url = new URL(request.url())
        const headers = request.headers()
        const method = request.method().toUpperCase()
        const key = headers['idempotency-key'] ?? null
        const company = headers['x-company-id'] ?? null
        const target = withholder.targets.find(
          (t) =>
            !t.done &&
            t.method === method &&
            t.companyId === company &&
            key !== null &&
            t.match(url.pathname),
        )
        if (target === undefined) {
          const matched = withholder.targets.find((t) => t.method === method && t.match(url.pathname))
          if (matched !== undefined) {
            withholder.passedThrough.push({ at: new Date().toISOString(), label: matched.label, method, path: url.pathname, idempotencyKey: key })
          }
          await route.continue()
          return
        }
        target.done = true

        let upstream: APIResponse | null = null
        let failure: string | null = null
        try {
          // The real API, through the same proxy the browser uses. No retry and no
          // redirect: one request, one answer, or the evidence is not what it says.
          upstream = await route.fetch({ maxRetries: 0, maxRedirects: 0 })
        } catch (error) {
          failure = String((error as Error)?.message ?? error).slice(0, 200)
        }

        if (upstream === null) {
          withholder.withheld.push({
            marker: 'upstream_not_reached',
            label: target.label,
            at: new Date().toISOString(),
            method,
            path: url.pathname,
            companyId: company,
            idempotencyKey: key,
            contractDeclaration: headers['x-erp-counterparty-contract'] ?? null,
            upstreamStatus: 0,
            contractAck: null,
            replayed: null,
            location: null,
            partyId: null,
            partyCode: null,
            accounts: [],
            deliveredToBrowser: false,
            browserOutcome: 'aborted-by-test',
            upstreamError: failure ?? 'upstream request did not complete',
          })
          await route.abort('failed')
          return
        }

        const responseHeaders = upstream.headers()
        const picked: Record<string, string> = {}
        for (const name of WITHHELD_HEADERS) {
          if (responseHeaders[name] !== undefined) picked[name] = responseHeaders[name]
        }
        let body: unknown = null
        try {
          body = await upstream.json()
        } catch {
          body = null
        }
        const record = body as {
          id?: unknown
          code?: unknown
          companyAccounts?: { id?: unknown; companyId?: unknown; roles?: unknown; version?: unknown }[]
        } | null
        const accounts = Array.isArray(record?.companyAccounts)
          ? record.companyAccounts.map((a) => ({
              id: String(a?.id ?? ''),
              companyId: String(a?.companyId ?? ''),
              roles: Number(a?.roles),
              version: String(a?.version ?? ''),
            }))
          : []
        /**
         * The loss may only be REPORTED as a withheld success once the real 201, the
         * acknowledgment and the real identity have been read. The request itself is
         * already consumed by `route.fetch` and cannot be un-sent — and delivering it
         * would mean `route.fulfill`, which this lane does not use — so an unexpected
         * upstream is aborted like any other and labelled for what it was. The spec then
         * fails on the marker instead of a success being recorded that never happened.
         */
        const status = upstream.status()
        const ack = picked['x-erp-counterparty-contract'] ?? null
        const partyId = typeof record?.id === 'string' ? record.id : null
        const expected = status === 201 && ack === CLIENT_CONTRACT_VERSION && partyId !== null && accounts.length > 0
        withholder.withheld.push({
          marker: expected ? 'upstream_success_then_delivery_abort' : 'upstream_unexpected_result',
          label: target.label,
          at: new Date().toISOString(),
          method,
          path: url.pathname,
          companyId: company,
          idempotencyKey: key,
          contractDeclaration: headers['x-erp-counterparty-contract'] ?? null,
          upstreamStatus: status,
          contractAck: ack,
          replayed: picked['idempotency-replayed'] ?? null,
          location: picked.location ?? null,
          partyId,
          partyCode: typeof record?.code === 'string' ? record.code : null,
          accounts,
          deliveredToBrowser: false,
          browserOutcome: 'aborted-by-test',
          upstreamError: expected ? null : `upstream answered ${status} with ack ${String(ack)}; the expected 201 + acknowledgment + identity was not seen`,
        })
        // Only the delivery is cut. The server has already committed.
        await route.abort('failed')
      },
    )
    return withholder
  }

  of(label: string): WithheldDelivery[] {
    return this.withheld.filter((w) => w.label === label)
  }
}

/**
 * A few requests issued as a DIFFERENT synthetic actor, from their own request context.
 *
 * The browser cannot do this: `VITE_DEV_USER` is baked into the application bundle at
 * dev-server start (`api.ts` reads `import.meta.env`), so every page in every browser
 * context sends the same actor. Changing it would mean restarting the server as
 * somebody else — which is precisely the "silently change the main actor" this must
 * not do. So the negatives travel on their own APIRequestContext, against the same
 * proxy origin, labelled as their own records.
 */
export interface ActorProbeRecord {
  readonly at: string
  readonly label: string
  readonly actor: string
  readonly method: string
  /** Pathname only. Query VALUES are never recorded here either — keys only, below. */
  readonly path: string
  readonly queryKeys: string[]
  readonly companyId: string
  readonly status: number
  readonly contractAck: string | null
  /**
   * Whether the response body mentioned a value it must not disclose — or `null` when
   * no forbidden text was supplied for this probe. A flat `false` would have read as
   * "checked and clean" on a probe nobody checked.
   */
  readonly disclosedForbiddenText: boolean | null
  /**
   * The stable ProblemDetails code. An authorization refusal and a business conflict are
   * different answers, and "some 4xx" cannot tell them apart — a 409 already_linked
   * would otherwise pass as proof that the authorization boundary held.
   */
  readonly problemCode: string | null
  readonly createdPartyId: string | null
}

export class ActorProbe {
  readonly records: ActorProbeRecord[] = []

  constructor(
    private readonly request: APIRequestContext,
    private readonly contractVersion: string,
  ) {}

  /**
   * One request as `actor`. `mustNotDisclose` is checked against the raw body text, so
   * a 403 that leaks a name it should not is a failure the caller can assert on.
   */
  async send(options: {
    label: string
    actor: string
    method: 'GET' | 'POST'
    path: string
    companyId: string
    data?: unknown
    idempotencyKey?: string
    mustNotDisclose?: readonly string[]
  }): Promise<ActorProbeRecord> {
    const headers: Record<string, string> = {
      'X-Development-User': options.actor,
      'X-Company-Id': options.companyId,
      'X-ERP-Counterparty-Contract': this.contractVersion,
    }
    if (options.idempotencyKey !== undefined) headers['Idempotency-Key'] = options.idempotencyKey
    const response =
      options.method === 'GET'
        ? await this.request.get(options.path, { headers, failOnStatusCode: false })
        : await this.request.post(options.path, { headers, data: options.data ?? {}, failOnStatusCode: false })
    const text = await response.text().catch(() => '')
    let createdPartyId: string | null = null
    let problemCode: string | null = null
    try {
      const parsed = JSON.parse(text) as { id?: unknown; code?: unknown }
      if (response.status() < 300 && typeof parsed.id === 'string') createdPartyId = parsed.id
      if (typeof parsed.code === 'string') problemCode = parsed.code
    } catch {
      createdPartyId = null
    }
    // Split exactly like the page recorder does: pathname, plus the KEYS of any query.
    const parsed = new URL(options.path, 'http://127.0.0.1')
    const record: ActorProbeRecord = {
      at: new Date().toISOString(),
      label: options.label,
      actor: options.actor,
      method: options.method,
      path: parsed.pathname,
      queryKeys: [...parsed.searchParams.keys()],
      companyId: options.companyId,
      status: response.status(),
      contractAck: response.headers()['x-erp-counterparty-contract'] ?? null,
      problemCode,
      disclosedForbiddenText:
        options.mustNotDisclose === undefined || options.mustNotDisclose.length === 0
          ? null
          : options.mustNotDisclose.some((needle) => text.includes(needle)),
      createdPartyId,
    }
    this.records.push(record)
    return record
  }
}

/**
 * What the browser can honestly promise a database snapshot taken after it.
 *
 * The environment's `control.ps1 -Action snapshot` is a multi-query reader, not an
 * atomic whole-database snapshot, so its before/after comparison is only meaningful
 * once mutations have stopped and nothing is still in flight. This is the browser half
 * of that barrier: no `/api/v1` request outstanding, nothing marked busy on the page,
 * and a settle window during which neither became true again.
 *
 * It states quiescence and nothing else. A quiet browser is not evidence of a server
 * effect or of its absence — that claim belongs to the snapshot, not here.
 */
/**
 * One request the barrier could not account for, with the raw state it was in.
 *
 * This exists because the first live run lost exactly this: the barrier threw, the
 * evidence writer never ran, and answering "which two requests?" took an excavation of
 * the Playwright trace. Every unsettled record is now written out with its own ticks.
 */
export interface OutstandingRecord {
  readonly seq: number
  readonly method: string
  readonly path: string
  readonly companyId: string | null
  readonly requestTick: number
  readonly headersTick: number | null
  readonly transferTick: number | null
  readonly captureTick: number | null
  readonly transferOutcome: 'pending' | 'finished' | 'failed'
  readonly bodyState: 'pending' | 'complete' | 'unavailable'
  readonly status: number | null
  readonly failure: string | null
  /**
   * DIAGNOSTIC ONLY — describes what was observed, and is never subtracted from the
   * outstanding count.
   *
   * In particular, "a committed navigation was observed after this request started" does
   * NOT establish that the request terminated, and says nothing at all about whether the
   * server still has work in flight for it. A barrier that discounted these would be
   * manufacturing an acceptance out of an absence of evidence.
   */
  readonly diagnosis: string
}

/**
 * Which failure a run should report when both the barrier and the evidence write can fail.
 *
 * The two must not mask each other. A failed barrier is what the run is FOR, so it stays
 * primary and the write error rides along as a secondary diagnosis. But a barrier that
 * held does not license losing the evidence either: a mandatory write that failed is then
 * itself the failure, and the test must not pass merely because the browser went quiet.
 *
 * Pure on purpose, so both specs share one decision and it can be exercised directly.
 */
export function failurePriority(
  barrierAccepted: boolean,
  evidenceError: unknown,
): { primary: 'barrier' | 'evidence' | null; secondary: 'evidence' | null } {
  const wrote = evidenceError === null || evidenceError === undefined
  if (!barrierAccepted) return { primary: 'barrier', secondary: wrote ? null : 'evidence' }
  return { primary: wrote ? null : 'evidence', secondary: null }
}

/** A short, safe rendering of an evidence-write failure for an assertion message. */
export function evidenceErrorText(evidenceError: unknown): string {
  if (evidenceError === null || evidenceError === undefined) return ''
  return String((evidenceError as { message?: string })?.message ?? evidenceError).slice(0, 300)
}

export interface SettleSinceResult {
  /** Everything issued in the interval reached a terminal transfer AND a settled capture. */
  readonly settled: boolean
  /**
   * The read this wait exists for was observed to COMPLETE — not merely to start.
   *
   * "Started" was not enough. Under the dev server StrictMode issues two copies of a
   * mount's first read and cancels one; the cancelled copy is terminal, so it settles,
   * and it matches the company and the path just as well as the real one. A wait that
   * accepted it would find an empty pending set at the very moment the real read had not
   * been issued yet, and wave the navigation straight through.
   */
  readonly completedExpected: boolean
  readonly matched: { seq: number; method: string; path: string; companyId: string | null; status: number | null }[]
  /** Matching reads that did NOT complete — kept so the near miss is visible. */
  readonly matchedIncomplete: { seq: number; path: string; companyId: string | null; status: number | null; transferOutcome: string; bodyState: string; failure: string | null }[]
  readonly outstanding: OutstandingRecord[]
}

export interface QuiescentBarrier {
  /** True only when the quiet window held, nothing was busy, and nothing was outstanding. */
  readonly accepted: boolean
  /** Diagnostic. A timeout never contributes to `accepted`. */
  readonly timedOut: boolean
  readonly quietWindowHeld: boolean
  readonly markedBusyAtDecision: boolean
  readonly requiredQuietMs: number
  readonly observedAtUtc: string
  readonly settledForMs: number
  /** Re-read at return time. A literal zero here would be a declaration, not a barrier. */
  readonly inFlightAtBarrier: number
  /** Every record still unaccounted for, in full. Empty exactly when accepted. */
  readonly outstanding: OutstandingRecord[]
  /** Raw counts of the page events this recorder actually received. */
  readonly observedEvents: { request: number; response: number; requestFinished: number; requestFailed: number }
  readonly totalApiRecords: number
  /**
   * PAGE-originated writes only. A separate APIRequestContext (the negative-actor probes)
   * never reaches this recorder, so calling this "writes issued" would under-report what
   * happened inside the bracket the snapshot is taken around.
   */
  readonly pageWritesIssued: number
  readonly meaning: string
}

export class BarrierNotReached extends Error {}
