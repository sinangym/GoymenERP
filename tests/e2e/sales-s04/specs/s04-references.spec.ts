/**
 * Sales s04 — SALES-A2a owner references against the REAL stack (`browser:s04:ci`), Business 5745076917
 * item 2: the delivery address and contact come from the REAL Cari contact information, the currency from
 * the REAL Settings candidates, each through the Sales reference reads bound to `sales.drafts.create`.
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server, the `normal`
 * capability profile. NOTHING IS MOCKED OR INTERCEPTED: the only thing the suite adds to the browser is
 * the actor's identity header (the s03/s04 way; the API still authorizes the header itself).
 *
 * What the stage-2 seed (tests/GoymenERP.BrowserSuiteFixtureTool/Suites/S04) provides:
 *   - a REAL Cari party + GDC account opened through the real counterparty endpoint, with one delivery
 *     address and one contact person written through the REAL contact-information command;
 *   - a SECOND draft (`deliveryDraftId`) written by the gated Sales fixture writer on that REAL party, carrying
 *     the REAL address and contact ids with the owner's versions and the REAL approved currency — exactly what a
 *     real create would store. It is only REOPENED here, never mutated; the approval journey runs on draft 1.
 *
 * Scope, stated honestly: the create WITH references is NOT proven in the browser. The product picker (E1,
 * ÜRÜN's product list read + grant) does not exist yet, so the S-03 form withholds its lines and the save;
 * the browser proves the REAL options of the three header selects and the reopen of the stored references.
 * The create with references is proven on real SQL through the host
 * (SalesDraftApiSqlServerRelationalAcceptanceTests). Nothing here shows or sends a price.
 *
 * Runs after the journey (Playwright runs files alphabetically: `s04-approval-journey` < `s04-references`):
 *   S04-06 the preparer opens the new-draft screen, picks the REAL customer and sees the REAL currency, delivery
 *          address and contact options (the browser's own 200 answers of the real reference reads); no save
 *   S04-07 the preparer reopens draft 2: the delivery block shows the REAL address text, contact name and
 *          currency code; the reader sees the same by URL and no command
 */

import { expect, test, type Page, type Response } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const {
  preparerActor,
  readerActor,
  companyId,
  deliveryDraftId,
  realPartyId,
  realCompanyAccountId,
  deliveryAddressId,
  deliveryContactId,
} = env.seed
// The seed exports GUIDs and actor ids only (validate_browser_acceptance.seed_export). The texts are the seed's
// deterministic labels (S04BrowserSeed: the address line, the contact's display name, the approved TRY code), which
// the seed verified the real Sales reference reads answer before the suite starts.
const deliveryAddressText = 'SENTETIK browser-suite:s04 Sanayi Cad. 1'
const deliveryContactName = 'SENTETIK browser-suite:s04 Ayşe Yükleme'
const currencyCode = 'TRY'
/**
 * The SEED's own business date, not a number picked here: it mirrors `S04BrowserSeed.BusinessDate` (2026-09-18,
 * S04BrowserSeed.cs:88). The approved TRY candidate the seed wrote is effective from `TurkishLiraEffectiveFrom`
 * (2026-09-18, S04BrowserSeed.cs:85) with no end date (`EffectiveThrough: null`, SalesDraftFixtureStore.cs:153), so
 * the seeded draft already proved TRY valid on this date. Bound: the suite proves the list for THIS date, not the
 * run's own date (follow-up: exercise the create path on the run's date).
 */
const businessDate = '2026-09-18'
/** The screen's idle rule for the currency select, word for word (salesText references.currencyIdle). */
const currencyIdleText = 'Para birimleri sipariş tarihine göre listelenir; önce sipariş tarihini girin.'
const DRAFTS_PATH = '/api/v1/sales/drafts'
const CONTACT_INFORMATION_PATH = '/api/v1/sales/references/contact-information'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const ACCOUNTS_PATH = '/api/v1/sales/references/accounts'
const CREATE_ROUTE = '/satis/siparisler/yeni'
const DETAIL_ROUTE = `/satis/siparisler/${deliveryDraftId}`
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

interface OwnerReference {
  readonly id: string
  readonly expectedVersion: string
}

interface DeliveryDraftReading {
  readonly draft: {
    readonly id: string
    readonly partyId: string
    readonly companyAccountId: string
    readonly currencyId: string
    readonly approvalState: string
  }
  readonly deliveryAddress: OwnerReference | null
  readonly contact: OwnerReference | null
}

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/** Draft 2 as the real API answers it to `actor` (the browser's own company header). */
async function readDeliveryDraft(page: Page, actor: string): Promise<DeliveryDraftReading> {
  const response = await page.request.get(`${env.apiBaseUrl}${DRAFTS_PATH}/${deliveryDraftId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': companyId },
  })
  expect(response.status(), `the real detail route answers ${actor}`).toBe(200)
  const reading = (await response.json()) as DeliveryDraftReading
  expect(reading.draft.id).toBe(deliveryDraftId)
  return reading
}

/** Collects the browser's own answers on one GET reference route. */
function watchRead(page: Page, pathname: string): Response[] {
  const answers: Response[] = []
  page.on('response', (response) => {
    const request = response.request()
    if (request.method() === 'GET' && new URL(response.url()).pathname === pathname) answers.push(response)
  })
  return answers
}

const currencySelect = (page: Page) => page.getByTestId('sales-currency-select')
const addressSelect = (page: Page) => page.getByTestId('sales-delivery-address-select')
const contactSelect = (page: Page) => page.getByTestId('sales-delivery-contact-select')
const saveButton = (page: Page) => page.getByRole('button', { name: 'Taslak olarak kaydet' })

async function expectNoApprovalCommand(page: Page): Promise<void> {
  await expect(page.getByRole('button', { name: 'Onaya gönder' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Onayla' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Reddet' })).toHaveCount(0)
}

/** The preparer holds sales.drafts.create, the binding of the reference reads: the owner's texts are shown. */
async function expectDeliveryBlock(page: Page): Promise<void> {
  await expect(page.getByTestId('sales-delivery-block')).toBeVisible()
  await expect(page.getByTestId('sales-delivery-address-text')).toContainText(deliveryAddressText)
  await expect(page.getByTestId('sales-delivery-contact-text')).toContainText(deliveryContactName)
  await expect(page.getByTestId('sales-currency-code')).toHaveText(currencyCode)
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * The reader holds only sales.drafts.read. The reference reads are bound to sales.drafts.create (Program.cs,
 * SalesReferenceReadBindings), so the screen may not be able to re-read the owner for the reader: the block
 * then shows the stored id (never a guessed or copied name). Either the owner's text or the stored id is
 * honest; a name from nowhere is not.
 */
async function expectDeliveryBlockAsReader(page: Page): Promise<void> {
  await expect(page.getByTestId('sales-delivery-block')).toBeVisible()
  await expect(page.getByTestId('sales-delivery-address-text')).toContainText(
    new RegExp(`${escapeRegExp(deliveryAddressText)}|${deliveryAddressId}`),
  )
  await expect(page.getByTestId('sales-delivery-contact-text')).toContainText(
    new RegExp(`${escapeRegExp(deliveryContactName)}|${deliveryContactId}`),
  )
}

test('S04-06 the preparer sees the REAL currency, delivery address and contact options on the new-draft screen; no save (E1)', async ({ page }) => {
  await signInAs(page, preparerActor)
  const currencyReads = watchRead(page, CURRENCIES_PATH)
  const contactReads = watchRead(page, CONTACT_INFORMATION_PATH)

  await page.goto(CREATE_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni satış siparişi taslağı' })).toBeVisible({ timeout: 30_000 })

  // The REAL customer the seed opened is offered by the real accounts read; its option carries both ids. The disposable
  // database carries the C01 golden fixture (52 active GDC accounts) before the seed, so the seeded customer is NOT on
  // the first page of 50 (#361, first s04 run): the screen reaches it the two ways the API offers — the server's own
  // cursor ("Daha fazla cari") and the server's own search (`q`). Both are exercised; nothing is filtered on the client.
  const customer = page.getByLabel('Cari ve şirket hesabı')
  const accountReads = watchRead(page, ACCOUNTS_PATH)
  await expect(page.getByTestId('sales-customer-more')).toBeVisible({ timeout: 30_000 })
  await expect(customer.locator('option', { hasText: 'Teslimat müşterisi' })).toHaveCount(0)
  await page.getByTestId('sales-customer-more').click()
  await expect(customer.locator('option', { hasText: 'Teslimat müşterisi' })).toHaveCount(1, { timeout: 15_000 })
  expect(accountReads.some((answer) => new URL(answer.url()).searchParams.has('cursor')), 'the continuation sent the server cursor').toBe(true)
  // The search: a new first page from the server for `q`; the seeded customer is the one match.
  await page.getByLabel('Cari ara').fill('Teslimat müşterisi')
  await page.getByTestId('sales-customer-search-button').click()
  await expect(customer.locator('option', { hasText: 'Teslimat müşterisi' })).toHaveCount(1, { timeout: 15_000 })
  await expect(customer.locator('option')).toHaveCount(2)
  expect(accountReads.some((answer) => new URL(answer.url()).searchParams.get('q') === 'Teslimat müşterisi'), 'the search sent the server q').toBe(true)
  for (const answer of accountReads) expect(answer.status()).toBe(200)
  await customer.selectOption(`${realPartyId}|${realCompanyAccountId}`)

  // Currency: the real Settings candidates revalidated for the BUSINESS DATE. Until a date is entered the screen keeps the
  // select idle and says so (run 35483437287 at 03815a6b: the spec expected TRY on the idle select — the spec's omission,
  // the screen's rule). The date is the seed's own business date, for which the seed already proved TRY valid.
  await expect(currencySelect(page)).toBeVisible()
  await expect(page.getByLabel('Para birimi')).toBeVisible()
  await expect(currencySelect(page)).toBeDisabled()
  await expect(page.getByTestId('sales-currency-status')).toHaveText(currencyIdleText)
  await page.getByLabel('Sipariş tarihi').fill(businessDate)
  await expect(currencySelect(page)).toBeEnabled({ timeout: 15_000 })
  await expect(currencySelect(page).locator('option', { hasText: currencyCode })).toHaveCount(1)
  await currencySelect(page).selectOption({ label: currencyCode })
  expect(currencyReads.length, 'the browser read the real currencies reference').toBeGreaterThan(0)
  expect(currencyReads.some((answer) => new URL(answer.url()).searchParams.get('effectiveOn') === businessDate), 'the read was for the business date').toBe(true)
  for (const answer of currencyReads) expect(answer.status()).toBe(200)

  // Delivery address and contact: the real Cari contact information of the chosen account, by text.
  await expect(page.getByLabel('Teslimat adresi')).toBeVisible()
  await expect(page.getByLabel('Teslimat kişisi')).toBeVisible()
  await expect(addressSelect(page).locator('option', { hasText: 'Sanayi Cad. 1' })).toHaveCount(1)
  await expect(contactSelect(page).locator('option', { hasText: 'Ayşe Yükleme' })).toHaveCount(1)
  await addressSelect(page).selectOption(deliveryAddressId)
  await contactSelect(page).selectOption(deliveryContactId)
  await expect(addressSelect(page)).toHaveValue(deliveryAddressId)
  await expect(contactSelect(page)).toHaveValue(deliveryContactId)
  expect(contactReads.length, 'the browser read the real contact-information reference').toBeGreaterThan(0)
  for (const answer of contactReads) expect(answer.status()).toBe(200)
  const contactAnswer = (await contactReads[contactReads.length - 1].json()) as {
    readonly companyId: string
    readonly addresses: readonly { readonly id: string; readonly text: string }[]
    readonly contacts: readonly { readonly id: string; readonly displayName: string }[]
  }
  expect(contactAnswer.companyId).toBe(companyId)
  expect(contactAnswer.addresses.find((address) => address.id === deliveryAddressId)?.text).toContain(deliveryAddressText)
  expect(contactAnswer.contacts.find((contact) => contact.id === deliveryContactId)?.displayName).toBe(deliveryContactName)

  // E1: no product list on main — the lines and the save are withheld with the honest notice; nothing is sent.
  await expect(page.getByTestId('sales-references-missing')).toBeVisible()
  await expect(saveButton(page)).toHaveCount(0)
  const drafts = await page.request.get(`${env.apiBaseUrl}${DRAFTS_PATH}?approvalState=Preparing`, {
    headers: { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': companyId },
  })
  expect(drafts.status()).toBe(200)
  const listed = (await drafts.json()) as { readonly items: readonly { readonly id: string }[] }
  expect(listed.items.filter((item) => item.id !== deliveryDraftId).length, 'the screen created nothing').toBe(0)
})

test('S04-07 the preparer reopens draft 2 and sees the REAL address, contact and currency; the reader sees the same and no command', async ({ page }) => {
  await signInAs(page, preparerActor)
  const stored = await readDeliveryDraft(page, preparerActor)
  expect([stored.draft.partyId, stored.draft.companyAccountId, stored.draft.approvalState]).toEqual([
    realPartyId,
    realCompanyAccountId,
    'Preparing',
  ])
  expect(stored.deliveryAddress?.id).toBe(deliveryAddressId)
  expect(stored.contact?.id).toBe(deliveryContactId)
  expect(stored.deliveryAddress?.expectedVersion, 'the owner version is stored as text').toMatch(/^[1-9][0-9]*$/)
  expect(stored.contact?.expectedVersion).toMatch(/^[1-9][0-9]*$/)

  const contactReads = watchRead(page, CONTACT_INFORMATION_PATH)
  const currencyReads = watchRead(page, CURRENCIES_PATH)
  await page.goto(DETAIL_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: /SENTETIK browser-suite:s04 SO-0002/ })).toBeVisible({ timeout: 30_000 })
  await expectDeliveryBlock(page)
  for (const answer of [...contactReads, ...currencyReads]) expect(answer.status()).toBe(200)
  expect(contactReads.length, 'the screen re-read the owner, it did not render a copied name').toBeGreaterThan(0)
  expect(currencyReads.length, 'the currency code is the Settings owner\'s answer').toBeGreaterThan(0)

  // The reader holds only sales.drafts.read: the same facts by URL, no command.
  await signInAs(page, readerActor)
  await readDeliveryDraft(page, readerActor)
  await page.goto(DETAIL_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: /SENTETIK browser-suite:s04 SO-0002/ })).toBeVisible({ timeout: 30_000 })
  await expectDeliveryBlockAsReader(page)
  await expectNoApprovalCommand(page)

  // Reopening changed nothing: the same ids and versions are still stored.
  const after = await readDeliveryDraft(page, readerActor)
  expect(after.deliveryAddress).toEqual(stored.deliveryAddress)
  expect(after.contact).toEqual(stored.contact)
  expect(after.draft.currencyId).toBe(stored.draft.currencyId)
})
