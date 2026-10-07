import { randomUUID } from 'node:crypto'
import { expect, test, type Response } from '@playwright/test'
import { readEnvironment } from '../src/env'

/**
 * AT-002: real exact-code search → existing Inventory detail → Back/fresh search.
 * Uses the existing disposable R14 recorder and genuine Product-backed opening command.
 * No route fulfillment, fixture/permission changes, fuzzy lookup or direct database writes.
 * Run only on the combined backend + consumer source, not on this frontend donor alone.
 */
const env = readEnvironment()
const { recorderActor, deniedActor, companyId, locationId, productId, productVersion } = env.seed
const USER = 'X-Development-User'
const SEARCH = '/api/v1/search'
const OPENINGS = '/api/v1/inventory/opening-counts'
const KIND = 'inventory.material-label'
const STOCK_CAPABILITY = 'inventory.stock-positions.list'
const LABEL_CODE = 'SYNR14-SEARCH-EXACT-1'

const headers = (actor: string) => ({ [USER]: actor, 'X-Company-Id': companyId })
const isSearch = (response: Response, term: string) => {
  const url = new URL(response.url())
  return response.request().method() === 'GET' && url.pathname === SEARCH && url.searchParams.get('q') === term
}
interface SearchGroup {
  readonly kind: string
  readonly items: readonly { readonly id: string; readonly code: string; readonly name: string | null;
    readonly secondaryName: string | null; readonly state: string; readonly date: string | null }[]
  readonly hasMore: boolean
}
interface SearchResponse { readonly query: string; readonly groups: readonly SearchGroup[] }

function inventoryGroup(body: SearchResponse): SearchGroup {
  const groups = body.groups.filter((group) => group.kind === KIND)
  expect(groups).toHaveLength(1)
  return groups[0]
}

test('R14 exact material-label search opens the stored label, rereads Back, and hides unauthorized results', async ({ page }) => {
  // Existing authority only; an operational Product selection grant is not a substitute for either Inventory read.
  for (const actor of [recorderActor, deniedActor]) {
    const capabilities = await page.request.get(`${env.apiBaseUrl}/api/v1/shell/capabilities`, { headers: headers(actor) })
    expect(capabilities.status()).toBe(200)
    const body = await capabilities.json() as { readonly capabilities: readonly string[] }
    expect(body.capabilities.includes(STOCK_CAPABILITY)).toBe(actor === recorderActor)
  }

  const countId = randomUUID()
  const labelId = randomUUID()
  const opening = await page.request.post(`${env.apiBaseUrl}${OPENINGS}`, {
    headers: { ...headers(recorderActor), 'Idempotency-Key': `r14-search-opening-${countId}` },
    data: {
      countId, locationId, countedOn: '2026-09-18', sheetReference: 'SENTETIK browser-suite:r14 exact label search',
      lines: [{ lineId: randomUUID(), labelId, labelCode: LABEL_CODE, lotId: randomUUID(),
        heatNumber: 'SYNR14-SEARCH-HEAT', coilNumber: null, productId, productVersion, quantityKg: '10' }],
    },
  })
  expect(opening.status()).toBe(201)
  const openingBody = await opening.json()
  expect(openingBody).toMatchObject({ countId, companyId, lines: [{ labelId, labelCode: LABEL_CODE, productId, quantityKg: '10' }] })
  const detailPath = `/api/v1/inventory/labels/${labelId}/detail`
  const before = await page.request.get(`${env.apiBaseUrl}${detailPath}`, { headers: headers(recorderActor) })
  expect(before.status()).toBe(200)
  const beforeBody = await before.json()
  expect(beforeBody.identity).toMatchObject({ labelId, companyId, productId, code: LABEL_CODE, documentStatus: 'Complete' })
  expect(beforeBody.balances).toMatchObject({ labelId, physicalKg: '10' })

  await page.setExtraHTTPHeaders({ [USER]: recorderActor })
  await page.goto('/')
  const box = page.getByRole('search', { name: 'Genel arama', exact: true })
  await expect(box).toBeVisible({ timeout: 30_000 })
  await box.getByRole('textbox', { name: 'Genel arama', exact: true }).fill(LABEL_CODE)
  const answer = page.waitForResponse((response) => isSearch(response, LABEL_CODE))
  await box.getByRole('button', { name: 'Genel aramada ara', exact: true }).click()
  const response = await answer
  expect(response.status()).toBe(200)
  expect(response.request().headers()['x-company-id']).toBe(companyId)
  const body = await response.json() as SearchResponse
  expect(body.query).toBe(LABEL_CODE)
  const group = inventoryGroup(body)
  expect(group).toEqual({ kind: KIND, items: [{ id: labelId, code: LABEL_CODE, name: null,
    secondaryName: null, state: 'Complete', date: null }], hasMore: false })
  await expect(page).toHaveURL(new RegExp(`/arama\\?q=${LABEL_CODE}$`))
  const region = page.getByRole('region', { name: 'Stok etiketleri', exact: true })
  await expect(region.getByText('Belgeler tamam', { exact: true })).toBeVisible()
  const link = region.getByRole('link', { name: LABEL_CODE, exact: true })
  await expect(link).toHaveAttribute('href', `/stok/etiket/${labelId}`)

  const detailAnswer = page.waitForResponse((r) => r.request().method() === 'GET' && new URL(r.url()).pathname === detailPath)
  await link.click()
  const detail = await detailAnswer
  expect(detail.status()).toBe(200)
  expect(await detail.json()).toEqual(beforeBody)
  await expect(page).toHaveURL(new RegExp(`/stok/etiket/${labelId}$`))
  await expect(page.getByRole('heading', { level: 1, name: LABEL_CODE, exact: true })).toBeVisible()

  const freshAnswer = page.waitForResponse((r) => isSearch(r, LABEL_CODE))
  await page.goBack()
  const freshResponse = await freshAnswer
  expect(freshResponse.status()).toBe(200)
  expect(inventoryGroup(await freshResponse.json() as SearchResponse)).toEqual(group)
  await expect(page).toHaveURL(new RegExp(`/arama\\?q=${LABEL_CODE}$`))
  await expect(page.getByRole('region', { name: 'Stok etiketleri', exact: true })
    .getByRole('link', { name: LABEL_CODE, exact: true })).toBeVisible()

  // A prefix is not an exact code. Inspect the actual backend answer and the rendered group.
  const prefix = LABEL_CODE.slice(0, -1)
  const field = page.getByRole('searchbox', { name: 'Aranan metin', exact: true })
  await field.fill(prefix)
  const prefixAnswer = page.waitForResponse((r) => isSearch(r, prefix))
  await page.getByRole('search', { name: 'Aranan metin', exact: true }).getByRole('button', { name: 'Ara', exact: true }).click()
  const prefixResponse = await prefixAnswer
  expect(prefixResponse.status()).toBe(200)
  expect(inventoryGroup(await prefixResponse.json() as SearchResponse)).toEqual({ kind: KIND, items: [], hasMore: false })
  await expect(page.getByRole('region', { name: 'Stok etiketleri', exact: true })
    .getByText('Eşleşen kayıt yok.', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: LABEL_CODE, exact: true })).toHaveCount(0)

  const denied = await page.request.get(`${env.apiBaseUrl}${SEARCH}?q=${LABEL_CODE}`, { headers: headers(deniedActor) })
  expect(denied.status()).toBe(200)
  const deniedBody = await denied.json() as SearchResponse
  expect(deniedBody.query).toBe(LABEL_CODE)
  expect(deniedBody.groups.filter((g) => g.kind === KIND)).toEqual([])
  expect(JSON.stringify(deniedBody.groups)).not.toContain(labelId)
  expect(JSON.stringify(deniedBody.groups)).not.toContain(LABEL_CODE)
  await page.setExtraHTTPHeaders({ [USER]: deniedActor })
  await page.goto(`/arama?q=${LABEL_CODE}`)
  await expect(page.locator('[data-state="not-found"]')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Stok etiketleri', exact: true })).toHaveCount(0)
  await expect(page.getByRole('link', { name: LABEL_CODE, exact: true })).toHaveCount(0)
  await expect(page.getByRole('search', { name: 'Genel arama', exact: true })).toHaveCount(0)

  // Search/navigation is read-only: no second receipt, changed label version, quantity, genealogy or movement timeline.
  const after = await page.request.get(`${env.apiBaseUrl}${detailPath}`, { headers: headers(recorderActor) })
  expect(after.status()).toBe(200)
  expect(await after.json()).toEqual(beforeBody)
  const count = await page.request.get(`${env.apiBaseUrl}${OPENINGS}/${countId}`, { headers: headers(recorderActor) })
  expect(count.status()).toBe(200)
  expect(await count.json()).toEqual(openingBody)
})
