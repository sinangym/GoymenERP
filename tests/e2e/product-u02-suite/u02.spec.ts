import { expect, test, type Page } from '@playwright/test'

/**
 * Product u02 — the fixture's proof on the REAL API, nothing mocked or intercepted:
 *
 *  - U02-FIX-01 the authorized actor (Product master Read / DraftCreate / DraftBasicEdit from the u02
 *    fixture, issued by the suite's synthetic issuer) opens the new-product screen, is offered the
 *    fixture's synthetic code group by the real code-group read, and saves a draft: the real create
 *    answers 201 and the screen prints the server's code, whose prefix is the fixture group's.
 *  - U02-FIX-02 the unauthorized actor (company-a-user: no Product master authority) is closed out of the
 *    Product screens and sends no Product request.
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

test('U02-FIX-01 the authorized actor creates, edits and reopens technical fields through the real API', async ({ page }) => {
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
  const template = options.templates.find(item => item.id === family!.templateId && item.version === family!.templateVersion)!
  expect(template).toBeDefined()
  await page.getByLabel('Ürün ailesi').selectOption(technicalFamilyId)
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
  const created = page.waitForResponse((response) =>
    new URL(response.url()).pathname === PRODUCTS_PATH && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Taslak olarak kaydet' }).click()

  const createdResponse = await created
  expect(createdResponse.status(), 'the real create').toBe(201)
  const createdBody = await createdResponse.json() as { card: { product: { id: string; version: string }; references: { templateId: string; templateVersion: string; attributes: Record<string, unknown>; units: { stock: string; sales: string } } } }
  expect(createdBody.card.references.attributes).toEqual(expectedAttributes)
  expect(createdBody.card.references.templateId).toBe(template.id)
  expect(createdBody.card.references.templateVersion).toBe(template.version)
  expect(createdBody.card.references.units.stock).toBe(kilogram.id)
  expect(createdBody.card.references.units.sales).toBe(kilogram.id)
  await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
  await expect(page.getByText(new RegExp(`^${group!.prefix}-[0-9]+ — ${name}$`))).toBeVisible()
  const id = createdBody.card.product.id
  await page.goto(`/urun/${id}`)
  await expect(page.getByText(coatingName, { exact: true })).toBeVisible()
  await expect(page.getByTestId('product-unit-stock')).toHaveText(kilogram.displayName)
  await expect(page.getByTestId('product-unit-sales')).toHaveText(kilogram.displayName)
  await page.locator(`a[href="/urun/${id}/duzenle"]`).click()
  await page.getByLabel(`${thickness.key} (mm)`, { exact: false }).fill('2.5')
  await page.getByLabel('Değişiklik gerekçesi').fill('SENTETIK u02 teknik ölçü düzeltmesi')
  const edited = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/${id}/draft` && response.request().method() === 'PATCH')
  await page.getByRole('button', { name: 'Değişiklikleri kaydet' }).click()
  const editedResponse = await edited
  expect(editedResponse.status()).toBe(200)
  expect(editedResponse.request().postDataJSON().expectedVersion).toBe(createdBody.card.product.version)
  const editedBody = await editedResponse.json()
  expect(editedBody.card.product.id).toBe(id)
  expect(editedBody.card.product.version).not.toBe(createdBody.card.product.version)
  await expect(page.getByRole('heading', { name: 'Değişiklikler kaydedildi' })).toBeVisible()
  await page.goto('about:blank')
  const reopened = page.waitForResponse(response => new URL(response.url()).pathname === `${PRODUCTS_PATH}/${id}` && response.request().method() === 'GET')
  await page.goto(`/urun/${id}`)
  const reopenedResponse = await reopened
  expect(reopenedResponse.status()).toBe(200)
  const persisted = await reopenedResponse.json()
  expect(persisted.product.id).toBe(id)
  expect(persisted.product.version).toBe(editedBody.card.product.version)
  expect(persisted.references.templateId).toBe(template.id)
  expect(persisted.references.templateVersion).toBe(template.version)
  expect(persisted.references.units.stock).toBe(kilogram.id)
  expect(persisted.references.units.sales).toBe(kilogram.id)
  expect(persisted.references.attributes).toEqual({ ...expectedAttributes, [thickness.key]: 2.5 })
  await expect(page.getByText('2.5', { exact: true })).toBeVisible()
  await expect(page.getByText(coatingName, { exact: true })).toBeVisible()
  await expect(page.getByTestId('product-unit-stock')).toHaveText(kilogram.displayName)
  await expect(page.getByTestId('product-unit-sales')).toHaveText(kilogram.displayName)
  expect(traffic.writeStatuses).toEqual([201, 200])
})

test('U02-FIX-02 the unauthorized actor is closed out of the Product screens and sends no Product request', async ({ page }) => {
  expect(unauthorizedBaseUrl, 'BROWSER_E2E_UNAUTHORIZED_BASE_URL is not set').not.toBe('')
  const traffic = watchProducts(page)
  for (const route of ['/urun', '/urun/yeni']) {
    await page.goto(new URL(route, unauthorizedBaseUrl).toString())
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
  }
  expect(traffic.reads + traffic.writes).toBe(0)
})
