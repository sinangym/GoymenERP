import { expect, test, type Page } from '@playwright/test'

/**
 * Sales s03 v2a — the order-draft screens (#260) on the disposable stack, `normal` profile, against
 * the REAL API. Nothing is mocked or intercepted. This suite is EXPLICITLY NEGATIVE evidence
 * (coordinator, S03 card correction): today no real provider can complete a sale line — the Product
 * activation command is not on main (a real product is Draft only → lifecycle conflict) and the GDC
 * family source answers Unavailable (503) — so:
 *
 *  - S03-V2A-01 the granted actor reads the real, EMPTY draft list from the API;
 *  - S03-V2A-02 the granted actor's create attempt ends in the honest refusal: the accounts reference
 *    route's REAL answer is measured (200 with a list, or 403/503 with its code); currencies and
 *    products have no list route on main, so the form is withheld naming them (or, once offered,
 *    the save is refused by the owners) — NOTHING is written: no 2xx on the draft route, the list
 *    stays empty;
 *  - S03-V2A-03 the ungranted profile is closed out of the Sales screens and reads no draft.
 *
 * This is not a positive sales delivery. v2b (create → detail → list → the same key answers 201 +
 * `Idempotency-Replayed` → reopen) comes after the Product activation path and the GDC data.
 *
 * Preconditions of the stack (BROWSER-CI stage 2, real services only, no raw SQL): the shell mounts
 * the Sales routes, the Sales provider publishes the three capabilities to the shell (a staged
 * declaration is never published — `ShellCapabilityService` filters `ImplementationAvailable`), and
 * the authorized actor holds `sales.drafts.create` + `sales.drafts.read` as a disposable grant.
 */

const DRAFTS_PATH = '/api/v1/sales/drafts'
const unauthorizedBaseUrl = process.env.BROWSER_E2E_UNAUTHORIZED_BASE_URL?.trim() ?? ''

type DraftTraffic = { readonly reads: number; readonly writes: number; readonly writeStatuses: number[] }

/** Counts the browser's own traffic on the draft route: reads (GET) and writes (POST, with their statuses). */
function watchDrafts(page: Page): DraftTraffic {
  const traffic = { reads: 0, writes: 0, writeStatuses: [] as number[] }
  page.on('request', (request) => {
    if (!new URL(request.url()).pathname.startsWith(DRAFTS_PATH)) return
    if (request.method() === 'GET') traffic.reads += 1
    if (request.method() === 'POST') traffic.writes += 1
  })
  page.on('response', (response) => {
    const request = response.request()
    if (request.method() === 'POST' && new URL(request.url()).pathname.startsWith(DRAFTS_PATH)) {
      traffic.writeStatuses.push(response.status())
    }
  })
  return traffic
}

async function expectEmptyList(page: Page, traffic: DraftTraffic): Promise<void> {
  await expect(page.getByRole('heading', { level: 1, name: 'Satış siparişi taslakları' })).toBeVisible({ timeout: 30_000 })
  await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  expect(traffic.reads, 'the empty list is the real API answer, not a local default').toBeGreaterThan(0)
}

test('S03-V2A-01 the granted actor reads the real, empty draft list', async ({ page }) => {
  const traffic = watchDrafts(page)
  await page.goto('/satis/siparisler')
  await expectEmptyList(page, traffic)
  expect(traffic.writes).toBe(0)
})

test('S03-V2A-02 the granted actor\'s create attempt is refused honestly by the real owners and writes nothing', async ({ page }) => {
  const traffic = watchDrafts(page)
  // The form reads its customer options from the REAL accounts reference route (ÜRÜN #327 P2-1, a):
  // the answer is measured here, whatever it is. Currencies and products have no list route on main
  // (their Sales routes validate one chosen id), so the form is withheld with that reason.
  const accountsAnswer = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/api/v1/sales/references/accounts' && response.request().method() === 'GET')
  await page.goto('/satis/siparisler/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni satış siparişi taslağı' })).toBeVisible({ timeout: 30_000 })

  const accounts = await accountsAnswer
  expect([200, 403, 503], `accounts reference answered ${accounts.status()}`).toContain(accounts.status())
  const body = (await accounts.json()) as { accounts?: unknown; code?: unknown }
  if (accounts.status() === 200) expect(Array.isArray(body.accounts)).toBe(true)
  else expect(typeof body.code).toBe('string')

  const withheld = page.locator('[data-tone="unknown"][role="status"]')
  const form = page.locator('form')
  await expect(withheld.or(form)).toBeVisible({ timeout: 30_000 })

  if (await form.count()) {
    // Only reachable once the owners publish currency and product lists (v2b): then the save itself
    // must be refused by the owners today, never stored.
    const customer = page.getByLabel('Cari ve şirket hesabı')
    const currency = page.getByLabel('Para birimi')
    const product = page.getByLabel('Ürün').first()
    for (const control of [customer, currency, product]) {
      const offered = await control.locator('option:not([value=""])').count()
      if (offered > 0) await control.selectOption({ index: 1 })
    }
    if ((await product.inputValue()) !== '') {
      await page.getByLabel('Miktar').first().fill('1')
      const unit = page.getByLabel('Sipariş birimi').first()
      if ((await unit.locator('option:not([value=""])').count()) > 0) await unit.selectOption({ index: 1 })
    }
    await page.getByRole('button', { name: /kaydet/i }).click()
    await expect(page.getByRole('alert').or(page.locator('[data-tone="unknown"]'))).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('heading', { level: 1, name: 'Sipariş taslağı kaydedildi.' })).toHaveCount(0)
  } else {
    // The withheld notice names the missing sources from the real answers: the accounts answer's
    // status when it refused, and the owners' missing list routes.
    const detail = page.getByTestId('sales-references-missing')
    await expect(detail).toBeVisible()
    await expect(detail).toContainText('para birimleri')
    await expect(detail).toContainText('satışa uygun ürünler ve birimler')
    if (accounts.status() !== 200) await expect(detail).toContainText(`gerçek yanıt http-${accounts.status()}`)
  }

  expect(traffic.writeStatuses.filter((status) => status >= 200 && status < 300), 'no draft may be stored today').toHaveLength(0)
  await page.goto('/satis/siparisler')
  await expectEmptyList(page, traffic)
})

test('S03-V2A-03 the ungranted profile is closed out of the Sales screens and reads no draft', async ({ page }) => {
  expect(unauthorizedBaseUrl, 'BROWSER_E2E_UNAUTHORIZED_BASE_URL is not set').not.toBe('')
  const traffic = watchDrafts(page)
  for (const route of ['/satis/siparisler', '/satis/siparisler/yeni']) {
    await page.goto(new URL(route, unauthorizedBaseUrl).toString())
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('table')).toHaveCount(0)
  }
  expect(traffic.reads + traffic.writes).toBe(0)
})
