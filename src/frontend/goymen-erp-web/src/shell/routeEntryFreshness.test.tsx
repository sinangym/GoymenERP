/**
 * Route-entry authorization freshness — #146 / #148.
 *
 * THE HOLE THIS CLOSES, as Codex traced it on the merged main:
 *
 *   open /cari  →  leave  →  grant revoked server-side  →  Back within 30s
 *
 * The capability snapshot was taken at bootstrap and never re-taken, and the
 * business query was still fresh under the global `staleTime: 30_000`. So the
 * revisit rendered cached rows having sent NO capability call and NO business
 * read. Issue #165's 401/403 boundary cannot close it, because it can only judge
 * a request that was actually made — and a fresh cache hit makes none.
 *
 * WHY THE ASSERTIONS ARE ABOUT REQUESTS. "The old row did not render" is the
 * symptom; "the client asked again before rendering anything" is the property.
 * A test that only checked the DOM would pass against a build that renders
 * cached rows and merely happens to refetch afterwards — which is the exact
 * behaviour `staleTime: 0` and `refetchOnMount: 'always'` produce, and which
 * Codex named as insufficient.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { CAPABILITY } from './capabilities'
import { PARTY_ROLE_FLAG } from '../cari/contract'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const USER = '11111111-1111-1111-1111-111111111111'
const PARTY = '99999999-1111-1111-1111-111111111111'

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

const DIRECTORY = [{ id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false }]

/**
 * The list and the detail decode `version` differently — number on the list,
 * lossless decimal STRING on the detail since #170/#171. Using one fixture for
 * both silently produced a malformed response and no rows, which is how this
 * harness first failed.
 */
function party(versionAsString: boolean) {
  const version = versionAsString ? '1' : 1
  return {
    id: PARTY,
    code: 'CAR-000001',
    kind: 1,
    legalName: 'Örnek Metal Ltd. Şti.',
    tradeName: null,
    isActive: true,
    version,
    companyAccounts: [
      {
        id: 'cccccccc-1111-1111-1111-111111111111',
        companyId: COMPANY,
        roles: PARTY_ROLE_FLAG.Customer,
        isActive: true,
        version,
      },
    ],
  }
}

interface Harness {
  capabilityCalls: number
  listCalls: number
  granted: readonly string[]
}

function installFetch(h: Harness) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/identity/me'))
        return json({
          userId: USER,
          displayName: 'Kullanıcı A',
          email: null,
          allowedCompanyIds: [COMPANY],
        })
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities')) {
        h.capabilityCalls += 1
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          // STABLE on purpose. A generation that changed per call would move the
          // context namespace by itself, and the entry-scoped query key would look
          // load-bearing when it was not — the mutation test caught exactly that.
          generation: 'gen-stable',
          capabilities: h.granted,
        })
      }
      if (url.includes('/counterparties')) {
        h.listCalls += 1
        return json({
          items: [party(false)],
          nextCursor: null,
          hasMore: false,
          totalCount: null,
          countScope: null,
        })
      }
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
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

async function renderShell(initial = '/cari') {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [initial] })
  const queryClient = new QueryClient({
    // The PRODUCTION default, on purpose. A test that set staleTime: 0 would
    // erase the very condition being reproduced.
    defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  await screen.findByText('Kullanıcı A', undefined, { timeout: 5000 })
  return router
}

describe('#146 / #148 — a route entry re-authorizes before anything renders', () => {
  it('a revisit within the stale window re-asks for capability AND data', async () => {
    const h: Harness = { capabilityCalls: 0, listCalls: 0, granted: [CAPABILITY.cariPartiesList] }
    installFetch(h)
    const router = await renderShell()

    await screen.findByRole('row', { name: /CAR-000001/ }, { timeout: 5000 })
    const capabilityAfterFirst = h.capabilityCalls
    const listAfterFirst = h.listCalls

    // Leave, then come back — the SPA never reloads, so both caches survive.
    await router.navigate('/')
    await waitFor(() => expect(screen.queryByRole('table')).toBeNull())
    await router.navigate('/cari')
    await screen.findByRole('row', { name: /CAR-000001/ })

    expect(
      h.capabilityCalls,
      'the revisit reused the bootstrap capability snapshot',
    ).toBeGreaterThan(capabilityAfterFirst)
    expect(
      h.listCalls,
      'the revisit was served from the previous entry cache',
    ).toBeGreaterThan(listAfterFirst)
  })

  it('a grant revoked while away closes the screen on return', async () => {
    const h: Harness = { capabilityCalls: 0, listCalls: 0, granted: [CAPABILITY.cariPartiesList] }
    installFetch(h)
    const router = await renderShell()

    await screen.findByRole('row', { name: /CAR-000001/ }, { timeout: 5000 })

    await router.navigate('/')
    await waitFor(() => expect(screen.queryByRole('table')).toBeNull())
    // The server revokes it while the user is elsewhere.
    h.granted = []
    await router.navigate('/cari')

    // Non-disclosing close, and — the part that matters — the old row must never
    // appear on the way there.
    await waitFor(() => {
      expect(document.querySelector('[data-state="not-found"]')).not.toBeNull()
    })
    expect(screen.queryByRole('row', { name: /CAR-000001/ })).toBeNull()
  })

  it('no cached row is rendered while the entry is still being verified', async () => {
    // The frame-level claim. `staleTime: 0` and `refetchOnMount: "always"` both
    // still paint cached data while revalidating, which is why neither is a fix.
    const h: Harness = { capabilityCalls: 0, listCalls: 0, granted: [CAPABILITY.cariPartiesList] }
    let releaseCapability: () => void = () => {}
    const held = new Promise<void>((resolve) => {
      releaseCapability = resolve
    })
    let holdNext = false

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/identity/me'))
          return json({
            userId: USER,
            displayName: 'Kullanıcı A',
            email: null,
            allowedCompanyIds: [COMPANY],
          })
        if (url.includes('/organization/companies')) return json(DIRECTORY)
        if (url.includes('/shell/capabilities')) {
          h.capabilityCalls += 1
          if (holdNext) await held
          return json({
            contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
            // STABLE on purpose. A generation that changed per call would move the
          // context namespace by itself, and the entry-scoped query key would look
          // load-bearing when it was not — the mutation test caught exactly that.
          generation: 'gen-stable',
            capabilities: h.granted,
          })
        }
        if (url.includes('/counterparties')) {
          h.listCalls += 1
          return json({
            items: [party(false)],
            nextCursor: null,
            hasMore: false,
            totalCount: null,
            countScope: null,
          })
        }
        throw new Error(`unexpected fetch: ${url}`)
      }),
    )

    const router = await renderShell()
    await screen.findByRole('row', { name: /CAR-000001/ }, { timeout: 5000 })
    await router.navigate('/')
    await waitFor(() => expect(screen.queryByRole('table')).toBeNull())

    // Return with the capability response withheld: the entry is unverified for
    // as long as we hold it.
    holdNext = true
    await router.navigate('/cari')
    await new Promise((r) => setTimeout(r, 150))

    expect(
      screen.queryByRole('row', { name: /CAR-000001/ }),
      'a cached row rendered while its authorization was still in flight',
    ).toBeNull()
    expect(document.body.textContent).not.toContain('CAR-000001')

    releaseCapability()
    await screen.findByRole('row', { name: /CAR-000001/ })
  })
})

describe('#148 — the same gate covers C-03', () => {
  it('a detail revisit re-asks before showing the record', async () => {
    const h: Harness = {
      capabilityCalls: 0,
      listCalls: 0,
      granted: [CAPABILITY.cariPartiesList, CAPABILITY.cariPartiesDetail],
    }
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/identity/me'))
          return json({
            userId: USER,
            displayName: 'Kullanıcı A',
            email: null,
            allowedCompanyIds: [COMPANY],
          })
        if (url.includes('/organization/companies')) return json(DIRECTORY)
        if (url.includes('/shell/capabilities')) {
          h.capabilityCalls += 1
          return json({
            contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
            // STABLE on purpose. A generation that changed per call would move the
          // context namespace by itself, and the entry-scoped query key would look
          // load-bearing when it was not — the mutation test caught exactly that.
          generation: 'gen-stable',
            capabilities: h.granted,
          })
        }
        if (url.includes(`/counterparties/${PARTY}`)) {
          h.listCalls += 1
          return json(party(true))
        }
        throw new Error(`unexpected fetch: ${url}`)
      }),
    )

    const router = await renderShell(`/cari/${PARTY}`)
    await screen.findAllByText('Örnek Metal Ltd. Şti.', undefined, { timeout: 5000 })
    const detailAfterFirst = h.listCalls

    await router.navigate('/')
    await waitFor(() => expect(screen.queryAllByText('Örnek Metal Ltd. Şti.')).toHaveLength(0))
    await router.navigate(`/cari/${PARTY}`)
    await screen.findAllByText('Örnek Metal Ltd. Şti.')

    expect(
      h.listCalls,
      'the detail revisit was served from the previous entry cache',
    ).toBeGreaterThan(detailAfterFirst)
  })
})
