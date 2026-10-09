/**
 * Test-owner boundary cases for D-05. The eight owner journeys stay unchanged in
 * work-approvals.spec.ts; these cases exercise the real API around their sharp edges.
 * No response body is mocked: a deliberate network abort is retried against the same
 * real cursor, and invalid cursors are forwarded to the real API for its own refusal.
 */

import { expect, test, type Page, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { financeOnlyActor, bothActor, neitherActor, companyId, financeBatchId } = env.seed
const FINANCE_KIND = 'finance.official-invoice-import.v1'
const SALES_KIND = 'sales.order-draft.v1'
const WORK_PATH = '/api/v1/work/approvals'
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const rawSeed = JSON.parse(process.env.BROWSER_E2E_FIN01_SEED ?? 'null') as Record<string, unknown> | null
const otherCompanyId = rawSeed?.otherCompanyId
if (typeof otherCompanyId !== 'string' || !GUID.test(otherCompanyId)) {
  throw new Error('BROWSER_E2E_FIN01_SEED field "otherCompanyId" is missing or malformed')
}

interface WorkRead {
  readonly method: string
  readonly companyId: string | null
  readonly url: URL
}

async function signIn(page: Page, actor: string): Promise<WorkRead[]> {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.setExtraHTTPHeaders({ 'X-Development-User': actor })
  const reads: WorkRead[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/v1/work/')) {
      reads.push({ method: request.method(), companyId: request.headers()['x-company-id'] ?? null, url })
    }
  })
  return reads
}

async function selectCompany(page: Page, id: string): Promise<void> {
  const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
  await picker.selectOption(id)
  const dialog = page.getByRole('dialog')
  if (await dialog.count()) await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
  await expect(picker).toHaveValue(id, { timeout: 30_000 })
}

function invalidCursorUrl(requestUrl: string, value: string): string {
  const url = new URL(requestUrl)
  url.searchParams.set('cursor', value)
  return url.toString()
}

test.describe.configure({ mode: 'serial' })

test('WA-B1 actor and GDC → GSP → GDC switches never retain the previous private Work rows', async ({ page }) => {
  const reads = await signIn(page, financeOnlyActor)
  await page.goto('/onaylarim')
  const table = page.getByTestId('work-approvals-table')
  await expect(table).toHaveAttribute('data-kind', FINANCE_KIND)
  await expect(table.locator(`tr[data-approval-id="${financeBatchId}"]`)).toBeVisible()

  await selectCompany(page, otherCompanyId)
  await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible()
  await expect(page.locator(`[data-approval-id="${financeBatchId}"]`)).toHaveCount(0)

  await selectCompany(page, companyId)
  await expect(table.locator(`tr[data-approval-id="${financeBatchId}"]`)).toBeVisible()
  expect(reads.some((read) => read.companyId?.toLowerCase() === otherCompanyId.toLowerCase())).toBe(true)
  expect(reads.every((read) => read.method === 'GET')).toBe(true)

  const beforeActorSwitch = reads.length
  await page.setExtraHTTPHeaders({ 'X-Development-User': neitherActor })
  await page.reload()
  await expect(page.locator('[data-state="not-found"]')).toBeVisible()
  expect(reads.slice(beforeActorSwitch), 'the unauthorized replacement actor issues no Work read').toEqual([])
})

test('WA-B2 one aborted continuation retries the same real cursor and keeps the verified rows', async ({ page }) => {
  const reads = await signIn(page, financeOnlyActor)
  let abortedCursor: string | null = null
  let retriedCursor: string | null = null
  await page.route('**/api/v1/work/approvals*', async (route) => {
    const url = new URL(route.request().url())
    const cursor = url.searchParams.get('cursor')
    if (url.pathname !== WORK_PATH || cursor === null || url.searchParams.has('kind')) {
      await route.continue()
      return
    }
    if (abortedCursor === null) {
      abortedCursor = cursor
      await route.abort('failed')
      return
    }
    retriedCursor = cursor
    await route.continue()
  })

  await page.goto('/onaylarim')
  const table = page.getByTestId('work-approvals-table')
  await expect(table.locator('tbody tr')).toHaveCount(50)
  await page.getByRole('button', { name: 'Daha fazla göster' }).click()
  await expect(page.locator('[data-state="network-error"]')).toBeVisible()
  await expect(table.locator('tbody tr')).toHaveCount(50)

  await page.getByRole('button', { name: 'Tekrar dene' }).click()
  await expect(table.locator('tbody tr')).toHaveCount(51)
  await expect(page.getByText('Listenin sonu.', { exact: true })).toBeVisible()
  expect(retriedCursor).toBe(abortedCursor)
  expect(reads.every((read) => read.method === 'GET')).toBe(true)
})

test('WA-B3 an abandoned late terminal answer cannot close or roll back the recovered generation', async ({ page }) => {
  const reads = await signIn(page, bothActor)
  await page.addInitScript(() => {
    const originalJson = Response.prototype.json
    Response.prototype.json = async function json() {
      const body = await originalJson.call(this)
      const url = new URL(this.url)
      if (url.pathname === '/api/v1/work/approvals' && url.searchParams.has('cursor') && !url.searchParams.has('kind')) {
        const state = globalThis as typeof globalThis & { __goymenFinanceContinuationJsonReads?: number }
        state.__goymenFinanceContinuationJsonReads = (state.__goymenFinanceContinuationJsonReads ?? 0) + 1
      }
      return body
    }
  })
  let financeCursorCalls = 0
  let releaseOld: (() => void) | null = null
  let oldRequestObserved: (() => void) | null = null
  const oldRequestReady = new Promise<void>((resolve) => { oldRequestObserved = resolve })
  const oldRequestGate = new Promise<void>((resolve) => { releaseOld = resolve })

  await page.route('**/api/v1/work/approvals*', async (route: Route) => {
    const url = new URL(route.request().url())
    if (url.pathname !== WORK_PATH || !url.searchParams.has('cursor') || url.searchParams.has('kind')) {
      await route.continue()
      return
    }
    financeCursorCalls += 1
    if (financeCursorCalls === 1) {
      oldRequestObserved?.()
      await oldRequestGate
      await route.continue({ url: invalidCursorUrl(route.request().url(), 'invalid-late-terminal') })
      return
    }
    await route.continue({
      url: invalidCursorUrl(route.request().url(), `invalid-current-terminal-${financeCursorCalls}`),
    })
  })

  await page.goto('/onaylarim')
  const table = page.getByTestId('work-approvals-table')
  const nav = page.getByRole('navigation', { name: 'Onay kaynağı' })
  await expect(table).toHaveAttribute('data-kind', FINANCE_KIND)
  await page.getByRole('button', { name: 'Daha fazla göster' }).click()
  await oldRequestReady

  await nav.getByRole('link', { name: 'Satış' }).click()
  await expect(table).toHaveAttribute('data-kind', SALES_KIND)
  await nav.getByRole('link', { name: 'Finans' }).click()
  await expect(table).toHaveAttribute('data-kind', FINANCE_KIND)

  await page.getByRole('button', { name: 'Daha fazla göster' }).click()
  await expect(page.getByTestId('work-approvals-closed')).toBeVisible()
  await expect(table).toHaveCount(0)
  await page.getByRole('button', { name: 'Listeyi baştan yükle' }).click()
  await expect(table).toHaveAttribute('data-kind', FINANCE_KIND)
  await expect(table.locator(`tr[data-approval-id="${financeBatchId}"]`)).toBeVisible()

  const firstPageReadsBeforeLateAnswer = reads.filter((read) =>
    read.url.pathname === WORK_PATH && !read.url.searchParams.has('cursor') && !read.url.searchParams.has('kind')).length
  expect(await page.evaluate(() =>
    (globalThis as typeof globalThis & { __goymenFinanceContinuationJsonReads?: number })
      .__goymenFinanceContinuationJsonReads)).toBe(1)
  releaseOld?.()
  // The current generation's terminal body is read first. The held old request is the second
  // Finance continuation body, independently of Playwright's route URL rewrite visibility.
  await page.waitForFunction(() =>
    (globalThis as typeof globalThis & { __goymenFinanceContinuationJsonReads?: number })
      .__goymenFinanceContinuationJsonReads === 2)
  await expect(page.getByTestId('work-approvals-closed')).toHaveCount(0)
  await expect(table.locator(`tr[data-approval-id="${financeBatchId}"]`)).toBeVisible()

  await page.getByRole('button', { name: 'Daha fazla göster' }).click()
  await expect(page.getByTestId('work-approvals-closed')).toBeVisible()
  await page.getByRole('button', { name: 'Listeyi baştan yükle' }).click()
  await expect(table.locator(`tr[data-approval-id="${financeBatchId}"]`)).toBeVisible()
  const firstPageReadsAfterSecondRecovery = reads.filter((read) =>
    read.url.pathname === WORK_PATH && !read.url.searchParams.has('cursor') && !read.url.searchParams.has('kind')).length
  expect(firstPageReadsAfterSecondRecovery).toBe(firstPageReadsBeforeLateAnswer + 1)
  expect(reads.every((read) => read.method === 'GET')).toBe(true)
})
