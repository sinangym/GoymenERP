/**
 * PUR01-SUP — the record-bound supplier identity (#661) on the Purchasing order list and detail, on the disposable
 * real stack (real SQL, real API, real browser).
 *
 * The expected identity is never invented here: it is read from the real order read DTO of the seeded approval
 * order, which the gated fixture binds to the real Cari Supplier account it opens through the Cari API. The spec
 * then requires the screens to show exactly that code and legal name — and nothing else in their place.
 *
 * Three journeys:
 *  1. 1440 px, the creator (holds the Cari card grant): list → detail → Cari card → back.
 *  2. 390 px, the order-only reader: the same identity as plain text, with no link anywhere.
 *  3. The creator's company picker GDC → Other → GDC: the departed company's supplier never stays on screen.
 *
 * Nothing is written: every call is a read, so the other PUR01 journeys' counts are untouched.
 */

import { expect, test, type Locator, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env'

const env = readEnvironment()
const { creatorActor, readerActor, companyId, otherCompanyId, orderId, supplierPartyId, companyAccountId } = env.seed

const ORDERS_PATH = '/api/v1/purchasing/orders'
const LIST_ROUTE = '/satinalma/siparisler'
const DETAIL_ROUTE = `${LIST_ROUTE}/${orderId}`
const CARI_ROUTE = `/cari/${supplierPartyId}`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const LIST_HEADING = 'Satın alma siparişleri'
const DETAIL_HEADING = 'Satın alma siparişi'

/**
 * The legal name the gated fixture gives its Supplier: the suite's synthetic label followed by
 * Pur01BrowserSeed.SupplierLegalName, which is long on purpose. Measured on the real stack, not assumed.
 */
const FIXTURE_SUPPLIER_LEGAL_NAME =
  'SENTETIK browser-suite:pur01 Göymen Endüstriyel Çelik Ürünleri Tedarik ve Lojistik Sanayi ve Ticaret Anonim Şirketi Uzun Ünvan Kabul Tedarikçisi'

interface SupplierDisplay {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

interface OrderRead {
  readonly id: string
  readonly companyId: string
  readonly supplierPartyId: string
  readonly companyAccountId: string
  readonly supplierDisplay: SupplierDisplay
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/** The seeded order exactly as the real read DTO answers it for this actor; the identity is the server's own. */
async function readSeededOrder(page: Page, actor: string): Promise<OrderRead> {
  const response = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}/${orderId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId },
  })
  expect(response.status()).toBe(200)
  const order = ((await response.json()) as { readonly order: OrderRead }).order
  expect(order.id).toBe(orderId)
  expect(order.companyId).toBe(companyId)
  expect(order.supplierPartyId).toBe(supplierPartyId)
  expect(order.companyAccountId).toBe(companyAccountId)
  // The contract the screens consume: exactly these four fields, bound to the order's own supplier.
  expect(Object.keys(order.supplierDisplay).sort()).toEqual(['code', 'legalName', 'partyId', 'tradeName'])
  expect(order.supplierDisplay.partyId).toBe(supplierPartyId)
  expect(order.supplierDisplay.code.trim()).not.toBe('')
  expect(order.supplierDisplay.legalName).toBe(FIXTURE_SUPPLIER_LEGAL_NAME)
  return order
}

function identityText(supplier: SupplierDisplay): string {
  return `${supplier.code} — ${supplier.legalName}`
}

/** The page itself never scrolls sideways (WCAG 1.4.10). */
async function expectNoPageOverflow(page: Page, where: string): Promise<void> {
  const overflows = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
  )
  expect(overflows, `page scrolls horizontally on ${where}`).toBe(false)
}

/**
 * The whole legal name is drawn: present as text, not ellipsised, not clipped by its own box and — once scrolled
 * to — inside the viewport rather than pushed past its edge.
 */
async function expectWholeName(scope: Locator, supplier: SupplierDisplay, where: string): Promise<void> {
  const name = scope.getByText(identityText(supplier), { exact: true })
  await expect(name, `the supplier identity is missing on ${where}`).toBeVisible()
  await name.scrollIntoViewIfNeeded()
  const cut = await name.evaluate((element) => {
    // A link is an inline box and has no scroll size of its own; the line it sits in is what could clip it.
    const line = getComputedStyle(element).display === 'inline' && element.parentElement !== null ? element.parentElement : element
    const style = getComputedStyle(line)
    const viewport = document.documentElement.clientWidth
    return (
      style.textOverflow === 'ellipsis' ||
      line.scrollWidth > line.clientWidth + 1 ||
      [...element.getClientRects()].some((box) => box.left < 0 || box.right > viewport + 0.5)
    )
  })
  expect(cut, `the supplier legal name is cut on ${where}`).toBe(false)
}

function supplierCell(page: Page): Locator {
  return page.getByTestId(`purchase-order-supplier-${orderId}`)
}

function supplierIdentity(page: Page): Locator {
  return page.getByTestId('purchase-order-supplier-identity')
}

async function openList(page: Page): Promise<void> {
  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
  await expect(supplierCell(page)).toBeVisible({ timeout: 30_000 })
}

test('PUR01-SUP-1 at 1440 the creator reads the real supplier on the list and detail, opens its Cari card and comes back', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await signInAs(page, creatorActor)
  const supplier = (await readSeededOrder(page, creatorActor)).supplierDisplay

  // LIST: code and the whole legal name, read from the order itself — not a shortened account id.
  await openList(page)
  const cell = supplierCell(page)
  await expect(cell.locator('[data-supplier-display="record"]')).toHaveCount(1)
  await expectWholeName(cell, supplier, 'the list at 1440')
  await expect(cell).not.toContainText(companyAccountId)
  await expect(cell).not.toContainText(`…${companyAccountId.slice(-8)}`)
  const listLink = cell.getByRole('link', { name: identityText(supplier), exact: true })
  await expect(listLink).toHaveAttribute('href', CARI_ROUTE)
  await expectNoPageOverflow(page, 'the list at 1440')

  // DETAIL, reached the way a user reaches it: the order's own link in the same row.
  const row = page.getByRole('row').filter({ hasText: `…${orderId.slice(-8)}` })
  await row.getByRole('link', { name: `…${orderId.slice(-8)}`, exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`${DETAIL_ROUTE}$`))
  await expect(page.getByRole('heading', { level: 1, name: DETAIL_HEADING })).toBeVisible({ timeout: 30_000 })
  const identity = supplierIdentity(page)
  await expect(identity.locator('[data-supplier-display="record"]')).toHaveCount(1)
  await expectWholeName(identity, supplier, 'the detail at 1440')
  // The #660 id facts stay beside the identity.
  const facts = page.getByTestId('purchase-order-supplier-facts')
  await expect(facts).toContainText(supplierPartyId)
  await expect(facts).toContainText(companyAccountId)
  await expectNoPageOverflow(page, 'the detail at 1440')

  // CARI CARD: the link is real and the card it opens is that same party's.
  const detailLink = identity.getByRole('link', { name: identityText(supplier), exact: true })
  await expect(detailLink).toHaveAttribute('href', CARI_ROUTE)
  await detailLink.click()
  await expect(page).toHaveURL(new RegExp(`${CARI_ROUTE}$`))
  await expect(page.getByRole('heading', { level: 1, name: supplier.legalName, exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('main')).toContainText(supplier.code)

  // BACK: the same order, the same identity, still linked.
  await page.goBack()
  await expect(page).toHaveURL(new RegExp(`${DETAIL_ROUTE}$`))
  await expect(page.getByRole('heading', { level: 1, name: DETAIL_HEADING })).toBeVisible({ timeout: 30_000 })
  await expectWholeName(supplierIdentity(page), supplier, 'the detail after returning from the Cari card')
  await expect(supplierIdentity(page).getByRole('link', { name: identityText(supplier), exact: true })).toHaveAttribute('href', CARI_ROUTE)
  await expect(page.getByTestId('purchase-order-supplier-facts')).toContainText(supplierPartyId)
})

test('PUR01-SUP-2 at 390 the order-only reader sees the same identity as plain text, with no Cari link', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await signInAs(page, readerActor)
  // The reader's own real read carries the same record-bound identity; no Cari grant is involved in getting it.
  const supplier = (await readSeededOrder(page, readerActor)).supplierDisplay
  const cariReads: string[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/counterparties')) cariReads.push(request.url())
  })

  await openList(page)
  const cell = supplierCell(page)
  await expect(cell.locator('[data-supplier-display="record"]')).toHaveCount(1)
  await expectWholeName(cell, supplier, 'the list at 390')
  await expect(cell.getByRole('link')).toHaveCount(0)
  await expect(page.locator(`a[href^="/cari"]`)).toHaveCount(0)
  await expectNoPageOverflow(page, 'the list at 390')

  await page.goto(DETAIL_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: DETAIL_HEADING })).toBeVisible({ timeout: 30_000 })
  const identity = supplierIdentity(page)
  await expect(identity.locator('[data-supplier-display="record"]')).toHaveCount(1)
  await expectWholeName(identity, supplier, 'the detail at 390')
  await expect(identity.getByRole('link')).toHaveCount(0)
  await expect(page.locator(`a[href^="/cari"]`)).toHaveCount(0)
  await expect(page.getByTestId('purchase-order-supplier-facts')).toContainText(supplierPartyId)
  await expectNoPageOverflow(page, 'the detail at 390')

  // The identity came with the order: the screens issued no Cari read to draw it.
  expect(cariReads, 'a Cari read was issued for a reader without the Cari grant').toEqual([])
})

test('PUR01-SUP-3 the creator switches GDC → Other → GDC and the departed company’s supplier never stays on screen', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await signInAs(page, creatorActor)
  const supplier = (await readSeededOrder(page, creatorActor)).supplierDisplay
  // The other company holds none of GDC's orders for this actor: the real API says so before the screen is asked.
  const otherList = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: creatorActor, 'X-Company-Id': otherCompanyId },
  })
  expect(otherList.status()).toBe(200)
  const otherItems = ((await otherList.json()) as { readonly items: ReadonlyArray<{ readonly id: string; readonly companyId: string }> }).items
  expect(otherItems.every((item) => item.companyId === otherCompanyId)).toBe(true)
  expect(otherItems.map((item) => item.id)).not.toContain(orderId)

  await openList(page)
  await expectWholeName(supplierCell(page), supplier, 'the GDC list before the switch')
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await expect(picker).toHaveValue(companyId)

  // GDC → Other: every read from now on must be Other's; GDC's supplier may not survive in any form.
  const otherRead = page.waitForResponse((response) => {
    const request = response.request()
    return request.method() === 'GET' && new URL(response.url()).pathname === ORDERS_PATH &&
      request.headers()['x-company-id'] === otherCompanyId
  }, { timeout: 30_000 })
  await picker.selectOption(otherCompanyId)
  expect((await otherRead).status()).toBe(200)
  await expect(picker).toHaveValue(otherCompanyId)
  await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
  await expect(supplierCell(page)).toHaveCount(0)
  await expect(page.locator(`a[href="${CARI_ROUTE}"]`)).toHaveCount(0)
  const underOther = await page.locator('body').innerText()
  expect(underOther, 'the GDC supplier legal name is still on screen under the other company').not.toContain(supplier.legalName)
  expect(underOther, 'the GDC supplier code is still on screen under the other company').not.toContain(supplier.code)
  expect(underOther).not.toContain(`…${orderId.slice(-8)}`)
  // Nothing arrives late either: the state is the same after the departed company's reads could have landed.
  await page.waitForTimeout(1500)
  await expect(supplierCell(page)).toHaveCount(0)
  expect(await page.locator('body').innerText()).not.toContain(supplier.legalName)

  // Other → GDC: the order and its supplier come back from a fresh GDC read.
  const gdcRead = page.waitForResponse((response) => {
    const request = response.request()
    return request.method() === 'GET' && new URL(response.url()).pathname === ORDERS_PATH &&
      request.headers()['x-company-id'] === companyId
  }, { timeout: 30_000 })
  await picker.selectOption(companyId)
  expect((await gdcRead).status()).toBe(200)
  await expect(picker).toHaveValue(companyId)
  await expect(supplierCell(page)).toBeVisible({ timeout: 30_000 })
  await expectWholeName(supplierCell(page), supplier, 'the GDC list after returning')
  await expect(supplierCell(page).getByRole('link', { name: identityText(supplier), exact: true })).toHaveAttribute('href', CARI_ROUTE)
})
