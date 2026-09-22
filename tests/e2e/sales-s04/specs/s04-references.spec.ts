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
 * E1 adds one isolated Product-owned sales candidate behind the existing complete fixture gate. The browser
 * searches it through the real Product-backed Sales reference endpoint and creates a draft through the real
 * Sales command. The Product fixture is not normal-host data and grants no permission. Nothing here shows or
 * sends a price.
 *
 * Runs after the journey (Playwright runs files alphabetically: `s04-approval-journey` < `s04-references`):
 *   S04-06 the preparer opens the new-draft screen, picks the REAL customer, currency, delivery address and contact,
 *          searches the isolated Product-owned candidate, creates one draft and reopens that exact draft
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
  productId,
  productVersion,
  productUnitId,
  productUnitProfileId,
  productUnitProfileVersion,
  productCode,
  productName,
} = env.seed
// Address/contact/currency labels are deterministic seed facts. Product identity, owner versions and labels are
// exported only after the seed has read the real Product-backed Sales endpoint and matched the exact fixture tuple.
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
const PRODUCTS_PATH = '/api/v1/sales/references/products'
const CREATE_ROUTE = '/satis/siparisler/yeni'
const LIST_ROUTE = '/satis/siparisler'
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

interface CreatedDraftReading extends DeliveryDraftReading {
  readonly lines: readonly {
    readonly productId: string
    readonly productVersion: string
    readonly productCode: string
    readonly productName: string
    readonly orderUnitId: string
  }[]
}

interface DraftMutationReading {
  readonly detail: CreatedDraftReading
  readonly isReplay: boolean
}

interface ProductReferenceReading {
  readonly items: readonly {
    readonly productId: string
    readonly productVersion: string
    readonly code: string
    readonly name: string
    readonly orderUnits: readonly {
      readonly unitId: string
      readonly profileId: string
      readonly profileVersion: string
    }[]
  }[]
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

test('S04-06 the preparer selects the real references and Product candidate, creates one draft and reopens the same record', async ({ page }) => {
  await signInAs(page, preparerActor)
  const currencyReads = watchRead(page, CURRENCIES_PATH)
  const contactReads = watchRead(page, CONTACT_INFORMATION_PATH)
  const productReads = watchRead(page, PRODUCTS_PATH)

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

  // E1: search and select the Product-owned disposable candidate through the real bounded endpoint.
  await page.getByLabel('Ürün ara').fill(productCode)
  const searchedPromise = page.waitForResponse((response) => {
    const url = new URL(response.url())
    return response.request().method() === 'GET'
      && url.pathname === PRODUCTS_PATH
      && url.searchParams.get('q') === productCode
      && url.searchParams.get('effectiveOn') === businessDate
  })
  await page.getByTestId('sales-product-search-button').click()
  const searched = await searchedPromise
  expect(searched.status()).toBe(200)
  const productAnswer = (await searched.json()) as ProductReferenceReading
  expect(productAnswer.items).toEqual([
    {
      productId,
      productVersion,
      code: productCode,
      name: productName,
      orderUnits: [
        expect.objectContaining({
          unitId: productUnitId,
          profileId: productUnitProfileId,
          profileVersion: productUnitProfileVersion,
        }),
      ],
    },
  ])

  const productSelect = page.getByLabel('Ürün', { exact: true }).first()
  await expect(productSelect.locator(`option[value="${productId}"]`)).toHaveCount(1, { timeout: 15_000 })
  await productSelect.selectOption(productId)
  await page.getByLabel('Miktar', { exact: true }).first().fill('1')
  const unitSelect = page.getByLabel('Sipariş birimi', { exact: true }).first()
  await expect(unitSelect.locator(`option[value="${productUnitId}"]`)).toHaveCount(1)
  await unitSelect.selectOption(productUnitId)

  expect(productReads).toContain(searched)

  const createdResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === DRAFTS_PATH)
  await saveButton(page).click()
  const createdResponse = await createdResponsePromise
  expect(createdResponse.status()).toBe(201)
  const created = (await createdResponse.json()) as DraftMutationReading
  expect(created.isReplay).toBe(false)
  expect(created.detail.lines).toHaveLength(1)
  expect(created.detail.lines[0]).toMatchObject({
    productId,
    productVersion,
    productCode,
    productName,
    orderUnitId: productUnitId,
  })
  const createdId = created.detail.draft.id
  await expect(page.getByRole('heading', { level: 1, name: 'Sipariş taslağı kaydedildi.' })).toBeVisible()

  // Close the terminal screen, then reopen the exact server-created record in the next operation.
  await page.getByRole('link', { name: 'Sipariş taslakları' }).click()
  await expect(page).toHaveURL(new RegExp(`${LIST_ROUTE}$`))
  await page.goto(`${LIST_ROUTE}/${createdId}`)
  await expect(page.getByRole('heading', { level: 3, name: `1. ${productCode} — ${productName}` })).toBeVisible({ timeout: 30_000 })

  const storedResponse = await page.request.get(`${env.apiBaseUrl}${DRAFTS_PATH}/${createdId}`, {
    headers: { [DEVELOPMENT_USER_HEADER]: preparerActor, 'X-Company-Id': companyId },
  })
  expect(storedResponse.status()).toBe(200)
  const stored = (await storedResponse.json()) as CreatedDraftReading
  expect(stored.draft.id).toBe(createdId)
  expect(stored.lines).toHaveLength(1)
  expect(stored.lines[0]).toMatchObject({
    productId,
    productVersion,
    productCode,
    productName,
    orderUnitId: productUnitId,
  })
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
