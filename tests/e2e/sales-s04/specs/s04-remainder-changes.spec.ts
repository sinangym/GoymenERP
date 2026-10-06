/**
 * Sales s04 - remainder changes against the REAL disposable stack.
 *
 * This file creates one public draft/line, submits and approves it, records customer acceptance for that exact
 * approved revision, then proves both remainder decisions on the same line. A request never mutates quantity;
 * rejection keeps the order unchanged; approval applies the owner-backed floor; replay is one effect. The final
 * browser close/reopen reads the same server record. Nothing is mocked or intercepted. The test creates, issues, loads and
 * definitely ships a real Logistics instruction bound to this exact draft/line, so the remainder floor contains a real
 * partial delivery from the same shipment and preserves every realized owner record across the remainder decisions.
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
} = seed

const DRAFTS_PATH = '/api/v1/sales/drafts'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const QUANTITY_WRITE = 'sales.orders.quantity.write'
const CUSTOMER_ACCEPTANCE_RECORD = 'sales.orders.customer-acceptance.record'
const LOGISTICS_CREATE_CAPABILITY = 'logistics.instructions.create'
const LOGISTICS_DETAIL_CAPABILITY = 'logistics.instructions.detail'
const LOGISTICS_SHIPMENT_CAPABILITY = 'logistics.shipments.record'
const LOGISTICS_PREPARER_ACTOR = 's04-logistics-preparer'
const LOGISTICS_LOADER_ACTOR = 's04-logistics-loader'
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
  readonly minimum: string | null
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
      readonly shipped: string
      readonly delivered: string
    }[]
    readonly shares: readonly {
      readonly shareId: string
      readonly lineId: string
      readonly kind: string
      readonly quantity: string
      readonly sourceShipmentId: string | null
    }[]
    readonly dispatchNotes: readonly {
      readonly dispatchNoteId: string
      readonly status: string
      readonly number: string | null
      readonly lines: readonly {
        readonly shipmentId: string
        readonly quantity: string
      }[]
    }[]
  }
  readonly isReplay: boolean
}

interface InventoryLabelDetail {
  readonly labelVersion: string
  readonly identity: { readonly labelId: string }
}

function readHeaders(actor: string): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId }
}

function commandHeaders(actor: string, key: string): Record<string, string> {
  return { ...readHeaders(actor), 'Idempotency-Key': key }
}

async function readInstruction(request: APIRequestContext, instructionId: string, actor: string): Promise<LoadingInstructionResponse['detail']> {
  const response = await request.get(`${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}`, {
    headers: readHeaders(actor),
  })
  expect(response.status(), `the real Logistics detail answers ${actor}`).toBe(200)
  const detail = (await response.json()) as LoadingInstructionResponse['detail']
  expect(detail.instruction.id).toBe(instructionId)
  return detail
}

async function readLabelVersion(request: APIRequestContext, actor: string): Promise<string> {
  const response = await request.get(`${apiBaseUrl}/api/v1/inventory/labels/${allocationLabelId}/detail`, {
    headers: readHeaders(actor),
  })
  expect(response.status(), `the real Inventory label detail answers ${actor}`).toBe(200)
  const detail = (await response.json()) as InventoryLabelDetail
  expect(detail.identity.labelId.toLowerCase(), 'the fresh Inventory detail is the reserved allocation label')
    .toBe(allocationLabelId.toLowerCase())
  expect(detail.labelVersion, 'the fresh Inventory label version is a positive decimal text').toMatch(/^[1-9][0-9]*$/)
  return detail.labelVersion
}

async function capabilitiesOf(request: APIRequestContext, actor: string): Promise<readonly string[]> {
  const response = await request.get(`${apiBaseUrl}${CAPABILITIES_PATH}`, { headers: readHeaders(actor) })
  expect(response.status(), `capabilities are published for ${actor}`).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  return Array.isArray(snapshot.capabilities) ? snapshot.capabilities as string[] : []
}

async function findActorWith(request: APIRequestContext, ...capabilities: readonly string[]): Promise<string | undefined> {
  const actors = [preparerActor, approverActor, readerActor, supportCreatorActor, ordinaryCreatorActor, plannerActor,
    LOGISTICS_PREPARER_ACTOR, LOGISTICS_LOADER_ACTOR]
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

test('S04-RM-01 one accepted order revises then cancels only its remainder while its approved dispatch note stays immutable', async ({ page, request }) => {
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
  const logisticsPreparerActor = LOGISTICS_PREPARER_ACTOR
  expect(await capabilitiesOf(request, logisticsPreparerActor)).toEqual(expect.arrayContaining([
    LOGISTICS_CREATE_CAPABILITY, LOGISTICS_DETAIL_CAPABILITY,
  ]))
  const logisticsLoaderActor = LOGISTICS_LOADER_ACTOR
  expect(await capabilitiesOf(request, logisticsLoaderActor)).toEqual(expect.arrayContaining([
    LOGISTICS_DETAIL_CAPABILITY, LOGISTICS_SHIPMENT_CAPABILITY,
  ]))
  // The shipment shell capability is itself the accepted three-permission fence (instruction read + shipment record +
  // Inventory physical-allocation write). Load and dispatch-note commands have no shell capability declaration; their strict
  // public POST status/body checks below prove those backend operation permissions without fabricating capability names.
  const logisticsShipmentActor = logisticsLoaderActor
  const logisticsDispatchActor = logisticsLoaderActor

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
  const labelVersionBeforeAllocation = await readLabelVersion(request, preparerActor)
  const allocationCreated = await request.post(`${apiBaseUrl}/api/v1/inventory/physical-allocations`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-rm-allocation-${RUN}`),
    data: {
      allocationId,
      salesOrderId: draftId,
      salesOrderLineId: lineId,
      labelId: allocationLabelId,
      expectedLabelVersion: labelVersionBeforeAllocation,
      quantityKg: '1',
      reason: 'SENTETIK browser-suite:s04 aynı sipariş satırı kesin sevkiyat tahsisi',
      reservedUntilUtc: null,
    },
  })
  expect(allocationCreated.status(), 'the real Inventory allocation binds the same Sales line').toBe(201)
  expect(await allocationCreated.json()).toMatchObject({ allocationId, status: 'Active', version: '1', remainingKg: '1' })

  const labelVersionBeforeShipment = await readLabelVersion(request, preparerActor)
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
        expectedLabelVersion: labelVersionBeforeShipment,
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

  const deliveryId = randomUUID()
  const deliveredQuantity = '0.5'
  const deliveryRecorded = await request.post(
    `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/lines/${logisticsLineId}/deliveries`,
    {
      headers: commandHeaders(logisticsShipmentActor, `SENTETIK-s04-rm-log-delivery-${RUN}`),
      data: {
        expectedVersion: shippedInstruction.detail.instruction.version,
        deliveryId,
        sourceShipmentId: shipmentId,
        quantity: deliveredQuantity,
        occurredAtUtc: '2026-09-18T12:30:00Z',
      },
    },
  )
  expect(deliveryRecorded.status(), 'the public Logistics delivery continues the same shipment').toBe(200)
  const deliveredInstruction = (await deliveryRecorded.json()) as LoadingInstructionResponse
  expect(deliveredInstruction.detail.shares.find((share) => share.shareId === deliveryId)).toMatchObject({
    lineId: logisticsLineId, kind: 'Delivered', quantity: deliveredQuantity, sourceShipmentId: shipmentId,
  })

  const allocationStatePath = `${apiBaseUrl}/api/v1/inventory/physical-allocations?salesOrderId=${draftId}&salesOrderLineId=${lineId}&limit=100`
  const allocationAfterDeliveryResponse = await request.get(allocationStatePath, {
    headers: readHeaders(logisticsShipmentActor),
  })
  expect(allocationAfterDeliveryResponse.status()).toBe(200)
  const allocationAfterDelivery = await allocationAfterDeliveryResponse.json() as {
    readonly items: readonly { readonly allocation: {
      readonly allocationId: string; readonly status: string; readonly version: string; readonly remainingKg: string
    } }[]
    readonly searchWindowFull: boolean
  }
  expect(allocationAfterDelivery.items.find((item) => item.allocation.allocationId === allocationId)?.allocation).toMatchObject({
    allocationId, status: 'Consumed', version: '2', remainingKg: '0',
  })
  const labelVersionAfterDelivery = await readLabelVersion(request, preparerActor)

  // The same definite shipment is documented by one real approved dispatch note. Sales remainder decisions must never
  // rewrite this owner record, its server number, shipment share or Logistics aggregate version.
  const dispatchNoteId = randomUUID()
  const dispatchPath = `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/dispatch-notes`
  const noteDrafted = await request.post(dispatchPath, {
    headers: commandHeaders(logisticsDispatchActor, `SENTETIK-s04-rm-note-create-${RUN}`),
    data: {
      expectedVersion: deliveredInstruction.detail.instruction.version,
      dispatchNoteId,
      shipmentIds: [shipmentId],
    },
  })
  expect(noteDrafted.status()).toBe(200)
  const draftedInstruction = (await noteDrafted.json()) as LoadingInstructionResponse
  expect(draftedInstruction.detail.dispatchNotes.find((note) => note.dispatchNoteId === dispatchNoteId)).toMatchObject({
    status: 'Draft', number: null, lines: [{ shipmentId, quantity: '1' }],
  })

  const noteApproved = await request.post(`${dispatchPath}/${dispatchNoteId}/approve`, {
    headers: commandHeaders(logisticsDispatchActor, `SENTETIK-s04-rm-note-approve-${RUN}`),
    data: { expectedVersion: draftedInstruction.detail.instruction.version },
  })
  expect(noteApproved.status()).toBe(200)
  const immutableLogistics = ((await noteApproved.json()) as LoadingInstructionResponse).detail
  const immutableNote = immutableLogistics.dispatchNotes.find((note) => note.dispatchNoteId === dispatchNoteId)
  expect(immutableNote).toMatchObject({ status: 'Approved', lines: [{ shipmentId, quantity: '1' }] })
  expect(immutableNote?.number).toMatch(/^IRS-\d{6}$/)
  const immutableShares = immutableLogistics.shares
    .map((share) => ({ shareId: share.shareId, lineId: share.lineId, kind: share.kind, quantity: share.quantity,
      sourceShipmentId: share.sourceShipmentId }))
    .sort((left, right) => left.shareId.localeCompare(right.shareId))
  const immutableLine = immutableLogistics.lines.find((line) => line.lineId === logisticsLineId)
  expect(immutableLine).toMatchObject({ loaded: '2', shipped: '1', delivered: deliveredQuantity })

  const nonZeroFloor = await readRemainders(request, draftId, readerActor)
  expect(nonZeroFloor.floorStatus).toBe('Readable')
  expect(nonZeroFloor.lines.find((line) => line.lineId === lineId)).toMatchObject({
    quantity: ORDERED_QUANTITY, loaded: '2', shipped: '1', delivered: deliveredQuantity, minimum: '2',
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
  expect(firstBrowserRequest.postDataJSON()).toEqual({
    expectedVersion: current.draft.version, lineId, kind: 'Revise', quantity: REVISED_QUANTITY,
    reason: 'SENTETIK browser-suite:s04 müşteri kalan miktarı düşürmek istedi',
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
  expect(rejectionBrowserRequest.postDataJSON()).toEqual({
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
  expect(secondBrowserRequest.postDataJSON()).toEqual({
    expectedVersion: remainder.draftVersion, lineId, kind: 'Revise', quantity: REVISED_QUANTITY,
    reason: 'SENTETIK browser-suite:s04 teslim edilmeyen kalan revize edilsin',
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

  const logisticsAfterRevision = await readInstruction(request, instructionId, logisticsDispatchActor)
  expect(logisticsAfterRevision.instruction.version).toBe(immutableLogistics.instruction.version)
  expect(logisticsAfterRevision.lines.find((line) => line.lineId === logisticsLineId)).toEqual(immutableLine)
  expect(logisticsAfterRevision.shares
    .map((share) => ({ shareId: share.shareId, lineId: share.lineId, kind: share.kind, quantity: share.quantity,
      sourceShipmentId: share.sourceShipmentId }))
    .sort((left, right) => left.shareId.localeCompare(right.shareId))).toEqual(immutableShares)
  expect(logisticsAfterRevision.dispatchNotes.find((note) => note.dispatchNoteId === dispatchNoteId)).toEqual(immutableNote)
  const allocationAfterRevisionResponse = await request.get(allocationStatePath, {
    headers: readHeaders(logisticsShipmentActor),
  })
  expect(allocationAfterRevisionResponse.status()).toBe(200)
  expect(await allocationAfterRevisionResponse.json()).toEqual(allocationAfterDelivery)
  expect(await readLabelVersion(request, preparerActor)).toBe(labelVersionAfterDelivery)

  fresh = await readDraft(request, draftId, readerActor)
  remainder = await readRemainders(request, draftId, readerActor)
  const exactLine = remainder.lines.find((line) => line.lineId === lineId)
  expect(fresh.lines.find((line) => line.lineId === lineId)?.quantity).toBe(REVISED_QUANTITY)
  expect(remainder.floorStatus).toBe('Readable')
  expect(exactLine).toMatchObject({
    quantity: REVISED_QUANTITY, remainder: 'Revised', loaded: '2', shipped: '1', delivered: deliveredQuantity, minimum: '2',
  })
  expect(remainder.items).toHaveLength(2)
  expect(remainder.items[1]).toMatchObject({
    changeNo: 2, state: 'Approved', requestedQuantity: REVISED_QUANTITY,
    resultQuantity: REVISED_QUANTITY, loadedAtRequest: '2', shippedAtRequest: '1', deliveredAtRequest: deliveredQuantity, loadedAtDecision: '2',
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

  // Latest direct user decision: actual shipment/note facts remain immutable; maker requests cancellation only for the
  // remaining quantity. Approval applies the existing loaded floor (2), not zero and not the shipped-only quantity (1).
  await openDetail(page, draftId, preparerActor)
  const cancelRequestPanel = page.getByTestId('sales-remainder-request')
  await cancelRequestPanel.getByLabel('İşlem').selectOption('Cancel')
  await cancelRequestPanel.getByLabel('Gerekçe').fill('SENTETIK browser-suite:s04 gerçekleşenler kalsın, yalnız kalan iptal edilsin')
  const cancelRequestSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes`)
  const cancelRequestAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes`)
  await cancelRequestPanel.getByRole('button', { name: 'Onaya gönder' }).click()
  const [cancelBrowserRequest, cancelBrowserResponse] = await Promise.all([cancelRequestSent, cancelRequestAnswered])
  expect(cancelBrowserResponse.status()).toBe(200)
  expect(cancelBrowserRequest.postDataJSON()).toEqual({
    expectedVersion: remainder.draftVersion, lineId, kind: 'Cancel', quantity: null,
    reason: 'SENTETIK browser-suite:s04 gerçekleşenler kalsın, yalnız kalan iptal edilsin',
  })

  fresh = await readDraft(request, draftId, readerActor)
  remainder = await readRemainders(request, draftId, approverActor)
  expect(fresh.lines.find((line) => line.lineId === lineId)?.quantity, 'a cancel request does not mutate quantity').toBe(REVISED_QUANTITY)
  expect(remainder.items).toHaveLength(3)
  expect(remainder.items[2]).toMatchObject({
    changeNo: 3, lineId, kind: 'Cancel', state: 'Pending', orderedQuantity: REVISED_QUANTITY,
    requestedQuantity: null, loadedAtRequest: '2', shippedAtRequest: '1', deliveredAtRequest: deliveredQuantity,
  })

  await openDetail(page, draftId, approverActor)
  const cancelCard = page.getByTestId('sales-remainder-change-3')
  const cancelApprovalSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes/3/approval`)
  const cancelApprovalAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/remainder-changes/3/approval`)
  await cancelCard.getByRole('button', { name: 'Onayla' }).click()
  const [cancelApprovalRequest, cancelApprovalResponse] = await Promise.all([cancelApprovalSent, cancelApprovalAnswered])
  expect(cancelApprovalResponse.status()).toBe(200)
  expect(cancelApprovalResponse.headers()['idempotency-replayed']).toBeUndefined()
  const cancelApplied = (await cancelApprovalResponse.json()) as DraftCommandResponse
  expect(cancelApplied).toMatchObject({
    isReplay: false,
    detail: { draft: { id: draftId }, lines: [{ lineId, quantity: '2' }] },
  })
  const cancelApprovalKey = cancelApprovalRequest.headers()['idempotency-key']
  if (!cancelApprovalKey) throw new Error('The browser remainder-cancel approval did not carry its Idempotency-Key.')
  const cancelApprovalBody = cancelApprovalRequest.postDataJSON() as { readonly expectedVersion: string; readonly reason: null }
  expect(cancelApprovalBody).toEqual({ expectedVersion: remainder.draftVersion, reason: null })

  const cancelReplay = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}/remainder-changes/3/approval`, {
    headers: commandHeaders(approverActor, cancelApprovalKey), data: cancelApprovalBody,
  })
  expect(cancelReplay.status()).toBe(200)
  expect(cancelReplay.headers()['idempotency-replayed']).toBe('true')
  expect(cancelReplay.headers()['idempotency-original-correlation-id']).toBe(cancelApplied.originalCorrelationId)
  expect(await cancelReplay.json()).toMatchObject({
    isReplay: true,
    originalCorrelationId: cancelApplied.originalCorrelationId,
    detail: { draft: { id: draftId }, lines: [{ lineId, quantity: '2' }] },
  })

  fresh = await readDraft(request, draftId, readerActor)
  remainder = await readRemainders(request, draftId, readerActor)
  expect(fresh.lines.find((line) => line.lineId === lineId)?.quantity).toBe('2')
  expect(remainder.floorStatus).toBe('Readable')
  expect(remainder.lines.find((line) => line.lineId === lineId)).toMatchObject({
    quantity: '2', remainder: 'Cancelled', loaded: '2', shipped: '1', delivered: deliveredQuantity, minimum: '2',
  })
  expect(remainder.items).toHaveLength(3)
  expect(remainder.items[2]).toMatchObject({
    changeNo: 3, kind: 'Cancel', state: 'Approved', orderedQuantity: REVISED_QUANTITY,
    requestedQuantity: null, resultQuantity: '2', loadedAtDecision: '2',
  })

  await openDetail(page, draftId, readerActor)
  await expect(page.getByTestId('sales-remainder-change-3')).toContainText('Approved')
  await page.goto('/satis/siparisler')
  await expect(page).toHaveURL(/\/satis\/siparisler$/)
  await openDetail(page, draftId, readerActor)
  await expect(page.getByTestId('sales-remainder-change-3')).toContainText('Kalanı iptal et')

  const reopenedLogistics = await readInstruction(request, instructionId, logisticsDispatchActor)
  expect(reopenedLogistics.instruction.version).toBe(immutableLogistics.instruction.version)
  expect(reopenedLogistics.lines.find((line) => line.lineId === logisticsLineId)).toMatchObject({
    loaded: immutableLine?.loaded, shipped: immutableLine?.shipped, delivered: immutableLine?.delivered,
  })
  expect(reopenedLogistics.shares
    .map((share) => ({ shareId: share.shareId, lineId: share.lineId, kind: share.kind, quantity: share.quantity,
      sourceShipmentId: share.sourceShipmentId }))
    .sort((left, right) => left.shareId.localeCompare(right.shareId))).toEqual(immutableShares)
  expect(reopenedLogistics.dispatchNotes).toHaveLength(1)
  expect(reopenedLogistics.dispatchNotes.find((note) => note.dispatchNoteId === dispatchNoteId)).toEqual(immutableNote)
  const allocationAfterCancelResponse = await request.get(allocationStatePath, {
    headers: readHeaders(logisticsShipmentActor),
  })
  expect(allocationAfterCancelResponse.status()).toBe(200)
  expect(await allocationAfterCancelResponse.json()).toEqual(allocationAfterDelivery)
  expect(await readLabelVersion(request, preparerActor)).toBe(labelVersionAfterDelivery)

  // Invoice-source consumption and post-invoice cancellation are intentionally deferred to T11A's explicit decisions.
  // This journey proves the independent physical chain without fabricating an invoice or a financial correction.
})
