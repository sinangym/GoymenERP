/**
 * MFG plan create/reopen — real Chromium, API and SQL-backed owner stores.
 *
 * The fixture records one real Inventory label through the public opening-count command. The page
 * discovers that label and its current version through Inventory's candidate endpoint, saves through
 * Manufacturing, closes the form, and reopens the stored plan through the plan read endpoint.
 */

import { randomUUID } from 'node:crypto'
import { expect, request, test, type APIRequestContext, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { apiAs, readOrder, readParentRollCandidate } from '../src/arrange.js'
import { readEnvironment, type MfgRollSeed } from '../src/env.js'

const env = readEnvironment()
const OTHER_COMPANY = '10000000-0000-0000-0000-000000000002'
const PLAN_POST_RE = /\/api\/v1\/manufacturing\/plans$/
const PLAN_GET_RE = /\/api\/v1\/manufacturing\/plans\/[^/]+$/
const PLAN_APPROVAL_RE = /\/api\/v1\/manufacturing\/plans\/[^/]+\/approval$/
const ORDER_POST_RE = /\/api\/v1\/manufacturing\/orders$/
const ORDER_GET_RE = /\/api\/v1\/manufacturing\/orders\/[^/]+$/
const CANDIDATES_RE = /\/api\/v1\/inventory\/references\/parent-rolls(?:\?|$)/

interface CandidateAnswer {
  readonly labelId: string
  readonly labelVersion: string
  readonly draftSelectable: boolean
}

const CANONICAL_PROCESSES = ['Slitting', 'ColdRolling', 'TubeProfile', 'Paint', 'CutToLength'] as const
type CanonicalProcess = (typeof CANONICAL_PROCESSES)[number]

interface WorkCenterAnswer {
  readonly id: string
  readonly kind: CanonicalProcess
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

async function openPlanForm(
  page: Page,
  roll: MfgRollSeed,
  workCenterId = env.seed.workCenterId,
): Promise<CandidateAnswer> {
  await page.goto('/uretim/emirler')
  await expect(page.getByRole('heading', { name: 'Üretim emirleri' })).toBeVisible()
  const createActions = page.getByRole('link', { name: 'Yeni üretim planı' })
  await expect(createActions).toHaveCount(1)
  await createActions.click()
  await expect(page.getByRole('heading', { name: 'Yeni taslak plan' })).toBeVisible()

  await page.getByRole('combobox', { name: 'İş merkezi' }).selectOption(workCenterId)
  await page.getByLabel('Etiket, ürün veya lot ara').fill(roll.labelCode)
  const candidatesAnswer = page.waitForResponse(
    (response) => CANDIDATES_RE.test(response.url()) && response.request().method() === 'GET',
  )
  await page.getByRole('button', { name: 'Ara', exact: true }).click()
  const response = await candidatesAnswer
  expect(response.status()).toBe(200)
  const candidates = (await response.json()) as { items: CandidateAnswer[] }
  const candidate = candidates.items.find((item) => item.labelId === roll.labelId)
  expect(candidate).toMatchObject({ labelId: roll.labelId, draftSelectable: true })
  expect(candidate?.labelVersion).toMatch(/^[1-9][0-9]*$/)

  const row = page.getByRole('row', { name: new RegExp(roll.labelCode) })
  await expect(row).toContainText('1000 kg')
  await row.getByRole('button', { name: 'Ekle', exact: true }).click()
  await page.getByRole('textbox', { name: `${roll.labelCode} planlanan kg` }).fill('1000')
  return candidate as CandidateAnswer
}

async function readPlan(api: APIRequestContext, planId: string) {
  const response = await api.get(`/api/v1/manufacturing/plans/${planId}`)
  return { response, body: response.status() === 200 ? await response.json() as Record<string, unknown> : null }
}

async function readCanonicalWorkCenters(api: APIRequestContext): Promise<ReadonlyMap<CanonicalProcess, WorkCenterAnswer>> {
  const response = await api.get('/api/v1/organization/work-centers')
  expect(response.status()).toBe(200)
  const body = await response.json() as unknown
  expect(Array.isArray(body)).toBe(true)

  const result = new Map<CanonicalProcess, WorkCenterAnswer>()
  for (const process of CANONICAL_PROCESSES) {
    const row = (body as unknown[]).find((candidate) =>
      typeof candidate === 'object' && candidate !== null
        && (candidate as Record<string, unknown>).kind === process)
    if (typeof row !== 'object' || row === null) {
      throw new Error(`fixture must publish one ${process} work center`)
    }
    const id = (row as Record<string, unknown>).id
    if (typeof id !== 'string' || !/^[0-9a-f-]{36}$/.test(id)) {
      throw new Error(`fixture ${process} work center must publish a canonical id`)
    }
    result.set(process, { id, kind: process })
  }
  return result
}

test.describe('MFG plan create and reopen (real Inventory parent roll)', () => {
  test('each canonical process follows the same authorized save and fresh-reopen path', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const api = await apiAs(env, env.seed.recorderActor)

    try {
      const workCenters = await readCanonicalWorkCenters(api)
      for (const process of CANONICAL_PROCESSES) {
        const workCenter = workCenters.get(process)!
        const candidate = await openPlanForm(page, env.seed.rolls.planCreate, workCenter.id)
        const sentPlan = page.waitForRequest((request) => PLAN_POST_RE.test(request.url()) && request.method() === 'POST')
        await page.getByRole('button', { name: 'Taslak planı kaydet', exact: true }).click()
        const sent = await sentPlan
        const body = sent.postDataJSON() as {
          planId: string
          workCenterId: string
          inputs: Array<{ parentRollId: string; expectedLabelVersion: string }>
        }
        expect(body).toMatchObject({
          workCenterId: workCenter.id,
          inputs: [{ parentRollId: candidate.labelId, expectedLabelVersion: candidate.labelVersion }],
        })
        await expect(page.getByRole('heading', { name: 'Taslak plan kaydedildi' })).toBeVisible()

        const freshRead = page.waitForResponse(
          (response) => PLAN_GET_RE.test(response.url()) && response.url().endsWith(body.planId)
            && response.request().method() === 'GET',
        )
        await page.getByRole('link', { name: 'Formu kapat ve planı yeniden aç' }).click()
        expect((await freshRead).status()).toBe(200)
        await expect(page.getByRole('heading', { name: 'Plan yeniden açıldı' })).toBeVisible()
        await expect(page.getByText(process, { exact: true })).toBeVisible()

        const stored = await readPlan(api, body.planId)
        expect(stored.response.status()).toBe(200)
        expect(stored.body).toMatchObject({
          planId: body.planId,
          companyId: env.seed.companyId,
          workCenterId: workCenter.id,
          process,
          status: 'Draft',
          version: 1,
        })
      }
    } finally {
      await api.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('the in-list action saves, reopens and approves the plan, then creates and opens its order', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const candidate = await openPlanForm(page, env.seed.rolls.planCreate)

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
        parentRollId: candidate.labelId,
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
    await expect(page.getByRole('row', { name: new RegExp(`${candidate.labelId}.*1000`) })).toBeVisible()

    const api = await apiAs(env, env.seed.recorderActor)
    try {
      const stored = await readPlan(api, body.planId)
      expect(stored.response.status()).toBe(200)
      expect(stored.body).toMatchObject({
        planId: body.planId,
        companyId: env.seed.companyId,
        status: 'Draft',
        workCenterId: env.seed.workCenterId,
        inputs: [{ parentRollId: candidate.labelId, plannedKg: '1000' }],
      })
    } finally {
      await api.dispose()
    }

    const approvalResponse = page.waitForResponse(
      (response) => PLAN_APPROVAL_RE.test(response.url()) && response.request().method() === 'POST',
    )
    const approvedFreshRead = page.waitForResponse(
      (response) => PLAN_GET_RE.test(response.url()) && response.url().endsWith(body.planId)
        && response.request().method() === 'GET',
    )
    await page.getByRole('button', { name: 'Planı onayla', exact: true }).click()
    const approved = await approvalResponse
    expect(approved.status()).toBe(200)
    expect(approved.request().headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(approved.request().postDataJSON()).toEqual({ expectedVersion: 1 })
    expect((await approvedFreshRead).status()).toBe(200)
    await expect(page.getByText('Plan onaylandı ve güncel kayıt yeniden okundu.', { exact: true })).toBeVisible()
    await expect(page.getByText('Onaylı', { exact: true })).toBeVisible()

    const orderResponse = page.waitForResponse(
      (response) => ORDER_POST_RE.test(response.url()) && response.request().method() === 'POST',
    )
    const orderFreshRead = page.waitForResponse(
      (response) => ORDER_GET_RE.test(response.url()) && response.request().method() === 'GET',
    )
    await page.getByRole('button', { name: 'Üretim emri oluştur', exact: true }).click()
    const created = await orderResponse
    expect(created.status()).toBe(201)
    expect(created.request().headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/)
    const orderBody = created.request().postDataJSON() as { orderId: string; planId: string }
    expect(orderBody.planId).toBe(body.planId)
    expect(orderBody.orderId).toMatch(/^[0-9a-f-]{36}$/)
    expect((await orderFreshRead).status()).toBe(200)
    expect(new URL(page.url()).pathname).toBe(`/uretim/emir/${orderBody.orderId}`)
    await expect(page.getByRole('heading', { name: 'Üretim emri' })).toBeVisible()
    await expect(page.getByText(orderBody.orderId, { exact: true })).toBeVisible()

    const verify = await apiAs(env, env.seed.recorderActor)
    try {
      const currentPlan = await readPlan(verify, body.planId)
      expect(currentPlan.response.status()).toBe(200)
      expect(currentPlan.body).toMatchObject({ planId: body.planId, status: 'Approved', version: 2 })
      expect(await readOrder(env, orderBody.orderId)).toMatchObject({
        orderId: orderBody.orderId,
        planId: body.planId,
        companyId: env.seed.companyId,
        status: 'Open',
        version: 1,
      })
    } finally {
      await verify.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('an undelivered save is replayed with the same key and creates one plan', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    await openPlanForm(page, env.seed.rolls.planReplay)

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
    const candidate = await readParentRollCandidate(env, env.seed.rolls.planRefusal)
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
