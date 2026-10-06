import { expect, test, type Page, type Request as BrowserRequest, type Response } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment, readSqlConnection } from '../src/env'
import { readLocationFacts, readReceiptEffects, RECEIPT_SOURCE_KIND, ZERO_GUID, type ReceiptEffects } from '../src/receiptEvidence'

/**
 * PUR01 goods receipt (#478 PUR-HOST) on the disposable normal stack. The approved order is arranged through the
 * real create and approval commands (a precondition, like P01's linked order); everything after that is the
 * receiver's own browser path: open the order, record ONE partial receipt at an authorized location, close and
 * reopen it, then the SAME label is read in Inventory by a separate stock reader. The same-key resend replays with
 * no second label, lot, movement, acceptance or command (API + read-only SQL counts). Approver and reader, who hold
 * no receipt grant, never get the control. No response is mocked and no row is written outside the real commands.
 */
const env = readEnvironment()
const sqlConnection = readSqlConnection()
const {
  creatorActor, approverActor, readerActor, receiverActor, stockReaderActor, companyId, otherCompanyId,
  supplierPartyId, companyAccountId, productId, productVersion, productUnitId,
} = env.seed

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const ORDERS_PATH = '/api/v1/purchasing/orders'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const LIST_ROUTE = '/satinalma/siparisler'
const LABEL_ROUTE = '/stok/etiket'
const RECEIPT_CAPABILITY = 'purchasing.goods-receipts.record'
const STOCK_LABEL_CAPABILITY = 'inventory.stock-positions.list'
const EFFECTIVE_ON = '2026-09-18'
const ORDER_LINE_ID = '7b1c0f2e-0000-4a00-8000-000000000201'
const ORDERED = '10'
const ACCEPTED = '4'
const REMAINING = '6'
const MEASURED_KG = '4.5'
const LABEL_CODE = 'SYNPUR01-LABEL-1'
const HEAT_NUMBER = 'SYNPUR01-HEAT-1'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const RECEIPT_REQUEST_FIELDS = [
  'acceptanceId', 'acceptedQuantity', 'coilNumber', 'expectedProductVersion', 'expectedPurchaseOrderVersion',
  'heatNumber', 'labelCode', 'labelId', 'locationId', 'lotId', 'measuredQuantityKg', 'movementId', 'occurredAtUtc',
]

interface OrderLine {
  readonly lineId: string
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly ordered: string
  readonly accepted: string
  readonly remaining: string
}

interface OrderDetail {
  readonly order: {
    readonly id: string
    readonly companyId: string
    readonly status: 'Draft' | 'Approved'
    readonly version: string
    readonly supplierPartyId: string
    readonly companyAccountId: string
    readonly effectiveOn: string
  }
  readonly lines: readonly OrderLine[]
}

interface ReceiptRequest {
  readonly expectedPurchaseOrderVersion: string
  readonly expectedProductVersion: string
  readonly acceptanceId: string
  readonly acceptedQuantity: string
  readonly measuredQuantityKg: string
  readonly locationId: string
  readonly lotId: string
  readonly labelId: string
  readonly labelCode: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly movementId: string
  readonly occurredAtUtc: string
}

interface Receipt {
  readonly companyId: string
  readonly acceptanceId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly purchaseOrderVersion: string
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly acceptedQuantity: string
  readonly remainingQuantity: string
  readonly lotId: string
  readonly labelId: string
  readonly labelCode: string
  readonly labelVersion: string
  readonly movementId: string
  readonly locationId: string
  readonly measuredQuantityKg: string
  readonly initialBlock: 'Free' | 'Blocked'
  readonly occurredAtUtc: string
}

interface ReceiptResponse { readonly receipt: Receipt; readonly isReplay: boolean; readonly originalCorrelationId: string }
interface ReceiptPage { readonly items: readonly Receipt[]; readonly searchWindowFull: boolean }
interface ReceiptComparison {
  readonly companyId: string
  readonly acceptanceId: string
  readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string
  readonly purchaseOrderVersion: string
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly acceptedQuantity: string
  readonly measuredQuantityKg: string
  readonly comparisonStatus: 'SameKilogramUnit' | 'Unavailable'
  readonly acceptedQuantityKg: string | null
  readonly measuredMinusAcceptedKg: string | null
}
interface ReceiptOptions {
  readonly source: Record<string, string>
  readonly locations: readonly { readonly locationId: string; readonly locationCode: string; readonly locationName: string; readonly siteName: string }[]
  readonly searchWindowFull: boolean
}

interface LabelPosition {
  readonly ownerKind: string
  readonly ownerId: string
  readonly custodianKind: string
  readonly custodianId: string
  readonly locationId: string
  readonly productId: string
  readonly block: string
  readonly quantityKg: string
}

interface LabelDetail {
  readonly labelVersion: string
  readonly identity: {
    readonly labelId: string
    readonly companyId: string
    readonly code: string
    readonly productId: string
    readonly lotId: string
    readonly heatNumber: string | null
    readonly coilNumber: string | null
    readonly documentStatus: string
    readonly requiredDocuments: readonly string[]
    readonly documents: readonly unknown[]
  }
  readonly balances: {
    readonly labelId: string
    readonly physicalKg: string
    readonly freeKg: string
    readonly blockedKg: string
    readonly positions: readonly LabelPosition[]
  }
  readonly movementTimeline: {
    readonly labelId: string
    readonly windowFull: boolean
    readonly entries: readonly {
      readonly movementId: string
      readonly kind: string
      readonly sourceKind: string
      readonly sourceId: string
      readonly lines: readonly (LabelPosition & { readonly direction: string })[]
    }[]
  }
}

function apiHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company }
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function isCall(response: Response, method: 'GET' | 'POST', path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path
}

async function readOrder(page: Page, actor: string, orderId: string): Promise<OrderDetail> {
  const response = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}/${orderId}`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  return (await response.json()) as OrderDetail
}

async function readReceipts(page: Page, actor: string, orderId: string): Promise<ReceiptPage> {
  const response = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}/${orderId}/goods-receipts`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  return (await response.json()) as ReceiptPage
}

async function readLabel(page: Page, actor: string, labelId: string): Promise<LabelDetail> {
  const response = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/labels/${labelId}/detail`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  return (await response.json()) as LabelDetail
}

async function capabilitiesOf(page: Page, actor: string): Promise<readonly string[]> {
  const response = await page.request.get(`${env.apiBaseUrl}${CAPABILITIES_PATH}`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(snapshot.capabilities) ? snapshot.capabilities as string[] : []
}

function orderRow(page: Page, orderId: string) {
  return page.getByRole('row').filter({ hasText: `…${orderId.slice(-8)}` })
}

function lineRow(page: Page) {
  return page.getByRole('row').filter({ hasText: `…${productId.slice(-8)}` })
}

/**
 * Closes whatever is open by going to the real list, then opens the order from its row. `arm` runs once the list
 * is on screen and before the row is clicked, so a response waiter can never match a request of the page just left.
 */
async function openOrderFromList(page: Page, orderId: string, arm: () => void = () => undefined): Promise<void> {
  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })).toBeVisible({ timeout: 30_000 })
  arm()
  await orderRow(page, orderId).getByRole('link', { name: `…${orderId.slice(-8)}` }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Satın alma siparişi' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(orderId, { exact: true })).toBeVisible()
}

/** The receipt view without its instant, which is compared as a point in time rather than as text. */
function withoutInstant(receipt: Receipt): Omit<Receipt, 'occurredAtUtc'> {
  const { occurredAtUtc: _instant, ...rest } = receipt
  return rest
}

/** Precondition only: one Draft through the creator's real create command, approved through the approver's real command. */
async function arrangeApprovedOrder(page: Page): Promise<OrderDetail> {
  const created = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}`, {
    headers: {
      ...apiHeaders(creatorActor), 'Idempotency-Key': 'browser-suite:pur01:receipt-order', 'Content-Type': 'application/json',
    },
    data: {
      supplierPartyId, companyAccountId, sourceRequestId: null, effectiveOn: EFFECTIVE_ON,
      lines: [{
        lineId: ORDER_LINE_ID, productId, expectedProductVersion: productVersion, unitId: productUnitId,
        quantity: ORDERED, sourceRequestLineId: null,
      }],
    },
  })
  expect(created.status()).toBe(201)
  const draft = ((await created.json()) as { readonly detail: OrderDetail }).detail
  expect(draft.order).toMatchObject({ companyId, status: 'Draft', supplierPartyId, companyAccountId, effectiveOn: EFFECTIVE_ON })
  expect(draft.lines).toHaveLength(1)
  expect(draft.lines[0]).toMatchObject({ lineId: ORDER_LINE_ID, productId, productVersion, unitId: productUnitId, ordered: ORDERED })

  const approval = await page.request.post(`${env.apiBaseUrl}${ORDERS_PATH}/${draft.order.id}/approval`, {
    headers: {
      ...apiHeaders(approverActor), 'Idempotency-Key': 'browser-suite:pur01:receipt-order-approval',
      'Content-Type': 'application/json',
    },
    data: { expectedVersion: draft.order.version },
  })
  expect(approval.status()).toBe(200)
  const approved = await readOrder(page, receiverActor, draft.order.id)
  expect(approved.order).toMatchObject({
    id: draft.order.id, companyId, status: 'Approved', version: (BigInt(draft.order.version) + 1n).toString(),
  })
  expect(approved.lines).toHaveLength(1)
  expect(approved.lines[0]).toMatchObject({ lineId: ORDER_LINE_ID, ordered: ORDERED, accepted: '0', remaining: ORDERED })
  return approved
}

const NOTHING: ReceiptEffects = {
  acceptancesOnOrder: 0, acceptance: 0, lots: 0, labelsByIdOrCode: 0, receiptMovements: 0,
  receiptMovementById: 0, labelMovementLines: 0, inventoryCommands: 0,
}
const ONCE: ReceiptEffects = {
  acceptancesOnOrder: 1, acceptance: 1, lots: 1, labelsByIdOrCode: 1, receiptMovements: 1,
  receiptMovementById: 1, labelMovementLines: 1, inventoryCommands: 1,
}

test('PUR01-GR records one partial receipt in the browser, reopens it, finds the same Free company label in Inventory and replays the same key once; non-receivers get no control', async ({ page }) => {
  test.setTimeout(240_000)
  const pool = await new sql.ConnectionPool(sqlConnection).connect()
  try {
    // Capabilities are server-measured: only the receiver publishes the receipt control, only the stock reader
    // the Inventory label screen.
    const receiverCapabilities = await capabilitiesOf(page, receiverActor)
    expect(receiverCapabilities).toEqual(expect.arrayContaining(['purchasing.orders.list', 'purchasing.orders.detail', RECEIPT_CAPABILITY]))
    expect(receiverCapabilities).not.toContain('purchasing.orders.approve')
    expect(receiverCapabilities).not.toContain('purchasing.orders.create')
    expect(receiverCapabilities).not.toContain(STOCK_LABEL_CAPABILITY)
    const stockCapabilities = await capabilitiesOf(page, stockReaderActor)
    expect(stockCapabilities).toContain(STOCK_LABEL_CAPABILITY)
    expect(stockCapabilities.filter((id) => id.startsWith('purchasing.'))).toEqual([])

    const approved = await arrangeApprovedOrder(page)
    const orderId = approved.order.id
    const approvedVersion = approved.order.version
    const receiptVersion = (BigInt(approvedVersion) + 1n).toString()
    const optionsPath = `${ORDERS_PATH}/${orderId}/lines/${ORDER_LINE_ID}/goods-receipt-options`
    const receiptsPath = `${ORDERS_PATH}/${orderId}/goods-receipts`
    const comparisonPath = (acceptanceId: string) => `${receiptsPath}/${acceptanceId}/quantity-comparison`
    const recordPath = `${ORDERS_PATH}/${orderId}/lines/${ORDER_LINE_ID}/goods-receipts`
    expect(await readReceiptEffects(pool, {
      companyId, orderId, acceptanceId: ZERO_GUID, lotId: ZERO_GUID, labelId: ZERO_GUID, movementId: ZERO_GUID, labelCode: LABEL_CODE,
    })).toEqual(NOTHING)

    // 2. The receiver opens the order from the real list; the panel reads the authorized receipt locations.
    await signInAs(page, receiverActor)
    const firstOpen: { options?: Promise<Response>; history?: Promise<Response> } = {}
    await openOrderFromList(page, orderId, () => {
      firstOpen.options = page.waitForResponse((response) => isCall(response, 'GET', optionsPath))
      firstOpen.history = page.waitForResponse((response) => isCall(response, 'GET', receiptsPath))
    })
    const optionsResponse = await firstOpen.options!
    expect(optionsResponse.status()).toBe(200)
    const options = (await optionsResponse.json()) as ReceiptOptions
    expect(options.source).toEqual({
      companyId, purchaseOrderId: orderId, purchaseOrderLineId: ORDER_LINE_ID, purchaseOrderVersion: approvedVersion,
      productId, productVersion, unitId: productUnitId, effectiveOn: EFFECTIVE_ON,
    })
    expect(options.locations.length).toBeGreaterThan(0)
    const location = options.locations[0]
    // The command's location is Organization's active, internal location of this company, not a typed id.
    expect(await readLocationFacts(pool, companyId, location.locationId)).toEqual({ locations: 1, activeInternal: 1 })
    const historyResponse = await firstOpen.history!
    expect(historyResponse.status()).toBe(200)
    expect(await historyResponse.json()).toEqual({ items: [], searchWindowFull: false })

    const panel = page.getByRole('region', { name: 'Mal kabulü', exact: true })
    await expect(panel).toBeVisible()
    await expect(panel.getByText('Bu sipariş için kaydedilmiş mal kabulü yok.', { exact: true })).toBeVisible()
    await expect(panel.getByText(/İrsaliye veya kantar fişi \(görsel\/PDF\) şimdilik zorunlu değildir\./)).toBeVisible()
    await panel.getByLabel('Kabul lokasyonu').selectOption(location.locationId)
    await panel.getByLabel('Kabul miktarı (sipariş birimi)').fill(ACCEPTED)
    await panel.getByLabel('Tartılan miktar (kg)').fill(MEASURED_KG)
    await panel.getByLabel('Etiket kodu').fill(LABEL_CODE)
    await panel.getByLabel('Döküm numarası (isteğe bağlı)').fill(HEAT_NUMBER)

    const recordAnswer = page.waitForResponse((response) => isCall(response, 'POST', recordPath))
    await panel.getByRole('button', { name: 'Mal kabulünü kaydet', exact: true }).click()
    const recordResponse = await recordAnswer
    expect(recordResponse.status()).toBe(201)
    const sentRequest = recordResponse.request()
    const idempotencyKey = await sentRequest.headerValue('Idempotency-Key')
    expect(idempotencyKey ?? '').toMatch(GUID)
    const sentBody = sentRequest.postData() ?? ''
    const sent = JSON.parse(sentBody) as ReceiptRequest
    // The browser sends the two measured facts and client identities only: no owner, custodian, block or documents.
    expect(Object.keys(sent).sort()).toEqual(RECEIPT_REQUEST_FIELDS)
    expect(sent).toMatchObject({
      expectedPurchaseOrderVersion: approvedVersion, expectedProductVersion: productVersion, acceptedQuantity: ACCEPTED,
      measuredQuantityKg: MEASURED_KG, locationId: location.locationId, labelCode: LABEL_CODE, heatNumber: HEAT_NUMBER,
      coilNumber: null,
    })
    const ids = [sent.acceptanceId, sent.lotId, sent.labelId, sent.movementId]
    for (const id of ids) expect(id).toMatch(GUID)
    expect(new Set(ids).size).toBe(4)
    expect(recordResponse.headers()['idempotency-replayed']).not.toBe('true')
    expect(recordResponse.headers()['location']).toContain(receiptsPath)

    const recorded = (await recordResponse.json()) as ReceiptResponse
    expect(recorded.isReplay).toBe(false)
    expect(recorded.originalCorrelationId.length).toBeGreaterThan(0)
    expect(recorded.receipt.labelVersion).toMatch(/^[1-9]\d*$/)
    expect(Date.parse(recorded.receipt.occurredAtUtc)).toBe(Date.parse(sent.occurredAtUtc))
    expect(withoutInstant(recorded.receipt)).toEqual({
      companyId, acceptanceId: sent.acceptanceId, purchaseOrderId: orderId, purchaseOrderLineId: ORDER_LINE_ID,
      purchaseOrderVersion: receiptVersion, productId, productVersion, unitId: productUnitId,
      acceptedQuantity: ACCEPTED, remainingQuantity: REMAINING, lotId: sent.lotId, labelId: sent.labelId,
      labelCode: LABEL_CODE, labelVersion: recorded.receipt.labelVersion, movementId: sent.movementId,
      locationId: location.locationId, measuredQuantityKg: MEASURED_KG, initialBlock: 'Free',
    })

    await expect(panel.getByRole('status').filter({ hasText: 'Mal kabulü kaydedildi.' }))
      .toContainText(`${LABEL_CODE}: İlk kayıt durumu: Serbest`)
    await expect(lineRow(page).getByRole('cell').nth(6)).toHaveText(ACCEPTED)
    await expect(lineRow(page).getByRole('cell').nth(7)).toHaveText(REMAINING)
    const historyText = `${LABEL_CODE}: Kabul miktarı (sipariş birimi) ${ACCEPTED}; Tartılan miktar (kg) ${MEASURED_KG}. İlk kayıt durumu: Serbest`
    await expect(page.getByTestId(`goods-receipt-${sent.acceptanceId}`)).toContainText(historyText)
    const comparisonCard = page.getByTestId(`goods-receipt-comparison-${sent.acceptanceId}`)
    await expect(comparisonCard).toBeVisible()
    await expect(comparisonCard).toContainText('+0.5')
    // Partial: the line keeps its remaining commitment open for a later receipt.
    await expect(panel.getByRole('button', { name: 'Mal kabulünü kaydet', exact: true })).toBeVisible()

    const identity = {
      companyId, orderId, acceptanceId: sent.acceptanceId, lotId: sent.lotId, labelId: sent.labelId,
      movementId: sent.movementId, labelCode: LABEL_CODE,
    }
    expect(await readReceiptEffects(pool, identity)).toEqual(ONCE)
    const comparisonResponse = await page.request.get(`${env.apiBaseUrl}${comparisonPath(sent.acceptanceId)}`, {
      headers: apiHeaders(receiverActor),
    })
    expect(comparisonResponse.status()).toBe(200)
    const comparison = (await comparisonResponse.json()) as ReceiptComparison
    expect(comparison).toEqual({
      companyId, acceptanceId: sent.acceptanceId, purchaseOrderId: orderId, purchaseOrderLineId: ORDER_LINE_ID,
      purchaseOrderVersion: receiptVersion, productId, productVersion, unitId: productUnitId,
      acceptedQuantity: ACCEPTED, measuredQuantityKg: MEASURED_KG, comparisonStatus: 'SameKilogramUnit',
      acceptedQuantityKg: ACCEPTED, measuredMinusAcceptedKg: '0.5',
    })
    expect(await readReceiptEffects(pool, identity)).toEqual(ONCE)

    // 3. Close the order and reopen it from the list: the same receipt comes from a fresh read, once.
    const reopen: { history?: Promise<Response>; comparison?: Promise<Response> } = {}
    await openOrderFromList(page, orderId, () => {
      reopen.history = page.waitForResponse((response) => isCall(response, 'GET', receiptsPath))
      reopen.comparison = page.waitForResponse((response) => isCall(response, 'GET', comparisonPath(sent.acceptanceId)))
    })
    const reopenedHistoryResponse = await reopen.history!
    expect(reopenedHistoryResponse.status()).toBe(200)
    const reopenedHistory = (await reopenedHistoryResponse.json()) as ReceiptPage
    expect(reopenedHistory.searchWindowFull).toBe(false)
    expect(reopenedHistory.items).toHaveLength(1)
    expect(withoutInstant(reopenedHistory.items[0])).toEqual(withoutInstant(recorded.receipt))
    expect(Date.parse(reopenedHistory.items[0].occurredAtUtc)).toBe(Date.parse(sent.occurredAtUtc))
    const reopenedComparisonResponse = await reopen.comparison!
    expect(reopenedComparisonResponse.status()).toBe(200)
    expect(await reopenedComparisonResponse.json()).toEqual(comparison)
    const reopenedPanel = page.getByRole('region', { name: 'Mal kabulü', exact: true })
    await expect(page.getByTestId(`goods-receipt-${sent.acceptanceId}`)).toContainText(historyText)
    await expect(page.getByTestId(`goods-receipt-comparison-${sent.acceptanceId}`)).toContainText('+0.5')
    await expect(page.locator('li[data-testid^="goods-receipt-"]')).toHaveCount(1)
    await expect(lineRow(page).getByRole('cell').nth(6)).toHaveText(ACCEPTED)
    await expect(lineRow(page).getByRole('cell').nth(7)).toHaveText(REMAINING)
    await expect(reopenedPanel.getByText('Mal kabulü kaydedildi.', { exact: false })).toHaveCount(0)
    await expect(reopenedPanel.getByRole('button', { name: 'Aynı kabulü yeniden gönder', exact: true })).toHaveCount(0)
    const reopenedOrder = await readOrder(page, receiverActor, orderId)
    expect(reopenedOrder.order).toMatchObject({ id: orderId, status: 'Approved', version: receiptVersion })
    expect(reopenedOrder.lines[0]).toMatchObject({ lineId: ORDER_LINE_ID, ordered: ORDERED, accepted: ACCEPTED, remaining: REMAINING })
    expect(await readReceiptEffects(pool, identity)).toEqual(ONCE)

    // The receiver's grant is Purchasing-only: it cannot read the Inventory label itself.
    const receiverLabel = await page.request.get(`${env.apiBaseUrl}/api/v1/inventory/labels/${sent.labelId}/detail`, {
      headers: apiHeaders(receiverActor),
    })
    expect(receiverLabel.status()).toBe(403)
    expect(await receiverLabel.text()).not.toContain(LABEL_CODE)

    // 4. The SAME label in Inventory: company-owned, company-custodied, Free, no required document.
    await signInAs(page, stockReaderActor)
    const labelPath = `/api/v1/inventory/labels/${sent.labelId}/detail`
    const labelAnswer = page.waitForResponse((response) => isCall(response, 'GET', labelPath))
    await page.goto(`${LABEL_ROUTE}/${sent.labelId}`)
    const labelResponse = await labelAnswer
    expect(labelResponse.status()).toBe(200)
    const label = (await labelResponse.json()) as LabelDetail
    const companyPosition = {
      ownerKind: 'Company', ownerId: companyId, custodianKind: 'Company', custodianId: companyId,
      locationId: location.locationId, productId, block: 'Free', quantityKg: MEASURED_KG,
    }
    expect(label.labelVersion).toBe(recorded.receipt.labelVersion)
    expect(label.identity).toMatchObject({
      labelId: sent.labelId, companyId, code: LABEL_CODE, productId, lotId: sent.lotId, heatNumber: HEAT_NUMBER,
      coilNumber: null, classification: null, documentStatus: 'Complete', requiredDocuments: [], documents: [],
    })
    expect(label.balances).toMatchObject({ labelId: sent.labelId, physicalKg: MEASURED_KG, freeKg: MEASURED_KG, blockedKg: '0' })
    expect(label.balances.positions).toHaveLength(1)
    expect(label.balances.positions[0]).toMatchObject(companyPosition)
    expect(label.movementTimeline).toMatchObject({ labelId: sent.labelId, windowFull: false })
    expect(label.movementTimeline.entries).toHaveLength(1)
    expect(label.movementTimeline.entries[0]).toMatchObject({
      movementId: sent.movementId, kind: 'Receipt', sourceKind: RECEIPT_SOURCE_KIND, sourceId: sent.acceptanceId,
    })
    expect(label.movementTimeline.entries[0].lines).toHaveLength(1)
    expect(label.movementTimeline.entries[0].lines[0]).toMatchObject({ ...companyPosition, direction: 'In' })

    await expect(page.getByRole('heading', { level: 1, name: LABEL_CODE, exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(sent.labelId, { exact: true })).toBeVisible()
    const positionRows = page.getByRole('region', { name: 'Sahiplik ve konum', exact: true }).getByRole('row')
      .filter({ hasText: location.locationId })
    await expect(positionRows).toHaveCount(1)
    await expect(positionRows.getByRole('cell').nth(0)).toContainText('Company')
    await expect(positionRows.getByRole('cell').nth(0)).toContainText(companyId)
    await expect(positionRows.getByRole('cell').nth(1)).toContainText('Company')
    await expect(positionRows.getByRole('cell').nth(1)).toContainText(companyId)
    await expect(positionRows.getByRole('cell').nth(3)).toHaveText('Free')
    await expect(positionRows.getByRole('cell').nth(4)).toHaveText(`${MEASURED_KG} kg`)
    await expect(page.getByRole('region', { name: 'Stok hareketleri', exact: true }).getByRole('row')
      .filter({ hasText: RECEIPT_SOURCE_KIND })).toHaveCount(1)
    await expect(page.getByText('Gerekli belgeler: Yok', { exact: true })).toBeVisible()

    // 5. The identical request under the same key replays: same answer, original correlation, nothing new.
    const replay = await page.request.post(`${env.apiBaseUrl}${recordPath}`, {
      headers: {
        ...apiHeaders(receiverActor), 'Idempotency-Key': idempotencyKey ?? '', 'Content-Type': 'application/json',
        'X-Correlation-Id': 'browser-suite-pur01-receipt-replay',
      },
      data: sentBody,
    })
    expect(replay.status()).toBe(201)
    expect(replay.headers()['idempotency-replayed']).toBe('true')
    const replayed = (await replay.json()) as ReceiptResponse
    expect(replayed.isReplay).toBe(true)
    expect(replayed.originalCorrelationId).toBe(recorded.originalCorrelationId)
    expect(withoutInstant(replayed.receipt)).toEqual(withoutInstant(recorded.receipt))
    expect(Date.parse(replayed.receipt.occurredAtUtc)).toBe(Date.parse(sent.occurredAtUtc))

    const afterReplayComparison = await page.request.get(`${env.apiBaseUrl}${comparisonPath(sent.acceptanceId)}`, {
      headers: apiHeaders(receiverActor),
    })
    expect(afterReplayComparison.status()).toBe(200)
    expect(await afterReplayComparison.json()).toEqual(comparison)

    expect(await readReceiptEffects(pool, identity)).toEqual(ONCE)
    const afterReplayOrder = await readOrder(page, receiverActor, orderId)
    expect(afterReplayOrder.order.version).toBe(receiptVersion)
    expect(afterReplayOrder.lines[0]).toMatchObject({ accepted: ACCEPTED, remaining: REMAINING })
    const afterReplayReceipts = await readReceipts(page, receiverActor, orderId)
    expect(afterReplayReceipts.items.map((item) => item.acceptanceId)).toEqual([sent.acceptanceId])
    const afterReplayLabel = await readLabel(page, stockReaderActor, sent.labelId)
    expect(afterReplayLabel.labelVersion).toBe(label.labelVersion)
    expect(afterReplayLabel.balances).toEqual(label.balances)
    expect(afterReplayLabel.movementTimeline.entries.map((entry) => entry.movementId)).toEqual([sent.movementId])

    // 6. Approver and reader can open the same approved order but never get the receipt control or command.
    for (const actor of [approverActor, readerActor]) {
      const published = await capabilitiesOf(page, actor)
      expect(published).toContain('purchasing.orders.detail')
      expect(published).not.toContain(RECEIPT_CAPABILITY)

      await signInAs(page, actor)
      const receiptCalls: string[] = []
      const watch = (request: BrowserRequest) => {
        const path = new URL(request.url()).pathname
        if (path.includes('/goods-receipt')) receiptCalls.push(`${request.method()} ${path}`)
      }
      await openOrderFromList(page, orderId, () => page.on('request', watch))
      await expect(page.getByText('Onaylandı', { exact: true })).toBeVisible()
      await expect(lineRow(page).getByRole('cell').nth(7)).toHaveText(REMAINING)
      await expect(page.getByRole('region', { name: 'Mal kabulü', exact: true })).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Mal kabulünü kaydet', exact: true })).toHaveCount(0)
      page.off('request', watch)
      expect(receiptCalls).toEqual([])

      const denied = await page.request.post(`${env.apiBaseUrl}${recordPath}`, {
        headers: {
          ...apiHeaders(actor), 'Idempotency-Key': `browser-suite:pur01:receipt-denied:${actor}`,
          'Content-Type': 'application/json', 'X-Correlation-Id': `browser-suite-pur01-receipt-denied-${actor}`,
        },
        data: {
          ...sent, expectedPurchaseOrderVersion: receiptVersion, acceptanceId: crypto.randomUUID(),
          lotId: crypto.randomUUID(), labelId: crypto.randomUUID(), movementId: crypto.randomUUID(),
          labelCode: `${LABEL_CODE}-${actor.toUpperCase()}`, acceptedQuantity: '1', measuredQuantityKg: '1',
        },
      })
      expect(denied.status()).toBe(403)
      const deniedOptions = await page.request.get(`${env.apiBaseUrl}${optionsPath}`, { headers: apiHeaders(actor) })
      expect(deniedOptions.status()).toBe(403)
    }

    // The receiver's grant does not cross the company fence.
    const elsewhere = await page.request.post(`${env.apiBaseUrl}${recordPath}`, {
      headers: {
        ...apiHeaders(receiverActor, otherCompanyId), 'Idempotency-Key': 'browser-suite:pur01:receipt-other-company',
        'Content-Type': 'application/json', 'X-Correlation-Id': 'browser-suite-pur01-receipt-other-company',
      },
      data: {
        ...sent, expectedPurchaseOrderVersion: receiptVersion, acceptanceId: crypto.randomUUID(),
        lotId: crypto.randomUUID(), labelId: crypto.randomUUID(), movementId: crypto.randomUUID(),
        labelCode: `${LABEL_CODE}-ELSEWHERE`, acceptedQuantity: '1', measuredQuantityKg: '1',
      },
    })
    expect([403, 404]).toContain(elsewhere.status())
    const elsewhereReceipts = await page.request.get(`${env.apiBaseUrl}${receiptsPath}`, {
      headers: apiHeaders(receiverActor, otherCompanyId),
    })
    expect([403, 404]).toContain(elsewhereReceipts.status())
    expect(await elsewhereReceipts.text()).not.toContain(LABEL_CODE)
    const elsewhereComparison = await page.request.get(`${env.apiBaseUrl}${comparisonPath(sent.acceptanceId)}`, {
      headers: apiHeaders(receiverActor, otherCompanyId),
    })
    expect([403, 404]).toContain(elsewhereComparison.status())
    const elsewhereComparisonBody = await elsewhereComparison.text()
    expect(elsewhereComparisonBody).not.toContain(LABEL_CODE)
    expect(elsewhereComparisonBody).not.toContain(MEASURED_KG)

    const deniedComparison = await page.request.get(`${env.apiBaseUrl}${comparisonPath(sent.acceptanceId)}`, {
      headers: apiHeaders(stockReaderActor),
    })
    expect(deniedComparison.status()).toBe(403)
    const deniedComparisonBody = await deniedComparison.text()
    expect(deniedComparisonBody).not.toContain(LABEL_CODE)
    expect(deniedComparisonBody).not.toContain(MEASURED_KG)

    // Nothing the denied or foreign attempts sent left any row: still exactly one receipt and one label code.
    expect(await readReceiptEffects(pool, identity)).toEqual(ONCE)
    const codes = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .query(`SELECT COUNT(*) AS labels FROM inventory.Labels WHERE CompanyId=@companyId AND Code LIKE N'SYNPUR01-LABEL-%'`)
    expect(codes.recordset[0]).toEqual({ labels: 1 })
    const finalOrder = await readOrder(page, readerActor, orderId)
    expect(finalOrder.order.version).toBe(receiptVersion)
    expect(finalOrder.lines[0]).toMatchObject({ accepted: ACCEPTED, remaining: REMAINING })
  } finally {
    await pool.close()
  }
})
