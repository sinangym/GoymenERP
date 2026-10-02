/**
 * Q9 same-record browser acceptance against the real disposable stack.
 *
 * One public order is created, submitted and internally approved; an existing authorised actor records customer
 * acceptance in S-04. The browser request is frozen and replayed with the same key/body, close/reopen and a fresh GET
 * retain the exact id/revision/version, and a following commercial revision makes the old acceptance non-current.
 * Rev.1 is submitted and internally approved again to prove Rev.0 acceptance cannot satisfy the new revision.
 * No actor, grant, seed key or fixture-only route is introduced here.
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
} = seed

const RUN = testedCommit.slice(0, 12)
const BUSINESS_DATE = '2026-09-18'
const DRAFTS_PATH = '/api/v1/sales/drafts'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const CUSTOMER_ACCEPTANCE_RECORD = 'sales.orders.customer-acceptance.record'

interface DraftReading {
  readonly draft: {
    readonly id: string
    readonly approvalState: string
    readonly version: string
    readonly revision: string
  }
  readonly customerAcceptance: {
    readonly required: boolean
    readonly acceptedRevision: string | null
    readonly acceptedBy: string | null
    readonly acceptedAtUtc: string | null
    readonly isCurrentRevisionAccepted: boolean
  }
  readonly lines: readonly {
    readonly lineId: string
    readonly orderUnitId: string
    readonly unitPrice: string | null
    readonly priceUnitId: string | null
    readonly discountPercent: string | null
  }[]
}

interface DraftCommandResponse {
  readonly detail: DraftReading
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

function readHeaders(actor: string): Record<string, string> {
  return { 'X-Development-User': actor, 'X-Company-Id': companyId }
}

function commandHeaders(actor: string, key: string, correlationId = `${key}-correlation`): Record<string, string> {
  return { ...readHeaders(actor), 'Idempotency-Key': key, 'X-Correlation-Id': correlationId }
}

async function capabilitiesOf(request: APIRequestContext, actor: string): Promise<readonly string[]> {
  const response = await request.get(`${apiBaseUrl}${CAPABILITIES_PATH}`, { headers: readHeaders(actor) })
  expect(response.status(), `capabilities are published for ${actor}`).toBe(200)
  return ((await response.json()) as { readonly capabilities: readonly string[] }).capabilities
}

async function findAcceptanceActor(request: APIRequestContext): Promise<string> {
  for (const actor of [preparerActor, approverActor, readerActor, supportCreatorActor, ordinaryCreatorActor, plannerActor]) {
    if ((await capabilitiesOf(request, actor)).includes(CUSTOMER_ACCEPTANCE_RECORD)) return actor
  }
  throw new Error(
    `BLOCKED: the s04 disposable fixture publishes no existing actor with ${CUSTOMER_ACCEPTANCE_RECORD}; `
    + 'the accepted predecessor grant is required without adding an actor or permission here.',
  )
}

async function readDraft(request: APIRequestContext, draftId: string, actor: string): Promise<DraftReading> {
  const response = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}`, { headers: readHeaders(actor) })
  expect(response.status(), `the same draft reopens for ${actor}`).toBe(200)
  const detail = (await response.json()) as DraftReading
  expect(detail.draft.id).toBe(draftId)
  return detail
}

async function stateCommand(request: APIRequestContext, draftId: string, actor: string, action: 'submission' | 'approval',
  expectedVersion: string, step: string): Promise<DraftCommandResponse> {
  const response = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}/${action}`, {
    headers: commandHeaders(actor, `SENTETIK-s04-ca-${step}-${RUN}`),
    data: { expectedVersion },
  })
  expect(response.status(), `${action} records on the same draft`).toBe(200)
  return response.json() as Promise<DraftCommandResponse>
}

async function openDetail(page: Page, draftId: string, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ 'X-Development-User': actor })
  await page.goto(`/satis/siparisler/${draftId}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible({ timeout: 30_000 })
}

test('S04-CA-01 customer acceptance reopens and replays once, then Rev.1 requires its own acceptance', async ({ page, request }) => {
  test.setTimeout(180_000)
  const acceptanceActor = await findAcceptanceActor(request)

  const currencies = await request.get(`${apiBaseUrl}${CURRENCIES_PATH}?effectiveOn=${BUSINESS_DATE}`, {
    headers: readHeaders(preparerActor),
  })
  expect(currencies.status()).toBe(200)
  const lira = ((await currencies.json()) as {
    readonly currencies: readonly { readonly currencyId: string; readonly code: string }[]
  }).currencies.find((currency) => currency.code === 'TRY')
  expect(lira, 'the approved TRY profile is listed for the seed business date').toBeTruthy()

  const lineId = randomUUID()
  const createdResponse = await request.post(DRAFTS_PATH.startsWith('/api/')
    ? `${apiBaseUrl}${DRAFTS_PATH}` : DRAFTS_PATH, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-ca-create-${RUN}`),
    data: {
      kind: 'Order', partyId: realPartyId, companyAccountId: realCompanyAccountId, businessDate: BUSINESS_DATE,
      currencyId: lira!.currencyId,
      lines: [{
        lineId, productId, expectedProductVersion: productVersion, orderUnitId: productUnitId, quantity: '5',
        unitPrice: null, priceUnitId: null, requestedDeliveryDate: null, note: null,
        requestedDeliverySlices: null, discountPercent: null,
      }],
      requestedDeliveryDate: null, note: null, deliveryAddress: null, contact: null, responsible: null,
      paymentTerms: null, sourceStrategy: null, package: null,
    },
  })
  expect(createdResponse.status()).toBe(201)
  const created = (await createdResponse.json()) as DraftCommandResponse
  const draftId = created.detail.draft.id
  expect(created.detail).toMatchObject({
    draft: { id: draftId, revision: 'Rev.0', approvalState: 'Preparing' },
    customerAcceptance: { required: true, acceptedRevision: null, isCurrentRevisionAccepted: false },
    lines: [{ lineId, orderUnitId: productUnitId }],
  })

  const submitted = await stateCommand(request, draftId, preparerActor, 'submission', created.detail.draft.version, 'submit-r0')
  const approved = await stateCommand(request, draftId, approverActor, 'approval', submitted.detail.draft.version, 'approve-r0')
  expect(approved.detail).toMatchObject({
    draft: { id: draftId, revision: 'Rev.0', approvalState: 'Approved' },
    customerAcceptance: { acceptedRevision: null, isCurrentRevisionAccepted: false },
  })

  await openDetail(page, draftId, acceptanceActor)
  await expect(page.getByTestId('sales-customer-acceptance-state')).toContainText('Güncel revizyon için bekleniyor')
  const acceptanceSent = page.waitForRequest((candidate) => candidate.method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/customer-acceptance`)
  const acceptanceAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/customer-acceptance`)
  await page.getByTestId('sales-approval-commands').getByRole('button', { name: 'Müşteri kabulünü kaydet' }).click()
  const [browserRequest, browserResponse] = await Promise.all([acceptanceSent, acceptanceAnswered])
  expect(browserResponse.status()).toBe(200)
  expect(browserResponse.headers()['idempotency-replayed']).not.toBe('true')
  const acceptanceKey = browserRequest.headers()['idempotency-key']
  if (!acceptanceKey) throw new Error('The browser customer-acceptance command did not carry its frozen Idempotency-Key.')
  const acceptanceBody = browserRequest.postDataJSON() as { readonly expectedVersion: string }
  expect(acceptanceBody).toEqual({ expectedVersion: approved.detail.draft.version })
  const accepted = (await browserResponse.json()) as DraftCommandResponse
  expect(accepted).toMatchObject({
    isReplay: false,
    detail: {
      draft: { id: draftId, revision: 'Rev.0', approvalState: 'Approved' },
      customerAcceptance: {
        acceptedRevision: 'Rev.0', acceptedBy: acceptanceActor, isCurrentRevisionAccepted: true,
      },
    },
  })
  const acceptedVersion = accepted.detail.draft.version
  const acceptedAtUtc = accepted.detail.customerAcceptance.acceptedAtUtc
  expect(acceptedAtUtc).toBeTruthy()

  await page.goto('/satis/siparisler')
  await expect(page).toHaveURL(/\/satis\/siparisler$/)
  await openDetail(page, draftId, acceptanceActor)
  await expect(page.getByTestId('sales-customer-acceptance-state')).toContainText('Güncel revizyon kabul edildi')
  const reopened = await readDraft(request, draftId, acceptanceActor)
  expect(reopened).toMatchObject({
    draft: { id: draftId, revision: 'Rev.0', version: acceptedVersion, approvalState: 'Approved' },
    customerAcceptance: {
      acceptedRevision: 'Rev.0', acceptedBy: acceptanceActor, acceptedAtUtc, isCurrentRevisionAccepted: true,
    },
    lines: [{ lineId }],
  })

  // Simulate a lost reply with the browser's frozen key/body: the server returns the first result and creates no second effect.
  const replayResponse = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${draftId}/customer-acceptance`, {
    headers: commandHeaders(acceptanceActor, acceptanceKey, `SENTETIK-s04-ca-accept-replay-${RUN}`),
    data: acceptanceBody,
  })
  expect(replayResponse.status()).toBe(200)
  expect(replayResponse.headers()['idempotency-replayed']).toBe('true')
  expect(replayResponse.headers()['idempotency-original-correlation-id']).toBe(accepted.originalCorrelationId)
  const replayed = (await replayResponse.json()) as DraftCommandResponse
  expect(replayed).toMatchObject({
    isReplay: true,
    originalCorrelationId: accepted.originalCorrelationId,
    detail: {
      draft: { id: draftId, revision: 'Rev.0', version: acceptedVersion },
      customerAcceptance: {
        acceptedRevision: 'Rev.0', acceptedBy: acceptanceActor, acceptedAtUtc, isCurrentRevisionAccepted: true,
      },
    },
  })
  expect(await readDraft(request, draftId, readerActor)).toEqual(reopened)

  await openDetail(page, draftId, preparerActor)
  const form = page.getByTestId('sales-price-revision-form')
  await expect(form).toBeVisible()
  await form.getByLabel('Birim fiyat').fill('13.00')
  await form.getByLabel('Fiyat birimi').selectOption(productUnitId)
  await form.getByLabel('İskonto %').fill('5')
  const revisionAnswered = page.waitForResponse((candidate) => candidate.request().method() === 'POST'
    && new URL(candidate.url()).pathname === `${DRAFTS_PATH}/${draftId}/commercial-revision`)
  await form.getByRole('button', { name: 'Fiyat ve iskontoyu kaydet' }).click()
  const revisionResponse = await revisionAnswered
  expect(revisionResponse.status()).toBe(200)
  const revised = (await revisionResponse.json()) as DraftCommandResponse
  expect(revised.detail).toMatchObject({
    draft: { id: draftId, revision: 'Rev.1', approvalState: 'Preparing' },
    customerAcceptance: {
      acceptedRevision: 'Rev.0', acceptedBy: acceptanceActor, acceptedAtUtc, isCurrentRevisionAccepted: false,
    },
    lines: [{ lineId, unitPrice: '13.00', priceUnitId: productUnitId, discountPercent: '5' }],
  })
  expect(revised.detail.draft.version).not.toBe(acceptedVersion)

  const resubmitted = await stateCommand(request, draftId, preparerActor, 'submission', revised.detail.draft.version, 'submit-r1')
  await stateCommand(request, draftId, approverActor, 'approval', resubmitted.detail.draft.version, 'approve-r1')
  const revisionOne = await readDraft(request, draftId, acceptanceActor)
  expect(revisionOne).toMatchObject({
    draft: { id: draftId, revision: 'Rev.1', approvalState: 'Approved' },
    customerAcceptance: {
      acceptedRevision: 'Rev.0', acceptedBy: acceptanceActor, acceptedAtUtc, isCurrentRevisionAccepted: false,
    },
    lines: [{ lineId, unitPrice: '13.00', priceUnitId: productUnitId, discountPercent: '5' }],
  })

  await page.goto('/satis/siparisler')
  await openDetail(page, draftId, acceptanceActor)
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Onaylı')
  await expect(page.getByTestId('sales-customer-acceptance-state')).toContainText('Güncel revizyon için bekleniyor')
  await expect(page.getByTestId('sales-approval-commands')
    .getByRole('button', { name: 'Müşteri kabulünü kaydet' })).toBeVisible()
})
