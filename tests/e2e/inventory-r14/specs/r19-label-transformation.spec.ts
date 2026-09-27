import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Request, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

/**
 * R19 / AT-038 label split and merge on the REAL API and UI of the r14 disposable stack. Nothing is mocked.
 *
 * Authority (user decision 2026-09-27): `inventory.label-transformation.record` is held ONLY by the disposable
 * r14-transformer of this per-run database (r14.override.yml); no real user and no seeded role holds it. The
 * recorder (label and balance reads, no transformation name) and the denied actor get no control and 403.
 *
 * Journey:
 *  1. Precondition: the recorder records one 10 kg opening label through the real opening command.
 *  2. Non-holders: the recorder opens the label and is offered no split/merge control; its direct command and the
 *     denied actor's are 403, and nothing is written.
 *  3. The transformer opens the label from ST-01 (/stok/pozisyonlar) and splits it 4 + 6 kg; the server confirms.
 *  4. Close and reopen: ST-01 lists the two children with exactly 4 and 6 kg and no longer the consumed source;
 *     the source reads 0 kg with its two genealogy rows, each child its own row. No double quantity (SQL).
 *  5. The same Idempotency-Key and body replays (Idempotency-Replayed, original correlation) with no second
 *     transformation, label, command or audit row; a new attempt at the consumed version is 409 stale.
 *  6. The transformer merges the two children in the UI into one 10 kg label; its reopened genealogy holds the
 *     split and the merge (four edges); the children read 0 kg; the mass is still 10 kg.
 *  7. Fences: the recorder sees the merged label with no control and is 403; the transformer from another company
 *     context is refused; nothing any refusal sent was written.
 */
const env = readEnvironment()
const { recorderActor, deniedActor, transformerActor, companyId, otherCompanyId, locationId, productId,
  productVersion, productCode } = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const OPENINGS = '/api/v1/inventory/opening-counts'
const TRANSFORMATIONS = '/api/v1/inventory/label-transformations'
const POSITIONS = '/api/v1/inventory/positions'
const CAPABILITIES = '/api/v1/shell/capabilities'
const TRANSFORMATION_CAPABILITY = 'inventory.label-transformation.record'
const LABEL_DETAIL_CAPABILITY = 'inventory.stock-positions.list'
const COMMAND_NAME = 'inventory.label-transformation.record.v1'
const COUNTED_ON = '2026-09-18'
const SHEET = 'SENTETIK browser-suite:r14 R19 split/merge source'
const SOURCE = 'SYNR19-SOURCE-1'
const CHILD_A = 'SYNR19-SPLIT-A'
const CHILD_B = 'SYNR19-SPLIT-B'
const MERGED = 'SYNR19-MERGED-1'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const PANEL_HEADING = 'Bölme / birleştirme'

interface TransformationView {
  readonly transformationId: string
  readonly companyId: string
  readonly kind: 'Split' | 'Merge'
  readonly occurredAtUtc: string
  readonly reason: string
  readonly sourceLabelIds: readonly string[]
  readonly outputLabelIds: readonly string[]
}

interface TransformationRequest {
  readonly transformationId: string
  readonly kind: 'Split' | 'Merge'
  readonly sources: readonly { readonly labelId: string; readonly expectedLabelVersion: string }[]
  readonly outputs: readonly { readonly labelId: string; readonly code: string; readonly quantityKg: string }[]
  readonly occurredAtUtc: string
  readonly reason: string
}

interface LabelDetail {
  readonly labelVersion: string
  readonly identity: { readonly labelId: string; readonly code: string; readonly companyId: string }
  readonly balances: { readonly labelId: string; readonly physicalKg: string; readonly freeKg: string }
  readonly genealogy: {
    readonly labelId: string
    readonly edges: readonly {
      readonly transformationId: string; readonly kind: string
      readonly sourceLabelId: string; readonly sourceCode: string; readonly sourceQuantityKg: string
      readonly outputLabelId: string; readonly outputCode: string; readonly outputQuantityKg: string
    }[]
    readonly windowFull: boolean
  }
}

interface Effects {
  readonly transformations: number
  readonly commands: number
  readonly audits: number
  readonly labels: number
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

/** Opens a label detail with a fresh navigation and returns the detail read that painted it. */
async function openLabel(page: Page, labelId: string, code: string): Promise<LabelDetail> {
  await page.goto('about:blank')
  const read = page.waitForResponse((response) =>
    isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/detail`))
  await page.goto(`/stok/etiket/${labelId}`)
  const response = await read
  expect(response.status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1, name: code, exact: true })).toBeVisible({ timeout: 30_000 })
  return (await response.json()) as LabelDetail
}

/**
 * Opens ST-01 with a fresh navigation, waits for its one unfiltered read, submits a label-code search and returns
 * the search's answer. ST-01 must read the company's list once and then exactly the submitted search: run
 * 36317283526 caught it re-reading the list after every render, which aborted the search in flight.
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

function genealogyRows(page: Page) {
  return page.getByRole('region', { name: 'Soy ağacı', exact: true }).getByRole('row')
}

/* ── Read-only SQL evidence, scoped to this journey's label codes ─────────────────────────────────────── */

/** The read-only pool of ../src/mssql.d.ts (mssql ships no types in this workspace). */
type SqlPool = Awaited<ReturnType<InstanceType<typeof sql.ConnectionPool>['connect']>>

async function effects(pool: SqlPool): Promise<Effects> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('command', sql.NVarChar(200), COMMAND_NAME)
    .query(`SELECT
              (SELECT COUNT(*) FROM [inventory].[LabelTransformations] WHERE [CompanyId] = @companyId) AS transformations,
              (SELECT COUNT(*) FROM [inventory].[Commands]
                WHERE [CompanyId] = @companyId AND [CommandName] = @command) AS commands,
              (SELECT COUNT(*) FROM [audit].[AuditEvents]
                WHERE [CompanyId] = @companyId AND [EntityType] = N'Inventory.LabelTransformation') AS audits,
              (SELECT COUNT(*) FROM [inventory].[Labels]
                WHERE [CompanyId] = @companyId AND [Code] LIKE N'SYNR19-%') AS labels`)
  return result.recordset[0] as Effects
}

/** The journey's total physical kg across every SYNR19 label: the source's 10 kg, never counted twice. */
async function journeyMassKg(pool: SqlPool): Promise<string> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .query(`SELECT CONVERT(nvarchar(40), SUM(b.[QuantityKg])) AS mass
            FROM [inventory].[Balances] AS b
            JOIN [inventory].[Labels] AS l ON l.[Id] = b.[LabelId]
            WHERE b.[CompanyId] = @companyId AND l.[Code] LIKE N'SYNR19-%'`)
  return Number(result.recordset[0].mass).toString()
}

async function positiveBalances(pool: SqlPool): Promise<Record<string, string>> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .query(`SELECT l.[Code] AS code, CONVERT(nvarchar(40), SUM(b.[QuantityKg])) AS kg
            FROM [inventory].[Balances] AS b
            JOIN [inventory].[Labels] AS l ON l.[Id] = b.[LabelId]
            WHERE b.[CompanyId] = @companyId AND l.[Code] LIKE N'SYNR19-%' AND b.[QuantityKg] > 0
            GROUP BY l.[Code]`)
  return Object.fromEntries((result.recordset as { code: string; kg: string }[])
    .map((row) => [row.code, Number(row.kg).toString()]))
}

test('R19 splits a label in the browser, reopens exact children and genealogy, replays once, merges them back and keeps every fence', async ({ page }) => {
  test.setTimeout(240_000)
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    // ── 0. Only the disposable transformer is offered the control (server capability snapshots). ────────────
    expect(await capabilitiesOf(page, transformerActor))
      .toEqual(expect.arrayContaining([LABEL_DETAIL_CAPABILITY, TRANSFORMATION_CAPABILITY]))
    for (const actor of [recorderActor, deniedActor]) {
      expect(await capabilitiesOf(page, actor)).not.toContain(TRANSFORMATION_CAPABILITY)
    }
    expect(await effects(pool)).toEqual({ transformations: 0, commands: 0, audits: 0, labels: 0 })

    // ── 1. Precondition: one 10 kg label through the recorder's real opening command. ──────────────────────
    const opening = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
      headers: commandHeaders(recorderActor, `r19-opening-${randomUUID()}`),
      data: {
        countId: randomUUID(), locationId, countedOn: COUNTED_ON, sheetReference: SHEET,
        lines: [{
          lineId: randomUUID(), labelId: randomUUID(), labelCode: SOURCE, lotId: randomUUID(),
          heatNumber: 'SYNR19-HEAT-1', coilNumber: null, productId, productVersion, quantityKg: '10',
        }],
      },
    })
    expect(opening.status()).toBe(201)
    const openingBody = (await opening.json()) as { readonly lines: readonly { readonly labelId: string; readonly labelCode: string; readonly quantityKg: string }[] }
    expect(openingBody.lines).toHaveLength(1)
    expect(openingBody.lines[0]).toMatchObject({ labelCode: SOURCE, quantityKg: '10' })
    const sourceId = openingBody.lines[0].labelId
    const sourceBefore = await readDetail(page, recorderActor, sourceId)
    expect(sourceBefore.balances).toMatchObject({ physicalKg: '10', freeKg: '10' })
    expect(sourceBefore.genealogy.edges).toEqual([])
    expect(await effects(pool)).toEqual({ transformations: 0, commands: 0, audits: 0, labels: 1 })

    // ── 2. Non-holders: no control, 403, nothing written. ──────────────────────────────────────────────────
    await signInAs(page, recorderActor)
    await openLabel(page, sourceId, SOURCE)
    await expect(page.getByRole('heading', { level: 2, name: 'Soy ağacı', exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Bölmeyi kaydet' })).toHaveCount(0)
    const refusedSplit: TransformationRequest = {
      transformationId: randomUUID(), kind: 'Split',
      sources: [{ labelId: sourceId, expectedLabelVersion: sourceBefore.labelVersion }],
      outputs: [
        { labelId: randomUUID(), code: 'SYNR19-DENIED-A', quantityKg: '4' },
        { labelId: randomUUID(), code: 'SYNR19-DENIED-B', quantityKg: '6' },
      ],
      occurredAtUtc: new Date().toISOString(), reason: 'SENTETIK R19 yetkisiz deneme',
    }
    for (const actor of [recorderActor, deniedActor]) {
      const denied = await page.request.post(`${env.apiBaseUrl}${TRANSFORMATIONS}`, {
        headers: commandHeaders(actor, `r19-denied-${actor}`), data: refusedSplit,
      })
      expect(denied.status()).toBe(403)
      expect(await denied.text()).not.toContain(SOURCE)
    }
    expect(await effects(pool)).toEqual({ transformations: 0, commands: 0, audits: 0, labels: 1 })

    // ── 3. The transformer opens the label from ST-01 and splits it 4 + 6 kg. ──────────────────────────────
    await signInAs(page, transformerActor)
    await searchPositions(page, SOURCE)
    const sourceRead = page.waitForResponse((response) =>
      isCall(response, 'GET', `/api/v1/inventory/labels/${sourceId}/detail`))
    await page.getByRole('link', { name: SOURCE, exact: true }).click()
    expect((await sourceRead).status()).toBe(200)
    await expect(page.getByRole('heading', { level: 1, name: SOURCE, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toBeVisible()

    await page.getByLabel('Çıktı 1 etiket kodu', { exact: true }).fill(CHILD_A)
    await page.getByLabel('Çıktı 1 kg', { exact: true }).fill('4')
    await page.getByLabel('Çıktı 2 etiket kodu', { exact: true }).fill(CHILD_B)
    await page.getByLabel('Çıktı 2 kg', { exact: true }).fill('6')
    await expect(page.getByText('Kaynak: 10 kg · Çıktı toplamı: 10 kg', { exact: true })).toBeVisible()
    await page.getByRole('textbox', { name: 'Gerekçe', exact: true }).fill('SENTETIK R19 paket bölme')
    const splitAnswer = page.waitForResponse((response) => isCall(response, 'POST', TRANSFORMATIONS))
    await page.getByRole('button', { name: 'Bölmeyi kaydet', exact: true }).click()
    const splitResponse = await splitAnswer
    expect(splitResponse.status()).toBe(201)
    expect(splitResponse.headers()['idempotency-replayed']).toBeUndefined()
    const splitKey = (await splitResponse.request().headerValue('Idempotency-Key')) ?? ''
    expect(splitKey).toMatch(GUID)
    const splitBody = splitResponse.request().postData() ?? ''
    const sentSplit = JSON.parse(splitBody) as TransformationRequest
    // The browser sends the source at the version it read, and the two outputs it was given, nothing else.
    expect(Object.keys(sentSplit).sort()).toEqual(['kind', 'occurredAtUtc', 'outputs', 'reason', 'sources', 'transformationId'])
    expect(sentSplit).toMatchObject({
      kind: 'Split', reason: 'SENTETIK R19 paket bölme',
      sources: [{ labelId: sourceId, expectedLabelVersion: sourceBefore.labelVersion }],
      outputs: [{ code: CHILD_A, quantityKg: '4' }, { code: CHILD_B, quantityKg: '6' }],
    })
    const [childAId, childBId] = sentSplit.outputs.map((output) => output.labelId)
    const splitView = (await splitResponse.json()) as TransformationView
    expect(splitView).toMatchObject({
      transformationId: sentSplit.transformationId, companyId, kind: 'Split',
      sourceLabelIds: [sourceId], outputLabelIds: [childAId, childBId],
    })
    const splitCorrelation = splitResponse.headers()['x-correlation-id'] ?? ''
    expect(splitCorrelation.length).toBeGreaterThan(0)
    await expect(page.getByRole('heading', { level: 2, name: 'Bölme kaydedildi', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: CHILD_A, exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: CHILD_B, exact: true })).toBeVisible()
    expect(await effects(pool)).toEqual({ transformations: 1, commands: 1, audits: 1, labels: 3 })
    expect(await positiveBalances(pool)).toEqual({ [CHILD_A]: '4', [CHILD_B]: '6' })
    expect(await journeyMassKg(pool)).toBe('10')

    // ── 4. Close and reopen: exact children, genealogy, no double quantity. ────────────────────────────────
    const journeyResponse = await searchPositions(page, 'SYNR19-')
    const listed = ((await journeyResponse.json()) as { readonly items: readonly { readonly labelCode: string; readonly physicalKg: string }[] })
      .items.map((item) => [item.labelCode, item.physicalKg]).sort()
    expect(listed).toEqual([[CHILD_A, '4'], [CHILD_B, '6']])
    await expect(page.getByRole('link', { name: CHILD_A, exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: CHILD_B, exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: SOURCE, exact: true })).toHaveCount(0)

    const sourceAfter = await openLabel(page, sourceId, SOURCE)
    expect(sourceAfter.balances).toMatchObject({ physicalKg: '0' })
    expect(BigInt(sourceAfter.labelVersion)).toBe(BigInt(sourceBefore.labelVersion) + 1n)
    expect(sourceAfter.genealogy.edges.map((edge) => [edge.kind, edge.sourceCode, edge.sourceQuantityKg, edge.outputCode, edge.outputQuantityKg]))
      .toEqual([['Split', SOURCE, '10', CHILD_A, '4'], ['Split', SOURCE, '10', CHILD_B, '6']])
    await expect(page.getByText('Fiziksel miktar', { exact: true })).toBeVisible()
    await expect(genealogyRows(page).filter({ hasText: 'Bölme' })).toHaveCount(2)
    await expect(page.getByRole('link', { name: CHILD_A, exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: CHILD_B, exact: true })).toBeVisible()
    // A consumed source offers nothing to transform.
    await expect(page.getByText('Bölme veya birleştirme için etiketin tek bir fiziksel pozisyonda stoğu olmalı.', { exact: true })).toBeVisible()

    const childA = await openLabel(page, childAId, CHILD_A)
    expect(childA.balances).toMatchObject({ physicalKg: '4', freeKg: '4' })
    expect(childA.genealogy.edges.map((edge) => [edge.kind, edge.sourceCode, edge.outputCode, edge.outputQuantityKg]))
      .toEqual([['Split', SOURCE, CHILD_A, '4']])
    await expect(genealogyRows(page).filter({ hasText: 'Bölme' })).toHaveCount(1)
    const childB = await readDetail(page, transformerActor, childBId)
    expect(childB.balances).toMatchObject({ physicalKg: '6', freeKg: '6' })

    // ── 5. Same key replays with nothing new; the consumed version is refused. ────────────────────────────
    const replay = await page.request.post(`${env.apiBaseUrl}${TRANSFORMATIONS}`, {
      headers: { ...commandHeaders(transformerActor, splitKey), 'X-Correlation-Id': 'r19-split-replay' },
      data: splitBody,
    })
    expect(replay.status()).toBe(200)
    expect(replay.headers()['idempotency-replayed']).toBe('true')
    expect(replay.headers()['idempotency-original-correlation-id']).toBe(splitCorrelation)
    expect(await replay.json()).toEqual(splitView)
    expect(await effects(pool)).toEqual({ transformations: 1, commands: 1, audits: 1, labels: 3 })

    const stale = await page.request.post(`${env.apiBaseUrl}${TRANSFORMATIONS}`, {
      headers: commandHeaders(transformerActor, `r19-stale-${randomUUID()}`),
      data: {
        ...sentSplit,
        transformationId: randomUUID(),
        outputs: [
          { labelId: randomUUID(), code: 'SYNR19-STALE-A', quantityKg: '4' },
          { labelId: randomUUID(), code: 'SYNR19-STALE-B', quantityKg: '6' },
        ],
      },
    })
    expect(stale.status()).toBe(409)
    expect(((await stale.json()) as { code?: string }).code).toBe('inventory_label_version_conflict')
    expect(await effects(pool)).toEqual({ transformations: 1, commands: 1, audits: 1, labels: 3 })
    expect(await journeyMassKg(pool)).toBe('10')

    // ── 6. Merge the two children back in the UI. ─────────────────────────────────────────────────────────
    await openLabel(page, childAId, CHILD_A)
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toBeVisible()
    await page.getByRole('radio', { name: 'Birleştir', exact: true }).check()
    await page.getByLabel('Etiket ara', { exact: true }).fill(CHILD_B)
    const candidates = page.waitForResponse((response) => isCall(response, 'GET', POSITIONS)
      && new URL(response.url()).searchParams.get('q') === CHILD_B)
    await page.getByRole('button', { name: 'Etiketleri getir', exact: true }).click()
    expect((await candidates).status()).toBe(200)
    await page.getByRole('button', { name: `Ekle: ${CHILD_B}`, exact: true }).click()
    await expect(page.getByText(`Birleşik paket: ${CHILD_A} + ${CHILD_B} = 10 kg`, { exact: true })).toBeVisible()
    await page.getByLabel('Birleşik paket etiket kodu', { exact: true }).fill(MERGED)
    await page.getByRole('textbox', { name: 'Gerekçe', exact: true }).fill('SENTETIK R19 paket birleştirme')
    const mergeAnswer = page.waitForResponse((response) => isCall(response, 'POST', TRANSFORMATIONS))
    await page.getByRole('button', { name: 'Birleştirmeyi kaydet', exact: true }).click()
    const mergeResponse = await mergeAnswer
    expect(mergeResponse.status()).toBe(201)
    const sentMerge = JSON.parse(mergeResponse.request().postData() ?? '') as TransformationRequest
    expect(sentMerge).toMatchObject({
      kind: 'Merge',
      sources: [{ labelId: childAId, expectedLabelVersion: '1' }, { labelId: childBId, expectedLabelVersion: '1' }],
      outputs: [{ code: MERGED, quantityKg: '10' }],
    })
    const mergedId = sentMerge.outputs[0].labelId
    expect((await mergeResponse.json()) as TransformationView).toMatchObject({
      transformationId: sentMerge.transformationId, companyId, kind: 'Merge',
      sourceLabelIds: [childAId, childBId], outputLabelIds: [mergedId],
    })
    await expect(page.getByRole('heading', { level: 2, name: 'Birleştirme kaydedildi', exact: true })).toBeVisible()
    expect(await effects(pool)).toEqual({ transformations: 2, commands: 2, audits: 2, labels: 4 })
    expect(await positiveBalances(pool)).toEqual({ [MERGED]: '10' })
    expect(await journeyMassKg(pool)).toBe('10')

    const merged = await openLabel(page, mergedId, MERGED)
    expect(merged.balances).toMatchObject({ physicalKg: '10', freeKg: '10' })
    expect(merged.genealogy.windowFull).toBe(false)
    expect(merged.genealogy.edges.map((edge) => [edge.kind, edge.sourceCode, edge.outputCode]).sort()).toEqual([
      ['Merge', CHILD_A, MERGED], ['Merge', CHILD_B, MERGED], ['Split', SOURCE, CHILD_A], ['Split', SOURCE, CHILD_B],
    ])
    await expect(genealogyRows(page).filter({ hasText: 'Birleştirme' })).toHaveCount(2)
    await expect(genealogyRows(page).filter({ hasText: 'Bölme' })).toHaveCount(2)
    for (const childId of [childAId, childBId]) {
      expect((await readDetail(page, transformerActor, childId)).balances).toMatchObject({ physicalKg: '0' })
    }

    // ── 7. Fences on the merged label. ────────────────────────────────────────────────────────────────────
    await signInAs(page, recorderActor)
    await openLabel(page, mergedId, MERGED)
    await expect(page.getByRole('heading', { level: 2, name: PANEL_HEADING, exact: true })).toHaveCount(0)
    const mergedSplit: TransformationRequest = {
      transformationId: randomUUID(), kind: 'Split',
      sources: [{ labelId: mergedId, expectedLabelVersion: merged.labelVersion }],
      outputs: [
        { labelId: randomUUID(), code: 'SYNR19-FENCE-A', quantityKg: '5' },
        { labelId: randomUUID(), code: 'SYNR19-FENCE-B', quantityKg: '5' },
      ],
      occurredAtUtc: new Date().toISOString(), reason: 'SENTETIK R19 çit denemesi',
    }
    const recorderDenied = await page.request.post(`${env.apiBaseUrl}${TRANSFORMATIONS}`, {
      headers: commandHeaders(recorderActor, `r19-fence-recorder-${randomUUID()}`), data: mergedSplit,
    })
    expect(recorderDenied.status()).toBe(403)
    const elsewhere = await page.request.post(`${env.apiBaseUrl}${TRANSFORMATIONS}`, {
      headers: commandHeaders(transformerActor, `r19-fence-elsewhere-${randomUUID()}`, otherCompanyId), data: mergedSplit,
    })
    // No membership in the other company: the company-context fence refuses before any read, a plain 403.
    expect(elsewhere.status()).toBe(403)
    const elsewhereBody = await elsewhere.text()
    expect((JSON.parse(elsewhereBody) as { code?: string }).code).toBe('forbidden')
    expect(elsewhereBody).not.toContain(MERGED)
    expect(elsewhereBody).not.toContain(mergedId)

    expect(await effects(pool)).toEqual({ transformations: 2, commands: 2, audits: 2, labels: 4 })
    expect(await positiveBalances(pool)).toEqual({ [MERGED]: '10' })
    expect(await journeyMassKg(pool)).toBe('10')
    // The product the journey started from is the r14 stock candidate; nothing else was touched.
    expect(productCode).toMatch(/^SYNR14-[1-9]\d*$/)
  } finally {
    await pool.close()
  }
})
