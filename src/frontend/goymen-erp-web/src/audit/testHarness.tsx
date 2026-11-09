/**
 * Test-only harness for "Denetim kaydı": the REAL shell machinery (`ShellProvider`, `AppShell` bootstrap,
 * `CapabilityRoute`, the auth boundary) around this lane's route table, with `fetch` stubbed by SENTETIK fixtures.
 *
 * NOT evidence of real API, real SQL or browser acceptance: a fixture is not a record. Requests are recorded, since
 * "did not show it" and "did not send it" are different guarantees.
 */

import { vi } from 'vitest'
import { render } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { StateBlock } from '../shell/StateBlock'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { AUDIT_CAPABILITY } from './capabilities'
import { auditRoutes } from './auditRoutes'

export const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
export const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'

const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'SENTETIK Denetçi',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
  authenticationSessionBinding: null,
}

const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'SENTETIK Şirket A', isPlaceholder: false },
  { id: COMPANY_B, code: 'GSP', legalName: 'SENTETIK Şirket B', isPlaceholder: false },
]

let sequence = 0

/** One SENTETIK audit event of company A, with every key of `AuditEventItem`. */
export function auditEvent(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  sequence += 1
  const suffix = String(sequence).padStart(12, '0')
  return {
    id: `cccccccc-0000-4000-8000-${suffix}`,
    companyId: COMPANY_A,
    occurredAtUtc: '2026-11-01T09:30:00+00:00',
    eventType: 'SalesOrderApproved',
    entityType: 'SalesOrder',
    entityId: `SENTETIK-SO-${sequence}`,
    operation: 'Update',
    entityVersionBefore: 3,
    entityVersionAfter: 4,
    actorType: 'User',
    actorId: 'SENTETIK-user-1',
    permissionCode: 'sales.orders.approve',
    roleCodes: ['SALES_MANAGER'],
    beforeJson: '{"status":"Submitted"}',
    afterJson: '{"status":"Approved"}',
    reason: 'SENTETIK gerekçe',
    approvalContextJson: null,
    aiInvolvement: 'None',
    aiContextJson: null,
    sourceChannel: 'Web',
    clientIpAddress: '203.0.113.77',
    userAgent: 'SENTETIK-Agent/1.0',
    correlationId: `SENTETIK-corr-${sequence}`,
    causationId: null,
    outcome: 'Succeeded',
    schemaVersion: 1,
    integrityHash: 'sha256:SENTETIK',
    ...overrides,
  }
}

export function page(items: readonly unknown[], nextCursor: string | null = null, pageSize = 25) {
  return { items, pageSize, nextCursor }
}

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

export function problem(status: number): Response {
  return new Response(JSON.stringify({ type: 'about:blank', title: 'Hata', status }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

export interface RecordedRequest {
  readonly method: string
  readonly url: string
  readonly headers: Record<string, string>
}

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
  readonly capabilities?: readonly string[]
  /** Answers an audit read; `undefined` falls through to a 404. */
  readonly respond?: (url: URL) => Response | Promise<Response> | undefined
}

export function installFetch(options: HarnessOptions = {}): RecordedRequest[] {
  resetCompanyPreference()
  const capabilities = options.capabilities ?? [AUDIT_CAPABILITY.list]
  const recorded: RecordedRequest[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers: Record<string, string> = {}
      for (const [k, v] of Object.entries((init?.headers ?? {}) as Record<string, string>)) headers[k] = v
      recorded.push({ method: init?.method ?? 'GET', url, headers })
      if (url.endsWith('/identity/me')) return json(IDENTITY)
      if (url.endsWith('/organization/companies')) return json(DIRECTORY)
      if (url.endsWith('/shell/capabilities')) {
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'audit-gen-1', capabilities })
      }
      if (url.includes('/audit/events')) {
        return (await options.respond?.(new URL(url, 'http://sentetik.invalid'))) ?? problem(404)
      }
      return problem(404)
    }),
  )
  return recorded
}

function routes(): RouteObject[] {
  return [
    {
      path: '/',
      element: <AppShell />,
      children: [...auditRoutes, { path: '*', element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" /> }],
    },
  ]
}

export function renderAt(path: string) {
  const router = createMemoryRouter(routes(), { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { router, queryClient }
}

export function auditReads(recorded: readonly RecordedRequest[]) {
  return recorded
    .filter((request) => request.url.includes('/audit/events'))
    .map((request) => ({ ...request, query: new URL(request.url, 'http://sentetik.invalid').searchParams }))
}
