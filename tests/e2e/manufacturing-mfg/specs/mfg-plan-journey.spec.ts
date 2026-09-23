/**
 * MFG plan create/reopen — real Chromium, API and SQL-backed owner stores.
 *
 * The fixture records one real Inventory label through the public opening-count command. The page
 * discovers that label and its current version through Inventory's candidate endpoint, saves through
 * Manufacturing, closes the form, and reopens the stored plan through the plan read endpoint.
 */

import { randomUUID } from 'node:crypto'
import { expect, request, test, type APIRequestContext, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { apiAs, PARENT_ROLL_CODE, readParentRollCandidate } from '../src/arrange.js'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const OTHER_COMPANY = '10000000-0000-0000-0000-000000000002'
const PLAN_POST_RE = /\/api\/v1\/manufacturing\/plans$/
const PLAN_GET_RE = /\/api\/v1\/manufacturing\/plans\/[^/]+$/
const CANDIDATES_RE = /\/api\/v1\/inventory\/references\/parent-rolls(?:\?|$)/

interface CandidateAnswer {
  readonly labelId: string
  readonly labelVersion: string
  readonly draftSelectable: boolean
}

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
  expect(answered.filter((request) => !rewritten.has(request)).map((request) => request.url())).toEqual([])
}

async function openPlanForm(page: Page): Promise<CandidateAnswer> {
  await page.goto('/uretim/emirler')
  await expect(page.getByRole('heading', { name: 'Üretim emirleri' })).toBeVisible()
  const createActions = page.getByRole('link', { name: 'Yeni üretim planı' })
  await expect(createActions).toHaveCount(1)
  await createActions.click()
  await expect(page.getByRole('heading', { name: 'Yeni taslak plan' })).toBeVisible()

  await page.getByRole('combobox', { name: 'İş merkezi' }).selectOption(env.seed.workCenterId)
  await page.getByLabel('Etiket, ürün veya lot ara').fill(PARENT_ROLL_CODE)
  const candidatesAnswer = page.waitForResponse(
    (response) => CANDIDATES_RE.test(response.url()) && response.request().method() === 'GET',
  )
  await page.getByRole('button', { name: 'Ara', exact: true }).click()
  const response = await candidatesAnswer
  expect(response.status()).toBe(200)
  const candidates = (await response.json()) as { items: CandidateAnswer[] }
  const candidate = candidates.items.find((item) => item.labelId === env.seed.rollId)
  expect(candidate).toMatchObject({ labelId: env.seed.rollId, draftSelectable: true })
  expect(candidate?.labelVersion).toMatch(/^[1-9][0-9]*$/)

  const row = page.getByRole('row', { name: new RegExp(PARENT_ROLL_CODE) })
  await expect(row).toContainText('1000 kg')
  await row.getByRole('button', { name: 'Ekle', exact: true }).click()
  await page.getByRole('textbox', { name: `${PARENT_ROLL_CODE} planlanan kg` }).fill('1000')
  return candidate as CandidateAnswer
}

async function readPlan(api: APIRequestContext, planId: string) {
  const response = await api.get(`/api/v1/manufacturing/plans/${planId}`)
  return { response, body: response.status() === 200 ? await response.json() as Record<string, unknown> : null }
}

test.describe('MFG plan create and reopen (real Inventory parent roll)', () => {
  test('the in-list action saves, closes the form and reopens the same stored plan', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const candidate = await openPlanForm(page)

    const sentPlan = page.waitForRequest((request) => PLAN_POST_RE.test(request.url()) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Taslak planı kaydet', exact: true }).click()
    const sent = await sentPlan
    const body = sent.postDataJSON() as {
      planId: string
      workCenterId: string
      inputs: Array<{ parentRollId: string; plannedKg: string; expectedLabelVersion: string }>
    }
    expect(sent.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(body).toMatchObject({
      workCenterId: env.seed.workCenterId,
      inputs: [{
        parentRollId: env.seed.rollId,
        plannedKg: '1000',
        expectedLabelVersion: candidate.labelVersion,
      }],
    })
    await expect(page.getByRole('heading', { name: 'Taslak plan kaydedildi' })).toBeVisible()
    await expect(page.getByText(body.planId, { exact: false })).toBeVisible()

    const freshRead = page.waitForResponse(
      (response) => PLAN_GET_RE.test(response.url()) && response.url().endsWith(body.planId)
        && response.request().method() === 'GET',
    )
    await page.getByRole('link', { name: 'Formu kapat ve planı yeniden aç' }).click()
    expect((await freshRead).status()).toBe(200)
    expect(new URL(page.url()).pathname).toBe(`/uretim/plan/${body.planId}`)
    await expect(page.getByRole('heading', { name: 'Plan yeniden açıldı' })).toBeVisible()
    await expect(page.getByText(body.planId, { exact: true })).toBeVisible()
    await expect(page.getByRole('row', { name: new RegExp(`${env.seed.rollId}.*1000`) })).toBeVisible()

    const api = await apiAs(env, env.seed.recorderActor)
    try {
      const stored = await readPlan(api, body.planId)
      expect(stored.response.status()).toBe(200)
      expect(stored.body).toMatchObject({
        planId: body.planId,
        companyId: env.seed.companyId,
        status: 'Draft',
        workCenterId: env.seed.workCenterId,
        inputs: [{ parentRollId: env.seed.rollId, plannedKg: '1000' }],
      })
    } finally {
      await api.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('an undelivered save is replayed with the same key and creates one plan', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    await openPlanForm(page)

    let firstRequest: Request | null = null
    let firstStatus: number | null = null
    await page.route(PLAN_POST_RE, async (route) => {
      if (firstRequest !== null) return route.fallback()
      firstRequest = route.request()
      const response = await route.fetch({
        headers: { ...route.request().headers(), 'x-development-user': env.seed.recorderActor },
      })
      firstStatus = response.status()
      await route.abort('failed')
    })

    await page.getByRole('button', { name: 'Taslak planı kaydet', exact: true }).click()
    await expect(page.getByText('Sonuç bilinmiyor.', { exact: false })).toBeVisible()
    expect(firstStatus).toBe(201)
    const first = firstRequest as Request | null
    expect(first).not.toBeNull()
    const firstBody = first!.postDataJSON() as { planId: string }
    const firstKey = first!.headers()['idempotency-key']

    const replayed = page.waitForResponse(
      (response) => PLAN_POST_RE.test(response.url()) && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Aynı anahtarla yeniden gönder', exact: true }).click()
    const replay = await replayed
    expect(replay.status()).toBe(201)
    expect(replay.headers()['idempotency-replayed']).toBe('true')
    expect(replay.request().headers()['idempotency-key']).toBe(firstKey)
    expect(replay.request().postDataJSON()).toEqual(first!.postDataJSON())
    await expect(page.getByText('Önceki kayıtlı sonuç yeniden gösterildi.', { exact: true })).toBeVisible()

    const api = await apiAs(env, env.seed.recorderActor)
    try {
      const stored = await readPlan(api, firstBody.planId)
      expect(stored.response.status()).toBe(200)
      expect(stored.body).toMatchObject({ planId: firstBody.planId, status: 'Draft', version: 1 })
    } finally {
      await api.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('permission and stale-label refusals write no plan, and a refusal replay is stable', async () => {
    const candidate = await readParentRollCandidate(env)
    const refusedPlanId = randomUUID()
    const body = {
      planId: refusedPlanId,
      workCenterId: env.seed.workCenterId,
      inputs: [{
        parentRollId: candidate.labelId,
        plannedKg: '1000',
        expectedLabelVersion: candidate.labelVersion,
      }],
      target: null,
    }

    const reader = await apiAs(env, env.seed.readerActor)
    try {
      const denied = await reader.post('/api/v1/manufacturing/plans', {
        data: body,
        headers: { 'Idempotency-Key': randomUUID() },
      })
      expect(denied.status()).toBe(403)
    } finally {
      await reader.dispose()
    }

    const recorder = await apiAs(env, env.seed.recorderActor)
    try {
      const stale = {
        ...body,
        inputs: [{ ...body.inputs[0], expectedLabelVersion: String(Number(candidate.labelVersion) + 1) }],
      }
      const key = randomUUID()
      const first = await recorder.post('/api/v1/manufacturing/plans', {
        data: stale,
        headers: { 'Idempotency-Key': key },
      })
      expect(first.status()).toBe(409)
      expect((await first.json()) as Record<string, unknown>).toMatchObject({ code: 'inventory_label_version_conflict' })
      const replay = await recorder.post('/api/v1/manufacturing/plans', {
        data: stale,
        headers: { 'Idempotency-Key': key },
      })
      expect(replay.status()).toBe(409)
      expect(replay.headers()['idempotency-replayed']).toBe('true')
      expect((await replay.json()) as Record<string, unknown>).toMatchObject({ code: 'inventory_label_version_conflict' })
      expect((await readPlan(recorder, refusedPlanId)).response.status()).toBe(404)
    } finally {
      await recorder.dispose()
    }

    const otherCompany = await request.newContext({
      baseURL: env.apiBaseUrl,
      extraHTTPHeaders: {
        'X-Development-User': env.seed.recorderActor,
        'X-Company-Id': OTHER_COMPANY,
        'X-Correlation-Id': `mfg-ci-${randomUUID()}`,
      },
    })
    try {
      const hidden = await otherCompany.get(`/api/v1/manufacturing/plans/${refusedPlanId}`)
      expect(hidden.status()).toBe(403)
    } finally {
      await otherCompany.dispose()
    }
  })
})
