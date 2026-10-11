import { randomUUID } from 'node:crypto'
import { expect, test, type Page, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

/**
 * D-05 Inventory → Onaylarım on the REAL r14 stack. Nothing is mocked or intercepted.
 *
 * The test arranges one pending stock count and one pending stock scrap through Inventory's native commands. Work is
 * read-only: its list/detail only route the combined approver to the exact label. Inventory's native label panels own
 * both decisions, idempotent replays and movements. The disposable actors preserve maker ≠ checker and existing
 * per-kind authority; `r14-both-approver` merely combines the two already-existing approve capabilities for this
 * cross-source journey and has neither record capability.
 */
const env = readEnvironment()
const {
  recorderActor, counterActor, countApproverActor, scrapperActor, scrapApproverActor, bothApproverActor, deniedActor,
  companyId, otherCompanyId, locationId, productId, productVersion,
} = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const OPENINGS = '/api/v1/inventory/opening-counts'
const COUNTS = '/api/v1/inventory/stock-counts'
const SCRAPS = '/api/v1/inventory/stock-scraps'
const WORK = '/api/v1/work/approvals'
const WORK_CONTRACT_HEADER = 'X-ERP-Work-Approval-Contract'
const WORK_CONTRACT_V2 = '2.0'
const CAPABILITIES = '/api/v1/shell/capabilities'
const FINANCE_KIND = 'finance.official-invoice-import.v1'
const SALES_KIND = 'sales.order-draft.v1'
const COUNT_KIND = 'inventory.stock-count.v1'
const SCRAP_KIND = 'inventory.stock-scrap.v1'
const COUNT_APPROVE = 'inventory.stock-count.approve'
const COUNT_RECORD = 'inventory.stock-count.record'
const SCRAP_APPROVE = 'inventory.stock-scrap.approve'
const SCRAP_RECORD = 'inventory.stock-scrap.record'
const VIEWPORTS = [[1440, 900], [390, 844]] as const
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

interface LabelDetail {
  readonly labelVersion: string
}

interface OpeningBody {
  readonly lines: readonly { readonly labelId: string; readonly labelCode: string }[]
}

interface CountBody {
  readonly countId: string
  readonly labelId: string
  readonly status: string
  readonly decision: null | { readonly adjustmentMovementId: string | null }
}

interface ScrapBody {
  readonly scrapId: string
  readonly labelId: string
  readonly status: string
  readonly decision: null | { readonly scrapMovementId: string | null }
}

interface PendingPair {
  readonly countId: string
  readonly countLabelId: string
  readonly countLabelCode: string
  readonly scrapId: string
  readonly scrapLabelId: string
  readonly scrapLabelCode: string
}

interface WorkObservation {
  readonly reads: string[]
  readonly posts: string[]
  readonly companies: (string | null)[]
}

interface WorkApprovalItemBody {
  readonly kind: string
  readonly approvalId: string
  readonly labelCode?: string | null
}

interface WorkApprovalPageBody {
  readonly items: readonly WorkApprovalItemBody[]
}

interface WorkApprovalDetailBody {
  readonly item: WorkApprovalItemBody
}

type SqlPool = Awaited<ReturnType<InstanceType<typeof sql.ConnectionPool>['connect']>>

function apiHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company }
}

function commandHeaders(actor: string, key: string, company = companyId): Record<string, string> {
  return { ...apiHeaders(actor, company), 'Content-Type': 'application/json', 'Idempotency-Key': key }
}

function isCall(response: Response, method: 'GET' | 'POST', path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

async function signIn(page: Page, actor: string, width: number, height: number): Promise<void> {
  await page.goto('about:blank')
  await page.setViewportSize({ width, height })
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function capabilitiesOf(page: Page, actor: string): Promise<readonly string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${CAPABILITIES}`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(body.capabilities) ? body.capabilities as string[] : []
}

async function readLabel(page: Page, actor: string, labelId: string): Promise<LabelDetail> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/labels/${labelId}/detail`, {
    headers: apiHeaders(actor),
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as LabelDetail
}

async function arrangePendingPair(page: Page, suffix: string): Promise<PendingPair> {
  const countLabelCode = `SYND05C${suffix}`
  const scrapLabelCode = `SYND05S${suffix}`
  const opening = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
    headers: commandHeaders(recorderActor, `d05-opening-${randomUUID()}`),
    data: {
      countId: randomUUID(), countedOn: '2026-10-11', sheetReference: `SENTETIK D-05 Work ${suffix}`,
      locationId,
      lines: [countLabelCode, scrapLabelCode].map((labelCode, index) => ({
        lineId: randomUUID(), labelId: randomUUID(), labelCode, lotId: randomUUID(),
        heatNumber: `SYND05-HEAT-${suffix}-${index + 1}`, coilNumber: null,
        productId, productVersion, quantityKg: '10',
      })),
    },
  })
  expect(opening.status()).toBe(201)
  const opened = (await opening.json()) as OpeningBody
  const countLine = opened.lines.find((line) => line.labelCode === countLabelCode)
  const scrapLine = opened.lines.find((line) => line.labelCode === scrapLabelCode)
  expect(countLine).toBeDefined()
  expect(scrapLine).toBeDefined()
  const countLabelId = countLine?.labelId ?? ''
  const scrapLabelId = scrapLine?.labelId ?? ''
  expect(countLabelId).toMatch(GUID)
  expect(scrapLabelId).toMatch(GUID)

  const countLabel = await readLabel(page, counterActor, countLabelId)
  const countId = randomUUID()
  const count = await page.request.post(`${env.apiBaseUrl}${COUNTS}`, {
    headers: commandHeaders(counterActor, `d05-count-${randomUUID()}`),
    data: {
      countId, labelId: countLabelId, expectedLabelVersion: countLabel.labelVersion,
      countedKg: '7', countedAtUtc: new Date().toISOString(), reason: `SENTETIK D-05 sayım ${suffix}`,
    },
  })
  expect(count.status()).toBe(201)
  expect((await count.json()) as CountBody).toMatchObject({ countId, labelId: countLabelId, status: 'PendingApproval', decision: null })

  const scrapLabel = await readLabel(page, scrapperActor, scrapLabelId)
  const scrapId = randomUUID()
  const scrap = await page.request.post(`${env.apiBaseUrl}${SCRAPS}`, {
    headers: commandHeaders(scrapperActor, `d05-scrap-${randomUUID()}`),
    data: {
      scrapId, labelId: scrapLabelId, expectedLabelVersion: scrapLabel.labelVersion,
      scrapKg: '4', scrappedAtUtc: new Date().toISOString(), reason: `SENTETIK D-05 hurda ${suffix}`,
    },
  })
  expect(scrap.status()).toBe(201)
  expect((await scrap.json()) as ScrapBody).toMatchObject({ scrapId, labelId: scrapLabelId, status: 'PendingApproval', decision: null })
  return { countId, countLabelId, countLabelCode, scrapId, scrapLabelId, scrapLabelCode }
}

function contractHeaders(actor: string, version?: string): Record<string, string> {
  return version === undefined
    ? apiHeaders(actor)
    : { ...apiHeaders(actor), [WORK_CONTRACT_HEADER]: version }
}

function findApproval(
  page: WorkApprovalPageBody,
  kind: string,
  approvalId: string,
): WorkApprovalItemBody {
  const matching = page.items.filter((item) => item.kind === kind && item.approvalId === approvalId)
  expect(matching).toHaveLength(1)
  return matching[0]
}

async function expectInventoryLabelDisplayContract(page: Page, pair: PendingPair): Promise<void> {
  const cases = [
    { kind: COUNT_KIND, approvalId: pair.countId, labelCode: pair.countLabelCode },
    { kind: SCRAP_KIND, approvalId: pair.scrapId, labelCode: pair.scrapLabelCode },
  ] as const

  for (const itemCase of cases) {
    const listPath = `${env.apiBaseUrl}${WORK}?kind=${itemCase.kind}`
    const detailPath = `${env.apiBaseUrl}${WORK}/${itemCase.kind}/${itemCase.approvalId}`

    const legacyList = await page.request.get(listPath, { headers: contractHeaders(bothApproverActor) })
    expect(legacyList.status()).toBe(200)
    expect(legacyList.headers()[WORK_CONTRACT_HEADER.toLowerCase()]).toBeUndefined()
    const legacyListItem = findApproval(
      (await legacyList.json()) as WorkApprovalPageBody,
      itemCase.kind,
      itemCase.approvalId,
    )
    expect(Object.hasOwn(legacyListItem, 'labelCode')).toBe(false)

    const enrichedList = await page.request.get(listPath, {
      headers: contractHeaders(bothApproverActor, WORK_CONTRACT_V2),
    })
    expect(enrichedList.status()).toBe(200)
    expect(enrichedList.headers()[WORK_CONTRACT_HEADER.toLowerCase()]).toBe(WORK_CONTRACT_V2)
    expect(findApproval(
      (await enrichedList.json()) as WorkApprovalPageBody,
      itemCase.kind,
      itemCase.approvalId,
    ).labelCode).toBe(itemCase.labelCode)

    const legacyDetail = await page.request.get(detailPath, { headers: contractHeaders(bothApproverActor) })
    expect(legacyDetail.status()).toBe(200)
    expect(legacyDetail.headers()[WORK_CONTRACT_HEADER.toLowerCase()]).toBeUndefined()
    const legacyDetailItem = ((await legacyDetail.json()) as WorkApprovalDetailBody).item
    expect(legacyDetailItem).toMatchObject({ kind: itemCase.kind, approvalId: itemCase.approvalId })
    expect(Object.hasOwn(legacyDetailItem, 'labelCode')).toBe(false)

    const enrichedDetail = await page.request.get(detailPath, {
      headers: contractHeaders(bothApproverActor, WORK_CONTRACT_V2),
    })
    expect(enrichedDetail.status()).toBe(200)
    expect(enrichedDetail.headers()[WORK_CONTRACT_HEADER.toLowerCase()]).toBe(WORK_CONTRACT_V2)
    expect(((await enrichedDetail.json()) as WorkApprovalDetailBody).item).toMatchObject({
      kind: itemCase.kind,
      approvalId: itemCase.approvalId,
      labelCode: itemCase.labelCode,
    })
  }

  // The Inventory selector neither creates Finance/Sales authority nor discloses the arranged Inventory records.
  for (const kind of [FINANCE_KIND, SALES_KIND]) {
    for (const version of [undefined, WORK_CONTRACT_V2]) {
      const response = await page.request.get(`${env.apiBaseUrl}${WORK}?kind=${kind}`, {
        headers: contractHeaders(bothApproverActor, version),
      })
      expect(response.status()).toBe(403)
      expect(response.headers()[WORK_CONTRACT_HEADER.toLowerCase()])
        .toBe(version === undefined ? undefined : WORK_CONTRACT_V2)
      const text = await response.text()
      expect(text).not.toContain(pair.countId)
      expect(text).not.toContain(pair.scrapId)
      expect(text).not.toContain(pair.countLabelCode)
      expect(text).not.toContain(pair.scrapLabelCode)
    }
  }
}

function observeWork(page: Page): WorkObservation {
  const observed: WorkObservation = { reads: [], posts: [], companies: [] }
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (!url.pathname.startsWith(WORK)) return
    const call = `${request.method()} ${url.pathname}${url.search}`
    if (request.method() === 'GET') observed.reads.push(call)
    else observed.posts.push(call)
    observed.companies.push(request.headers()['x-company-id'] ?? null)
  })
  return observed
}

async function expectProfile(page: Page, actor: string, expectedKind: string, approvalId: string,
  forbiddenKind: string, width: number, height: number): Promise<void> {
  await signIn(page, actor, width, height)
  const observed = observeWork(page)
  await page.goto('/onaylarim')
  const table = page.getByTestId('work-approvals-table')
  await expect(table).toHaveAttribute('data-kind', expectedKind)
  await expect(table.locator(`tr[data-approval-id="${approvalId}"]`)).toHaveCount(1)
  await expect(page.getByRole('navigation', { name: 'Onay kaynağı' })).toHaveCount(0)
  expect(observed.reads.some((call) => call.includes(`kind=${forbiddenKind}`))).toBe(false)
  expect(observed.posts).toEqual([])
}

async function movementCount(pool: SqlPool, sourceKind: string, sourceId: string): Promise<number> {
  const result = await pool.request()
    .input('companyId', sql.UniqueIdentifier, companyId)
    .input('sourceKind', sql.NVarChar, sourceKind)
    .input('sourceId', sql.UniqueIdentifier, sourceId)
    .query(`SELECT COUNT(*) AS [count]
            FROM [inventory].[Movements]
            WHERE [CompanyId] = @companyId AND [SourceKind] = @sourceKind AND [SourceId] = @sourceId`)
  return Number(result.recordset[0].count)
}

async function openWorkDetail(page: Page, kind: string, approvalId: string, labelId: string): Promise<void> {
  const table = page.getByTestId('work-approvals-table')
  const row = table.locator(`tr[data-approval-id="${approvalId}"]`)
  await expect(row).toHaveCount(1)
  await expect(row).toHaveAttribute('data-label-id', labelId)
  await row.getByRole('link', { name: 'Ayrıntı', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/onaylarim/${kind}/${approvalId}$`))
  const facts = page.getByTestId('work-approval-facts')
  await expect(facts).toBeVisible()
  await expect(facts).toContainText(labelId)
}

async function openNativeLabel(page: Page, labelId: string): Promise<void> {
  const detailRead = page.waitForResponse((response) =>
    isCall(response, 'GET', `/api/v1/inventory/labels/${labelId}/detail`))
  await page.getByRole('link', { name: 'Etiket ekranında aç', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/stok/etiket/${labelId}$`))
  expect((await detailRead).status()).toBe(200)
}

async function approveCount(page: Page, pool: SqlPool, pair: PendingPair): Promise<void> {
  await page.getByLabel('Karar notu', { exact: true }).fill('SENTETIK D-05 Work sayım onayı')
  const path = `${COUNTS}/${pair.countId}/decision`
  const answer = page.waitForResponse((response) => isCall(response, 'POST', path))
  await page.getByRole('button', { name: 'Farkı onayla', exact: true }).click()
  const response = await answer
  expect(response.status()).toBe(201)
  const key = (await response.request().headerValue('Idempotency-Key')) ?? ''
  const body = response.request().postData() ?? ''
  expect(key).toMatch(GUID)
  const decided = (await response.json()) as CountBody
  const movementId = decided.decision?.adjustmentMovementId ?? ''
  expect(movementId).toMatch(GUID)
  expect(await movementCount(pool, 'inventory.stock-count', pair.countId)).toBe(1)

  const replay = await page.request.post(`${env.apiBaseUrl}${path}`, {
    headers: commandHeaders(bothApproverActor, key), data: body,
  })
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  expect(((await replay.json()) as CountBody).decision?.adjustmentMovementId).toBe(movementId)
  expect(await movementCount(pool, 'inventory.stock-count', pair.countId)).toBe(1)
}

async function approveScrap(page: Page, pool: SqlPool, pair: PendingPair): Promise<void> {
  await page.getByLabel('Karar notu', { exact: true }).fill('SENTETIK D-05 Work hurda onayı')
  const path = `${SCRAPS}/${pair.scrapId}/decision`
  const answer = page.waitForResponse((response) => isCall(response, 'POST', path))
  await page.getByRole('button', { name: 'Hurdayı onayla', exact: true }).click()
  const response = await answer
  expect(response.status()).toBe(201)
  const key = (await response.request().headerValue('Idempotency-Key')) ?? ''
  const body = response.request().postData() ?? ''
  expect(key).toMatch(GUID)
  const decided = (await response.json()) as ScrapBody
  const movementId = decided.decision?.scrapMovementId ?? ''
  expect(movementId).toMatch(GUID)
  expect(await movementCount(pool, 'inventory.stock-scrap', pair.scrapId)).toBe(1)

  const replay = await page.request.post(`${env.apiBaseUrl}${path}`, {
    headers: commandHeaders(bothApproverActor, key), data: body,
  })
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  expect(((await replay.json()) as ScrapBody).decision?.scrapMovementId).toBe(movementId)
  expect(await movementCount(pool, 'inventory.stock-scrap', pair.scrapId)).toBe(1)
}

test.describe.configure({ mode: 'serial' })

for (const [width, height] of VIEWPORTS) {
  test(`D05-R14 ${width}px: Inventory sources stay separated, open native panels and leave one movement`, async ({ page }) => {
    test.setTimeout(300_000)
    const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
    try {
      // Genuine disposable authority: existing per-kind actors remain separate; one new test actor combines approve only.
      const counterCaps = await capabilitiesOf(page, counterActor)
      const countApproverCaps = await capabilitiesOf(page, countApproverActor)
      const scrapperCaps = await capabilitiesOf(page, scrapperActor)
      const scrapApproverCaps = await capabilitiesOf(page, scrapApproverActor)
      const bothCaps = await capabilitiesOf(page, bothApproverActor)
      const deniedCaps = await capabilitiesOf(page, deniedActor)
      expect(counterCaps).toContain(COUNT_RECORD)
      expect(counterCaps).not.toContain(COUNT_APPROVE)
      expect(countApproverCaps).toContain(COUNT_APPROVE)
      expect(countApproverCaps).not.toContain(SCRAP_APPROVE)
      expect(scrapperCaps).toContain(SCRAP_RECORD)
      expect(scrapperCaps).not.toContain(SCRAP_APPROVE)
      expect(scrapApproverCaps).toContain(SCRAP_APPROVE)
      expect(scrapApproverCaps).not.toContain(COUNT_APPROVE)
      expect(bothCaps).toEqual(expect.arrayContaining([COUNT_APPROVE, SCRAP_APPROVE]))
      expect(bothCaps).not.toContain(COUNT_RECORD)
      expect(bothCaps).not.toContain(SCRAP_RECORD)
      expect(deniedCaps).not.toContain(COUNT_APPROVE)
      expect(deniedCaps).not.toContain(SCRAP_APPROVE)

      const pair = await arrangePendingPair(page, width.toString())

      // The same genuine pending rows prove legacy omission and the explicit Work 2.0 label-display contract.
      await expectInventoryLabelDisplayContract(page, pair)

      // Each single-source actor sees only its native source; no capability is inferred from the Work screen.
      await expectProfile(page, countApproverActor, COUNT_KIND, pair.countId, SCRAP_KIND, width, height)
      await expectProfile(page, scrapApproverActor, SCRAP_KIND, pair.scrapId, COUNT_KIND, width, height)

      // The denied actor gets neither navigation nor Work read, even with a direct route.
      await signIn(page, deniedActor, width, height)
      const deniedObserved = observeWork(page)
      await page.goto('/')
      await expect(page.getByTestId('home-approvals')).toHaveCount(0)
      await page.goto('/onaylarim')
      await expect(page.locator('[data-state="not-found"]')).toBeVisible()
      await page.goto(`/onaylarim/${COUNT_KIND}/${pair.countId}`)
      await expect(page.locator('[data-state="not-found"]')).toBeVisible()
      expect(deniedObserved.reads).toEqual([])
      expect(deniedObserved.posts).toEqual([])

      // The combined approver switches between exactly the two Inventory sources; no rows leak across sources.
      await signIn(page, bothApproverActor, width, height)
      const observed = observeWork(page)
      await page.goto('/')
      const home = page.getByTestId('home-approvals')
      await expect(home).toContainText('Stok sayım farkı')
      await expect(home).toContainText('Stok hurda çıkışı')
      await home.getByRole('link', { name: 'Bekleyen onaylarımı aç' }).click()
      const nav = page.getByRole('navigation', { name: 'Onay kaynağı' })
      const table = page.getByTestId('work-approvals-table')
      await expect(table).toHaveAttribute('data-kind', COUNT_KIND)
      await expect(table.locator('tbody tr')).toHaveCount(1)
      await expect(table.locator(`tr[data-approval-id="${pair.scrapId}"]`)).toHaveCount(0)
      await nav.getByRole('link', { name: 'Stok hurda çıkışı', exact: true }).click()
      await expect(table).toHaveAttribute('data-kind', SCRAP_KIND)
      await expect(table.locator('tbody tr')).toHaveCount(1)
      await expect(table.locator(`tr[data-approval-id="${pair.countId}"]`)).toHaveCount(0)
      await nav.getByRole('link', { name: 'Stok sayım farkı', exact: true }).click()
      await expect(table).toHaveAttribute('data-kind', COUNT_KIND)
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)

      // Work detail names the exact label; Inventory's own label screen owns the count decision and replay.
      await openWorkDetail(page, COUNT_KIND, pair.countId, pair.countLabelId)
      await openNativeLabel(page, pair.countLabelId)
      await expect(page.getByRole('heading', { level: 1, name: pair.countLabelCode, exact: true })).toBeVisible()
      await expect(page.getByRole('heading', { level: 2, name: 'Sayım ve fark (ST-05)', exact: true })).toBeVisible()
      await approveCount(page, pool, pair)

      // The decided count disappears; the independent scrap stays and opens its own native panel.
      await page.goto(`/onaylarim?kaynak=stok-sayim`)
      await expect(page.getByTestId('work-approvals-table')).toHaveCount(0)
      await page.goto(`/onaylarim?kaynak=stok-hurda`)
      await expect(table).toHaveAttribute('data-kind', SCRAP_KIND)
      await openWorkDetail(page, SCRAP_KIND, pair.scrapId, pair.scrapLabelId)
      await openNativeLabel(page, pair.scrapLabelId)
      await expect(page.getByRole('heading', { level: 1, name: pair.scrapLabelCode, exact: true })).toBeVisible()
      await expect(page.getByRole('heading', { level: 2, name: 'Hurda / fire çıkışı', exact: true })).toBeVisible()
      await approveScrap(page, pool, pair)
      await page.goto(`/onaylarim?kaynak=stok-hurda`)
      await expect(page.getByTestId('work-approvals-table')).toHaveCount(0)

      expect(observed.posts, 'Onaylarım itself must never issue a Work command').toEqual([])
      expect(observed.companies.every((value) => value?.toLowerCase() === companyId.toLowerCase())).toBe(true)

      // The actor has no assignment in the other company. The response discloses neither pending identity.
      const foreign = await page.request.get(`${env.apiBaseUrl}${WORK}?kind=${COUNT_KIND}`, {
        headers: apiHeaders(bothApproverActor, otherCompanyId),
      })
      expect(foreign.status()).toBe(403)
      const foreignText = await foreign.text()
      expect(foreignText).not.toContain(pair.countId)
      expect(foreignText).not.toContain(pair.scrapId)

      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
    } finally {
      await pool.close()
    }
  })
}
