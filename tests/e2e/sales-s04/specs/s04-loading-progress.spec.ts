/**
 * SALES-LOADING-PROGRESS-UI-01 — real S04 acceptance.
 *
 * The test creates one two-line Sales order through the public API, then creates, issues and realizes one real
 * Logistics loading instruction for only its first line. The Sales detail must show owner-backed instructed / loaded /
 * shipped / delivered facts, and the untouched second line must show the owner's real zeros. Success is never mocked.
 * Controlled 503/malformed responses exercise only failure and retry; every recovered answer is the real API answer.
 * Existing actors and grants are reused unchanged. Nothing is written through Work and no new permission is introduced.
 */

import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext, type Page, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { apiBaseUrl, testedCommit, seed } = env
const {
  preparerActor,
  approverActor,
  readerActor,
  companyId,
  draftId: seededDraftId,
  realPartyId,
  realCompanyAccountId,
  productId,
  productVersion,
  productUnitId,
  allocationLabelId,
} = seed

const DRAFTS_PATH = '/api/v1/sales/drafts'
const BUSINESS_DATE = '2026-09-18'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const COMPANY_HEADER = 'X-Company-Id'
const COMPANY_B = '10000000-0000-0000-0000-000000000002'
const LOGISTICS_PREPARER_ACTOR = 's04-logistics-preparer'
const LOGISTICS_LOADER_ACTOR = 's04-logistics-loader'
const RUN = testedCommit.slice(0, 12)

interface DraftReading {
  readonly draft: { readonly id: string; readonly version: string }
  readonly lines: readonly {
    readonly lineId: string
    readonly lineNo: number
    readonly orderUnitId: string
    readonly quantity: string
  }[]
}

interface DraftCommandResponse {
  readonly detail: DraftReading
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
  }
}

interface LoadingProgressLine {
  readonly lineId: string
  readonly lineNo: number
  readonly orderUnitId: string
  readonly ordered: string
  readonly loadingInstructionLines: number
  readonly instructed: string
  readonly loaded: string
  readonly shipped: string
  readonly delivered: string
}

interface LoadingProgressResponse {
  readonly draftVersion: string
  readonly lines: readonly LoadingProgressLine[]
}

interface InventoryLabelDetail {
  readonly labelVersion: string
  readonly identity: { readonly labelId: string }
}

interface JourneyRecord {
  readonly orderId: string
  readonly instructedLineId: string
  readonly untouchedLineId: string
}

let journey: JourneyRecord | undefined

function readHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, [COMPANY_HEADER]: company }
}

function commandHeaders(actor: string, key: string): Record<string, string> {
  return { ...readHeaders(actor), 'Idempotency-Key': key }
}

async function readDraft(request: APIRequestContext, id: string, actor: string): Promise<DraftReading> {
  const response = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${id}`, { headers: readHeaders(actor) })
  expect(response.status(), `the real Sales detail answers ${actor}`).toBe(200)
  const detail = await response.json() as DraftReading
  expect(detail.draft.id).toBe(id)
  return detail
}

async function readProgress(request: APIRequestContext, id: string, actor: string): Promise<LoadingProgressResponse> {
  const response = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${id}/loading-progress`, {
    headers: readHeaders(actor),
  })
  expect(response.status(), `the real Sales loading-progress read answers ${actor}`).toBe(200)
  return response.json() as Promise<LoadingProgressResponse>
}

async function readLabelVersion(request: APIRequestContext): Promise<string> {
  const response = await request.get(`${apiBaseUrl}/api/v1/inventory/labels/${allocationLabelId}/detail`, {
    headers: readHeaders(preparerActor),
  })
  expect(response.status()).toBe(200)
  const label = await response.json() as InventoryLabelDetail
  expect(label.identity.labelId.toLowerCase()).toBe(allocationLabelId.toLowerCase())
  expect(label.labelVersion).toMatch(/^[1-9][0-9]*$/)
  return label.labelVersion
}

async function openDetail(page: Page, id: string, actor = preparerActor): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
  await page.goto(`/satis/siparisler/${id}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible({ timeout: 30_000 })
}

function loadingPath(id: string): string {
  return `${DRAFTS_PATH}/${id}/loading-progress`
}

function loadingLine(page: Page, lineId: string) {
  return page.getByTestId(`sales-linked-loading-line-${lineId}`)
}

async function expectFacts(page: Page, lineId: string, expected: {
  readonly ordered: string
  readonly instructed: string
  readonly loaded: string
  readonly shipped: string
  readonly delivered: string
  readonly instructionLines: string
}): Promise<void> {
  const row = loadingLine(page, lineId)
  await expect(row).toHaveAttribute('data-instruction-lines', expected.instructionLines)
  for (const fact of ['ordered', 'instructed', 'loaded', 'shipped', 'delivered'] as const) {
    await expect(row.locator(`[data-fact="${fact}"]`)).toHaveText(expected[fact])
  }
}

async function expectNoPageOverflow(page: Page): Promise<void> {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
}

async function arrangeRealJourney(request: APIRequestContext): Promise<JourneyRecord> {
  const currencies = await request.get(`${apiBaseUrl}/api/v1/sales/references/currencies?effectiveOn=${BUSINESS_DATE}`, {
    headers: readHeaders(preparerActor),
  })
  expect(currencies.status()).toBe(200)
  const lira = ((await currencies.json()) as {
    readonly currencies: readonly { readonly currencyId: string; readonly code: string }[]
  }).currencies.find((currency) => currency.code === 'TRY')
  expect(lira, 'the accepted TRY profile is available to the real Sales command').toBeTruthy()

  const instructedLineId = randomUUID()
  const untouchedLineId = randomUUID()
  const created = await request.post(`${apiBaseUrl}${DRAFTS_PATH}`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-load-create-${RUN}`),
    data: {
      kind: 'Order', partyId: realPartyId, companyAccountId: realCompanyAccountId, businessDate: BUSINESS_DATE,
      currencyId: lira!.currencyId,
      lines: [
        {
          lineId: instructedLineId, productId, expectedProductVersion: productVersion, orderUnitId: productUnitId,
          quantity: '5', unitPrice: null, priceUnitId: null, requestedDeliveryDate: null, note: null,
          requestedDeliverySlices: null, discountPercent: null,
        },
        {
          lineId: untouchedLineId, productId, expectedProductVersion: productVersion, orderUnitId: productUnitId,
          quantity: '3', unitPrice: null, priceUnitId: null, requestedDeliveryDate: null, note: null,
          requestedDeliverySlices: null, discountPercent: null,
        },
      ],
      requestedDeliveryDate: null, note: null, deliveryAddress: null, contact: null, responsible: null,
      paymentTerms: null, sourceStrategy: null, package: null,
    },
  })
  expect(created.status(), 'the public Sales command creates the two-line order').toBe(201)
  const orderId = ((await created.json()) as DraftCommandResponse).detail.draft.id

  let current = await readDraft(request, orderId, preparerActor)
  const submitted = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${orderId}/submission`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-load-submit-${RUN}`),
    data: { expectedVersion: current.draft.version },
  })
  expect(submitted.status()).toBe(200)

  current = await readDraft(request, orderId, approverActor)
  const approved = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${orderId}/approval`, {
    headers: commandHeaders(approverActor, `SENTETIK-s04-load-approve-${RUN}`),
    data: { expectedVersion: current.draft.version },
  })
  expect(approved.status()).toBe(200)

  const logisticsLineId = randomUUID()
  const instructionCreated = await request.post(`${apiBaseUrl}/api/v1/logistics/loading-instructions`, {
    headers: commandHeaders(LOGISTICS_PREPARER_ACTOR, `SENTETIK-s04-load-instruction-${RUN}`),
    data: {
      customerPartyId: realPartyId,
      companyAccountId: realCompanyAccountId,
      effectiveOn: BUSINESS_DATE,
      lines: [{
        lineId: logisticsLineId,
        sourceOrderId: orderId,
        sourceOrderLineId: instructedLineId,
        unitId: productUnitId,
        quantity: '5',
      }],
    },
  })
  expect(instructionCreated.status(), 'the real Logistics instruction binds the exact Sales line').toBe(201)
  const createdInstruction = await instructionCreated.json() as LoadingInstructionResponse
  const instructionId = createdInstruction.detail.instruction.id

  const instructionIssued = await request.post(
    `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/issue`,
    {
      headers: commandHeaders(LOGISTICS_PREPARER_ACTOR, `SENTETIK-s04-load-issue-${RUN}`),
      data: { expectedVersion: createdInstruction.detail.instruction.version },
    },
  )
  expect(instructionIssued.status()).toBe(200)
  const issuedInstruction = await instructionIssued.json() as LoadingInstructionResponse

  const loadRecorded = await request.post(
    `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/lines/${logisticsLineId}/loads`,
    {
      headers: commandHeaders(LOGISTICS_LOADER_ACTOR, `SENTETIK-s04-load-record-${RUN}`),
      data: {
        expectedVersion: issuedInstruction.detail.instruction.version,
        loadId: randomUUID(),
        quantity: '2',
        measurement: 'Measured',
      },
    },
  )
  expect(loadRecorded.status()).toBe(200)
  const loadedInstruction = await loadRecorded.json() as LoadingInstructionResponse

  const allocationId = randomUUID()
  const allocationCreated = await request.post(`${apiBaseUrl}/api/v1/inventory/physical-allocations`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-load-allocation-${RUN}`),
    data: {
      allocationId,
      salesOrderId: orderId,
      salesOrderLineId: instructedLineId,
      labelId: allocationLabelId,
      expectedLabelVersion: await readLabelVersion(request),
      quantityKg: '1',
      reason: 'SENTETIK browser-suite:s04 yükleme ilerlemesi kesin sevkiyat tahsisi',
      reservedUntilUtc: null,
    },
  })
  expect(allocationCreated.status()).toBe(201)

  const shipmentId = randomUUID()
  const shipmentRecorded = await request.post(
    `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/lines/${logisticsLineId}/shipments`,
    {
      headers: commandHeaders(LOGISTICS_LOADER_ACTOR, `SENTETIK-s04-load-shipment-${RUN}`),
      data: {
        expectedVersion: loadedInstruction.detail.instruction.version,
        shipmentId,
        allocationId,
        expectedAllocationVersion: '1',
        expectedLabelVersion: await readLabelVersion(request),
        movementId: randomUUID(),
        quantity: '1',
        occurredAtUtc: '2026-09-18T13:00:00Z',
      },
    },
  )
  expect(shipmentRecorded.status()).toBe(200)
  const shippedInstruction = await shipmentRecorded.json() as LoadingInstructionResponse

  const deliveryRecorded = await request.post(
    `${apiBaseUrl}/api/v1/logistics/loading-instructions/${instructionId}/lines/${logisticsLineId}/deliveries`,
    {
      headers: commandHeaders(LOGISTICS_LOADER_ACTOR, `SENTETIK-s04-load-delivery-${RUN}`),
      data: {
        expectedVersion: shippedInstruction.detail.instruction.version,
        deliveryId: randomUUID(),
        sourceShipmentId: shipmentId,
        quantity: '0.5',
        occurredAtUtc: '2026-09-18T13:30:00Z',
      },
    },
  )
  expect(deliveryRecorded.status()).toBe(200)

  return { orderId, instructedLineId, untouchedLineId }
}

test.describe.configure({ mode: 'serial' })

test('S04-LOAD-01 real owner facts show exact quantities and real zeros at 1440 and 390', async ({ page, request }) => {
  test.setTimeout(300_000)

  // The seed's genuine two-line order has no Logistics instruction. Zeros are accepted only after the real owner read.
  const seededDetail = await readDraft(request, seededDraftId, readerActor)
  const seededProgress = await readProgress(request, seededDraftId, readerActor)
  expect(seededProgress.draftVersion).toBe(seededDetail.draft.version)
  expect(seededProgress.lines).toHaveLength(2)
  for (const [index, line] of seededProgress.lines.entries()) {
    const detailLine = seededDetail.lines[index]
    expect(line).toMatchObject({
      lineId: detailLine.lineId,
      lineNo: detailLine.lineNo,
      orderUnitId: detailLine.orderUnitId,
      ordered: detailLine.quantity,
      loadingInstructionLines: 0,
      instructed: '0', loaded: '0', shipped: '0', delivered: '0',
    })
  }

  journey = await arrangeRealJourney(request)
  const detail = await readDraft(request, journey.orderId, preparerActor)
  const progress = await readProgress(request, journey.orderId, preparerActor)
  expect(progress.draftVersion).toBe(detail.draft.version)
  expect(progress.lines.map((line) => [line.lineId, line.orderUnitId, line.ordered])).toEqual(
    detail.lines.map((line) => [line.lineId, line.orderUnitId, line.quantity]),
  )
  expect(progress.lines.find((line) => line.lineId === journey.instructedLineId)).toMatchObject({
    loadingInstructionLines: 1, instructed: '5', loaded: '2', shipped: '1', delivered: '0.5',
  })
  expect(progress.lines.find((line) => line.lineId === journey.untouchedLineId)).toMatchObject({
    loadingInstructionLines: 0, instructed: '0', loaded: '0', shipped: '0', delivered: '0',
  })

  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport)
    await openDetail(page, journey.orderId)
    await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'ready')
    await expectFacts(page, journey.instructedLineId, {
      ordered: '5', instructed: '5', loaded: '2', shipped: '1', delivered: '0.5', instructionLines: '1',
    })
    await expectFacts(page, journey.untouchedLineId, {
      ordered: '3', instructed: '0', loaded: '0', shipped: '0', delivered: '0', instructionLines: '0',
    })
    await expect(page.getByRole('table', { name: 'Satır bazında yükleme ilerlemesi' })).toBeVisible()
    await expect(page.getByTestId('sales-linked-allocation')).toBeVisible()
    await expect(page.getByTestId('sales-line-1')).toBeVisible()
    await expect(page.getByTestId('sales-line-2')).toBeVisible()
    await expectNoPageOverflow(page)
  }
})

test('S04-LOAD-02 authentication, actor and company fences disclose no departed loading facts', async ({ page, request }) => {
  test.setTimeout(180_000)
  if (journey === undefined) throw new Error('S04-LOAD-01 did not arrange the real loading journey')

  const noActor = await request.get(`${apiBaseUrl}${loadingPath(journey.orderId)}`, {
    headers: { [COMPANY_HEADER]: companyId },
  })
  expect(noActor.status()).toBe(401)

  const forbidden = await request.get(`${apiBaseUrl}${loadingPath(journey.orderId)}`, {
    headers: readHeaders(LOGISTICS_LOADER_ACTOR),
  })
  expect(forbidden.status()).toBe(403)

  const foreign = await request.get(`${apiBaseUrl}${loadingPath(journey.orderId)}`, {
    headers: readHeaders(preparerActor, COMPANY_B),
  })
  expect(foreign.status()).toBe(404)

  await page.setViewportSize({ width: 1440, height: 900 })
  await openDetail(page, journey.orderId)
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'ready')

  const companyPicker = page.getByRole('combobox', { name: 'Şirket' })
  await companyPicker.selectOption(COMPANY_B)
  const confirmation = page.getByRole('dialog')
  if (await confirmation.count()) await confirmation.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(companyPicker).toHaveValue(COMPANY_B)
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveCount(0)
  await expect(page.getByTestId(`sales-linked-loading-line-${journey.instructedLineId}`)).toHaveCount(0)

  await companyPicker.selectOption(companyId)
  const returnConfirmation = page.getByRole('dialog')
  if (await returnConfirmation.count()) await returnConfirmation.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(companyPicker).toHaveValue(companyId)
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'ready')

  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: LOGISTICS_LOADER_ACTOR })
  await page.reload()
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveCount(0)
  await expect(page.getByTestId(`sales-linked-loading-line-${journey.instructedLineId}`)).toHaveCount(0)

  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: readerActor })
  await page.reload()
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'ready')
})

test('S04-LOAD-03 definite 404 and controlled failures show no quantities; retry recovers only from real API', async ({ page }) => {
  test.setTimeout(180_000)
  if (journey === undefined) throw new Error('S04-LOAD-01 did not arrange the real loading journey')
  const routePattern = `**${loadingPath(journey.orderId)}`

  // The request reaches the real API under a missing order id; the UI receives the server's real 404.
  const missingId = randomUUID()
  await page.route(routePattern, async (route: Route) => {
    await route.continue({ url: route.request().url().replace(journey!.orderId, missingId) })
  }, { times: 1 })
  await openDetail(page, journey.orderId)
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'not-found')
  await expect(page.getByTestId('sales-linked-loading').locator('[data-fact]')).toHaveCount(0)
  await expect(page.getByTestId('sales-linked-loading').getByRole('button')).toHaveCount(0)

  // Failure is injected once; retry must go through to the real API and show only that owner answer.
  await page.route(routePattern, async (route: Route) => {
    await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({
      status: 503, code: 'loading_provider_unavailable', title: 'Service Unavailable',
    }) })
  }, { times: 1 })
  await page.reload()
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'error')
  const realRetry = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === loadingPath(journey!.orderId)
      && response.status() === 200)
  await page.getByTestId('sales-linked-loading').getByRole('button', { name: 'Tekrar dene' }).click()
  await realRetry
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'ready')
  await expectFacts(page, journey.instructedLineId, {
    ordered: '5', instructed: '5', loaded: '2', shipped: '1', delivered: '0.5', instructionLines: '1',
  })

  // Corrupt one real response without fabricating a successful answer; retry again reaches the real endpoint.
  await page.route(routePattern, async (route: Route) => {
    const real = await route.fetch()
    expect(real.status()).toBe(200)
    const body = await real.json() as LoadingProgressResponse
    await route.fulfill({ response: real, json: { ...body, draftVersion: '0' } })
  }, { times: 1 })
  await page.reload()
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'malformed')
  await expect(page.getByTestId('sales-linked-loading').locator('[data-fact]')).toHaveCount(0)
  const malformedRetry = page.waitForResponse((response) =>
    response.request().method() === 'GET'
      && new URL(response.url()).pathname === loadingPath(journey!.orderId)
      && response.status() === 200)
  await page.getByTestId('sales-linked-loading').getByRole('button', { name: 'Tekrar dene' }).click()
  await malformedRetry
  await expect(page.getByTestId('sales-linked-loading-progress')).toHaveAttribute('data-state', 'ready')
  await expectNoPageOverflow(page)
})
