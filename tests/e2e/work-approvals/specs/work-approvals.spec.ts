/**
 * D-05 Sales → Onaylarım against the REAL stack: Sales-only, Finance-only, both and neither, at 1440 and 390.
 * NOTHING IS MOCKED OR INTERCEPTED: the suite adds only the actor's identity header; requests are only observed.
 *
 *   WA-1 Sales-only: home entry names only Satış → inbox asks `kind=sales.order-draft.v1` → detail (item only) → fresh
 *        reload → "Satış siparişinde aç" opens /satis/siparisler/{draftId}, which reads the draft afresh → Back
 *   WA-2 Finance-only: the pilot request stays kind-less; the record opens Finance's own approval screen
 *   WA-3 both: the source switch keeps each source's rows apart; Back returns to the previous source
 *   WA-4 neither: no home entry; /onaylarim is the neutral not-found block; zero Work reads
 * Across every case: zero Work POSTs, zero non-GET requests from the inbox, and every Work read under the committed
 * company.
 */

import { expect, test, type Page } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { financeOnlyActor, salesOnlyActor, bothActor, neitherActor, companyId, salesDraftId, financeBatchId } = env.seed
const SALES_KIND = 'sales.order-draft.v1'
const FINANCE_KIND = 'finance.official-invoice-import.v1'
const VIEWPORTS = [[1440, 900], [390, 844]] as const

interface Observed {
  readonly work: string[]
  readonly workCompanies: (string | null)[]
  readonly nonGet: string[]
  readonly salesReads: string[]
}

async function signIn(page: Page, actor: string, width: number, height: number): Promise<Observed> {
  await page.setViewportSize({ width, height })
  await page.setExtraHTTPHeaders({ 'X-Development-User': actor })
  const observed: Observed = { work: [], workCompanies: [], nonGet: [], salesReads: [] }
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (!url.pathname.startsWith('/api/')) return
    if (url.pathname.startsWith('/api/v1/work/')) {
      observed.work.push(`${request.method()} ${url.pathname}${url.search}`)
      observed.workCompanies.push(request.headers()['x-company-id'] ?? null)
    }
    if (request.method() !== 'GET') observed.nonGet.push(`${request.method()} ${url.pathname}`)
    if (url.pathname === `/api/v1/sales/drafts/${salesDraftId}`) observed.salesReads.push(request.method())
  })
  return observed
}

function expectReadOnlyAndFenced(observed: Observed) {
  expect(observed.work.filter((call) => !call.startsWith('GET ')), 'no Work command').toEqual([])
  expect(observed.workCompanies.every((company) => company?.toLowerCase() === companyId.toLowerCase())).toBe(true)
}

test.describe.configure({ mode: 'serial' })

for (const [width, height] of VIEWPORTS) {
  test(`WA-1 Sales-only approver, ${width}px: inbox → detail → Sales order screen → back`, async ({ page }) => {
    const observed = await signIn(page, salesOnlyActor, width, height)
    await page.goto('/')
    const entry = page.getByTestId('home-approvals')
    await expect(entry).toContainText('Satış')
    await expect(entry).not.toContainText('Finans')
    expect(observed.work, 'the home entry reads nothing').toEqual([])

    await entry.getByRole('link', { name: 'Bekleyen onaylarımı aç' }).click()
    const table = page.getByTestId('work-approvals-table')
    await expect(table).toHaveAttribute('data-kind', SALES_KIND)
    await expect(page.getByRole('navigation', { name: 'Onay kaynağı' })).toHaveCount(0)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
    const row = table.locator(`tr[data-approval-id="${salesDraftId}"]`)
    await expect(row).toBeVisible()

    await row.getByRole('link', { name: 'Ayrıntı' }).click()
    await expect(page).toHaveURL(new RegExp(`/onaylarim/${SALES_KIND}/${salesDraftId}$`))
    await expect(page.getByTestId('work-approval-facts')).toBeVisible()
    await page.reload()
    await expect(page.getByTestId('work-approval-facts')).toBeVisible()

    const nativeRead = page.waitForResponse((response) =>
      new URL(response.url()).pathname === `/api/v1/sales/drafts/${salesDraftId}` && response.request().method() === 'GET')
    await page.getByRole('link', { name: 'Satış siparişinde aç' }).click()
    await expect(page).toHaveURL(new RegExp(`/satis/siparisler/${salesDraftId}$`))
    expect((await nativeRead).status(), 'the Sales screen reads the draft afresh').toBe(200)
    await page.goBack()
    await expect(page.getByTestId('work-approval-facts')).toBeVisible()

    expect(observed.work.every((call) => call.startsWith(`GET /api/v1/work/approvals?kind=${SALES_KIND}`)
      || call === `GET /api/v1/work/approvals/${SALES_KIND}/${salesDraftId}`), observed.work.join('\n')).toBe(true)
    expectReadOnlyAndFenced(observed)
  })

  test(`WA-2 Finance-only approver, ${width}px: the pilot request and Finance's own screen`, async ({ page }) => {
    const observed = await signIn(page, financeOnlyActor, width, height)
    await page.goto('/onaylarim')
    const table = page.getByTestId('work-approvals-table')
    await expect(table).toHaveAttribute('data-kind', FINANCE_KIND)
    await expect(table.locator(`[data-approval-id="${salesDraftId}"]`)).toHaveCount(0)
    await table.locator(`tr[data-approval-id="${financeBatchId}"]`).getByRole('link', { name: 'Finans onay ekranında aç' }).click()
    await expect(page).toHaveURL(new RegExp(`/finans/resmi-fatura-aktarim-onaylari\\?approvalId=${financeBatchId}$`))
    expect(observed.work[0]).toBe('GET /api/v1/work/approvals')
    expect(observed.work.some((call) => call.includes(`kind=${SALES_KIND}`))).toBe(false)
    expectReadOnlyAndFenced(observed)
  })

  test(`WA-3 approver of both, ${width}px: the source switch keeps each source apart`, async ({ page }) => {
    const observed = await signIn(page, bothActor, width, height)
    await page.goto('/onaylarim')
    const nav = page.getByRole('navigation', { name: 'Onay kaynağı' })
    const table = page.getByTestId('work-approvals-table')
    await expect(table).toHaveAttribute('data-kind', FINANCE_KIND)
    await nav.getByRole('link', { name: 'Satış' }).click()
    await expect(table).toHaveAttribute('data-kind', SALES_KIND)
    await expect(table.locator(`[data-approval-id="${financeBatchId}"]`)).toHaveCount(0)
    await expect(table.locator(`[data-approval-id="${salesDraftId}"]`)).toBeVisible()
    await page.goBack()
    await expect(table).toHaveAttribute('data-kind', FINANCE_KIND)
    await expect(table.locator(`[data-approval-id="${salesDraftId}"]`)).toHaveCount(0)
    expectReadOnlyAndFenced(observed)
  })

  test(`WA-4 neither, ${width}px: no entry, neutral block, zero Work reads`, async ({ page }) => {
    const observed = await signIn(page, neitherActor, width, height)
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(page.getByTestId('home-approvals')).toHaveCount(0)
    await page.goto('/onaylarim')
    await expect(page.locator('[data-state="not-found"]')).toBeVisible()
    await page.goto(`/onaylarim/${SALES_KIND}/${salesDraftId}`)
    await expect(page.locator('[data-state="not-found"]')).toBeVisible()
    expect(observed.work, 'an unauthorized actor reads nothing from Work').toEqual([])
    expect(observed.nonGet).toEqual([])
  })
}
