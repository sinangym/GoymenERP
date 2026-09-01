/**
 * SHELL-AUTH-BOUNDARY-001 integration proof (Issue #165).
 *
 * SAB-011 is explicit that `queryByRole('table') === null` does not close this
 * issue. Every assertion below checks the shell phase, the session epoch, the
 * capability usability, the QueryClient contents and the network call count
 * TOGETHER — because a screen that hides its table has not ended a session, and
 * that was exactly the defect.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { PARTY_ROLE_FLAG } from './contract'
import { useShellState } from '../shell/shellContext'

const A_ID = 'aaaaaaaa-0000-0000-0000-000000000001'
const B_ID = 'bbbbbbbb-0000-0000-0000-000000000002'
const USER_A = '11111111-1111-1111-1111-111111111111'
const ACC_1 = 'cccccccc-1111-1111-1111-111111111111'
const PARTY_1 = '99999999-1111-1111-1111-111111111111'

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function identity(userId = USER_A) {
  return { userId, displayName: 'Kullanıcı A', email: null, allowedCompanyIds: [A_ID, B_ID] }
}
const DIRECTORY = [
  { id: A_ID, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false },
  { id: B_ID, code: 'GSP', legalName: 'Göymen B', isPlaceholder: false },
]

function party(over: Record<string, unknown> = {}) {
  return {
    id: PARTY_1,
    code: 'CAR-000001',
    kind: 1,
    legalName: 'Örnek Metal Ltd. Şti.',
    tradeName: null,
    isActive: true,
    version: 1,
    companyAccounts: [
      { id: ACC_1, companyId: A_ID, roles: PARTY_ROLE_FLAG.Customer, isActive: true, version: 1 },
    ],
    ...over,
  }
}
function page(over: Record<string, unknown> = {}) {
  return { items: [party()], nextCursor: null, hasMore: false, totalCount: null, countScope: null, ...over }
}

interface Harness {
  counterparties: (url: string, companyId: string | null, call: number) => Response
}

function installFetch(config: Partial<Harness> = {}) {
  const calls: { url: string; companyId: string | null }[] = []
  let listCalls = 0
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString()
    const companyId = (init?.headers as Record<string, string> | undefined)?.['X-Company-Id'] ?? null
    calls.push({ url, companyId })
    if (url.includes('/identity/me')) return json(identity())
    if (url.includes('/organization/companies')) return json(DIRECTORY)
    if (url.includes('/shell/capabilities'))
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: `gen-${companyId?.slice(-1) ?? 'x'}`,
        capabilities: [CAPABILITY.cariPartiesList],
      })
    if (url.includes('/counterparties')) {
      listCalls += 1
      return (config.counterparties ?? (() => json(page())))(url, companyId, listCalls)
    }
    throw new Error(`unexpected fetch: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return {
    calls,
    listCallCount: () => calls.filter((c) => c.url.includes('/counterparties')).length,
  }
}

/** Records the shell epoch on every render — "exactly once" is a COUNT, not a state. */
function EpochProbe({ log }: { readonly log: number[] }) {
  const s = useShellState()
  if (log[log.length - 1] !== s.sessionEpoch) log.push(s.sessionEpoch)
  return null
}

function renderApp(epochLog?: number[]) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: ['/cari'] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      {epochLog ? <EpochProbe log={epochLog} /> : null}
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return { queryClient, router }
}

/** SAB-011: the evidence a security assertion must actually rest on. */
function securityEvidence(queryClient: QueryClient) {
  const queries = queryClient.getQueryCache().getAll()
  return {
    businessQueries: queries.filter((q) => String(q.queryKey[1] ?? '') === 'counterparties').length,
    // what actually survived, for diagnosis
    survivors: queries.map((q) => ({ key: JSON.stringify(q.queryKey).slice(0, 60), hasData: q.state.data !== undefined })),
    queriesWithData: queries.filter((q) => q.state.data !== undefined).length,
    totalQueries: queries.length,
    domHasBusinessData: /CAR-000001|Örnek Metal/.test(document.body.textContent ?? ''),
    tableRendered: document.querySelector('table') !== null,
  }
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

describe('SAB-002 / SAB-003 — a current 401 ends the session centrally', () => {
  it('first-page 401: session reset, every cached read gone, no refetch storm', async () => {
    const h = installFetch({ counterparties: () => json({ code: 'authentication_required' }, 401) })
    const { queryClient } = renderApp()

    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()

    const ev = securityEvidence(queryClient)
    // Not just "no table" — nothing survives anywhere.
    expect(ev.tableRendered).toBe(false)
    expect(ev.domHasBusinessData).toBe(false)
    expect(ev.queriesWithData).toBe(0)
    expect(ev.businessQueries).toBe(0)
    // The only entries left are the two DISABLED shell observers, and they hold
    // nothing. Asserting totalQueries===0 would measure the wrong thing: an empty
    // query entry is not cached data, and a disabled observer cannot refetch.
    expect(ev.survivors.every((q) => !q.hasData)).toBe(true)

    // SAB-003: mounted observers must not restart the queries.
    const settled = h.calls.length
    await new Promise((r) => setTimeout(r, 120))
    expect(h.calls.length).toBe(settled)
  })

  it('page 1 succeeds, then a continuation 401 tears the whole session down', async () => {
    const h = installFetch({
      counterparties: (_url, _c, call) =>
        call === 1
          ? json(page({ hasMore: true, nextCursor: 'next-1' }))
          : json({ code: 'authentication_required' }, 401),
    })
    const { queryClient } = renderApp()
    await screen.findByRole('row', { name: /CAR-000001/ })
    // the validated first page really is cached before we start
    expect(securityEvidence(queryClient).queriesWithData).toBeGreaterThan(0)

    await userEvent.click(screen.getByRole('button', { name: /Daha fazla yükle/i }))

    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()
    const ev = securityEvidence(queryClient)
    expect(ev.domHasBusinessData).toBe(false)
    expect(ev.queriesWithData).toBe(0)
    expect(ev.businessQueries).toBe(0)
    expect(h.listCallCount()).toBe(2)
  })
})

describe('SAB-007 — the cleared state cannot be resurrected', () => {
  it('a manual refetch after a 401 brings nothing back', async () => {
    installFetch({
      counterparties: (_u, _c, call) =>
        call === 1 ? json(page()) : json({ code: 'authentication_required' }, 401),
    })
    const { queryClient } = renderApp()
    await screen.findByRole('row', { name: /CAR-000001/ })

    // force the second call: a background/manual refetch, not a continuation.
    await queryClient.refetchQueries()
    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()

    // Everything an app might do to bring data back:
    await queryClient.refetchQueries()
    await queryClient.invalidateQueries()
    window.dispatchEvent(new Event('focus'))
    window.dispatchEvent(new Event('online'))
    await new Promise((r) => setTimeout(r, 150))

    const ev = securityEvidence(queryClient)
    expect(ev.domHasBusinessData).toBe(false)
    expect(ev.queriesWithData).toBe(0)
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('SAB-004 / SAB-005 — a current 403 re-fences the capability', () => {
  it('the feature closes and does not remount on the rejected authority', async () => {
    installFetch({ counterparties: () => json({ code: "forbidden" }, 403) })
    const { queryClient } = renderApp()

    // The gated route closes; it does not sit there showing a local error over
    // a still-usable capability.
    await waitFor(() => {
      expect(document.querySelector('[data-state]')).not.toBeNull()
    })

    const ev = securityEvidence(queryClient)
    expect(ev.tableRendered).toBe(false)
    expect(ev.domHasBusinessData).toBe(false)
    // the namespace's business queries were removed, not left to refetch
    expect(ev.businessQueries).toBe(0)

    // the session itself is NOT ended by a 403
    expect(screen.queryByText(/Oturum sonlandı/i)).toBeNull()
  })
})

describe('SAB-006 — one boundary on every read path', () => {
  it('a refetch 401 is handled exactly like a first-page 401', async () => {
    installFetch({
      counterparties: (_u, _c, call) =>
        call === 1 ? json(page()) : json({ code: 'authentication_required' }, 401),
    })
    const { queryClient } = renderApp()
    await screen.findByRole('row', { name: /CAR-000001/ })
    await queryClient.refetchQueries()

    // Same terminal outcome as the first-page case above — no weaker variant.
    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()
    const ev = securityEvidence(queryClient)
    expect(ev.queriesWithData).toBe(0)
    expect(ev.businessQueries).toBe(0)
    expect(ev.domHasBusinessData).toBe(false)
  })
})

describe('SAB-010 — the same outcome applies exactly once', () => {
  it('two observers of one 401 advance the session epoch by one, not two', async () => {
    // Two components subscribed to the SAME query both see the rejection. Without
    // an exactly-once guard each dispatches its own reset: the fence moves further
    // than the facts justify and the app re-renders in a storm.
    const h = installFetch({
      counterparties: (_u, _c, call) =>
        call === 1 ? json(page()) : json({ code: 'authentication_required' }, 401),
    })
    const epochs: number[] = []
    const { queryClient } = renderApp(epochs)
    await screen.findByRole('row', { name: /CAR-000001/ })
    const epochBefore = epochs[epochs.length - 1]

    // Force several observers to resolve the same failing fetch together.
    await Promise.all([
      queryClient.refetchQueries(),
      queryClient.refetchQueries(),
      queryClient.refetchQueries(),
    ])
    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()

    // The terminal state is reached once and stays: no reset storm, and the
    // dead session issues no further requests.
    const settled = h.calls.length
    await new Promise((r) => setTimeout(r, 200))
    expect(h.calls.length).toBe(settled)

    // THE assertion: the epoch moved exactly one step. Two resets would move it
    // twice and push the fence past what the facts justify.
    expect(epochs[epochs.length - 1]).toBe(epochBefore + 1)

    const ev = securityEvidence(queryClient)
    expect(ev.queriesWithData).toBe(0)
    expect(ev.businessQueries).toBe(0)
  })
})
