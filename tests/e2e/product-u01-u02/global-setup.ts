/**
 * Warm-up and run record, the C010 way: a cold Vite dev server pays dependency
 * optimization and first transforms on the FIRST page load, and that cost must not
 * land inside a test's own timeout. One real navigation of the list route happens
 * here before any test; what it cost is recorded, never hidden by a retry. A warm-up
 * that cannot render the route is recorded and then aborts the run before any test.
 *
 * T lane: the same controlled responses the tests use. L lane: NO controlled
 * response exists — the warm-up opens the list against the live API as the manifest's
 * actor and records only what it observed; it asserts nothing about the data.
 */
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { chromium, type FullConfig } from '@playwright/test'
import { Lane } from './src/harness'
import { origin, runDir, runId, viteCacheDir, coldStart, role, claimedAtUtc, lane, liveBinding } from './paths'

export default async function globalSetup(_config: FullConfig): Promise<void> {
  mkdirSync(path.join(runDir, 'diagnostics'), { recursive: true })
  const startedAtUtc = new Date().toISOString()
  const t0 = Date.now()
  const events: { atMs: number; kind: string; detail: string }[] = []
  const push = (kind: string, detail = '') => events.push({ atMs: Date.now() - t0, kind, detail })
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, locale: 'tr-TR' })
  page.on('console', (message) => {
    const type = message.type()
    if (type === 'error' || type === 'warning') push('console-' + type, message.text().slice(0, 300))
  })
  page.on('pageerror', (error) => push('pageerror', String(error.message).slice(0, 300)))
  page.on('requestfailed', (request) => push('requestfailed', request.method() + ' ' + new URL(request.url()).pathname + ' ' + (request.failure()?.errorText ?? '')))
  let outcome = 'ok'
  let headingMs: number | null = null
  let optimizerWaitMs: number | null = null
  let secondPassHeadingMs: number | null = null
  const metadata = path.join(viteCacheDir, 'deps', '_metadata.json')
  const controlled = lane === 'T' ? await Lane.install(page, { slug: 'warm-up' }) : null
  if (lane === 'L' && liveBinding !== null) {
    await page.addInitScript((company) => localStorage.setItem('goymen-company-id', company), liveBinding.companyId)
    push('live-binding', `contract=${liveBinding.contractId} resourceRun=${liveBinding.resourceRunId} instance=${liveBinding.resourceInstanceId} manifestSha256=${liveBinding.manifestSha256}`)
  }
  try {
    await page.goto(origin + '/urun', { waitUntil: 'domcontentloaded', timeout: 180_000 })
    push('domcontentloaded')
    await page.getByRole('heading', { level: 1, name: 'Ürünler' }).waitFor({ state: 'visible', timeout: 180_000 })
    headingMs = Date.now() - t0
    push('heading-visible')
    if (controlled !== null) {
      await page.getByRole('link', { name: 'SAC-42' }).waitFor({ state: 'visible', timeout: 60_000 })
      push('rows-visible')
    }
    const waitStart = Date.now()
    while (!existsSync(metadata) && Date.now() - waitStart < 120_000) await new Promise((resolve) => setTimeout(resolve, 250))
    optimizerWaitMs = Date.now() - waitStart
    push(existsSync(metadata) ? 'optimizer-metadata-present' : 'optimizer-metadata-absent-after-wait', `${optimizerWaitMs}ms`)
    await page.waitForTimeout(500)
    const secondStart = Date.now()
    await page.goto(origin + '/urun', { waitUntil: 'domcontentloaded', timeout: 180_000 })
    await page.getByRole('heading', { level: 1, name: 'Ürünler' }).waitFor({ state: 'visible', timeout: 60_000 })
    secondPassHeadingMs = Date.now() - secondStart
    push('second-pass-heading-visible', `${secondPassHeadingMs}ms`)
  } catch (error) {
    outcome = 'failed: ' + String(error instanceof Error ? error.message : error).slice(0, 300)
    push('warm-up-failure', outcome)
  }
  controlled?.dispose()
  const browserVersion = browser.version()
  await browser.close()
  const serviceDir = path.join(runDir, 'service')
  const service = existsSync(serviceDir)
    ? readdirSync(serviceDir).filter((name) => name.startsWith('ui-')).map((name) => JSON.parse(readFileSync(path.join(serviceDir, name), 'utf8')) as Record<string, unknown>)
    : []
  writeFileSync(path.join(runDir, 'diagnostics', 'warm-up.json'), JSON.stringify({
    task_id: 'U-01-U-02', lane, runId, role, coldStart, claimedAtUtc, viteCacheDir, startedAtUtc, finishedAtUtc: new Date().toISOString(), browserVersion,
    liveBinding: liveBinding === null ? null : { contractId: liveBinding.contractId, resourceRunId: liveBinding.resourceRunId, resourceInstanceId: liveBinding.resourceInstanceId, manifestSha256: liveBinding.manifestSha256, acceptedContractSha256: liveBinding.acceptedContractSha256, sourceBindingSha256: liveBinding.sourceBindingSha256 },
    outcome, warmUpMs: Date.now() - t0, headingVisibleMs: headingMs, optimizerWaitMs, secondPassHeadingMs,
    cacheMetadataPresentAfterWarmUp: existsSync(metadata),
    controlledRequests: controlled?.records.map((r) => ({ kind: r.kind, status: r.status, transport: r.transport, browserFinished: r.browserFinished, browserCancelled: r.browserCancelled })) ?? null,
    externalBlocked: controlled?.externalBlocked ?? null,
    service: service.map((s) => ({ pid: s.pid, configLoadedAtUtc: s.configLoadedAtUtc, listeningAtUtc: s.listeningAtUtc, startupMs: s.startupMs, coldStart: s.coldStart, lane: s.lane })),
    events,
    note: lane === 'T'
      ? 'Same controlled API responses as the tests; no live service. A failed warm-up is recorded here first and then aborts the run before any test.'
      : 'Live API through the proxy; nothing fulfilled, nothing asserted about data. A failed warm-up is recorded here first and then aborts the run before any test.',
  }, null, 2) + '\n')
  if (outcome !== 'ok') throw new Error('U-01/U-02 warm-up did not render the list route; the run is aborted before any test: ' + outcome)
}
