/**
 * Test-only harness for the Manufacturing order screen.
 *
 * Mounts the REAL shell machinery — `ShellProvider`, `AppShell` bootstrap,
 * `CapabilityRoute` entry gate, the auth boundary — around this lane's own route table
 * (the shell owner has not mounted it yet; that is the handoff), and stubs `fetch` with
 * synthetic fixtures. No database, no real host, no secret.
 *
 * WHAT THIS HARNESS IS NOT: evidence of the real API, real SQL or browser acceptance.
 * Every assertion built on it is a component/DOM claim and nothing more.
 *
 * Requests are RECORDED with method, headers and parsed body, because "did not show it"
 * and "did not send it" are different guarantees.
 */

import { vi } from 'vitest'
import { render } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { StateBlock } from '../shell/StateBlock'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { manufacturingRoutes } from './manufacturingRoutes'
import { clearPendingFacts } from './pendingFactStore'
import { clearPendingPlanLifecycles } from './planCommandApi'

export const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
export const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
export const ORDER = 'd0000000-0000-4000-8000-000000000001'
export const PLAN = 'd0000000-0000-4000-8000-000000000002'
export const WORK_CENTER = 'd0000000-0000-4000-8000-000000000003'
export const APPROVER = '99999999-9999-9999-9999-999999999999'
export const ROLL_1 = 'e0000000-0000-4000-8000-000000000001'
export const ROLL_2 = 'e0000000-0000-4000-8000-000000000002'

export const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'SENTETIK Kullanıcı',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
}

export const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: COMPANY_B, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

/** The order's reader: the balances, no form. */
export const READER_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderDetail]
/** Reader + the fact forms. */
export const RECORDER_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderDetail, MANUFACTURING_CAPABILITY.orderFacts]
/** The list only: whoever opens the list may also open one order (the same read grant). */
export const LIST_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderList, MANUFACTURING_CAPABILITY.orderDetail]
/** MFG-PLAN-LIST: the plan list and the plan detail it opens (the same plan read grant). */
export const PLAN_LIST_CAPABILITIES = [MANUFACTURING_CAPABILITY.planList, MANUFACTURING_CAPABILITY.planDetail]
/** Reader + the close control, without the fact forms: closing is its own authority. */
export const CLOSER_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderDetail, MANUFACTURING_CAPABILITY.orderClose]

const ZERO_OUTPUTS = { GoodProduct: '0', SecondQuality: '0', UsableRemnant: '0', Scrap: '0', ProcessLoss: '0' }

export function roll(parentRollId: string, overrides: Record<string, unknown> = {}) {
  return {
    parentRollId,
    plannedKg: '1000',
    issuedKg: '0',
    returnedKg: '0',
    netIssuedKg: '0',
    outputsKg: ZERO_OUTPUTS,
    workInProgressKg: '0',
    unexplainedKg: '0',
    isFullyAccounted: true,
    ...overrides,
  }
}

/** A synthetic `PlannedInputViewV1`, in the server's JSON shape. */
export function plannedInput(parentRollId: string, plannedKg = '1000.125') {
  return { parentRollId, plannedKg, technicalSnapshot: null }
}

/** A synthetic captured Product/Settings snapshot. It is component data, never real fixture evidence. */
export function technicalSnapshot(overrides: Record<string, unknown> = {}) {
  return {
    productId: 'f0000000-0000-4000-8000-000000000001',
    productVersion: '2',
    family: { familyId: 'f0000000-0000-4000-8000-000000000002', familyCode: 'COIL', sourceRevision: 'family-r1' },
    template: {
      templateId: 'f0000000-0000-4000-8000-000000000003',
      templateVersion: '3',
      templateCode: 'COIL-SHEET',
      sourceRevision: 'template-r3',
    },
    thicknessMm: '2',
    widthMm: '1500',
    unitKg: {
      companyId: COMPANY_A,
      unitId: 'f0000000-0000-4000-8000-000000000004',
      unitCode: 'kg',
      profileId: 'f0000000-0000-4000-8000-000000000005',
      profileVersion: '1',
    },
    effectiveOn: '2026-09-30',
    ...overrides,
  }
}

/** A synthetic stored calculation projection aligned with the default plan fixture. */
export function planCalculation(overrides: Record<string, unknown> = {}) {
  return {
    calculatorVersion: 'GDC-PLAN-CALC/1',
    workCenterId: WORK_CENTER,
    process: 'Slitting',
    plannedRollCount: 2,
    plannedInputKg: '1500.625',
    target: null,
    answers: [],
    rolls: [
      { parentRollId: ROLL_1, plannedKg: '1000.125', answers: [] },
      { parentRollId: ROLL_2, plannedKg: '500.5', answers: [] },
    ],
    missingParameters: ['densityKgPerCubicMetre'],
    hasRefusal: false,
    ...overrides,
  }
}

/** A synthetic `PlanViewV1`, in the server's JSON shape. */
export function plan(overrides: Record<string, unknown> = {}) {
  return {
    planId: PLAN,
    companyId: COMPANY_A,
    workCenterId: WORK_CENTER,
    process: 'Slitting',
    status: 'Draft',
    version: 1,
    inputs: [plannedInput(ROLL_1), plannedInput(ROLL_2, '500.5')],
    target: null,
    approvedByUserId: null,
    approvedAtUtc: null,
    calculation: null,
    slittingSpec: null,
    // M01.2: the current server always emits the member, explicit null when the plan carries no cut lines.
    cutToLengthSpec: null,
    ...overrides,
  }
}

export const CUT_LINE_1 = 'c0000000-0000-4000-8000-000000000001'
export const CUT_LINE_2 = 'c0000000-0000-4000-8000-000000000002'

/**
 * A synthetic stored `CutToLengthSpecViewV1`: ordered as the client sent it, the sequence explicit, signed and not
 * contiguous, so a client that renumbers or re-sorts it shows up.
 */
export function cutToLengthSpec(overrides: Record<string, unknown> = {}) {
  return {
    lines: [
      { lineId: CUT_LINE_2, sequence: 20, lengthMm: '1250.5', count: 3 },
      { lineId: CUT_LINE_1, sequence: -1, lengthMm: '0.001', count: 2147483647 },
    ],
    ...overrides,
  }
}

/** A synthetic `PlanRevisionInputViewV1` (MFG-PLAN-EDIT), in the server's current JSON shape. */
export function revisionInput(
  parentRollId: string,
  plannedKg = '1000.125',
  expectedLabelVersion: string | null = '7',
  commercialContext: unknown = null,
  capturedTechnicalSnapshot: unknown = null,
) {
  return {
    parentRollId,
    plannedKg,
    expectedLabelVersion,
    commercialContext,
    technicalSnapshot: capturedTechnicalSnapshot,
  }
}

/** A synthetic `PlanRevisionViewV1`: revision 0 by default — the plan as created, matching {@link plan}. */
export function planRevision(overrides: Record<string, unknown> = {}) {
  return {
    revision: 0,
    planVersion: 1,
    workCenterId: WORK_CENTER,
    inputs: [revisionInput(ROLL_1), revisionInput(ROLL_2, '500.5', '3')],
    target: null,
    reason: null,
    revisedBy: null,
    recordedAtUtc: '2026-09-27T08:00:00Z',
    cutToLengthSpec: null,
    ...overrides,
  }
}

/** A synthetic `PlanRevisionListViewV1`, oldest first. */
export function planRevisions(revisions: readonly unknown[] = [planRevision()], planId = PLAN) {
  return { planId, revisions }
}

/** A synthetic `OrderViewV1`, in the server's JSON shape. */
export function order(overrides: Record<string, unknown> = {}) {
  return {
    orderId: ORDER,
    companyId: COMPANY_A,
    planId: PLAN,
    workCenterId: WORK_CENTER,
    process: 'Slitting',
    status: 'Open',
    version: 1,
    target: null,
    remainingTarget: { basis: null, targetKg: null, achievedKg: null, remainingKg: null },
    rolls: [roll(ROLL_1), roll(ROLL_2)],
    costStatus: 'NotCalculated',
    closedByUserId: null,
    closedAtUtc: null,
    ...overrides,
  }
}

/** The engine's `Unavailable` answer, as the server gives it for BAL-004 / BAL-006 today. */
export function unavailable(...missingParameters: string[]) {
  return { outcome: 'Unavailable', value: null, missingParameters, refusalCode: null, refusedParameter: null }
}

/** A synthetic `RollMassBalanceViewV1`, in the server's JSON shape. */
export function rollBalance(parentRollId: string, overrides: Record<string, unknown> = {}) {
  return {
    parentRollId,
    difference: { actualInputKg: '0', accountedKg: '0', differenceKg: '0', isFullyAccounted: true },
    scrapPercent: unavailable('fireKg', 'rawMaterialKg'),
    actualUnitKg: unavailable('actualKg', 'actualCount'),
    ...overrides,
  }
}

/** A synthetic `MassBalanceViewV1` (GDC-BAL/1), in the server's JSON shape. */
export function massBalance(overrides: Record<string, unknown> = {}) {
  return {
    orderId: ORDER,
    version: 1,
    formulaVersion: 'GDC-BAL/1 (master v3 §28.9)',
    rolls: [rollBalance(ROLL_1), rollBalance(ROLL_2)],
    ...overrides,
  }
}

/** A synthetic `OrderSummaryViewV1`, in the server's JSON shape. */
export function orderSummary(overrides: Record<string, unknown> = {}) {
  return {
    orderId: ORDER,
    planId: PLAN,
    workCenterId: WORK_CENTER,
    process: 'Slitting',
    status: 'Open',
    version: 1,
    plannedRollCount: 2,
    plannedInputKg: '2000',
    target: null,
    createdAtUtc: '2026-09-18T09:00:00.000Z',
    closedAtUtc: null,
    ...overrides,
  }
}

/** A synthetic `OrderListViewV1`; `nextCursor` null is the end of the list. */
export function orderList(orders: unknown[] = [orderSummary()], nextCursor: string | null = null) {
  return { orders, nextCursor }
}

/** A synthetic `PlanSummaryViewV1` (MFG-PLAN-LIST), in the server's JSON shape. */
export function planSummary(overrides: Record<string, unknown> = {}) {
  return {
    planId: PLAN,
    workCenterId: WORK_CENTER,
    process: 'Slitting',
    status: 'Draft',
    version: 1,
    plannedRollCount: 2,
    plannedInputKg: '1500.625',
    target: null,
    createdAtUtc: '2026-09-18T09:00:00.000Z',
    approvedAtUtc: null,
    ...overrides,
  }
}

/** A synthetic `PlanListViewV1`; `nextCursor` null is the end of the list. */
export function planList(plans: unknown[] = [planSummary()], nextCursor: string | null = null) {
  return { plans, nextCursor }
}

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  })
}

/** A problem body with the server's stable `code` extension (absent on 404). */
export function problem(
  status: number,
  extra: Record<string, unknown> = {},
  headers: Record<string, string> = {},
): Response {
  return json({ type: 'about:blank', title: 'Hata', status, ...extra }, status, {
    'Content-Type': 'application/problem+json',
    ...headers,
  })
}

export interface RecordedRequest {
  readonly method: string
  readonly url: string
  readonly headers: Record<string, string>
  readonly body: unknown
}

/** Same reason as the Organization harness: per-test company preference, identical everywhere. */
function resetCompanyPreference(): void {
  const store = new Map<string, string>()
  const stub: Storage = {
    get length() {
      return store.size
    },
    clear: () => store.clear(),
    getItem: (key: string) => store.get(key) ?? null,
    key: (index: number) => [...store.keys()][index] ?? null,
    removeItem: (key: string) => void store.delete(key),
    setItem: (key: string, value: string) => void store.set(key, String(value)),
  }
  Object.defineProperty(window, 'localStorage', { value: stub, configurable: true, writable: true })
}

export interface HarnessOptions {
  /** Capability ids the SERVER grants. Defaults to the recorder's. */
  readonly capabilities?: readonly string[] | (() => readonly string[])
  /** Feature-endpoint responder. Return undefined to fall through to a 404. */
  readonly respond?: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined
}

export function installFetch(options: HarnessOptions = {}): RecordedRequest[] {
  resetCompanyPreference()
  // Module state that deliberately outlives unmounts: never inherited across cases.
  clearPendingFacts()
  clearPendingPlanLifecycles()
  const capabilities = options.capabilities ?? RECORDER_CAPABILITIES
  const recorded: RecordedRequest[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers: Record<string, string> = {}
      for (const [k, v] of Object.entries((init?.headers ?? {}) as Record<string, string>)) {
        headers[k] = v
      }
      let body: unknown = null
      if (typeof init?.body === 'string') {
        try {
          body = JSON.parse(init.body)
        } catch {
          body = init.body
        }
      }
      recorded.push({ method: init?.method ?? 'GET', url, headers, body })

      if (url.endsWith('/identity/me')) return json(IDENTITY)
      if (url.endsWith('/organization/companies')) return json(DIRECTORY)
      if (url.endsWith('/shell/capabilities')) {
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: 'mfg-cap-gen-1',
          capabilities: typeof capabilities === 'function' ? capabilities() : capabilities,
        })
      }
      return (await options.respond?.(url, init)) ?? problem(404)
    }),
  )
  return recorded
}

function routes(): RouteObject[] {
  return [
    {
      path: '/',
      element: <AppShell />,
      children: [
        ...manufacturingRoutes,
        { path: '*', element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" /> },
      ],
    },
  ]
}

export function renderAt(path: string, options: { readonly staleTime?: number } = {}) {
  const router = createMemoryRouter(routes(), { initialEntries: [path] })
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: options.staleTime ?? 0 } },
  })
  const view = render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { queryClient, router, view }
}

export function requestsFor(recorded: readonly RecordedRequest[], fragment: string, method?: string) {
  return recorded.filter(
    (request) => request.url.includes(fragment) && (method === undefined || request.method === method),
  )
}
