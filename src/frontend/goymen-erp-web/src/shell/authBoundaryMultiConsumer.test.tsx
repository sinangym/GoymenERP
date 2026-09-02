/**
 * SAB-010 — exactly once, across TWO independent consumers.
 *
 * The committed test this replaces did not have the topology it claimed. It used
 * one `renderApp()`, one business query and three `refetchQueries()` calls on the
 * same QueryClient, so it exercised one hook instance resolving one fetch three
 * times. Codex's #169 P1-2 was right: two `useAuthBoundary` instances holding two
 * different query keys and reporting the SAME current outcome is a different
 * situation, and it is the one an exactly-once ledger exists for.
 *
 * That topology is not hypothetical. `useAuthBoundary` is a hook, so the moment a
 * second screen reads business data — C-03 is already open in #172 — two
 * consumers under one shell is the normal case.
 *
 * WHAT IS ASSERTED, and why each is needed:
 *
 *   fence advances EXACTLY +1   two dispatches would push the fence past what the
 *                               facts justify, invalidating requests that were
 *                               never stale
 *   namespace cleanup runs ONCE a second `removeQueries` for the same effect key
 *                               is the storm the ledger is supposed to prevent
 *   capability stripped ONCE    the observable end state, so the test still fails
 *                               if the ledger is "fixed" by doing nothing at all
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, useQuery } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { CAPABILITY } from './capabilities'
import { useShellState } from './shellContext'
import { useAuthBoundary } from './useAuthBoundary'
import { contextKey, contextKeyString, type ShellState } from './shellState'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const USER = '11111111-1111-1111-1111-111111111111'

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

/**
 * A business reader that owns its own query key and its own boundary instance —
 * the same shape `CariListPage` has, reduced to what this case needs.
 */
function BusinessReader({ resource }: { readonly resource: string }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', resource],
    enabled: namespace !== null,
    queryFn: async () => {
      const snapshot = boundary.capture()
      const response = await fetch(`/api/v1/${resource}`, {
        headers: { 'X-Company-Id': shell.activeContext?.companyId ?? '' },
      })
      if (response.status === 403) {
        boundary.report(403, snapshot)
        return { forbidden: true }
      }
      return { forbidden: false }
    },
  })
  return <div data-testid={`reader-${resource}`}>{query.isFetched ? 'done' : 'pending'}</div>
}

/** Records the fence on every render — "exactly once" is a COUNT, not a state. */
function FenceProbe({ log }: { readonly log: ShellState[] }) {
  const state = useShellState()
  if (log[log.length - 1] !== state) log.push(state)
  return null
}

beforeEach(() => {
  try {
    window.localStorage.clear()
  } catch {
    // ignore
  }
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('SAB-010 — one current 403, two independent consumers', () => {
  it('advances the fence once and cleans the namespace once', async () => {
    let forbiddenReads = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input)
        const companyId =
          (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
        if (url.includes('/identity/me'))
          return json({
            userId: USER,
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
        // BOTH readers are refused with the SAME current outcome.
        forbiddenReads += 1
        return json({ code: 'forbidden' }, 403)
      }),
    )

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: 0 } },
    })
    const removeQueries = vi.spyOn(queryClient, 'removeQueries')
    const states: ShellState[] = []

    // The REAL shell has to run: `useShellOrchestrator` lives in AppShell, so a
    // bare provider never bootstraps and never commits a context. The two readers
    // sit beside it, which is exactly how a second screen would.
    const router = createMemoryRouter(shellRoutes, { initialEntries: ['/'] })
    render(
      <ShellProvider queryClient={queryClient}>
        <FenceProbe log={states} />
        <BusinessReader resource="counterparties" />
        <BusinessReader resource="counterparty-accounts" />
        <RouterProvider router={router} />
      </ShellProvider>,
    )

    // Observed through shell state, not through chrome.
    await waitFor(() => {
      expect(states.some((s) => s.activeContext !== null)).toBe(true)
    })
    const committed = states.find((s) => s.activeContext !== null)!
    const fenceBefore = committed.requestFenceGeneration

    // Both readers must actually have been refused, or the case is vacuous.
    await waitFor(() => expect(forbiddenReads).toBeGreaterThanOrEqual(2))
    await waitFor(() => {
      expect(screen.getByTestId('reader-counterparties').textContent).toBe('done')
      expect(screen.getByTestId('reader-counterparty-accounts').textContent).toBe('done')
    })

    await waitFor(() => {
      const latest = states[states.length - 1]
      expect(latest.lastSecurityEffect).not.toBeNull()
    })

    console.log('FENCE DIZISI:', states.map((s) => s.requestFenceGeneration).join(' -> '))
    console.log('REV DIZISI  :', states.map((s) => s.committedContextRevision).join(' -> '))
    console.log('CAPGEN DIZISI:', states.map((s) => String(s.activeContext?.capabilityGeneration ?? 'null')).join(' -> '))
    console.log('EFFECT KEYS :', [...new Set(states.map((s) => s.lastSecurityEffect?.key ?? '-'))].join(' || '))
    const latest = states[states.length - 1]

    // Exactly one step. Two dispatches would move it twice.
    expect(
      latest.requestFenceGeneration,
      'the fence advanced more than once for a single current 403',
    ).toBe(fenceBefore + 1)

    // The capability is stripped — the outcome was applied, not merely deduplicated
    // into doing nothing.
    expect(latest.activeContext?.capabilityGeneration ?? null).toBeNull()

    // And the namespace cleanup ran once, not once per consumer.
    const namespaceCleanups = removeQueries.mock.calls.filter(
      (call) => typeof call[0]?.predicate === 'function',
    )
    expect(
      namespaceCleanups.length,
      'the namespace was cleaned once per consumer instead of once per outcome',
    ).toBe(1)

    // A session-wide clear belongs to 401, never to 403.
    expect(latest.sessionInvalid).toBe(false)
  })
})
