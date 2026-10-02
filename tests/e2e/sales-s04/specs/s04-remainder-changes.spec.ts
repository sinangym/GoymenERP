/**
 * Sales s04 - remainder changes against the REAL disposable stack.
 *
 * This file creates one public draft/line, submits and approves it, records customer acceptance for that exact
 * approved revision, then proves both remainder decisions on the same line. A request never mutates quantity;
 * rejection keeps the order unchanged; approval applies the owner-backed floor; replay is one effect. The final
 * browser close/reopen reads the same server record. Nothing is mocked or intercepted. The test creates, issues, loads and
 * definitely ships a real Logistics instruction bound to this exact draft/line, so the remainder floor contains a real
 * partial shipment. It does not record a delivery.
 *
 * The suite creates no actor or grant. The existing preparer owns `sales.orders.quantity.write`, the existing
 * approver owns `sales.orders.approve`, and one existing disposable actor must be published with the already
 * accepted narrow `sales.orders.customer-acceptance.record` capability. If Central has not supplied that exact
 * fixture grant, this source fails explicitly before a remainder mutation and must not be counted as acceptance.
 */

import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { apiBaseUrl, testedCommit, seed } = env
const {
  preparerActor,
  approverActor,
  readerActor,
  supportCreatorActor,
  ordinaryCreatorActor,
  plannerActor,
  companyId,
  realPartyId,
  realCompanyAccountId,
  productId,
  productVersion,
  productUnitId,
  allocationLabelId,
  allocationLabelVersion,
} = seed

const DRAFTS_PATH = '/api/v1/sales/drafts'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const QUANTITY_WRITE = 'sales.orders.quantity.write'
const CUSTOMER_ACCEPTANCE_RECORD = 'sales.orders.customer-acceptance.record'
const LOGISTICS_INSTRUCTION_CREATE = 'logistics.loading-instructions.create'
const LOGISTICS_LOAD_RECORD = 'logistics.loads.record'
const LOGISTICS_SHIPMENT_RECORD = 'logistics.shipments.record'
const INVENTORY_ALLOCATION_WRITE = 'inventory.physical-allocation.write'
const LOGISTICS_AGGREGATE_LIMIT_EXCEEDED = 'logistics_order_line_aggregate_limit_exceeded'
const BUSINESS_DATE = '2026-09-18'
const RUN = testedCommit.slice(0, 12)
const ORDERED_QUANTITY = '5'
const REVISED_QUANTITY = '3'

interface DraftReading {
  readonly draft: {
    readonly id: string
    readonly state: string
    readonly approvalState: string
    readonly version: string
    readonly revision: string
  }
  readonly customerAcceptance: { readonly isCurrentRevisionAccepted: boolean }
  readonly lines: readonly { readonly lineId: string; readonly lineNo: number; readonly quantity: string }[]
}

interface DraftCommandResponse {
  readonly detail: DraftReading
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

interface RemainderLine {
  readonly lineId: string
  readonly lineNo: number
  readonly quantity: string
  readonly remainder: 'Open' | 'Revised' | 'Cancelled'
  readonly loaded: string | null
  readonly shipped: string | null
  readonly delivered: string | null
  readonly minimumQuantity: string | null
}

interface RemainderChange {
  readonly changeNo: number
  readonly lineId: string
  readonly kind: 'Cancel' | 'Revise'
  readonly state: 'Pending' | 'Approved' | 'Rejected'
  readonly orderedQuantity: string
  readonly requestedQuantity: string | null
  readonly resultQuantity: string | null
  readonly loadedAtRequest: string
  readonly loadedAtDecision: string | null
}

interface RemainderReading {
  readonly draftVersion: string
  readonly floorStatus: 'Readable' | 'Unreadable'
  readonly lines: readonly RemainderLine[]
  readonly items: readonly RemainderChange[]
}

interface LoadingInstructionResponse {
  readonly detail: {
    readonly instruction: { readonly id: string; readonly status: string; readonly version: string }
    readonly lines: readonly {
      readonly lineId: string
      readonly sourceOrderId: string
      readonly sourceOrderLineId: string
      readonly instructed: string
      readonly loaded: string
    }[]
    readonly shares: readonly {
      readonly shareId: string
      readonly lineId: string
      readonly kind: string
      readonly quantity: string
      readonly sourceShipmentId: string | null
    }[]
  }
  readonly isReplay: boolean
}

function readHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

function commandHeaders(actor: string, key: string): Record<string, string> {
  return { ...readHeaders(actor), 'Idempotency-Key': key }
}

async function capabilitiesOf(request: APIRequestContext, actor: string): Promise<readonly string[]> {
  const response = await request.get(`${apiBaseUrl}${CAPABILITIES_PATH}`, { headers: readHeaders(actor) })
  expect(response.status(), `capabilities are published for ${actor}`).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(snapshot.capabilities) ? snapshot.capabilities as string[] : []
}

async function findActorWith(request: APIRequestContext, ...capabilities: readonly string[]): Promise<string | undefined> {
  const actors = [preparerActor, approverActor, readerActor, supportCreatorActor, ordinaryCreatorActor, plannerActor,
    's04-logistics-preparer', 's04-logistics-loader']
  for (const actor of actors) {
    const published = await capabilitiesOf(request, actor)
    if (capabilities.every((capability) => published.includes(capability))) return actor
  }
  return undefined
}

async function readDraft(request: APIRequestContext, draftId: string, actor: string): Promise<DraftReading> {
  const response = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}`, { headers: readHeaders(actor) })
  expect(response.status(), `the real detail route answers ${actor}`).toBe(200)
  const detail = (await response.json()) as DraftReading
  expect(detail.draft.id).toBe(draftId)
  return detail
}

async function readRemainders(request: APIRequestContext, draftId: string, actor: string): Promise<RemainderReading> {
  const response = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}/remainder-changes`, { headers: readHeaders(actor) })
  expect(response.status(), `the real remainder read answers ${actor}`).toBe(200)
  return response.json() as Promise<RemainderReading>
}

async function openDetail(page: Page, draftId: string, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
  await page.goto(`/satis/siparisler/${draftId}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible({ timeout: 30_000 })
}

test('S04-RM-01 one accepted order rejects then approves a remainder change; close/reopen and replay prove one exact effect', async ({ page, request }) => {
  test.setTimeout(240_000)

  expect(await capabilitiesOf(request, preparerActor)).toEqual(expect.arrayContaining(['sales.drafts.detail', QUANTITY_WRITE]))
  expect(await capabilitiesOf(request, approverActor)).toEqual(expect.arrayContaining(['sales.drafts.detail', 'sales.orders.approve']))

  const acceptanceActor = await findActorWith(request, CUSTOMER_ACCEPTANCE_RECORD)
  if (acceptanceActor === undefined) {
    throw new Error(
      `BLOCKED: the s04 disposable fixture publishes no actor with ${CUSTOMER_ACCEPTANCE_RECORD}; `
      + 'Cari/Central must grant that exact accepted narrow permission to one existing disposable actor, not a role.',
    )
  }
  const logisticsPreparerActor = await findActorWith(request, LOGISTICS_INSTRUCTION_CREATE)
  if (logisticsPreparerActor === undefined) {
    throw new Error(
      `BLOCKED: the s04 disposable fixture publishes no actor with ${LOGISTICS_INSTRUCTION_CREATE}; `
      + 'Cari/Central must grant that exact existing Logistics permission to one existing disposable actor, not a role.',
    )
  }
  const logisticsLoaderActor = await findActorWith(request, LOGISTICS_LOAD_RECORD)
  if (logisticsLoaderActor === undefined) {
    throw new Error(
      `BLOCKED: the s04 disposable fixture publishes no actor with ${LOGISTICS_LOAD_RECORD}; `
      + 'Cari/Central must grant that exact existing Logistics permission to one existing disposable actor, not a role.',
    )
  }
  const logisticsShipmentActor = await findActorWith(request, LOGISTICS_SHIPMENT_RECORD, INVENTORY_ALLOCATION_WRITE)
  if (logisticsShipmentActor === undefined) {
    throw new Error(
      `BLOCKED: the s04 disposable fixture publishes no actor with ${LOGISTICS_SHIPMENT_RECORD} and ${INVENTORY_ALLOCATION_WRITE}; `
      + 'Cari/Central must bind those exact existing capabilities to one existing disposable actor, not a role.',
    )
  }

  const currencies = await request.get(`${apiBaseUrl}${CURRENCIES_PATH}?effectiveOn=${BUSINESS_DATE}`, {
    headers: readHeaders(preparerActor),
  })
  expect(currencies.status()).toBe(200)
  const lira = ((await currencies.json()) as {
    readonly currencies: readonly { readonly currencyId: string; readonly code: string }[]
  }).currencies.find((currency) => currency.code === 'TRY')
  expect(lira, 'the approved TRY profile is listed for the seed business date').toBeTruthy()

  const lineId = randomUUID()
  const created = await request.post(`${apiBaseUrl}${DRAFTS_PATH}`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-rm-create-${RUN}`),
    data: {
      kind: 'Order', partyId: realPartyId, companyAccountId: realCompanyAccountId, businessDate: BUSINESS_DATE,
      currencyId: lira!.currencyId,
      lines: [{
        lineId, productId, expectedProductVersion: productVersion, orderUnitId: productUnitId,
        quantity: ORDERED_QUANTITY, unitPrice: null, priceUnitId: null, requestedDeliveryDate: null, note: null,
        requestedDeliverySlices: null, discountPercent: null,
      }],
      requestedDeliveryDate: null, note: null, deliveryAddress: null, contact: null, responsible: null,
      paymentTerms: null, sourceStrategy: null, package: null,
    },
  })
  expect(created.status(), 'the real create stores the order').toBe(201)
  const draftId = ((await created.json()) as DraftCommandResponse).detail.draft.id

  let current = await readDraft(request, draftId, preparerActor)
  const submitted = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}/submission`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-rm-submit-${RUN}`),
    data: { expectedVersion: current.draft.version },
  })
  expect(submitted.status()).toBe(200)

  current = await readDraft(request, draftId, approverActor)
  const approved = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}/approval`, {
    headers: commandHeaders(approverActor, `SENTETIK-s04-rm-internal-approve-${RUN}`),
    data: { expectedVersion: current.draft.version },
  })
  expect(approved.status()).toBe(200)

  current = await readDraft(request, draftId, acceptanceActor)
  expect([current.draft.approvalState, current.lines[0]?.quantity, current.customerAcceptance.isCurrentRevisionAccepted])
    .toEqual(['Approved', ORDERED_QUANTITY, false])
  await openDetail(page, draftId, acceptanceActor)
  const acceptanceSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/customer-acceptance`)
  const acceptanceAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/customer-acceptance`)
  await page.getByTestId('sales-approval-commands')
    .getByRole('button', { name: 'Müşteri kabulünü kaydet' }).click()
  const [acceptanceBrowserRequest, acceptanceBrowserResponse] = await Promise.all([acceptanceSent, acceptanceAnswered])
  expect(acceptanceBrowserResponse.status()).toBe(200)
  expect(acceptanceBrowserResponse.headers()['idempotency-replayed']).toBeUndefined()
  expect(acceptanceBrowserRequest.headers()['idempotency-key']).toBeTruthy()
  expect(acceptanceBrowserRequest.postDataJSON()).toEqual({ expectedVersion: current.draft.version })
  const acceptedCommand = (await acceptanceBrowserResponse.json()) as DraftCommandResponse
  expect(acceptedCommand).toMatchObject({
    isReplay: false,
    detail: {
      draft: { id: draftId, approvalState: 'Approved' },
      customerAcceptance: { isCurrentRevisionAccepted: true },
    },
  })
  await expect(page.getByTestId('sales-customer-acceptance-state')).toContainText('Güncel revizyon kabul edildi')

  current = await readDraft(request, draftId, preparerActor)
  expect(current.customerAcceptance.isCurrentRevisionAccepted).toBe(true)

  // Existing public Logistics contracts bind the real load to this exact Sales draft/line. The API resolves product and
  // customer from Sales; the client sends neither a fabricated product snapshot nor a fixture-only mapping.
  const logisticsLineId = randomUUID()
  const instructionCreated = await request.post(`${apiBaseUrl}/api/v1/logistics/loading-instructions`, {
    headers: commandHeaders(logisticsPreparerActor, `SENTETIK-s04-rm-log-create-${RUN}`),
    data: {
      customerPartyId: realPartyId,
      companyAccountId: realCompanyAccountId,
      effectiveOn: BUSINESS_DATE,
      lines: [{
        lineId: logisticsLineId,
        sourceOrderId: draftId,
        sourceOrderLineId: lineId,
        unitId: productUnitId,
        quantity: ORDERED_QUANTITY,
      }],
    },
  })
  expect(instructionCreated.status(), 'the public Logistics create binds the same Sales line').toBe(201)
  const createdInstruction = (await instructionCreated.json()) as LoadingInstructionResponse
  expect(createdInstruction.detail.lines[0]).toMatchObject({
    lineId: logisticsLineId, sourceOrderId: draftId, sourceOrderLineId: lineId,
    instructed: ORDERED_QUANTITY, loaded: '0',
  })
  const instructionId = createdInstruction.detail.instruction.id

  // 7B: without a configured tolerance the exact current order quantity is the ceiling across every instruction.
  // A second instruction for this same Sales line would make the aggregate 6 > 5, so the real store refuses it
  // atomically. The same key replays the terminal refusal and the first instruction remains usable below.
  const overCapKey = `SENTETIK-s04-rm-log-over-cap-${RUN}`
  const overCapRequest = {
    customerPartyId: realPartyId,
    companyAccountId: realCompanyAccountId,
    effectiveOn: BUSINESS_DATE,
    lines: [{
      lineId: randomUUID(),
      sourceOrderId: draftId,
      sourceOrderLineId: lineId,
      unitId: productUnitId,
      quantity: '1',
    }],
  }
  const overCap = await request.post(`${apiBaseUrl}/api/v1/logistics/loading-instructions`, {
    headers: commandHeaders(logisticsPreparerActor, overCapKey),
    data: overCapRequest,
  })
  expect(overCap.status()).toBe(409)
  expect((await overCap.json()) as { readonly code?: string }).toMatchObject({
    code: LOGISTICS_AGGREGATE_LIMIT_EXCEEDED,
  })

  const overCapReplay = await request.post(`${apiBaseUrl}/api/v1/logistics/loading-instructions`, {
    headers: commandHeaders(logisticsPreparerActor, overCapKey),
    data: overCapRequest,
  })
  expect(overCapReplay.status()).toBe(409)
  expect(overCapReplay.headers()['idempotency-replayed']).toBe('true')
  expect((await overCapReplay.json()) as { readonly code?: string }).toMatchObject({
    code: LOGISTICS_AGGREGATE_LIMIT_EXCEEDED,
  })

  const instructionIssued = await request.post(`${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/issue`, {
    headers: commandHeaders(logisticsPreparerActor, `SENTETIK-s04-rm-log-issue-${RUN}`),
    data: { expectedVersion: createdInstruction.detail.instruction.version },
  })
  expect(instructionIssued.status()).toBe(200)
  const issuedInstruction = (await instructionIssued.json()) as LoadingInstructionResponse
  expect(issuedInstruction.detail.instruction.status).toBe('Issued')

  const loadRecorded = await request.post(
    `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/lines/${logisticsLineId}/loads`,
    {
      headers: commandHeaders(logisticsLoaderActor, `SENTETIK-s04-rm-log-load-${RUN}`),
      data: {
        expectedVersion: issuedInstruction.detail.instruction.version,
        loadId: randomUUID(),
        quantity: '2',
        measurement: 'Measured',
      },
    },
  )
  expect(loadRecorded.status()).toBe(200)
  const loadedInstruction = (await loadRecorded.json()) as LoadingInstructionResponse
  expect(loadedInstruction.detail.lines[0]).toMatchObject({
    lineId: logisticsLineId, sourceOrderId: draftId, sourceOrderLineId: lineId, loaded: '2',
  })

  const allocationId = randomUUID()
  const allocationCreated = await request.post(`${apiBaseUrl}/api/v1/inventory/physical-allocations`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-rm-allocation-${RUN}`),
    data: {
      allocationId,
      salesOrderId: draftId,
      salesOrderLineId: lineId,
      labelId: allocationLabelId,
      expectedLabelVersion: allocationLabelVersion,
      quantityKg: '1',
      reason: 'SENTETIK browser-suite:s04 aynı sipariş satırı kesin sevkiyat tahsisi',
      reservedUntilUtc: null,
    },
  })
  expect(allocationCreated.status(), 'the real Inventory allocation binds the same Sales line').toBe(201)
  expect(await allocationCreated.json()).toMatchObject({ allocationId, status: 'Active', version: '1', remainingKg: '1' })

  const shipmentId = randomUUID()
  const shipmentRecorded = await request.post(
    `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/lines/${logisticsLineId}/shipments`,
    {
      headers: commandHeaders(logisticsShipmentActor, `SENTETIK-s04-rm-log-shipment-${RUN}`),
      data: {
        expectedVersion: loadedInstruction.detail.instruction.version,
        shipmentId,
        allocationId,
        expectedAllocationVersion: '1',
        expectedLabelVersion: allocationLabelVersion,
        movementId: randomUUID(),
        quantity: '1',
        occurredAtUtc: '2026-09-18T12:00:00Z',
      },
    },
  )
  expect(shipmentRecorded.status(), 'the public Logistics shipment consumes the same-line allocation').toBe(200)
  const shippedInstruction = (await shipmentRecorded.json()) as LoadingInstructionResponse
  expect(shippedInstruction.detail.shares.find((share) => share.shareId === shipmentId)).toMatchObject({
    lineId: logisticsLineId, kind: 'Shipped', quantity: '1', sourceShipmentId: null,
  })

  const nonZeroFloor = await readRemainders(request, draftId, readerActor)
  expect(nonZeroFloor.floorStatus).toBe('Readable')
  expect(nonZeroFloor.lines.find((line) => line.lineId === lineId)).toMatchObject({
    quantity: ORDERED_QUANTITY, loaded: '2', shipped: '1', delivered: '0', minimumQuantity: '2',
  })

  await openDetail(page, draftId, preparerActor)
  const firstRequestPanel = page.getByTestId('sales-remainder-request')
  await firstRequestPanel.getByLabel('İşlem').selectOption('Revise')
  await firstRequestPanel.getByLabel('Yeni sipariş miktarı').fill(REVISED_QUANTITY)
  await firstRequestPanel.getByLabel('Gerekçe').fill('SENTETIK browser-suite:s04 müşteri kalan miktarı düşürmek istedi')
  const firstRequestSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes`)
  const firstRequestAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes`)
  await firstRequestPanel.getByRole('button', { name: 'Onaya gönder' }).click()
  const [firstBrowserRequest, firstBrowserResponse] = await Promise.all([firstRequestSent, firstRequestAnswered])
  expect(firstBrowserResponse.status()).toBe(200)
  expect(firstBrowserRequest.postDataJSON()).toMatchObject({
    expectedVersion: current.draft.version, lineId, kind: 'Revise', quantity: REVISED_QUANTITY,
  })

  let fresh = await readDraft(request, draftId, readerActor)
  let remainder = await readRemainders(request, draftId, readerActor)
  expect(fresh.lines.find((line) => line.lineId === lineId)?.quantity, 'a request does not mutate quantity').toBe(ORDERED_QUANTITY)
  expect(remainder.items).toHaveLength(1)
  expect(remainder.items[0]).toMatchObject({ changeNo: 1, lineId, state: 'Pending', requestedQuantity: REVISED_QUANTITY })

  await openDetail(page, draftId, approverActor)
  const rejectedCard = page.getByTestId('sales-remainder-change-1')
  await rejectedCard.getByLabel('Ret nedeni').fill('SENTETIK browser-suite:s04 mevcut miktar korunsun')
  const rejectionSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes/1/rejection`)
  const rejectionAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes/1/rejection`)
  await rejectedCard.getByRole('button', { name: 'Reddet' }).click()
  const [rejectionBrowserRequest, rejectionBrowserResponse] = await Promise.all([rejectionSent, rejectionAnswered])
  expect(rejectionBrowserResponse.status()).toBe(200)
  expect(rejectionBrowserRequest.postDataJSON()).toMatchObject({
    expectedVersion: remainder.draftVersion, reason: 'SENTETIK browser-suite:s04 mevcut miktar korunsun',
  })

  fresh = await readDraft(request, draftId, readerActor)
  remainder = await readRemainders(request, draftId, readerActor)
  expect(fresh.lines.find((line) => line.lineId === lineId)?.quantity).toBe(ORDERED_QUANTITY)
  expect(remainder.items).toHaveLength(1)
  expect(remainder.items[0]).toMatchObject({ changeNo: 1, state: 'Rejected', resultQuantity: null })

  await openDetail(page, draftId, preparerActor)
  const secondRequestPanel = page.getByTestId('sales-remainder-request')
  await secondRequestPanel.getByLabel('İşlem').selectOption('Revise')
  await secondRequestPanel.getByLabel('Yeni sipariş miktarı').fill(REVISED_QUANTITY)
  await secondRequestPanel.getByLabel('Gerekçe').fill('SENTETIK browser-suite:s04 teslim edilmeyen kalan revize edilsin')
  const secondRequestSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes`)
  const secondRequestAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes`)
  await secondRequestPanel.getByRole('button', { name: 'Onaya gönder' }).click()
  const [secondBrowserRequest, secondBrowserResponse] = await Promise.all([secondRequestSent, secondRequestAnswered])
  expect(secondBrowserResponse.status()).toBe(200)
  expect(secondBrowserRequest.postDataJSON()).toMatchObject({
    expectedVersion: remainder.draftVersion, lineId, kind: 'Revise', quantity: REVISED_QUANTITY,
  })

  remainder = await readRemainders(request, draftId, approverActor)
  expect(remainder.items).toHaveLength(2)
  expect(remainder.items[1]).toMatchObject({ changeNo: 2, state: 'Pending', orderedQuantity: ORDERED_QUANTITY })
  await openDetail(page, draftId, approverActor)
  const approvedCard = page.getByTestId('sales-remainder-change-2')
  const approvalSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes/2/approval`)
  const approvalAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes/2/approval`)
  await approvedCard.getByRole('button', { name: 'Onayla' }).click()
  const [approvalBrowserRequest, approvalBrowserResponse] = await Promise.all([approvalSent, approvalAnswered])
  expect(approvalBrowserResponse.status()).toBe(200)
  expect(approvalBrowserResponse.headers()['idempotency-replayed']).toBeUndefined()
  const appliedApproval = (await approvalBrowserResponse.json()) as DraftCommandResponse
  expect(appliedApproval).toMatchObject({
    isReplay: false,
    detail: { draft: { id: draftId }, lines: [{ lineId, quantity: REVISED_QUANTITY }] },
  })
  const approvalKey = approvalBrowserRequest.headers()['idempotency-key']
  if (!approvalKey) throw new Error('The browser remainder approval did not carry its Idempotency-Key.')
  const approvalBody = approvalBrowserRequest.postDataJSON() as { readonly expectedVersion: string; readonly reason: null }
  expect(approvalBody).toEqual({ expectedVersion: remainder.draftVersion, reason: null })

  const replay = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}/remainder-changes/2/approval`, {
    headers: commandHeaders(approverActor, approvalKey), data: approvalBody,
  })
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  expect(replay.headers()['idempotency-original-correlation-id']).toBe(appliedApproval.originalCorrelationId)
  const replayedApproval = (await replay.json()) as DraftCommandResponse
  expect(replayedApproval).toMatchObject({
    isReplay: true,
    originalCorrelationId: appliedApproval.originalCorrelationId,
    detail: { draft: { id: draftId }, lines: [{ lineId, quantity: REVISED_QUANTITY }] },
  })

  fresh = await readDraft(request, draftId, readerActor)
  remainder = await readRemainders(request, draftId, readerActor)
  const exactLine = remainder.lines.find((line) => line.lineId === lineId)
  expect(fresh.lines.find((line) => line.lineId === lineId)?.quantity).toBe(REVISED_QUANTITY)
  expect(remainder.floorStatus).toBe('Readable')
  expect(exactLine).toMatchObject({
    quantity: REVISED_QUANTITY, remainder: 'Revised', loaded: '2', shipped: '1', delivered: '0', minimumQuantity: '2',
  })
  expect(remainder.items).toHaveLength(2)
  expect(remainder.items[1]).toMatchObject({
    changeNo: 2, state: 'Approved', requestedQuantity: REVISED_QUANTITY,
    resultQuantity: REVISED_QUANTITY, loadedAtRequest: '2', shippedAtRequest: '1', deliveredAtRequest: '0', loadedAtDecision: '2',
  })

  await openDetail(page, draftId, readerActor)
  await expect(page.getByTestId('sales-remainder-changes')).toBeVisible()
  await expect(page.getByTestId('sales-remainder-change-1')).toContainText('Rejected')
  await expect(page.getByTestId('sales-remainder-change-2')).toContainText('Approved')
  await page.goto('/satis/siparisler')
  await expect(page).toHaveURL(/\/satis\/siparisler$/)
  await openDetail(page, draftId, readerActor)
  await expect(page.getByTestId('sales-remainder-changes')).toContainText('Alt sınır: 2')
  expect((await readRemainders(request, draftId, readerActor)).items).toHaveLength(2)
})
