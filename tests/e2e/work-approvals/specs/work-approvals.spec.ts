/**
 * D-05 Sales → Onaylarım against the REAL fin01 stack (#648 seed): four profiles at 1440 and 390.
 * NO SYNTHETIC RESPONSE: the suite adds the actor's identity header, observes requests, and — for the two continuation
 * cases only — rewrites the outgoing cursor of ONE request and lets it reach the real server (`route.continue`), or
 * aborts ONE request at the network (`route.abort`). It never fulfills a body; it approves nothing.
 *
 *   WA-1 Sales-only: home entry → `kind=sales.order-draft.v1` list → detail → reload → Sales order screen (fresh owner
 *        read) → Back
 *   WA-2 Finance-only: kind-less pilot request; 50 + real cursor → 51; the batch opens Finance's own approval screen → Back
 *   WA-3 Finance-only: one aborted continuation keeps the rows; the retry sends the EXACT same real cursor
 *   WA-4 Finance-only: a malformed cursor reaches the real server, which refuses it; the list closes (no row/link) and
 *        recovers only by a fresh cursorless first page
 *   WA-5 Finance-only: company switch GDC → GSP (its empty real queue) → GDC, each read under its own company
 *   WA-6 both: the source switch keeps Finance and Sales rows apart; Back returns to the previous source
 *   WA-7 actor switch + reload: both → Sales-only → Finance-only → neither; no source survives into an actor lacking it
 *   WA-8 neither: no home entry; /onaylarim and a detail are the neutral block; zero Work reads
 * Every case: Work requests are GET only, under the committed company, of the right kind; no non-GET API request.
 */

import { expect, test, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { companyId, otherCompanyId, financeOnlyActor, salesOnlyActor, bothActor, neitherActor, salesDraftId, financeBatchId } = env.seed
const SALES_KIND = 'sales.order-draft.v1'
const FINANCE_KIND = 'finance.official-invoice-import.v1'
const FINANCE_LIST = '/api/v1/work/approvals'
const SALES_LIST = `/api/v1/work/approvals?kind=${SALES_KIND}`
const CONTINUATION = /\/api\/v1\/work\/approvals\?(?:.*&)?cursor=/
const VIEWPORTS = [[1440, 900], [390, 844]] as const

interface WorkCall {
  readonly method: string
  readonly path: string
  readonly cursor: string | null
  readonly kind: string | null
  readonly company: string | null
}

interface Observed {
  readonly work: WorkCall[]
  readonly nonGet: string[]
}

async function as(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ 'X-Development-User': actor })
}

function observe(page: Page): Observed {
  const observed: Observed = { work: [], nonGet: [] }
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (!url.pathname.startsWith('/api/')) return
    if (request.method() !== 'GET') observed.nonGet.push(`${request.method()} ${url.pathname}`)
    if (url.pathname.startsWith('/api/v1/work/')) {
      observed.work.push({
        method: request.method(),
        path: `${url.pathname}${url.search}`,
        cursor: url.searchParams.get('cursor'),
        kind: url.searchParams.get('kind'),
        company: request.headers()['x-company-id'] ?? null,
      })
    }
  })
  return observed
}

function expectReadOnly(observed: Observed) {
  expect(observed.work.filter((call) => call.method !== 'GET'), 'Work is read only').toEqual([])
  expect(observed.nonGet, 'no command of any module from the inbox journey').toEqual([])
}

const table = (page: Page) => page.getByTestId('work-approvals-table')
const rows = (page: Page) => table(page).locator('tbody tr')
const noPageOverflow = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)

async function openFinanceInbox(page: Page) {
  await page.goto('/onaylarim')
  await expect(table(page)).toHaveAttribute('data-kind', FINANCE_KIND)
  await expect(rows(page)).toHaveCount(50)
}

test.describe.configure({ mode: 'serial' })

for (const [width, height] of VIEWPORTS) {
  test.describe(`${width}px`, () => {
    test.use({ viewport: { width, height } })

    test('WA-1 Sales-only: list → detail → reload → Sales order screen → back', async ({ page }) => {
      const observed = observe(page)
      await as(page, salesOnlyActor)
      await page.goto('/')
      const entry = page.getByTestId('home-approvals')
      await expect(entry).toContainText('Satış')
      await expect(entry).not.toContainText('Finans')
      expect(observed.work, 'the home entry reads nothing').toEqual([])

      await entry.getByRole('link', { name: 'Bekleyen onaylarımı aç' }).click()
      await expect(table(page)).toHaveAttribute('data-kind', SALES_KIND)
      await expect(page.getByRole('navigation', { name: 'Onay kaynağı' })).toHaveCount(0)
      expect(await noPageOverflow(page)).toBe(true)
      const row = table(page).locator(`tr[data-approval-id="${salesDraftId}"]`)
      await expect(row).toBeVisible()

      await row.getByRole('link', { name: 'Ayrıntı' }).click()
      await expect(page).toHaveURL(new RegExp(`/onaylarim/${SALES_KIND}/${salesDraftId}$`))
      await expect(page.getByTestId('work-approval-facts')).toBeVisible()
      await page.reload()
      await expect(page.getByTestId('work-approval-facts')).toBeVisible()

      const ownerRead = page.waitForResponse((response) => new URL(response.url()).pathname === `/api/v1/sales/drafts/${salesDraftId}`
        && response.request().method() === 'GET')
      await page.getByRole('link', { name: 'Satış siparişinde aç' }).click()
      await expect(page).toHaveURL(new RegExp(`/satis/siparisler/${salesDraftId}$`))
      expect((await ownerRead).status(), 'the Sales screen reads the draft afresh').toBe(200)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await page.goBack()
      await expect(page.getByTestId('work-approval-facts')).toBeVisible()

      expect(observed.work.every((call) => call.path.startsWith(SALES_LIST) || call.path === `/api/v1/work/approvals/${SALES_KIND}/${salesDraftId}`),
        observed.work.map((call) => call.path).join('\n')).toBe(true)
      expect(observed.work.every((call) => call.company?.toLowerCase() === companyId.toLowerCase())).toBe(true)
      expectReadOnly(observed)
    })

    test('WA-2 Finance-only: kind-less list, real continuation, Finance\'s own screen → back', async ({ page }) => {
      const observed = observe(page)
      await as(page, financeOnlyActor)
      await openFinanceInbox(page)
      expect(observed.work[0]?.path).toBe(FINANCE_LIST)
      await page.getByRole('button', { name: 'Daha fazla göster' }).click()
      await expect(rows(page)).toHaveCount(51)
      await expect(page.getByText('Listenin sonu.')).toBeVisible()
      expect(await noPageOverflow(page)).toBe(true)
      const batch = table(page).locator(`tr[data-approval-id="${financeBatchId}"]`)
      await expect(batch).toHaveCount(1)
      await batch.getByRole('link', { name: 'Finans onay ekranında aç' }).click()
      await expect(page).toHaveURL(new RegExp(`/finans/resmi-fatura-aktarim-onaylari\\?approvalId=${financeBatchId}$`))
      await expect(page.getByRole('heading', { level: 1, name: 'Resmî fatura aktarım onayları' })).toBeVisible()
      await page.goBack()
      await expect(table(page)).toHaveAttribute('data-kind', FINANCE_KIND)
      expect(observed.work.some((call) => call.kind !== null), 'the Finance pilot request stays kind-less').toBe(false)
      expect(observed.work.every((call) => call.company?.toLowerCase() === companyId.toLowerCase())).toBe(true)
      expectReadOnly(observed)
    })

    test('WA-3 Finance-only: one aborted continuation, then the exact same real cursor', async ({ page }) => {
      const observed = observe(page)
      await as(page, financeOnlyActor)
      let aborted = false
      await page.route(CONTINUATION, async (route) => {
        if (aborted) return route.continue()
        aborted = true
        return route.abort('failed')
      })
      await openFinanceInbox(page)
      await page.getByRole('button', { name: 'Daha fazla göster' }).click()
      await expect(page.getByText('Sonraki sayfa şu anda alınamadı', { exact: false })).toBeVisible()
      await expect(rows(page)).toHaveCount(50)
      await page.getByRole('button', { name: 'Tekrar dene' }).click()
      await expect(rows(page)).toHaveCount(51)
      const cursors = observed.work.filter((call) => call.cursor !== null).map((call) => call.cursor)
      expect(cursors).toHaveLength(2)
      expect(cursors[1], 'the retry resends the same real cursor').toBe(cursors[0])
      expectReadOnly(observed)
    })

    test('WA-4 Finance-only: the real server refuses a malformed cursor; the list closes, then recovers fresh', async ({ page }) => {
      const observed = observe(page)
      await as(page, financeOnlyActor)
      let rewritten = false
      await page.route(CONTINUATION, async (route) => {
        if (rewritten) return route.continue()
        rewritten = true
        const url = new URL(route.request().url())
        url.searchParams.set('cursor', `${url.searchParams.get('cursor') ?? ''}~not-a-cursor`)
        return route.continue({ url: url.toString() })
      })
      await openFinanceInbox(page)
      const refusal = page.waitForResponse((response) => CONTINUATION.test(response.url()))
      await page.getByRole('button', { name: 'Daha fazla göster' }).click()
      expect((await refusal).status(), 'the real Work read refuses the cursor').toBe(404)
      await expect(page.getByTestId('work-approvals-closed')).toBeVisible()
      await expect(table(page)).toHaveCount(0)
      await expect(page.getByRole('link', { name: 'Finans onay ekranında aç' })).toHaveCount(0)
      const before = observed.work.filter((call) => call.path === FINANCE_LIST).length
      await page.getByRole('button', { name: 'Listeyi baştan yükle' }).click()
      await expect(rows(page)).toHaveCount(50)
      expect(observed.work.filter((call) => call.path === FINANCE_LIST).length, 'a fresh cursorless first page').toBe(before + 1)
      expectReadOnly(observed)
    })

    test('WA-5 Finance-only: GDC → GSP → GDC reads each company on its own', async ({ page }) => {
      const observed = observe(page)
      await as(page, financeOnlyActor)
      await openFinanceInbox(page)
      await page.getByLabel('Şirket', { exact: true }).selectOption(otherCompanyId)
      await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible()
      await expect(table(page)).toHaveCount(0)
      await page.getByLabel('Şirket', { exact: true }).selectOption(companyId)
      await expect(rows(page)).toHaveCount(50)
      const companies = observed.work.map((call) => call.company?.toLowerCase())
      expect(companies).toContain(otherCompanyId.toLowerCase())
      expect(companies.at(-1)).toBe(companyId.toLowerCase())
      expect(observed.work.every((call) => call.path === FINANCE_LIST)).toBe(true)
      expectReadOnly(observed)
    })

    test('WA-6 both: the source switch keeps each source apart; Back returns', async ({ page }) => {
      const observed = observe(page)
      await as(page, bothActor)
      await openFinanceInbox(page)
      const nav = page.getByRole('navigation', { name: 'Onay kaynağı' })
      await nav.getByRole('link', { name: 'Satış' }).click()
      await expect(table(page)).toHaveAttribute('data-kind', SALES_KIND)
      await expect(table(page).locator(`[data-approval-id="${financeBatchId}"]`)).toHaveCount(0)
      await expect(table(page).locator(`[data-approval-id="${salesDraftId}"]`)).toBeVisible()
      expect(await noPageOverflow(page)).toBe(true)
      await page.goBack()
      await expect(table(page)).toHaveAttribute('data-kind', FINANCE_KIND)
      await expect(table(page).locator(`[data-approval-id="${salesDraftId}"]`)).toHaveCount(0)
      expect(observed.work.filter((call) => call.kind === SALES_KIND).every((call) => call.path.startsWith(SALES_LIST))).toBe(true)
      expectReadOnly(observed)
    })

    test('WA-7 actor switch + reload: no source survives into an actor lacking it', async ({ page }) => {
      const observed = observe(page)
      await as(page, bothActor)
      await page.goto('/onaylarim?kaynak=satis')
      await expect(table(page)).toHaveAttribute('data-kind', SALES_KIND)

      await as(page, salesOnlyActor)
      await page.reload()
      await expect(table(page)).toHaveAttribute('data-kind', SALES_KIND)
      await expect(page.getByRole('navigation', { name: 'Onay kaynağı' })).toHaveCount(0)

      const financeFrom = observed.work.length
      await as(page, financeOnlyActor)
      await page.reload()
      await expect(table(page)).toHaveAttribute('data-kind', FINANCE_KIND)
      await expect(table(page).locator(`[data-approval-id="${salesDraftId}"]`)).toHaveCount(0)
      expect(observed.work.slice(financeFrom).some((call) => call.kind === SALES_KIND), 'no Sales read for a Finance-only actor').toBe(false)

      const neitherFrom = observed.work.length
      await as(page, neitherActor)
      await page.reload()
      await expect(page.locator('[data-state="not-found"]')).toBeVisible()
      await expect(table(page)).toHaveCount(0)
      expect(observed.work.slice(neitherFrom), 'no Work read for an actor with neither').toEqual([])
      expectReadOnly(observed)
    })

    test('WA-8 neither: no entry, neutral block, zero Work reads', async ({ page }) => {
      const observed = observe(page)
      await as(page, neitherActor)
      await page.goto('/')
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      await expect(page.getByTestId('home-approvals')).toHaveCount(0)
      await page.goto('/onaylarim')
      await expect(page.locator('[data-state="not-found"]')).toBeVisible()
      await page.goto(`/onaylarim/${SALES_KIND}/${salesDraftId}`)
      await expect(page.locator('[data-state="not-found"]')).toBeVisible()
      expect(observed.work, 'an actor with neither approval reads nothing from Work').toEqual([])
      expectReadOnly(observed)
    })
  })
}
