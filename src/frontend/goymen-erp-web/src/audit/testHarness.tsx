/**
 * Test-only harness for "Denetim kayıtları": the REAL shell machinery (`ShellProvider`, `AppShell` bootstrap,
 * `CapabilityRoute`, the auth boundary) around this module's own route table, with `fetch` stubbed by synthetic
 * fixtures. It uses `auditRoutes` because the shell router does not mount them yet (./capabilities.ts).
 *
 * NOT evidence of real API, real SQL or browser acceptance: a fixture is not a record. Requests are recorded, since
 * "did not show it" and "did not send it" are different guarantees and only the second is about authorization.
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
export const SITE_ID = '66666666-6666-4666-8666-666666666666'

export const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Denetçi',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
  authenticationSessionBinding: null,
}

export const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: COMPANY_B, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

export const GRANTED = [AUDIT_CAPABILITY.eventsList]

let sequence = 0

/** A full wire row, every published key present, as the server serializes `AuditEventItem`. */
export function auditItem(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  sequence += 1
  const suffix = String(sequence).padStart(12, '0')
  return {
    id: `0000000${sequence % 10}-aaaa-4aaa-8aaa-${suffix}`,
    companyId: COMPANY_A,
    occurredAtUtc: '2026-10-24T09:15:30.1234567+00:00',
    eventType: 'organization.site.updated',
    entityType: 'Organization.Site',
    entityId: SITE_ID,
    operation: 'Update',
    entityVersionBefore: 3,
    entityVersionAfter: 4,
    actorType: 'User',
    actorId: 'company-a-user',
    permissionCode: 'organization.sites.update',
    roleCodes: ['ORG_ADMIN'],
    beforeJson: '{"name":"GİZLİ-ÖNCEKİ"}',
    afterJson: '{"name":"GİZLİ-SONRAKİ"}',
    reason: 'GİZLİ-GEREKÇE',
    approvalContextJson: null,
    aiInvolvement: 'None',
    aiContextJson: null,
    sourceChannel: 'Api',
    clientIpAddress: '203.0.113.7',
    userAgent: 'GİZLİ-TARAYICI',
    correlationId: 'corr-0001',
    causationId: null,
    outcome: 'Succeeded',
    schemaVersion: 1,
    integrityHash: 'sha256:abc',
    ...overrides,
  }
}

export function auditPage(items: readonly unknown[], nextCursor: string | null = null, pageSize = 25) {
  return { items, pageSize, nextCursor }
}

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

export function problem(status: number): Response {
  return new Response(JSON.stringify({ type: 'about:blank', title: 'Hata', status, detail: 'Ayrıntı' }), {
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
  readonly respond?: (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined
}

export function installFetch(options: HarnessOptions = {}): RecordedRequest[] {
  resetCompanyPreference()
  const capabilities = options.capabilities ?? GRANTED
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
      return options.respond?.(url, init) ?? problem(404)
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
  return recorded.filter((request) => request.url.includes('/audit/events'))
}
