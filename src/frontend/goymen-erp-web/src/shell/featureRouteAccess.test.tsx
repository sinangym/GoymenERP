/**
 * Mounted feature routes are reachable when granted and close neutrally when not
 * (#264 ÜRÜN P2, shell follow-up).
 *
 * The drift tests (`productMountDrift`, `organizationMountDrift`) pin the two lanes'
 * capability ids and routes to each other. They never render the router, so a
 * router that dropped `...productRoutes` would still pass them: every id would
 * agree and no screen would mount. These cases render the real `shellRoutes`.
 *
 * What each case is built to catch:
 * - `...productRoutes` / `...organizationRoutes` deleted from the router: the
 *   granted case finds the catch-all's not-found block instead of the page heading.
 * - a lane route mounted WITHOUT its `CapabilityRoute`: the ungranted case finds the
 *   page heading (and a business request) instead of the neutral block.
 *
 * MOVING A SPREAD BEHIND THE CATCH-ALL IS NOT A BEHAVIOURAL CHANGE on the pinned
 * react-router (8.3.1). Routes are ranked by score (`rankRouteBranches`,
 * `computeScore`): a static segment scores +10, while `*` scores lower and carries a
 * splat penalty, so `/urun` beats the catch-all wherever it is declared. Declaration
 * order only breaks ties. No render can tell the two orders apart, so the last case
 * pins the ORDER structurally. It keeps the router's stated convention and would
 * matter under a first-match router, but it is a source-shape check, not evidence of
 * reachability.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, type RouteObject } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'
import { productRoutes } from '../product/productRoutes'
import { organizationRoutes } from '../organization/organizationRoutes'
import { PRODUCT_LIST_TEXT } from '../product/productText'
import { productPage } from '../product/testHarness'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const USER = '11111111-1111-1111-1111-111111111111'
const DIRECTORY = [{ id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false }]

/** Business endpoints of the two lanes: an ungranted route must reach none of them. */
const BUSINESS_PATHS = ['/api/v1/products', '/api/v1/organization/sites', '/api/v1/organization/locations']

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function installFetch(granted: readonly string[]): string[] {
  const calls: string[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      calls.push(url)
      if (url.includes('/identity/me'))
        return json({ userId: USER, displayName: 'Kullanıcı A', email: null, allowedCompanyIds: [COMPANY] })
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities'))
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: 'gen-stable',
          capabilities: granted,
        })
      if (url.includes('/products/code-groups')) return json([])
      if (url.includes('/api/v1/products')) return json(productPage({ items: [] }))
      if (url.includes('/organization/sites')) return json([])
      if (url.includes('/organization/locations'))
        return json({ items: [], page: 1, pageSize: 25, totalCount: 0 })
      // Anything else is not what these cases are about; answer it as unavailable
      // rather than throwing, so the heading assertions stay about the ROUTE.
      return json({ code: 'unavailable' }, 503)
    }),
  )
  return calls
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

async function renderShellAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  // The shell itself is up: whatever renders below is the route's own outcome.
  await screen.findByText('Kullanıcı A', undefined, { timeout: 5000 })
}

const notFoundBlock = () => document.querySelector('[data-state="not-found"]')

describe('shell mounts the Product routes: reachable when granted, neutral when not', () => {
  const path = CAPABILITY_ROUTE[CAPABILITY.productItemsList]

  it('a granted snapshot opens the Product list inside the shell', async () => {
    installFetch([CAPABILITY.productItemsList])
    await renderShellAt(path)

    expect(path).toBe('/urun')
    expect(
      await screen.findByRole('heading', { level: 1, name: PRODUCT_LIST_TEXT.title }, { timeout: 5000 }),
      'a granted /urun rendered no Product list — is ...productRoutes still mounted?',
    ).toBeTruthy()
    expect(notFoundBlock()).toBeNull()
  })

  it('an ungranted snapshot gets the neutral not-found block and reads nothing', async () => {
    const calls = installFetch([])
    await renderShellAt(path)

    await waitFor(() => expect(notFoundBlock()).not.toBeNull())
    expect(screen.queryByRole('heading', { level: 1, name: PRODUCT_LIST_TEXT.title })).toBeNull()
    expect(calls.filter((url) => BUSINESS_PATHS.some((p) => url.includes(p)))).toEqual([])
  })
})

describe('shell mounts the Organization routes: reachable when granted, neutral when not', () => {
  const path = CAPABILITY_ROUTE[CAPABILITY.organizationSitesList]

  it('a granted snapshot opens the Organization settings inside the shell', async () => {
    installFetch([CAPABILITY.organizationSitesList])
    await renderShellAt(path)

    expect(path).toBe('/ayarlar/organizasyon')
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Organizasyon' }, { timeout: 5000 }),
      'a granted /ayarlar/organizasyon rendered no settings page — is ...organizationRoutes still mounted?',
    ).toBeTruthy()
    expect(notFoundBlock()).toBeNull()
  })

  it('an ungranted snapshot gets the neutral not-found block and reads nothing', async () => {
    const calls = installFetch([])
    await renderShellAt(path)

    await waitFor(() => expect(notFoundBlock()).not.toBeNull())
    expect(screen.queryByRole('heading', { level: 1, name: 'Organizasyon' })).toBeNull()
    expect(calls.filter((url) => BUSINESS_PATHS.some((p) => url.includes(p)))).toEqual([])
  })
})

describe('shell router source shape (not a reachability check)', () => {
  it('mounts every lane route object, each ahead of the catch-all', () => {
    const children: readonly RouteObject[] = shellRoutes[0]?.children ?? []
    const catchAll = children.findIndex((route) => route.path === '*')
    expect(catchAll, 'the catch-all is gone').toBeGreaterThanOrEqual(0)
    expect(catchAll, 'the catch-all is no longer the last child').toBe(children.length - 1)

    for (const [lane, routes] of [
      ['Product', productRoutes],
      ['Organization', organizationRoutes],
    ] as const) {
      expect(routes.length, `${lane} declares no routes`).toBeGreaterThan(0)
      for (const route of routes) {
        const at = children.indexOf(route)
        expect(at, `${lane} route ${String(route.path)} is not mounted`).toBeGreaterThanOrEqual(0)
        expect(at, `${lane} route ${String(route.path)} sits behind the catch-all`).toBeLessThan(catchAll)
      }
    }
  })
})
