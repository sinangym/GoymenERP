/**
 * Sales s04 — AT-018 / AT-021 against the REAL stack (user decision 2026-09-30): a Product price list is created on the U-05
 * screen, saved, closed and reopened, published by a different authorised actor; then an order line picks the list price as its REFERENCE (and default),
 * gets a discount and a maturity surcharge (vade farkı), is saved, and reopening shows the reference, the adjustments and the
 * server's net. A net below the list floor is saved as pending maker-checker approval and blocks submission until another
 * narrowly authorised actor approves the immutable snapshot with a reason.
 *
 * Real Chromium, the exact-head web build, the real API and SQL Server. NOTHING IS MOCKED OR INTERCEPTED.
 *
 * SOURCE-ONLY, NOT RUN in this authoring slice. The central s04 binding must keep the existing actors distinct: PREPARER gets
 * Product price-list read/write but not publish plus the narrow Sales floor-exception name only for the authorised-self refusal;
 * READER has no floor-exception name; APPROVER gets Product price-list read/publish plus Sales price read and the same narrow
 * floor-exception name. No actor, role or production grant is created here. Until that closed binding exists,
 * this file fails at its first capability assertion — it never tests something else.
 *
 * The cases are one serial journey, in file order:
 *   S04-PL-01 the preparer creates two touching tiers for the seed's REAL product in the browser ([0,10) at 42000.50 and
 *             [10,20) at 43000, both floor 40000, from today), saves, closes and reopens them as a Draft; the different
 *             existing approver publishes it;
 *             it reads "Geçerli"
 *   S04-PL-02 the preparer creates an order of exactly 10 for today through the real API, opens it, fetches the second tier and uses it
 *             (reference + default), gives 3 % discount and 1.5 % surcharge, saves; close/reopen shows the reference, "Liste
 *             fiyatı", [10,20), %3, %1.5 and the server's net 42335.65
 *   S04-PL-03 a 10 % discount (net 38700 < floor 40000) is saved pending; submit and self-approval are refused, the existing
 *             different approver records a reason, fresh reopen shows it, and same-key/same-body replay has one effect
 */

import { randomUUID } from 'node:crypto'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { apiBaseUrl, testedCommit, seed } = env
const { preparerActor, approverActor, readerActor, companyId, realPartyId, realCompanyAccountId, productId, productVersion, productUnitId, productCode } = seed

const RUN = testedCommit.slice(0, 12)
const TODAY = new Date().toISOString().slice(0, 10)
const LIST_NAME = `SENTETIK s04 fiyat listesi ${RUN}`
const DRAFTS_PATH = '/api/v1/sales/drafts'
const CURRENCIES_PATH = '/api/v1/sales/references/currencies'
const CAPABILITIES_PATH = '/api/v1/shell/capabilities'
// S04BrowserSeed.PreparerIdentityId: the authenticated actor persisted by Sales, not the external development subject.
const PREPARER_IDENTITY_ID = 'b2000000-0000-4000-8000-000000000401'

const journey: { priceListId?: string; draftId?: string; lineId?: string } = {}

test.describe.configure({ mode: 'serial' })

const shellNavigation = (page: Page) => page.getByRole('navigation', { name: 'Modül navigasyonu' })

async function expectExactShellNavigation(page: Page, viewport: { width: number; height: number }): Promise<void> {
  await page.setViewportSize(viewport)
  await page.setExtraHTTPHeaders({ 'X-Development-User': preparerActor })

  await page.goto('/satis/siparisler/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni satış siparişi taslağı' })).toBeVisible()
  await expect(shellNavigation(page).getByRole('link', { name: 'Sipariş Taslakları' })).toBeVisible()
  await expect(shellNavigation(page).getByRole('link', { name: 'Yeni Sipariş Taslağı' })).toHaveCount(0)
  await expect(shellNavigation(page).getByRole('link', { name: 'Fiyat listeleri' })).toBeVisible()
  await expect(shellNavigation(page).locator('[aria-current="page"]')).toHaveCount(0)

  await page.goto('/satis/siparisler')
  await expect(page.getByRole('heading', { level: 1, name: 'Satış siparişi taslakları' })).toBeVisible()
  await expect(page.getByRole('main').getByRole('link', { name: /yeni sipariş taslağı/i })).toHaveCount(1)
  await expect(shellNavigation(page).getByRole('link', { name: 'Yeni Sipariş Taslağı' })).toHaveCount(0)
  await expect(shellNavigation(page).locator('[aria-current="page"]')).toHaveCount(1)
  await expect(shellNavigation(page).getByRole('link', { name: 'Sipariş Taslakları' })).toHaveAttribute('aria-current', 'page')

  await page.goto('/fiyat-listeleri')
  await expect(page.getByRole('heading', { level: 1, name: 'Fiyat listeleri' })).toBeVisible()
  await expect(shellNavigation(page).locator('[aria-current="page"]')).toHaveCount(1)
  await expect(shellNavigation(page).getByRole('link', { name: 'Fiyat listeleri' })).toHaveAttribute('aria-current', 'page')
}

test('S04-NAV-01 exact Sales and price-list entry points stay correct on desktop and mobile', async ({ page, request }) => {
  const preparerCapabilities = await capabilitiesOf(request, preparerActor)
  expect(preparerCapabilities).toEqual(expect.arrayContaining([
    'sales.drafts.list', 'sales.drafts.create', 'price-lists.list',
  ]))
  const readerCapabilities = await capabilitiesOf(request, readerActor)
  expect(readerCapabilities).toContain('sales.drafts.list')
  expect(readerCapabilities).not.toContain('price-lists.list')

  await expectExactShellNavigation(page, { width: 1400, height: 900 })
  await expectExactShellNavigation(page, { width: 390, height: 844 })

  await page.setExtraHTTPHeaders({ 'X-Development-User': readerActor })
  await page.goto('/satis/siparisler')
  await expect(shellNavigation(page).getByRole('link', { name: 'Sipariş Taslakları' })).toBeVisible()
  await expect(shellNavigation(page).getByRole('link', { name: 'Fiyat listeleri' })).toHaveCount(0)
})

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
  const preparerCapabilities = await capabilitiesOf(request, preparerActor)
  expect(preparerCapabilities).toEqual(expect.arrayContaining([
    'price-lists.list', 'price-lists.create', 'price-lists.detail', 'price-lists.edit',
  ]))
  expect(preparerCapabilities).not.toContain('price-lists.publish')
  expect(await capabilitiesOf(request, approverActor)).toEqual(expect.arrayContaining([
    'price-lists.list', 'price-lists.detail', 'price-lists.publish',
  ]))

  await page.setExtraHTTPHeaders({ 'X-Development-User': preparerActor })
  await page.goto('/fiyat-listeleri/yeni')
  await page.getByLabel('Liste adı').fill(LIST_NAME)
  await expect(page.getByLabel('Para birimi (ISO, ör. TRY)')).toHaveValue('TRY')
  await page.getByLabel('Geçerlilik başlangıcı').fill(TODAY)
  await page.getByLabel('Ürün ara (kod veya ad)').fill(productCode)
  await page.getByTestId('price-list-lines').getByRole('button', { name: 'Ara', exact: true }).click()
  await page.getByTestId('price-list-product-options').getByRole('button', { name: 'Listeye ekle' }).first().click()
  await page.getByTestId('price-list-product-options').getByRole('button', { name: 'Listeye ekle' }).first().click()
  await page.getByLabel('1. Liste fiyatı').fill('42000,50')
  await page.getByLabel('1. Alt fiyat (isteğe bağlı)').fill('40000')
  await page.getByLabel('1. Alt miktar (dahil)').fill('0')
  await page.getByLabel('1. Üst miktar (hariç, boş: sınırsız)').fill('10')
  await page.getByLabel('2. Liste fiyatı').fill('43000')
  await page.getByLabel('2. Alt fiyat (isteğe bağlı)').fill('40000')
  await page.getByLabel('2. Alt miktar (dahil)').fill('10')
  await page.getByLabel('2. Üst miktar (hariç, boş: sınırsız)').fill('20')

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
  await expect(page.getByTestId('price-list-line-quantity-tier')).toHaveText(['0 ≤ miktar < 10', '10 ≤ miktar < 20'])
  await expect(page.getByTestId('price-list-publish')).toHaveCount(0)

  const detailUrl = page.url()
  await page.setExtraHTTPHeaders({ 'X-Development-User': approverActor })
  await page.goto(detailUrl)
  await expect(page.getByTestId('price-list-version-1-status')).toHaveText('Taslak')
  await page.getByTestId('price-list-publish').click()
  await expect(page.getByTestId('price-list-version-1-status')).toHaveText('Geçerli')
  await page.reload()
  await expect(page.getByTestId('price-list-version-1-status')).toHaveText('Geçerli')
  await expect(page.getByTestId('price-list-line-quantity-tier')).toHaveText(['0 ≤ miktar < 10', '10 ≤ miktar < 20'])
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
        lineId, productId, expectedProductVersion: productVersion, orderUnitId: productUnitId, quantity: '10',
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
  await expect(form.getByLabel('Birim fiyat')).toHaveValue('43000')
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
  await expect(page.getByTestId('sales-line-1-reference')).toContainText('miktar 10 dahil, 20 hariç')
  await expect(page.getByTestId('sales-line-1-net')).toHaveText('42335.65')

  const pricing = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/line-pricing`, { headers: readHeaders(preparerActor) })
  expect(pricing.status()).toBe(200)
  expect(((await pricing.json()) as { readonly lines: readonly Record<string, unknown>[] }).lines[0]).toMatchObject({
    lineId, unitPrice: '43000', priceSource: 'Reference', discountPercent: '3', surchargePercent: '1.5', netUnitPrice: '42335.65',
    priceReference: {
      priceListId: journey.priceListId, versionNo: 1, currencyCode: 'TRY', floorUnitPrice: '40000',
      unitPrice: '43000', lowerQuantity: '10', upperQuantity: '20',
    },
  })
})

test('S04-PL-03 below-floor pricing is saved pending and a different actor approves it once with a reason', async ({ page, request }, testInfo) => {
  let safeFailurePhase = 's04_pl03_p1'
  try {
  expect(journey.draftId, 'S04-PL-02 priced the order').toBeTruthy()
  expect(journey.lineId, 'S04-PL-02 retained the line identity').toBeTruthy()
  expect(await capabilitiesOf(request, preparerActor)).toContain('sales.orders.price-floor-exception.approve')
  expect(await capabilitiesOf(request, readerActor)).not.toContain('sales.orders.price-floor-exception.approve')
  expect(await capabilitiesOf(request, approverActor)).toEqual(expect.arrayContaining([
    'sales.drafts.detail', 'sales.orders.price-floor-exception.approve',
  ]))

  safeFailurePhase = 's04_pl03_p2'
  await page.setExtraHTTPHeaders({ 'X-Development-User': preparerActor })
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  const form = page.getByTestId('sales-price-revision-form')
  await form.getByLabel('İskonto %').fill('10')
  await form.getByLabel('Vade farkı', { exact: true }).fill('')
  const savedAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
    && new URL(response.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/commercial-revision`)
  await form.getByRole('button', { name: 'Fiyat ve iskontoyu kaydet' }).click()
  expect((await savedAnswer).status()).toBe(200)
  await expect(page.getByText('Fiyat revizyonu kaydedildi.')).toBeVisible()
  await expect(page.getByText('Farklı yetkilinin gerekçeli onayı bekleniyor')).toBeVisible()

  // Close and reopen: the server-owned pending snapshot, not local form state, is shown.
  await page.goto('/satis/siparisler')
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  await expect(page.getByText('Farklı yetkilinin gerekçeli onayı bekleniyor')).toBeVisible()
  const pendingDetail = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, { headers: readHeaders(preparerActor) })
  expect(pendingDetail.status()).toBe(200)
  const pendingDraft = (await pendingDetail.json()) as { readonly draft: { readonly version: string } }
  const pendingPricingResponse = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/line-pricing`, {
    headers: readHeaders(preparerActor),
  })
  expect(pendingPricingResponse.status()).toBe(200)
  const pendingPricing = (await pendingPricingResponse.json()) as {
    readonly lines: readonly { readonly lineId: string; readonly netUnitPrice: string; readonly floorException: {
      readonly requestedBy: string; readonly approvedBy: string | null; readonly reason: string | null
    } | null }[]
  }
  expect(pendingPricing.lines[0]).toMatchObject({
    lineId: journey.lineId, netUnitPrice: '38700',
    floorException: { requestedBy: PREPARER_IDENTITY_ID, approvedBy: null, reason: null },
  })

  safeFailurePhase = 's04_pl03_p3'
  // Price authority is proven at the endpoint, not inferred from a non-existent shell capability.
  const approverPricingResponse = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/line-pricing`, {
    headers: readHeaders(approverActor),
  })
  expect(approverPricingResponse.status()).toBe(200)
  const approverPricing = (await approverPricingResponse.json()) as {
    readonly lines: readonly { readonly lineId: string; readonly netUnitPrice: string;
      readonly floorException: { readonly requestedBy: string; readonly approvedBy: string | null; readonly reason: string | null } | null }[]
  }
  expect(approverPricing.lines[0]).toMatchObject({
    lineId: journey.lineId, netUnitPrice: '38700',
    floorException: { requestedBy: PREPARER_IDENTITY_ID, approvedBy: null, reason: null },
  })

  const readerPricingResponse = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/line-pricing`, {
    headers: readHeaders(readerActor),
  })
  expect(readerPricingResponse.status()).toBe(403)

  const approverDetailResponse = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, {
    headers: readHeaders(approverActor),
  })
  expect(approverDetailResponse.status()).toBe(200)
  const approverDetail = (await approverDetailResponse.json()) as {
    readonly lines: readonly { readonly lineId: string; readonly priceDisclosure: string;
      readonly unitPrice: string | null; readonly priceUnitId: string | null; readonly discountPercent: string | null }[]
  }
  expect(approverDetail.lines[0]).toMatchObject({
    lineId: journey.lineId, priceDisclosure: 'Visible', unitPrice: '43000', priceUnitId: productUnitId, discountPercent: '10',
  })

  const readerDetailResponse = await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, {
    headers: readHeaders(readerActor),
  })
  expect(readerDetailResponse.status()).toBe(200)
  const readerDetail = (await readerDetailResponse.json()) as {
    readonly lines: readonly { readonly lineId: string; readonly priceDisclosure: string;
      readonly unitPrice: string | null; readonly priceUnitId: string | null; readonly discountPercent: string | null }[]
  }
  expect(readerDetail.lines[0]).toMatchObject({
    lineId: journey.lineId, priceDisclosure: 'Withheld', unitPrice: null, priceUnitId: null, discountPercent: null,
  })

  safeFailurePhase = 's04_pl03_p4'
  // Pending floor approval blocks the ordinary submission.
  const submissionAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
    && new URL(response.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/submission`)
  await page.getByTestId('sales-approval-commands').getByRole('button', { name: 'Onaya gönder' }).click()
  const blockedSubmission = await submissionAnswer
  expect(blockedSubmission.status()).toBe(422)
  expect(((await blockedSubmission.json()) as { readonly errors: readonly { readonly code: string }[] }).errors)
    .toEqual(expect.arrayContaining([expect.objectContaining({ code: 'sales_price_floor_exception_pending' })]))

  const reason = `SENTETIK s04 taban fiyat onayı ${RUN}`
  const approvalBody = {
    expectedVersion: pendingDraft.draft.version,
    lines: [{ lineId: journey.lineId, reason }],
  }
  const unauthorizedApproval = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/below-floor-exception-approval`, {
    headers: commandHeaders(readerActor, `SENTETIK-s04-pl-floor-denied-${RUN}`), data: approvalBody,
  })
  expect(unauthorizedApproval.status()).toBe(403)
  const afterUnauthorizedVersion = ((await (await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, {
    headers: readHeaders(preparerActor),
  })).json()) as { readonly draft: { readonly version: string } }).draft.version
  expect(afterUnauthorizedVersion).toBe(pendingDraft.draft.version)

  // Authorised at the transport fence, but still the immutable snapshot's requester: the domain refuses maker = checker.
  const selfApproval = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/below-floor-exception-approval`, {
    headers: commandHeaders(preparerActor, `SENTETIK-s04-pl-floor-self-${RUN}`), data: approvalBody,
  })
  expect(selfApproval.status()).toBe(422)
  expect(((await selfApproval.json()) as { readonly errors: readonly { readonly code: string }[] }).errors)
    .toEqual(expect.arrayContaining([expect.objectContaining({ code: 'sales_price_floor_exception_same_actor' })]))
  const afterSelfVersion = ((await (await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, {
    headers: readHeaders(preparerActor),
  })).json()) as { readonly draft: { readonly version: string } }).draft.version
  expect(afterSelfVersion).toBe(pendingDraft.draft.version)

  safeFailurePhase = 's04_pl03_p5'
  // The existing different actor sees the real pending snapshot and supplies the required reason in the UI.
  await page.setExtraHTTPHeaders({ 'X-Development-User': approverActor })
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  await expect(page.getByRole('heading', { name: 'Taban fiyat istisna onayı' })).toBeVisible()
  await page.getByLabel('1. satır onay gerekçesi').fill(reason)
  const approvalAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
    && new URL(response.url()).pathname === `${DRAFTS_PATH}/${journey.draftId}/below-floor-exception-approval`)
  await page.getByRole('button', { name: 'İstisnaları onayla' }).click()
  const approved = await approvalAnswer
  expect(approved.status()).toBe(200)
  expect(approved.headers()['idempotency-replayed']).not.toBe('true')
  await expect(page.getByText(`Onaylandı: ${reason}`)).toBeVisible()

  safeFailurePhase = 's04_pl03_p6'
  const firstVersion = ((await (await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, {
    headers: readHeaders(approverActor),
  })).json()) as { readonly draft: { readonly version: string } }).draft.version
  const browserRequest = approved.request()
  const replayKey = browserRequest.headers()['idempotency-key']
  expect(replayKey, 'the browser approval carries its stable idempotency key').toBeTruthy()
  const replay = await request.post(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}/below-floor-exception-approval`, {
    headers: commandHeaders(approverActor, replayKey), data: browserRequest.postDataJSON(),
  })
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  const replayVersion = ((await (await request.get(`${apiBaseUrl}${DRAFTS_PATH}/${journey.draftId}`, {
    headers: readHeaders(approverActor),
  })).json()) as { readonly draft: { readonly version: string } }).draft.version
  expect(replayVersion).toBe(firstVersion)

  safeFailurePhase = 's04_pl03_p7'
  await page.setExtraHTTPHeaders({ 'X-Development-User': preparerActor })
  await page.goto('/satis/siparisler')
  await page.goto(`/satis/siparisler/${journey.draftId}`)
  await expect(page.getByText(`Onaylandı: ${reason}`)).toBeVisible()
  } catch (error) {
    testInfo.annotations.push({ type: 'safe_failure_phase', description: safeFailurePhase })
    throw error
  }
})
