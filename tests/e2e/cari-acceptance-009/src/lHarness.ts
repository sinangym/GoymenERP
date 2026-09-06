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
  requestBody: unknown
  status: number | null
  responseHeaders: Record<string, string>
  responseBody: unknown
  failure: string | null
}

export interface EnvironmentManifest {
  readonly contractId: string
  readonly taskId: string
  readonly readyForBrowser: boolean
  readonly preparedAtUtc: string
  readonly runId: string
  readonly apiBaseUrl: string
  readonly databaseName: string
  readonly databaseEndpointLoopback: string
  readonly backendHead: string
  readonly backendDiffSha256: string
  readonly frontendDiffSha256: string
  readonly fixtureHash: string
  readonly companyAId: string
  readonly companyBId: string
  readonly syntheticActorIds: { readonly browser: string }
  readonly safeBootstrapMethodNoSecrets: { readonly headers: Record<string, string> }
  readonly browserDataPrefix: string
  readonly allowedUiOrigins: readonly string[]
  readonly credentialFreeVerifyCommand: string
  readonly credentialFreeSnapshotCommand: string
}

const CAPTURED_RESPONSE_HEADERS = ['idempotency-replayed', 'idempotency-original-correlation-id', 'location', 'content-type', 'x-correlation-id', 'traceparent']

export function readManifest(): EnvironmentManifest {
  const file = process.env.CARI009_ENVIRONMENT_MANIFEST
  if (!file) throw new Error('CARI009_ENVIRONMENT_MANIFEST is not set; the L lane needs the C-DESKTOP-006 environment.json')
  const manifest = JSON.parse(readFileSync(file, 'utf8')) as EnvironmentManifest
  if (manifest.readyForBrowser !== true) throw new Error('environment manifest is not readyForBrowser')
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
