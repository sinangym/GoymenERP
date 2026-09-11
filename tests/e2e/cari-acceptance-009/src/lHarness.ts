/**
 * L lane harness: LIVE API through the Vite proxy — no `page.route`, no
 * `route.fulfill`, no mock of any kind. The browser talks to the C-DESKTOP-006
 * loopback API exactly as a user's browser would; this module only OBSERVES.
 *
 * Every `/api/v1/**` request and response is recorded: method, path, query keys,
 * status, the company header, the idempotency key (the attempt identity), the POST
 * body, and the replay/correlation response headers. Structural redaction: any
 * `authorization` or `cookie` header is never copied, and query values other than
 * their keys are dropped.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import type { Page, TestInfo } from '@playwright/test'

export interface LiveRecord {
  seq: number
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
  private readonly pending = new Map<string, LiveRecord>()

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
      recorder.pending.set(requestKey(request), record)
    })
    page.on('response', async (response) => {
      const request = response.request()
      const record = recorder.pending.get(requestKey(request))
      if (!record) return
      record.status = response.status()
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
      const record = recorder.pending.get(requestKey(request))
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

function requestKey(request: { url(): string; method(): string; timing?: unknown }): string {
  // Playwright request objects are stable identities; key them by object via a WeakMap-free trick:
  // url+method+postData is not unique for retries, so use the object's own identity string.
  return `${request.method()} ${request.url()} #${(request as unknown as { _guid?: string })._guid ?? ''}`
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
