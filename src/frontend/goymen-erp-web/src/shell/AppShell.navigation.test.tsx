import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from './ShellProvider'
import { AppShell } from './AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from './api'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'
import { PRICE_LIST_CAPABILITY, PRICE_LIST_ROUTE } from '../product/priceLists/capabilities'
import { PRICE_LIST_TEXT } from '../product/priceLists/priceListText'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const USER = '11111111-1111-1111-1111-111111111111'

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

function installFetch(granted: readonly string[]) {
  vi.stubGlobal('navigator', { onLine: true })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url.includes('/identity/me')) {
      return json({
        userId: USER,
        displayName: 'Kullanici A',
        email: null,
        allowedCompanyIds: [COMPANY],
      })
    }
    if (url.includes('/organization/companies')) {
      return json([{ id: COMPANY, code: 'GDC', legalName: 'Goymen A', isPlaceholder: false }])
    }
    if (url.includes('/shell/capabilities')) {
      return json({
        contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION,
        generation: 'gen-navigation',
        capabilities: granted,
      })
    }
    throw new Error(`unexpected fetch ${url}`)
  }))
}

async function renderShell(path: string, granted: readonly string[]) {
  installFetch(granted)
  const router = createMemoryRouter([
    {
      path: '/',
      element: <AppShell />,
      children: [
        { path: CAPABILITY_ROUTE[CAPABILITY.salesDraftsList].slice(1), element: <h1>Siparis listesi</h1> },
        { path: CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate].slice(1), element: <h1>Yeni siparis</h1> },
        { path: PRICE_LIST_ROUTE.list.slice(1), element: <h1>{PRICE_LIST_TEXT.listHeading}</h1> },
      ],
    },
  ], { initialEntries: [path] })
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
  })
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  await screen.findByText('Kullanici A', undefined, { timeout: 5000 })
  return screen.getByRole('navigation', { name: 'Modül navigasyonu' })
}

afterEach(() => {
  cleanup()
  try {
    window.localStorage?.clear()
  } catch {
    // The Node test host may not expose storage; the shell does not require it for these cases.
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AppShell navigation reachability and active leaf', () => {
  it('keeps Sales create reachable but out of the sidebar, with the list leaf inactive', async () => {
    const navigation = await renderShell(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate], [
      CAPABILITY.salesDraftsList,
      CAPABILITY.salesDraftsCreate,
    ])

    const list = within(navigation).getByRole('link', {
      name: CAPABILITY_NAV_LABEL[CAPABILITY.salesDraftsList],
    })
    expect(list.getAttribute('aria-current')).toBeNull()
    expect(screen.getByRole('heading', { level: 1, name: 'Yeni siparis' })).toBeTruthy()
    expect(
      within(navigation).queryAllByRole('link')
        .some((link) => link.getAttribute('href') === CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate]),
    ).toBe(false)
    expect(
      within(navigation).getAllByRole('link')
        .filter((link) => link.getAttribute('aria-current') === 'page'),
    ).toEqual([])
  })

  it('publishes the existing price-list owner capability as one exact, authorized menu path', async () => {
    const navigation = await renderShell(PRICE_LIST_ROUTE.list, [PRICE_LIST_CAPABILITY.list])

    const link = within(navigation).getByRole('link', { name: PRICE_LIST_TEXT.listHeading })
    expect(link.getAttribute('href')).toBe(PRICE_LIST_ROUTE.list)
    expect(link.getAttribute('aria-current')).toBe('page')
  })

  it('does not disclose the price-list menu path without its own capability', async () => {
    const navigation = await renderShell('/', [])
    expect(within(navigation).queryByRole('link', { name: PRICE_LIST_TEXT.listHeading })).toBeNull()
  })
})
