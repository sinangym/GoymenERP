/**
 * Test-only harness for the Accounting screens (ACC-V0b).
 *
 * Mounts the REAL shell machinery — `ShellProvider`, `AppShell` bootstrap, `CapabilityRoute` entry
 * gate, the auth boundary — around this lane's own route table (the shell owner has not mounted it
 * yet; that is the handoff), and stubs `fetch` with SENTETIK fixtures. No database, no real host, no
 * secret.
 *
 * WHAT THIS HARNESS IS NOT: evidence of the real API, real SQL or browser acceptance. Every assertion
 * built on it is a component/DOM claim and nothing more.
 *
 * Requests are RECORDED with method, headers and parsed body, because "did not show it" and "did not
 * send it" are different guarantees.
 */

import { vi } from 'vitest'
import { render } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { StateBlock } from '../shell/StateBlock'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { ACCOUNTING_CAPABILITY } from './capabilities'
import { accountingRoutes } from './accountingRoutes'
import { clearPendingPeriodCommands } from './pendingPeriodCommands'

export const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
export const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
export const PERIOD_SEPTEMBER = 'a1000000-0000-4000-8000-000000000009'
export const PERIOD_AUGUST = 'a1000000-0000-4000-8000-000000000008'
export const PREPARER = 'a2000000-0000-4000-8000-000000000001'
export const APPROVER = 'a2000000-0000-4000-8000-000000000002'
export const JOURNAL = 'a3000000-0000-4000-8000-000000000001'
export const SOURCE = 'a4000000-0000-4000-8000-000000000001'
export const CURRENCY = 'a5000000-0000-4000-8000-000000000001'
export const ACCOUNT_DEBIT = 'a6000000-0000-4000-8000-000000000001'
export const ACCOUNT_CREDIT = 'a6000000-0000-4000-8000-000000000002'

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

export const READER_CAPABILITIES = [ACCOUNTING_CAPABILITY.periodsList]
export const APPROVER_CAPABILITIES = [
  ACCOUNTING_CAPABILITY.periodsList,
  ACCOUNTING_CAPABILITY.periodsApprove,
  ACCOUNTING_CAPABILITY.periodsClose,
  ACCOUNTING_CAPABILITY.journalsList,
  ACCOUNTING_CAPABILITY.journalsDetail,
  ACCOUNTING_CAPABILITY.postingQueues,
]
export const PREPARER_CAPABILITIES = [
  ACCOUNTING_CAPABILITY.periodsList,
  ACCOUNTING_CAPABILITY.periodsPropose,
  ACCOUNTING_CAPABILITY.periodsApprove,
]

/** A synthetic `PeriodViewV1`, in the server's JSON shape. */
export function period(overrides: Record<string, unknown> = {}) {
  return {
    periodId: PERIOD_SEPTEMBER,
    companyId: COMPANY_A,
    startsOn: '2026-09-01',
    endsOn: '2026-09-30',
    status: 'Proposed',
    version: 1,
    preparedByUserId: PREPARER,
    preparedAtUtc: '2026-08-31T09:00:00+00:00',
    approvedByUserId: null,
    approvedAtUtc: null,
    closedByUserId: null,
    closedAtUtc: null,
    isOwnProposal: false,
    ...overrides,
  }
}

export function openPeriod(overrides: Record<string, unknown> = {}) {
  return period({ status: 'Open', version: 2, approvedByUserId: APPROVER, approvedAtUtc: '2026-08-31T10:00:00+00:00', ...overrides })
}

export function periodPage(items: readonly unknown[], nextCursor: string | null = null) {
  return { items, nextCursor }
}

/** A synthetic `JournalSummaryV1`. */
export function journalSummary(overrides: Record<string, unknown> = {}) {
  return {
    journalId: JOURNAL,
    kind: 'Original',
    sourceId: SOURCE,
    postingPurpose: 'synthetic-purpose-a',
    postingRuleVersion: 'synthetic-rule-1',
    currencyId: CURRENCY,
    transactionDate: '2026-09-14',
    accountingDate: '2026-09-15',
    valueDate: null,
    periodId: PERIOD_SEPTEMBER,
    linkedJournalId: null,
    totalDebit: '1250.5',
    totalCredit: '1250.5',
    lineCount: 2,
    storedAtUtc: '2026-09-18T10:00:00+00:00',
    ...overrides,
  }
}

export function journalDetail(overrides: Record<string, unknown> = {}) {
  return {
    journal: journalSummary(overrides),
    lines: [
      { lineNumber: 1, accountId: ACCOUNT_DEBIT, debit: '1250.5', credit: '0' },
      { lineNumber: 2, accountId: ACCOUNT_CREDIT, debit: '0', credit: '1250.5' },
    ],
  }
}

export const PROPOSAL = 'a7000000-0000-4000-8000-000000000001'
export const REVERSAL_JOURNAL = 'a3000000-0000-4000-8000-000000000002'

/** A synthetic `AccountingJournalReversalProposalViewV1` (SENTETIK, not UAT), pending by default. */
export function reversalProposal(overrides: Record<string, unknown> = {}) {
  return {
    proposalId: PROPOSAL,
    companyId: COMPANY_A,
    sourceJournalId: JOURNAL,
    reversalJournalId: null,
    periodId: PERIOD_SEPTEMBER,
    transactionDate: '2026-09-23',
    accountingDate: '2026-09-24',
    valueDate: null,
    reason: 'SENTETIK ters kayıt gerekçesi',
    proposedBy: PREPARER,
    proposedAtUtc: '2026-09-24T09:00:00+00:00',
    status: 'Pending',
    decidedBy: null,
    decidedAtUtc: null,
    decisionReason: null,
    version: 1,
    ...overrides,
  }
}

export function approvedReversalProposal(overrides: Record<string, unknown> = {}) {
  return reversalProposal({
    status: 'Approved',
    version: 2,
    decidedBy: APPROVER,
    decidedAtUtc: '2026-09-24T10:00:00+00:00',
    reversalJournalId: REVERSAL_JOURNAL,
    ...overrides,
  })
}

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

/** A problem body with the server's stable `code` extension (absent on 404). */
export function problem(status: number, extra: Record<string, unknown> = {}, headers: Record<string, string> = {}): Response {
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

/** Per-test company preference, identical to the other lanes' harnesses. */
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
  /** Capability ids the SERVER grants. Defaults to the approver's. */
  readonly capabilities?: readonly string[]
  /** Feature-endpoint responder. Return undefined to fall through to a 404. */
  readonly respond?: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined
  /** The signed-in identity's user id (defaults to the neutral synthetic user). */
  readonly identityUserId?: string
}

export function installFetch(options: HarnessOptions = {}): RecordedRequest[] {
  resetCompanyPreference()
  // Module state that deliberately outlives unmounts: never inherited across cases.
  clearPendingPeriodCommands()
  const capabilities = options.capabilities ?? APPROVER_CAPABILITIES
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

      if (url.endsWith('/identity/me')) return json({ ...IDENTITY, userId: options.identityUserId ?? IDENTITY.userId })
      if (url.endsWith('/organization/companies')) return json(DIRECTORY)
      if (url.endsWith('/shell/capabilities')) {
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'acc-cap-gen-1', capabilities })
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
      children: [...accountingRoutes, { path: '*', element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" /> }],
    },
  ]
}

export function renderAt(path: string) {
  const router = createMemoryRouter(routes(), { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const view = render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { queryClient, router, view }
}

export function requestsFor(recorded: readonly RecordedRequest[], fragment: string, method?: string) {
  return recorded.filter((request) => request.url.includes(fragment) && (method === undefined || request.method === method))
}
