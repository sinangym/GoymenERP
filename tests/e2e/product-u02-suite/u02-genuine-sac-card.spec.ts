import { expect, test, type APIResponse, type Locator, type Page } from '@playwright/test'
import sql from 'mssql'

/**
 * The approved Sac/SAC and GDC definition are installed by normal Product migrations.
 * The u02 fixture supplies only disposable authority and its separate synthetic data;
 * this case creates the card through the visible form using the genuine registry rows.
 * The internal Manufacturing snapshot port has its own SQL acceptance, not this UI actor.
 */
const GROUP_ID = '829e576d-c635-502a-97b8-9d5074c9d6a0'
const FAMILY_ID = '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55'
const TEMPLATE_ID = 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2'
const CUT_SHEET_FAMILY_ID = '075d01d5-c57c-44b9-903a-89c00e4db029'
const CUT_SHEET_TEMPLATE_ID = '57937348-393e-4982-a5a4-63eff8be40c9'
const KILOGRAM_ID = 'c5ef643e-a6e8-51f3-89f2-8e68b00cb983'
// ApprovedSettingsUnitProfileCatalog.GdcKilogramV1, not an inferred unit conversion.
const KILOGRAM_PROFILE_ID = '4a790fdc-2a6b-5126-835f-d3a13d28b71c'
const KILOGRAM_PROFILE_VERSION = '1'
const OTHER_COMPANY_ID = '10000000-0000-0000-0000-000000000002'
const PRODUCTS_PATH = '/api/v1/products'
const INVENTORY_OPENINGS_PATH = '/api/v1/inventory/opening-counts'
const INVENTORY_PRODUCT_REFERENCES_PATH = '/api/v1/inventory/references/products'
const INVENTORY_LOCATION_REFERENCES_PATH = '/api/v1/inventory/references/locations'
const OPENING_DATE = '2026-10-05'

type ReferenceOptions = {
  readonly familyFacts: {
    readonly id: string
    readonly code: string
    readonly displayName: string
    readonly cins: string
    readonly tur: string
    readonly tip: string
    readonly templateId: string | null
    readonly templateVersion: string | null
  }[]
  readonly templates: {
    readonly id: string
    readonly version: string
    readonly fields: {
      readonly key: string
      readonly kind: string
      readonly role: string | null
      readonly required: boolean
    }[]
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
    readonly lifecycle: string
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

type ProductHistory = {
  readonly productId: string
  readonly truncated: boolean
  readonly entries: {
    readonly operation: string
    readonly versionBefore: string | null
    readonly versionAfter: string
    readonly reason: string
    readonly changedFields: string[]
  }[]
}

function definitionOf(root: Page | Locator, label: string) {
  return root.locator('dt').filter({ hasText: new RegExp(`^${label}$`) }).locator('..').locator('dd')
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
  expect(family).toMatchObject({
    code: 'hr-slit-coil', cins: 'Sac', tur: 'Sıcak Haddelenmiş', tip: 'Dilinmiş Bant',
    templateId: TEMPLATE_ID, templateVersion: '1',
  })
  const template = options.templates.find(item => item.id === TEMPLATE_ID && item.version === '1')
  expect(template?.fields.map(field => field.key)).toEqual([
    'thickness-mm', 'width-mm', 'steel-grade', 'coating', 'producer-grade',
  ])
  expect(template?.fields.filter(field => field.role === 'thickness' || field.role === 'width')
    .map(field => field.key)).toEqual(['thickness-mm', 'width-mm'])
  expect(template?.fields.some(field => field.role === 'length' || field.key === 'length-mm')).toBe(false)
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
  const familyFacts = page.locator('dl[aria-label="Ailenin sabit sınıflandırması"]')
  await expect(definitionOf(familyFacts, 'Cins')).toHaveText('Sac')
  await expect(definitionOf(familyFacts, 'Tür')).toHaveText('Sıcak Haddelenmiş')
  await expect(definitionOf(familyFacts, 'Tip')).toHaveText('Dilinmiş Bant')
  await page.getByLabel('Kalınlık (mm)', { exact: false }).fill('3')
  await page.getByLabel('En (mm)', { exact: false }).fill('248')
  await expect(page.getByLabel('Boy (mm)', { exact: false })).toHaveCount(0)
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
  expect(created.references.attributes).not.toHaveProperty('length-mm')
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
  expect(reopened.references.attributes).not.toHaveProperty('length-mm')
  await expect(page.getByTestId('product-family')).toHaveText(family!.displayName)
  await expect(definitionOf(page, 'Cins')).toHaveText('Sac')
  await expect(definitionOf(page, 'Tür')).toHaveText('Sıcak Haddelenmiş')
  await expect(definitionOf(page, 'Tip')).toHaveText('Dilinmiş Bant')
  await expect(definitionOf(page, 'Kalınlık')).toHaveText('3')
  await expect(definitionOf(page, 'En')).toHaveText('248')
  await expect(definitionOf(page, 'Boy')).toHaveCount(0)
  await expect(page.getByText(steelGrade!.displayName, { exact: true })).toBeVisible()
  await expect(page.getByText(coating!.displayName, { exact: true })).toBeVisible()
  await expect(page.getByTestId('product-unit-stock')).toHaveText(kilogram!.displayName)
  await expect(page.getByTestId('product-unit-sales')).toHaveText(kilogram!.displayName)
})

test('P03.2 a genuine cut-sheet card keeps nominal length exact through clear, refusal, restore, activation and reopen', async ({ page }) => {
  const rawSeed = process.env.BROWSER_E2E_U02_SEED?.trim()
  if (!rawSeed) throw new Error('BROWSER_E2E_U02_SEED is required for the disposable u02 actor binding.')
  const actorSeed = JSON.parse(rawSeed) as Record<string, unknown>
  const companyId = actorSeed.companyId
  const requester = actorSeed.authorizedActor
  const activator = actorSeed.activatorActor
  if (typeof companyId !== 'string' || typeof requester !== 'string' || typeof activator !== 'string'
    || !companyId || !requester || !/^u02-[a-z0-9-]+$/.test(activator) || requester === activator) {
    throw new Error('The u02 fixture must bind distinct Product requester and activator actors.')
  }
  const apiBaseUrl = process.env.BROWSER_E2E_API_BASE_URL?.trim().replace(/\/$/, '')
  if (!apiBaseUrl || new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error('BROWSER_E2E_API_BASE_URL must name the disposable loopback API.')
  }
  const headersFor = (actor: string) => ({ 'X-Development-User': actor, 'X-Company-Id': companyId })

  await page.setExtraHTTPHeaders({ 'X-Development-User': requester })
  const groupRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${PRODUCTS_PATH}/code-groups`
      && response.request().method() === 'GET')
  await page.goto('/urun')
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
  const family = options.familyFacts.find(item => item.id === CUT_SHEET_FAMILY_ID)
  expect(family).toMatchObject({
    code: 'hr-cut-sheet-plate', displayName: 'Sıcak Haddelenmiş Kesilmiş Sac/Levha',
    cins: 'Sac', tur: 'Sıcak Haddelenmiş', tip: 'Kesilmiş Sac/Levha',
    templateId: CUT_SHEET_TEMPLATE_ID, templateVersion: '1',
  })
  const template = options.templates.find(item =>
    item.id === CUT_SHEET_TEMPLATE_ID && item.version === '1')
  expect(template?.fields.map(field => field.key)).toEqual([
    'thickness-mm', 'width-mm', 'length-mm', 'steel-grade', 'coating', 'producer-grade',
  ])
  expect(template?.fields.filter(field =>
    field.role === 'thickness' || field.role === 'width' || field.role === 'length')
    .map(field => [field.key, field.kind, field.required])).toEqual([
    ['thickness-mm', 'measureMillimetre', true],
    ['width-mm', 'measureMillimetre', true],
    ['length-mm', 'measureMillimetre', true],
  ])
  const steelGrade = options.dictionaries.find(item => item.name === 'gdc-steel-grades')
    ?.entries.find(item => item.code === 'S235JR')
  const coating = options.dictionaries.find(item => item.name === 'gdc-coatings')
    ?.entries.find(item => item.code === 'uncoated')
  const kilogram = options.units.find(unit => unit.id === KILOGRAM_ID)
  expect(steelGrade).toMatchObject({ status: 'approved' })
  expect(coating).toMatchObject({ status: 'approved', isExplicitNone: true })
  expect(kilogram).toMatchObject({ isKilogram: true })

  const name = `S235JR 3 × 1250 × 2500.125 kesilmiş sac ${Date.now()}`
  await page.getByLabel('Ürün adı').fill(name)
  await page.getByLabel('Ürün ailesi').selectOption(CUT_SHEET_FAMILY_ID)
  const familyFacts = page.locator('dl[aria-label="Ailenin sabit sınıflandırması"]')
  await expect(definitionOf(familyFacts, 'Cins')).toHaveText('Sac')
  await expect(definitionOf(familyFacts, 'Tür')).toHaveText('Sıcak Haddelenmiş')
  await expect(definitionOf(familyFacts, 'Tip')).toHaveText('Kesilmiş Sac/Levha')
  await page.getByLabel('Kalınlık (mm)', { exact: false }).fill('3')
  await page.getByLabel('En (mm)', { exact: false }).fill('1250')
  await page.getByLabel('Boy (mm)', { exact: false }).fill('2500.125')
  await page.getByLabel('steel-grade', { exact: false }).selectOption('S235JR')
  await page.getByLabel('coating', { exact: false }).selectOption('uncoated')
  await page.getByLabel('Stok birimi (kg)').selectOption(KILOGRAM_ID)
  await page.getByLabel('Satış birimi').selectOption(KILOGRAM_ID)

  const createRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === PRODUCTS_PATH && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Taslak olarak kaydet' }).click()
  const createResponse = await createRead
  expect(createResponse.status()).toBe(201)
  const createPayload = createResponse.request().postDataJSON() as {
    readonly familyId: string
    readonly references: { readonly templateId: string; readonly templateVersion: string
      readonly attributes: Record<string, unknown> }
  }
  expect(createPayload.familyId).toBe(CUT_SHEET_FAMILY_ID)
  expect(createPayload.references).toMatchObject({
    templateId: CUT_SHEET_TEMPLATE_ID, templateVersion: '1',
    attributes: {
      'thickness-mm': 3, 'width-mm': 1250, 'length-mm': 2500.125,
      'steel-grade': 'S235JR', coating: 'uncoated',
    },
  })
  const created = (await createResponse.json() as { card: StoredCard }).card
  expect(created.product).toMatchObject({
    name, lifecycle: 'Draft', version: '1', groupId: GROUP_ID, familyId: CUT_SHEET_FAMILY_ID,
  })
  expect(created.references).toMatchObject({
    templateId: CUT_SHEET_TEMPLATE_ID, templateVersion: '1',
    attributes: {
      'thickness-mm': 3, 'width-mm': 1250, 'length-mm': 2500.125,
      'steel-grade': 'S235JR', coating: 'uncoated',
    },
  })
  expect(created.references.attributes).not.toHaveProperty('producer-grade')
  const cardPath = `${PRODUCTS_PATH}/${created.product.id}`

  await page.goto('about:blank')
  const firstCardRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === cardPath && response.request().method() === 'GET')
  await page.goto(`/urun/${created.product.id}`)
  const firstCardResponse = await firstCardRead
  expect(firstCardResponse.status()).toBe(200)
  expect((await firstCardResponse.json() as StoredCard).references).toEqual(created.references)
  await expect(page.getByTestId('product-family')).toHaveText(family!.displayName)
  await expect(definitionOf(page, 'Cins')).toHaveText('Sac')
  await expect(definitionOf(page, 'Tür')).toHaveText('Sıcak Haddelenmiş')
  await expect(definitionOf(page, 'Tip')).toHaveText('Kesilmiş Sac/Levha')
  await expect(definitionOf(page, 'Kalınlık')).toHaveText('3')
  await expect(definitionOf(page, 'En')).toHaveText('1250')
  await expect(definitionOf(page, 'Boy')).toHaveText('2500.125')

  await page.locator(`a[href="/urun/${created.product.id}/duzenle"]`).click()
  await expect(page.getByLabel('Kalınlık (mm)', { exact: false })).toHaveValue('3')
  await expect(page.getByLabel('En (mm)', { exact: false })).toHaveValue('1250')
  await expect(page.getByLabel('Boy (mm)', { exact: false })).toHaveValue('2500.125')
  await page.getByLabel('Boy (mm)', { exact: false }).fill('')
  await page.getByLabel('Değişiklik gerekçesi').fill('SENTETIK u02 nominal boy temizleme')
  const clearRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${cardPath}/draft` && response.request().method() === 'PATCH')
  await page.getByRole('button', { name: 'Değişiklikleri kaydet' }).click()
  const clearResponse = await clearRead
  expect(clearResponse.status()).toBe(200)
  const clearPayload = clearResponse.request().postDataJSON() as {
    readonly expectedVersion: string
    readonly changes: { readonly references: { readonly attributes: Record<string, unknown> } }
  }
  expect(clearPayload.expectedVersion).toBe(created.product.version)
  expect(clearPayload.changes.references.attributes).toEqual({
    'thickness-mm': 3, 'width-mm': 1250, 'steel-grade': 'S235JR', coating: 'uncoated',
  })
  expect(clearPayload.changes.references.attributes).not.toHaveProperty('length-mm')
  const cleared = (await clearResponse.json() as { card: StoredCard }).card
  expect(cleared.product.version).toBe((BigInt(created.product.version) + 1n).toString())
  expect(cleared.references.attributes).not.toHaveProperty('length-mm')

  await page.setExtraHTTPHeaders({ 'X-Development-User': activator })
  await page.goto('about:blank')
  const incompleteRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === cardPath && response.request().method() === 'GET')
  await page.goto(`/urun/${created.product.id}`)
  const incompleteResponse = await incompleteRead
  expect(incompleteResponse.status()).toBe(200)
  const incomplete = await incompleteResponse.json() as StoredCard
  expect(incomplete.product).toMatchObject({ lifecycle: 'Draft', version: cleared.product.version })
  expect(incomplete.references.attributes).not.toHaveProperty('length-mm')
  const beforeRefusalHistoryResponse = await page.request.get(`${apiBaseUrl}${cardPath}/history`,
    { headers: headersFor(activator) })
  expect(beforeRefusalHistoryResponse.status()).toBe(200)
  const beforeRefusalHistory = await beforeRefusalHistoryResponse.json() as ProductHistory

  const activationRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${cardPath}/activation`
      && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Ürünü aktifleştir', exact: true }).click()
  const activationRefusal = await activationRead
  expect(activationRefusal.status()).toBe(422)
  expect(activationRefusal.request().postDataJSON()).toEqual({ expectedVersion: cleared.product.version })
  const refusal = await activationRefusal.json() as {
    readonly code: string
    readonly errors: readonly { readonly field: string; readonly code: string }[]
  }
  expect(refusal.code).toBe('validation_failed')
  expect(refusal.errors).toContainEqual({ field: 'references.attributes.length-mm', code: 'required' })
  const lengthAlert = page.getByRole('alert').filter({ hasText: 'Boy' })
    .filter({ hasText: 'Bu alan zorunludur.' })
  await expect(lengthAlert).toBeVisible()
  const afterRefusalCard = await page.request.get(`${apiBaseUrl}${cardPath}`, { headers: headersFor(activator) })
  const afterRefusalHistory = await page.request.get(`${apiBaseUrl}${cardPath}/history`,
    { headers: headersFor(activator) })
  expect(afterRefusalCard.status()).toBe(200)
  expect(afterRefusalHistory.status()).toBe(200)
  expect(await afterRefusalCard.json()).toEqual(incomplete)
  expect(await afterRefusalHistory.json()).toEqual(beforeRefusalHistory)

  await page.setExtraHTTPHeaders({ 'X-Development-User': requester })
  await page.goto('about:blank')
  await page.goto(`/urun/${created.product.id}/duzenle`)
  await expect(page.getByLabel('Boy (mm)', { exact: false })).toHaveValue('')
  await page.getByLabel('Boy (mm)', { exact: false }).fill('2500.125')
  await page.getByLabel('Değişiklik gerekçesi').fill('SENTETIK u02 nominal boy geri yükleme')
  const restoreRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${cardPath}/draft` && response.request().method() === 'PATCH')
  await page.getByRole('button', { name: 'Değişiklikleri kaydet' }).click()
  const restoreResponse = await restoreRead
  expect(restoreResponse.status()).toBe(200)
  const restorePayload = restoreResponse.request().postDataJSON() as {
    readonly expectedVersion: string
    readonly changes: { readonly references: { readonly attributes: Record<string, unknown> } }
  }
  expect(restorePayload.expectedVersion).toBe(cleared.product.version)
  expect(restorePayload.changes.references.attributes['length-mm']).toBe(2500.125)
  const restored = (await restoreResponse.json() as { card: StoredCard }).card
  expect(restored.product.version).toBe((BigInt(cleared.product.version) + 1n).toString())
  expect(restored.references.attributes['length-mm']).toBe(2500.125)

  await page.setExtraHTTPHeaders({ 'X-Development-User': activator })
  await page.goto('about:blank')
  const restoredRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === cardPath && response.request().method() === 'GET')
  await page.goto(`/urun/${created.product.id}`)
  const restoredResponse = await restoredRead
  expect(restoredResponse.status()).toBe(200)
  expect((await restoredResponse.json() as StoredCard).references.attributes['length-mm']).toBe(2500.125)
  await expect(definitionOf(page, 'Boy')).toHaveText('2500.125')
  const finalActivationRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${cardPath}/activation`
      && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Ürünü aktifleştir', exact: true }).click()
  const finalActivationResponse = await finalActivationRead
  expect(finalActivationResponse.status()).toBe(200)
  expect(finalActivationResponse.request().postDataJSON()).toEqual({ expectedVersion: restored.product.version })
  const activated = (await finalActivationResponse.json() as { card: StoredCard; isReplay: boolean })
  expect(activated.isReplay).toBe(false)
  expect(activated.card.product).toMatchObject({
    lifecycle: 'Active', version: (BigInt(restored.product.version) + 1n).toString(),
  })
  expect(activated.card.references.attributes['length-mm']).toBe(2500.125)

  await page.goto('about:blank')
  const finalCardRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === cardPath && response.request().method() === 'GET')
  const finalHistoryRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${cardPath}/history` && response.request().method() === 'GET')
  await page.goto(`/urun/${created.product.id}`)
  const finalCardResponse = await finalCardRead
  const finalHistoryResponse = await finalHistoryRead
  expect(finalCardResponse.status()).toBe(200)
  expect(finalHistoryResponse.status()).toBe(200)
  const reopened = await finalCardResponse.json() as StoredCard
  const history = await finalHistoryResponse.json() as ProductHistory
  expect(reopened.product).toMatchObject({
    lifecycle: 'Active', version: activated.card.product.version,
    groupId: GROUP_ID, familyId: CUT_SHEET_FAMILY_ID,
  })
  expect(reopened.references.attributes['length-mm']).toBe(2500.125)
  expect(history.productId).toBe(created.product.id)
  expect(history.entries.map(entry => entry.operation)).toEqual([
    'Activate', 'DraftBasicEdit', 'DraftBasicEdit', 'DraftCreate',
  ])
  expect(history.entries.map(entry => [entry.versionBefore, entry.versionAfter])).toEqual([
    [restored.product.version, activated.card.product.version],
    [cleared.product.version, restored.product.version],
    [created.product.version, cleared.product.version],
    [null, created.product.version],
  ])
  expect(history.entries.map(entry => entry.reason)).toEqual([
    'Ürün kartı onayı',
    'SENTETIK u02 nominal boy geri yükleme',
    'SENTETIK u02 nominal boy temizleme',
    'Ürün taslağı oluşturma',
  ])
  await expect(page.getByTestId('product-family')).toHaveText(family!.displayName)
  await expect(definitionOf(page, 'Cins')).toHaveText('Sac')
  await expect(definitionOf(page, 'Tür')).toHaveText('Sıcak Haddelenmiş')
  await expect(definitionOf(page, 'Tip')).toHaveText('Kesilmiş Sac/Levha')
  await expect(definitionOf(page, 'Kalınlık')).toHaveText('3')
  await expect(definitionOf(page, 'En')).toHaveText('1250')
  await expect(definitionOf(page, 'Boy')).toHaveText('2500.125')
  await expect(page.getByTestId('product-history').getByRole('listitem')).toHaveCount(4)
})

test('the same genuine SAC card is activated by the existing activator and reopened as Active', async ({ page }) => {
  const rawSeed = process.env.BROWSER_E2E_U02_SEED?.trim()
  if (!rawSeed) throw new Error('BROWSER_E2E_U02_SEED is required for the disposable u02 actor binding.')
  const actorSeed = JSON.parse(rawSeed) as Record<string, unknown>
  const companyId = actorSeed.companyId
  const requester = actorSeed.authorizedActor
  const activator = actorSeed.activatorActor
  const inventoryRecorder = actorSeed.inventoryRecorderActor
  if (typeof companyId !== 'string' || typeof requester !== 'string' || typeof activator !== 'string'
    || typeof inventoryRecorder !== 'string' || !companyId || !requester
    || !/^u02-[a-z0-9-]+$/.test(activator) || inventoryRecorder !== 'u02-inventory-recorder'
    || new Set([requester, activator, inventoryRecorder]).size !== 3) {
    throw new Error('The u02 fixture must bind distinct Product requester, activator and disposable Inventory recorder actors.')
  }
  const apiBaseUrl = process.env.BROWSER_E2E_API_BASE_URL?.trim().replace(/\/$/, '')
  if (!apiBaseUrl || new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error('BROWSER_E2E_API_BASE_URL must name the disposable loopback API.')
  }
  const headersFor = (actor: string) => ({ 'X-Development-User': actor, 'X-Company-Id': companyId })

  await page.setExtraHTTPHeaders({ 'X-Development-User': requester })
  await page.goto('/urun')
  await page.getByRole('main').getByRole('link', { name: 'Yeni ürün', exact: true }).click()
  const optionsRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${PRODUCTS_PATH}/reference-options`
      && response.request().method() === 'GET')
  await page.getByLabel('Kod grubu').selectOption(GROUP_ID)
  const optionsResponse = await optionsRead
  expect(optionsResponse.status()).toBe(200)
  const options = await optionsResponse.json() as ReferenceOptions
  const family = options.familyFacts.find(item => item.id === FAMILY_ID)
  expect(family).toMatchObject({
    code: 'hr-slit-coil', cins: 'Sac', tur: 'Sıcak Haddelenmiş', tip: 'Dilinmiş Bant',
    templateId: TEMPLATE_ID, templateVersion: '1',
  })
  const kilogram = options.units.find(unit => unit.id === KILOGRAM_ID)
  expect(kilogram).toMatchObject({ isKilogram: true })

  const name = `S235JR 3 mm × 248 mm kaplamasız aktivasyon ${Date.now()}`
  await page.getByLabel('Ürün adı').fill(name)
  await page.getByLabel('Ürün ailesi').selectOption(FAMILY_ID)
  await page.getByLabel('Kalınlık (mm)', { exact: false }).fill('3')
  await page.getByLabel('En (mm)', { exact: false }).fill('248')
  await expect(page.getByLabel('Boy (mm)', { exact: false })).toHaveCount(0)
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
  expect(created.product).toMatchObject({ name, lifecycle: 'Draft', version: '1', groupId: GROUP_ID, familyId: FAMILY_ID })
  expect(created.references).toMatchObject({
    templateId: TEMPLATE_ID, templateVersion: '1',
    attributes: { 'thickness-mm': 3, 'width-mm': 248, 'steel-grade': 'S235JR', coating: 'uncoated' },
    units: { stock: KILOGRAM_ID, sales: KILOGRAM_ID, purchase: null },
  })
  expect(created.references.attributes).not.toHaveProperty('producer-grade')
  const cardPath = `${PRODUCTS_PATH}/${created.product.id}`
  const activationPath = `${cardPath}/activation`

  await page.goto(`/urun/${created.product.id}`)
  await expect(page.getByRole('button', { name: 'Ürünü aktifleştir' })).toHaveCount(0)
  const beforeCardResponse = await page.request.get(`${apiBaseUrl}${cardPath}`, { headers: headersFor(requester) })
  const beforeHistoryResponse = await page.request.get(`${apiBaseUrl}${cardPath}/history`, { headers: headersFor(requester) })
  expect(beforeCardResponse.status()).toBe(200)
  expect(beforeHistoryResponse.status()).toBe(200)
  const beforeCard = await beforeCardResponse.json() as StoredCard
  const beforeHistory = await beforeHistoryResponse.json() as ProductHistory
  expect(beforeHistory.entries.map(entry => entry.operation)).toEqual(['DraftCreate'])
  const denied = await page.request.post(`${apiBaseUrl}${activationPath}`, {
    headers: { ...headersFor(requester), 'Idempotency-Key': `u02-genuine-denied-${created.product.id}` },
    data: { expectedVersion: created.product.version },
  })
  expect(denied.status()).toBe(403)
  expect(await denied.text()).not.toContain(name)
  const deniedCardResponse = await page.request.get(`${apiBaseUrl}${cardPath}`, { headers: headersFor(requester) })
  const deniedHistoryResponse = await page.request.get(`${apiBaseUrl}${cardPath}/history`, { headers: headersFor(requester) })
  expect(deniedCardResponse.status()).toBe(200)
  expect(deniedHistoryResponse.status()).toBe(200)
  expect(await deniedCardResponse.json()).toEqual(beforeCard)
  expect(await deniedHistoryResponse.json()).toEqual(beforeHistory)

  await page.setExtraHTTPHeaders({ 'X-Development-User': activator })
  await page.goto('about:blank')
  const activatorRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === cardPath && response.request().method() === 'GET')
  await page.goto(`/urun/${created.product.id}`)
  expect((await activatorRead).status()).toBe(200)
  const activate = page.getByRole('button', { name: 'Ürünü aktifleştir', exact: true })
  await expect(activate).toBeVisible()
  const activationRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === activationPath && response.request().method() === 'POST')
  await activate.click()
  const activationResponse = await activationRead
  expect(activationResponse.status()).toBe(200)
  const activationBody = await activationResponse.json() as { card: StoredCard; isReplay: boolean }
  expect(activationBody.isReplay).toBe(false)
  expect(activationBody.card.product).toMatchObject({
    id: created.product.id, lifecycle: 'Active', version: (BigInt(created.product.version) + 1n).toString(),
  })
  const activationRequest = activationResponse.request()
  const activationKey = await activationRequest.headerValue('Idempotency-Key')
  expect(activationKey).toBeTruthy()
  expect(activationRequest.postDataJSON()).toEqual({ expectedVersion: created.product.version })
  await expect(page.locator('span[data-lifecycle]')).toHaveText('Aktif')

  await page.goto('about:blank')
  const cardRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === cardPath && response.request().method() === 'GET')
  const historyRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${cardPath}/history` && response.request().method() === 'GET')
  await page.goto(`/urun/${created.product.id}`)
  const reopenedResponse = await cardRead
  const historyResponse = await historyRead
  expect(reopenedResponse.status()).toBe(200)
  expect(historyResponse.status()).toBe(200)
  const reopened = await reopenedResponse.json() as StoredCard
  const history = await historyResponse.json() as ProductHistory
  expect(reopened.product).toMatchObject({
    id: created.product.id, code: created.product.code, name,
    lifecycle: 'Active', version: activationBody.card.product.version,
    groupId: GROUP_ID, familyId: FAMILY_ID,
  })
  expect(reopened.references).toEqual(created.references)
  expect(history.productId).toBe(created.product.id)
  expect(history.truncated).toBe(false)
  expect(history.entries.map(entry => entry.operation)).toEqual(['Activate', 'DraftCreate'])
  expect(history.entries[0]).toMatchObject({
    versionBefore: created.product.version, versionAfter: reopened.product.version,
    reason: 'Ürün kartı onayı', changedFields: ['lifecycle'],
  })
  await expect(page.getByTestId('product-family')).toHaveText(family!.displayName)
  await expect(definitionOf(page, 'Kalınlık')).toHaveText('3')
  await expect(definitionOf(page, 'En')).toHaveText('248')
  await expect(definitionOf(page, 'Boy')).toHaveCount(0)
  await expect(page.getByText('S235JR', { exact: true })).toBeVisible()
  await expect(page.getByText('Kaplamasız', { exact: true })).toBeVisible()
  await expect(page.getByTestId('product-unit-stock')).toHaveText(kilogram!.displayName)
  await expect(page.getByTestId('product-unit-sales')).toHaveText(kilogram!.displayName)

  const replay = await page.request.post(`${apiBaseUrl}${activationPath}`, {
    headers: { ...headersFor(activator), 'Idempotency-Key': activationKey! },
    data: activationRequest.postDataJSON(),
  })
  expect(replay.status()).toBe(200)
  expect(replay.headers()['idempotency-replayed']).toBe('true')
  const replayed = await replay.json() as { card: StoredCard; isReplay: boolean }
  expect(replayed.isReplay).toBe(true)
  expect(replayed.card.product.version).toBe(reopened.product.version)
  const afterReplayCard = await page.request.get(`${apiBaseUrl}${cardPath}`, { headers: headersFor(activator) })
  const afterReplayHistory = await page.request.get(`${apiBaseUrl}${cardPath}/history`, { headers: headersFor(activator) })
  expect(afterReplayCard.status()).toBe(200)
  expect(afterReplayHistory.status()).toBe(200)
  expect(await afterReplayCard.json()).toEqual(reopened)
  expect(await afterReplayHistory.json()).toEqual(history)

  // Keep this exact Active card in the same disposable run. Only the separate Inventory
  // recorder can choose it for stock; neither Product master actor gains an opening grant.
  await page.setExtraHTTPHeaders({ 'X-Development-User': inventoryRecorder })
  await page.goto('about:blank')
  await page.goto('/stok/acilis')
  await expect(page.getByRole('heading', { level: 1, name: 'İlk stok / açılış sayımı' })).toBeVisible()
  await page.getByRole('link', { name: 'Yeni açılış sayımı', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni açılış sayımı' })).toBeVisible()
  await page.getByLabel('Sayım tarihi').fill(OPENING_DATE)
  const sheetReference = `SENTETIK U02 genuine SAC opening ${created.product.id}`
  const labelCode = `SAC-U02-${created.product.id.slice(0, 8)}`
  await page.getByLabel('Belge / sayım referansı').fill(sheetReference)

  const locationRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === INVENTORY_LOCATION_REFERENCES_PATH
      && response.request().method() === 'GET')
  await page.getByRole('button', { name: 'Lokasyonları getir', exact: true }).click()
  const locationResponse = await locationRead
  expect(locationResponse.status()).toBe(200)
  const locationCandidates = await locationResponse.json() as {
    readonly items: readonly { readonly locationId: string; readonly locationCode: string }[]
  }
  expect(locationCandidates.items.length).toBeGreaterThan(0)
  const locationId = locationCandidates.items[0].locationId
  expect(locationId).toMatch(/^[0-9a-f-]{36}$/i)
  await page.getByLabel('Lokasyon', { exact: true }).selectOption(locationId)

  await page.getByLabel('Ürün ara').fill(created.product.code)
  const productRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === INVENTORY_PRODUCT_REFERENCES_PATH
      && response.request().method() === 'GET')
  await page.getByRole('button', { name: 'Ürünleri getir', exact: true }).click()
  const productResponse = await productRead
  expect(productResponse.status()).toBe(200)
  const candidates = await productResponse.json() as { readonly items: readonly {
    readonly productId: string; readonly productVersion: string; readonly code: string; readonly name: string
    readonly stockUnitId: string; readonly stockUnitCode: string
    readonly profileId: string; readonly profileVersion: string
  }[] }
  const exactCandidates = candidates.items.filter(candidate => candidate.productId === created.product.id)
  expect(exactCandidates).toHaveLength(1)
  expect(exactCandidates[0]).toMatchObject({
    productVersion: reopened.product.version, code: created.product.code, name,
    stockUnitId: KILOGRAM_ID, stockUnitCode: 'kg',
    profileId: KILOGRAM_PROFILE_ID, profileVersion: KILOGRAM_PROFILE_VERSION,
  })
  await page.getByLabel('Ürün', { exact: true }).selectOption(created.product.id)
  await page.getByLabel('Etiket kodu').fill(labelCode)
  await page.getByLabel('Sayım kg').fill('12.5')

  const openingRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === INVENTORY_OPENINGS_PATH
      && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Açılış sayımını kaydet', exact: true }).click()
  const openingResponse = await openingRead
  expect(openingResponse.status()).toBe(201)
  const originalOpeningCorrelation = openingResponse.headers()['x-correlation-id']
  expect(originalOpeningCorrelation).toBeTruthy()
  expect(openingResponse.headers()['idempotency-replayed']).toBeUndefined()
  expect(openingResponse.headers()['idempotency-original-correlation-id']).toBeUndefined()
  const openingRequest = openingResponse.request()
  const openingKey = await openingRequest.headerValue('Idempotency-Key')
  expect(openingKey).toBeTruthy()
  const openingPayload = openingRequest.postDataJSON() as {
    readonly countId: string; readonly locationId: string; readonly countedOn: string
    readonly sheetReference: string; readonly lines: readonly {
      readonly lineId: string; readonly labelId: string; readonly labelCode: string
      readonly lotId: string; readonly productId: string; readonly productVersion: string
      readonly quantityKg: string
    }[]
  }
  expect(openingPayload).toMatchObject({
    locationId, countedOn: OPENING_DATE, sheetReference,
    lines: [{ labelCode, productId: created.product.id,
      productVersion: reopened.product.version, quantityKg: '12.5' }],
  })
  expect(openingPayload.lines).toHaveLength(1)
  const recorded = await openingResponse.json() as {
    readonly countId: string; readonly companyId: string; readonly locationId: string
    readonly countedOn: string; readonly sheetReference: string; readonly lines: readonly {
      readonly lineId: string; readonly labelId: string; readonly labelCode: string
      readonly lotId: string; readonly productId: string; readonly quantityKg: string
    }[]
  }
  expect(recorded).toMatchObject({
    countId: openingPayload.countId, companyId, locationId, countedOn: OPENING_DATE,
    sheetReference, lines: [{ labelId: openingPayload.lines[0].labelId,
      labelCode, productId: created.product.id, quantityKg: '12.5' }],
  })
  expect(recorded.lines).toHaveLength(1)
  await expect(page.getByRole('heading', { level: 1, name: 'Açılış sayımı kaydedildi' })).toBeVisible()

  await page.goto('about:blank')
  const countRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === `${INVENTORY_OPENINGS_PATH}/${recorded.countId}`
      && response.request().method() === 'GET')
  await page.goto(`/stok/acilis/${recorded.countId}`)
  const countResponse = await countRead
  expect(countResponse.status()).toBe(200)
  expect(await countResponse.json()).toEqual(recorded)
  await expect(page.getByRole('heading', { level: 1, name: 'Açılış sayımı' })).toBeVisible()
  const labelLink = page.getByRole('link', { name: labelCode, exact: true })
  await expect(labelLink).toHaveAttribute('href', `/stok/etiket/${recorded.lines[0].labelId}`)
  const labelDetailPath = `/api/v1/inventory/labels/${recorded.lines[0].labelId}/detail`
  const labelRead = page.waitForResponse(response =>
    new URL(response.url()).pathname === labelDetailPath && response.request().method() === 'GET')
  await labelLink.click()
  const labelResponse = await labelRead
  expect(labelResponse.status()).toBe(200)
  const label = await labelResponse.json() as {
    readonly identity: { readonly labelId: string; readonly companyId: string
      readonly productId: string; readonly code: string }
    readonly balances: { readonly physicalKg: string }
  }
  expect(label.identity).toMatchObject({
    labelId: recorded.lines[0].labelId, companyId, productId: created.product.id, code: labelCode,
  })
  expect(label.balances.physicalKg).toBe('12.5')
  await expect(page.getByRole('heading', { level: 1, name: labelCode, exact: true })).toBeVisible()

  const sqlConnection = process.env.BROWSER_E2E_SQL_CONNECTION?.trim()
  if (!sqlConnection) throw new Error('BROWSER_E2E_SQL_CONNECTION is required for exact opening effects.')
  const pool = await new sql.ConnectionPool(sqlConnection).connect()
  try {
    async function effects() {
      const result = await pool.request()
        .input('companyId', sql.UniqueIdentifier, companyId)
        .input('countId', sql.UniqueIdentifier, recorded.countId)
        .input('labelId', sql.UniqueIdentifier, recorded.lines[0].labelId)
        .input('entityId', sql.NVarChar, recorded.countId)
        .query(`SELECT
          (SELECT COUNT(*) FROM inventory.OpeningCounts WHERE CompanyId=@companyId AND Id=@countId) AS counts,
          (SELECT COUNT(*) FROM inventory.OpeningCountLines WHERE CompanyId=@companyId AND CountId=@countId) AS lines,
          (SELECT COUNT(*) FROM inventory.Labels WHERE CompanyId=@companyId AND Id=@labelId) AS labels,
          (SELECT LOWER(CONVERT(nvarchar(36), ProductId)) FROM inventory.Labels
            WHERE CompanyId=@companyId AND Id=@labelId) AS productId,
          (SELECT ProductVersion FROM inventory.Labels
            WHERE CompanyId=@companyId AND Id=@labelId) AS productVersion,
          (SELECT COUNT(*) FROM inventory.MovementLines
            WHERE CompanyId=@companyId AND LabelId=@labelId) AS movementLines,
          (SELECT COUNT(*) FROM inventory.Commands
            WHERE CompanyId=@companyId AND CommandName=N'inventory.opening-count.record'
              AND TargetId=@countId) AS commands,
          (SELECT COUNT(*) FROM audit.AuditEvents
            WHERE CompanyId=@companyId AND EntityId=@entityId
              AND EventType=N'inventory.opening-count.recorded.v1') AS audits`)
      expect(result.recordset).toHaveLength(1)
      return result.recordset[0] as {
        counts: number; lines: number; labels: number; productId: string; productVersion: string
        movementLines: number; commands: number; audits: number
      }
    }

    const beforeOpeningReplay = await effects()
    expect(beforeOpeningReplay).toEqual({
      counts: 1, lines: 1, labels: 1, productId: created.product.id.toLowerCase(),
      productVersion: reopened.product.version, movementLines: 1, commands: 1, audits: 1,
    })

    async function expectPrivateRefusal(response: APIResponse) {
      expect([403, 404]).toContain(response.status())
      const body = (await response.text()).toLowerCase()
      for (const identity of [created.product.id, created.product.code, name, recorded.countId,
        recorded.lines[0].labelId, recorded.lines[0].lotId, labelCode, sheetReference]) {
        expect(body).not.toContain(identity.toLowerCase())
      }
    }

    const candidatePath = `${apiBaseUrl}${INVENTORY_PRODUCT_REFERENCES_PATH}`
      + `?effectiveOn=${OPENING_DATE}&limit=25&q=${encodeURIComponent(created.product.code)}`
    const countPath = `${apiBaseUrl}${INVENTORY_OPENINGS_PATH}/${recorded.countId}`
    const labelPath = `${apiBaseUrl}${labelDetailPath}`
    const forbiddenHeaders = headersFor(requester)
    const foreignHeaders = { ...headersFor(inventoryRecorder), 'X-Company-Id': OTHER_COMPANY_ID }
    for (const [scope, headers] of [
      ['product-only', forbiddenHeaders],
      ['other-company', foreignHeaders],
    ] as const) {
      await expectPrivateRefusal(await page.request.get(candidatePath, { headers }))
      await expectPrivateRefusal(await page.request.get(countPath, { headers }))
      await expectPrivateRefusal(await page.request.get(labelPath, { headers }))
      await expectPrivateRefusal(await page.request.post(`${apiBaseUrl}${INVENTORY_OPENINGS_PATH}`, {
        headers: { ...headers, 'Idempotency-Key': `u02-genuine-opening-${scope}-${recorded.countId}` },
        data: openingPayload,
      }))
      expect(await effects()).toEqual(beforeOpeningReplay)
    }

    const openingReplay = await page.request.post(`${apiBaseUrl}${INVENTORY_OPENINGS_PATH}`, {
      headers: { ...headersFor(inventoryRecorder), 'Idempotency-Key': openingKey! },
      data: openingPayload,
    })
    expect(openingReplay.status()).toBe(200)
    expect(openingReplay.headers()['idempotency-replayed']).toBe('true')
    expect(openingReplay.headers()['idempotency-original-correlation-id']).toBe(originalOpeningCorrelation)
    expect(await openingReplay.json()).toEqual(recorded)
    expect(await effects()).toEqual(beforeOpeningReplay)
  } finally {
    await pool.close()
  }

  const freshCount = await page.request.get(`${apiBaseUrl}${INVENTORY_OPENINGS_PATH}/${recorded.countId}`,
    { headers: headersFor(inventoryRecorder) })
  const freshLabel = await page.request.get(`${apiBaseUrl}${labelDetailPath}`,
    { headers: headersFor(inventoryRecorder) })
  expect(freshCount.status()).toBe(200)
  expect(freshLabel.status()).toBe(200)
  expect(await freshCount.json()).toEqual(recorded)
  expect(await freshLabel.json()).toEqual(label)
})
