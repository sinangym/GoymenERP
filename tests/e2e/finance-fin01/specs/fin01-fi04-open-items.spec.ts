/**
 * FI04 acceptance over the disposable FIN01 stack.
 *
 * The fixture creates its open items only through Finance's accepted official-invoice
 * upload -> checker approval -> application path. These tests issue no mutation: they
 * prove the independent metadata and amount authorities, deterministic pagination and
 * stale-context clearing through the actual web route and real GET endpoints.
 */

import { expect, test, type Page, type Request, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const OPEN_ITEMS_API = '/api/v1/finance/open-items'
const OPEN_ITEMS_ROUTE = '/finans/acik-kalemler'
const OPEN_ITEMS_LABEL = 'Açık kalemler'

interface ReadSurfaceRequest {
  readonly method: string
  readonly url: URL
  readonly companyId: string | null
}

interface OpenItemAmount {
  readonly disclosure: 'Visible' | 'Redacted' | 'Unavailable'
  readonly money: null | { readonly amount: string; readonly currencyId: string }
}

interface OpenItem {
  readonly id: string
  readonly companyId: string
  readonly createdAtUtc: string
  readonly status: 'Open' | 'Closed' | null
  readonly initialAmount: OpenItemAmount
  readonly outstandingAmount: OpenItemAmount
}

interface OpenItemPage {
  readonly companyId: string
  readonly items: readonly OpenItem[]
  readonly nextCursor: string | null
}

async function signIn(page: Page, actor: string, width: number): Promise<ReadSurfaceRequest[]> {
  await page.setViewportSize({ width, height: width <= 390 ? 844 : 900 })
  await page.setExtraHTTPHeaders({ 'X-Development-User': actor })
  const requests: ReadSurfaceRequest[] = []
  page.on('request', (request: Request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/v1/finance/') || url.pathname.startsWith('/api/v1/work/')) {
      requests.push({
        method: request.method(),
        url,
        companyId: request.headers()['x-company-id'] ?? null,
      })
    }
  })
  return requests
}

function firstPageResponse(page: Page) {
  return page.waitForResponse((response) => {
    const url = new URL(response.url())
    return response.request().method() === 'GET'
      && url.pathname === OPEN_ITEMS_API
      && !url.searchParams.has('cursor')
  })
}

function detailResponse(page: Page, id: string) {
  return page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === `${OPEN_ITEMS_API}/${id}`)
}

function rowLink(page: Page, id: string) {
  return page.locator(`a[href="${OPEN_ITEMS_ROUTE}/${id}"]`)
}

function assertNoFinanceWrites(requests: readonly ReadSurfaceRequest[]) {
  expect(requests.filter((request) => request.method !== 'GET'),
    'FI04 is read-only and must not send a Finance or Work command').toEqual([])
}

async function expectNoPageOverflow(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow).toBeLessThanOrEqual(1)
}

function amountSpellings(value: string): readonly string[] {
  const parsed = Number(value)
  return [value, Number.isFinite(parsed) ? parsed.toLocaleString('tr-TR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }) : value]
}

async function expectAmountAbsentFromDom(page: Page, value: string) {
  const html = await page.content()
  for (const candidate of amountSpellings(value)) expect(html).not.toContain(candidate)
}

async function expectAmountVisibleInDom(page: Page, value: string) {
  const html = await page.content()
  expect(amountSpellings(value).some((candidate) => html.includes(candidate)),
    `one exact representation of ${value} must be visible`).toBe(true)
}

async function openList(page: Page): Promise<OpenItemPage> {
  const responsePromise = firstPageResponse(page)
  await page.goto(OPEN_ITEMS_ROUTE)
  const response = await responsePromise
  const body = await response.json() as OpenItemPage
  expect(response.status(), JSON.stringify(body)).toBe(200)
  expect(body.companyId).toBe(env.seed.companyId)
  expect(body.items).toHaveLength(50)
  expect(body.nextCursor).toBeTruthy()
  await expect(page.getByRole('heading', { level: 1, name: OPEN_ITEMS_LABEL, exact: true })).toBeVisible()
  await expect(rowLink(page, env.seed.openItemNewestId)).toBeVisible()
  await expectNoPageOverflow(page)
  return body
}

async function openAndReturnFromDetail(page: Page, id: string) {
  const detailPromise = detailResponse(page, id)
  await rowLink(page, id).click()
  const response = await detailPromise
  expect(response.status(), await response.text()).toBe(200)
  await expect(page).toHaveURL(`${OPEN_ITEMS_ROUTE}/${id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Açık kalem ayrıntısı', exact: true })).toBeVisible()
  await page.goBack()
  await expect(page).toHaveURL(OPEN_ITEMS_ROUTE)
  await expect(rowLink(page, id)).toBeVisible()
}

test.describe.configure({ mode: 'serial' })

for (const width of [1440, 390]) {
  test(`FI04 metadata-only ${width}px: list/detail work without status or amount disclosure`, async ({ page }) => {
    const requests = await signIn(page, env.seed.openItemMetadataActor, width)
    const list = await openList(page)
    const first = list.items[0]
    expect(first.id).toBe(env.seed.openItemNewestId)
    expect(first.status).toBeNull()
    expect(first.initialAmount).toEqual({ disclosure: 'Redacted', money: null })
    expect(first.outstandingAmount).toEqual({ disclosure: 'Redacted', money: null })
    // The accepted fixture amount is known to the test owner but must not survive in DOM,
    // title, accessibility text or data attributes for this actor.
    await expectAmountAbsentFromDom(page, '1000.51')
    await expect(page.getByText('Açık', { exact: true })).toHaveCount(0)
    await expect(page.getByText('Kapalı', { exact: true })).toHaveCount(0)
    await expect(page.getByText(/^(Toplam|Gecikmiş toplam|Açık toplam)$/)).toHaveCount(0)
    await openAndReturnFromDetail(page, first.id)
    await expectAmountAbsentFromDom(page, '1000.51')
    assertNoFinanceWrites(requests)
  })

  test(`FI04 metadata+amount ${width}px: disclosed stored values survive list/detail/reopen`, async ({ page }) => {
    const requests = await signIn(page, env.seed.openItemVisibleActor, width)
    const list = await openList(page)
    const first = list.items[0]
    expect(first.id).toBe(env.seed.openItemNewestId)
    expect(first.status).toBe('Open')
    expect(first.initialAmount.disclosure).toBe('Visible')
    expect(first.initialAmount.money).not.toBeNull()
    expect(first.outstandingAmount).toEqual(first.initialAmount)
    await expectAmountVisibleInDom(page, first.initialAmount.money!.amount)
    await openAndReturnFromDetail(page, first.id)
    const reopenPromise = detailResponse(page, first.id)
    await rowLink(page, first.id).click()
    expect((await reopenPromise).status()).toBe(200)
    await expectAmountVisibleInDom(page, first.initialAmount.money!.amount)
    await expectNoPageOverflow(page)
    assertNoFinanceWrites(requests)
  })

  for (const [profile, actor] of [
    ['amount-only', env.seed.openItemAmountOnlyActor],
    ['neither', env.seed.openItemNeitherActor],
  ] as const) {
    test(`FI04 ${profile} ${width}px: route and API remain closed with no stale private row`, async ({ page }) => {
      const requests = await signIn(page, actor, width)
      await page.goto('/finans')
      await expect(page.getByRole('link', { name: OPEN_ITEMS_LABEL, exact: true })).toHaveCount(0)
      await page.goto(OPEN_ITEMS_ROUTE)
      await expect(rowLink(page, env.seed.openItemNewestId)).toHaveCount(0)
      const answer = await page.evaluate(async ({ path, companyId }) => {
        const response = await fetch(path, { headers: { 'X-Company-Id': companyId } })
        return { status: response.status, body: await response.text() }
      }, { path: OPEN_ITEMS_API, companyId: env.seed.companyId })
      expect(answer.status, answer.body).toBe(403)
      await expectNoPageOverflow(page)
      assertNoFinanceWrites(requests)
    })
  }
}

test('FI04 cursor: equal timestamps remain deterministic and a failed continuation retries the same opaque cursor', async ({ page }) => {
  const requests = await signIn(page, env.seed.openItemVisibleActor, 1440)
  let failedCursor: string | null = null
  let retriedCursor: string | null = null
  await page.route('**/api/v1/finance/open-items?cursor=*', async (route: Route) => {
    const cursor = new URL(route.request().url()).searchParams.get('cursor')
    if (failedCursor === null) {
      failedCursor = cursor
      await route.abort('failed')
      return
    }
    retriedCursor = cursor
    await route.continue()
  })

  const list = await openList(page)
  expect(list.items[0].id).toBe(env.seed.openItemNewestId)
  expect(list.items[1].id).toBe(env.seed.openItemEqualTimestampLowerId)
  expect(list.items[0].createdAtUtc).toBe(list.items[1].createdAtUtc)

  await page.getByRole('button', { name: 'Daha fazla göster', exact: true }).click()
  await expect(page.locator('[data-state="network-error"]')).toBeVisible()
  await expect(rowLink(page, env.seed.openItemNewestId)).toBeVisible()
  await expect(rowLink(page, env.seed.openItemContinuationId)).toHaveCount(0)

  await page.getByRole('button', { name: 'Tekrar dene', exact: true }).click()
  await expect(rowLink(page, env.seed.openItemContinuationId)).toBeVisible()
  expect(retriedCursor).toBe(failedCursor)
  await expect(page.locator(`a[href^="${OPEN_ITEMS_ROUTE}/"]`)).toHaveCount(Number(env.seed.openItemCount))
  assertNoFinanceWrites(requests)
})

test('FI04 company and actor changes clear private rows before the replacement context answers', async ({ page }) => {
  const requests = await signIn(page, env.seed.openItemVisibleActor, 390)
  await openList(page)
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(env.seed.otherCompanyId)
  const dialog = page.getByRole('dialog')
  if (await dialog.count()) await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(picker).toHaveValue(env.seed.otherCompanyId, { timeout: 30_000 })
  await expect(rowLink(page, env.seed.openItemNewestId)).toHaveCount(0)
  await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible()

  await picker.selectOption(env.seed.companyId)
  const returnDialog = page.getByRole('dialog')
  if (await returnDialog.count()) await returnDialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(rowLink(page, env.seed.openItemNewestId)).toBeVisible({ timeout: 30_000 })

  const beforeActorLoss = requests.length
  await page.setExtraHTTPHeaders({ 'X-Development-User': env.seed.openItemNeitherActor })
  await page.reload()
  await expect(rowLink(page, env.seed.openItemNewestId)).toHaveCount(0)
  expect(requests.slice(beforeActorLoss).filter((request) => request.url.pathname === OPEN_ITEMS_API),
    'an actor without metadata permission must not read FI04 through the mounted route').toEqual([])
  assertNoFinanceWrites(requests)
})

test('FI04 controlled 503 and malformed answers close rows; retry reads a fresh real first page', async ({ page }) => {
  const requests = await signIn(page, env.seed.openItemVisibleActor, 1440)
  let firstPageCalls = 0
  await page.route('**/api/v1/finance/open-items', async (route: Route) => {
    firstPageCalls += 1
    if (firstPageCalls === 1) {
      await route.fulfill({ status: 503, contentType: 'application/problem+json',
        body: JSON.stringify({ type: 'about:blank', title: 'Hata', status: 503 }) })
      return
    }
    if (firstPageCalls === 2) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '{"items":[' })
      return
    }
    await route.continue()
  })

  await page.goto(OPEN_ITEMS_ROUTE)
  await expect(rowLink(page, env.seed.openItemNewestId)).toHaveCount(0)
  await expect(page.locator('[data-state="service-unavailable"]')).toBeVisible()
  await page.getByRole('button', { name: 'Tekrar dene', exact: true }).click()
  await expect(rowLink(page, env.seed.openItemNewestId)).toHaveCount(0)
  await expect(page.locator('[data-state="malformed-response"]')).toBeVisible()
  await page.getByRole('button', { name: 'Tekrar dene', exact: true }).click()
  await expect(rowLink(page, env.seed.openItemNewestId)).toBeVisible()
  expect(firstPageCalls).toBe(3)
  assertNoFinanceWrites(requests)
})
