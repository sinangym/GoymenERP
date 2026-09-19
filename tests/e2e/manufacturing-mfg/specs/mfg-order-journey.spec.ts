/**
 * MFG — the production order screen against the REAL stack (`browser:mfg:ci`).
 *
 * Real Chromium, the real web build of the exact head, the real API and SQL Server with the
 * approved kilogram profile the real Settings provider answers. The order is arranged through the
 * real Manufacturing API as the seeded recorder (./src/arrange.ts); every fact on the screen goes
 * through the same routes. NOTHING IS FULFILLED LOCALLY: the only interceptions are the
 * Development identity rewrite and, in the unknown-outcome case, withholding the browser's
 * DELIVERY of a real answer the server really gave (the org03 pattern).
 *
 * IDENTITY. The web image bakes its own Development user at build time, so every case binds its
 * actor by rewriting the identity header on each API request and asserts every answer it relied on
 * belongs to a rewritten request.
 */

import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { arrangeOpenOrder, issueThroughApi, readOrder } from '../src/arrange.js'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const FACT_RE = /\/api\/v1\/manufacturing\/orders\/[^/]+\/(input-issues|input-returns|wip-measurements|outputs)$/
const ORDER_RE = /\/api\/v1\/manufacturing\/orders\/[^/]+$/

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

async function openOrder(page: Page, orderId: string) {
  await page.goto(`/uretim/emir/${orderId}`)
  await expect(page.getByRole('heading', { name: 'Üretim emri' })).toBeVisible()
}

function rollRow(page: Page, rollId: string) {
  return page.getByRole('row', { name: new RegExp(rollId) })
}

/** The roll's "Çıkan" cell: data cells are Planlanan, Çıkan, İade, … after the row header. */
function issuedCell(page: Page, rollId: string) {
  return rollRow(page, rollId).getByRole('cell').nth(1)
}

function kgField(page: Page) {
  return page.getByRole('textbox', { name: 'Kütle (kg)', exact: true })
}

async function fillIssue(page: Page, rollId: string, kg: string) {
  // Exact names: a label query is a substring match by default, and "Ana rulo" also names the
  // balances section ("Ana rulo dengesi (kg)").
  await page.getByRole('radio', { name: 'Girdi çıkışı', exact: true }).check()
  await page.getByRole('combobox', { name: 'Ana rulo', exact: true }).selectOption(rollId)
  await kgField(page).fill(kg)
  await page.getByLabel('Zaman', { exact: true }).fill('2026-09-18T09:30')
}

test.describe('MFG order screen (real API, real SQL)', () => {
  test('a recorder records an input issue and the table is the server’s re-read', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env)
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)

    await openOrder(page, arranged.orderId)
    await expect(rollRow(page, arranged.rollId)).toContainText('1000')
    await fillIssue(page, arranged.rollId, '250.5')
    const post = page.waitForRequest((request) => FACT_RE.test(request.url()) && request.method() === 'POST')
    const reread = page.waitForResponse((response) => ORDER_RE.test(response.url()) && response.request().method() === 'GET')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()

    const sent = await post
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(sent.postDataJSON()).toMatchObject({ parentRollId: arranged.rollId, kg: '250.5', expectedVersion: arranged.version })
    expect((await reread).status()).toBe(200)
    await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()
    await expect(issuedCell(page, arranged.rollId)).toHaveText('250.5')

    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<{ issuedKg: string }>)[0].issuedKg).toBe('250.5')
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('an answer the browser never received is re-sent with the same key and recorded once', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env)
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    // Withhold the DELIVERY of the first real answer: the server records the fact, the page learns nothing.
    let withheld: number | null = null
    await page.route(FACT_RE, async (route) => {
      if (withheld !== null) return route.fallback()
      const response = await route.fetch({
        headers: { ...route.request().headers(), 'x-development-user': env.seed.recorderActor },
      })
      withheld = response.status()
      await route.abort('failed')
    })

    await openOrder(page, arranged.orderId)
    await fillIssue(page, arranged.rollId, '100')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    await expect(page.getByText('Sonuç bilinmiyor')).toBeVisible()
    expect(withheld).toBe(200)
    await expect(kgField(page)).toBeDisabled()

    const resend = page.waitForRequest((request) => FACT_RE.test(request.url()) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Aynı olguyu yeniden gönder', exact: true }).click()
    await resend
    await expect(page.getByText('Daha önce kaydedilmiş')).toBeVisible()

    // Recorded ONCE: 100 kg issued, not 200.
    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<{ issuedKg: string }>)[0].issuedKg).toBe('100')
    await expect(issuedCell(page, arranged.rollId)).toHaveText('100')
    // The withheld request went out through route.fetch with the actor header set explicitly; every
    // answer the PAGE received came through the identity rewrite.
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('a write that another writer overtook is a conflict, re-read, and a new command at the new version', async ({ page, context }) => {
    const arranged = await arrangeOpenOrder(env)
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)

    await openOrder(page, arranged.orderId)
    await fillIssue(page, arranged.rollId, '50')
    // Another writer records first through the real API; the page still holds the old version.
    const moved = await issueThroughApi(env, arranged, arranged.version, '10')
    const posts: Request[] = []
    page.on('request', (request) => {
      if (FACT_RE.test(request.url()) && request.method() === 'POST') posts.push(request)
    })
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    await expect(page.getByText(/Emir siz formu doldururken değişti/)).toBeVisible()
    await expect(issuedCell(page, arranged.rollId)).toHaveText('10')

    await page.getByRole('button', { name: 'Kaydet', exact: true }).click()
    await expect(page.getByText('Kaydedildi', { exact: true })).toBeVisible()
    await expect(issuedCell(page, arranged.rollId)).toHaveText('60')
    expect(posts).toHaveLength(2)
    expect(posts[1].postDataJSON()).toMatchObject({ expectedVersion: moved.version })
    expect(posts[1].headers()['idempotency-key']).not.toBe(posts[0].headers()['idempotency-key'])

    const stored = await readOrder(env, arranged.orderId)
    expect((stored.rolls as Array<{ issuedKg: string }>)[0].issuedKg).toBe('60')
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('a reader sees the balances and no form, and sends no command', async ({ page, context }) => {
    // The order the suite fixture seeded through the real API (Suites/MFG): the seed path, end to end.
    const arranged = { orderId: env.seed.orderId, rollId: env.seed.rollId }
    const rewritten = await asActor(context, env.seed.readerActor)
    const answered = watchAnswers(page)
    const posts: Request[] = []
    page.on('request', (request) => {
      if (request.method() !== 'GET' && new URL(request.url()).pathname.startsWith('/api/v1/manufacturing')) posts.push(request)
    })

    await openOrder(page, arranged.orderId)
    await expect(rollRow(page, arranged.rollId)).toContainText('1000')
    await expect(page.getByRole('heading', { name: 'Gerçekleşme kaydı' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Kaydet', exact: true })).toHaveCount(0)
    expect(posts).toEqual([])
    expectEveryAnswerRewritten(answered, rewritten)
  })
})
