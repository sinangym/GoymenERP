import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY, planCreateFromLabelPath } from './capabilities'
import {
  APPROVER,
  COMPANY_A,
  IDENTITY,
  ROLL_1,
  ROLL_2,
  WORK_CENTER,
  installFetch,
  json,
  plan,
  problem,
  reloadDocument,
  renderAt,
  requestsFor,
  type RecordedRequest,
} from './testHarness'

const PRODUCT = '10000000-0000-4000-8000-000000000001'
const LOT = '20000000-0000-4000-8000-000000000001'
const LOCATION = '40000000-0000-4000-8000-000000000001'
const CUT_WORK_CENTER = 'd0000000-0000-4000-8000-000000000007'

function workCenter() {
  return {
    id: WORK_CENTER,
    locationId: LOCATION,
    code: 'SLT-01',
    name: 'Dilme Hattı',
    kind: 'Slitting',
    isActive: true,
    canOpenLocationDetail: false,
  }
}

function cutWorkCenter() {
  return { ...workCenter(), id: CUT_WORK_CENTER, code: 'CTL-01', name: 'Boy Kesme Hattı', kind: 'CutToLength' }
}

function candidate(labelId = ROLL_1, overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY_A,
    labelId,
    labelVersion: labelId === ROLL_1 ? '7' : '12',
    labelCode: labelId === ROLL_1 ? 'LBL-0007' : 'LBL-0012',
    productId: PRODUCT,
    productVersion: labelId === ROLL_1 ? 'release-A.02' : 'release-B.04',
    lot: { id: LOT, heatNumber: 'H-12', coilNumber: null },
    heatNumber: 'H-12',
    coilNumber: null,
    documentStatus: 'Complete',
    requiredDocuments: ['MillCertificate'],
    attachedDocuments: ['MillCertificate'],
    physicalKg: '1200.5',
    unblockedPhysicalKg: '1000.5',
    blockedPhysicalKg: '200',
    positions: [{
      ownerKind: 'Company', ownerId: COMPANY_A, custodianKind: 'Company', custodianId: COMPANY_A,
      locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '1000.5',
    }],
    draftSelectable: true,
    draftRefusalCode: null,
    productionUseRequiresRevalidation: true,
    ...overrides,
  }
}

/** The plan SAVES only: the live preview (AT-061) posts to `/plans/preview`, which is not a save. */
function saveRequests(recorded: ReturnType<typeof installFetch>) {
  return requestsFor(recorded, '/manufacturing/plans', 'POST').filter((request) => request.url.endsWith('/manufacturing/plans'))
}

function canonicalDecimal(value: string): string {
  return value.includes('.') ? value.replace(/0+$/, '').replace(/\.$/, '') : value
}

function installPlanApi(options: {
  readonly failFirstSave?: boolean
  readonly commitThenLoseFirstSave?: boolean
  readonly refuseFirstSave?: boolean
  readonly candidates?: () => unknown
  readonly preview?: (body: Record<string, unknown>) => Response | Promise<Response>
  readonly capabilities?: readonly string[]
  readonly actor?: () => string
} = {}) {
  let saves = 0
  let stored: Record<string, unknown> | null = null
  const actor = options.actor
  const recorded = installFetch({
    capabilities: options.capabilities ?? [MANUFACTURING_CAPABILITY.planCreate, MANUFACTURING_CAPABILITY.planDetail],
    identity: actor === undefined ? undefined : () => ({ ...IDENTITY, userId: actor() }),
    respond: (url, init) => {
      if (url.includes('/organization/work-centers')) return json([workCenter(), cutWorkCenter()])
      if (url.includes('/inventory/references/parent-rolls')) {
        if (options.candidates !== undefined) return json(options.candidates())
        return json({
          items: [
            candidate(),
            candidate(ROLL_2, {
              draftSelectable: false,
              draftRefusalCode: 'inventory_parent_roll_context_required',
              positions: [{ ...candidate().positions[0], ownerKind: 'Customer' }],
            }),
          ],
          searchWindowFull: false,
        })
      }
      if (url.endsWith('/manufacturing/plans/preview') && init?.method === 'POST') {
        return options.preview?.(JSON.parse(init.body as string) as Record<string, unknown>)
      }
      if (url.endsWith('/manufacturing/plans') && init?.method === 'POST') {
        saves += 1
        if (options.failFirstSave && saves === 1) throw new TypeError('connection lost')
        if (options.refuseFirstSave && saves === 1) {
          return problem(409, { code: 'inventory_label_version_conflict' })
        }
        const request = JSON.parse(init.body as string) as {
          planId: string
          workCenterId: string
          inputs: Array<{ parentRollId: string; plannedKg: string }>
          target: { basis: string; kg: string } | null
          slittingSpec?: {
            lines: Array<{ bandWidthMm: string; count: number }>
            windingTargetKg: string | null
          } | null
          cutToLengthSpec?: {
            lines: Array<{ lineId: string; sequence: number; lengthMm: string; count: number }>
          }
        }
        stored = plan({
          process: request.workCenterId === CUT_WORK_CENTER ? 'CutToLength' : 'Slitting',
          cutToLengthSpec: request.cutToLengthSpec === undefined
            ? null
            : { lines: request.cutToLengthSpec.lines.map((line) => ({ ...line, lengthMm: canonicalDecimal(line.lengthMm) })) },
          planId: request.planId,
          workCenterId: request.workCenterId,
          inputs: request.inputs.map(({ parentRollId, plannedKg }) => ({
            parentRollId,
            plannedKg: canonicalDecimal(plannedKg),
          })),
          target: request.target,
          slittingSpec: request.slittingSpec === undefined || request.slittingSpec === null
            ? null
            : {
                lines: request.slittingSpec.lines.map((line) => ({
                  bandWidthMm: canonicalDecimal(line.bandWidthMm),
                  count: line.count,
                })),
                windingTargetKg: request.slittingSpec.windingTargetKg === null
                  ? null
                  : canonicalDecimal(request.slittingSpec.windingTargetKg),
              },
        })
        if (options.commitThenLoseFirstSave && saves === 1) throw new TypeError('response lost after commit')
        return json(stored, 201, {
          Location: `/api/v1/manufacturing/plans/${request.planId}`,
          ...(options.commitThenLoseFirstSave && saves > 1
            ? { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-slitting-1' }
            : {}),
        })
      }
      if (url.includes('/manufacturing/plans/') && init?.method !== 'POST' && stored !== null) return json(stored)
      return undefined
    },
  })
  return recorded
}

async function fillPlan(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('option', { name: 'SLT-01 — Dilme Hattı' })
  await user.selectOptions(screen.getByLabelText('İş merkezi'), WORK_CENTER)
  await user.click(screen.getByRole('button', { name: 'Ara' }))
  const own = await screen.findByRole('row', { name: /LBL-0007/ })
  await user.click(within(own).getByRole('button', { name: 'Ekle' }))
  await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '900.50')
  await user.selectOptions(screen.getByLabelText('Hedef türü'), 'GoodOutputMass')
  await user.type(screen.getByLabelText('Hedef kg'), '850')
}

afterEach(() => {
  vi.unstubAllGlobals()
  sessionStorage.clear()
})

describe('plan create and reopen', () => {
  it('selects real work-center and Inventory facts, saves frozen label version, then reopens with GET', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi()
    renderAt('/uretim/plan/yeni')
    await fillPlan(user)

    const customer = screen.getByRole('row', { name: /LBL-0012/ })
    expect(within(customer).getByRole('button', { name: 'Seçilemez' }).hasAttribute('disabled')).toBe(true)
    expect(within(customer).getByText('inventory_parent_roll_context_required')).toBeTruthy()
    expect(screen.getByText(/kullanılabilirlik veya rezervasyon garantisi değildir/)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    const reopen = await screen.findByRole('link', { name: 'Formu kapat ve planı yeniden aç' })

    const save = saveRequests(recorded)[0]
    expect(save.headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect(save.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(save.body).toMatchObject({
      workCenterId: WORK_CENTER,
      inputs: [{ parentRollId: ROLL_1, plannedKg: '900.50', expectedLabelVersion: '7' }],
      target: { basis: 'GoodOutputMass', kg: '850' },
    })

    await user.click(reopen)
    expect(await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })).toBeTruthy()
    expect(screen.getByText('900.5')).toBeTruthy()
    // MFG-PLAN-EDIT: the reopened detail also reads the plan history; the plan read itself is once.
    expect(requestsFor(recorded, '/manufacturing/plans/', 'GET').filter((request) => !request.url.endsWith('/revisions')))
      .toHaveLength(1)
  })

  it('retries an unknown write with exactly the same request and idempotency key', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({ failFirstSave: true })
    renderAt('/uretim/plan/yeni')
    await fillPlan(user)
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))

    const retry = await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' })
    await user.click(retry)
    await screen.findByText('Taslak plan kaydedildi')

    const [first, second] = saveRequests(recorded)
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('previews, saves and reopens the exact ordered slitting draft content while keeping revision unavailable', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({
      preview: (body) => json(previewAnswer(body)),
      commitThenLoseFirstSave: true,
      capabilities: [
        MANUFACTURING_CAPABILITY.planCreate,
        MANUFACTURING_CAPABILITY.planDetail,
        MANUFACTURING_CAPABILITY.planEdit,
      ],
    })
    renderAt('/uretim/plan/yeni')
    await fillPlan(user)
    await user.click(screen.getByRole('checkbox', { name: 'Sıralı bant planı ekle' }))
    await user.type(screen.getByLabelText('Bant 1 genişliği mm'), '150.000')
    await user.type(screen.getByLabelText('Bant 1 adedi'), '4')
    await user.click(screen.getByRole('button', { name: 'Bant satırı ekle' }))
    await user.type(screen.getByLabelText('Bant 2 genişliği mm'), '240.50')
    await user.type(screen.getByLabelText('Bant 2 adedi'), '3')
    await user.type(screen.getByLabelText('İsteğe bağlı sarım hedefi (kg)'), '2000.00')

    await waitFor(() => expect(requestsFor(recorded, '/manufacturing/plans/preview', 'POST').at(-1)?.body)
      .toMatchObject({
        slittingSpec: {
          lines: [{ bandWidthMm: '150.000', count: 4 }, { bandWidthMm: '240.50', count: 3 }],
          windingTargetKg: '2000.00',
        },
      }))
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    const reopen = await screen.findByRole('link', { name: 'Formu kapat ve planı yeniden aç' })
    const [first, saved] = saveRequests(recorded)
    expect(saved.body).toEqual(first.body)
    expect(saved.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    const lastPreview = requestsFor(recorded, '/manufacturing/plans/preview', 'POST').at(-1)
    expect(lastPreview).toBeDefined()
    expect((saved.body as { slittingSpec: unknown }).slittingSpec).toEqual(
      (lastPreview!.body as { slittingSpec: unknown }).slittingSpec,
    )

    await user.click(reopen)
    const spec = await screen.findByRole('heading', { name: 'Dilme ayrıntısı' })
    const section = spec.closest('section') as HTMLElement
    const rows = within(section).getAllByRole('row')
    expect(rows[1].textContent).toContain('150 mm')
    expect(rows[2].textContent).toContain('240.5 mm')
    expect(within(section).getByText('Sarım hedefi: 2000 kg')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
    expect(screen.getByText(/Dilme ayrıntısı taşıyan plan düzenlenmez/)).toBeTruthy()
  })

  it('allows a new attempt with a new key after a definitive refusal', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({ refuseFirstSave: true })
    renderAt('/uretim/plan/yeni')
    await fillPlan(user)
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    expect(await screen.findByText(/sürümü veya yazma durumu değişti/)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await screen.findByText('Taslak plan kaydedildi')

    const [first, second] = saveRequests(recorded)
    expect(second.headers['Idempotency-Key']).not.toBe(first.headers['Idempotency-Key'])
    expect((second.body as { planId: string }).planId).not.toBe((first.body as { planId: string }).planId)
  })

  it('AT-063: preselects the ST-01 row roll through the candidate read and saves it at the candidate version', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi()
    renderAt(planCreateFromLabelPath(ROLL_1.toUpperCase(), 'LBL-0007'))

    expect(await screen.findByText(/Stok pozisyonundan seçilen ana rulo eklendi: LBL-0007/)).toBeTruthy()
    const read = requestsFor(recorded, '/inventory/references/parent-rolls', 'GET')
    expect(read).toHaveLength(1)
    const query = new URL(read[0].url, 'http://x').searchParams
    expect(query.get('q')).toBe('LBL-0007')
    expect(query.get('limit')).toBe('100')
    expect((screen.getByLabelText('Etiket, ürün veya lot ara') as HTMLInputElement).value).toBe('LBL-0007')
    // The candidate row reads 'Eklendi' and the planned-inputs table holds the roll.
    expect(within(screen.getAllByRole('row', { name: /LBL-0007/ })[0]).getByRole('button', { name: 'Eklendi' })).toBeTruthy()

    await screen.findByRole('option', { name: 'SLT-01 — Dilme Hattı' })
    await user.selectOptions(screen.getByLabelText('İş merkezi'), WORK_CENTER)
    await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '400')
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await screen.findByText('Taslak plan kaydedildi')
    expect(saveRequests(recorded)[0].body).toMatchObject({
      workCenterId: WORK_CENTER,
      inputs: [{ parentRollId: ROLL_1, plannedKg: '400', expectedLabelVersion: '7' }],
      target: null,
    })
  })

  it('AT-063: never selects a named roll the candidate read refuses or does not answer', async () => {
    installPlanApi()
    renderAt(planCreateFromLabelPath(ROLL_2, 'LBL-0012'))
    expect(await screen.findByText(/seçilen etiket \(LBL-0012\) taslak plana eklenemez \(inventory_parent_roll_context_required\)/))
      .toBeTruthy()
    expect(screen.getByText('Henüz ana rulo eklenmedi.')).toBeTruthy()
  })

  it('AT-063: says so when the named roll is outside the candidate window, and ignores a malformed label id', async () => {
    installPlanApi({ candidates: () => ({ items: [candidate()], searchWindowFull: true }) })
    const unknownRoll = '30000000-0000-4000-8000-00000000abcd'
    const { view } = renderAt(planCreateFromLabelPath(unknownRoll, 'LBL'))
    expect(await screen.findByText(/ana rulo adayları arasında bulunamadı \(arama penceresi doldu\); etiketi elle arayın/))
      .toBeTruthy()
    expect(screen.getByText('Henüz ana rulo eklenmedi.')).toBeTruthy()
    view.unmount()

    const recorded = installPlanApi()
    renderAt('/uretim/plan/yeni?etiket=not-a-guid&kod=LBL-0007')
    await screen.findByRole('option', { name: 'SLT-01 — Dilme Hattı' })
    expect(requestsFor(recorded, '/inventory/references/parent-rolls')).toHaveLength(0)
    expect(screen.queryByText(/Stok pozisyonundan/)).toBeNull()
  })

  it('keeps the create route closed when its server capability is absent', async () => {
    installFetch({ capabilities: [MANUFACTURING_CAPABILITY.planDetail] })
    renderAt('/uretim/plan/yeni')
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByRole('heading', { name: 'Yeni taslak plan' })).toBeNull()
  })
})

/** The server's preview for the sent content: nothing known from outside, so the engine names what is missing. */
function previewAnswer(body: Record<string, unknown>) {
  const inputs = body.inputs as Array<{ parentRollId: string; plannedKg: string }>
  const kg = canonicalDecimal(inputs[0].plannedKg)
  return {
    calculatorVersion: 'GDC-PLAN-CALC/1',
    workCenterId: body.workCenterId,
    process: 'Slitting',
    plannedRollCount: inputs.length,
    plannedInputKg: kg,
    target: null,
    answers: [],
    rolls: inputs.map((input) => ({
      parentRollId: input.parentRollId,
      plannedKg: canonicalDecimal(input.plannedKg),
      answers: [{
        formula: 'SLT-010',
        formulaVersion: 'GDC-SLT/1 (master v3 §28.4)',
        outcome: 'Unavailable',
        values: [],
        missingParameters: ['densityKgPerCubicMetre', 'lineSpeedMetresPerMinute', 'parentWidthMm', 'thicknessMm'],
        refusalCode: null,
        refusedParameter: null,
      }],
    })),
    missingParameters: ['densityKgPerCubicMetre', 'lineSpeedMetresPerMinute', 'parentWidthMm', 'thicknessMm'],
    hasRefusal: false,
  }
}

describe('M01.3: authoring ordered cut lines on create', () => {
  type CutBody = { cutToLengthSpec?: { lines: Array<{ lineId: string; sequence: number; lengthMm: string; count: number }> } }
  const previewBodies = (recorded: ReturnType<typeof installFetch>) =>
    requestsFor(recorded, '/manufacturing/plans/preview', 'POST').map((request) => request.body as CutBody)

  async function fillCutPlan(user: ReturnType<typeof userEvent.setup>) {
    await screen.findByRole('option', { name: 'CTL-01 — Boy Kesme Hattı' })
    await user.selectOptions(screen.getByLabelText('İş merkezi'), CUT_WORK_CENTER)
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await user.click(within(await screen.findByRole('row', { name: /LBL-0007/ })).getByRole('button', { name: 'Ekle' }))
    await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '1000')
  }

  async function authorTwoRows(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: 'Kesim satırı ekle' }))
    await user.type(screen.getByLabelText('Kesim satırı 1 Sıra no'), '40')
    await user.type(screen.getByLabelText('Kesim satırı 1 Uzunluk (mm)'), '6000.000')
    await user.type(screen.getByLabelText('Kesim satırı 1 Adet'), '3')
    await user.click(screen.getByRole('button', { name: 'Kesim satırı ekle' }))
    await user.type(screen.getByLabelText('Kesim satırı 2 Sıra no'), '-7')
    await user.type(screen.getByLabelText('Kesim satırı 2 Uzunluk (mm)'), '2500.5')
    await user.type(screen.getByLabelText('Kesim satırı 2 Adet'), '2')
  }

  it('offers no row by default and a new row has an empty Sıra no: nothing is numbered for the user', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({ preview: (body) => json(previewAnswer(body)) })
    renderAt('/uretim/plan/yeni')
    await fillCutPlan(user)
    expect(screen.queryAllByLabelText(/Sıra no$/)).toHaveLength(0)
    await waitFor(() => expect(previewBodies(recorded).length).toBeGreaterThan(0))
    expect(previewBodies(recorded).every((body) => !Object.hasOwn(body, 'cutToLengthSpec'))).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Kesim satırı ekle' }))
    expect((screen.getByLabelText('Kesim satırı 1 Sıra no') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Kesim satırı 1 Uzunluk (mm)') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Kesim satırı 1 Adet') as HTMLInputElement).value).toBe('')
    await user.click(screen.getByRole('button', { name: 'Kesim satırı ekle' }))
    expect((screen.getByLabelText('Kesim satırı 2 Sıra no') as HTMLInputElement).value).toBe('')

    // The editor is the CutToLength work center's: a slitting line shows none.
    await user.selectOptions(screen.getByLabelText('İş merkezi'), WORK_CENTER)
    expect(screen.queryByRole('button', { name: 'Kesim satırı ekle' })).toBeNull()
  })

  it('an incomplete or invalid row sends no preview and no save, and says why', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({ preview: (body) => json(previewAnswer(body)) })
    renderAt('/uretim/plan/yeni')
    await fillCutPlan(user)
    await waitFor(() => expect(previewBodies(recorded).length).toBeGreaterThan(0))
    await user.click(screen.getByRole('button', { name: 'Kesim satırı ekle' }))
    await user.type(screen.getByLabelText('Kesim satırı 1 Sıra no'), '40')
    await user.type(screen.getByLabelText('Kesim satırı 1 Uzunluk (mm)'), '6000.0000')
    await user.type(screen.getByLabelText('Kesim satırı 1 Adet'), '0')
    const panel = screen.getByTestId('plan-preview')
    expect(await within(panel).findByText(/Kesim satırı 1: uzunluk/)).toBeTruthy()
    await new Promise((resolve) => setTimeout(resolve, 500))
    expect(previewBodies(recorded).some((body) => Object.hasOwn(body, 'cutToLengthSpec'))).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    const alert = await screen.findByText(/Kesim satırı 1: adet/, { selector: '[role="alert"]' })
    expect(alert).toBeTruthy()
    expect(saveRequests(recorded)).toHaveLength(0)
  })

  it('moves a row by keyboard without changing its line id or Sıra no; preview, unknown save, retry and reopen carry one definition', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({
      preview: (body) => json(previewAnswer(body)),
      commitThenLoseFirstSave: true,
    })
    renderAt('/uretim/plan/yeni')
    await fillCutPlan(user)
    await authorTwoRows(user)

    await waitFor(() => expect(previewBodies(recorded).at(-1)?.cutToLengthSpec?.lines.map((line) => line.sequence))
      .toEqual([40, -7]))
    const beforeMove = previewBodies(recorded).at(-1)!.cutToLengthSpec!.lines
    expect(beforeMove.map((line) => line.lineId).every((id) => /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(id)))
      .toBe(true)
    expect(new Set(beforeMove.map((line) => line.lineId)).size).toBe(2)

    // The first row cannot move up, but its button stays focusable (aria-disabled).
    const firstUp = screen.getByRole('button', { name: 'Kesim satırı 1 yukarı taşı' })
    expect(firstUp.getAttribute('aria-disabled')).toBe('true')
    const secondUp = screen.getByRole('button', { name: 'Kesim satırı 2 yukarı taşı' })
    secondUp.focus()
    await user.keyboard('{Enter}')
    // The moved row keeps focus on its own control, now first.
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Kesim satırı 1 yukarı taşı')
    expect((screen.getByLabelText('Kesim satırı 1 Sıra no') as HTMLInputElement).value).toBe('-7')
    expect((screen.getByLabelText('Kesim satırı 2 Sıra no') as HTMLInputElement).value).toBe('40')

    await waitFor(() => expect(previewBodies(recorded).at(-1)?.cutToLengthSpec?.lines.map((line) => line.sequence))
      .toEqual([-7, 40]))
    const authored = previewBodies(recorded).at(-1)!.cutToLengthSpec!
    expect(authored).toEqual({ lines: [beforeMove[1], beforeMove[0]] })

    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    expect(await screen.findByText(/Sonuç bilinmiyor\./)).toBeTruthy()
    const [first] = saveRequests(recorded)
    expect((first.body as CutBody).cutToLengthSpec).toEqual(authored)
    // While unknown the whole draft is frozen: nothing typed or moved can reach the frozen attempt.
    for (const field of [
      screen.getByLabelText('Kesim satırı 1 Sıra no'),
      screen.getByLabelText('Kesim satırı 2 Uzunluk (mm)'),
      screen.getByLabelText('Kesim satırı 2 Adet'),
      screen.getByRole('button', { name: 'Kesim satırı ekle' }),
      screen.getByRole('button', { name: 'Kesim satırı 2 yukarı taşı' }),
      screen.getByRole('button', { name: 'Kesim satırı 1 kaldır' }),
    ]) {
      expect((field as HTMLInputElement | HTMLButtonElement).matches(':disabled'), field.getAttribute('aria-label') ?? '').toBe(true)
    }

    await user.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    const reopen = await screen.findByRole('link', { name: 'Formu kapat ve planı yeniden aç' })
    const [, second] = saveRequests(recorded)
    expect(JSON.stringify(second.body)).toBe(JSON.stringify(first.body))
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])

    await user.click(reopen)
    const section = await screen.findByRole('region', { name: 'Boy kesme ayrıntısı' })
    expect(within(section).getAllByRole('rowheader').map((cell) => cell.textContent)).toEqual(['-7', '40'])
    expect(within(section).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['2500.5 mm', '2', '6000 mm', '3'])
  })

  it('a remounted form replays the frozen pending cut definition, not a new one', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({ failFirstSave: true })
    const { view } = renderAt('/uretim/plan/yeni')
    await fillCutPlan(user)
    await authorTwoRows(user)
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' })
    view.unmount()

    renderAt('/uretim/plan/yeni')
    expect(await screen.findByText('Önceki gönderimin sonucu bilinmiyor.')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await screen.findByText('Taslak plan kaydedildi')
    const [first, second] = saveRequests(recorded)
    expect(JSON.stringify(second.body)).toBe(JSON.stringify(first.body))
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect((second.body as CutBody).cutToLengthSpec?.lines.map((line) => [line.sequence, line.lengthMm, line.count]))
      .toEqual([[40, '6000.000', 3], [-7, '2500.5', 2]])
  })
})

describe('AT-061 / AT-062: the live plan preview', () => {
  it('shows the server preview of the typed content without a button, with every missing parameter named', async () => {
    const user = userEvent.setup()
    // The server's answer can be held, so "not current" is observed while the new content is still unanswered.
    let held: Promise<void> | null = null
    let release = () => {}
    const recorded = installPlanApi({
      preview: async (body) => {
        if (held !== null) await held
        return json(previewAnswer(body))
      },
    })
    renderAt('/uretim/plan/yeni')
    const panel = await screen.findByTestId('plan-preview')
    expect(within(panel).getByText('İş merkezi seçilmedi.')).toBeTruthy()

    await screen.findByRole('option', { name: 'SLT-01 — Dilme Hattı' })
    await user.selectOptions(screen.getByLabelText('İş merkezi'), WORK_CENTER)
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await user.click(within(await screen.findByRole('row', { name: /LBL-0007/ })).getByRole('button', { name: 'Ekle' }))
    await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '900.50')

    expect(await within(panel).findByText(/Önizleme güncel/)).toBeTruthy()
    const previews = requestsFor(recorded, '/manufacturing/plans/preview', 'POST')
    // The last request carries the final content (the debounce itself is timed in usePlanPreview.test.ts).
    expect(previews.at(-1)?.body).toEqual({
      workCenterId: WORK_CENTER,
      inputs: [{ parentRollId: ROLL_1, plannedKg: '900.50', expectedLabelVersion: '7' }],
      target: null,
      slittingSpec: null,
    })
    expect(previews.at(-1)?.headers['Idempotency-Key']).toBeUndefined()
    expect(within(panel).getByText('Toplam planlanan girdi').nextSibling?.textContent).toBe('900.5 kg')
    const missing = within(panel).getByRole('list', { name: 'Eksik parametreler' })
    expect(within(missing).getByText(/Yoğunluk \(kg\/m³\)/)).toBeTruthy()
    expect(within(missing).getByText(/Hat hızı \(m\/dk\)/)).toBeTruthy()
    expect(within(panel).getByText(/varsayılan değer kullanmaz/)).toBeTruthy()
    // The preview wrote nothing: no plan save happened.
    expect(saveRequests(recorded)).toHaveLength(0)

    // A change makes the shown answer "not current" at once, until the new content is answered.
    held = new Promise<void>((resolve) => { release = resolve })
    await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '1')
    expect(within(panel).getByText(/Güncel değil/)).toBeTruthy()
    expect(within(panel).queryByText(/Önizleme güncel/)).toBeNull()
    held = null
    release()
    expect(await within(panel).findByText(/Önizleme güncel/)).toBeTruthy()
    expect(within(panel).getByText('Toplam planlanan girdi').nextSibling?.textContent).toBe('900.501 kg')

    // The save stores the same content the preview answered.
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await screen.findByText('Taslak plan kaydedildi')
    const answered = requestsFor(recorded, '/manufacturing/plans/preview', 'POST').at(-1)?.body as { inputs: unknown }
    expect((saveRequests(recorded)[0].body as { inputs: unknown }).inputs).toEqual(answered.inputs)
  })

  it('a refused or failed preview says why, never shows the old answer as current, and never blocks the save', async () => {
    const user = userEvent.setup()
    let calls = 0
    const recorded = installPlanApi({
      preview: (body) => {
        calls += 1
        if (calls === 1) return json(previewAnswer(body))
        return problem(422, { code: 'manufacturing_mass_scale_exceeded', field: 'inputs.plannedKg' })
      },
    })
    renderAt('/uretim/plan/yeni')
    const panel = await screen.findByTestId('plan-preview')
    await screen.findByRole('option', { name: 'SLT-01 — Dilme Hattı' })
    await user.selectOptions(screen.getByLabelText('İş merkezi'), WORK_CENTER)
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await user.click(within(await screen.findByRole('row', { name: /LBL-0007/ })).getByRole('button', { name: 'Ekle' }))
    await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '10')
    expect(await within(panel).findByText(/Önizleme güncel/)).toBeTruthy()

    await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '.0001')
    expect(await within(panel).findByText(/manufacturing_mass_scale_exceeded/)).toBeTruthy()
    expect(within(panel).getByText(/Alan: inputs.plannedKg/)).toBeTruthy()
    expect(within(panel).queryByText(/Önizleme güncel/)).toBeNull()
    expect(within(panel).getByText(/Güncel değil/)).toBeTruthy()

    // The save is not gated by the preview: it is sent and the server decides.
    await user.clear(screen.getByLabelText('LBL-0007 planlanan kg'))
    await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '12')
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await screen.findByText('Taslak plan kaydedildi')
    expect(saveRequests(recorded)).toHaveLength(1)
  })
})

/**
 * M01.3 K2 #728/6180459736 — the pending create across the real session boundary. An actor change is the orchestrator's
 * SESSION_RESET; the reload is a new module graph and a new provider over the same sessionStorage. Nothing here sets an
 * epoch or a binding by hand.
 */
describe('pending plan create across the session boundary', () => {
  const OTHER_ACTOR = '88888888-8888-4888-8888-888888888888'
  const BLOCKED = /bekleyen bir plan kaydı okunamadı/

  async function actorChange(recorded: readonly RecordedRequest[], queryClient: import('@tanstack/react-query').QueryClient) {
    const bootstraps = requestsFor(recorded, '/shell/capabilities').length
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await waitFor(() => expect(requestsFor(recorded, '/shell/capabilities').length).toBe(bootstraps + 1))
  }

  function storedCreates(): Record<string, string | null> {
    const entries: Record<string, string | null> = {}
    for (const name of Object.keys(sessionStorage)) {
      if (name.startsWith('goymen.manufacturing.plan-create.')) entries[name] = sessionStorage.getItem(name)
    }
    return entries
  }

  it('A: a create left unknown in a session reached through a real reset is restored over F5 — same key, same bytes', async () => {
    const user = userEvent.setup()
    let actor = OTHER_ACTOR
    const recorded = installPlanApi({ failFirstSave: true, actor: () => actor })
    const first = renderAt('/uretim/plan/yeni')
    await screen.findByRole('option', { name: 'SLT-01 — Dilme Hattı' })

    actor = APPROVER
    await actorChange(recorded, first.queryClient)
    await fillPlan(user)
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' })

    const next = await reloadDocument(first.view)
    const second = next.renderAt('/uretim/plan/yeni')
    try {
      expect(await screen.findByText('Önceki gönderimin sonucu bilinmiyor.')).toBeTruthy()
      await user.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
      await screen.findByText('Taslak plan kaydedildi')
      const [lost, replay] = saveRequests(recorded)
      expect(saveRequests(recorded)).toHaveLength(2)
      expect(replay.headers['Idempotency-Key']).toBe(lost.headers['Idempotency-Key'])
      expect(JSON.stringify(replay.body)).toBe(JSON.stringify(lost.body))
    } finally {
      second.view.unmount()
    }
  })

  it("B: after a real reset back to the same actor, the earlier session's create is neither replayed nor replaced", async () => {
    const user = userEvent.setup()
    let actor = APPROVER
    const recorded = installPlanApi({ failFirstSave: true, actor: () => actor })
    const view = renderAt('/uretim/plan/yeni')
    await fillPlan(user)
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' })
    const stored = storedCreates()
    expect(Object.keys(stored)).toHaveLength(1)

    actor = OTHER_ACTOR
    await actorChange(recorded, view.queryClient)
    actor = APPROVER
    await actorChange(recorded, view.queryClient)

    expect(await screen.findByText(BLOCKED)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Taslak planı kaydet' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aynı anahtarla yeniden gönder' })).toBeNull()
    expect(saveRequests(recorded)).toHaveLength(1)
    expect(storedCreates()).toEqual(stored)
  })

  it('a create whose attempt cannot be stored for a reload is not sent', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi()
    renderAt('/uretim/plan/yeni')
    await fillPlan(user)
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    try {
      await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
      expect(await screen.findByText(/saklanamadı; istek gönderilmedi/)).toBeTruthy()
    } finally {
      setItem.mockRestore()
    }
    expect(saveRequests(recorded)).toHaveLength(0)
    expect(storedCreates()).toEqual({})
    // Nothing is frozen: the same form may be saved again once storage works.
    expect(screen.getByRole('button', { name: 'Taslak planı kaydet' }).hasAttribute('disabled')).toBe(false)
  })

  it('an unavailable store keeps the create form closed and sends nothing', async () => {
    const recorded = installPlanApi()
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    try {
      renderAt('/uretim/plan/yeni')
      expect(await screen.findByText(BLOCKED)).toBeTruthy()
      expect(screen.queryByRole('button', { name: 'Taslak planı kaydet' })).toBeNull()
    } finally {
      getItem.mockRestore()
    }
    expect(saveRequests(recorded)).toHaveLength(0)
  })
})
