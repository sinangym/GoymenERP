import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env'

const environment = readEnvironment()
const { apiBaseUrl, seed, testedCommit } = environment

interface DraftLine {
  readonly lineId: string
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly discountPercent: string | null
  readonly priceDisclosure: 'Visible' | 'Withheld'
}

interface DraftDetail {
  readonly draft: {
    readonly id: string
    readonly version: string
    readonly revision: string
    readonly approvalState: string
  }
  readonly lines: readonly DraftLine[]
}

interface DraftCommandResponse {
  readonly detail: DraftDetail
  readonly isReplay: boolean
}

interface RevisionHistory {
  readonly items: readonly {
    readonly revision: string
    readonly submittedBy: string | null
    readonly approvedBy: string | null
    readonly archivedBy: string
    readonly lines: readonly DraftLine[]
  }[]
}

interface ProblemDetails {
  readonly code: string
  readonly errors?: readonly { readonly field: string; readonly code: string }[]
}

function readHeaders(actor: string): Record<string, string> {
  return {
    'X-Development-User': actor,
    'X-Company-Id': seed.companyId,
  }
}

function commandHeaders(actor: string, key: string, correlationId: string): Record<string, string> {
  return {
    ...readHeaders(actor),
    'Idempotency-Key': key,
    'X-Correlation-Id': correlationId,
  }
}

async function openDraft(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ 'X-Development-User': actor })
  await page.goto(`/satis/siparisler/${seed.draftId}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toBeVisible()
}

async function readDetail(request: APIRequestContext): Promise<DraftDetail> {
  const response = await request.get(`${apiBaseUrl}/api/v1/sales/drafts/${seed.draftId}`, {
    headers: readHeaders(seed.preparerActor),
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as DraftDetail
}

async function readHistory(request: APIRequestContext): Promise<RevisionHistory> {
  const response = await request.get(`${apiBaseUrl}/api/v1/sales/drafts/${seed.draftId}/revisions`, {
    headers: readHeaders(seed.preparerActor),
  })
  expect(response.status()).toBe(200)
  return (await response.json()) as RevisionHistory
}

test('S04-08 one approved record is priced in the form, reopened as Rev.1, and its past-approved withdrawal is a terminal replayed refusal', async ({ page, request }) => {
  await openDraft(page, seed.preparerActor)
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Approved')

  const form = page.getByTestId('sales-price-revision-form')
  await expect(form).toBeVisible()
  await expect(form.getByRole('heading', { name: 'Fiyat ve iskonto revizyonu' })).toBeVisible()

  const prices = form.getByLabel('Birim fiyat')
  const priceUnits = form.getByLabel('Fiyat birimi')
  const discounts = form.getByLabel('İskonto %')
  await expect(prices).toHaveCount(2)
  await expect(priceUnits).toHaveCount(2)
  await expect(discounts).toHaveCount(2)
  await expect(prices.nth(0)).toHaveValue('')
  await expect(prices.nth(1)).toHaveValue('')

  await prices.nth(0).fill('13.00')
  await priceUnits.nth(0).selectOption(seed.productUnitId)
  await discounts.nth(0).fill('5')

  const revisionResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST'
      && new URL(response.url()).pathname === `/api/v1/sales/drafts/${seed.draftId}/commercial-revision`,
  )
  await form.getByRole('button', { name: 'Fiyat ve iskontoyu kaydet' }).click()
  const revisionResponse = await revisionResponsePromise
  expect(revisionResponse.status()).toBe(200)
  const revised = (await revisionResponse.json()) as DraftCommandResponse
  expect(revised.isReplay).toBe(false)
  expect(revised.detail.draft).toMatchObject({
    id: seed.draftId,
    revision: 'Rev.1',
    approvalState: 'Preparing',
  })
  expect(revised.detail.lines).toHaveLength(2)
  expect(revised.detail.lines[0]).toMatchObject({
    unitPrice: '13.00',
    priceUnitId: seed.productUnitId,
    discountPercent: '5',
    priceDisclosure: 'Visible',
  })
  expect(revised.detail.lines[1]).toMatchObject({
    unitPrice: null,
    priceUnitId: null,
    discountPercent: null,
    priceDisclosure: 'Visible',
  })
  await expect(page.getByText('Fiyat revizyonu kaydedildi.')).toBeVisible()

  await page.goto('/satis/siparisler')
  await expect(page).toHaveURL(/\/satis\/siparisler$/)
  await page.goto(`/satis/siparisler/${seed.draftId}`)
  await expect(page.getByTestId('sales-draft-approval-state')).toHaveText('Preparing')
  await expect(page.getByTestId('sales-price-revision-form')).toBeVisible()
  await expect(page.getByTestId('sales-price-revision-form').getByLabel('Birim fiyat').nth(0)).toHaveValue('13.00')
  await expect(page.getByTestId('sales-price-revision-form').getByLabel('İskonto %').nth(0)).toHaveValue('5')
  await expect(page.getByTestId('sales-price-revision-form').getByLabel('Birim fiyat').nth(1)).toHaveValue('')

  const history = await readHistory(request)
  expect(history.items).toHaveLength(1)
  expect(history.items[0].revision).toBe('Rev.0')
  expect(history.items[0].submittedBy).toBeTruthy()
  expect(history.items[0].approvedBy).toBeTruthy()
  expect(history.items[0].archivedBy).toBe(seed.preparerActor)
  expect(history.items[0].lines).toHaveLength(2)
  expect(history.items[0].lines.every((line) => line.priceDisclosure === 'Visible')).toBe(true)
  expect(history.items[0].lines.every((line) => line.unitPrice === null)).toBe(true)

  const withdrawalKey = `s04-price-withdraw-${testedCommit.slice(0, 12)}`
  const firstCorrelation = `s04-price-withdraw-first-${testedCommit.slice(0, 12)}`
  const withdrawalBody = { expectedVersion: revised.detail.draft.version }
  const firstWithdrawal = await request.post(`${apiBaseUrl}/api/v1/sales/drafts/${seed.draftId}/withdrawal`, {
    headers: commandHeaders(seed.preparerActor, withdrawalKey, firstCorrelation),
    data: withdrawalBody,
  })
  expect(firstWithdrawal.status()).toBe(409)
  const firstProblem = (await firstWithdrawal.json()) as ProblemDetails
  expect(firstProblem.code).toBe('sales_state_conflict')
  expect(firstProblem.errors).toContainEqual({ field: 'approvalState', code: 'state_conflict' })
  expect(firstWithdrawal.headers()['idempotency-replayed']).toBeUndefined()

  const replayWithdrawal = await request.post(`${apiBaseUrl}/api/v1/sales/drafts/${seed.draftId}/withdrawal`, {
    headers: commandHeaders(seed.preparerActor, withdrawalKey, `s04-price-withdraw-replay-${testedCommit.slice(0, 12)}`),
    data: withdrawalBody,
  })
  expect(replayWithdrawal.status()).toBe(409)
  expect((await replayWithdrawal.json()) as ProblemDetails).toEqual(firstProblem)
  expect(replayWithdrawal.headers()['idempotency-replayed']).toBe('true')
  expect(replayWithdrawal.headers()['idempotency-original-correlation-id']).toBe(firstCorrelation)

  const afterRefusal = await readDetail(request)
  expect(afterRefusal.draft).toMatchObject({
    id: seed.draftId,
    version: revised.detail.draft.version,
    revision: 'Rev.1',
    approvalState: 'Preparing',
  })
  expect((await readHistory(request)).items).toHaveLength(1)
})
