/**
 * Test-only harness for the discount limit screen: the REAL shell machinery (`ShellProvider`, `AppShell` bootstrap,
 * `CapabilityRoute`, the auth boundary) around the Settings route table, with `fetch` stubbed by synthetic fixtures.
 *
 * NOT evidence of the real API, real SQL or browser acceptance: a fixture is not a record. Requests are recorded, with
 * their method, headers and body, because "did not show it" and "did not send it" are different guarantees.
 */

import { vi } from 'vitest'
import { render } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { StateBlock } from '../shell/StateBlock'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { SETTINGS_CAPABILITY } from './capabilities'
import { settingsRoutes } from './settingsRoutes'

export const COMPANY_A = 'aaaaaaaa-0000-4000-8000-000000000001'
export const COMPANY_B = 'bbbbbbbb-0000-4000-8000-000000000002'
export const GROUP_SELLER = '11111111-1111-4111-8111-111111111111'
export const GROUP_MANAGER = '22222222-2222-4222-8222-222222222222'
export const GROUP_OLD = '33333333-3333-4333-8333-333333333333'
export const PERSON = '44444444-4444-4444-8444-444444444444'

export const IDENTITY = {
  userId: '99999999-9999-4999-8999-999999999999',
  displayName: 'Yönetim Kurulu Üyesi',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
  authenticationSessionBinding: null,
}

export const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: COMPANY_B, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

export const LIMIT_PATH = '/ayarlar/iskonto-limitleri'

export function groupList(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY_A,
    businessDate: '2026-10-27',
    groups: [
      {
        id: GROUP_MANAGER,
        code: 'MUDUR',
        name: 'Müdür',
        isActive: true,
        version: '2',
        limit: {
          limitVersion: '1',
          kind: 'Linked',
          percent: null,
          linkedGroupId: GROUP_SELLER,
          extraPoints: '3',
          effectiveFrom: '2026-10-27',
          recordedBy: IDENTITY.userId,
          recordedAtUtc: '2026-10-27T09:00:00+00:00',
          reason: 'SENTETIK',
        },
        effectiveLimit: { state: 'Defined', percent: '38' },
      },
      {
        id: GROUP_SELLER,
        code: 'SATIS-DESTEK',
        name: 'Satış Destek',
        isActive: true,
        version: '2',
        limit: {
          limitVersion: '1',
          kind: 'Fixed',
          percent: '35',
          linkedGroupId: null,
          extraPoints: null,
          effectiveFrom: '2026-10-27',
          recordedBy: IDENTITY.userId,
          recordedAtUtc: '2026-10-27T09:00:00+00:00',
          reason: 'SENTETIK',
        },
        effectiveLimit: { state: 'Defined', percent: '35' },
      },
      {
        id: GROUP_OLD,
        code: 'ESKI',
        name: 'Eski grup',
        isActive: false,
        version: '3',
        limit: null,
        effectiveLimit: { state: 'None', percent: null },
      },
    ],
    ...overrides,
  }
}

export function binding(versionA = '1', versionB = '1') {
  return {
    userId: PERSON,
    companies: [
      {
        companyId: COMPANY_A,
        version: versionA,
        groups: versionA === '0' ? [] : [{ id: GROUP_SELLER, code: 'SATIS-DESTEK', name: 'Satış Destek', isActive: true }],
      },
      {
        companyId: COMPANY_B,
        version: versionB,
        groups:
          versionB === '0'
            ? []
            : [{ id: '55555555-5555-4555-8555-555555555555', code: 'SATIS-DESTEK', name: 'Satış Destek', isActive: true }],
      },
    ],
  }
}

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

export function problem(status: number, extensions: Record<string, unknown> = {}): Response {
  return new Response(JSON.stringify({ type: 'about:blank', title: 'Hata', status, detail: 'Ayrıntı', ...extensions }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

export function noContent(headers: Record<string, string> = {}): Response {
  return new Response(null, { status: 204, headers })
}

export interface RecordedRequest {
  readonly method: string
  readonly url: string
  readonly headers: Record<string, string>
  readonly body: unknown
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
  const capabilities = options.capabilities ?? [SETTINGS_CAPABILITY.salesDiscountLimits]
  const recorded: RecordedRequest[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers: Record<string, string> = {}
      for (const [k, v] of Object.entries((init?.headers ?? {}) as Record<string, string>)) headers[k] = v
      const body = typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : null
      recorded.push({ method: init?.method ?? 'GET', url, headers, body })
      if (url.endsWith('/identity/me')) return json(IDENTITY)
      if (url.endsWith('/organization/companies')) return json(DIRECTORY)
      if (url.endsWith('/shell/capabilities')) {
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'settings-gen-1', capabilities })
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
      children: [...settingsRoutes, { path: '*', element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" /> }],
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

export function commands(recorded: readonly RecordedRequest[]) {
  return recorded.filter((request) => request.method !== 'GET')
}
