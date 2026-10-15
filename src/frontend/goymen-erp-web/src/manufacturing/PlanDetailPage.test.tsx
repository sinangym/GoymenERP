import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY, orderDetailPath } from './capabilities'
import { pendingPlanLifecycleKey, reservePendingPlanLifecycle, type RevisePlanRequest } from './planCommandApi'
import {
  APPROVER,
  COMPANY_A,
  IDENTITY,
  COMPANY_B,
  CUT_LINE_1,
  CUT_LINE_2,
  PLAN,
  ROLL_1,
  ROLL_2,
  WORK_CENTER,
  cutToLengthSpec,
  installFetch,
  json,
  massBalance,
  order,
  plan,
  planCalculation,
  planRevision,
  planRevisions,
  problem,
  reloadDocument,
  renderAt,
  requestsFor,
  technicalSnapshot,
  type RecordedRequest,
} from './testHarness'

const LIFECYCLE_CAPABILITIES = [
  MANUFACTURING_CAPABILITY.planDetail,
  MANUFACTURING_CAPABILITY.planApprove,
  MANUFACTURING_CAPABILITY.planOrderCreate,
  MANUFACTURING_CAPABILITY.orderDetail,
]

afterEach(() => {
  vi.unstubAllGlobals()
})

/** The shell's session binding (M01.3 K2 #728/6180459736). */
const BINDING = '50000000-0000-4000-8000-000000000001'

/** The tab already holds this actor's session binding — the same authenticated session after an ordinary F5. */
function sameTabSession(): string {
  sessionStorage.setItem('goymen.shell.session-binding.v1', JSON.stringify({ v: 1, id: BINDING, actorId: APPROVER }))
  return BINDING
}

/** The plan read itself; MFG-PLAN-EDIT's history read (`/plans/{id}/revisions`) is a separate request. */
function planReadRequests(recorded: readonly RecordedRequest[]) {
  return requestsFor(recorded, `/manufacturing/plans/${PLAN}`, 'GET').filter((request) => !request.url.endsWith('/revisions'))
}

describe('plan approval and order creation', () => {
  it('approves the read version, re-reads the plan, creates one order and opens its fresh detail', async () => {
    const user = userEvent.setup()
    let storedPlan = plan()
    let storedOrder: ReturnType<typeof order> | null = null
    const recorded = installFetch({
      capabilities: LIFECYCLE_CAPABILITIES,
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/approval`) && init?.method === 'POST') {
          storedPlan = plan({
            status: 'Approved',
            version: 2,
            approvedByUserId: APPROVER,
            approvedAtUtc: '2026-09-24T09:00:00Z',
          })
          return json(storedPlan)
        }
        if (url.endsWith('/manufacturing/orders') && init?.method === 'POST') {
          const request = JSON.parse(init.body as string) as { orderId: string; planId: string }
          storedOrder = order({ orderId: request.orderId, planId: request.planId })
          return json(storedOrder, 201, { Location: `/api/v1/manufacturing/orders/${request.orderId}` })
        }
        if (storedOrder !== null && url.endsWith(`/manufacturing/orders/${storedOrder.orderId}`)) {
          return json(storedOrder)
        }
        if (storedOrder !== null && url.endsWith(`/manufacturing/orders/${storedOrder.orderId}/mass-balance`)) {
          return json(massBalance({ orderId: storedOrder.orderId }))
        }
        return undefined
      },
    })
    const { router } = renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı onayla' }))
    expect(await screen.findByText('Plan onaylandı ve güncel kayıt yeniden okundu.')).toBeTruthy()
    expect(screen.getByText('Onaylı')).toBeTruthy()

    const approval = requestsFor(recorded, `/manufacturing/plans/${PLAN}/approval`, 'POST')
    expect(approval).toHaveLength(1)
    expect(approval[0].headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect(approval[0].headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(approval[0].body).toEqual({ expectedVersion: 1 })
    expect(planReadRequests(recorded)).toHaveLength(2)

    await user.click(screen.getByRole('button', { name: 'Üretim emri oluştur' }))
    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/uretim\/emir\/[0-9a-f-]{36}$/i))

    const create = requestsFor(recorded, '/manufacturing/orders', 'POST')
    expect(create).toHaveLength(1)
    expect(create[0].headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect(create[0].headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(create[0].body).toMatchObject({ planId: PLAN })
    expect(router.state.location.pathname).toBe(orderDetailPath((create[0].body as { orderId: string }).orderId))
  })

  it('re-sends an unknown approval with the same key, body and read version', async () => {
    const user = userEvent.setup()
    let approvals = 0
    let storedPlan = plan()
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planApprove],
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') return json(storedPlan)
        if (url.endsWith(`/manufacturing/plans/${PLAN}/approval`) && init?.method === 'POST') {
          approvals += 1
          if (approvals === 1) throw new TypeError('connection lost')
          storedPlan = plan({
            status: 'Approved',
            version: 2,
            approvedByUserId: APPROVER,
            approvedAtUtc: '2026-09-24T09:00:00Z',
          })
          return json(storedPlan, 200, {
            'Idempotency-Replayed': 'true',
            'Idempotency-Original-Correlation-Id': 'corr-1',
          })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı onayla' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))
    expect(await screen.findByText('Plan onaylandı ve güncel kayıt yeniden okundu.')).toBeTruthy()

    const [first, second] = requestsFor(recorded, `/manufacturing/plans/${PLAN}/approval`, 'POST')
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('re-sends an unknown order create with the same order id and key', async () => {
    const user = userEvent.setup()
    let creates = 0
    const approved = plan({
      status: 'Approved',
      version: 2,
      approvedByUserId: APPROVER,
      approvedAtUtc: '2026-09-24T09:00:00Z',
    })
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planOrderCreate],
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`)) return json(approved)
        if (url.endsWith('/manufacturing/orders') && init?.method === 'POST') {
          creates += 1
          if (creates === 1) throw new TypeError('connection lost')
          const request = JSON.parse(init.body as string) as { orderId: string; planId: string }
          return json(order({ orderId: request.orderId, planId: request.planId }), 201, {
            Location: `/api/v1/manufacturing/orders/${request.orderId}`,
            'Idempotency-Replayed': 'true',
            'Idempotency-Original-Correlation-Id': 'corr-2',
          })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Üretim emri oluştur' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))
    expect(await screen.findByText(/Önceki emir oluşturma sonucu yeniden gösterildi/)).toBeTruthy()

    const [first, second] = requestsFor(recorded, '/manufacturing/orders', 'POST')
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('keeps stale plan actions closed when the conflict refresh cannot be read', async () => {
    const user = userEvent.setup()
    let planReads = 0
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planApprove],
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') {
          planReads += 1
          if (planReads === 1) return json(plan())
          throw new TypeError('connection lost')
        }
        if (url.endsWith(`/manufacturing/plans/${PLAN}/approval`) && init?.method === 'POST') {
          return problem(409, { code: 'manufacturing_plan_version_conflict' })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı onayla' }))

    expect(await screen.findByText(
      'Plan onayı uygulanmadı (manufacturing_plan_version_conflict). Güncel plan henüz yeniden okunamadı.',
    )).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Güncel planı yeniden oku' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/approval`, 'POST')).toHaveLength(1)
  })

  it('unlocks a conflict only after the refresh returns the current company and plan', async () => {
    const user = userEvent.setup()
    let planReads = 0
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planApprove],
      respond: (url, init) => {
        if (url.endsWith(`/manufacturing/plans/${PLAN}`) && init?.method !== 'POST') {
          planReads += 1
          if (planReads === 1) return json(plan())
          if (planReads === 2) return json(plan({ companyId: COMPANY_B, version: 2 }))
          return json(plan({ version: 2 }))
        }
        if (url.endsWith(`/manufacturing/plans/${PLAN}/approval`) && init?.method === 'POST') {
          return problem(409, { code: 'manufacturing_plan_version_conflict' })
        }
        return undefined
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await user.click(await screen.findByRole('button', { name: 'Planı onayla' }))
    expect(await screen.findByRole('button', { name: 'Güncel planı yeniden oku' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Güncel planı yeniden oku' }))

    expect(await screen.findByText(
      'Plan onayı uygulanmadı (manufacturing_plan_version_conflict). Güncel plan yeniden okundu.',
    )).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Planı onayla' })).toBeTruthy()
    expect(planReadRequests(recorded)).toHaveLength(3)
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/approval`, 'POST')).toHaveLength(1)
  })

  it('suppresses a recalled unknown approval after its child capability is revoked', async () => {
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url) => url.endsWith(`/manufacturing/plans/${PLAN}`) ? json(plan()) : undefined,
    })
    reservePendingPlanLifecycle(pendingPlanLifecycleKey(sameTabSession(), APPROVER, COMPANY_A, PLAN), {
      actorId: APPROVER,
      companyId: COMPANY_A,
      planId: PLAN,
      snapshot: {
        kind: 'approve',
        idempotencyKey: '10000000-0000-4000-8000-000000000001',
        expectedVersion: 1,
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    expect(screen.queryByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(requestsFor(recorded, `/manufacturing/plans/${PLAN}/approval`, 'POST')).toHaveLength(0)
  })

  it('suppresses a recalled unknown order create after its child capability is revoked', async () => {
    const approved = plan({
      status: 'Approved',
      version: 2,
      approvedByUserId: APPROVER,
      approvedAtUtc: '2026-09-24T09:00:00Z',
    })
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url) => url.endsWith(`/manufacturing/plans/${PLAN}`) ? json(approved) : undefined,
    })
    reservePendingPlanLifecycle(pendingPlanLifecycleKey(sameTabSession(), APPROVER, COMPANY_A, PLAN), {
      actorId: APPROVER,
      companyId: COMPANY_A,
      planId: PLAN,
      snapshot: {
        kind: 'create-order',
        idempotencyKey: '10000000-0000-4000-8000-000000000002',
        orderId: '10000000-0000-4000-8000-000000000003',
      },
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    expect(screen.queryByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Üretim emri oluştur' })).toBeNull()
    expect(requestsFor(recorded, '/manufacturing/orders', 'POST')).toHaveLength(0)
  })

  it('draws neither command when the server publishes only plan read', async () => {
    const recorded = installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url) => url.endsWith(`/manufacturing/plans/${PLAN}`) ? json(plan()) : undefined,
    })
    renderAt(`/uretim/plan/${PLAN}`)

    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Üretim emri oluştur' })).toBeNull()
    expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(0)
    expect(requestsFor(recorded, '/manufacturing/orders', 'POST')).toHaveLength(0)
  })
})

describe('plan detail navigation', () => {
  function stubPlan(capabilities: readonly string[]) {
    return installFetch({
      capabilities,
      respond: (url, init) =>
        url.endsWith(`/manufacturing/plans/${PLAN}`) && (init?.method ?? 'GET') === 'GET' ? json(plan()) : undefined,
    })
  }

  it('links back to the plan list only for a holder of the plan-list capability', async () => {
    const user = userEvent.setup()
    stubPlan([MANUFACTURING_CAPABILITY.planDetail, MANUFACTURING_CAPABILITY.planList])
    const { router } = renderAt(`/uretim/plan/${PLAN}`)

    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    const back = screen.getAllByRole('link', { name: '← Üretim planları' })
    expect(back).toHaveLength(1)
    expect(back[0].getAttribute('href')).toBe('/uretim/planlar')
    // The order-list link beside it is unchanged.
    expect(screen.getByRole('link', { name: '← Üretim emirleri' }).getAttribute('href')).toBe('/uretim/emirler')

    await user.click(back[0])
    await waitFor(() => expect(router.state.location.pathname).toBe('/uretim/planlar'))
  })

  it('draws no plan-list back link when the plan-list capability is absent', async () => {
    stubPlan([MANUFACTURING_CAPABILITY.planDetail])
    renderAt(`/uretim/plan/${PLAN}`)

    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    expect(screen.queryByRole('link', { name: '← Üretim planları' })).toBeNull()
    expect(screen.getByRole('link', { name: '← Üretim emirleri' })).toBeTruthy()
  })
})

describe('stored cut-to-length lines on the plan detail', () => {
  /** Browser-selector test data (#66/6173714948): signed, non-monotonic sequences in source order. */
  const LINES = [
    { lineId: CUT_LINE_2, sequence: 40, lengthMm: '6000', count: 3 },
    { lineId: CUT_LINE_1, sequence: -7, lengthMm: '2500.5', count: 2 },
  ]

  function stubPlan(stored: ReturnType<typeof plan>, capabilities: readonly string[] = LIFECYCLE_CAPABILITIES) {
    return installFetch({
      capabilities,
      respond: (url, init) =>
        url.endsWith(`/manufacturing/plans/${PLAN}`) && (init?.method ?? 'GET') === 'GET' ? json(stored) : undefined,
    })
  }

  it('shows the ordered sequence, length and count read-only before the approval is offered', async () => {
    stubPlan(plan({ process: 'CutToLength', cutToLengthSpec: cutToLengthSpec({ lines: LINES }) }))
    renderAt(`/uretim/plan/${PLAN}`)

    const section = await screen.findByRole('region', { name: 'Boy kesme ayrıntısı' })
    const rows = within(within(section).getByRole('table')).getAllByRole('row').slice(1)
    // Source order, not sorted by sequence; the signed sequence is shown as stored.
    expect(rows.map((row) => [
      within(row).getByRole('rowheader').textContent,
      ...within(row).getAllByRole('cell').map((cell) => cell.textContent),
    ])).toEqual([['40', '6000 mm', '3'], ['-7', '2500.5 mm', '2']])
    expect(within(section).getAllByRole('columnheader').map((header) => header.textContent))
      .toEqual(['Sıra', 'Uzunluk (mm)', 'Adet'])
    // Read-only and business-first: no control, and the technical line id is not the visible content.
    expect(within(section).queryAllByRole('textbox')).toHaveLength(0)
    expect(within(section).queryAllByRole('button')).toHaveLength(0)
    expect(within(section).queryByText(CUT_LINE_1)).toBeNull()
    expect(within(section).queryByText(CUT_LINE_2)).toBeNull()
    // The approval is still the existing, unchanged command beside it.
    expect(screen.getByRole('button', { name: 'Planı onayla' })).toBeTruthy()
  })

  it('keeps the lines visible on an approved plan and for a read-only viewer', async () => {
    stubPlan(
      plan({
        process: 'CutToLength',
        status: 'Approved',
        version: 2,
        approvedByUserId: APPROVER,
        approvedAtUtc: '2026-09-24T09:00:00Z',
        cutToLengthSpec: cutToLengthSpec({ lines: LINES }),
      }),
      [MANUFACTURING_CAPABILITY.planDetail],
    )
    renderAt(`/uretim/plan/${PLAN}`)

    const section = await screen.findByRole('region', { name: 'Boy kesme ayrıntısı' })
    expect(within(section).getAllByRole('rowheader').map((cell) => cell.textContent)).toEqual(['40', '-7'])
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
  })

  it.each([
    ['an explicit null', plan()],
    ['the legacy omitted member', (({ cutToLengthSpec: _omitted, ...legacy }) => legacy)(plan()) as ReturnType<typeof plan>],
  ])('draws no cut-line section for %s', async (_case, stored) => {
    stubPlan(stored)
    renderAt(`/uretim/plan/${PLAN}`)
    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    expect(screen.queryByRole('region', { name: 'Boy kesme ayrıntısı' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Planı onayla' })).toBeTruthy()
  })
})

describe('stored plan technical projection', () => {
  it('shows the captured source and the same backend calculation renderer after reopen', async () => {
    installFetch({
      capabilities: [MANUFACTURING_CAPABILITY.planDetail],
      respond: (url) => url.endsWith(`/manufacturing/plans/${PLAN}`) ? json(plan({
        inputs: [{ parentRollId: ROLL_1, plannedKg: '1000.125', technicalSnapshot: technicalSnapshot() }],
        calculation: planCalculation({
          plannedRollCount: 1,
          plannedInputKg: '1000.125',
          rolls: [{ parentRollId: ROLL_1, plannedKg: '1000.125', answers: [] }],
        }),
      })) : undefined,
    })

    renderAt(`/uretim/plan/${PLAN}`)

    expect(await screen.findByRole('heading', { name: 'Kayıtlı plan hesaplaması' })).toBeTruthy()
    expect(screen.getByText(/COIL · family-r1/)).toBeTruthy()
    expect(screen.getByText('2 mm × 1500 mm')).toBeTruthy()
    expect(screen.getByText('GDC-PLAN-CALC/1')).toBeTruthy()
  })
})

/**
 * M01.3 K2 #727/6178721152: the first revise (v1 → v2) was processed and only its answer was lost. After the reload the
 * module memory is empty, the fresh GET already shows version 2 and the expected-version fence would accept a NEW,
 * newly keyed v2 → v3 revise. So the stored slot decides: unreadable keeps every command of this context closed. The
 * reload is the seeded state here (empty memory, stored slot, server at N+1); the real F5 is #726's browser journey.
 */
describe('reload safety of the pending plan command slot', () => {
  const STORED_REVISE_PREFIX = 'goymen.manufacturing.plan-revise.v2:'
  const OTHER_PLAN = 'd0000000-0000-4000-8000-000000000098'
  const OTHER_LINE = 'd0000000-0000-4000-8000-000000000009'
  const FIRST_KEY = '40000000-0000-4000-8000-000000000001'
  const BLOCKED = /bekleyen bir komut kaydı okunamadı/
  const EDITOR_CAPABILITIES = [
    MANUFACTURING_CAPABILITY.planDetail,
    MANUFACTURING_CAPABILITY.planEdit,
    MANUFACTURING_CAPABILITY.planApprove,
    MANUFACTURING_CAPABILITY.planOrderCreate,
  ]
  /** The request the processed first revise carried: v1 → v2. */
  const FIRST_REQUEST: RevisePlanRequest = {
    expectedVersion: 1,
    workCenterId: WORK_CENTER,
    inputs: [
      { parentRollId: ROLL_1, plannedKg: '800', expectedLabelVersion: '7', commercialContext: null },
      { parentRollId: ROLL_2, plannedKg: '500.5', expectedLabelVersion: '3', commercialContext: null },
    ],
    target: null,
    reason: 'İlk düzenleme işlendi',
  }

  function storedAttempt(overrides: Record<string, unknown> = {}) {
    return {
      actorId: APPROVER,
      companyId: COMPANY_A,
      planId: PLAN,
      snapshot: { kind: 'revise', idempotencyKey: FIRST_KEY, request: FIRST_REQUEST },
      sessionBinding: BINDING,
      ...overrides,
    }
  }

  function slot(planId = PLAN) {
    return STORED_REVISE_PREFIX + JSON.stringify([APPROVER, COMPANY_A, planId])
  }

  /** The server after the processed first revise: the plan at version 2 and its history [0, 1]. */
  function movedServer(
    revise?: () => Response,
    { actor = () => APPROVER, rejectReads = () => false, seedSession = true }: {
      readonly actor?: () => string
      readonly rejectReads?: () => boolean
      readonly seedSession?: boolean
    } = {},
  ) {
    const moved = (planId: string) => plan({ planId, version: 2, inputs: [plan().inputs[0], plan().inputs[1]] })
    const history = (planId: string) => planRevisions([
      planRevision(),
      planRevision({ revision: 1, planVersion: 2, reason: 'İlk düzenleme işlendi', revisedBy: APPROVER }),
    ], planId)
    const recorded = installFetch({
      capabilities: EDITOR_CAPABILITIES,
      identity: () => ({ ...IDENTITY, userId: actor() }),
      respond: (url, init) => {
        if (rejectReads() && url.includes('/manufacturing/plans/') && init?.method !== 'POST') return problem(401)
        for (const planId of [PLAN, OTHER_PLAN]) {
          if (url.endsWith(`/manufacturing/plans/${planId}`) && init?.method !== 'POST') return json(moved(planId))
          if (url.endsWith(`/manufacturing/plans/${planId}/revisions`)) return json(history(planId))
        }
        if (url.endsWith('/organization/work-centers')) {
          return json([
            { id: WORK_CENTER, locationId: WORK_CENTER, code: 'DL-1', name: 'Dilme 1', kind: 'Slitting', isActive: true, canOpenLocationDetail: false },
            { id: OTHER_LINE, locationId: WORK_CENTER, code: 'DL-2', name: 'Dilme 2', kind: 'Slitting', isActive: true, canOpenLocationDetail: false },
          ])
        }
        if (url.endsWith('/manufacturing/plans/preview') && init?.method === 'POST') {
          const body = JSON.parse(init.body as string) as { workCenterId: string; inputs: { parentRollId: string; plannedKg: string }[] }
          return json(planCalculation({
            workCenterId: body.workCenterId,
            plannedRollCount: body.inputs.length,
            rolls: body.inputs.map((input) => ({ parentRollId: input.parentRollId, plannedKg: input.plannedKg, answers: [] })),
          }))
        }
        if (url.includes('/revision') && init?.method === 'POST') return revise?.()
        return undefined
      },
    })
    if (seedSession) sameTabSession()
    return recorded
  }

  function commandPosts(recorded: readonly RecordedRequest[]) {
    return recorded.filter((request) => request.method === 'POST' && !request.url.endsWith('/plans/preview'))
  }

  /** The fresh GET and history are on screen: the plan at version 2 with its processed first revision. */
  async function readBack() {
    await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })
    await screen.findAllByText(/İlk düzenleme işlendi/)
    expect(within(screen.getByText('Sürüm').parentElement!).getByText('2')).toBeTruthy()
  }

  it.each([
    ['malformed', () => '{not json'],
    ['foreign to this company', () => JSON.stringify(storedAttempt({ companyId: COMPANY_B }))],
    ['contract-violating', () => JSON.stringify(storedAttempt({ snapshot: { kind: 'revise', idempotencyKey: FIRST_KEY, request: { ...FIRST_REQUEST, expectedVersion: 0 } } }))],
  ])('a %s pending record keeps edit, approval and order closed even though the fresh GET shows N+1', async (_case, value) => {
    const recorded = movedServer()
    const raw = value()
    sessionStorage.setItem(slot(), raw)
    renderAt(`/uretim/plan/${PLAN}`)

    await readBack()
    expect(screen.getByRole('alert').textContent).toMatch(BLOCKED)
    // No new lifecycle can be started — not even a "current" v2 → v3 revise — and nothing is retried.
    expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Taslak planı düzenle' })).toBeNull()
    expect(commandPosts(recorded)).toHaveLength(0)
    // Read and back navigation stay; the evidence is neither replayed nor deleted.
    expect(screen.getByRole('link', { name: '← Üretim emirleri' })).toBeTruthy()
    expect(sessionStorage.getItem(slot())).toBe(raw)
  })

  it('an unavailable store is unreadable, not empty: the plan reads and nothing can be sent', async () => {
    const recorded = movedServer()
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    try {
      renderAt(`/uretim/plan/${PLAN}`)
      await readBack()
      expect(screen.getByRole('alert').textContent).toMatch(BLOCKED)
      expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
      expect(commandPosts(recorded)).toHaveLength(0)
    } finally {
      getItem.mockRestore()
    }
  })

  it('a valid stored pending revise offers only its exact retry: same key, same frozen body', async () => {
    const user = userEvent.setup()
    const recorded = movedServer(() => { throw new TypeError('still offline') })
    const raw = JSON.stringify(storedAttempt())
    sessionStorage.setItem(slot(), raw)
    renderAt(`/uretim/plan/${PLAN}`)

    await readBack()
    expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))
    await waitFor(() => expect(commandPosts(recorded)).toHaveLength(1))

    const [sent] = commandPosts(recorded)
    expect(sent.url).toMatch(new RegExp(`/manufacturing/plans/${PLAN}/revision$`))
    expect(sent.headers['Idempotency-Key']).toBe(FIRST_KEY)
    // The stored v1 body, not one rebuilt from the v2 just read.
    expect(sent.body).toEqual(FIRST_REQUEST)
    expect(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' })).toBeTruthy()
    expect(sessionStorage.getItem(slot())).toBe(raw)
  })

  it('an unrelated plan of the same actor and company stays usable', async () => {
    movedServer()
    sessionStorage.setItem(slot(), '{not json')
    renderAt(`/uretim/plan/${OTHER_PLAN}`)

    await readBack()
    expect(screen.queryByText(BLOCKED)).toBeNull()
    expect(screen.getByRole('button', { name: 'Planı düzenle' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Planı onayla' })).toBeTruthy()
  })

  it('a revise whose attempt cannot be stored for a reload is not sent', async () => {
    const user = userEvent.setup()
    const recorded = movedServer(() => json(plan({ version: 3 })))
    renderAt(`/uretim/plan/${PLAN}`)
    await readBack()
    await user.click(screen.getByRole('button', { name: 'Planı düzenle' }))
    await screen.findByRole('heading', { name: 'Taslak planı düzenle' })
    await screen.findByRole('option', { name: /DL-2/ })
    const kg = screen.getByRole('textbox', { name: `${ROLL_1} planlanan kg` })
    await user.clear(kg)
    await user.type(kg, '700')
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'İkinci düzenleme')

    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    try {
      await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))
      expect(await screen.findByText(/saklanamadı; istek gönderilmedi/)).toBeTruthy()
    } finally {
      setItem.mockRestore()
    }
    expect(commandPosts(recorded)).toHaveLength(0)
    expect(sessionStorage.getItem(slot())).toBeNull()
  })

  /*
   * M01.3 K2 #728/6180459736 — the session boundary itself, through the REAL shell paths: an actor change dispatches
   * the orchestrator's SESSION_RESET, a business 401 the reducer's invalidation, and the reload is a new module graph
   * and a new provider over the same sessionStorage. Nothing here sets an epoch or a binding by hand.
   */
  const OTHER_ACTOR = '88888888-8888-4888-8888-888888888888'

  async function reviseUntilUnknown(user: ReturnType<typeof userEvent.setup>) {
    await user.click(await screen.findByRole('button', { name: 'Planı düzenle' }))
    await screen.findByRole('heading', { name: 'Taslak planı düzenle' })
    await screen.findByRole('option', { name: /DL-2/ })
    const kg = screen.getByRole('textbox', { name: `${ROLL_1} planlanan kg` })
    await user.clear(kg)
    await user.type(kg, '700')
    await user.type(screen.getByRole('textbox', { name: 'Düzenleme gerekçesi' }), 'İkinci düzenleme')
    await user.click(screen.getByRole('button', { name: 'Değişikliği kaydet' }))
    await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' })
  }

  /** The identity re-read the orchestrator acts on; resolves once the next session has bootstrapped. */
  async function actorChange(
    recorded: readonly RecordedRequest[],
    queryClient: import('@tanstack/react-query').QueryClient,
  ) {
    const bootstraps = requestsFor(recorded, '/shell/capabilities').length
    await queryClient.refetchQueries({ queryKey: ['shell', 'identity'] })
    await waitFor(() => expect(requestsFor(recorded, '/shell/capabilities').length).toBe(bootstraps + 1))
  }

  function storedRevises(): Record<string, string | null> {
    const entries: Record<string, string | null> = {}
    for (const name of Object.keys(sessionStorage)) {
      if (name.startsWith('goymen.manufacturing.plan-revise.')) entries[name] = sessionStorage.getItem(name)
    }
    return entries
  }

  it('A: a revise left unknown in a session reached through a real reset is restored over F5 — same key, same bytes, no N+2', async () => {
    const user = userEvent.setup()
    let actor = OTHER_ACTOR
    const recorded = movedServer(() => { throw new TypeError('answer lost') }, { actor: () => actor, seedSession: false })
    const first = renderAt(`/uretim/plan/${PLAN}`)
    await readBack()

    // A real in-app session boundary: the epoch is no longer 0 when the approver's session starts.
    actor = APPROVER
    await actorChange(recorded, first.queryClient)
    await reviseUntilUnknown(user)
    const [lost] = commandPosts(recorded)

    const next = await reloadDocument(first.view)
    const second = next.renderAt(`/uretim/plan/${PLAN}`)
    try {
      await readBack()
      expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
      await user.click(await screen.findByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' }))
      await waitFor(() => expect(commandPosts(recorded)).toHaveLength(2))
      const [, replay] = commandPosts(recorded)
      expect(replay.url).toBe(lost.url)
      expect(replay.headers['Idempotency-Key']).toBe(lost.headers['Idempotency-Key'])
      expect(JSON.stringify(replay.body)).toBe(JSON.stringify(lost.body))
    } finally {
      second.view.unmount()
    }
  })

  it("B: after a real reset back to the same actor, the earlier session's revise is neither replayed nor replaced", async () => {
    const user = userEvent.setup()
    let actor = APPROVER
    const recorded = movedServer(() => { throw new TypeError('answer lost') }, { actor: () => actor, seedSession: false })
    const view = renderAt(`/uretim/plan/${PLAN}`)
    await readBack()
    await reviseUntilUnknown(user)
    const stored = storedRevises()
    expect(Object.keys(stored)).toHaveLength(1)

    actor = OTHER_ACTOR
    await actorChange(recorded, view.queryClient)
    actor = APPROVER
    await actorChange(recorded, view.queryClient)

    expect(await screen.findByText(BLOCKED)).toBeTruthy()
    await readBack()
    expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' })).toBeNull()
    expect(commandPosts(recorded)).toHaveLength(1)
    expect(storedRevises()).toEqual(stored)
  })

  it('B: after the server ended the session, the same actor re-entering over F5 gets no replay and no new key', async () => {
    const user = userEvent.setup()
    let reject = false
    const recorded = movedServer(() => { throw new TypeError('answer lost') }, { rejectReads: () => reject, seedSession: false })
    const first = renderAt(`/uretim/plan/${PLAN}`)
    await readBack()
    await reviseUntilUnknown(user)
    const stored = storedRevises()

    reject = true
    await first.queryClient.invalidateQueries()
    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()

    reject = false
    const next = await reloadDocument(first.view)
    const second = next.renderAt(`/uretim/plan/${PLAN}`)
    try {
      expect(await screen.findByText(BLOCKED)).toBeTruthy()
      await readBack()
      expect(screen.queryByRole('button', { name: 'Planı düzenle' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Planı onayla' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Aynı istek ve anahtarla yeniden gönder' })).toBeNull()
      expect(commandPosts(recorded)).toHaveLength(1)
      expect(storedRevises()).toEqual(stored)
    } finally {
      second.view.unmount()
    }
  })
})
