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
   * A single monotonic counter stamped when the request goes out and again when its
   * response is parsed, so "this GET had ANSWERED before that POST was issued" is a
   * comparison rather than an inference from wall-clock timestamps that can tie.
   */
  requestTick: number
  responseTick: number | null
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
        responseTick: null,
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
      recorder.records.push(record)
      recorder.byRequest.set(request, record)
    })
    page.on('response', async (response) => {
      const request = response.request()
      const record = recorder.byRequest.get(request)
      if (!record) return
      record.status = response.status()
      record.responseTick = ++recorder.tick
      const headers = response.headers()
      record.contractAck = headers['x-erp-counterparty-contract'] ?? null
      for (const name of CAPTURED_RESPONSE_HEADERS) {
        if (headers[name] !== undefined) record.responseHeaders[name] = headers[name]
      }
      try {
        const text = await response.text()
        record.responseBody = text.length > 0 ? safeJson(text) : null
      } catch {
        record.responseBody = null
      }
    })
    page.on('requestfailed', (request) => {
      const record = recorder.byRequest.get(request)
      if (record) record.failure = request.failure()?.errorText ?? 'unknown'
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

  /** The event counter as it stands now — the instant a test action happened. */
  currentTick(): number {
    return this.tick
  }

  /** Requests this recorder has seen go out and not yet seen finish, either way. */
  inFlight(): LiveRecord[] {
    return this.records.filter((r) => r.status === null && r.failure === null)
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
      if (settledFor >= settleMs) {
        const outstanding = this.inFlight().length
        if (outstanding > 0) {
          lastBusyAt = Date.now()
          continue
        }
        return {
          accepted: true,
          observedAtUtc: new Date().toISOString(),
          settledForMs: settledFor,
          inFlightAtBarrier: outstanding,
          totalApiRecords: this.records.length,
          pageWritesIssued: this.records.filter((r) => r.method === 'POST').length,
          meaning:
            'Browser quiescence only: no PAGE-originated /api/v1 request outstanding and nothing marked busy for the settle window. Requests issued on a separate APIRequestContext are not visible here. NOT a claim about database state, which the environment snapshot measures.',
        }
      }
      if (Date.now() - started > timeoutMs) {
        throw new BarrierNotReached(`the browser did not go quiet within ${timeoutMs} ms; ${this.inFlight().length} request(s) still outstanding`)
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
      status: testInfo.status ?? 'unknown',
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
    try {
      const parsed = JSON.parse(text) as { id?: unknown }
      if (response.status() < 300 && typeof parsed.id === 'string') createdPartyId = parsed.id
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
export interface QuiescentBarrier {
  readonly accepted: boolean
  readonly observedAtUtc: string
  readonly settledForMs: number
  /** Re-read at return time. A literal zero here would be a declaration, not a barrier. */
  readonly inFlightAtBarrier: number
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
