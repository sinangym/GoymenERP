import { expect, test } from '@playwright/test'

/**
 * The approved Sac/SAC and GDC definition are installed by normal Product migrations.
 * The u02 fixture supplies only disposable authority and its separate synthetic data;
 * this case creates the card through the visible form using the genuine registry rows.
 * The internal Manufacturing snapshot port has its own SQL acceptance, not this UI actor.
 */
const GROUP_ID = '829e576d-c635-502a-97b8-9d5074c9d6a0'
const FAMILY_ID = '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55'
const TEMPLATE_ID = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2'
const KILOGRAM_ID = 'c5ef643e-a6e8-51f3-89f2-8e68b00cb983'
const PRODUCTS_PATH = '/api/v1/products'

type ReferenceOptions = {
  readonly familyFacts: {
    readonly id: string
    readonly code: string
    readonly displayName: string
    readonly templateId: string | null
    readonly templateVersion: string | null
  }[]
  readonly templates: {
    readonly id: string
    readonly version: string
    readonly fields: { readonly key: string; readonly role: string | null }[]
  }[]
  readonly dictionaries: {
    readonly name: string
    readonly entries: {
      readonly code: string
      readonly displayName: string
      readonly status: string
      readonly isExplicitNone: boolean
    }[]
  }[]
  readonly units: { readonly id: string; readonly displayName: string; readonly isKilogram: boolean }[]
}

type StoredCard = {
  readonly product: {
    readonly id: string
    readonly code: string
    readonly name: string
    readonly version: string
    readonly groupId: string
    readonly familyId: string
  }
  readonly references: {
    readonly templateId: string
    readonly templateVersion: string
    readonly attributes: Record<string, unknown>
    readonly units: { readonly stock: string; readonly sales: string; readonly purchase: string | null }
  }
}

test('approved GDC Sac card is created through the form and reopened from the real Product API', async ({ page }) => {
  const groupRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${PRODUCTS_PATH}/code-groups`
      && response.request().method() === 'GET')
  await page.goto('/urun')
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible()
  await page.getByRole('main').getByRole('link', { name: 'Yeni ürün', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()

  const groupsResponse = await groupRead
  expect(groupsResponse.status()).toBe(200)
  const groups = await groupsResponse.json() as { id: string; name: string; prefix: string }[]
  expect(groups.find(group => group.id === GROUP_ID)).toMatchObject({ id: GROUP_ID, name: 'Sac', prefix: 'SAC' })

  const optionsRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${PRODUCTS_PATH}/reference-options`
      && response.request().method() === 'GET')
  await page.getByLabel('Kod grubu').selectOption(GROUP_ID)
  const optionsResponse = await optionsRead
  expect(optionsResponse.status()).toBe(200)
  const options = await optionsResponse.json() as ReferenceOptions
  const family = options.familyFacts.find(item => item.id === FAMILY_ID)
  expect(family).toMatchObject({ code: 'hr-slit-coil', templateId: TEMPLATE_ID, templateVersion: '1' })
  const template = options.templates.find(item => item.id === TEMPLATE_ID && item.version === '1')
  expect(template?.fields.map(field => field.key)).toEqual([
    'thickness-mm', 'width-mm', 'steel-grade', 'coating', 'producer-grade',
  ])
  expect(template?.fields.filter(field => field.role === 'thickness' || field.role === 'width')
    .map(field => field.key)).toEqual(['thickness-mm', 'width-mm'])
  const steelGrade = options.dictionaries.find(item => item.name === 'gdc-steel-grades')
    ?.entries.find(item => item.code === 'S235JR')
  expect(steelGrade).toMatchObject({ status: 'approved' })
  const coating = options.dictionaries.find(item => item.name === 'gdc-coatings')
    ?.entries.find(item => item.code === 'uncoated')
  expect(coating).toMatchObject({ status: 'approved', isExplicitNone: true })
  const kilogram = options.units.find(unit => unit.id === KILOGRAM_ID)
  expect(kilogram).toMatchObject({ isKilogram: true })

  const name = `S235JR 3 mm × 248 mm kaplamasız sac ${Date.now()}`
  await page.getByLabel('Ürün adı').fill(name)
  await page.getByLabel('Ürün ailesi').selectOption(FAMILY_ID)
  await page.getByLabel('thickness-mm (mm)', { exact: false }).fill('3')
  await page.getByLabel('width-mm (mm)', { exact: false }).fill('248')
  await page.getByLabel('steel-grade', { exact: false }).selectOption('S235JR')
  await page.getByLabel('coating', { exact: false }).selectOption('uncoated')
  await page.getByLabel('Stok birimi (kg)').selectOption(KILOGRAM_ID)
  await page.getByLabel('Satış birimi').selectOption(KILOGRAM_ID)

  const createRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === PRODUCTS_PATH && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Taslak olarak kaydet' }).click()
  const createResponse = await createRead
  expect(createResponse.status()).toBe(201)
  const created = (await createResponse.json() as { card: StoredCard }).card
  expect(created.product).toMatchObject({ name, version: '1', groupId: GROUP_ID, familyId: FAMILY_ID })
  expect(created.product.code).toMatch(/^SAC-[0-9]+$/)
  expect(created.references).toMatchObject({
    templateId: TEMPLATE_ID,
    templateVersion: '1',
    attributes: { 'thickness-mm': 3, 'width-mm': 248, 'steel-grade': 'S235JR', coating: 'uncoated' },
    units: { stock: KILOGRAM_ID, sales: KILOGRAM_ID, purchase: null },
  })
  expect(created.references.attributes).not.toHaveProperty('producer-grade')
  await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()

  await page.goto('about:blank')
  const reopenRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${PRODUCTS_PATH}/${created.product.id}`
      && response.request().method() === 'GET')
  await page.goto(`/urun/${created.product.id}`)
  const reopenResponse = await reopenRead
  expect(reopenResponse.status()).toBe(200)
  const reopened = await reopenResponse.json() as StoredCard
  expect(reopened.product).toMatchObject({
    id: created.product.id, code: created.product.code, name, version: created.product.version,
    groupId: GROUP_ID, familyId: FAMILY_ID,
  })
  expect(reopened.references).toEqual(created.references)
  await expect(page.getByTestId('product-family')).toHaveText(family!.displayName)
  await expect(page.getByText(steelGrade!.displayName, { exact: true })).toBeVisible()
  await expect(page.getByText(coating!.displayName, { exact: true })).toBeVisible()
  await expect(page.getByTestId('product-unit-stock')).toHaveText(kilogram!.displayName)
  await expect(page.getByTestId('product-unit-sales')).toHaveText(kilogram!.displayName)
})
