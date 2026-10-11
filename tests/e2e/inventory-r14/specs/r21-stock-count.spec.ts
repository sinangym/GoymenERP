import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Request, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

/**
 * AT-037 / DEC-STK-007 stock count on the REAL API and UI of the r14 disposable stack. Nothing is mocked.
 *
 * Authority (user decision 2026-09-27): `inventory.stock-count.record` is held ONLY by the disposable r14-counter and
 * `inventory.stock-count.approve` ONLY by the disposable r14-count-approver (r14.override.yml) — two different actors.
 * Until the critical-difference threshold is decided (INV-R20-R21-01 Q2) every non-zero difference waits for an actor
 * other than its counter (fail-closed maker ≠ checker); the reason is free text.
 *
 * Journey:
 *  1. Precondition: the recorder records one 10 kg opening label through the real opening command.
 *  2. The counter opens the label from ST-01 and records 7 kg with a reason; the server answers a pending -3 kg
 *     difference and writes no stock.
 *  3. Close and reopen: the count reads "Onay bekliyor", the label still reads 10 kg and the counter is offered no
 *     decision; its direct decision and the approver's direct count are 403; the same key replays the count once.
 *  4. The approver opens the label and approves the difference; one CountAdjustment movement is posted.
 *  5. Close and reopen: the count reads approved with its movement, the label reads 7 kg and ST-01 lists 7 kg;
 *     the same decision key replays with nothing new.
 *  6. Fences: a count at the consumed label version is 409, another company context is refused; nothing is written.
 */
const env = readEnvironment()
const { recorderActor, deniedActor, arrangerActor, transformerActor, counterActor, countApproverActor, companyId,
  otherCompanyId, locationId, productId, productVersion } = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const OPENINGS = '/api/v1/inventory/opening-counts'
const STOCK_COUNTS = '/api/v1/inventory/stock-counts'
const POSITIONS = '/api/v1/inventory/positions'
const CAPABILITIES = '/api/v1/shell/capabilities'
const RECORD_CAPABILITY = 'inventory.stock-count.record'
const APPROVE_CAPABILITY = 'inventory.stock-count.approve'
const LABEL_DETAIL_CAPABILITY = 'inventory.stock-positions.list'
const COUNTED_ON = '2026-09-18'
const SHEET = 'SENTETIK browser-suite:r14 AT-037 count source'
const LABEL = 'SYNR21-COUNT-1'
const COUNT_REASON = 'SENTETIK AT-037 sayım farkı'
const DECISION_NOTE = 'SENTETIK AT-037 ikinci kişi onayı'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const PANEL_HEADING = 'Sayım ve fark (ST-05)'

interface StockCountView {
  readonly countId: string
  readonly companyId: string
  readonly labelId: string
  readonly labelVersion: string
  readonly bookKg: string
  readonly countedKg: string
  readonly differenceKg: string
  readonly reason: string
  readonly status: 'NoDifference' | 'PendingApproval' | 'Approved' | 'Rejected'
  readonly decision: null | {
    readonly decision: string
    readonly reason: string
    readonly adjustmentMovementId: string | null
  }
}

interface RecordRequest {
  readonly countId: string
  readonly labelId: string
  readonly expectedLabelVersion: string
  readonly countedKg: string
  readonly countedAtUtc: string
  readonly reason: string
}

interface LabelDetail {
  readonly labelVersion: string
  readonly identity: { readonly labelId: string; readonly code: string; readonly companyId: string }
  readonly balances: { readonly labelId: string; readonly physicalKg: string; readonly freeKg: string }
}

interface Effects {
  readonly counts: number
  readonly decisions: number
  readonly adjustments: number
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

async function readCounts(page: Page, actor: string, labelId: string): Promise<readonly StockCountView[]> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/labels/${labelId}/stock-counts`, {
    headers: apiHeaders(actor),
  })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly labelId: string; readonly items: readonly StockCountView[]; readonly windowFull: boolean }
  expect(body.labelId).toBe(labelId)
  expect(body.windowFull).toBe(false)
  return body.items
}

/** Opens a label detail with a fresh navigation and waits for the detail read and the label's count read. */
async function openLabel(page: Page, labelId: string): Promise<LabelDetail> {
  await page.goto('about:blank')
  const read = page.waitForResponse((response) =>
    isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/detail`))
  const counts = page.waitForResponse((response) =>
    isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/stock-counts`))
  await page.goto(`/stok/etiket/${labelId}`)
  const response = await read
  expect(response.status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1, name: LABEL, exact: true })).toBeVisible({ timeout: 30_000 })
  expect((await counts).status()).toBe(200)
  return (await response.json()) as LabelDetail
}

/**
 * Opens ST-01 with a fresh navigation, waits for its one unfiltered read, submits a label-code search and returns
 * the search's answer (the R19 helper: ST-01 must read the list once and then exactly the submitted search).
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

function countRows(page: Page) {
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
              (SELECT COUNT(*) FROM [inventory].[StockCounts]
                WHERE [CompanyId] = @companyId AND [LabelId] = @labelId) AS counts,
              (SELECT COUNT(*) FROM [inventory].[StockCountDecisions] AS d
                JOIN [inventory].[StockCounts] AS c ON c.[Id] = d.[CountId]
                WHERE c.[CompanyId] = @companyId AND c.[LabelId] = @labelId) AS decisions,
              -- Exactly this label's count-sourced adjustment. Other R14 journeys may post the same movement kind.
              (SELECT COUNT(*) FROM [inventory].[Movements] AS m
                JOIN [inventory].[StockCounts] AS c ON c.[CompanyId] = m.[CompanyId] AND c.[Id] = m.[SourceId]
                WHERE m.[CompanyId] = @companyId AND m.[Kind] = 6
                  AND m.[SourceKind] = N'inventory.stock-count' AND c.[LabelId] = @labelId) AS adjustments,
              (SELECT COUNT(*) FROM [audit].[AuditEvents] AS a
                JOIN [inventory].[StockCounts] AS c ON LOWER(CONVERT(nvarchar(36), c.[Id])) = LOWER(a.[EntityId])
                WHERE a.[CompanyId] = @companyId AND a.[EntityType] = N'Inventory.StockCount'
                  AND c.[LabelId] = @labelId) AS audits`)
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

test('AT-037 records a count difference in the browser, waits for another approver, posts one adjustment and keeps every fence', async ({ page }) => {
  test.setTimeout(240_000)
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    // ── 0. Only the counter holds record and only the approver holds approve (server capability snapshots). ──
    const counterCapabilities = await capabilitiesOf(page, counterActor)
    expect(counterCapabilities).toEqual(expect.arrayContaining([LABEL_DETAIL_CAPABILITY, RECORD_CAPABILITY]))
    expect(counterCapabilities).not.toContain(APPROVE_CAPABILITY)
    const approverCapabilities = await capabilitiesOf(page, countApproverActor)
    expect(approverCapabilities).toEqual(expect.arrayContaining([LABEL_DETAIL_CAPABILITY, APPROVE_CAPABILITY]))
    expect(approverCapabilities).not.toContain(RECORD_CAPABILITY)
    for (const actor of [recorderActor, deniedActor, arrangerActor, transformerActor]) {
      const capabilities = await capabilitiesOf(page, actor)
      expect(capabilities).not.toContain(RECORD_CAPABILITY)
      expect(capabilities).not.toContain(APPROVE_CAPABILITY)
    }

    // ── 1. Precondition: one 10 kg label through the recorder's real opening command. ──────────────────────
    const opening = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
      headers: commandHeaders(recorderActor, `r21-opening-${randomUUID()}`),
      data: {
        countId: randomUUID(), locationId, countedOn: COUNTED_ON, sheetReference: SHEET,
        lines: [{
          lineId: randomUUID(), labelId: randomUUID(), labelCode: LABEL, lotId: randomUUID(),
          heatNumber: 'SYNR21-HEAT-1', coilNumber: null, productId, productVersion, quantityKg: '10',
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
    expect(await effects(pool, labelId)).toEqual({ counts: 0, decisions: 0, adjustments: 0, audits: 0 })

    // ── 2. The counter opens the label from ST-01 and records 7 kg with a reason. ──────────────────────────
    await signInAs(page, counterActor)
    await searchPositions(page, LABEL)
    const labelRead = page.waitForResponse((response) =>
      isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/detail`))
    await page.getByRole('link', { name: LABEL, exact: true }).click()
    expect((await labelRead).status()).toBe(200)
    await expect(page.getByRole('heading', { level: 1, name: LABEL, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toBeVisible()
    await expect(page.getByText('Bu etiket için sayım yok.', { exact: true })).toBeVisible()

    await page.getByLabel('Sayılan kg', { exact: true }).fill('7')
    await page.getByLabel('Fark gerekçesi', { exact: true }).fill(COUNT_REASON)
    await expect(page.getByText('Fark: -3 kg', { exact: true })).toBeVisible()
    const recordAnswer = page.waitForResponse((response) => isCall(response, 'POST', STOCK_COUNTS))
    await page.getByRole('button', { name: 'Sayımı kaydet', exact: true }).click()
    const recordResponse = await recordAnswer
    expect(recordResponse.status()).toBe(201)
    expect(recordResponse.headers()['idempotency-replayed']).toBeUndefined()
    const recordKey = (await recordResponse.request().headerValue('Idempotency-Key')) ?? ''
    expect(recordKey).toMatch(GUID)
    const recordBody = recordResponse.request().postData() ?? ''
    const sentRecord = JSON.parse(recordBody) as RecordRequest
    // The browser sends the label at the version it read, the counted kg and the reason, nothing else.
    expect(Object.keys(sentRecord).sort())
      .toEqual(['countId', 'countedAtUtc', 'countedKg', 'expectedLabelVersion', 'labelId', 'reason'])
    expect(sentRecord).toMatchObject({
      labelId, expectedLabelVersion: labelBefore.labelVersion, countedKg: '7', reason: COUNT_REASON,
    })
    const recorded = (await recordResponse.json()) as StockCountView
    expect(recorded).toMatchObject({
      countId: sentRecord.countId, companyId, labelId, bookKg: '10', countedKg: '7', differenceKg: '-3',
      reason: COUNT_REASON, status: 'PendingApproval', decision: null,
    })
    const recordCorrelation = recordResponse.headers()['x-correlation-id'] ?? ''
    expect(recordCorrelation.length).toBeGreaterThan(0)
    await expect(page.getByRole('status').filter({ hasText: 'başka bir yetkilinin onayını bekliyor' })).toBeVisible()
    expect(await effects(pool, labelId)).toEqual({ counts: 1, decisions: 0, adjustments: 0, audits: 1 })
    expect(await labelBalanceKg(pool, labelId)).toBe('10')

    // ── 3. Close and reopen: pending, no stock change, no decision for the counter; fences and replay. ────
    const reopenedByCounter = await openLabel(page, labelId)
    expect(reopenedByCounter.balances).toMatchObject({ physicalKg: '10', freeKg: '10' })
    await expect(countRows(page).filter({ hasText: 'Onay bekliyor' })).toHaveCount(1)
    await expect(page.getByRole('button', { name: 'Farkı onayla', exact: true })).toHaveCount(0)
    expect((await readCounts(page, counterActor, labelId)).map((item) => [item.countId, item.status, item.differenceKg]))
      .toEqual([[sentRecord.countId, 'PendingApproval', '-3']])

    const counterDecision = await page.request.post(`${env.apiBaseUrl}${STOCK_COUNTS}/${sentRecord.countId}/decision`, {
      headers: commandHeaders(counterActor, `r21-self-${randomUUID()}`),
      data: { decision: 'Approve', reason: 'SENTETIK kendi sayımını onaylama denemesi' },
    })
    expect(counterDecision.status()).toBe(403)
    const approverCount = await page.request.post(`${env.apiBaseUrl}${STOCK_COUNTS}`, {
      headers: commandHeaders(countApproverActor, `r21-approver-count-${randomUUID()}`),
      data: { ...sentRecord, countId: randomUUID() },
    })
    expect(approverCount.status()).toBe(403)

    const recordReplay = await page.request.post(`${env.apiBaseUrl}${STOCK_COUNTS}`, {
      headers: { ...commandHeaders(counterActor, recordKey), 'X-Correlation-Id': 'r21-record-replay' },
      data: recordBody,
    })
    expect(recordReplay.status()).toBe(200)
    expect(recordReplay.headers()['idempotency-replayed']).toBe('true')
    expect(recordReplay.headers()['idempotency-original-correlation-id']).toBe(recordCorrelation)
    expect(((await recordReplay.json()) as StockCountView).countId).toBe(sentRecord.countId)
    expect(await effects(pool, labelId)).toEqual({ counts: 1, decisions: 0, adjustments: 0, audits: 1 })

    // ── 4. The approver opens the label and approves the difference. ───────────────────────────────────────
    await signInAs(page, countApproverActor)
    await openLabel(page, labelId)
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sayımı kaydet', exact: true })).toHaveCount(0)
    await page.getByLabel('Karar notu', { exact: true }).fill(DECISION_NOTE)
    const decisionPath = `${STOCK_COUNTS}/${sentRecord.countId}/decision`
    const decideAnswer = page.waitForResponse((response) => isCall(response, 'POST', decisionPath))
    await page.getByRole('button', { name: 'Farkı onayla', exact: true }).click()
    const decideResponse = await decideAnswer
    expect(decideResponse.status()).toBe(201)
    expect(decideResponse.headers()['idempotency-replayed']).toBeUndefined()
    const decideKey = (await decideResponse.request().headerValue('Idempotency-Key')) ?? ''
    expect(decideKey).toMatch(GUID)
    const decideBody = decideResponse.request().postData() ?? ''
    expect(JSON.parse(decideBody)).toEqual({ decision: 'Approve', reason: DECISION_NOTE })
    const decided = (await decideResponse.json()) as StockCountView
    expect(decided).toMatchObject({
      countId: sentRecord.countId, status: 'Approved', differenceKg: '-3',
      decision: { decision: 'Approved', reason: DECISION_NOTE },
    })
    const adjustmentMovementId = decided.decision?.adjustmentMovementId ?? ''
    expect(adjustmentMovementId).toMatch(GUID)
    const decideCorrelation = decideResponse.headers()['x-correlation-id'] ?? ''
    expect(decideCorrelation.length).toBeGreaterThan(0)
    await expect(page.getByRole('status').filter({ hasText: 'Sayım farkı onaylandı ve stoğa işlendi' })).toBeVisible()
    expect(await effects(pool, labelId)).toEqual({ counts: 1, decisions: 1, adjustments: 1, audits: 2 })
    expect(await labelBalanceKg(pool, labelId)).toBe('7')

    // ── 5. Close and reopen: approved with its movement, 7 kg on the label and in ST-01; decision replay. ──
    const reopenedByApprover = await openLabel(page, labelId)
    expect(reopenedByApprover.balances).toMatchObject({ physicalKg: '7', freeKg: '7' })
    await expect(countRows(page).filter({ hasText: 'Onaylandı (stoğa işlendi)' })).toHaveCount(1)
    await expect(page.getByRole('button', { name: 'Farkı onayla', exact: true })).toHaveCount(0)
    const countsAfter = await readCounts(page, countApproverActor, labelId)
    expect(countsAfter.map((item) => [item.countId, item.status, item.decision?.adjustmentMovementId]))
      .toEqual([[sentRecord.countId, 'Approved', adjustmentMovementId]])

    await signInAs(page, counterActor)
    const positions = await searchPositions(page, LABEL)
    const listed = ((await positions.json()) as { readonly items: readonly { readonly labelCode: string; readonly physicalKg: string }[] })
      .items.map((item) => [item.labelCode, item.physicalKg])
    expect(listed).toEqual([[LABEL, '7']])

    const decideReplay = await page.request.post(`${env.apiBaseUrl}${decisionPath}`, {
      headers: { ...commandHeaders(countApproverActor, decideKey), 'X-Correlation-Id': 'r21-decision-replay' },
      data: decideBody,
    })
    expect(decideReplay.status()).toBe(200)
    expect(decideReplay.headers()['idempotency-replayed']).toBe('true')
    expect(decideReplay.headers()['idempotency-original-correlation-id']).toBe(decideCorrelation)
    expect(((await decideReplay.json()) as StockCountView).decision?.adjustmentMovementId).toBe(adjustmentMovementId)
    expect(await effects(pool, labelId)).toEqual({ counts: 1, decisions: 1, adjustments: 1, audits: 2 })
    expect(await labelBalanceKg(pool, labelId)).toBe('7')

    // ── 6. Fences: a count at the consumed label version, another company; nothing written. ────────────────
    const stale = await page.request.post(`${env.apiBaseUrl}${STOCK_COUNTS}`, {
      headers: commandHeaders(counterActor, `r21-stale-${randomUUID()}`),
      data: { ...sentRecord, countId: randomUUID(), countedAtUtc: new Date().toISOString() },
    })
    expect(stale.status()).toBe(409)
    expect(((await stale.json()) as { readonly code?: string }).code).toBe('inventory_label_version_conflict')

    const foreign = await page.request.post(`${env.apiBaseUrl}${STOCK_COUNTS}`, {
      headers: commandHeaders(counterActor, `r21-foreign-${randomUUID()}`, otherCompanyId),
      data: { ...sentRecord, countId: randomUUID(), countedAtUtc: new Date().toISOString() },
    })
    expect(foreign.status()).toBe(403)
    const foreignBody = await foreign.text()
    expect(JSON.parse(foreignBody)).toMatchObject({ code: 'forbidden' })
    expect(foreignBody).not.toContain(LABEL)
    expect(await effects(pool, labelId)).toEqual({ counts: 1, decisions: 1, adjustments: 1, audits: 2 })
    expect(await labelBalanceKg(pool, labelId)).toBe('7')
  } finally {
    await pool.close()
  }
})
