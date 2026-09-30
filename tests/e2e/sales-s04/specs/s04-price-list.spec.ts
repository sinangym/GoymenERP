/**
 * Sales s04 — AT-018 / AT-021 against the REAL stack (user decision 2026-09-30): a Product price list is created on the U-05
 * screen, saved, closed and reopened, published; then an order line picks the list price as its REFERENCE (and default),
 * gets a discount and a maturity surcharge (vade farkı), is saved, and reopening shows the reference, the adjustments and the
 * server's net. A net below the list floor is refused.
 *
 * Real Chromium, the exact-head web build, the real API and SQL Server. NOTHING IS MOCKED OR INTERCEPTED.
 *
 * WRITTEN, NOT RUN in the authoring slice. It needs the central delta (AT-018-central-delta.patch): the three price-list
 * names registered, `AddProductPriceLists` + its initializer + `MapProductPriceListEndpoints` in the host, the price-list shell
 * provider published, and the s04 override granting the disposable PREPARER role `product.price-lists.read`, `…write` and
 * `…publish` beside its existing `sales.orders.price.write` / `sales.orders.price.read` (a human grant decision for real users).
 * Until then this file fails at its first capability assertion — it never tests something else.
 *
 * The cases are one serial journey, in file order:
 *   S04-PL-01 the preparer creates a price list for the seed's REAL product in the browser (price 42000.50, floor 40000, from
 *             today), saves, closes and reopens it as a Draft, publishes it; it reads "Geçerli"
 *   S04-PL-02 the preparer creates an order for today through the real API, opens it, fetches the line's list price and uses it
 *             (reference + default), gives 3 % discount and 1.5 % surcharge, saves; close/reopen shows the reference, "Liste
 *             fiyatı", %3, %1.5 and the server's net 41351.592275
 *   S04-PL-03 a 10 % discount (net 37800.45 < floor 40000) is refused 422 sales_price_below_floor and nothing changes
 */

import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { apiBaseUrl, testedCommit, seed } = env
const { preparerActor, companyId, realPartyId, realCompanyAccountId, productId, productVersion, productUnitId, productCode } = seed

const RUN = testedCommit.slice(0, 12)
const TODAY = new Date().toISOString().slice(0, 10)
const LIST_NAME = `SENTETIK s04 fiyat listesi ${RUN}`
const DRAFTS_PATH = '/api/v1/sales/drafts'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'

const journey: { priceListId?: string; draftId?: string; lineId?: string } = {}

test.describe.configure({ mode: 'serial' })

function readHeaders(actor: string): Record<string, string> {
  return { 'X-Development-User': actor, 'X-Company-Id': companyId }
}

function commandHeaders(actor: string, key: string): Record<string, string> {
  return { ...readHeaders(actor), 'Idempotency-Key': key, 'X-Correlation-Id': `${key}-correlation` }
}

async function capabilitiesOf(request: APIRequestContext, actor: string): Promise<readonly string[]> {
  const response = await request.get(`${apiBaseUrl}${CAPABILITIES_PATH}`, { headers: readHeaders(actor) })
  expect(response.status()).toBe(200)
  return ((await response.json()) as { readonly capabilities: readonly string[] }).capabilities
}

test('S04-PL-01 a price list is created, saved, closed, reopened and published on U-05', async ({ page, request }) => {
  expect(await capabilitiesOf(request, preparerActor)).toEqual(expect.arrayContaining([
    'product.price-lists.list', 'product.price-lists.create', 'product.price-lists.detail', 'product.price-lists.edit',
    'product.price-lists.publish',
  ]))

  await page.setExtraHTTPHeaders({ 'X-Development-User': preparerActor })
  await page.goto('/fiyat-listeleri/yeni')
  await page.getByLabel('Liste adı').fill(LIST_NAME)
  await expect(page.getByLabel('Para birimi (ISO, ör. TRY)')).toHaveValue('TRY')
  await page.getByLabel('Geçerlilik başlangıcı').fill(TODAY)
  await page.getByLabel('Ürün ara (kod veya ad)').fill(productCode)
  await page.getByRole('button', { name: 'Ara' }).click()
  await page.getByTestId('price-list-product-options').getByRole('button', { name: 'Listeye ekle' }).first().click()
  await page.getByLabel('1. Liste fiyatı').fill('42000,50')
  await page.getByLabel('1. Alt fiyat (isteğe bağlı)').fill('40000')

  const created = page.waitForResponse((response) => response.request().method() === 'POST'
    && new URL(response.url()).pathname === '/api/v1/price-lists')
  await page.getByTestId('price-list-save').click()
  const response = await created
  expect(response.status()).toBe(201)
  journey.priceListId = ((await response.json()) as { readonly priceList: { readonly id: string } }).priceList.id

  // Save → the stored list as the server answers it; close → the list page; reopen → the same Draft.
  await expect(page.getByTestId('price-list-name')).toHaveText(LIST_NAME)
  await page.goto('/fiyat-listeleri')
  await page.getByRole('link', { name: LIST_NAME }).click()
  await expect(page.getByTestId('price-list-version-1-status')).toHaveText('Taslak')
  await expect(page.getByTestId('price-list-line-price').first()).toHaveText('42.000,5')

  await page.getByTestId('price-list-publish').click()
  await expect(page.getByTestId('price-list-version-1-status')).toHaveText('Geçerli')
  await page.reload()
  await expect(page.getByTestId('price-list-version-1-status')).toHaveText('Geçerli')
  await expect(page.getByTestId('price-list-publish')).toHaveCount(0)
})

test('S04-PL-02 the order line takes the list price as reference and default, adjusts it, and reopens with the server net', async ({ page, request }) => {
  expect(journey.priceListId, 'S04-PL-01 published the list').toBeTruthy()
  const currencies = await request.get(`${apiBaseUrl}${CURRENCIES_PATH}?effectiveOn=${TODAY}`, { headers: readHeaders(preparerActor) })
  expect(currencies.status()).toBe(200)
  const lira = ((await currencies.json()) as { readonly currencies: readonly { readonly currencyId: string; readonly code: string }[] })
    .currencies.find((currency) => currency.code === 'TRY')
  expect(lira, 'the approved TRY profile is listed for today').toBeTruthy()
  const lineId = randomUUID()
  const created = await request.post(`${apiBaseUrl}${DRAFTS_PATH}`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-pl-create-${RUN}`),
    data: {
      kind: 'Order', partyId: realPartyId, companyAccountId: realCompanyAccountId, businessDate: TODAY, currencyId: lira!.currencyId,
      lines: [{
        lineId, productId, expectedProductVersion: productVersion, orderUnitId: productUnitId, quantity: '5',
        unitPrice: null, priceUnitId: null, requestedDeliveryDate: null, note: null, requestedDeliverySlices: null, discountPercent: null,
      }],
      requestedDeliveryDate: null, note: null, deliveryAddress: null, contact: null, responsible: null, paymentTerms: null,
      sourceStrategy: null, package: null,
    },
  })
  expect(created.status()).toBe(201)
  journey.draftId = ((await created.json()) as { readonly detail: { readonly draft: { readonly id: string } } }).detail.draft.id
  journey.lineId = lineId

  await page.setExtraHTTPHeaders({ 'X-Development-User': preparerActor })
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  const form = page.getByTestId('sales-price-revision-form')
  await expect(form).toBeVisible()
  await form.getByTestId('sales-price-fetch-1').click()
  await expect(form.getByTestId('sales-price-choices-1')).toContainText(LIST_NAME)
  await form.getByTestId('sales-price-choices-1').getByRole('listitem').filter({ hasText: LIST_NAME })
    .getByRole('button', { name: 'Bu fiyatı kullan' }).click()
  await expect(form.getByLabel('Birim fiyat')).toHaveValue('42000.5')
  await form.getByLabel('İskonto %').fill('3')
  await form.getByLabel('Vade farkı', { exact: true }).fill('1.5')
  await form.getByRole('button', { name: 'Fiyat ve iskontoyu kaydet' }).click()
  await expect(page.getByText('Fiyat revizyonu kaydedildi.')).toBeVisible()

  // Close and reopen: the reference, its source, the adjustments and the server's net stay visible.
  await page.goto('/satis/siparisler')
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  await expect(page.getByTestId('sales-line-1-reference')).toContainText(LIST_NAME)
  await expect(page.getByTestId('sales-line-1-source')).toHaveText('Liste fiyatı')
  await expect(page.getByTestId('sales-line-1-discount-adjustment')).toHaveText('%3')
  await expect(page.getByTestId('sales-line-1-surcharge')).toHaveText('%1.5')
  await expect(page.getByTestId('sales-line-1-net')).toHaveText('41351.592275')

  const pricing = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/line-pricing`, { headers: readHeaders(preparerActor) })
  expect(pricing.status()).toBe(200)
  expect(((await pricing.json()) as { readonly lines: readonly Record<string, unknown>[] }).lines[0]).toMatchObject({
    lineId, priceSource: 'Reference', discountPercent: '3', surchargePercent: '1.5', netUnitPrice: '41351.592275',
    priceReference: { priceListId: journey.priceListId, versionNo: 1, currencyCode: 'TRY', floorUnitPrice: '40000' },
  })
})

test('S04-PL-03 a net below the list floor is refused and nothing changes', async ({ request }) => {
  expect(journey.draftId, 'S04-PL-02 priced the order').toBeTruthy()
  const detail = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, { headers: readHeaders(preparerActor) })
  const before = (await detail.json()) as { readonly draft: { readonly version: string } }
  const refused = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/commercial-revision`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-pl-floor-${RUN}`),
    data: {
      expectedVersion: before.draft.version,
      lines: [{
        lineId: journey.lineId, unitPrice: '42000.5', priceUnitId: productUnitId, discountPercent: '10',
        priceReference: { priceListId: journey.priceListId, versionNo: 1 }, discountAmount: null, surchargePercent: null, surchargeAmount: null,
      }],
    },
  })
  expect(refused.status()).toBe(422)
  const problem = (await refused.json()) as { readonly errors: readonly { readonly field: string; readonly code: string }[] }
  expect(problem.errors).toEqual([{ field: 'lines[0].unitPrice', code: 'sales_price_below_floor' }])
  const after = (await (await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, { headers: readHeaders(preparerActor) })).json()) as
    { readonly draft: { readonly version: string } }
  expect(after.draft.version).toBe(before.draft.version)
})
