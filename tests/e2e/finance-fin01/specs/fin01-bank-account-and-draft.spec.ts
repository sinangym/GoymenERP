/**
 * FIN-01 — the Finance user path against the REAL stack (`browser:fin01:ci`), card FIN-UI-1 §9.
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server, the real
 * Settings money profile (GDC/TRY) and currency candidates. The customer is the seed's, opened
 * through the real Cari API. Everything else happens on the screens:
 *
 *   FIN01-01 the proposer proposes a bank account (a currency picked from Settings' candidates,
 *            nothing preselected); it is pending and marked as the proposer's own, with no approval;
 *   FIN01-02 ANOTHER user approves it once (Ö4) — it becomes usable;
 *   FIN01-03 the proposer saves a draft against it, entered from the customer, and reopens the
 *            draft from the list — a draft, no money effect;
 *   FIN01-04 the approver (no draft grant) and the ungranted profile are closed out of the screens
 *            they may not use, and send no Finance request.
 *
 * NOTHING IS FULFILLED LOCALLY: the only interception is the Development identity rewrite, and
 * every answer a case relies on is asserted to belong to a rewritten request (the mfg pattern).
 * The cases run in order on one database: FIN01-02 approves what FIN01-01 proposed.
 */

import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const ACCOUNT_NAME = `SENTETIK FIN01 Ana TL ${Date.now()}`
const FINANCE_API = /\/api\/v1\/finance\//
const PROPOSE_RE = /\/api\/v1\/finance\/bank-accounts$/
const APPROVAL_RE = /\/api\/v1\/finance\/bank-accounts\/[^/]+\/approval$/
const DRAFT_RE = /\/api\/v1\/finance\/manual-receipts\/drafts$/

/** Binds `actor` to every API request of this context and records which requests it rewrote. */
async function asActor(context: BrowserContext, actor: string): Promise<Set<Request>> {
  const rewritten = new Set<Request>()
  await context.route('**/api/v1/**', async (route: Route) => {
    rewritten.add(route.request())
    await route.fallback({ headers: { ...route.request().headers(), 'x-development-user': actor } })
  })
  return rewritten
}

function watchAnswers(page: Page): Request[] {
  const answered: Request[] = []
  page.on('requestfinished', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/')) answered.push(request)
  })
  return answered
}

function expectEveryAnswerRewritten(answered: readonly Request[], rewritten: ReadonlySet<Request>) {
  const foreign = answered.filter((request) => !rewritten.has(request)).map((request) => request.url())
  expect(foreign, 'every API answer must belong to a request sent as the bound actor').toEqual([])
}

function accountRow(page: Page) {
  return page.getByRole('row', { name: new RegExp(ACCOUNT_NAME) })
}

test.describe.serial('FIN-01 bank account and draft (real API, real SQL)', () => {
  test('FIN01-01 the proposer proposes an account with a currency from Settings, pending and not self-approvable', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    await page.goto('/finans/banka-hesaplari/yeni')
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni banka hesabı öner', exact: true })).toBeVisible({ timeout: 30_000 })
    const currency = page.getByLabel('Para birimi', { exact: true })
    await expect(currency).toHaveValue('')
    await page.getByLabel('Hesap adı', { exact: true }).fill(ACCOUNT_NAME)
    await currency.selectOption({ label: /^TRY — / })

    const post = page.waitForRequest((request) => PROPOSE_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => PROPOSE_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Öner', exact: true }).click()
    const sent = await post
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f]{32}$/)
    expect(Object.keys(sent.postDataJSON()).sort()).toEqual(['currencyId', 'displayName'])
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('Hesap önerildi; onay bekliyor.', { exact: false })).toBeVisible()

    await page.goto('/finans/banka-hesaplari')
    await expect(accountRow(page)).toContainText('Onay bekliyor')
    await expect(accountRow(page)).toContainText('Sizin öneriniz')
    await expect(accountRow(page).getByRole('button', { name: /Onayla/ })).toHaveCount(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-02 another user approves it once and it becomes usable', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)

    await page.goto('/finans/banka-hesaplari')
    await expect(accountRow(page)).toContainText('Onay bekliyor', { timeout: 30_000 })
    await accountRow(page).getByRole('button', { name: new RegExp(`Onayla: ${ACCOUNT_NAME}`) }).click()
    const post = page.waitForRequest((request) => APPROVAL_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Evet, onayla', exact: true }).click()
    const sent = await post
    expect(sent.postDataJSON()).toEqual({ expectedVersion: '1' })
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('Hesap onaylandı.', { exact: false })).toBeVisible()
    await expect(accountRow(page)).toContainText('Onaylı', { timeout: 15_000 })
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-03 the proposer saves a draft against it from the customer and reopens it from the list', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.proposerActor)
    const answered = watchAnswers(page)

    const entry = new URLSearchParams({ partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId })
    await page.goto(`/finans/tahsilatlar/yeni?${entry.toString()}`)
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni tahsilat taslağı', exact: true })).toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Banka hesabı', { exact: true }).selectOption({ label: ACCOUNT_NAME })
    await page.getByLabel('Banka işlem tarihi', { exact: true }).fill('2026-09-18')
    const amount = page.getByLabel(/^Tutar/)
    await expect(amount).toBeEnabled({ timeout: 30_000 })
    await amount.fill('1250,50')

    const post = page.waitForRequest((request) => DRAFT_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => DRAFT_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click()
    const sent = await post
    expect(sent.postDataJSON()).toMatchObject({
      draftId: null,
      expectedVersion: null,
      preparation: { partyId: env.seed.partyId, companyAccountId: env.seed.companyAccountId, money: { amount: '1250.50' } },
    })
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('Taslak kaydedildi — para etkisi yok.')).toBeVisible()

    await page.goto('/finans/tahsilatlar')
    const rows = page.getByRole('row').filter({ hasText: 'Taslak' })
    await expect(rows).toHaveCount(1, { timeout: 30_000 })
    await rows.first().getByRole('button', { name: /^Aç/ }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Tahsilat taslağı', exact: true })).toBeVisible()
    await expect(page.getByText('Taslak — para etkisi yok.', { exact: false })).toBeVisible()
    // Record and Reverse have no route (T05, OPEN-009): not offered.
    await expect(page.getByRole('button', { name: 'Kesin kayda al' })).toHaveCount(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('FIN01-04 the approver has no draft screens and the ungranted profile no Finance screen', async ({ page, context }) => {
    await asActor(context, env.seed.approverActor)
    let financeRequests = 0
    page.on('request', (request) => {
      if (FINANCE_API.test(request.url()) && new URL(request.url()).pathname.includes('/manual-receipts')) financeRequests += 1
    })
    await page.goto('/finans/tahsilatlar')
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    expect(financeRequests).toBe(0)

    const ungranted = await context.browser()!.newContext({ baseURL: env.unauthorizedBaseUrl })
    try {
      const other = await ungranted.newPage()
      let anyFinance = 0
      other.on('request', (request) => {
        if (FINANCE_API.test(request.url())) anyFinance += 1
      })
      for (const route of ['/finans/banka-hesaplari', '/finans/tahsilatlar']) {
        await other.goto(new URL(route, env.unauthorizedBaseUrl).toString())
        await expect(other.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
      }
      expect(anyFinance).toBe(0)
    } finally {
      await ungranted.close()
    }
  })
})
