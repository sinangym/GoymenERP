import { expect, test, type Page } from '@playwright/test'
import { ActorProbe } from '../cari-acceptance-009/src/lHarness'

/**
 * Product u02 — the fixture's proof on the REAL API, nothing mocked or intercepted:
 *
 *  - U02-FIX-01 the authorized actor (Product master Read / DraftCreate / DraftBasicEdit from the u02
 *    fixture, issued by the suite's synthetic issuer) opens the new-product screen, is offered the
 *    fixture's synthetic code group by the real code-group read, and saves a draft: the real create
 *    answers 201 and the screen prints the server's code, whose prefix is the fixture group's.
 *  - U02-FIX-02 the unauthorized actor (company-a-user: no Product master authority) is closed out of the
 *    Product screens and sends no Product request.
 *  - Product history is read from the real endpoint after closing and reopening the card. The authorized actor
 *    holds no Activate authority, so no Activate row is written here. The real activation (U-01-360, #466)
 *    runs in u01-360-activation-journey.spec.ts as the disposable u02 activator, the only actor this run's
 *    fixture grants Product master Activate (user decision 2026-09-27); no real user or seeded role holds it.
 *  - The fixture has one authorized company context. The history 404 and authority 403 boundaries run here;
 *    a cross-company history negative remains explicitly NOT_RUN rather than being faked with headers.
 *
 * This is the owner suite's plumbing proof (the fixture, the issuer binding, the gated writers). The U-02
 * cases of the open PRs (#270, #274) run as this suite at their own exact heads.
 */

type Seed = {
  readonly companyId: string
  readonly codeGroupId: string
  readonly authorizedActor: string
  readonly unauthorizedActor: string
  /** Supplied only by the gated u02 SYNTHETIC release fixture; never a real Q6 release. */
  readonly technicalFamilyId: string
}

function seed(): Seed {
  const raw = process.env.BROWSER_E2E_U02_SEED?.trim()
  if (!raw) throw new Error('BROWSER_E2E_U02_SEED is not set; the u02 fixture did not run.')
  const value = JSON.parse(raw) as Seed
  for (const key of ['companyId', 'codeGroupId', 'authorizedActor', 'unauthorizedActor', 'technicalFamilyId'] as const) {
    if (typeof value[key] !== 'string' || value[key] === '') throw new Error(`BROWSER_E2E_U02_SEED.${key} is missing.`)
  }
  return value
}

const PRODUCTS_PATH = '/api/v1/products'
const unauthorizedBaseUrl = process.env.BROWSER_E2E_UNAUTHORIZED_BASE_URL?.trim() ?? ''
const apiBaseUrl = process.env.BROWSER_E2E_API_BASE_URL?.trim() ?? ''
const MISSING_PRODUCT_ID = 'ffffffff-ffff-4fff-8fff-ffffffffffff'

type HistoryEntry = {
  readonly eventId: string
  readonly operation: string
  readonly occurredAtUtc: string
  readonly versionBefore: string | null
  readonly versionAfter: string
  readonly reason: string
  readonly changedFields: string[]
}

type ProductHistory = {
  readonly productId: string
  readonly entries: HistoryEntry[]
  readonly truncated: boolean
}

type FamilyFacts = {
  readonly cins: string
  readonly tur: string
  readonly tip: string
}

type UnitReferences = {
  readonly stock: string | null
  readonly purchase: string | null
  readonly sales: string | null
  readonly price: string | null
  readonly production: string | null
  readonly package: string | null
}

type ProductCard = {
  readonly product: {
    readonly id: string
    readonly version: string
    readonly labels: { readonly familyFacts: FamilyFacts | null }
  }
  readonly references: {
    readonly templateId: string | null
    readonly templateVersion: string | null
    readonly attributes: Record<string, unknown>
    readonly units: UnitReferences
  }
}

const CLOSED_UNIT_ROLES = ['price', 'production', 'package'] as const
const CLOSED_UNIT_LABELS = ['Fiyat birimi', 'Üretim birimi', 'Paket birimi'] as const

/** The read-only value paired with an exact definition-list term. */
function definitionOf(page: Page, term: string) {
  const exact = new RegExp(`^${term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`)
  return page.locator('dt', { hasText: exact }).locator('xpath=following-sibling::dd[1]')
}

async function expectFamilyFacts(page: Page, facts: FamilyFacts): Promise<void> {
  await expect(definitionOf(page, 'Cins')).toHaveText(facts.cins)
  await expect(definitionOf(page, 'Tür')).toHaveText(facts.tur)
  await expect(definitionOf(page, 'Tip')).toHaveText(facts.tip)
}

/** Closed roles remain readable on the card, but this form must never offer an authoring control for them. */
async function expectClosedUnitRolesNotEditable(page: Page): Promise<void> {
  for (const label of CLOSED_UNIT_LABELS) {
    await expect(page.getByRole('combobox', { name: label, exact: true })).toHaveCount(0)
  }
}

function expectClosedUnitRolesNull(units: UnitReferences): void {
  for (const role of CLOSED_UNIT_ROLES) expect(units[role], `${role} remains null`).toBeNull()
}

function expectClosedUnitRolesOmitted(units: Record<string, unknown>): void {
  for (const role of CLOSED_UNIT_ROLES) expect(units, `${role} is not written`).not.toHaveProperty(role)
}

async function expectClosedUnitRolesUnassigned(page: Page): Promise<void> {
  for (const role of CLOSED_UNIT_ROLES) {
    await expect(page.getByTestId(`product-unit-${role}`), `${role} never falls back to stock`).toHaveText('atanmadı')
  }
}

/** Counts the browser's own Product traffic (reads and writes), with the write statuses. */
function watchProducts(page: Page): { reads: number; writes: number; writeStatuses: number[] } {
  const traffic = { reads: 0, writes: 0, writeStatuses: [] as number[] }
  page.on('request', (request) => {
    if (!new URL(request.url()).pathname.startsWith(PRODUCTS_PATH)) return
    if (request.method() === 'GET') traffic.reads += 1
    else traffic.writes += 1
  })
  page.on('response', (response) => {
    const request = response.request()
    if (request.method() !== 'GET' && new URL(request.url()).pathname.startsWith(PRODUCTS_PATH)) {
      traffic.writeStatuses.push(response.status())
    }
  })
  return traffic
}

/**
 * Reuses one request made by the real browser application so the fixture's actor and
 * committed-company headers remain intact. A raw page.request call carries neither and
 * only proves the anonymous 401 boundary.
 */
async function redirectNextApplicationRead(page: Page, requestPattern: string, targetPath: string): Promise<number> {
  let status: number | null = null
  await page.route(requestPattern, async (route) => {
    if (status !== null) return route.fallback()
    const target = new URL(route.request().url())
    target.pathname = targetPath
    target.search = ''
    const response = await route.fetch({ url: target.toString() })
    status = response.status()
    await route.fulfill({ response })
  })
  try {
    await page.reload()
    await expect.poll(() => status, { timeout: 30_000 }).not.toBeNull()
    return status!
  } finally {
    await page.unroute(requestPattern)
  }
}

test('U02-FIX-01 the authorized actor creates, edits and reopens technical fields and history through the real API', async ({ page }) => {
  const { codeGroupId, technicalFamilyId } = seed()
  const traffic = watchProducts(page)
  const groupsAnswer = page.waitForResponse((response) =>
    new URL(response.url()).pathname === `${PRODUCTS_PATH}/code-groups` && response.request().method() === 'GET')

  await page.goto('/urun')
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible({ timeout: 30_000 })
  await page.getByRole('main').getByRole('link', { name: 'Yeni ürün', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible({ timeout: 30_000 })
  const groups = await groupsAnswer
  expect(groups.status(), 'the real code-group read').toBe(200)
  const offered = (await groups.json()) as { id: string; prefix: string }[]
  const group = offered.find((item) => item.id === codeGroupId)
  expect(group, 'the fixture code group is offered by the real read').toBeDefined()

  const name = `SENTETIK u02 fixture proof ${Date.now()}`
  await page.getByLabel('Ürün adı').fill(name)
  const optionsAnswer = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/reference-options`)
  await page.getByLabel('Kod grubu').selectOption(codeGroupId)
  const optionsResponse = await optionsAnswer
  expect(optionsResponse.status()).toBe(200)
  const options = await optionsResponse.json() as {
    familyFacts: { id: string; templateId: string; templateVersion: string; cins: string; tur: string; tip: string }[]
    templates: { id: string; version: string; fields: { key: string; kind: string; role: string | null; dictionary: string | null; appliesWhenField: string | null }[] }[]
    dictionaries: { name: string; entries: { code: string; displayName: string; status: string; isExplicitNone: boolean }[] }[]
    units: { id: string; isKilogram: boolean; displayName: string }[]
  }
  const family = options.familyFacts.find(item => item.id === technicalFamilyId)
  expect(family, 'the test-only released family must come from the real registry read').toBeDefined()
  const familyFacts = { cins: family!.cins, tur: family!.tur, tip: family!.tip }
  const template = options.templates.find(item => item.id === family!.templateId && item.version === family!.templateVersion)!
  expect(template).toBeDefined()
  await page.getByLabel('Ürün ailesi').selectOption(technicalFamilyId)
  await expectFamilyFacts(page, familyFacts)
  await expectClosedUnitRolesNotEditable(page)
  const thickness = template.fields.find(field => field.role === 'thickness')!
  expect(thickness).toBeDefined()
  const expectedAttributes: Record<string, number | string> = {}
  let coatingName = ''
  for (const field of template.fields.filter(field => field.appliesWhenField === null)) {
    if (field.kind === 'measureMillimetre') {
      const value = field.role === 'thickness' ? '1.25' : '500'
      await page.getByLabel(`${field.key} (mm)`, { exact: false }).fill(value)
      expectedAttributes[field.key] = Number(value)
    } else if (field.role === 'quality' || field.role === 'coating') {
      const entries = options.dictionaries.find(dictionary => dictionary.name === field.dictionary)!.entries
      const entry = entries.find(item => item.status === 'approved' && (field.role !== 'coating' || item.isExplicitNone))!
      expect(entry).toBeDefined()
      await page.getByLabel(field.key, { exact: false }).selectOption(entry.code)
      expectedAttributes[field.key] = entry.code
      if (field.role === 'coating') coatingName = entry.displayName
    }
  }
  const kilogram = options.units.find(unit => unit.isKilogram)!
  expect(kilogram).toBeDefined()
  await page.getByLabel('Stok birimi (kg)').selectOption(kilogram.id)
  await page.getByLabel('Satış birimi').selectOption(kilogram.id)
  await page.getByLabel('Alış birimi').selectOption(kilogram.id)
  const created = page.waitForResponse((response) =>
    new URL(response.url()).pathname === PRODUCTS_PATH && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Taslak olarak kaydet' }).click()

  const createdResponse = await created
  expect(createdResponse.status(), 'the real create').toBe(201)
  const createdBody = await createdResponse.json() as { card: ProductCard }
  const createdUnits = createdResponse.request().postDataJSON().references.units as Record<string, unknown>
  expect(createdUnits).toEqual({ stock: kilogram.id, purchase: kilogram.id, sales: kilogram.id })
  expectClosedUnitRolesOmitted(createdUnits)
  expect(createdBody.card.references.attributes).toEqual(expectedAttributes)
  expect(createdBody.card.references.templateId).toBe(template.id)
  expect(createdBody.card.references.templateVersion).toBe(template.version)
  expect(createdBody.card.references.units.stock).toBe(kilogram.id)
  expect(createdBody.card.references.units.sales).toBe(kilogram.id)
  expect(createdBody.card.references.units.purchase).toBe(kilogram.id)
  expectClosedUnitRolesNull(createdBody.card.references.units)
  await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
  await expect(page.getByText(new RegExp(`^${group!.prefix}-[0-9]+ — ${name}$`))).toBeVisible()
  const id = createdBody.card.product.id
  await page.goto(`/urun/${id}`)
  await expectFamilyFacts(page, familyFacts)
  await expect(page.getByText(coatingName, { exact: true })).toBeVisible()
  await expect(page.getByTestId('product-unit-stock')).toHaveText(kilogram.displayName)
  await expect(page.getByTestId('product-unit-sales')).toHaveText(kilogram.displayName)
  await expect(page.getByTestId('product-unit-purchase')).toHaveText(kilogram.displayName)
  await expectClosedUnitRolesUnassigned(page)
  await page.locator(`a[href="/urun/${id}/duzenle"]`).click()
  await expectFamilyFacts(page, familyFacts)
  await expectClosedUnitRolesNotEditable(page)
  await expect(page.getByLabel('Stok birimi (kg)')).toHaveValue(kilogram.id)
  await expect(page.getByLabel('Satış birimi')).toHaveValue(kilogram.id)
  await expect(page.getByLabel('Alış birimi')).toHaveValue(kilogram.id)
  await page.getByLabel('Alış birimi').selectOption('')
  await page.getByLabel(`${thickness.key} (mm)`, { exact: false }).fill('2.5')
  await page.getByLabel('Değişiklik gerekçesi').fill('SENTETIK u02 teknik ölçü düzeltmesi')
  const edited = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/${id}/draft` && response.request().method() === 'PATCH')
  await page.getByRole('button', { name: 'Değişiklikleri kaydet' }).click()
  const editedResponse = await edited
  expect(editedResponse.status()).toBe(200)
  expect(editedResponse.request().postDataJSON().expectedVersion).toBe(createdBody.card.product.version)
  const clearedUnits = editedResponse.request().postDataJSON().changes.references.units as Record<string, unknown>
  expect(clearedUnits).toEqual({ purchase: null })
  expectClosedUnitRolesOmitted(clearedUnits)
  const editedBody = await editedResponse.json() as { card: ProductCard }
  expect(editedBody.card.references.units.purchase).toBeNull()
  expectClosedUnitRolesNull(editedBody.card.references.units)
  expect(editedBody.card.product.id).toBe(id)
  expect(editedBody.card.product.version).not.toBe(createdBody.card.product.version)
  await expect(page.getByRole('heading', { name: 'Değişiklikler kaydedildi' })).toBeVisible()
  await page.goto('about:blank')
  const reopened = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/${id}` && response.request().method() === 'GET')
  await page.goto(`/urun/${id}`)
  const reopenedResponse = await reopened
  expect(reopenedResponse.status()).toBe(200)
  const persisted = await reopenedResponse.json() as ProductCard
  expect(persisted.product.id).toBe(id)
  expect(persisted.product.version).toBe(editedBody.card.product.version)
  expect(persisted.product.labels.familyFacts, 'the reopened card resolves the same fixed family facts').toEqual(familyFacts)
  expect(persisted.references.templateId).toBe(template.id)
  expect(persisted.references.templateVersion).toBe(template.version)
  expect(persisted.references.units.stock).toBe(kilogram.id)
  expect(persisted.references.units.sales).toBe(kilogram.id)
  expect(persisted.references.units.purchase).toBeNull()
  expectClosedUnitRolesNull(persisted.references.units)
  expect(persisted.references.attributes).toEqual({ ...expectedAttributes, [thickness.key]: 2.5 })
  await expect(page.getByText('2.5', { exact: true })).toBeVisible()
  await expect(page.getByText(coatingName, { exact: true })).toBeVisible()
  await expectFamilyFacts(page, familyFacts)
  await expect(page.getByTestId('product-unit-stock')).toHaveText(kilogram.displayName)
  await expect(page.getByTestId('product-unit-sales')).toHaveText(kilogram.displayName)
  await expect(page.getByTestId('product-unit-purchase')).toHaveText('atanmadı')
  await expectClosedUnitRolesUnassigned(page)
  // Use the same reopened card in the next edit; selecting purchase must not overwrite stock/sales.
  await page.locator(`a[href="/urun/${id}/duzenle"]`).click()
  await expectFamilyFacts(page, familyFacts)
  await expectClosedUnitRolesNotEditable(page)
  await expect(page.getByLabel('Stok birimi (kg)')).toHaveValue(kilogram.id)
  await expect(page.getByLabel('Satış birimi')).toHaveValue(kilogram.id)
  await expect(page.getByLabel('Alış birimi')).toHaveValue('')
  await page.getByLabel('Alış birimi').selectOption(kilogram.id)
  await page.getByLabel('Değişiklik gerekçesi').fill('SENTETIK u02 alış birimi seçimi')
  const selected = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/${id}/draft` && response.request().method() === 'PATCH')
  await page.getByRole('button', { name: 'Değişiklikleri kaydet' }).click()
  const selectedResponse = await selected
  expect(selectedResponse.status()).toBe(200)
  expect(selectedResponse.request().postDataJSON().expectedVersion).toBe(persisted.product.version)
  const restoredUnits = selectedResponse.request().postDataJSON().changes.references.units as Record<string, unknown>
  expect(restoredUnits).toEqual({ purchase: kilogram.id })
  expectClosedUnitRolesOmitted(restoredUnits)
  const selectedBody = await selectedResponse.json() as { card: ProductCard }
  expectClosedUnitRolesNull(selectedBody.card.references.units)
  expect(selectedBody.card.product.id).toBe(id)
  expect(selectedBody.card.product.version).not.toBe(persisted.product.version)
  await expect(page.getByRole('heading', { name: 'Değişiklikler kaydedildi' })).toBeVisible()
  await page.goto('about:blank')
  const finalRead = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/${id}` && response.request().method() === 'GET')
  const finalHistoryRead = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/${id}/history` && response.request().method() === 'GET')
  await page.goto(`/urun/${id}`)
  const finalResponse = await finalRead
  const finalHistoryResponse = await finalHistoryRead
  expect(finalResponse.status()).toBe(200)
  expect(finalHistoryResponse.status()).toBe(200)
  const finalCard = await finalResponse.json() as ProductCard
  const history = await finalHistoryResponse.json() as ProductHistory
  expect(finalCard.product.id).toBe(id)
  expect(finalCard.product.version).toBe(selectedBody.card.product.version)
  expect(finalCard.product.labels.familyFacts).toEqual(familyFacts)
  expect(finalCard.references.units.purchase).toBe(kilogram.id)
  expect(finalCard.references.units.stock).toBe(kilogram.id)
  expect(finalCard.references.units.sales).toBe(kilogram.id)
  expectClosedUnitRolesNull(finalCard.references.units)
  expect(finalCard.references.attributes).toEqual(persisted.references.attributes)
  await expectFamilyFacts(page, familyFacts)
  await expect(page.getByTestId('product-unit-purchase')).toHaveText(kilogram.displayName)
  await expectClosedUnitRolesUnassigned(page)
  expect(history.productId).toBe(id)
  expect(history.truncated).toBe(false)
  expect(history.entries.map(entry => entry.operation)).toEqual(['DraftBasicEdit', 'DraftBasicEdit', 'DraftCreate'])
  expect(history.entries.map(entry => [entry.versionBefore, entry.versionAfter])).toEqual([
    [editedBody.card.product.version, selectedBody.card.product.version],
    [createdBody.card.product.version, editedBody.card.product.version],
    [null, createdBody.card.product.version],
  ])
  expect(history.entries.map(entry => entry.reason)).toEqual([
    'SENTETIK u02 alış birimi seçimi',
    'SENTETIK u02 teknik ölçü düzeltmesi',
    'Ürün taslağı oluşturma',
  ])
  const allowedEntryKeys = ['changedFields', 'eventId', 'occurredAtUtc', 'operation', 'reason', 'versionAfter', 'versionBefore']
  for (const entry of history.entries) expect(Object.keys(entry).sort()).toEqual(allowedEntryKeys)
  expect(JSON.stringify(history)).not.toMatch(/actor|clientIp|userAgent|correlation|causation|idempotency/i)

  const historyItems = page.getByTestId('product-history').getByRole('listitem')
  await expect(page.getByRole('heading', { level: 2, name: 'Değişiklik geçmişi' })).toBeVisible()
  await expect(historyItems).toHaveCount(3)
  await expect(historyItems.nth(0)).toContainText(`Sürüm ${editedBody.card.product.version} → ${selectedBody.card.product.version}`)
  await expect(historyItems.nth(0)).toContainText('Gerekçe: SENTETIK u02 alış birimi seçimi')
  await expect(historyItems.nth(1)).toContainText(`Sürüm ${createdBody.card.product.version} → ${editedBody.card.product.version}`)
  await expect(historyItems.nth(1)).toContainText('Gerekçe: SENTETIK u02 teknik ölçü düzeltmesi')
  await expect(historyItems.nth(2)).toContainText(`Sürüm ${createdBody.card.product.version}`)
  await expect(historyItems.nth(2)).toContainText('Gerekçe: Ürün taslağı oluşturma')
  await expect(page.getByText('Değişikliği yapan kişi bu görünümde gösterilmez.')).toBeVisible()

  const missingHistoryStatus = await redirectNextApplicationRead(
    page,
    `**${PRODUCTS_PATH}/${id}/history`,
    `${PRODUCTS_PATH}/${MISSING_PRODUCT_ID}/history`,
  )
  expect(missingHistoryStatus, 'an authorized read does not disclose a missing Product as history').toBe(404)
  expect(traffic.writeStatuses).toEqual([201, 200, 200])
})

test('U02-FIX-02 the unauthorized actor is closed out of the Product screens and sends no Product request', async ({ page, request }) => {
  expect(unauthorizedBaseUrl, 'BROWSER_E2E_UNAUTHORIZED_BASE_URL is not set').not.toBe('')
  expect(apiBaseUrl, 'BROWSER_E2E_API_BASE_URL is not set').not.toBe('')
  const { companyId, unauthorizedActor } = seed()
  const traffic = watchProducts(page)
  for (const route of ['/urun', '/urun/yeni']) {
    await page.goto(new URL(route, unauthorizedBaseUrl).toString())
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  }
  expect(traffic.reads + traffic.writes).toBe(0)

  // Same direct actor probe used by Product L-02: the browser bundle stays bound to
  // its own actor, while this isolated request names the fixture's unauthorized actor
  // and real company context. Product ignores the helper's Counterparty contract header.
  const probe = new ActorProbe(request, 'optional-trade-roles-v1')
  const deniedHistory = await probe.send({
    label: 'history',
    actor: unauthorizedActor,
    method: 'GET',
    path: `${apiBaseUrl.replace(/\/$/, '')}${PRODUCTS_PATH}/${MISSING_PRODUCT_ID}/history`,
    companyId,
    mustNotDisclose: [MISSING_PRODUCT_ID],
  })
  expect(deniedHistory.status, 'Product authority is checked before history existence').toBe(403)
  expect(deniedHistory.disclosedForbiddenText).toBe(false)
})
