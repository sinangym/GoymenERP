import { expect, test, type Page } from '@playwright/test'
import sql from 'mssql'
import { readEnvironment } from '../src/env'

const env = readEnvironment()
const { recorderActor, deniedActor, companyId, otherCompanyId, locationId, productId,
  productVersion, productUnitId, productUnitProfileId, productUnitProfileVersion,
  productCode, productName } = env.seed
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const HOME = '/stok/acilis'
const OPENINGS = '/api/v1/inventory/opening-counts'
const PRODUCT_REFERENCES = '/api/v1/inventory/references/products'
const LOCATION_REFERENCES = '/api/v1/inventory/references/locations'
const EFFECTIVE_ON = '2026-09-18'
const SHEET = 'SENTETIK browser-suite:r14 opening count'
const LABEL = 'SYNR14-LABEL-1'

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function apiHeaders(actor: string, company = companyId): Record<string, string> {
  return { [DEVELOPMENT_USER_HEADER]: actor, 'X-Company-Id': company }
}

test('R14 records one real opening count, closes and reopens it, and preserves permission/company fences', async ({ page }) => {
  const pool = await new sql.ConnectionPool(env.sqlConnection).connect()
  try {
    const baseline = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('sheet', sql.NVarChar, SHEET)
      .query<{ count: number }>('SELECT COUNT(*) AS count FROM inventory.OpeningCounts WHERE CompanyId=@companyId AND SheetReference=@sheet')
    expect(baseline.recordset[0].count).toBe(0)

    await signInAs(page, recorderActor)
    await page.goto(HOME)
    await expect(page.getByRole('heading', { level: 1, name: 'İlk stok / açılış sayımı' })).toBeVisible({ timeout: 30_000 })
    await page.getByRole('link', { name: 'Yeni açılış sayımı', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni açılış sayımı' })).toBeVisible()
    await page.getByLabel('Sayım tarihi').fill(EFFECTIVE_ON)
    await page.getByLabel('Belge / sayım referansı').fill(SHEET)

    const locationAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === LOCATION_REFERENCES)
    await page.getByRole('button', { name: 'Lokasyonları getir', exact: true }).click()
    const locationResponse = await locationAnswer
    expect(locationResponse.status()).toBe(200)
    const locationBody = await locationResponse.json() as { readonly items: readonly { readonly locationId: string }[] }
    expect(locationBody.items.filter((item) => item.locationId === locationId)).toHaveLength(1)
    await page.getByLabel('Lokasyon', { exact: true }).selectOption(locationId)

    await page.getByLabel('Ürün ara').fill(productCode)
    const productAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === PRODUCT_REFERENCES)
    await page.getByRole('button', { name: 'Ürünleri getir', exact: true }).click()
    const productResponse = await productAnswer
    expect(productResponse.status()).toBe(200)
    expect(await productResponse.json()).toMatchObject({
      items: [{ productId, productVersion, code: productCode, name: productName,
        stockUnitId: productUnitId, profileId: productUnitProfileId,
        profileVersion: productUnitProfileVersion }],
    })
    await page.getByLabel('Ürün', { exact: true }).selectOption(productId)
    await page.getByLabel('Etiket kodu').fill(LABEL)
    await page.getByLabel('Sayım kg').fill('12.5')

    const createAnswer = page.waitForResponse((response) => response.request().method() === 'POST'
      && new URL(response.url()).pathname === OPENINGS)
    await page.getByRole('button', { name: 'Açılış sayımını kaydet', exact: true }).click()
    const createResponse = await createAnswer
    expect(createResponse.status()).toBe(201)
    const created = await createResponse.json() as {
      readonly countId: string; readonly companyId: string; readonly locationId: string
      readonly sheetReference: string; readonly lines: readonly {
        readonly lineId: string; readonly labelId: string; readonly labelCode: string
        readonly lotId: string; readonly productId: string; readonly quantityKg: string
      }[]
    }
    expect(created).toMatchObject({ companyId, locationId, sheetReference: SHEET,
      lines: [{ labelCode: LABEL, productId, quantityKg: '12.5' }] })
    expect(created.lines).toHaveLength(1)
    await expect(page.getByRole('heading', { level: 1, name: 'Açılış sayımı kaydedildi' })).toBeVisible()

    await page.getByRole('link', { name: 'Formu kapat', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'İlk stok / açılış sayımı' })).toBeVisible()
    await page.getByRole('link', { name: 'Son kaydı yeniden aç', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Açılış sayımı' })).toBeVisible()
    await expect(page.getByText(created.countId, { exact: true })).toBeVisible()
    await expect(page.getByRole('cell', { name: new RegExp(`^${LABEL}(?:\\s|$)`) })).toBeVisible()

    const read = await page.request.get(`${env.apiBaseUrl}${OPENINGS}/${created.countId}`, {
      headers: apiHeaders(recorderActor),
    })
    expect(read.status()).toBe(200)
    expect(await read.json()).toEqual(created)

    // Follow the actual stored label, not a synthetic id or a substituted route response.
    const actualLabelId = created.lines[0].labelId
    const labelPath = `/api/v1/inventory/labels/${actualLabelId}/detail`
    const labelLink = page.getByRole('link', { name: LABEL, exact: true })
    await expect(labelLink).toHaveAttribute('href', `/stok/etiket/${actualLabelId}`)
    const labelAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === labelPath)
    await labelLink.click()
    const labelResponse = await labelAnswer
    expect(labelResponse.status()).toBe(200)
    const labelDetail = await labelResponse.json() as {
      readonly labelVersion: string
      readonly identity: { readonly labelId: string; readonly companyId: string; readonly productId: string; readonly code: string }
      readonly balances: { readonly labelId: string; readonly physicalKg: string }
      readonly genealogy: { readonly labelId: string; readonly labelCode: string; readonly edges: readonly unknown[]; readonly windowFull: boolean }
      readonly movementTimeline: { readonly labelId: string; readonly windowFull: boolean; readonly entries: readonly [{
        readonly kind: string; readonly sourceKind: string; readonly lines: readonly [{ readonly direction: string; readonly quantityKg: string }]
      }] }
    }
    expect(labelDetail.identity).toMatchObject({ labelId: actualLabelId, companyId, productId, code: LABEL })
    expect(labelDetail.balances).toMatchObject({ labelId: actualLabelId, physicalKg: '12.5' })
    expect(labelDetail.genealogy).toEqual({ labelId: actualLabelId, labelCode: LABEL, edges: [], windowFull: false })
    expect(labelDetail.movementTimeline).toMatchObject({
      labelId: actualLabelId, windowFull: false,
      entries: [{ kind: 'Receipt', sourceKind: 'inventory.opening-count', lines: [{ direction: 'In', quantityKg: '12.5' }] }],
    })
    await expect(page.getByRole('heading', { level: 1, name: LABEL, exact: true })).toBeVisible()
    await expect(page.getByText(actualLabelId, { exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: 'Soy ağacı', exact: true })).toBeVisible()
    await expect(page.getByText('Bu etiket için bölme veya birleştirme kaydı yok.', { exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: 'Stok hareketleri', exact: true })).toBeVisible()

    const productLink = page.getByRole('link', { name: 'Ürün kartı', exact: true })
    await expect(productLink).toHaveAttribute('href', `/urun/${productId}`)
    const productCardAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === `/api/v1/products/${productId}`)
    await productLink.click()
    const productCardResponse = await productCardAnswer
    expect(productCardResponse.status()).toBe(200)
    expect(await productCardResponse.json()).toMatchObject({ product: { id: productId, code: productCode, name: productName } })
    await expect(page.getByRole('heading', { level: 1, name: productName, exact: true })).toBeVisible()

    const returnedLabelAnswer = page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === labelPath)
    await page.goBack()
    const returnedLabelResponse = await returnedLabelAnswer
    expect(returnedLabelResponse.status()).toBe(200)
    expect(await returnedLabelResponse.json()).toMatchObject({
      labelVersion: labelDetail.labelVersion,
      identity: { labelId: actualLabelId, companyId, productId, code: LABEL },
      balances: { labelId: actualLabelId, physicalKg: '12.5' },
      genealogy: { labelId: actualLabelId, labelCode: LABEL, edges: [], windowFull: false },
      movementTimeline: {
        labelId: actualLabelId, windowFull: false,
        entries: [{ kind: 'Receipt', sourceKind: 'inventory.opening-count', lines: [{ direction: 'In', quantityKg: '12.5' }] }],
      },
    })
    await expect(page.getByRole('heading', { level: 1, name: LABEL, exact: true })).toBeVisible()
    await expect(page.getByText(actualLabelId, { exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: 'Soy ağacı', exact: true })).toBeVisible()

    const stored = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('countId', sql.UniqueIdentifier, created.countId)
      .query<{ counts: number; lines: number }>(`
        SELECT
          (SELECT COUNT(*) FROM inventory.OpeningCounts WHERE CompanyId=@companyId AND Id=@countId) AS counts,
          (SELECT COUNT(*) FROM inventory.OpeningCountLines WHERE CompanyId=@companyId AND CountId=@countId) AS lines`)
    expect(stored.recordset[0]).toEqual({ counts: 1, lines: 1 })

    const denied = await page.request.get(`${env.apiBaseUrl}${PRODUCT_REFERENCES}?effectiveOn=${EFFECTIVE_ON}&limit=25`, {
      headers: apiHeaders(deniedActor),
    })
    expect(denied.status()).toBe(403)
    expect(await denied.text()).not.toContain(productName)
    const elsewhere = await page.request.get(`${env.apiBaseUrl}${OPENINGS}/${created.countId}`, {
      headers: apiHeaders(recorderActor, otherCompanyId),
    })
    expect([403, 404]).toContain(elsewhere.status())
    expect(await elsewhere.text()).not.toContain(LABEL)

    // The denied actor did not gain either detail permission from the recorder's new test grants.
    for (const path of [labelPath, `/api/v1/products/${productId}`]) {
      const deniedDetail = await page.request.get(`${env.apiBaseUrl}${path}`, { headers: apiHeaders(deniedActor) })
      expect(deniedDetail.status()).toBe(403)
      const body = await deniedDetail.text()
      expect(body).not.toContain(LABEL)
      expect(body).not.toContain(productName)
      expect(body).not.toContain(actualLabelId)
    }
    const foreignLabel = await page.request.get(`${env.apiBaseUrl}${labelPath}`, {
      headers: apiHeaders(recorderActor, otherCompanyId),
    })
    expect([403, 404]).toContain(foreignLabel.status())
    const foreignBody = await foreignLabel.text()
    expect(foreignBody).not.toContain(LABEL)
    expect(foreignBody).not.toContain(actualLabelId)

    const afterNegatives = await pool.request()
      .input('companyId', sql.UniqueIdentifier, companyId)
      .input('countId', sql.UniqueIdentifier, created.countId)
      .query<{ counts: number; lines: number }>(`
        SELECT
          (SELECT COUNT(*) FROM inventory.OpeningCounts WHERE CompanyId=@companyId AND Id=@countId) AS counts,
          (SELECT COUNT(*) FROM inventory.OpeningCountLines WHERE CompanyId=@companyId AND CountId=@countId) AS lines`)
    expect(afterNegatives.recordset[0]).toEqual({ counts: 1, lines: 1 })
  } finally {
    await pool.close()
  }
})
