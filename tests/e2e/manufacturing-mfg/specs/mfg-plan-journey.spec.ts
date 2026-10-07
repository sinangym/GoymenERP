/**
 * MFG plan create/reopen/edit — real Chromium, API and SQL-backed owner stores.
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
const PLAN_LIST_RE = /\/api\/v1\/manufacturing\/plans(?:\?|$)/
const PLAN_APPROVAL_RE = /\/api\/v1\/manufacturing\/plans\/[^/]+\/approval$/
const PLAN_REVISION_RE = /\/api\/v1\/manufacturing\/plans\/[^/]+\/revision$/
const PLAN_REVISIONS_RE = /\/api\/v1\/manufacturing\/plans\/[^/]+\/revisions$/
const PLAN_PREVIEW_RE = /\/api\/v1\/manufacturing\/plans\/preview$/
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

const PROCESS_PREVIEW_CONTRACT: Readonly<Record<CanonicalProcess, {
  readonly formulas: readonly string[]
  readonly missingParameters: readonly string[]
}>> = {
  Slitting: {
    formulas: ['SLT-001…007', 'SLT-010'],
    missingParameters: [
      'densityKgPerCubicMetre', 'lineSpeedMetresPerMinute', 'lines[0].bandWidthMm',
      'lines[0].count', 'netUsableWidthMm',
    ],
  },
  ColdRolling: {
    formulas: ['CR-001…005'],
    missingParameters: ['densityKgPerCubicMetre', 'scrapKg', 'targetThicknessMm'],
  },
  TubeProfile: {
    formulas: ['TUB-001…003', 'TUB-004'],
    missingParameters: [
      'lineSpeedMetresPerMinute', 'packageCount', 'pieceLengthMm', 'piecesPerPackage',
      'totalPieces', 'unitKg',
    ],
  },
  Paint: {
    formulas: ['PNT-001…004', 'PNT-005', 'PNT-006'],
    missingParameters: [
      'areaSquareMetres', 'filmMicrons', 'lineSpeedMetresPerMinute', 'packageCount',
      'perimeterMm', 'pieces', 'piecesPerPackage', 'profileLengthMm', 'scrapFactor',
      'simultaneousProfiles', 'solidsRatio',
    ],
  },
  CutToLength: {
    formulas: ['CUT-001…006'],
    missingParameters: ['densityKgPerCubicMetre', 'lines[0].count', 'lines[0].targetLengthMm'],
  },
}

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

function storedPlanInputRows(page: Page) {
  return page.getByRole('region', { name: 'Planlanan ana rulolar' })
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
        await expect(page.locator('dt', { hasText: /^Proses$/ }).locator('..').getByText(process, { exact: true }))
          .toBeVisible()

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
    await expect(storedPlanInputRows(page).getByRole('row', { name: new RegExp(`${candidate.labelId}.*1000`) }))
      .toBeVisible()

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

  test('a saved plan whose screen was closed is found in the plan list, reopened, approved and ordered', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const candidate = await openPlanForm(page, env.seed.rolls.planCreate)

    const sentPlan = page.waitForRequest((request) => PLAN_POST_RE.test(request.url()) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Taslak planı kaydet', exact: true }).click()
    const body = (await sentPlan).postDataJSON() as { planId: string }
    await expect(page.getByRole('heading', { name: 'Taslak plan kaydedildi' })).toBeVisible()
    const reference = body.planId.slice(-8)

    // Close the screen: the user leaves for the order list, and the plan's URL is no longer in hand.
    await page.goto('/uretim/emirler')
    await expect(page.getByRole('heading', { name: 'Üretim emirleri' })).toBeVisible()

    // Find it again from the order list, through the plan list's own server read.
    const listAnswer = page.waitForResponse(
      (response) => PLAN_LIST_RE.test(response.url()) && response.request().method() === 'GET',
    )
    await page.getByRole('link', { name: 'Üretim planları', exact: true }).click()
    const list = await listAnswer
    expect(list.status()).toBe(200)
    expect(list.request().headers()['x-company-id']).toBe(env.seed.companyId)
    expect(new URL(page.url()).pathname).toBe('/uretim/planlar')
    await expect(page.getByRole('heading', { name: 'Üretim planları' })).toBeVisible()
    const listed = (await list.json()) as { plans: Array<Record<string, unknown>>; nextCursor: string | null }
    expect(listed.plans.find((row) => row.planId === body.planId)).toMatchObject({
      planId: body.planId,
      workCenterId: env.seed.workCenterId,
      status: 'Draft',
      version: 1,
      plannedRollCount: 1,
      plannedInputKg: '1000',
      approvedAtUtc: null,
    })
    const row = page.getByRole('row', { name: new RegExp(reference) })
    await expect(row).toContainText('Taslak')
    await expect(row).toContainText('1000')

    // Reopen from the list row: the existing plan detail reads the stored plan afresh.
    const reopened = page.waitForResponse(
      (response) => PLAN_GET_RE.test(response.url()) && response.url().endsWith(body.planId)
        && response.request().method() === 'GET',
    )
    await row.getByRole('link', { name: new RegExp(reference) }).click()
    expect((await reopened).status()).toBe(200)
    expect(new URL(page.url()).pathname).toBe(`/uretim/plan/${body.planId}`)
    await expect(page.getByRole('heading', { name: 'Plan yeniden açıldı' })).toBeVisible()
    await expect(storedPlanInputRows(page).getByRole('row', { name: new RegExp(`${candidate.labelId}.*1000`) }))
      .toBeVisible()

    // Downstream: approve, then create the order from the reopened plan.
    const approvalResponse = page.waitForResponse(
      (response) => PLAN_APPROVAL_RE.test(response.url()) && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Planı onayla', exact: true }).click()
    expect((await approvalResponse).status()).toBe(200)
    await expect(page.getByText('Plan onaylandı ve güncel kayıt yeniden okundu.', { exact: true })).toBeVisible()

    const orderResponse = page.waitForResponse(
      (response) => ORDER_POST_RE.test(response.url()) && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Üretim emri oluştur', exact: true }).click()
    const created = await orderResponse
    expect(created.status()).toBe(201)
    const orderBody = created.request().postDataJSON() as { orderId: string; planId: string }
    expect(orderBody.planId).toBe(body.planId)
    await expect(page.getByRole('heading', { name: 'Üretim emri' })).toBeVisible()

    // Back to the reopened plan, and from its own back link to the plan list: the list reads the server again and
    // the same plan now reads as approved; the Draft filter no longer shows it.
    await page.goBack()
    await expect(page.getByRole('heading', { name: 'Plan yeniden açıldı' })).toBeVisible()
    expect(new URL(page.url()).pathname).toBe(`/uretim/plan/${body.planId}`)
    const backToList = page.waitForResponse(
      (response) => PLAN_LIST_RE.test(response.url()) && response.request().method() === 'GET',
    )
    await page.getByRole('link', { name: '← Üretim planları', exact: true }).click()
    const relisted = await backToList
    expect(relisted.status()).toBe(200)
    expect(relisted.request().headers()['x-company-id']).toBe(env.seed.companyId)
    expect(new URL(page.url()).pathname).toBe('/uretim/planlar')
    await expect(page.getByRole('heading', { name: 'Üretim planları' })).toBeVisible()
    await expect(page.getByRole('row', { name: new RegExp(reference) })).toContainText('Onaylı')
    const draftsAnswer = page.waitForResponse(
      (response) => PLAN_LIST_RE.test(response.url()) && response.url().includes('status=Draft')
        && response.request().method() === 'GET',
    )
    await page.getByLabel('Durum').selectOption('Draft')
    const drafts = await draftsAnswer
    expect(drafts.status()).toBe(200)
    const draftPage = (await drafts.json()) as { plans: Array<{ planId: string }> }
    expect(draftPage.plans.some((plan) => plan.planId === body.planId)).toBe(false)
    await expect(page.getByRole('row', { name: new RegExp(reference) })).toHaveCount(0)

    const verify = await apiAs(env, env.seed.recorderActor)
    try {
      const currentPlan = await readPlan(verify, body.planId)
      expect(currentPlan.body).toMatchObject({ planId: body.planId, status: 'Approved', version: 2 })
      expect(await readOrder(env, orderBody.orderId)).toMatchObject({
        orderId: orderBody.orderId,
        planId: body.planId,
        companyId: env.seed.companyId,
        status: 'Open',
      })
      // The API list agrees with the screen: the approved plan is in this company's Approved page.
      const approvedList = await verify.get('/api/v1/manufacturing/plans?status=Approved&pageSize=100')
      expect(approvedList.status()).toBe(200)
      expect(((await approvedList.json()) as { plans: Array<{ planId: string }> }).plans.map((plan) => plan.planId))
        .toContain(body.planId)
    } finally {
      await verify.dispose()
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
      // The recorder holds no grant in the other company: refused before anything is read.
      expect((await otherCompany.get('/api/v1/manufacturing/plans')).status()).toBe(403)
    } finally {
      await otherCompany.dispose()
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

  test('AT-061 / AT-062: the form previews the typed content live through the server calculator and writes nothing', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const api = await apiAs(env, env.seed.recorderActor)
    const roll = env.seed.rolls.planCreate
    try {
      const newestPlan = async () => {
        const response = await api.get('/api/v1/manufacturing/plans?pageSize=1')
        expect(response.status()).toBe(200)
        return ((await response.json()) as { plans: Array<{ planId: string }> }).plans[0]?.planId ?? null
      }
      const newestBefore = await newestPlan()

      // No "Hesapla" button: typing the planned kg is enough. The answer for the final content is awaited.
      const previewOf = (kg: string) => page.waitForResponse((response) =>
        PLAN_PREVIEW_RE.test(response.url()) && response.request().method() === 'POST'
          && (response.request().postDataJSON() as { inputs: Array<{ plannedKg: string }> }).inputs[0]?.plannedKg === kg)
      const firstPreview = previewOf('1000')
      const candidate = await openPlanForm(page, roll)
      const response = await firstPreview
      expect(response.status()).toBe(200)
      expect(response.request().headers()['idempotency-key']).toBeUndefined()
      const preview = (await response.json()) as Record<string, unknown>
      expect(preview).toMatchObject({
        calculatorVersion: 'GDC-PLAN-CALC/1',
        workCenterId: env.seed.workCenterId,
        plannedRollCount: 1,
        plannedInputKg: '1000',
        target: null,
        hasRefusal: false,
      })
      // OPEN-007: no density, speed or width is decided or wired, so the engine names them; nothing is defaulted.
      const missing = preview.missingParameters as string[]
      expect(missing).toContain('densityKgPerCubicMetre')

      const panel = page.getByTestId('plan-preview')
      await expect(panel.getByText(/Önizleme güncel/)).toBeVisible()
      await expect(panel.getByText('1000 kg', { exact: true })).toBeVisible()
      await expect(panel.getByRole('list', { name: 'Eksik parametreler' })).toContainText('densityKgPerCubicMetre')
      await expect(panel).toContainText('varsayılan değer kullanmaz')

      // AT-062: an invalid content is refused with its reason and field; the earlier answer is not shown as current.
      const kg = page.getByRole('textbox', { name: `${roll.labelCode} planlanan kg` })
      const refusedPreview = previewOf('1000.0001')
      await kg.fill('1000.0001')
      const refused = await refusedPreview
      expect(refused.status()).toBe(422)
      expect((await refused.json()) as Record<string, unknown>).toMatchObject({
        code: 'manufacturing_mass_scale_exceeded',
        field: 'inputs.plannedKg',
      })
      await expect(panel.getByRole('alert')).toContainText('manufacturing_mass_scale_exceeded')
      await expect(panel.getByText(/Güncel değil/)).toBeVisible()
      await expect(panel.getByText(/Önizleme güncel/)).toHaveCount(0)

      // A preview is not a record: no plan appeared.
      expect(await newestPlan()).toBe(newestBefore)

      // Back to the valid content, then save: the server's calculation of the saved content is the preview.
      const againPreview = previewOf('1000')
      await kg.fill('1000')
      expect((await againPreview).status()).toBe(200)
      await expect(panel.getByText(/Önizleme güncel/)).toBeVisible()
      const sentPlan = page.waitForRequest((request) => PLAN_POST_RE.test(request.url()) && request.method() === 'POST')
      await page.getByRole('button', { name: 'Taslak planı kaydet', exact: true }).click()
      const body = (await sentPlan).postDataJSON() as {
        planId: string
        workCenterId: string
        inputs: Array<{ parentRollId: string; plannedKg: string; expectedLabelVersion: string }>
        target: null
      }
      await expect(page.getByRole('heading', { name: 'Taslak plan kaydedildi' })).toBeVisible()
      expect(body.inputs).toEqual([{ parentRollId: candidate.labelId, plannedKg: '1000', expectedLabelVersion: candidate.labelVersion }])
      const stored = await readPlan(api, body.planId)
      expect(stored.response.status()).toBe(200)
      const storedBody = stored.body as {
        workCenterId: string
        inputs: Array<{ parentRollId: string; plannedKg: string; commercialContext: null }>
        target: null
      }
      expect(storedBody.inputs).toMatchObject([{ parentRollId: candidate.labelId, plannedKg: '1000', commercialContext: null }])
      const ofSaved = await api.post('/api/v1/manufacturing/plans/preview', {
        data: {
          workCenterId: storedBody.workCenterId,
          inputs: storedBody.inputs.map((input) => ({
            parentRollId: input.parentRollId,
            plannedKg: input.plannedKg,
            expectedLabelVersion: candidate.labelVersion,
            commercialContext: input.commercialContext,
          })),
          target: storedBody.target,
        },
      })
      expect(ofSaved.status()).toBe(200)
      expect(await ofSaved.json()).toEqual(preview)
    } finally {
      await api.dispose()
    }

    // The preview is the plan save's grant: a reader is refused and learns nothing.
    const reader = await apiAs(env, env.seed.readerActor)
    try {
      const denied = await reader.post('/api/v1/manufacturing/plans/preview', {
        data: { workCenterId: env.seed.workCenterId, inputs: [], target: null },
      })
      expect(denied.status()).toBe(403)
    } finally {
      await reader.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })

  test('each canonical process previews through its server engine and names every still-unknown parameter', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const api = await apiAs(env, env.seed.recorderActor)

    try {
      const workCenters = await readCanonicalWorkCenters(api)
      const newestPlan = async () => {
        const response = await api.get('/api/v1/manufacturing/plans?pageSize=1')
        expect(response.status()).toBe(200)
        return ((await response.json()) as { plans: Array<{ planId: string }> }).plans[0]?.planId ?? null
      }
      const newestBefore = await newestPlan()

      for (const process of CANONICAL_PROCESSES) {
        const workCenter = workCenters.get(process)!
        const previewAnswer = page.waitForResponse((response) => {
          if (!PLAN_PREVIEW_RE.test(response.url()) || response.request().method() !== 'POST') return false
          const request = response.request().postDataJSON() as {
            workCenterId?: string
            inputs?: Array<{ plannedKg?: string }>
          }
          return request.workCenterId === workCenter.id && request.inputs?.[0]?.plannedKg === '1000'
        })

        await openPlanForm(page, env.seed.rolls.planCreate, workCenter.id)
        const response = await previewAnswer
        expect(response.status()).toBe(200)
        const preview = await response.json() as {
          calculatorVersion: string
          process: string
          plannedRollCount: number
          plannedInputKg: string
          answers: Array<{ formula: string; outcome: string }>
          rolls: Array<{ answers: Array<{ formula: string; outcome: string }> }>
          missingParameters: string[]
          hasRefusal: boolean
        }
        const expected = PROCESS_PREVIEW_CONTRACT[process]
        const answers = [...preview.answers, ...preview.rolls.flatMap((roll) => roll.answers)]

        expect(preview).toMatchObject({
          calculatorVersion: 'GDC-PLAN-CALC/1',
          process,
          plannedRollCount: 1,
          plannedInputKg: '1000',
          missingParameters: expected.missingParameters,
          hasRefusal: false,
        })
        expect(answers.map((answer) => answer.formula)).toEqual(expected.formulas)
        expect(answers.every((answer) => answer.outcome === 'Unavailable')).toBe(true)

        const panel = page.getByTestId('plan-preview')
        await expect(panel.getByText(/Önizleme güncel/)).toBeVisible()
        await expect(panel.getByRole('list', { name: 'Eksik parametreler' }))
          .toContainText(expected.missingParameters[0])
        await expect(panel).toContainText('varsayılan değer kullanmaz')
      }

      // A preview is a read-only calculation for every process; the loop must not create a plan.
      expect(await newestPlan()).toBe(newestBefore)
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

  test('a draft plan is edited, closed and reopened with its history, and the approval and order read the edit', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const candidate = await openPlanForm(page, env.seed.rolls.planCreate)

    const sentPlan = page.waitForRequest((request) => PLAN_POST_RE.test(request.url()) && request.method() === 'POST')
    const createdAnswer = page.waitForResponse(
      (response) => PLAN_POST_RE.test(response.url()) && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Taslak planı kaydet', exact: true }).click()
    const body = (await sentPlan).postDataJSON() as { planId: string }
    await expect(page.getByRole('heading', { name: 'Taslak plan kaydedildi' })).toBeVisible()

    const createdResponse = await createdAnswer
    expect(createdResponse.status()).toBe(201)
    const createdPlan = (await createdResponse.json()) as { inputs: Array<{ parentRollId: string; technicalSnapshot: unknown }> }
    expect(createdPlan.inputs).toHaveLength(1)
    expect(createdPlan.inputs[0].parentRollId).toBe(candidate.labelId)
    const createdTechnicalSnapshot = createdPlan.inputs[0].technicalSnapshot

    // Reopen: the plan and its history are both read from the server; the history has only the creation.
    const historyRead = page.waitForResponse(
      (response) => PLAN_REVISIONS_RE.test(response.url()) && response.request().method() === 'GET',
    )
    await page.getByRole('link', { name: 'Formu kapat ve planı yeniden aç' }).click()
    const initialHistoryResponse = await historyRead
    expect(initialHistoryResponse.status()).toBe(200)
    const initialHistory = (await initialHistoryResponse.json()) as {
      revisions: Array<{ recordedAtUtc: string; inputs: Array<{ technicalSnapshot: unknown }> }>
    }
    const capturedTechnicalSnapshot = initialHistory.revisions[0]?.inputs[0]?.technicalSnapshot
    // Exact released tuple used by MfgBrowserSeed.EnsureGenuineProductAsync:
    // Product.Infrastructure/Persistence/ProductInitialTechnicalDefinitionV1.cs (4A source release),
    // Settings.Application/ApprovedUnitProfileCatalog.cs + Contracts/V1/SettingsUnitIdentitiesV1.cs.
    // ManufacturingApiV1.VerifyPlanContentAsync selects UTC today; revision 0 records the creation time.
    const createdOnUtc = new Date(initialHistory.revisions[0].recordedAtUtc).toISOString().slice(0, 10)
    expect(capturedTechnicalSnapshot).toEqual({
      productId: env.seed.output.productId,
      productVersion: env.seed.output.productVersion,
      family: {
        familyId: '2ffdf417-c7b0-4cc5-8c58-40b5cd906c55',
        familyCode: 'hr-slit-coil',
        sourceRevision: 'issue66-5973142585-4A',
      },
      template: {
        templateId: 'ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2',
        templateVersion: '1',
        templateCode: 'gdc-flat-coil-v1',
        sourceRevision: 'issue66-5973142585-4A',
      },
      thicknessMm: '1',
      widthMm: '1000',
      unitKg: {
        companyId: '10000000-0000-0000-0000-000000000001',
        unitId: 'c5ef643e-a6e8-51f3-89f2-8e68b00cb983',
        unitCode: 'kg',
        profileId: '4a790fdc-2a6b-5126-835f-d3a13d28b71c',
        profileVersion: '1',
      },
      effectiveOn: createdOnUtc,
    })
    expect(capturedTechnicalSnapshot).toEqual(createdTechnicalSnapshot)
    await expect(page.getByRole('heading', { name: 'Plan yeniden açıldı' })).toBeVisible()
    await expect(page.getByText('Plan oluşturulduğundan beri düzenlenmedi.', { exact: true })).toBeVisible()

    // Edit: the planned kg and a target change, with a reason; the roll keeps its accepted label version.
    await page.getByRole('button', { name: 'Planı düzenle', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Taslak planı düzenle' })).toBeVisible()
    const kg = page.getByRole('textbox', { name: `${candidate.labelId} planlanan kg` })
    await expect(kg).toHaveValue('1000')
    await kg.fill('800')
    await page.getByLabel('Hedef türü').selectOption('GoodOutputMass')
    await page.getByLabel('Hedef kg').fill('750')
    await page.getByRole('textbox', { name: 'Düzenleme gerekçesi' }).fill('Tarayıcı kabulü: bobin kg düşürüldü')
    const revision = page.waitForResponse(
      (response) => PLAN_REVISION_RE.test(response.url()) && response.request().method() === 'POST',
    )
    const revisedRead = page.waitForResponse(
      (response) => PLAN_GET_RE.test(response.url()) && response.url().endsWith(body.planId)
        && response.request().method() === 'GET',
    )
    await page.getByRole('button', { name: 'Değişikliği kaydet', exact: true }).click()
    const revised = await revision
    expect(revised.status()).toBe(200)
    const revisedPlan = (await revised.json()) as { inputs: Array<{ parentRollId: string; technicalSnapshot: unknown }> }
    expect(revisedPlan.inputs).toHaveLength(1)
    expect(revisedPlan.inputs[0].parentRollId).toBe(candidate.labelId)
    expect(revisedPlan.inputs[0].technicalSnapshot).toEqual(capturedTechnicalSnapshot)
    const key = revised.request().headers()['idempotency-key']
    expect(key).toMatch(/^[0-9a-f-]{36}$/)
    expect(revised.request().headers()['x-company-id']).toBe(env.seed.companyId)
    const edit = revised.request().postDataJSON() as Record<string, unknown>
    expect(edit).toEqual({
      expectedVersion: 1,
      workCenterId: env.seed.workCenterId,
      inputs: [{
        parentRollId: candidate.labelId,
        plannedKg: '800',
        expectedLabelVersion: candidate.labelVersion,
        commercialContext: null,
      }],
      target: { basis: 'GoodOutputMass', kg: '750' },
      reason: 'Tarayıcı kabulü: bobin kg düşürüldü',
    })
    const revisedPlanResponse = await revisedRead
    expect(revisedPlanResponse.status()).toBe(200)
    const freshPlan = (await revisedPlanResponse.json()) as { inputs: Array<{ parentRollId: string; technicalSnapshot: unknown }> }
    expect(freshPlan.inputs).toHaveLength(1)
    expect(freshPlan.inputs[0].parentRollId).toBe(candidate.labelId)
    expect(freshPlan.inputs[0].technicalSnapshot).toEqual(capturedTechnicalSnapshot)
    await expect(page.getByText('Plan güncellendi (sürüm 2); güncel kayıt ve geçmiş yeniden okundu.', { exact: true }))
      .toBeVisible()
    await expect(storedPlanInputRows(page).getByRole('row', { name: new RegExp(`${candidate.labelId}.*800`) }))
      .toBeVisible()

    // Close the screen and reopen it from the plan list: the list, the plan and the history all read the edit.
    await page.goto('/uretim/emirler')
    const listAnswer = page.waitForResponse(
      (response) => PLAN_LIST_RE.test(response.url()) && response.request().method() === 'GET',
    )
    await page.getByRole('link', { name: 'Üretim planları', exact: true }).click()
    const listed = (await (await listAnswer).json()) as { plans: Array<Record<string, unknown>> }
    expect(listed.plans.find((row) => row.planId === body.planId)).toMatchObject({
      version: 2,
      status: 'Draft',
      plannedRollCount: 1,
      plannedInputKg: '800',
      target: { basis: 'GoodOutputMass', kg: '750' },
    })
    const reference = body.planId.slice(-8)
    const reopenedHistory = page.waitForResponse(
      (response) => PLAN_REVISIONS_RE.test(response.url()) && response.request().method() === 'GET',
    )
    await page.getByRole('row', { name: new RegExp(reference) }).getByRole('link', { name: new RegExp(reference) }).click()
    const reopenedHistoryResponse = await reopenedHistory
    expect(reopenedHistoryResponse.status()).toBe(200)
    const reopenedRevisions = (await reopenedHistoryResponse.json()) as {
      revisions: Array<{ revision: number; inputs: Array<{ parentRollId: string; technicalSnapshot: unknown }> }>
    }
    expect(reopenedRevisions.revisions.map((entry) => entry.revision)).toEqual([0, 1])
    for (const entry of reopenedRevisions.revisions) {
      expect(entry.inputs).toHaveLength(1)
      expect(entry.inputs[0].parentRollId).toBe(candidate.labelId)
      expect(entry.inputs[0].technicalSnapshot).toEqual(capturedTechnicalSnapshot)
    }
    const history = page.getByRole('region', { name: 'Plan geçmişi' })
    const latest = history.getByRole('listitem', { name: 'Revizyon 1' })
    await expect(latest).toContainText('Gerekçe: Tarayıcı kabulü: bobin kg düşürüldü')
    await expect(latest).toContainText(`Ana rulo ${candidate.labelId}: 1000 kg → 800 kg`)
    await expect(latest).toContainText('Hedef: hedef yok → İyi ürün kütlesi 750 kg')
    await expect(history.getByRole('listitem', { name: 'Revizyon 0' })).toBeVisible()

    const api = await apiAs(env, env.seed.recorderActor)
    try {
      // The stored plan and its history are the edit.
      const storedPlan = (await readPlan(api, body.planId)).body
      if (storedPlan === null) throw new Error('The edited plan must reopen from the stored plan read.')
      expect(storedPlan).toMatchObject({
        planId: body.planId,
        status: 'Draft',
        version: 2,
        inputs: [{ parentRollId: candidate.labelId, plannedKg: '800' }],
        target: { basis: 'GoodOutputMass', kg: '750' },
      })
      const storedInputs = storedPlan.inputs as Array<{ technicalSnapshot: unknown }>
      expect(storedInputs).toHaveLength(1)
      expect(storedInputs[0].technicalSnapshot).toEqual(capturedTechnicalSnapshot)
      const revisions = await api.get(`/api/v1/manufacturing/plans/${body.planId}/revisions`)
      expect(revisions.status()).toBe(200)
      const stored = (await revisions.json()) as { revisions: Array<Record<string, unknown>> }
      expect(stored.revisions.map((entry) => entry.revision)).toEqual([0, 1])
      expect(stored.revisions[1]).toMatchObject({
        planVersion: 2,
        reason: 'Tarayıcı kabulü: bobin kg düşürüldü',
        inputs: [{
          parentRollId: candidate.labelId,
          plannedKg: '800',
          expectedLabelVersion: candidate.labelVersion,
        }],
      })
      for (const entry of stored.revisions) {
        const inputs = (entry as { inputs: Array<{ technicalSnapshot: unknown }> }).inputs
        expect(inputs).toHaveLength(1)
        expect(inputs[0].technicalSnapshot).toEqual(capturedTechnicalSnapshot)
      }

      // Idempotent replay: the browser's key and body again answer the stored result, no second revision.
      const replay = await api.post(`/api/v1/manufacturing/plans/${body.planId}/revision`, {
        data: edit,
        headers: { 'Idempotency-Key': key },
      })
      expect(replay.status()).toBe(200)
      expect(replay.headers()['idempotency-replayed']).toBe('true')
      // A stale version under a new key is 409 with both versions.
      const stale = await api.post(`/api/v1/manufacturing/plans/${body.planId}/revision`, {
        data: { ...edit, reason: 'eski sürüm' },
        headers: { 'Idempotency-Key': randomUUID() },
      })
      expect(stale.status()).toBe(409)
      expect(await stale.json()).toMatchObject({
        code: 'manufacturing_version_conflict',
        expectedVersion: '1',
        currentVersion: '2',
      })
    } finally {
      await api.dispose()
    }

    // The next steps read the edited plan: the approval names version 2 and the order snapshots 800 kg.
    const approvalResponse = page.waitForResponse(
      (response) => PLAN_APPROVAL_RE.test(response.url()) && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Planı onayla', exact: true }).click()
    const approval = await approvalResponse
    expect(approval.status()).toBe(200)
    expect(approval.request().postDataJSON()).toEqual({ expectedVersion: 2 })
    await expect(page.getByText('Onaylı plan düzenlenmez', { exact: false })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Planı düzenle', exact: true })).toHaveCount(0)
    const orderResponse = page.waitForResponse(
      (response) => ORDER_POST_RE.test(response.url()) && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Üretim emri oluştur', exact: true }).click()
    const created = await orderResponse
    expect(created.status()).toBe(201)
    const orderBody = created.request().postDataJSON() as { orderId: string }
    await expect(page.getByRole('heading', { name: 'Üretim emri' })).toBeVisible()

    const verify = await apiAs(env, env.seed.recorderActor)
    try {
      expect(await readOrder(env, orderBody.orderId)).toMatchObject({
        planId: body.planId,
        status: 'Open',
        target: { basis: 'GoodOutputMass', kg: '750' },
        rolls: [{ parentRollId: candidate.labelId, plannedKg: '800' }],
      })
      // Fail closed after the downstream effect: the approved plan (with its order) is not edited.
      const afterApproval = await verify.post(`/api/v1/manufacturing/plans/${body.planId}/revision`, {
        data: { ...edit, expectedVersion: 3, reason: 'onaydan sonra' },
        headers: { 'Idempotency-Key': randomUUID() },
      })
      expect(afterApproval.status()).toBe(422)
      expect(await afterApproval.json()).toMatchObject({ code: 'manufacturing_plan_not_draft' })
    } finally {
      await verify.dispose()
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
      // The recorder holds no grant in the other company: the edit and the history are refused before any read.
      const foreign = await otherCompany.post(`/api/v1/manufacturing/plans/${body.planId}/revision`, {
        data: { ...edit, expectedVersion: 3 },
        headers: { 'Idempotency-Key': randomUUID() },
      })
      expect(foreign.status()).toBe(403)
      expect((await otherCompany.get(`/api/v1/manufacturing/plans/${body.planId}/revisions`)).status()).toBe(403)
    } finally {
      await otherCompany.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })
})

test.describe('AT-063 "Üretim planla" from an ST-01 stock row (real Inventory position and candidate reads)', () => {
  test('the row action opens the plan form with the roll preselected; the save revalidates it and the detail shows it', async ({ page, context }) => {
    const rewritten = await asActor(context, env.seed.recorderActor)
    const answered = watchAnswers(page)
    const roll = env.seed.rolls.planCreate

    // ST-01 filtered by the roll's code: the recorder's row menu offers the plan action beside the label reads only.
    const positions = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/inventory/positions'
      && new URL(response.url()).searchParams.get('q') === roll.labelCode && response.request().method() === 'GET')
    await page.goto(`/stok/pozisyonlar?etiket=${encodeURIComponent(roll.labelCode)}`)
    expect((await positions).status()).toBe(200)
    const row = page.locator(`tr[data-label-id="${roll.labelId}"]`)
    await expect(row).toBeVisible({ timeout: 30_000 })
    await row.getByRole('button', { name: `${roll.labelCode} işlemleri`, exact: true }).click()
    const menu = page.getByRole('list', { name: `${roll.labelCode} için işlemler`, exact: true })
    await expect(menu.getByRole('link')).toHaveText(['Etiket 360’ı aç', 'Üretim planla', 'Etiket / lot soy ağacı'])

    // The action names the label; the form finds it through the SAME Inventory candidate read a manual search uses.
    const candidatesAnswer = page.waitForResponse(
      (response) => CANDIDATES_RE.test(response.url()) && response.request().method() === 'GET',
    )
    await menu.getByRole('link', { name: 'Üretim planla', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/uretim/plan/yeni\\?etiket=${roll.labelId}&kod=`))
    const candidatesResponse = await candidatesAnswer
    expect(candidatesResponse.status()).toBe(200)
    expect(new URL(candidatesResponse.url()).searchParams.get('q')).toBe(roll.labelCode)
    const candidate = ((await candidatesResponse.json()) as { items: CandidateAnswer[] }).items
      .find((item) => item.labelId === roll.labelId)
    expect(candidate).toMatchObject({ labelId: roll.labelId, draftSelectable: true })
    await expect(page.getByRole('heading', { name: 'Yeni taslak plan' })).toBeVisible()
    await expect(page.getByText(`Stok pozisyonundan seçilen ana rulo eklendi: ${roll.labelCode}.`, { exact: false })).toBeVisible()

    await page.getByRole('combobox', { name: 'İş merkezi' }).selectOption(env.seed.workCenterId)
    await page.getByRole('textbox', { name: `${roll.labelCode} planlanan kg` }).fill('500')
    const sentPlan = page.waitForRequest((request) => PLAN_POST_RE.test(request.url()) && request.method() === 'POST')
    await page.getByRole('button', { name: 'Taslak planı kaydet', exact: true }).click()
    const body = (await sentPlan).postDataJSON() as {
      planId: string
      inputs: Array<{ parentRollId: string; plannedKg: string; expectedLabelVersion: string }>
    }
    expect(body.inputs).toEqual([
      { parentRollId: roll.labelId, plannedKg: '500', expectedLabelVersion: candidate!.labelVersion },
    ])
    await expect(page.getByRole('heading', { name: 'Taslak plan kaydedildi' })).toBeVisible()

    const freshRead = page.waitForResponse(
      (response) => PLAN_GET_RE.test(response.url()) && response.url().endsWith(body.planId)
        && response.request().method() === 'GET',
    )
    await page.getByRole('link', { name: 'Formu kapat ve planı yeniden aç' }).click()
    expect((await freshRead).status()).toBe(200)
    await expect(page.getByRole('heading', { name: 'Plan yeniden açıldı' })).toBeVisible()
    await expect(page.getByRole('rowheader', { name: roll.labelId, exact: true })).toBeVisible()

    const api = await apiAs(env, env.seed.recorderActor)
    try {
      const stored = await readPlan(api, body.planId)
      expect(stored.response.status()).toBe(200)
      expect(stored.body).toMatchObject({ planId: body.planId, status: 'Draft', inputs: [{ parentRollId: roll.labelId }] })
    } finally {
      await api.dispose()
    }
    expectEveryAnswerRewritten(answered, rewritten)
  })
})
