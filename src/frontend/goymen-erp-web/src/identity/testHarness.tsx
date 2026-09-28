/**
 * Test-only harness for the Identity access view: the REAL shell machinery (`ShellProvider`, `AppShell` bootstrap,
 * `CapabilityRoute`, the auth boundary) around this lane's own route table, with `fetch` stubbed by synthetic
 * fixtures. It uses `identityRoutes` rather than `shellRoutes` because the shell owner has not mounted them yet.
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
import { IDENTITY_CAPABILITY } from './capabilities'
import { identityRoutes } from './identityRoutes'

export const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
export const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
export const USER_ALICE = '11111111-1111-4111-8111-111111111111'
export const USER_BORA = '22222222-2222-4222-8222-222222222222'
export const USER_APPROVER = '33333333-3333-4333-8333-333333333333'
export const ROLE_OPERATOR = '44444444-4444-4444-8444-444444444444'
export const ROLE_AUDITOR = '55555555-5555-4555-8555-555555555555'
export const SITE_OSM = '66666666-6666-4666-8666-666666666666'

export const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Yönetici',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
}

export const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: COMPANY_B, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

export const FULL_CAPABILITIES = [IDENTITY_CAPABILITY.accessList, IDENTITY_CAPABILITY.roleDetail]

export function userSummary(userId: string, displayName: string, overrides: Partial<Record<string, unknown>> = {}) {
  return { userId, displayName, isActive: true, assignmentCount: 1, effectiveAssignmentCount: 1, ...overrides }
}

export function assignment(roleId: string, roleCode: string, roleName: string, overrides: Record<string, unknown> = {}) {
  return {
    assignmentId: crypto.randomUUID(),
    roleId,
    roleCode,
    roleName,
    roleIsActive: true,
    status: 'Active',
    validity: 'effective',
    validFromUtc: '2026-01-01T00:00:00+00:00',
    validUntilUtc: null,
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    approvedBy: { userId: USER_APPROVER, displayName: 'Onaylayan Yönetici' },
    scopes: [{ type: 'Company', id: COMPANY_A }],
    ...overrides,
  }
}

export function userDetail(userId: string, displayName: string, overrides: Record<string, unknown> = {}) {
  return {
    userId,
    displayName,
    isActive: true,
    assignments: [assignment(ROLE_OPERATOR, 'OPERATOR', 'Operatör')],
    effectivePermissions: [
      { code: 'organization.locations.read', isCritical: false, roleCodes: ['OPERATOR'] },
      { code: 'identity.audit.read', isCritical: true, roleCodes: ['OPERATOR'] },
    ],
    ...overrides,
  }
}

export function roleSummary(roleId: string, code: string, name: string, overrides: Record<string, unknown> = {}) {
  return {
    roleId,
    code,
    name,
    isSystem: true,
    isActive: true,
    permissionCount: 2,
    holderCount: 1,
    effectiveHolderCount: 1,
    ...overrides,
  }
}

export function roleDetail(roleId: string, code: string, name: string, overrides: Record<string, unknown> = {}) {
  return {
    roleId,
    code,
    name,
    isSystem: true,
    isActive: true,
    permissions: [
      { code: 'identity.audit.read', isCritical: true },
      { code: 'organization.locations.read', isCritical: false },
    ],
    holders: [
      {
        assignmentId: crypto.randomUUID(),
        userId: USER_ALICE,
        displayName: 'Alice',
        userIsActive: true,
        status: 'Active',
        validity: 'effective',
        validFromUtc: '2026-01-01T00:00:00+00:00',
        validUntilUtc: '2027-01-01T00:00:00+00:00',
        approvedBy: null,
        scopes: [{ type: 'Site', id: SITE_OSM }],
      },
    ],
    ...overrides,
  }
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
  const capabilities = options.capabilities ?? FULL_CAPABILITIES
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
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'identity-gen-1', capabilities })
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
      children: [...identityRoutes, { path: '*', element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" /> }],
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

export function requestsFor(recorded: readonly RecordedRequest[], fragment: string) {
  return recorded.filter((request) => request.url.includes(fragment))
}
