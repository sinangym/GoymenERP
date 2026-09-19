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
}

function seed(): Seed {
  const raw = process.env.BROWSER_E2E_U02_SEED?.trim()
  if (!raw) throw new Error('BROWSER_E2E_U02_SEED is not set; the u02 fixture did not run.')
  const value = JSON.parse(raw) as Seed
  for (const key of ['companyId', 'codeGroupId', 'authorizedActor', 'unauthorizedActor'] as const) {
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

test('U02-FIX-01 the authorized actor saves a draft in the fixture code group through the real API', async ({ page }) => {
  const { codeGroupId } = seed()
  const traffic = watchProducts(page)
  const groupsAnswer = page.waitForResponse((response) =>
    new URL(response.url()).pathname === `${PRODUCTS_PATH}/code-groups` && response.request().method() === 'GET')

  await page.goto('/urun/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible({ timeout: 30_000 })
  const groups = await groupsAnswer
  expect(groups.status(), 'the real code-group read').toBe(200)
  const offered = (await groups.json()) as { id: string; prefix: string }[]
  const group = offered.find((item) => item.id === codeGroupId)
  expect(group, 'the fixture code group is offered by the real read').toBeDefined()

  const name = `SENTETIK u02 fixture proof ${Date.now()}`
  await page.getByLabel('Ürün adı').fill(name)
  await page.getByLabel('Kod grubu').selectOption(codeGroupId)
  const created = page.waitForResponse((response) =>
    new URL(response.url()).pathname === PRODUCTS_PATH && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Taslak olarak kaydet' }).click()

  expect((await created).status(), 'the real create').toBe(201)
  await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
  await expect(page.getByText(new RegExp(`^${group!.prefix}-[0-9]+ — ${name}$`))).toBeVisible()
  expect(traffic.writeStatuses).toEqual([201])
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
