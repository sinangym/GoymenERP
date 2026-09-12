/**
 * Test-only harness for the Organization screens.
 *
 * Mounts the REAL shell machinery — `ShellProvider`, `AppShell` bootstrap,
 * `CapabilityRoute` entry gate, the auth boundary — around this lane's own route
 * table, and stubs `fetch` with synthetic fixtures. No database, no real host, no
 * secret. Same status as `../shell/fixtures.ts` and `../cari/commandPageHarness.ts`:
 * not used by production code.
 *
 * It uses `organizationRoutes` rather than the shared `shellRoutes` because the shell
 * owner has not mounted these routes yet (that is the handoff). Everything above the
 * route table is the real thing, so the gate, the fence and the boundary are exercised
 * as they will run in the app.
 *
 * WHAT THIS HARNESS IS NOT: evidence of real API, real SQL or browser acceptance.
 * A fixture is not a record. Every assertion built on it is a component/DOM claim and
 * nothing more.
 *
 * Requests are RECORDED with method, headers and parsed body, because "did not show
 * it" and "did not send it" are different guarantees and only the second is about
 * authorization.
 */

import { vi } from 'vitest'
import { render } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { StateBlock } from '../shell/StateBlock'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { ORGANIZATION_CAPABILITY } from './capabilities'
import { organizationRoutes } from './organizationRoutes'

export const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
export const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
export const SITE_OSM = '11111111-1111-1111-1111-111111111111'
export const SITE_GAZ = '22222222-2222-2222-2222-222222222222'
export const LOC_H0 = '33333333-3333-3333-3333-333333333333'
export const LOC_H1 = '44444444-4444-4444-4444-444444444444'
export const LOC_GAZ = '55555555-5555-5555-5555-555555555555'
/** A location whose site is NOT in the /sites response — a real scope combination. */
export const LOC_ORPHAN = '66666666-6666-6666-6666-666666666666'
export const SITE_UNSEEN = '77777777-7777-7777-7777-777777777777'

export const IDENTITY = {
  userId: '99999999-9999-9999-9999-999999999999',
  displayName: 'Kullanıcı A',
  email: null,
  allowedCompanyIds: [COMPANY_A, COMPANY_B],
}

export const DIRECTORY = [
  { id: COMPANY_A, code: 'GDC', legalName: 'Göymen Demir Çelik', isPlaceholder: false },
  { id: COMPANY_B, code: 'GSP', legalName: 'Göymen Sac Profil', isPlaceholder: false },
]

/** Read-only capability set: the card renders, the write surface does not. */
export const READ_CAPABILITIES = [
  ORGANIZATION_CAPABILITY.settingsList,
  ORGANIZATION_CAPABILITY.locationDetail,
]

/** Read + the status control. Mirrors the seeded ORGANIZATION_OPERATOR role. */
export const OPERATOR_CAPABILITIES = [...READ_CAPABILITIES, ORGANIZATION_CAPABILITY.locationStatus]

export function site(id: string, code: string, name: string, type = 'Factory', isActive = true) {
  return { id, code, name, type, isActive }
}

export function location(
  id: string,
  siteId: string,
  code: string,
  name: string,
  overrides: { isExternal?: boolean; isActive?: boolean; version?: number } = {},
) {
  return {
    id,
    siteId,
    code,
    name,
    isExternal: overrides.isExternal ?? false,
    isActive: overrides.isActive ?? true,
    version: overrides.version ?? 1,
  }
}

export function page(
  items: readonly unknown[],
  overrides: { page?: number; pageSize?: number; totalCount?: number } = {},
) {
  return {
    items,
    page: overrides.page ?? 1,
    pageSize: overrides.pageSize ?? 25,
    totalCount: overrides.totalCount ?? items.length,
  }
}

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  })
}

/** A 204 — genuinely empty, like the real endpoint. */
export function noContent(headers: Record<string, string> = {}): Response {
  return new Response(null, { status: 204, headers })
}

/** A code-less problem body, the shape 404/409 actually use on this surface. */
export function problem(
  status: number,
  extra: Record<string, unknown> = {},
  headers: Record<string, string> = {},
): Response {
  return json({ type: 'about:blank', title: 'Hata', status, detail: 'Ayrıntı', ...extra }, status, {
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

/**
 * The shell remembers the last chosen company in `localStorage`
 * (`useShellOrchestrator`, key `goymen-company-id`) and bootstraps into it. That is
 * SHARED state between tests in one file, so without a reset the company a previous
 * case switched to becomes the company the next case starts in — and a test that means
 * "switch from A to B" silently becomes "already in B, no switch at all".
 *
 * This is exactly what failed on CI while passing locally: this Node build has no
 * `window.localStorage`, so the preference was never stored here and every case
 * bootstrapped into the first authorized company. CI has real web storage, so the leak
 * was real there and only there.
 *
 * Rather than call `window.localStorage.clear()` — which throws in this environment and
 * is why several shell test files fail locally — the harness installs a small in-memory
 * store and clears it per test. Both environments then behave the SAME way, and the way
 * they behave is the one production has.
 */
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
  // Defined on the window the shell actually reads (`window.localStorage`), per test.
  Object.defineProperty(window, 'localStorage', {
    value: stub,
    configurable: true,
    writable: true,
  })
}

export interface HarnessOptions {
  /** Capability ids the SERVER grants. Defaults to read + status. */
  readonly capabilities?: readonly string[]
  /** Feature-endpoint responder. Return undefined to fall through to a 404. */
  readonly respond?: (url: string, init: RequestInit | undefined) => Response | undefined
}

/**
 * Installs the fetch stub. Shell endpoints are always answered so the shell can
 * bootstrap; everything else is delegated to `respond`.
 */
export function installFetch(options: HarnessOptions = {}): RecordedRequest[] {
  // Per test, before anything mounts: the company the shell bootstraps into must be
  // decided by this case alone, never inherited from the previous one.
  resetCompanyPreference()
  const capabilities = options.capabilities ?? OPERATOR_CAPABILITIES
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
          generation: 'org-cap-gen-1',
          capabilities,
        })
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
      children: [
        ...organizationRoutes,
        { path: '*', element: <StateBlock state={{ kind: 'not-found' }} subject="sayfa" /> },
      ],
    },
  ]
}

export function renderAt(
  path: string,
  options: {
    /**
     * Keep pages the way PRODUCTION does — the shell's 30 s `staleTime` and default
     * gcTime. The default client keeps nothing once a key loses its observer, which
     * would make any "the list refreshed" assertion vacuous: every revisit is a
     * request anyway. Use this whenever the point is that a CACHED page was dropped.
     */
    readonly retainCache?: boolean
  } = {},
) {
  const router = createMemoryRouter(routes(), { initialEntries: [path] })
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: options.retainCache
        ? { retry: false, staleTime: 30_000, refetchOnWindowFocus: false }
        : { retry: false, gcTime: 0 },
    },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { queryClient, router }
}

/** Requests whose URL contains `fragment`. */
export function requestsFor(recorded: readonly RecordedRequest[], fragment: string) {
  return recorded.filter((request) => request.url.includes(fragment))
}
