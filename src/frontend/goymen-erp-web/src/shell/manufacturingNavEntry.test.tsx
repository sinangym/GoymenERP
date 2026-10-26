/**
 * MFG-02 is reachable from the sidebar — the LINK, not the label it would use.
 *
 * Why this file exists, stated plainly because the gap it closes was invisible for
 * several slices: `manufacturingOrdersList` was a recognised capability with a mounted
 * route and the nav label 'Üretim emirleri', and `manufacturingMountDrift` asserted that
 * label — but the capability was absent from `NAV_CAPABILITIES`, and nothing in the tree
 * linked to `/uretim/emirler`. Every test asserted the label, which existed; none asserted
 * the link, which did not. So a delivered screen could only be opened by typing its URL,
 * and MFG-01's detail sat behind a list nobody could reach (found reviewing #363, P3-3).
 *
 * K5 (Sinan, 20 September 2026) then settled it as a user decision rather than our
 * finding: the sidebar carries modules and lists, screens reachable from nowhere are
 * linked from their module or list, and the MFG list is the example the decision names.
 *
 * The cases below render the real `shellRoutes` and the real `AppShell`, so they fail if
 * the entry is dropped from `NAV_CAPABILITIES`, if the route stops being mounted, or if
 * the link stops pointing at the list. `navEntryPointInvariant` guards the two TABLES
 * agreeing; this guards that a person can actually get there.
 *
 * K5 keeps creation a single authorised action inside its list, so there is deliberately
 * no sidebar create entry for Manufacturing to assert — the last case pins that absence.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { shellRoutes } from './router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'
import { ORDER_LIST_TEXT } from '../manufacturing/manufacturingText'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const USER = '11111111-1111-1111-1111-111111111111'
const DIRECTORY = [{ id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false }]

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function installFetch(granted: readonly string[]): string[] {
  const calls: string[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      calls.push(url)
      if (url.includes('/identity/me'))
        return json({ userId: USER, displayName: 'Kullanıcı A', email: null, allowedCompanyIds: [COMPANY], authenticationSessionBinding: null })
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities'))
        return json({
          contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
          generation: 'gen-stable',
          capabilities: granted,
        })
      // An empty page is enough: these cases are about REACHING the screen, not its rows.
      if (url.includes('/manufacturing/orders')) return json({ orders: [], nextCursor: null })
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

async function renderShell(granted: readonly string[], path = '/'): Promise<string[]> {
  const calls = installFetch(granted)
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000 } } })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  await screen.findByText('Kullanıcı A', undefined, { timeout: 5000 })
  return calls
}

const navigation = () => screen.getByRole('navigation', { name: 'Modül navigasyonu' })

describe('MFG-02 is reachable from the sidebar (K5)', () => {
  it('renders a nav link to the order list and opens the screen when it is clicked', async () => {
    await renderShell([CAPABILITY.manufacturingOrdersList])

    const link = await within(navigation()).findByRole(
      'link',
      { name: CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersList] },
      { timeout: 5000 },
    )
    // The LINK, and where it points — the assertion that was missing.
    expect(link.getAttribute('href')).toBe(CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersList])
    expect(CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersList]).toBe('/uretim/emirler')

    // …and that following it actually lands on MFG-02 rather than the neutral not-found block.
    await userEvent.click(link)
    expect(
      await screen.findByRole('heading', { level: 1, name: ORDER_LIST_TEXT.heading }, { timeout: 5000 }),
      'the sidebar link did not open the order list — is the route still mounted?',
    ).toBeTruthy()
    expect(document.querySelector('[data-state="not-found"]')).toBeNull()
  })

  it('offers no link to an ungranted actor, and the screen itself reads nothing at its own URL', async () => {
    // Started AT the list route on purpose (#368 review, SİPARİŞ P3). Rendering the home page and
    // then asserting "no orders were read" is true by CONSTRUCTION — the list never mounts there, so
    // the assertion documents an intent it cannot fail on. Opening `/uretim/emirler` directly is the
    // case that matters anyway: it is the one way an ungranted actor can still reach the screen, and
    // it is exactly how MFG-02 was reachable before the sidebar entry existed.
    const calls = await renderShell([], CAPABILITY_ROUTE[CAPABILITY.manufacturingOrdersList])

    // The route closes onto the neutral not-found block: denied and non-existent look the same.
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(
      screen.queryByRole('heading', { level: 1, name: ORDER_LIST_TEXT.heading }),
      'an ungranted actor reached the order list heading by typing its URL',
    ).toBeNull()
    // …and nothing was read on its behalf. This CAN fail now: the screen was mounted at its own path,
    // so a route that skipped its capability gate would issue the read and turn this red.
    expect(
      calls.filter((url) => url.includes('/manufacturing/orders')),
      'an ungranted actor caused a Manufacturing read',
    ).toEqual([])
    // The sidebar offers nothing either.
    expect(
      within(navigation()).queryByRole('link', {
        name: CAPABILITY_NAV_LABEL[CAPABILITY.manufacturingOrdersList],
      }),
    ).toBeNull()
  })

  it('offers no second, parallel create entry: creation belongs inside the list (K5)', async () => {
    await renderShell([CAPABILITY.manufacturingOrdersList])

    // Exactly one Manufacturing link in the sidebar. K5 forbids a parallel sidebar create
    // path beside the list; if Manufacturing ever gains a create capability, it goes in the
    // list's own primary action, and this case is what refuses the sidebar copy.
    const manufacturingLinks = within(navigation())
      .getAllByRole('link')
      .filter((element) => element.getAttribute('href')?.startsWith('/uretim'))
    expect(
      manufacturingLinks,
      'K5 (Sinan, 20 September 2026): creation is a single authorised action INSIDE its list, so the ' +
        'sidebar carries one Manufacturing entry — the list — and never a second create link beside ' +
        'it. If Manufacturing legitimately gained another WORK LIST, update this count and this ' +
        'message together; if a create entry appeared, it belongs in the list instead.',
    ).toHaveLength(1)
  })
})
