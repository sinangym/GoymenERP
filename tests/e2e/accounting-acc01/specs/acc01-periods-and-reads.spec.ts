/**
 * ACC-01 — the Accounting user path against the REAL stack (`browser:acc01:ci`), ACC-V0b (M-07 / M-04 / M-01).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server. The Accounting screens are
 * STAGED on the normal host; this suite runs on the disposable staged Accounting profile (central), which opens
 * only their availability. Every grant is the suite override's synthetic one.
 *
 * FIXTURE SCOPE. The seed (Suites/ACC01) writes ONE thing: Accounting's fixed SENTETIK posted journal, through the
 * owner's gated fixture port, in the fixed fin01 Open period (September 2026) it ensures first — a journal cannot be
 * posted over Accounting's HTTP surface, and ACC01-09 must open one. Every period the cases act on (November,
 * December, January) is created by the preparer — through the screen, or, where a case needs a second period to act
 * on, through the SAME real Accounting API the screen calls (./src/arrange.ts). No SQL. M-04 therefore lists exactly
 * that one journal and M-01 stays its real, empty, read-only view.
 *
 *   ACC01-01 the preparer proposes a period on the screen; it is pending and marked as their own, with NO approve
 *            control although they hold the grant, and the server refuses their approval anyway (Ö4, 403);
 *   ACC01-02 ANOTHER user approves it once; after a reload the list is the server's read: open;
 *   ACC01-03 the approver closes it by one confirmed act (Ö5); after a reload it is closed;
 *   ACC01-04 a stale version in a second tab: tab A approves, tab B's approval is a version conflict and B re-reads;
 *   ACC01-05 an approval whose answer the browser never received is re-sent with the SAME key and answered as the
 *            server's first answer (replayed); the period moved once;
 *   ACC01-06 the preparer reads M-04 journals (exactly the seed's one) and M-01 queues (empty), read-only; no command;
 *   ACC01-07 closed routes: the approver (no journal/queue name) and the ungranted profile get the neutral
 *            not-found block and send no Accounting request;
 *   ACC01-08 company B: the preparer's GSP context lists no GDC period;
 *   ACC01-09 (ACC-V1 reversal UI, phase 1) the preparer opens the posted journal from M-04 on the published M-04
 *            detail, closes and reopens it, and M-06 Mizan totals exactly its posted lines; the reversal propose
 *            control and approval screen stay staged; the approver (no journal name) gets neither screen.
 *   ACC01-10 selects an authorized recorded Finance source and approved accounts by visible labels, saves a normal
 *            draft, closes/reopens the same record, approves it and follows the posted journal into M-06.
 *
 * NOTHING IS FULFILLED LOCALLY: the interceptions are the Development identity rewrite and, in ACC01-05,
 * withholding the browser's DELIVERY of a real answer the server really gave (the mfg / org03 pattern). Every
 * answer a case relies on is asserted to belong to a rewritten request. The cases run in order on one database.
 */

import { randomUUID } from 'node:crypto'
import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import {
  apiAs,
  proposeThroughApi,
  readJournal,
  readJournals,
  readPeriod,
  readTrialBalance,
  type JournalDetailRead,
} from '../src/arrange.js'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const ACCOUNTING_API = /\/api\/v1\/accounting\//
const PROPOSE_RE = /\/api\/v1\/accounting\/periods$/
const APPROVAL_RE = /\/api\/v1\/accounting\/periods\/[^/]+\/approval$/
const CLOSE_RE = /\/api\/v1\/accounting\/periods\/[^/]+\/close$/

// Three periods of one company that never overlap; the database is fresh per run (the seed checks it).
const NOVEMBER = { startsOn: '2026-11-01', endsOn: '2026-11-30' }
const DECEMBER = { startsOn: '2026-12-01', endsOn: '2026-12-31' }
const JANUARY = { startsOn: '2027-01-01', endsOn: '2027-01-31' }

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

function periodRow(page: Page, period: { startsOn: string; endsOn: string }) {
  return page.getByRole('row').filter({ hasText: period.startsOn }).filter({ hasText: period.endsOn })
}

async function openPeriods(page: Page) {
  await page.goto('/muhasebe/donemler')
  await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe Dönemleri', exact: true })).toBeVisible({ timeout: 30_000 })
}

/** The server's decimal text as the screens show it (tr-TR grouping, the server's own digits; nothing rounded). */
function trAmount(wire: string): string {
  const match = /^([0-9]+)(?:\.([0-9]+))?$/.exec(wire)
  if (match === null) throw new Error(`not a server decimal text: ${wire}`)
  const grouped = match[1].replace(/\B(?=(?:[0-9]{3})+$)/g, '.')
  return match[2] === undefined ? grouped : `${grouped},${match[2]}`
}

const JOURNAL_KIND_TR: Readonly<Record<string, string>> = { Original: 'Asıl', Reversal: 'Ters kayıt', Correction: 'Düzeltme' }

function field(page: Page, term: string) {
  return page.locator('dt', { hasText: new RegExp(`^${term}$`) }).locator('xpath=following-sibling::dd[1]')
}

/** The M-04 detail shows exactly the server's read of the journal: its header facts, every line and its totals. */
async function expectJournalDetail(page: Page, detail: JournalDetailRead) {
  await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe fişi', exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Bu fiş bir kaynak kaydının türev muhasebe kaydıdır; ikinci bir para hareketi değildir.')).toBeVisible()
  await expect(field(page, 'Fiş türü')).toHaveText(JOURNAL_KIND_TR[detail.journal.kind])
  await expect(field(page, 'Muhasebe tarihi')).toHaveText(detail.journal.accountingDate)
  await expect(field(page, 'İşlem tarihi')).toHaveText(detail.journal.transactionDate)
  await expect(field(page, 'Valör')).toHaveText(detail.journal.valueDate ?? '—')
  const rows = page.locator('table tbody tr')
  await expect(rows).toHaveCount(detail.lines.length)
  for (const [index, line] of detail.lines.entries()) {
    const cells = rows.nth(index).getByRole('cell')
    await expect(cells.nth(0)).toHaveText(String(line.lineNumber))
    await expect(cells.nth(1).locator(`[title="${line.accountId}"]`)).toHaveCount(1)
    await expect(cells.nth(2)).toHaveText(trAmount(line.debit))
    await expect(cells.nth(3)).toHaveText(trAmount(line.credit))
  }
  const totals = page.locator('table tfoot tr[data-totals="balanced"]').getByRole('cell')
  await expect(totals.nth(0)).toHaveText(trAmount(detail.journal.totalDebit))
  await expect(totals.nth(1)).toHaveText(trAmount(detail.journal.totalCredit))
  // ACC-V1 phase 1: the reversal PROPOSE control of this detail is staged — no heading, no button.
  await expect(page.getByRole('heading', { name: 'Ters kayıt öner' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Ters kayıt öner' })).toHaveCount(0)
}

test.describe.serial('ACC-01 periods and reads (real API, real SQL)', () => {
  test('ACC01-01 the preparer proposes a period; it is their own, has no approve control, and the server refuses their approval', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.preparerActor)
    const answered = watchAnswers(page)

    await page.goto('/muhasebe/donemler/yeni')
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni dönem öner', exact: true })).toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Başlangıç', { exact: true }).fill(NOVEMBER.startsOn)
    await page.getByLabel('Bitiş', { exact: true }).fill(NOVEMBER.endsOn)

    const post = page.waitForRequest((request) => PROPOSE_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => PROPOSE_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Öner', exact: true }).click()
    const sent = await post
    expect(sent.headers()['idempotency-key']).toBeTruthy()
    expect(Object.keys(sent.postDataJSON()).sort()).toEqual(['endsOn', 'periodId', 'startsOn'])
    expect(sent.postDataJSON()).toMatchObject(NOVEMBER)
    expect((await answer).status()).toBe(201)
    await expect(page.getByText('Dönem önerildi; açılması için başka bir yetkili onaylamalı.')).toBeVisible()

    await openPeriods(page)
    const row = periodRow(page, NOVEMBER)
    await expect(row).toContainText('Önerildi — onay bekliyor')
    await expect(row).toContainText('Sizin öneriniz — başka bir yetkili onaylamalı')
    // Ö4: the preparer holds accounting.periods.approve, and still has no control for their own proposal.
    await expect(row.getByRole('button', { name: /^Onayla/ })).toHaveCount(0)

    // …and the server refuses it anyway: a plain 403, and the period stays proposed.
    const periodId = (sent.postDataJSON() as { periodId: string }).periodId
    const api = await apiAs(env, env.seed.preparerActor, env.seed.companyId)
    try {
      const own = await api.post(`/api/v1/accounting/periods/${periodId}/approval`, {
        data: { expectedVersion: 1 },
        headers: { 'Idempotency-Key': `acc01-own-${periodId}` },
      })
      expect(own.status()).toBe(403)
      expect(((await own.json()) as { code?: string }).code).toBe('forbidden')
    } finally {
      await api.dispose()
    }
    expect((await readPeriod(env, env.seed.preparerActor, periodId)).status).toBe('Proposed')
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('ACC01-02 another user approves it once; after a reload the list is the server read: open', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)

    await openPeriods(page)
    const row = periodRow(page, NOVEMBER)
    await expect(row).toContainText('Önerildi — onay bekliyor', { timeout: 30_000 })
    await expect(row).not.toContainText('Sizin öneriniz')
    await row.getByRole('button', { name: `Onayla: ${NOVEMBER.startsOn} – ${NOVEMBER.endsOn}`, exact: true }).click()
    const post = page.waitForRequest((request) => APPROVAL_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Evet, onayla', exact: true }).click()
    expect((await post).postDataJSON()).toEqual({ expectedVersion: 1 })
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('Dönem açıldı. Liste sunucudan yeniden okundu.')).toBeVisible()
    await expect(row).toContainText('Açık', { timeout: 15_000 })

    await page.reload()
    await expect(periodRow(page, NOVEMBER)).toContainText('Açık', { timeout: 30_000 })
    await expect(periodRow(page, NOVEMBER).getByRole('button', { name: /^Onayla/ })).toHaveCount(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('ACC01-03 the approver closes it by one confirmed act; after a reload it is closed', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)

    await openPeriods(page)
    const row = periodRow(page, NOVEMBER)
    await row.getByRole('button', { name: `Kapat: ${NOVEMBER.startsOn} – ${NOVEMBER.endsOn}`, exact: true }).click({ timeout: 30_000 })
    await expect(page.getByText('Kapanan döneme yeni kayıt girmez.', { exact: false })).toBeVisible()
    const post = page.waitForRequest((request) => CLOSE_RE.test(request.url()) && request.method() === 'POST')
    const answer = page.waitForResponse((response) => CLOSE_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Evet, kapat', exact: true }).click()
    expect((await post).postDataJSON()).toEqual({ expectedVersion: 2 })
    expect((await answer).status()).toBe(200)
    await expect(page.getByText('Dönem kapandı. Liste sunucudan yeniden okundu.')).toBeVisible()

    await page.reload()
    await expect(periodRow(page, NOVEMBER)).toContainText('Kapalı', { timeout: 30_000 })
    await expect(periodRow(page, NOVEMBER).getByRole('button')).toHaveCount(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('ACC01-04 a stale version in a second tab is a conflict, and that tab re-reads the server', async ({ page, context }) => {
    await proposeThroughApi(env, DECEMBER)
    const rewritten = await asActor(context, env.seed.approverActor)
    const tabA = page
    const tabB = await context.newPage()
    const answeredA = watchAnswers(tabA)
    const answeredB = watchAnswers(tabB)
    await openPeriods(tabA)
    await openPeriods(tabB)
    await expect(periodRow(tabB, DECEMBER)).toContainText('Önerildi — onay bekliyor', { timeout: 30_000 })

    // Tab A approves; tab B still shows version 1.
    await periodRow(tabA, DECEMBER).getByRole('button', { name: /^Onayla/ }).click({ timeout: 30_000 })
    const answerA = tabA.waitForResponse((response) => APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await tabA.getByRole('button', { name: 'Evet, onayla', exact: true }).click()
    expect((await answerA).status()).toBe(200)

    await periodRow(tabB, DECEMBER).getByRole('button', { name: /^Onayla/ }).click()
    const postB = tabB.waitForRequest((request) => APPROVAL_RE.test(request.url()) && request.method() === 'POST')
    const answerB = tabB.waitForResponse((response) => APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await tabB.getByRole('button', { name: 'Evet, onayla', exact: true }).click()
    expect((await postB).postDataJSON()).toEqual({ expectedVersion: 1 })
    expect((await answerB).status()).toBe(409)
    await expect(tabB.locator('[data-outcome="failed"][data-failure="version-conflict"]')).toBeVisible()
    await expect(tabB.getByText('Dönem siz okurken değişti.', { exact: false })).toBeVisible()
    await expect(periodRow(tabB, DECEMBER)).toContainText('Açık', { timeout: 15_000 })
    expectEveryAnswerRewritten([...answeredA, ...answeredB], rewritten)
  })

  test('ACC01-05 an approval whose answer never arrived is re-sent with the same key and answered as the first answer', async ({ page, context }) => {
    const periodId = await proposeThroughApi(env, JANUARY)
    const rewritten = await asActor(context, env.seed.approverActor)
    const answered = watchAnswers(page)
    // Withhold the DELIVERY of the first real answer: the server approves, the page learns nothing.
    let withheld: number | null = null
    await page.route(APPROVAL_RE, async (route) => {
      if (withheld !== null) return route.fallback()
      const response = await route.fetch({
        headers: { ...route.request().headers(), 'x-development-user': env.seed.approverActor },
      })
      withheld = response.status()
      await route.abort('failed')
    })
    const posts: Request[] = []
    page.on('request', (request) => {
      if (APPROVAL_RE.test(request.url()) && request.method() === 'POST') posts.push(request)
    })

    await openPeriods(page)
    await periodRow(page, JANUARY).getByRole('button', { name: /^Onayla/ }).click({ timeout: 30_000 })
    await page.getByRole('button', { name: 'Evet, onayla', exact: true }).click()
    await expect(page.getByText('Sonuç bilinmiyor', { exact: true })).toBeVisible()
    expect(withheld).toBe(200)

    const replay = page.waitForResponse((response) => APPROVAL_RE.test(response.url()) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Aynı işlemi yeniden gönder', exact: true }).click()
    const replayed = await replay
    expect(replayed.status()).toBe(200)
    expect(replayed.headers()['idempotency-replayed']).toBe('true')
    await expect(page.locator('[data-outcome="replayed"]')).toBeVisible()
    await expect(page.getByText('Bu işlem daha önce kaydedilmişti', { exact: false })).toBeVisible()
    expect(posts).toHaveLength(2)
    expect(posts[1].headers()['idempotency-key']).toBe(posts[0].headers()['idempotency-key'])
    expect(posts[1].postDataJSON()).toEqual(posts[0].postDataJSON())

    // Approved ONCE: version 2, not 3.
    const stored = await readPeriod(env, env.seed.approverActor, periodId)
    expect([stored.status, stored.version]).toEqual(['Open', 2])
    await expect(periodRow(page, JANUARY)).toContainText('Açık')
    // The withheld request went out through route.fetch with the actor header set explicitly; every answer the
    // PAGE received came through the identity rewrite.
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('ACC01-06 the preparer reads M-04 journals (the seed\'s one) and M-01 queues (empty), read-only', async ({ page, context }) => {
    // The server's own list: exactly the seed's synthetic posted journal (the seed already failed on anything else).
    const journals = await readJournals(env, env.seed.preparerActor)
    expect(journals).toHaveLength(1)
    const rewritten = await asActor(context, env.seed.preparerActor)
    const answered = watchAnswers(page)
    const commands: Request[] = []
    page.on('request', (request) => {
      if (ACCOUNTING_API.test(request.url()) && request.method() !== 'GET') commands.push(request)
    })

    await page.goto('/muhasebe/fisler')
    await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe Fişleri', exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Fişler kaynak kayıtlardan türetilir ve burada yalnız okunur.', { exact: false })).toBeVisible()
    await expect(page.locator('tr[data-journal]')).toHaveCount(1, { timeout: 30_000 })
    await expect(page.locator(`tr[data-journal="${journals[0].journalId}"]`)).toHaveCount(1)

    // From a non-proposed period, its journals (the filtered read, also empty).
    await openPeriods(page)
    await periodRow(page, NOVEMBER).getByRole('link', { name: 'Fişleri', exact: true }).click()
    await expect(page.getByText('Yalnız bir dönemin fişleri gösteriliyor.')).toBeVisible({ timeout: 30_000 })
    await expect(page.locator('[data-state="empty-filtered"]')).toBeVisible()

    await page.goto('/muhasebe/kontrol')
    await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe Kontrol', exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Eşleme bekleyen kayıt yok.')).toBeVisible()
    await expect(page.getByText('Tutar bekleyen kayıt yok.')).toBeVisible()
    expect(commands).toHaveLength(0)
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('ACC01-07 the approver has no journal or queue screen, and the ungranted profile no Accounting screen', async ({ page, context }) => {
    await asActor(context, env.seed.approverActor)
    let readsOutsideItsNames = 0
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname
      if (path.startsWith('/api/v1/accounting/journals') || path.startsWith('/api/v1/accounting/posting-queues')) readsOutsideItsNames += 1
    })
    for (const route of ['/muhasebe/fisler', '/muhasebe/kontrol']) {
      await page.goto(route)
      await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
    }
    expect(readsOutsideItsNames).toBe(0)

    const ungranted = await context.browser()!.newContext({ baseURL: env.unauthorizedBaseUrl })
    try {
      const other = await ungranted.newPage()
      let anyAccounting = 0
      other.on('request', (request) => {
        if (ACCOUNTING_API.test(request.url())) anyAccounting += 1
      })
      for (const route of ['/muhasebe/donemler', '/muhasebe/donemler/yeni', '/muhasebe/fisler', '/muhasebe/kontrol']) {
        await other.goto(new URL(route, env.unauthorizedBaseUrl).toString())
        await expect(other.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
      }
      expect(anyAccounting).toBe(0)
    } finally {
      await ungranted.close()
    }
  })

  test('ACC01-08 company B: the preparer reads periods in GSP and sees no GDC period', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.preparerActor)
    const answered = watchAnswers(page)
    const companiesAsked = new Set<string>()
    page.on('request', (request) => {
      if (/\/api\/v1\/accounting\/periods(\?|$)/.test(request.url())) companiesAsked.add(request.headers()['x-company-id'] ?? '')
    })

    await openPeriods(page)
    await expect(periodRow(page, NOVEMBER)).toHaveCount(1, { timeout: 30_000 })
    const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
    await picker.selectOption(env.seed.companyBId)
    const dialog = page.getByRole('dialog')
    if (await dialog.count()) await dialog.getByRole('button', { name: 'Değişiklikleri bırak' }).click()
    await expect(picker).toHaveValue(env.seed.companyBId, { timeout: 30_000 })

    await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible({ timeout: 30_000 })
    for (const period of [NOVEMBER, DECEMBER, JANUARY]) {
      await expect(periodRow(page, period)).toHaveCount(0)
    }
    // GSP holds only the period read: no proposal link there.
    await expect(page.getByRole('link', { name: 'Yeni dönem öner', exact: true })).toHaveCount(0)
    expect(companiesAsked).toEqual(new Set([env.seed.companyId, env.seed.companyBId]))
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('ACC01-09 the preparer opens the posted journal from M-04, closes and reopens it, and M-06 totals its lines', async ({ page, context, baseURL }) => {
    // The server truth the screens are compared with: the seed's one posted journal and its lines, and the
    // September trial balance, read from the real API as the same preparer.
    const journals = await readJournals(env, env.seed.preparerActor)
    expect(journals).toHaveLength(1)
    const journalId = journals[0].journalId
    const detail = await readJournal(env, env.seed.preparerActor, journalId)
    expect(detail.lines).toHaveLength(detail.journal.lineCount)
    const september = { fromAccountingDate: '2026-09-01', throughAccountingDate: '2026-09-30' }
    const report = await readTrialBalance(env, env.seed.preparerActor, september)
    // Server to server first: the only journal of the range, so each account's M-06 line IS its posted line.
    expect(report.lines).toHaveLength(detail.lines.length)
    for (const line of detail.lines) {
      const aggregated = report.lines.find((item) => item.accountId === line.accountId)
      expect([aggregated?.debit, aggregated?.credit]).toEqual([line.debit, line.credit])
    }
    expect(report.currencyTotals).toHaveLength(1)
    expect(report.currencyTotals[0]).toMatchObject({
      debit: detail.journal.totalDebit, credit: detail.journal.totalCredit, isBalanced: true,
    })

    const rewritten = await asActor(context, env.seed.preparerActor)
    const answered = watchAnswers(page)
    const commands: Request[] = []
    const reversalReads: Request[] = []
    page.on('request', (request) => {
      if (ACCOUNTING_API.test(request.url()) && request.method() !== 'GET') commands.push(request)
      if (/\/api\/v1\/accounting\/journal-reversal-proposals/.test(request.url())) reversalReads.push(request)
    })
    const detailRead = () => page.waitForResponse((response) =>
      new URL(response.url()).pathname === `/api/v1/accounting/journals/${journalId}` && response.request().method() === 'GET')

    // Open it from the list: the published M-04 detail link of its row.
    await page.goto('/muhasebe/fisler')
    await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe Fişleri', exact: true })).toBeVisible({ timeout: 30_000 })
    const row = page.locator(`tr[data-journal="${journalId}"]`)
    await expect(row).toContainText(trAmount(detail.journal.totalDebit), { timeout: 30_000 })
    let read = detailRead()
    await row.getByRole('link', { name: /^Aç: / }).click()
    expect((await read).status()).toBe(200)
    await expect(page).toHaveURL(new RegExp(`/muhasebe/fisler/${journalId}$`))
    await expectJournalDetail(page, detail)

    // Close it (back to the list) and reopen it from the list: the server is read again, the same journal answers.
    await page.getByRole('link', { name: 'Tüm fişler', exact: true }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe Fişleri', exact: true })).toBeVisible()
    read = detailRead()
    await page.locator(`tr[data-journal="${journalId}"]`).getByRole('link', { name: /^Aç: / }).click()
    expect((await read).status()).toBe(200)
    await expectJournalDetail(page, detail)

    // Reopen by its address in a fresh load of the application: the route itself is published.
    read = detailRead()
    await page.reload()
    expect((await read).status()).toBe(200)
    await expectJournalDetail(page, detail)

    // The reversal APPROVAL screen stays staged: the neutral not-found block, and no proposal is read.
    await page.goto(`/muhasebe/ters-kayit-onay/${randomUUID()}`)
    await expect(page.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })

    // M-06 Mizan over September: every posted line's account shows exactly that line, and the currency total is the
    // journal's totals, balanced. Nothing is summed on the screen: these are the server's aggregates.
    await page.goto('/muhasebe/mizan')
    await expect(page.getByRole('heading', { level: 1, name: 'Mizan', exact: true })).toBeVisible({ timeout: 30_000 })
    await page.getByLabel('Başlangıç muhasebe tarihi', { exact: true }).fill(september.fromAccountingDate)
    await page.getByLabel('Bitiş muhasebe tarihi', { exact: true }).fill(september.throughAccountingDate)
    const trialBalance = page.waitForResponse((response) =>
      new URL(response.url()).pathname === '/api/v1/accounting/trial-balance' && response.request().method() === 'GET')
    await page.getByRole('button', { name: 'Mizanı getir', exact: true }).click()
    const answer = await trialBalance
    expect(answer.status()).toBe(200)
    expect(Object.fromEntries(new URL(answer.url()).searchParams)).toEqual(september)
    const lines = page.locator('table tbody tr')
    await expect(lines).toHaveCount(detail.lines.length, { timeout: 30_000 })
    for (const line of detail.lines) {
      const cells = lines.filter({ has: page.locator(`[title="${line.accountId}"]`) }).getByRole('cell')
      await expect(cells.nth(2)).toHaveText(trAmount(line.debit))
      await expect(cells.nth(3)).toHaveText(trAmount(line.credit))
    }
    const total = page.locator('table tfoot tr')
    await expect(total).toHaveCount(1)
    await expect(total.getByRole('cell').nth(0)).toHaveText(trAmount(detail.journal.totalDebit))
    await expect(total.getByRole('cell').nth(1)).toHaveText(trAmount(detail.journal.totalCredit))
    await expect(total.getByRole('cell').nth(2)).toHaveText('Dengeli')

    expect(commands).toHaveLength(0)
    expect(reversalReads).toHaveLength(0)
    expectEveryAnswerRewritten(answered, rewritten)

    // The approver holds no journal name: neither the detail nor M-06 opens for it, and neither read is sent.
    const approverContext = await context.browser()!.newContext({ baseURL })
    try {
      await asActor(approverContext, env.seed.approverActor)
      const other = await approverContext.newPage()
      let journalReads = 0
      other.on('request', (request) => {
        const path = new URL(request.url()).pathname
        if (path.startsWith('/api/v1/accounting/journals') || path.startsWith('/api/v1/accounting/trial-balance')) journalReads += 1
      })
      for (const route of [`/muhasebe/fisler/${journalId}`, '/muhasebe/mizan']) {
        await other.goto(route)
        await expect(other.locator('[data-state="not-found"]')).toBeVisible({ timeout: 30_000 })
      }
      expect(journalReads).toBe(0)
    } finally {
      await approverContext.close()
    }
  })

  test('ACC01-10 an authorized Finance source becomes one normal draft, posted journal and trial-balance effect', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.preparerActor)
    const answered = watchAnswers(page)
    const sourceRead = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/api/v1/finance/manual-receipts'
        && response.request().method() === 'GET')
    await page.goto('/muhasebe/normal-fisler/yeni')
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni normal fiş taslağı', exact: true })).toBeVisible({ timeout: 30_000 })

    const sourceResponse = await sourceRead
    expect(sourceResponse.status()).toBe(200)
    const sourcePage = await sourceResponse.json() as { items: Array<{ id: string; stage: string }> }
    const recordedSources = sourcePage.items.filter(source => source.stage === 'Recorded')
    expect(recordedSources).toHaveLength(1)
    const sourcePicker = page.getByLabel('Onaylı finans kaynağı', { exact: true })
    const sourceOption = sourcePicker.locator(`option[value="${recordedSources[0].id}"]`)
    await expect(sourceOption).toHaveText(env.seed.normalSourceLabel)
    await expect(sourcePicker).toBeEnabled()
    await sourcePicker.selectOption(recordedSources[0].id)
    await expect(page.getByText(/Onaylı ayna .*; kural .*/)).toBeVisible({ timeout: 30_000 })
    const accounts = page.getByLabel('Onaylı hesap', { exact: true })
    await accounts.nth(0).selectOption({ label: env.seed.normalDebitLabel })
    await accounts.nth(1).selectOption({ label: env.seed.normalCreditLabel })
    await page.getByLabel('İşlem tarihi', { exact: true }).fill(env.seed.normalOccurredOn)
    await page.getByLabel('Muhasebe tarihi', { exact: true }).fill(env.seed.normalOccurredOn)
    const amountInput = env.seed.normalAmount.replace('.', ',')
    await page.getByLabel('Tutar', { exact: true }).nth(0).fill(amountInput)
    await page.getByLabel('Tutar', { exact: true }).nth(1).fill(amountInput)

    const createAnswer = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/api/v1/accounting/normal-journal-drafts'
        && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Taslağı kaydet', exact: true }).click()
    const createdResponse = await createAnswer
    expect(createdResponse.status()).toBe(201)
    const created = await createdResponse.json() as { draftId: string; sourceId: string; status: string }
    expect(created.status).toBe('Draft')
    await expect(page.getByRole('heading', { level: 1, name: 'Normal fiş taslağı kaydedildi', exact: true })).toBeVisible()

    const reopenRead = page.waitForResponse(response =>
      new URL(response.url()).pathname === `/api/v1/accounting/normal-journal-drafts/${created.draftId}`
        && response.request().method() === 'GET')
    await page.getByRole('link', { name: 'Kaydı kapat ve yeniden aç', exact: true }).click()
    const reopenedResponse = await reopenRead
    expect(reopenedResponse.status()).toBe(200)
    const reopened = await reopenedResponse.json() as { draftId: string; sourceId: string; status: string }
    expect(reopened).toMatchObject({ draftId: created.draftId, sourceId: created.sourceId, status: 'Draft' })
    await expect(page).toHaveURL(new RegExp(`/muhasebe/normal-fisler/${created.draftId}$`))
    await expect(page.getByText('Draft', { exact: true })).toBeVisible()
    await expect(page.locator('body')).not.toContainText(created.sourceId)

    await page.getByRole('link', { name: 'Onay ekranını aç', exact: true }).click()
    await page.getByLabel('Açık dönem kimliği', { exact: true }).fill(env.seed.openPeriodId)
    const approvalAnswer = page.waitForResponse(response =>
      new URL(response.url()).pathname === `/api/v1/accounting/normal-journal-drafts/${created.draftId}/approval`
        && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Normal taslağı onayla', exact: true }).click()
    const approvedResponse = await approvalAnswer
    expect(approvedResponse.status()).toBe(200)
    const approved = await approvedResponse.json() as { draftId: string; sourceId: string; status: string; postedJournalId: string }
    expect(approved).toMatchObject({ draftId: created.draftId, sourceId: created.sourceId, status: 'Approved' })

    await page.getByRole('link', { name: 'Oluşturulan fişi aç', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/muhasebe/fisler/${approved.postedJournalId}$`))
    await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe fişi', exact: true })).toBeVisible({ timeout: 30_000 })

    await page.goto('/muhasebe/mizan')
    await page.getByLabel('Başlangıç muhasebe tarihi', { exact: true }).fill(env.seed.normalOccurredOn)
    await page.getByLabel('Bitiş muhasebe tarihi', { exact: true }).fill(env.seed.normalOccurredOn)
    const reportAnswer = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/api/v1/accounting/trial-balance' && response.request().method() === 'GET')
    await page.getByRole('button', { name: 'Mizanı getir', exact: true }).click()
    expect((await reportAnswer).status()).toBe(200)
    const totals = page.locator('table tfoot tr').getByRole('cell')
    await expect(totals.nth(0)).toHaveText(trAmount(env.seed.normalAmount))
    await expect(totals.nth(1)).toHaveText(trAmount(env.seed.normalAmount))
    await expect(totals.nth(2)).toHaveText('Dengeli')
    expectEveryAnswerRewritten(answered, rewritten)
  })
})
