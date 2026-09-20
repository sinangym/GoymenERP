/**
 * ACC-01 — the Accounting user path against the REAL stack (`browser:acc01:ci`), ACC-V0b (M-07 / M-04 / M-01).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server. The Accounting screens are
 * STAGED on the normal host; this suite runs on the disposable staged Accounting profile (central), which opens
 * only their availability. Every grant is the suite override's synthetic one.
 *
 * FIXTURE SCOPE. The seed (Suites/ACC01) writes NOTHING: it reads the period list as each actor and fails on a
 * non-empty database. Every period below is created by the preparer — through the screen, or, where a case needs
 * a second period to act on, through the SAME real Accounting API the screen calls (./src/arrange.ts). No SQL, no
 * gated writer. Journals and posting queues cannot be filled over Accounting's HTTP surface, so M-04 and M-01 are
 * shown as their real, empty, read-only views.
 *
 *   ACC01-01 the preparer proposes a period on the screen; it is pending and marked as their own, with NO approve
 *            control although they hold the grant, and the server refuses their approval anyway (Ö4, 403);
 *   ACC01-02 ANOTHER user approves it once; after a reload the list is the server's read: open;
 *   ACC01-03 the approver closes it by one confirmed act (Ö5); after a reload it is closed;
 *   ACC01-04 a stale version in a second tab: tab A approves, tab B's approval is a version conflict and B re-reads;
 *   ACC01-05 an approval whose answer the browser never received is re-sent with the SAME key and answered as the
 *            server's first answer (replayed); the period moved once;
 *   ACC01-06 the preparer reads M-04 journals and M-01 queues: real, empty, read-only views; no command is sent;
 *   ACC01-07 closed routes: the approver (no journal/queue name) and the ungranted profile get the neutral
 *            not-found block and send no Accounting request;
 *   ACC01-08 company B: the preparer's GSP context lists no GDC period.
 *
 * NOTHING IS FULFILLED LOCALLY: the interceptions are the Development identity rewrite and, in ACC01-05,
 * withholding the browser's DELIVERY of a real answer the server really gave (the mfg / org03 pattern). Every
 * answer a case relies on is asserted to belong to a rewritten request. The cases run in order on one database.
 */

import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { apiAs, proposeThroughApi, readPeriod } from '../src/arrange.js'
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

  test('ACC01-06 the preparer reads M-04 journals and M-01 queues: real, empty and read-only', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.preparerActor)
    const answered = watchAnswers(page)
    const commands: Request[] = []
    page.on('request', (request) => {
      if (ACCOUNTING_API.test(request.url()) && request.method() !== 'GET') commands.push(request)
    })

    await page.goto('/muhasebe/fisler')
    await expect(page.getByRole('heading', { level: 1, name: 'Muhasebe Fişleri', exact: true })).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Fişler kaynak kayıtlardan türetilir ve burada yalnız okunur.', { exact: false })).toBeVisible()
    await expect(page.locator('[data-state="empty-no-data"]')).toBeVisible({ timeout: 30_000 })

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
})
