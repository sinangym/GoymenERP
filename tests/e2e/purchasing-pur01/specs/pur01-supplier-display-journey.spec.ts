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
 * Those three write nothing: every call is a read, so the other PUR01 journeys' counts are untouched.
 *
 * PUR01-LIST (PURCHASING-DS-LIST-01), declared after them: the same list on the design system — a real table from
 * 768 px, real labelled cards at 390 and 320, links by each actor's own capability, the server's status filter and
 * cursor, a busy continuation with one test-level failure and a real same-cursor retry, and a company switch with
 * a next page in flight. Its Drafts are arranged through the creator's real create command (see the block below).
 */

import { expect, test, type APIRequestContext, type Locator, type Page, type Response, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env'

const env = readEnvironment()
const {
  creatorActor, readerActor, companyId, otherCompanyId, orderId, supplierPartyId, companyAccountId,
  otherSupplierPartyId, otherCompanyAccountId,
  productId, productVersion, productUnitId,
} = env.seed

const ORDERS_PATH = '/api/v1/purchasing/orders'
const LIST_ROUTE = '/satinalma/siparisler'
const DETAIL_ROUTE = `${LIST_ROUTE}/${orderId}`
const CARI_ROUTE = `/cari/${supplierPartyId}`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const LIST_HEADING = 'Satın alma siparişleri'
const DETAIL_HEADING = 'Satın alma siparişi'
const DETAIL_LINES_TABLE = 'Sipariş kalem miktarları'
const DETAIL_LINE_LABELS = ['No', 'Ürün ID', 'Sipariş birimi ID', 'Sipariş', 'Teknik', 'Taahhüt', 'Kabul', 'Kalan']

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

interface OrderLineRead {
  readonly productId: string
  readonly unitId: string
}

interface OrderDetailRead {
  readonly order: OrderRead
  readonly lines: readonly OrderLineRead[]
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/** The seeded order exactly as the real read DTO answers it for this actor; all expectations bind to that record. */
async function readSeededOrderDetail(page: Page, actor: string): Promise<OrderDetailRead> {
  const response = await page.request.get(`${env.apiBaseUrl}${ORDERS_PATH}/${orderId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId },
  })
  expect(response.status()).toBe(200)
  const detail = (await response.json()) as OrderDetailRead
  const { order } = detail
  expect(order.id).toBe(orderId)
  expect(order.companyId).toBe(companyId)
  expect(order.supplierPartyId).toBe(supplierPartyId)
  expect(order.companyAccountId).toBe(companyAccountId)
  // The contract the screens consume: exactly these four fields, bound to the order's own supplier.
  expect(Object.keys(order.supplierDisplay).sort()).toEqual(['code', 'legalName', 'partyId', 'tradeName'])
  expect(order.supplierDisplay.partyId).toBe(supplierPartyId)
  expect(order.supplierDisplay.code.trim()).not.toBe('')
  expect(order.supplierDisplay.legalName).toBe(FIXTURE_SUPPLIER_LEGAL_NAME)
  expect(detail.lines).toHaveLength(1)
  expect(detail.lines[0].productId).toMatch(/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i)
  expect(detail.lines[0].unitId).toMatch(/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i)
  return detail
}

async function readSeededOrder(page: Page, actor: string): Promise<OrderRead> {
  return (await readSeededOrderDetail(page, actor)).order
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

test.describe('PUR01-DETAIL-DS the real purchase-order detail on the design system', () => {
  for (const width of [1440, 768, 390, 320] as const) {
    test(`PUR01-DETAIL-DS-${width} keeps the real line readable in the intended table/card layout without page overflow`, async ({ page }) => {
      await page.setViewportSize({ width, height: width >= 768 ? 900 : 844 })
      await signInAs(page, creatorActor)
      const detail = await readSeededOrderDetail(page, creatorActor)
      const supplier = detail.order.supplierDisplay
      const line = detail.lines[0]

      await page.goto(DETAIL_ROUTE)
      await expect(page.getByRole('heading', { level: 1, name: DETAIL_HEADING })).toBeVisible({ timeout: 30_000 })
      await expect(page.getByTestId('purchase-order-status-facts').locator('time')).toHaveAttribute('datetime', '2026-09-23')
      await expect(page.getByTestId('purchase-order-status-facts').locator('time')).toHaveText('23.09.2026')
      await expectWholeName(supplierIdentity(page), supplier, `the detail at ${width}`)

      const table = page.getByRole('table', { name: DETAIL_LINES_TABLE })
      const headers = table.getByRole('columnheader')
      const row = table.locator('tbody tr').first()
      const cells = row.getByRole('cell')
      await expect(table).toBeVisible()
      await expect(row).toBeVisible()
      await expect(cells).toHaveCount(DETAIL_LINE_LABELS.length)
      expect(await cells.evaluateAll((items) => items.map((item) => item.getAttribute('data-label')))).toEqual(DETAIL_LINE_LABELS)
      await expect(row).toContainText(line.productId)
      await expect(row).toContainText(line.unitId)

      if (width >= 768) {
        await expect(headers).toHaveCount(DETAIL_LINE_LABELS.length)
        for (const header of await headers.all()) await expect(header).toBeVisible()
        expect(await table.evaluate((element) => getComputedStyle(element).display)).toBe('table')
        if (width === 768) {
          const tableWrap = table.locator('..')
          expect(await tableWrap.evaluate((element) => element.scrollWidth > element.clientWidth)).toBe(true)
        }
      } else {
        // The semantic column headers remain in the accessibility tree while their one-pixel, clipped thead is
        // not drawn above the labelled cards. Playwright deliberately still resolves those roles as visible, so
        // assert the actual visual-hiding contract on the owning row group instead of misclassifying its children.
        const headerPresentation = await table.locator('thead').evaluate((element) => {
          const style = getComputedStyle(element)
          const box = element.getBoundingClientRect()
          return {
            position: style.position,
            overflow: style.overflow,
            clipPath: style.clipPath,
            width: box.width,
            height: box.height,
          }
        })
        expect(headerPresentation.position).toBe('absolute')
        expect(headerPresentation.overflow).toBe('hidden')
        expect(headerPresentation.clipPath).toBe('inset(50%)')
        expect(headerPresentation.width).toBeLessThanOrEqual(1)
        expect(headerPresentation.height).toBeLessThanOrEqual(1)
        expect(await table.evaluate((element) => getComputedStyle(element).display)).toBe('block')
        expect(await row.evaluate((element) => getComputedStyle(element).display)).toBe('grid')
        expect(await cells.evaluateAll((items) => items.every((item) => {
          const style = getComputedStyle(item)
          const box = item.getBoundingClientRect()
          return style.display === 'grid' && box.width > 0 && box.height > 0 && box.left >= -0.5 && box.right <= innerWidth + 0.5
        }))).toBe(true)
      }
      await expectNoPageOverflow(page, `the detail at ${width}`)

      const supplierLink = supplierIdentity(page).getByRole('link', { name: identityText(supplier), exact: true })
      await expect(supplierLink).toHaveAttribute('href', CARI_ROUTE)
      if (width === 390) {
        await supplierLink.focus()
        await expect(supplierLink).toBeFocused()
        await page.keyboard.press('Enter')
        await expect(page).toHaveURL(new RegExp(`${CARI_ROUTE}$`))
        await page.goBack()
        await expect(page.getByRole('heading', { level: 1, name: DETAIL_HEADING })).toBeVisible({ timeout: 30_000 })
      } else if (width === 320) {
        await supplierLink.click()
        await expect(page).toHaveURL(new RegExp(`${CARI_ROUTE}$`))
      }
    })
  }
})

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
// PUR01-LIST — the purchase-order list on the design system (PURCHASING-DS-LIST-01, #670).
//
// These journeys need a REAL second page, and the API's page is 50 orders (the screen sends no page size). So,
// only here and only on the disposable stack, the creator's real create command arranges ARRANGED_ORDERS Drafts
// from the gated fixture's own supplier and product. They are declared after the three read-only journeys above
// (which look for the seeded order on the first page) and this file is the suite's last, so no other journey's
// counts or first page are disturbed. No grant, fixture source, approval or other command is added.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
const CREATE_ROUTE = `${LIST_ROUTE}/yeni`
const LIST_NAME = 'Şirketin satın alma siparişleri'
const LIST_COLUMNS = ['Sipariş', 'Durum', 'Tedarikçi', 'İş tarihi', 'Satır', 'Güncellendi']
const CARD_LABELS = ['Tedarikçi', 'İş tarihi', 'Satır', 'Güncellendi']
const MORE = 'Daha fazla göster'
const LOADING_MORE = 'Sonraki sayfa yükleniyor…'
const RETRY_SAME_PAGE = 'Aynı sayfayı yeniden dene'
/** The API's own default page; the screen never asks for another size. */
const SERVER_PAGE = 50
/** Enough Drafts for a real second page of the unfiltered list and of the Draft filter. */
const ARRANGED_ORDERS = 52
const ARRANGED_EFFECTIVE_ON = '2026-09-18'
const ARRANGED_EFFECTIVE_ON_SHOWN = '18.09.2026'
const ARRANGED_QUANTITY = '10'

type OrderStatus = 'Draft' | 'Approved'

interface ListedOrder {
  readonly id: string
  readonly companyId: string
  readonly status: OrderStatus
  readonly effectiveOn: string
  readonly updatedAtUtc: string
  readonly lineCount: number
  readonly supplierDisplay: SupplierDisplay
}

interface OrderPage {
  readonly items: readonly ListedOrder[]
  readonly nextCursor: string | null
}

interface ListQuery {
  readonly status: string | null
  readonly cursor: string | null
  readonly supplierPartyId?: string | null
}

function apiHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company }
}

/** One page exactly as the real list read answers it for this actor and company. */
async function readPage(
  request: APIRequestContext,
  actor: string,
  query: { readonly status?: OrderStatus; readonly cursor?: string; readonly supplierPartyId?: string } = {},
  company = companyId,
): Promise<OrderPage> {
  const search = new URLSearchParams()
  if (query.cursor !== undefined) search.set('cursor', query.cursor)
  if (query.status !== undefined) search.set('status', query.status)
  if (query.supplierPartyId !== undefined) search.set('supplierPartyId', query.supplierPartyId)
  const suffix = search.toString()
  const response = await request.get(`${env.apiBaseUrl}${ORDERS_PATH}${suffix ? `?${suffix}` : ''}`, { headers: apiHeaders(actor, company) })
  expect(response.status()).toBe(200)
  const page = (await response.json()) as OrderPage
  expect(page.items.every((item) => item.companyId === company)).toBe(true)
  return page
}

/** The capabilities the real shell publishes to this actor in this company. */
async function capabilitiesOf(request: APIRequestContext, actor: string): Promise<readonly string[]> {
  const response = await request.get(`${env.apiBaseUrl}${CAPABILITIES_PATH}`, { headers: apiHeaders(actor) })
  expect(response.status()).toBe(200)
  const snapshot = (await response.json()) as { readonly capabilities?: unknown }
  expect(Array.isArray(snapshot.capabilities)).toBe(true)
  return snapshot.capabilities as readonly string[]
}

const idsOf = (page: OrderPage): string[] => page.items.map((item) => item.id)
const orderReference = (id: string): string => `…${id.slice(-8)}`

function queryOf(url: string): ListQuery {
  const search = new URL(url).searchParams
  return { status: search.get('status'), cursor: search.get('cursor') }
}

function isListRead(response: Response, company: string): boolean {
  const request = response.request()
  return request.method() === 'GET' && new URL(response.url()).pathname === ORDERS_PATH && request.headers()['x-company-id'] === company
}

/** The browser's own list read with exactly this status and cursor — neither more nor less. */
function listRead(page: Page, want: ListQuery, company = companyId): Promise<Response> {
  return page.waitForResponse((response) => {
    if (!isListRead(response, company)) return false
    const got = queryOf(response.url())
    return got.status === want.status && got.cursor === want.cursor
      && new URL(response.url()).searchParams.get('supplierPartyId') === (want.supplierPartyId ?? null)
  }, { timeout: 30_000 })
}

const tableRows = (page: Page): Locator => page.locator('tbody tr[data-order-id]')
const cards = (page: Page): Locator => page.getByTestId('purchase-order-cards').locator('li[data-order-id]')
const supplierOf = (page: Page, id: string): Locator => page.getByTestId(`purchase-order-supplier-${id}`)
const statusFilter = (page: Page): Locator => page.getByLabel('Durum', { exact: true })

async function shownIds(rows: Locator): Promise<string[]> {
  return rows.evaluateAll((elements) => elements.map((element) => element.getAttribute('data-order-id') ?? ''))
}

/** The rows on screen are exactly these orders, in this order — none missing, none left over, none twice. */
async function expectRows(rows: Locator, expected: readonly string[], where: string): Promise<void> {
  await expect(rows, `row count on ${where}`).toHaveCount(expected.length)
  expect(await shownIds(rows), `rows on ${where}`).toEqual(expected)
}

/** The instant as this browser shows it; computed in the page so the time zone is the browser's own. */
async function shownInstant(page: Page, value: string): Promise<string> {
  return page.evaluate((instant) => new Date(instant).toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' }), value)
}

/** Holds the first continuation request of the page until the test decides its fate; later ones go to the API. */
async function holdFirstContinuation(page: Page): Promise<{ readonly held: Promise<Route>; readonly sent: string[] }> {
  const sent: string[] = []
  let capture: (route: Route) => void = () => undefined
  const held = new Promise<Route>((resolve) => { capture = resolve })
  await page.route((url) => url.pathname === ORDERS_PATH && url.searchParams.has('cursor'), async (route) => {
    sent.push(route.request().url())
    if (sent.length === 1) { capture(route); return }
    await route.continue()
  })
  return { held, sent }
}

test.describe('PUR01-LIST the purchase-order list on the design system', () => {
  /** The Drafts this journey arranged through the creator's real create command. */
  let arranged: readonly string[] = []
  let otherSupplierOrderId = ''

  test.beforeAll(async ({ request }) => {
    test.setTimeout(300_000)
    const created: string[] = []
    const otherResponse = await request.post(`${env.apiBaseUrl}${ORDERS_PATH}`, {
      headers: {
        ...apiHeaders(creatorActor), 'Idempotency-Key': 'browser-suite:pur01:ds-list-other-supplier-order', 'Content-Type': 'application/json',
      },
      data: {
        supplierPartyId: otherSupplierPartyId, companyAccountId: otherCompanyAccountId,
        sourceRequestId: null, effectiveOn: ARRANGED_EFFECTIVE_ON,
        lines: [{
          lineId: '0670d511-0000-4a00-8000-000000000099', productId, expectedProductVersion: productVersion,
          unitId: productUnitId, quantity: ARRANGED_QUANTITY, sourceRequestLineId: null,
        }],
      },
    })
    expect(otherResponse.status(), 'arranging the second supplier order').toBe(201)
    const otherOrder = ((await otherResponse.json()) as { readonly detail: { readonly order: { readonly id: string } } }).detail.order
    expect(otherOrder).toMatchObject({
      companyId, status: 'Draft', supplierPartyId: otherSupplierPartyId,
      companyAccountId: otherCompanyAccountId, effectiveOn: ARRANGED_EFFECTIVE_ON,
    })
    otherSupplierOrderId = otherOrder.id
    for (let number = 1; number <= ARRANGED_ORDERS; number += 1) {
      const suffix = String(number).padStart(2, '0')
      const response = await request.post(`${env.apiBaseUrl}${ORDERS_PATH}`, {
        headers: {
          ...apiHeaders(creatorActor), 'Idempotency-Key': `browser-suite:pur01:ds-list-order-${suffix}`, 'Content-Type': 'application/json',
        },
        data: {
          supplierPartyId, companyAccountId, sourceRequestId: null, effectiveOn: ARRANGED_EFFECTIVE_ON,
          lines: [{
            lineId: `0670d511-0000-4a00-8000-0000000000${suffix}`, productId, expectedProductVersion: productVersion, unitId: productUnitId,
            quantity: ARRANGED_QUANTITY, sourceRequestLineId: null,
          }],
        },
      })
      // 201 for the first send and for the same key's replay (a worker restart re-runs this hook); never a new order twice.
      expect(response.status(), `arranging list order ${suffix}`).toBe(201)
      const order = ((await response.json()) as { readonly detail: { readonly order: { readonly id: string } } }).detail.order
      expect(order).toMatchObject({ companyId, status: 'Draft', supplierPartyId, companyAccountId, effectiveOn: ARRANGED_EFFECTIVE_ON })
      created.push(order.id)
    }
    expect(new Set(created).size, 'each key arranged its own order').toBe(ARRANGED_ORDERS)
    arranged = created
  })

  test('PUR01-LIST-1 a real table from 768 px up, real labelled cards at 390 and 320: the whole fixture supplier, no sideways scroll', async ({ page }) => {
    test.setTimeout(240_000)
    await signInAs(page, creatorActor)
    const supplier = (await readSeededOrder(page, creatorActor)).supplierDisplay
    const first = await readPage(page.request, creatorActor)
    // The first page is the API's own full page with a real continuation, and it is this journey's Drafts.
    expect(first.items).toHaveLength(SERVER_PAGE)
    expect(first.nextCursor).not.toBeNull()
    expect(first.items.filter((item) => !arranged.includes(item.id))).toEqual([])
    expect(first.items.filter((item) => item.status !== 'Draft' || item.lineCount !== 1 || item.effectiveOn !== ARRANGED_EFFECTIVE_ON)).toEqual([])
    for (const item of first.items) expect(item.supplierDisplay).toEqual(supplier)
    const probe = first.items[0]
    const updated = await shownInstant(page, probe.updatedAtUtc)

    for (const width of [1440, 768]) {
      await page.setViewportSize({ width, height: 900 })
      const read = listRead(page, { status: null, cursor: null })
      await page.goto(LIST_ROUTE)
      expect((await read).status()).toBe(200)
      await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
      const table = page.getByRole('table', { name: LIST_NAME, exact: true })
      await expect(table, `the table at ${width}`).toBeVisible({ timeout: 30_000 })
      await expect(page.getByTestId('purchase-order-cards'), `cards drawn beside the table at ${width}`).toHaveCount(0)
      await expect(table.locator('thead th')).toHaveText(LIST_COLUMNS)
      await expectRows(tableRows(page), idsOf(first), `the table at ${width}`)

      const cells = page.locator(`tbody tr[data-order-id="${probe.id}"] td`)
      await expect(cells).toHaveCount(LIST_COLUMNS.length)
      await expect(cells.nth(0)).toHaveText(orderReference(probe.id))
      await expect(cells.nth(1)).toHaveText('Taslak')
      await expect(cells.nth(3)).toHaveText(ARRANGED_EFFECTIVE_ON_SHOWN)
      await expect(cells.nth(4)).toHaveText('1')
      await expect(cells.nth(5)).toHaveText(updated)
      await expect(supplierOf(page, probe.id).locator('[data-supplier-display="record"]')).toHaveCount(1)
      await expectWholeName(supplierOf(page, probe.id), supplier, `the table at ${width}`)
      await expectNoPageOverflow(page, `the table at ${width}`)
    }

    for (const width of [390, 320]) {
      await page.setViewportSize({ width, height: 844 })
      const read = listRead(page, { status: null, cursor: null })
      await page.goto(LIST_ROUTE)
      expect((await read).status()).toBe(200)
      await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
      await expect(page.getByRole('list', { name: LIST_NAME, exact: true }), `the cards at ${width}`).toBeVisible({ timeout: 30_000 })
      await expect(page.getByRole('table', { name: LIST_NAME, exact: true }), `a table drawn beside the cards at ${width}`).toHaveCount(0)
      await expectRows(cards(page), idsOf(first), `the cards at ${width}`)

      const card = page.locator(`li[data-order-id="${probe.id}"]`)
      await expect(card.locator('[data-tone]')).toHaveText('Taslak')
      await expect(card.getByRole('link', { name: orderReference(probe.id), exact: true })).toHaveAttribute('href', `${LIST_ROUTE}/${probe.id}`)
      // Every fact carries its own label: a card has no column headers to lean on.
      await expect(card.locator('dl > div > dt')).toHaveText(CARD_LABELS)
      const facts = card.locator('dl > div > dd')
      await expect(facts).toHaveCount(CARD_LABELS.length)
      await expect(facts.nth(1)).toHaveText(ARRANGED_EFFECTIVE_ON_SHOWN)
      await expect(facts.nth(2)).toHaveText('1')
      await expect(facts.nth(3)).toHaveText(updated)
      await expect(supplierOf(page, probe.id).locator('[data-supplier-display="record"]')).toHaveCount(1)
      await expectWholeName(supplierOf(page, probe.id), supplier, `the card at ${width}`)
      // Not one piece of any of the fifty cards leaves the viewport.
      const outside = await page.getByTestId('purchase-order-cards').evaluate((root) => {
        const viewport = document.documentElement.clientWidth
        return [...root.querySelectorAll('*')].filter((element) => {
          const box = element.getBoundingClientRect()
          return box.width > 0 && (box.left < 0 || box.right > viewport + 0.5)
        }).length
      })
      expect(outside, `card content outside the viewport at ${width}`).toBe(0)
      await expectNoPageOverflow(page, `the cards at ${width}`)
    }

    // Table or cards is layout only: narrowing the open page swaps one for the other without another read.
    await page.setViewportSize({ width: 1440, height: 900 })
    const opened = listRead(page, { status: null, cursor: null })
    await page.goto(LIST_ROUTE)
    expect((await opened).status()).toBe(200)
    await expectRows(tableRows(page), idsOf(first), 'the table before narrowing')
    const readsWhileResizing: string[] = []
    page.on('request', (request) => {
      if (request.method() === 'GET' && new URL(request.url()).pathname === ORDERS_PATH) readsWhileResizing.push(request.url())
    })
    await page.setViewportSize({ width: 390, height: 844 })
    await expectRows(cards(page), idsOf(first), 'the cards after narrowing')
    await expect(page.getByRole('table', { name: LIST_NAME, exact: true })).toHaveCount(0)
    await page.setViewportSize({ width: 768, height: 900 })
    await expectRows(tableRows(page), idsOf(first), 'the table after widening to 768')
    await expect(page.getByTestId('purchase-order-cards')).toHaveCount(0)
    expect(readsWhileResizing, 'a layout change issued a list read').toEqual([])
  })

  test('PUR01-LIST-2 creator and reader each get exactly the links their own capabilities publish; the creator’s do not survive the actor change', async ({ page }) => {
    const creatorCapabilities = await capabilitiesOf(page.request, creatorActor)
    const readerCapabilities = await capabilitiesOf(page.request, readerActor)
    expect(creatorCapabilities).toEqual(expect.arrayContaining([
      'purchasing.orders.list', 'purchasing.orders.detail', 'purchasing.orders.create', 'cari.parties.detail',
    ]))
    expect(readerCapabilities).toEqual(expect.arrayContaining(['purchasing.orders.list', 'purchasing.orders.detail']))
    expect(readerCapabilities).not.toContain('purchasing.orders.create')
    expect(readerCapabilities).not.toContain('cari.parties.detail')

    await signInAs(page, creatorActor)
    const supplier = (await readSeededOrder(page, creatorActor)).supplierDisplay
    const first = await readPage(page.request, creatorActor)
    const probe = first.items[0]
    expect(arranged).toContain(probe.id)
    expect(first.items).toHaveLength(SERVER_PAGE)
    expect(first.items.filter((item) => item.supplierDisplay.partyId !== supplierPartyId)).toEqual([])

    // CREATOR: the order's own link, the supplier's Cari link and the create link — each to its own route.
    const creatorRead = listRead(page, { status: null, cursor: null })
    await page.goto(LIST_ROUTE)
    expect((await creatorRead).status()).toBe(200)
    await expectRows(tableRows(page), idsOf(first), 'the creator’s list')
    const creatorRow = page.locator(`tbody tr[data-order-id="${probe.id}"]`)
    await expect(creatorRow.getByRole('link', { name: orderReference(probe.id), exact: true })).toHaveAttribute('href', `${LIST_ROUTE}/${probe.id}`)
    await expect(supplierOf(page, probe.id).getByRole('link', { name: identityText(supplier), exact: true })).toHaveAttribute('href', CARI_ROUTE)
    await expect(page.locator(`tbody a[href="${CARI_ROUTE}"]`)).toHaveCount(SERVER_PAGE)
    const create = page.getByRole('link', { name: 'Yeni', exact: true })
    await expect(create).toHaveCount(1)
    await expect(create).toHaveAttribute('href', CREATE_ROUTE)
    await create.click()
    await expect(page).toHaveURL(new RegExp(`${CREATE_ROUTE}$`))
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni satın alma siparişi' })).toBeVisible({ timeout: 30_000 })
    await page.goBack()
    await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
    await page.locator(`tbody tr[data-order-id="${probe.id}"]`).getByRole('link', { name: orderReference(probe.id), exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`${LIST_ROUTE}/${probe.id}$`))
    await expect(page.getByRole('heading', { level: 1, name: DETAIL_HEADING })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(probe.id, { exact: true })).toBeVisible()

    // ACTOR CHANGE on the same page: the reader. Same orders, same identity — and only the reader's own links.
    await signInAs(page, readerActor)
    const readerFirst = await readPage(page.request, readerActor)
    expect(idsOf(readerFirst)).toEqual(idsOf(first))
    const cariReads: string[] = []
    const orderWrites: string[] = []
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname
      if (path.startsWith('/api/v1/counterparties')) cariReads.push(request.url())
      if (request.method() !== 'GET' && path.startsWith(ORDERS_PATH)) orderWrites.push(`${request.method()} ${path}`)
    })
    const readerRead = listRead(page, { status: null, cursor: null })
    await page.goto(LIST_ROUTE)
    expect((await readerRead).status()).toBe(200)
    await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
    await expectRows(tableRows(page), idsOf(readerFirst), 'the reader’s list')
    await expect(page.getByRole('link', { name: 'Yeni', exact: true })).toHaveCount(0)
    await expect(page.locator(`a[href="${CREATE_ROUTE}"]`)).toHaveCount(0)
    await expect(page.locator('a[href^="/cari"]')).toHaveCount(0)
    await expect(supplierOf(page, probe.id).getByRole('link')).toHaveCount(0)
    await expect(supplierOf(page, probe.id).locator('[data-supplier-display="record"]')).toHaveCount(1)
    await expectWholeName(supplierOf(page, probe.id), supplier, 'the reader’s list')
    // The reader holds the order read, which publishes the detail: that one link stays, and it opens.
    const readerRow = page.locator(`tbody tr[data-order-id="${probe.id}"]`)
    await expect(readerRow.getByRole('link')).toHaveCount(1)
    await readerRow.getByRole('link', { name: orderReference(probe.id), exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`${LIST_ROUTE}/${probe.id}$`))
    await expect(page.getByRole('heading', { level: 1, name: DETAIL_HEADING })).toBeVisible({ timeout: 30_000 })
    // The create screen is closed to the reader as a route too, not only as a missing link.
    await page.goto(CREATE_ROUTE)
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni satın alma siparişi' })).toHaveCount(0)
    expect(cariReads, 'a Cari read was issued for a reader without the Cari grant').toEqual([])
    expect(orderWrites, 'the reader’s screens sent an order command').toEqual([])
  })

  test('PUR01-LIST-3 the status filter is the server’s query and every filter change starts the cursor over', async ({ page }) => {
    await signInAs(page, readerActor)
    const cursorReads: ListQuery[] = []
    const filterReads: ListQuery[] = []
    page.on('request', (request) => {
      if (request.method() !== 'GET' || new URL(request.url()).pathname !== ORDERS_PATH) return
      const query = queryOf(request.url())
      if (query.cursor !== null) cursorReads.push(query)
      else if (query.status !== null) filterReads.push(query)
    })
    const more = page.getByRole('button', { name: MORE, exact: true })
    const statuses = page.locator('tbody tr[data-order-id] [data-tone]')

    // ALL: the first page, then its own continuation to the end.
    const allRead = listRead(page, { status: null, cursor: null })
    await page.goto(LIST_ROUTE)
    const all1 = (await (await allRead).json()) as OrderPage
    expect(all1.items).toHaveLength(SERVER_PAGE)
    expect(all1.nextCursor).not.toBeNull()
    await expectRows(tableRows(page), idsOf(all1), 'the unfiltered first page')
    await expect(statusFilter(page)).toHaveValue('')
    const all2Read = listRead(page, { status: null, cursor: all1.nextCursor })
    await more.click()
    const all2 = (await (await all2Read).json()) as OrderPage
    expect(all2.items.length).toBeGreaterThan(0)
    expect(all2.nextCursor).toBeNull()
    await expectRows(tableRows(page), [...idsOf(all1), ...idsOf(all2)], 'the unfiltered list at its end')
    await expect(more).toHaveCount(0)

    // DRAFT: the server filters; the pages already read are dropped and the cursor is the Draft list's own.
    const draft1Read = listRead(page, { status: 'Draft', cursor: null })
    await statusFilter(page).selectOption('Draft')
    const draft1 = (await (await draft1Read).json()) as OrderPage
    expect(draft1.items).toHaveLength(SERVER_PAGE)
    expect(draft1.items.filter((item) => item.status !== 'Draft')).toEqual([])
    expect(draft1.nextCursor).not.toBeNull()
    await expectRows(tableRows(page), idsOf(draft1), 'the Draft first page')
    await expect(statuses).toHaveText(Array.from({ length: SERVER_PAGE }, () => 'Taslak'))
    const draft2Read = listRead(page, { status: 'Draft', cursor: draft1.nextCursor })
    await more.click()
    const draft2 = (await (await draft2Read).json()) as OrderPage
    expect(draft2.items.length).toBeGreaterThan(0)
    expect(draft2.items.filter((item) => item.status !== 'Draft')).toEqual([])
    await expectRows(tableRows(page), [...idsOf(draft1), ...idsOf(draft2)], 'the Draft list after its continuation')
    // The real API agrees, read independently of the browser.
    expect(idsOf(await readPage(page.request, readerActor, { status: 'Draft' }))).toEqual(idsOf(draft1))
    expect(idsOf(await readPage(page.request, readerActor, { status: 'Draft', cursor: draft1.nextCursor ?? '' }))).toEqual(idsOf(draft2))

    // APPROVED: again from the start — none of the Draft rows and no Draft cursor carried over.
    const approvedRead = listRead(page, { status: 'Approved', cursor: null })
    await statusFilter(page).selectOption('Approved')
    const approved = (await (await approvedRead).json()) as OrderPage
    expect(approved.items.filter((item) => item.status !== 'Approved')).toEqual([])
    expect(approved.items.filter((item) => arranged.includes(item.id))).toEqual([])
    await expectRows(tableRows(page), idsOf(approved), 'the Approved list')
    expect(await statuses.allTextContents()).toEqual(approved.items.map(() => 'Onaylandı'))
    await expect(page.locator('[data-state="empty-filtered"]')).toHaveCount(approved.items.length === 0 ? 1 : 0)
    await expect(more).toHaveCount(approved.nextCursor === null ? 0 : 1)
    await expect(statusFilter(page)).toHaveValue('Approved')

    // ALL again: the first page only — the finished unfiltered list's end and the filters' cursors are all gone.
    await statusFilter(page).selectOption('')
    await expectRows(tableRows(page), idsOf(all1), 'the unfiltered list after the filters')
    await expect(more).toBeEnabled()
    const againRead = listRead(page, { status: null, cursor: all1.nextCursor })
    await more.click()
    expect((await againRead).status()).toBe(200)
    await expectRows(tableRows(page), [...idsOf(all1), ...idsOf(all2)], 'the unfiltered list continued again')

    // Every cursor went out with its own filter, and each filter change was one cursor-less server query.
    expect(cursorReads).toEqual([
      { status: null, cursor: all1.nextCursor },
      { status: 'Draft', cursor: draft1.nextCursor },
      { status: null, cursor: all1.nextCursor },
    ])
    expect(filterReads).toEqual([{ status: 'Draft', cursor: null }, { status: 'Approved', cursor: null }])
  })

  test('PUR01-LIST-4 a busy continuation sends once; one failed next page keeps the rows and the retry sends the same cursor to the real API', async ({ page }) => {
    await signInAs(page, readerActor)
    const { held, sent } = await holdFirstContinuation(page)
    const firstRead = listRead(page, { status: null, cursor: null })
    await page.goto(LIST_ROUTE)
    const first = (await (await firstRead).json()) as OrderPage
    expect(first.items).toHaveLength(SERVER_PAGE)
    expect(first.nextCursor).not.toBeNull()
    await expectRows(tableRows(page), idsOf(first), 'the first page')

    // BUSY: the request is on its way; the button says so, is disabled, and pressing it again sends nothing.
    await page.getByRole('button', { name: MORE, exact: true }).click()
    const heldRoute = await held
    expect(queryOf(heldRoute.request().url())).toEqual({ status: null, cursor: first.nextCursor })
    const busy = page.getByRole('button', { name: LOADING_MORE, exact: true })
    await expect(busy).toBeDisabled()
    await expect(busy).toHaveAttribute('aria-busy', 'true')
    await expect(page.getByRole('button', { name: MORE, exact: true })).toHaveCount(0)
    await busy.click({ force: true })
    await page.keyboard.press('Enter')
    await page.keyboard.press('Space')
    await page.waitForTimeout(750)
    expect(sent, 'a second continuation was sent while the first was pending').toHaveLength(1)
    await expectRows(tableRows(page), idsOf(first), 'the list while the next page is pending')

    // FAILURE, once and at test level only: this one answer is replaced by a 503; nothing reaches the API.
    await heldRoute.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'service_unavailable' }) })
    const retry = page.getByRole('button', { name: RETRY_SAME_PAGE, exact: true })
    await expect(retry).toBeVisible()
    await expect(retry).toBeEnabled()
    await expect(page.locator('[data-state="service-unavailable"]')).toHaveCount(1)
    await expect(page.getByRole('button', { name: MORE, exact: true })).toHaveCount(0)
    await expect(busy).toHaveCount(0)
    await expectRows(tableRows(page), idsOf(first), 'the list after the failed next page')
    // Nothing is resent on its own.
    await page.waitForTimeout(1500)
    expect(sent, 'the failed page was resent without the user').toHaveLength(1)
    await expectRows(tableRows(page), idsOf(first), 'the list a moment after the failure')

    // RETRY: the same cursor, this time answered by the real API; its rows are appended once.
    const retried = listRead(page, { status: null, cursor: first.nextCursor })
    await retry.click()
    const answer = await retried
    expect(answer.status()).toBe(200)
    const second = (await answer.json()) as OrderPage
    expect(second.items.length).toBeGreaterThan(0)
    expect(sent).toHaveLength(2)
    expect(sent[1]).toBe(sent[0])
    await expectRows(tableRows(page), [...idsOf(first), ...idsOf(second)], 'the list after the retry')
    expect(new Set([...idsOf(first), ...idsOf(second)]).size).toBe(first.items.length + second.items.length)
    await expect(page.locator('[data-state="service-unavailable"]')).toHaveCount(0)
    await expect(retry).toHaveCount(0)
    await expect(page.getByRole('button', { name: MORE, exact: true })).toHaveCount(second.nextCursor === null ? 0 : 1)
    // The real API agrees on that page, read independently of the browser.
    expect(idsOf(await readPage(page.request, readerActor, { cursor: first.nextCursor ?? '' }))).toEqual(idsOf(second))
  })

  test('PUR01-LIST-5 a company switch with a filter set and a next page in flight leaves no row, filter or late answer of the departed company', async ({ page }) => {
    await signInAs(page, creatorActor)
    const other = await readPage(page.request, creatorActor, {}, otherCompanyId)
    expect(other.items.filter((item) => arranged.includes(item.id))).toEqual([])
    const { held, sent } = await holdFirstContinuation(page)

    // GDC, filtered to Draft, with its next page requested and not yet answered.
    await page.goto(LIST_ROUTE)
    await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
    const draftRead = listRead(page, { status: 'Draft', cursor: null })
    await statusFilter(page).selectOption('Draft')
    const draft = (await (await draftRead).json()) as OrderPage
    expect(draft.items).toHaveLength(SERVER_PAGE)
    expect(draft.nextCursor).not.toBeNull()
    await expectRows(tableRows(page), idsOf(draft), 'the GDC Draft list before the switch')
    await page.getByRole('button', { name: MORE, exact: true }).click()
    const heldRoute = await held
    expect(heldRoute.request().headers()['x-company-id']).toBe(companyId)
    await expect(page.getByRole('button', { name: LOADING_MORE, exact: true })).toBeDisabled()

    // GDC → Other: one fresh, unfiltered, cursor-less read in Other's own context.
    const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
    await expect(picker).toHaveValue(companyId)
    const otherRead = listRead(page, { status: null, cursor: null }, otherCompanyId)
    await picker.selectOption(otherCompanyId)
    expect((await otherRead).status()).toBe(200)
    await expect(picker).toHaveValue(otherCompanyId)
    await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
    await expect(statusFilter(page)).toHaveValue('')
    await expectRows(tableRows(page), idsOf(other), 'the other company’s list')
    await expect(page.locator('[data-state="empty-no-data"]')).toHaveCount(other.items.length === 0 ? 1 : 0)

    // The departed company's next page is let through now. The page may already have abandoned that request,
    // in which case there is nothing left to continue; either way its rows must never be drawn.
    await heldRoute.continue().catch(() => undefined)
    await page.waitForTimeout(1500)
    expect(sent, 'a continuation was sent in the other company').toHaveLength(1)
    await expectRows(tableRows(page), idsOf(other), 'the other company’s list after the late answer')
    await expect(statusFilter(page)).toHaveValue('')
    await expect(page.getByRole('button', { name: LOADING_MORE, exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: RETRY_SAME_PAGE, exact: true })).toHaveCount(0)
    for (const id of arranged) await expect(supplierOf(page, id)).toHaveCount(0)
    const underOther = await page.locator('body').innerText()
    expect(arranged.filter((id) => underOther.includes(orderReference(id))), 'GDC orders on screen under the other company').toEqual([])

    // Other → GDC: a fresh first page — unfiltered, with its continuation offered again from the start.
    const gdcRead = listRead(page, { status: null, cursor: null })
    await picker.selectOption(companyId)
    const gdc = (await (await gdcRead).json()) as OrderPage
    await expect(picker).toHaveValue(companyId)
    expect(gdc.items).toHaveLength(SERVER_PAGE)
    await expect(statusFilter(page)).toHaveValue('')
    await expectRows(tableRows(page), idsOf(gdc), 'the GDC list after returning')
    await expect(page.getByRole('button', { name: MORE, exact: true })).toBeEnabled()
    expect(sent, 'the departed list’s continuation was resent on return').toHaveLength(1)
  })

  test('PUR01-SUP-FILTER-1 filters a real multi-supplier list, combines status and retries the same filtered cursor', async ({ page }) => {
    test.setTimeout(180_000)
    await page.setViewportSize({ width: 1440, height: 900 })
    await signInAs(page, readerActor)

    const filteredFirst = await readPage(page.request, readerActor, { status: 'Draft', supplierPartyId })
    expect(filteredFirst.items).toHaveLength(SERVER_PAGE)
    expect(filteredFirst.nextCursor).not.toBeNull()
    expect(filteredFirst.items.every((item) => item.status === 'Draft' && item.supplierDisplay.partyId === supplierPartyId)).toBe(true)
    const otherSupplier = await readPage(page.request, readerActor, { status: 'Draft', supplierPartyId: otherSupplierPartyId })
    expect(idsOf(otherSupplier)).toEqual([otherSupplierOrderId])
    expect(otherSupplier.items[0].supplierDisplay.partyId).toBe(otherSupplierPartyId)

    await page.goto(LIST_ROUTE)
    await expect(page.getByRole('heading', { level: 1, name: LIST_HEADING })).toBeVisible({ timeout: 30_000 })
    const probe = (await readPage(page.request, readerActor)).items[0]
    const supplierRead = listRead(page, { status: null, cursor: null, supplierPartyId })
    await page.getByTestId(`purchase-order-filter-supplier-${probe.id}`).click()
    expect((await supplierRead).status()).toBe(200)
    await expect(page).toHaveURL(new RegExp(`[?&]supplierPartyId=${supplierPartyId}(?:&|$)`))
    await expect(page.getByTestId('purchase-order-supplier-filter')).toBeVisible()
    await expectRows(tableRows(page), idsOf(await readPage(page.request, readerActor, { supplierPartyId })), 'the supplier-filtered first page')

    const draftRead = listRead(page, { status: 'Draft', cursor: null, supplierPartyId })
    await statusFilter(page).selectOption('Draft')
    const draftFirst = (await (await draftRead).json()) as OrderPage
    await expectRows(tableRows(page), idsOf(draftFirst), 'the supplier and Draft filtered first page')
    const activeUrl = new URL(page.url())
    expect(activeUrl.searchParams.get('supplierPartyId')).toBe(supplierPartyId)
    expect(activeUrl.searchParams.get('status')).toBe('Draft')

    const { held, sent } = await holdFirstContinuation(page)
    await page.getByRole('button', { name: MORE, exact: true }).click()
    const failedRoute = await held
    const failedUrl = new URL(failedRoute.request().url())
    expect(failedUrl.searchParams.get('supplierPartyId')).toBe(supplierPartyId)
    expect(failedUrl.searchParams.get('status')).toBe('Draft')
    expect(failedUrl.searchParams.get('cursor')).toBe(draftFirst.nextCursor)
    await failedRoute.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'service_unavailable' }) })
    await expectRows(tableRows(page), idsOf(draftFirst), 'the filtered rows after the failed continuation')
    const retry = page.getByRole('button', { name: RETRY_SAME_PAGE, exact: true })
    await expect(retry).toBeVisible()
    const retried = listRead(page, { status: 'Draft', cursor: draftFirst.nextCursor, supplierPartyId })
    await retry.click()
    const second = (await (await retried).json()) as OrderPage
    expect(sent).toHaveLength(2)
    expect(sent[1]).toBe(sent[0])
    await expectRows(tableRows(page), [...idsOf(draftFirst), ...idsOf(second)], 'the filtered rows after the real retry')
  })

  test('PUR01-SUP-FILTER-2 keeps the filter in URL history, opens a second real supplier and clears it without a lookup', async ({ page }) => {
    test.setTimeout(180_000)
    await page.setViewportSize({ width: 1440, height: 900 })
    await signInAs(page, creatorActor)

    const deepLink = `${LIST_ROUTE}?status=Draft&supplierPartyId=${otherSupplierPartyId}`
    const deepRead = listRead(page, { status: 'Draft', cursor: null, supplierPartyId: otherSupplierPartyId })
    await page.goto(deepLink)
    expect((await deepRead).status()).toBe(200)
    await expectRows(tableRows(page), [otherSupplierOrderId], 'the second supplier deep link')
    const chip = page.getByTestId('purchase-order-supplier-filter')
    await expect(chip).toBeVisible()
    await expect(chip).not.toContainText(otherSupplierPartyId)
    await expect(page.locator(`tbody tr[data-order-id="${otherSupplierOrderId}"] a[href="/cari/${otherSupplierPartyId}"]`)).toHaveCount(1)

    const clearedRead = listRead(page, { status: 'Draft', cursor: null })
    await page.getByRole('button', { name: 'Tedarikçi filtresini kaldır', exact: true }).click()
    const clearedResponse = await clearedRead
    expect(clearedResponse.status()).toBe(200)
    const clearedPage = (await clearedResponse.json()) as OrderPage
    expect(new URL(page.url()).searchParams.get('supplierPartyId')).toBeNull()
    expect(new URL(page.url()).searchParams.get('status')).toBe('Draft')

    await page.goBack()
    await expect(page).toHaveURL(new RegExp(`[?&]supplierPartyId=${otherSupplierPartyId}(?:&|$)`))
    await expectRows(tableRows(page), [otherSupplierOrderId], 'the restored supplier deep link after Back')
    await page.goForward()
    await expect(page).not.toHaveURL(new RegExp(`[?&]supplierPartyId=`))
    await expectRows(tableRows(page), idsOf(clearedPage), 'the restored unfiltered Draft page after Forward')
  })

  test('PUR01-SUP-FILTER-3 draws the filtered real supplier as accessible cards at 390 and 320 for a limited reader', async ({ page }) => {
    await signInAs(page, readerActor)
    for (const width of [390, 320]) {
      await page.setViewportSize({ width, height: 844 })
      const read = listRead(page, { status: 'Draft', cursor: null, supplierPartyId: otherSupplierPartyId })
      await page.goto(`${LIST_ROUTE}?status=Draft&supplierPartyId=${otherSupplierPartyId}`)
      expect((await read).status()).toBe(200)
      await expectRows(cards(page), [otherSupplierOrderId], `the second supplier cards at ${width}`)
      await expect(page.getByRole('table', { name: LIST_NAME, exact: true })).toHaveCount(0)
      await expect(page.locator('a[href^="/cari"]')).toHaveCount(0)
      await expect(page.getByTestId('purchase-order-supplier-filter')).not.toContainText(otherSupplierPartyId)
      await expectNoPageOverflow(page, `the supplier filter cards at ${width}`)
    }
  })

  test('PUR01-SUP-FILTER-4 drops the departed company supplier query and its rows before reading the next company', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 })
    await signInAs(page, creatorActor)
    const initial = listRead(page, { status: 'Draft', cursor: null, supplierPartyId })
    await page.goto(`${LIST_ROUTE}?status=Draft&supplierPartyId=${supplierPartyId}`)
    expect((await initial).status()).toBe(200)
    await expect(page.getByTestId('purchase-order-supplier-filter')).toBeVisible()

    const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
    const otherRead = listRead(page, { status: null, cursor: null }, otherCompanyId)
    await picker.selectOption(otherCompanyId)
    expect((await otherRead).status()).toBe(200)
    expect(new URL(page.url()).searchParams.get('supplierPartyId')).toBeNull()
    expect(new URL(page.url()).searchParams.get('status')).toBeNull()
    await expect(page.getByTestId('purchase-order-supplier-filter')).toHaveCount(0)
    for (const id of arranged) await expect(page.locator(`[data-order-id="${id}"]`)).toHaveCount(0)
    await expect(page.locator(`[data-order-id="${otherSupplierOrderId}"]`)).toHaveCount(0)
  })
})
