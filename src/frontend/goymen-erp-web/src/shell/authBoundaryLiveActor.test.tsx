/**
 * SAB-004 — the authenticated actor must come from the LIVE identity query.
 *
 * WHY THIS FILE EXISTS, given that `authBoundary.test.ts` already covers the
 * reducer: every test there hands `BUSINESS_AUTH_OUTCOME` an
 * `authenticatedActorId` that the test itself wrote. None of them proves that
 * production ever PRODUCES that actor.
 *
 * That gap is not theoretical, and Codex was right to block on it (#169 P1-1).
 * `readAuthenticatedActor` reads `['shell','identity']` out of the QueryClient.
 * A wrong key, a renamed key, or a changed response envelope makes it return
 * `null` — and the reducer deliberately treats `null` as "unknown", which does
 * NOT make an outcome stale. So the binding can break silently: the late-actor
 * defect returns while every reducer test stays green.
 *
 * The test therefore drives the REAL hook inside the REAL provider, and never
 * names the query key itself — it finds the identity query by the SHAPE of its
 * data. If production and the cache stop agreeing on where identity lives, only
 * production is wrong, and this goes red.
 *
 * HOW THE ASSERTION DISCRIMINATES. Making identity B also, correctly, makes the
 * orchestrator reset the shell a tick later — so "did the epoch move" cannot
 * tell an ignored 401 from a legitimate actor reset. `sessionInvalid` and
 * `lastSecurityEffect` can: only an APPLIED 401/403 sets them, and a legitimate
 * reset leaves both untouched. That was measured against the running shell
 * before this file was written, not assumed.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { CAPABILITY } from './capabilities'
import { useShellState } from './shellContext'
import { useAuthBoundary } from './useAuthBoundary'
import type { ShellState } from './shellState'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const USER_A = '11111111-1111-1111-1111-111111111111'
const USER_B = '22222222-2222-2222-2222-222222222222'

interface Probe {
  readonly report401: () => void
  readonly report403: () => void
  readonly state: ShellState
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

const DIRECTORY = [
  { id: A_ID, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false },
  { id: B_ID, code: 'GSP', legalName: 'Göymen B', isPlaceholder: false },
]

function installFetch() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      const companyId =
        (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
      if (url.includes('/identity/me'))
        return json({
          userId: USER_A,
          displayName: 'Kullanıcı A',
          email: null,
          allowedCompanyIds: [A_ID, B_ID],
        })
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities'))
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: `gen-${companyId?.slice(-1) ?? 'x'}`,
          capabilities: [CAPABILITY.cariPartiesList],
        })
      if (url.includes('/counterparties'))
        return json({
          items: [],
          nextCursor: null,
          hasMore: false,
          totalCount: null,
          countScope: null,
        })
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
}

/**
 * Drives the real hook. The test calls `report401`/`report403` synchronously so
 * the capture and the report land in one tick, before any effect can intervene —
 * which is precisely the window the live read exists to cover.
 */
function BoundaryProbe({ bind }: { readonly bind: (probe: Probe) => void }) {
  const boundary = useAuthBoundary()
  const state = useShellState()
  bind({
    report401: () => boundary.report(401, boundary.capture()),
    report403: () => boundary.report(403, boundary.capture()),
    state,
  })
  return null
}

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
  installFetch()
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/** Finds the identity query by the shape of its data — never by its key. */
function identityQueryKey(queryClient: QueryClient): readonly unknown[] {
  const entry = queryClient
    .getQueryCache()
    .getAll()
    .find((candidate) => {
      const data = candidate.state.data as { type?: string; data?: { userId?: string } } | undefined
      return data?.type === 'ok-single' && typeof data.data?.userId === 'string'
    })
  if (!entry) throw new Error('identity query not found by shape — the harness is wrong here, not production')
  return entry.queryKey
}

async function bootShellAsA() {
  let latest: Probe | null = null
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const router = createMemoryRouter(shellRoutes, { initialEntries: ['/cari'] })
  render(
    <ShellProvider queryClient={queryClient}>
      <BoundaryProbe
        bind={(probe) => {
          latest = probe
        }}
      />
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  await screen.findByText('Kullanıcı A')
  const probe = () => {
    if (!latest) throw new Error('probe never bound')
    return latest
  }
  expect(probe().state.activeContext?.userId).toBe(USER_A)
  return { queryClient, probe }
}

/** The server now authenticates B; the shell has not committed that yet. */
function serverNowAuthenticates(queryClient: QueryClient, userId: string) {
  queryClient.setQueryData(identityQueryKey(queryClient), {
    type: 'ok-single',
    data: { userId, displayName: 'Kullanıcı B', email: null, allowedCompanyIds: [A_ID, B_ID] },
  })
}

describe('SAB-004 — the actor is read from the live identity query, not assumed', () => {
  it('a 401 for the departed actor cannot terminate the session', async () => {
    const { queryClient, probe } = await bootShellAsA()

    // One synchronous tick: identity becomes B, then A's read reports 401.
    // Every OTHER currentness term still matches — same epoch, same company,
    // same committed revision — so the live actor is the ONLY thing that can
    // make this outcome stale. Read it as null and the 401 is applied.
    act(() => {
      serverNowAuthenticates(queryClient, USER_B)
      probe().report401()
    })

    expect(probe().state.sessionInvalid).toBe(false)
    expect(probe().state.lastSecurityEffect).toBeNull()
    expect(document.body.textContent).not.toContain('Oturum sonlandı')
  })

  it('a 403 for the departed actor cannot re-fence the live capability', async () => {
    const { queryClient, probe } = await bootShellAsA()
    const capabilityBefore = probe().state.activeContext?.capabilityGeneration ?? null
    expect(capabilityBefore).not.toBeNull()

    act(() => {
      serverNowAuthenticates(queryClient, USER_B)
      probe().report403()
    })

    // `lastSecurityEffect` is the discriminating signal: only an APPLIED outcome
    // writes it. The raw fence number is not — it also moves when the shell
    // legitimately resets on the actor change this very test provokes, so
    // asserting on it measured two different causes at once and started failing
    // the moment an unrelated render landed between the setup and the check.
    expect(probe().state.lastSecurityEffect).toBeNull()
  })

  it('the same outcome IS applied while the live actor still matches', async () => {
    // The negative control. Without it a `readAuthenticatedActor` that returned
    // some constant non-matching value would pass both tests above while having
    // disabled the boundary completely.
    const { probe } = await bootShellAsA()

    act(() => {
      probe().report401()
    })

    expect(probe().state.sessionInvalid).toBe(true)
    expect(probe().state.lastSecurityEffect).not.toBeNull()
  })
})
