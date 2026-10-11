import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Request, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

/**
 * ST-02 scrap write-off (hurda/fire çıkışı) on the REAL API and UI of the r14 disposable stack. Nothing is mocked.
 *
 * Authority (the AT-037 pattern): `inventory.stock-scrap.record` is held ONLY by the disposable r14-scrapper and
 * `inventory.stock-scrap.approve` ONLY by the disposable r14-scrap-approver (r14.override.yml) — two different actors.
 * The scrap approval authority is undecided, so every scrap waits for an actor other than its recorder (fail-closed
 * maker ≠ checker); the reason is free text.
 *
 * Journey:
 *  1. Precondition: the recorder records one 10 kg opening label through the real opening command.
 *  2. The scrapper opens the label from ST-01 and records a 4 kg scrap with a reason; the server answers a pending
 *     scrap leaving 6 kg and writes no stock.
 *  3. Close and reopen: the scrap reads "Onay bekliyor", the label still reads 10 kg and the scrapper is offered no
 *     decision; its direct decision and the approver's direct scrap are 403; the same key replays the scrap once.
 *  4. The approver opens the label and approves the scrap; exactly one Scrap movement (kind 7) is posted.
 *  5. Close and reopen: the scrap reads approved with its movement, the label reads 6 kg with a "Hurda çıkışı" line in
 *     its movement timeline, and ST-01 lists 6 kg; the same decision key replays with nothing new.
 *  6. Fences: a scrap at the consumed label version is 409, more than the book is 409, another company context is
 *     refused; nothing is written.
 */
const env = readEnvironment()
const { recorderActor, deniedActor, arrangerActor, transformerActor, counterActor, countApproverActor, scrapperActor,
  scrapApproverActor, companyId, otherCompanyId, locationId, productId, productVersion } = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const OPENINGS = '/api/v1/inventory/opening-counts'
const STOCK_SCRAPS = '/api/v1/inventory/stock-scraps'
const POSITIONS = '/api/v1/inventory/positions'
const CAPABILITIES = '/api/v1/shell/capabilities'
const RECORD_CAPABILITY = 'inventory.stock-scrap.record'
const APPROVE_CAPABILITY = 'inventory.stock-scrap.approve'
const LABEL_DETAIL_CAPABILITY = 'inventory.stock-positions.list'
const COUNTED_ON = '2026-09-18'
const SHEET = 'SENTETIK browser-suite:r14 ST-02 scrap source'
const LABEL = 'SYNR22-SCRAP-1'
const SCRAP_REASON = 'SENTETIK ST-02 hurda: kenar ezilmesi'
const DECISION_NOTE = 'SENTETIK ST-02 ikinci kişi onayı'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const PANEL_HEADING = 'Hurda / fire çıkışı'

interface StockScrapView {
  readonly scrapId: string
  readonly companyId: string
  readonly labelId: string
  readonly labelVersion: string
  readonly bookKg: string
  readonly scrapKg: string
  readonly remainingKg: string
  readonly reason: string
  readonly status: 'PendingApproval' | 'Approved' | 'Rejected'
  readonly decision: null | {
    readonly decision: string
    readonly reason: string
    readonly scrapMovementId: string | null
  }
}

interface RecordRequest {
  readonly scrapId: string
  readonly labelId: string
  readonly expectedLabelVersion: string
  readonly scrapKg: string
  readonly scrappedAtUtc: string
  readonly reason: string
}

interface LabelDetail {
  readonly labelVersion: string
  readonly identity: { readonly labelId: string; readonly code: string; readonly companyId: string }
  readonly balances: { readonly labelId: string; readonly physicalKg: string; readonly freeKg: string }
}

interface Effects {
  readonly scraps: number
  readonly decisions: number
  readonly scrapMovements: number
  readonly audits: number
}

function apiHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company }
}

function commandHeaders(actor: string, key: string, company = companyId): Record<string, string> {
  return { ...apiHeaders(actor, company), 'Content-Type': 'application/json', 'Idempotency-Key': key }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function isCall(response: Response, method: 'GET' | 'POST', path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

async function capabilitiesOf(page: Page, actor: string): Promise<readonly string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${CAPABILITIES}`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(snapshot.capabilities) ? snapshot.capabilities as string[] : []
}

async function readDetail(page: Page, actor: string, labelId: string): Promise<LabelDetail> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/labels/${labelId}/detail`, {
    headers: apiHeaders(actor),
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as LabelDetail
}

async function readScraps(page: Page, actor: string, labelId: string): Promise<readonly StockScrapView[]> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/labels/${labelId}/stock-scraps`, {
    headers: apiHeaders(actor),
  })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly labelId: string; readonly items: readonly StockScrapView[]; readonly windowFull: boolean }
  expect(body.labelId).toBe(labelId)
  expect(body.windowFull).toBe(false)
  return body.items
}

/** Opens a label detail with a fresh navigation and waits for the detail read and the label's scrap read. */
async function openLabel(page: Page, labelId: string): Promise<LabelDetail> {
  await page.goto('about:blank')
  const read = page.waitForResponse((response) =>
    isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/detail`))
  const scraps = page.waitForResponse((response) =>
    isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/stock-scraps`))
  await page.goto(`/stok/etiket/${labelId}`)
  const response = await read
  expect(response.status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1, name: LABEL, exact: true })).toBeVisible({ timeout: 30_000 })
  expect((await scraps).status()).toBe(200)
  return (await response.json()) as LabelDetail
}

/**
 * Opens ST-01 with a fresh navigation, waits for its one unfiltered read, submits a label-code search and returns
 * the search's answer (the R19/R21 helper).
 */
async function searchPositions(page: Page, q: string): Promise<Response> {
  const reads: string[] = []
  const onRequest = (request: Request) => {
    const url = new URL(request.url())
    if (request.method() === 'GET' && url.pathname === POSITIONS) reads.push(url.searchParams.get('q') ?? '')
  }
  page.on('request', onRequest)
  try {
    await page.goto('about:blank')
    const initial = page.waitForResponse((response) => isCall(response, 'GET', POSITIONS)
      && !new URL(response.url()).searchParams.has('q'))
    await page.goto('/stok/pozisyonlar')
    await expect(page.getByRole('heading', { level: 1, name: 'Stok / Malzeme Pozisyonu' })).toBeVisible({ timeout: 30_000 })
    expect((await initial).status()).toBe(200)
    const field = page.getByLabel('Etiket kodu', { exact: true })
    await field.fill(q)
    await expect(field).toHaveValue(q)
    const answer = page.waitForResponse((response) => isCall(response, 'GET', POSITIONS)
      && new URL(response.url()).searchParams.get('q') === q)
    await page.getByRole('button', { name: 'Ara', exact: true }).click()
    const response = await answer
    expect(response.status()).toBe(200)
    await expect(field).toHaveValue(q)
    expect(reads).toEqual(['', q])
    return response
  } finally {
    page.off('request', onRequest)
  }
}

function scrapRows(page: Page) {
  return page.getByRole('region', { name: PANEL_HEADING, exact: true }).getByRole('row')
}

/* ── Read-only SQL evidence, scoped to this journey's label ───────────────────────────────────────────── */

/** The read-only pool of ../src/mssql.d.ts (mssql ships no types in this workspace). */
type SqlPool = Awaited<ReturnType<InstanceType<typeof sql.ConnectionPool>['connect']>>

async function effects(pool: SqlPool, labelId: string): Promise<Effects> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('labelId', sql.UniqueIdentifier, labelId)
    .query(`SELECT
              (SELECT COUNT(*) FROM [inventory].[StockScraps]
                WHERE [CompanyId] = @companyId AND [LabelId] = @labelId) AS scraps,
              (SELECT COUNT(*) FROM [inventory].[StockScrapDecisions] AS d
                JOIN [inventory].[StockScraps] AS s ON s.[Id] = d.[ScrapId]
                WHERE s.[CompanyId] = @companyId AND s.[LabelId] = @labelId) AS decisions,
              -- Exactly this label's scrap-sourced movement. Other R14 journeys may post the same movement kind.
              (SELECT COUNT(*) FROM [inventory].[Movements] AS m
                JOIN [inventory].[StockScraps] AS s ON s.[CompanyId] = m.[CompanyId] AND s.[Id] = m.[SourceId]
                WHERE m.[CompanyId] = @companyId AND m.[Kind] = 7
                  AND m.[SourceKind] = N'inventory.stock-scrap' AND s.[LabelId] = @labelId) AS scrapMovements,
              (SELECT COUNT(*) FROM [audit].[AuditEvents] AS a
                JOIN [inventory].[StockScraps] AS s ON LOWER(CONVERT(nvarchar(36), s.[Id])) = LOWER(a.[EntityId])
                WHERE a.[CompanyId] = @companyId AND a.[EntityType] = N'Inventory.StockScrap'
                  AND a.[SourceChannel] = N'Web' AND s.[LabelId] = @labelId) AS audits`)
  return result.recordset[0] as Effects
}

async function labelBalanceKg(pool: SqlPool, labelId: string): Promise<string> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('labelId', sql.UniqueIdentifier, labelId)
    .query(`SELECT CONVERT(nvarchar(40), SUM([QuantityKg])) AS kg
            FROM [inventory].[Balances] WHERE [CompanyId] = @companyId AND [LabelId] = @labelId`)
  return Number(result.recordset[0].kg).toString()
}

test('ST-02 records a scrap in the browser, waits for another approver, posts one scrap movement and keeps every fence', async ({ page }) => {
  test.setTimeout(240_000)
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    // ── 0. Only the scrapper holds record and only the approver holds approve (server capability snapshots). ──
    const scrapperCapabilities = await capabilitiesOf(page, scrapperActor)
    expect(scrapperCapabilities).toEqual(expect.arrayContaining([LABEL_DETAIL_CAPABILITY, RECORD_CAPABILITY]))
    expect(scrapperCapabilities).not.toContain(APPROVE_CAPABILITY)
    const approverCapabilities = await capabilitiesOf(page, scrapApproverActor)
    expect(approverCapabilities).toEqual(expect.arrayContaining([LABEL_DETAIL_CAPABILITY, APPROVE_CAPABILITY]))
    expect(approverCapabilities).not.toContain(RECORD_CAPABILITY)
    for (const actor of [recorderActor, deniedActor, arrangerActor, transformerActor, counterActor, countApproverActor]) {
      const capabilities = await capabilitiesOf(page, actor)
      expect(capabilities).not.toContain(RECORD_CAPABILITY)
      expect(capabilities).not.toContain(APPROVE_CAPABILITY)
    }

    // ── 1. Precondition: one 10 kg label through the recorder's real opening command. ──────────────────────
    const opening = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
      headers: commandHeaders(recorderActor, `r22-opening-${randomUUID()}`),
      data: {
        countId: randomUUID(), locationId, countedOn: COUNTED_ON, sheetReference: SHEET,
        lines: [{
          lineId: randomUUID(), labelId: randomUUID(), labelCode: LABEL, lotId: randomUUID(),
          heatNumber: 'SYNR22-HEAT-1', coilNumber: null, productId, productVersion, quantityKg: '10',
        }],
      },
    })
    expect(opening.status()).toBe(201)
    const openingBody = (await opening.json()) as { readonly lines: readonly { readonly labelId: string; readonly labelCode: string; readonly quantityKg: string }[] }
    expect(openingBody.lines).toHaveLength(1)
    expect(openingBody.lines[0]).toMatchObject({ labelCode: LABEL, quantityKg: '10' })
    const labelId = openingBody.lines[0].labelId
    const labelBefore = await readDetail(page, recorderActor, labelId)
    expect(labelBefore.balances).toMatchObject({ physicalKg: '10', freeKg: '10' })
    expect(await effects(pool, labelId)).toEqual({ scraps: 0, decisions: 0, scrapMovements: 0, audits: 0 })

    // ── 2. The scrapper opens the label from ST-01 and records a 4 kg scrap with a reason. ─────────────────
    await signInAs(page, scrapperActor)
    await searchPositions(page, LABEL)
    const labelRead = page.waitForResponse((response) =>
      isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/detail`))
    await page.getByRole('link', { name: LABEL, exact: true }).click()
    expect((await labelRead).status()).toBe(200)
    await expect(page.getByRole('heading', { level: 1, name: LABEL, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toBeVisible()
    await expect(page.getByText('Bu etiket için hurda kaydı yok.', { exact: true })).toBeVisible()

    await page.getByLabel('Hurda kg', { exact: true }).fill('4')
    await page.getByLabel('Hurda gerekçesi', { exact: true }).fill(SCRAP_REASON)
    await expect(page.getByText('Onaydan sonra kalan: 6 kg', { exact: true })).toBeVisible()
    const recordAnswer = page.waitForResponse((response) => isCall(response, 'POST', STOCK_SCRAPS))
    await page.getByRole('button', { name: 'Hurdayı kaydet', exact: true }).click()
    const recordResponse = await recordAnswer
    expect(recordResponse.status()).toBe(201)
    expect(recordResponse.headers()['idempotency-replayed']).toBeUndefined()
    const recordKey = (await recordResponse.request().headerValue('Idempotency-Key')) ?? ''
    expect(recordKey).toMatch(GUID)
    const recordBody = recordResponse.request().postData() ?? ''
    const sentRecord = JSON.parse(recordBody) as RecordRequest
    // The browser sends the label at the version it read, the scrap kg and the reason, nothing else.
    expect(Object.keys(sentRecord).sort())
      .toEqual(['expectedLabelVersion', 'labelId', 'reason', 'scrapId', 'scrapKg', 'scrappedAtUtc'])
    expect(sentRecord).toMatchObject({
      labelId, expectedLabelVersion: labelBefore.labelVersion, scrapKg: '4', reason: SCRAP_REASON,
    })
    const recorded = (await recordResponse.json()) as StockScrapView
    expect(recorded).toMatchObject({
      scrapId: sentRecord.scrapId, companyId, labelId, bookKg: '10', scrapKg: '4', remainingKg: '6',
      reason: SCRAP_REASON, status: 'PendingApproval', decision: null,
    })
    const recordCorrelation = recordResponse.headers()['x-correlation-id'] ?? ''
    expect(recordCorrelation.length).toBeGreaterThan(0)
    await expect(page.getByRole('status').filter({ hasText: 'başka bir yetkilinin onayını bekliyor' })).toBeVisible()
    expect(await effects(pool, labelId)).toEqual({ scraps: 1, decisions: 0, scrapMovements: 0, audits: 1 })
    expect(await labelBalanceKg(pool, labelId)).toBe('10')

    // ── 3. Close and reopen: pending, no stock change, no decision for the scrapper; fences and replay. ───
    const reopenedByScrapper = await openLabel(page, labelId)
    expect(reopenedByScrapper.balances).toMatchObject({ physicalKg: '10', freeKg: '10' })
    await expect(scrapRows(page).filter({ hasText: 'Onay bekliyor' })).toHaveCount(1)
    await expect(page.getByRole('button', { name: 'Hurdayı onayla', exact: true })).toHaveCount(0)
    expect((await readScraps(page, scrapperActor, labelId)).map((item) => [item.scrapId, item.status, item.remainingKg]))
      .toEqual([[sentRecord.scrapId, 'PendingApproval', '6']])

    const scrapperDecision = await page.request.post(`${env.apiBaseUrl}${STOCK_SCRAPS}/${sentRecord.scrapId}/decision`, {
      headers: commandHeaders(scrapperActor, `r22-self-${randomUUID()}`),
      data: { decision: 'Approve', reason: 'SENTETIK kendi hurdasını onaylama denemesi' },
    })
    expect(scrapperDecision.status()).toBe(403)
    const approverScrap = await page.request.post(`${env.apiBaseUrl}${STOCK_SCRAPS}`, {
      headers: commandHeaders(scrapApproverActor, `r22-approver-scrap-${randomUUID()}`),
      data: { ...sentRecord, scrapId: randomUUID() },
    })
    expect(approverScrap.status()).toBe(403)

    const recordReplay = await page.request.post(`${env.apiBaseUrl}${STOCK_SCRAPS}`, {
      headers: { ...commandHeaders(scrapperActor, recordKey), 'X-Correlation-Id': 'r22-record-replay' },
      data: recordBody,
    })
    expect(recordReplay.status()).toBe(200)
    expect(recordReplay.headers()['idempotency-replayed']).toBe('true')
    expect(recordReplay.headers()['idempotency-original-correlation-id']).toBe(recordCorrelation)
    expect(((await recordReplay.json()) as StockScrapView).scrapId).toBe(sentRecord.scrapId)
    expect(await effects(pool, labelId)).toEqual({ scraps: 1, decisions: 0, scrapMovements: 0, audits: 1 })

    // ── 4. The approver opens the label and approves the scrap. ────────────────────────────────────────────
    await signInAs(page, scrapApproverActor)
    await openLabel(page, labelId)
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Hurdayı kaydet', exact: true })).toHaveCount(0)
    await page.getByLabel('Karar notu', { exact: true }).fill(DECISION_NOTE)
    const decisionPath = `${STOCK_SCRAPS}/${sentRecord.scrapId}/decision`
    const decideAnswer = page.waitForResponse((response) => isCall(response, 'POST', decisionPath))
    await page.getByRole('button', { name: 'Hurdayı onayla', exact: true }).click()
    const decideResponse = await decideAnswer
    expect(decideResponse.status()).toBe(201)
    expect(decideResponse.headers()['idempotency-replayed']).toBeUndefined()
    const decideKey = (await decideResponse.request().headerValue('Idempotency-Key')) ?? ''
    expect(decideKey).toMatch(GUID)
    const decideBody = decideResponse.request().postData() ?? ''
    expect(JSON.parse(decideBody)).toEqual({ decision: 'Approve', reason: DECISION_NOTE })
    const decided = (await decideResponse.json()) as StockScrapView
    expect(decided).toMatchObject({
      scrapId: sentRecord.scrapId, status: 'Approved', scrapKg: '4', remainingKg: '6',
      decision: { decision: 'Approved', reason: DECISION_NOTE },
    })
    const scrapMovementId = decided.decision?.scrapMovementId ?? ''
    expect(scrapMovementId).toMatch(GUID)
    const decideCorrelation = decideResponse.headers()['x-correlation-id'] ?? ''
    expect(decideCorrelation.length).toBeGreaterThan(0)
    await expect(page.getByRole('status').filter({ hasText: 'Hurda onaylandı ve stoktan düşüldü' })).toBeVisible()
    expect(await effects(pool, labelId)).toEqual({ scraps: 1, decisions: 1, scrapMovements: 1, audits: 2 })
    expect(await labelBalanceKg(pool, labelId)).toBe('6')

    // ── 5. Close and reopen: approved with its movement, 6 kg on the label and in ST-01; decision replay. ──
    const reopenedByApprover = await openLabel(page, labelId)
    expect(reopenedByApprover.balances).toMatchObject({ physicalKg: '6', freeKg: '6' })
    await expect(scrapRows(page).filter({ hasText: 'Onaylandı (stoktan düşüldü)' })).toHaveCount(1)
    await expect(page.getByRole('button', { name: 'Hurdayı onayla', exact: true })).toHaveCount(0)
    await expect(page.getByRole('cell').filter({ hasText: 'Hurda çıkışı' })).toHaveCount(1)
    const scrapsAfter = await readScraps(page, scrapApproverActor, labelId)
    expect(scrapsAfter.map((item) => [item.scrapId, item.status, item.decision?.scrapMovementId]))
      .toEqual([[sentRecord.scrapId, 'Approved', scrapMovementId]])

    await signInAs(page, scrapperActor)
    const positions = await searchPositions(page, LABEL)
    const listed = ((await positions.json()) as { readonly items: readonly { readonly labelCode: string; readonly physicalKg: string }[] })
      .items.map((item) => [item.labelCode, item.physicalKg])
    expect(listed).toEqual([[LABEL, '6']])

    const decideReplay = await page.request.post(`${env.apiBaseUrl}${decisionPath}`, {
      headers: { ...commandHeaders(scrapApproverActor, decideKey), 'X-Correlation-Id': 'r22-decision-replay' },
      data: decideBody,
    })
    expect(decideReplay.status()).toBe(200)
    expect(decideReplay.headers()['idempotency-replayed']).toBe('true')
    expect(decideReplay.headers()['idempotency-original-correlation-id']).toBe(decideCorrelation)
    expect(((await decideReplay.json()) as StockScrapView).decision?.scrapMovementId).toBe(scrapMovementId)
    expect(await effects(pool, labelId)).toEqual({ scraps: 1, decisions: 1, scrapMovements: 1, audits: 2 })
    expect(await labelBalanceKg(pool, labelId)).toBe('6')

    // ── 6. Fences: the consumed label version, more than the book, another company; nothing written. ───────
    const stale = await page.request.post(`${env.apiBaseUrl}${STOCK_SCRAPS}`, {
      headers: commandHeaders(scrapperActor, `r22-stale-${randomUUID()}`),
      data: { ...sentRecord, scrapId: randomUUID(), scrappedAtUtc: new Date().toISOString() },
    })
    expect(stale.status()).toBe(409)
    expect(((await stale.json()) as { readonly code?: string }).code).toBe('inventory_label_version_conflict')

    const current = await readDetail(page, scrapperActor, labelId)
    const overBook = await page.request.post(`${env.apiBaseUrl}${STOCK_SCRAPS}`, {
      headers: commandHeaders(scrapperActor, `r22-over-${randomUUID()}`),
      data: {
        ...sentRecord, scrapId: randomUUID(), expectedLabelVersion: current.labelVersion, scrapKg: '6.001',
        scrappedAtUtc: new Date().toISOString(),
      },
    })
    expect(overBook.status()).toBe(409)
    expect(((await overBook.json()) as { readonly code?: string }).code).toBe('inventory_stock_scrap_exceeds_book')

    const foreign = await page.request.post(`${env.apiBaseUrl}${STOCK_SCRAPS}`, {
      headers: commandHeaders(scrapperActor, `r22-foreign-${randomUUID()}`, otherCompanyId),
      data: { ...sentRecord, scrapId: randomUUID(), scrappedAtUtc: new Date().toISOString() },
    })
    expect(foreign.status()).toBe(403)
    const foreignBody = await foreign.text()
    expect(JSON.parse(foreignBody)).toMatchObject({ code: 'forbidden' })
    expect(foreignBody).not.toContain(LABEL)
    expect(await effects(pool, labelId)).toEqual({ scraps: 1, decisions: 1, scrapMovements: 1, audits: 2 })
    expect(await labelBalanceKg(pool, labelId)).toBe('6')
  } finally {
    await pool.close()
  }
})
