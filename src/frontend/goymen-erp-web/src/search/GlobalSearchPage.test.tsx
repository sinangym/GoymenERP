/**
 * AT-002 — the global search in the real shell (AppShell, ShellProvider, CapabilityRoute) with a stubbed transport:
 * type in the box → grouped results → open one → Back returns to the same results (the term is in the URL) → the reopened
 * page shows the same. Only the groups the server answered are shown; a result opens its owner screen only with that
 * screen's grant; the search is not offered (and its route reads nothing) without a searchable list grant; no money word
 * reaches the screen. The box is also placed in the harness layout because its toolbar mount is the shell owner's step;
 * once the toolbar carries it too, either copy navigates the same way, so the tests use the first.
 * Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Outlet, RouterProvider, createMemoryRouter, useParams } from 'react-router'
import { QueryClient } from '@tanstack/react-query'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import type { CapabilityId } from '../shell/capabilities'
import { GlobalSearchBox } from './GlobalSearchBox'
import { searchRoutes } from './searchRoutes'
import { SEARCH_KIND } from './searchContract'
import { SEARCH_TEXT } from './searchText'

const COMPANY_A = '99999999-9999-9999-9999-999999999999'
const PARTY = '11111111-1111-1111-1111-111111111111'
const ORDER = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const CARI_LIST = 'cari.parties.list' as CapabilityId
const CARI_DETAIL = 'cari.parties.detail' as CapabilityId
const SALES_LIST = 'sales.drafts.list' as CapabilityId
const SALES_DETAIL = 'sales.drafts.detail' as CapabilityId
const MONEY_WORDS = ['₺', 'fiyat', 'Fiyat', 'price', 'iskonto', 'tutar', 'Tutar']

const IDENTITY = { userId: '77777777-7777-7777-7777-777777777777', displayName: 'Arama kullanıcısı', email: null, allowedCompanyIds: [COMPANY_A] }
const DIRECTORY = [{ id: COMPANY_A, code: 'GDC', legalName: 'Göymen A', isPlaceholder: true }]

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const answer = (query: string) => ({
  query,
  groups: [
    {
      kind: SEARCH_KIND.party,
      items: [{ id: PARTY, code: 'CAR-000001', name: 'Zeta Çelik A.Ş.', secondaryName: 'Zeta', state: 'Active', date: null }],
      hasMore: true,
    },
    {
      kind: SEARCH_KIND.salesOrder,
      items: [{ id: ORDER, code: 'SO-2026-000001', name: null, secondaryName: null, state: 'Approved', date: '2026-09-28' }],
      hasMore: true,
    },
  ],
})

function installFetch(capabilities: readonly string[], onSearch: (url: string) => Response | Promise<Response> = (url) => json(answer(new URL(url, 'http://x').searchParams.get('q') ?? ''))) {
  const searches: { url: string; companyId: string | null }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      const headers = (init?.headers ?? {}) as Record<string, string>
      if (url.includes('/identity/me')) return json(IDENTITY)
      if (url.includes('/organization/companies')) return json(DIRECTORY)
      if (url.includes('/shell/capabilities')) {
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities })
      }
      if (url.includes('/search?')) {
        searches.push({ url, companyId: headers['X-Company-Id'] ?? null })
        return onSearch(url)
      }
      throw new Error(`unexpected fetch: ${url}`)
    }),
  )
  return { searches }
}

function OwnerStub({ label }: { readonly label: string }) {
  const params = useParams()
  return <p data-testid="owner-screen">{`${label} ${Object.values(params).join(',')}`}</p>
}

/** The harness layout: the real shell, with the box above its outlet (the toolbar mount is the shell owner's step). */
function renderApp(initialPath: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, refetchOnWindowFocus: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: <AppShell />,
        children: [
          {
            element: (
              <>
                <GlobalSearchBox />
                <Outlet />
              </>
            ),
            children: [
              { index: true, element: <p>Ana sayfa</p> },
              ...searchRoutes,
              // Behind the real entry gate, as the owner routes are in the app: leaving and coming Back are route entries.
              { path: 'cari', element: <CapabilityRoute capability={CARI_LIST}><OwnerStub label="Cari listesi" /></CapabilityRoute> },
              { path: 'cari/:partyId', element: <CapabilityRoute capability={CARI_DETAIL}><OwnerStub label="Cari detay" /></CapabilityRoute> },
              { path: 'satis/siparisler/:draftId', element: <CapabilityRoute capability={SALES_DETAIL}><OwnerStub label="Sipariş detay" /></CapabilityRoute> },
            ],
          },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  )
  render(
    <ShellProvider queryClient={queryClient}>
      <RouterProvider router={router} />
    </ShellProvider>,
  )
  return router
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AT-002 global search', () => {
  it('type → grouped results → open one → Back returns to the same results, read again under the same company', async () => {
    // The FIRST answer comes at once; every later one waits for the test, so the Back step can prove the page shows no
    // earlier answer while the entry's own fresh read is in flight (#146/#148).
    let held: ((response: Response) => void) | null = null
    let reads = 0
    const { searches } = installFetch([CARI_LIST, CARI_DETAIL, SALES_LIST], (url) => {
      reads += 1
      const body = json(answer(new URL(url, 'http://x').searchParams.get('q') ?? ''))
      return reads === 1 ? body : new Promise<Response>((resolve) => { held = () => resolve(body) })
    })
    const router = renderApp('/')

    await userEvent.type((await screen.findAllByLabelText(SEARCH_TEXT.boxLabel, { selector: 'input' }))[0], '  Zeta ')
    await userEvent.click(screen.getAllByRole('button', { name: SEARCH_TEXT.boxSubmit })[0])

    const party = await screen.findByTestId(`global-search-group-${SEARCH_KIND.party}`)
    expect(router.state.location.pathname + router.state.location.search).toBe('/arama?q=Zeta')
    expect(searches.map((call) => call.url)).toEqual(['/api/v1/search?q=Zeta'])
    expect(searches[0].companyId).toBe(COMPANY_A)
    // The Cari result opens its owner detail (the actor holds it); the Sales result is text (no detail grant).
    expect(within(party).getByRole('link', { name: /CAR-000001/ }).getAttribute('href')).toBe(`/cari/${PARTY}`)
    expect(within(party).getByRole('link', { name: SEARCH_TEXT.moreInList }).getAttribute('href')).toBe('/cari?q=Zeta')
    const sales = screen.getByTestId(`global-search-group-${SEARCH_KIND.salesOrder}`)
    expect(within(sales).queryByRole('link')).toBeNull()
    expect(within(sales).getByText('SO-2026-000001')).toBeTruthy()
    expect(within(sales).getByText('Onaylı')).toBeTruthy()
    expect(within(sales).getByText('28.09.2026')).toBeTruthy()
    expect(within(sales).getByText(new RegExp(SEARCH_TEXT.narrow))).toBeTruthy()
    // No group the server did not send, no count, no money word.
    expect(screen.queryByTestId(`global-search-group-${SEARCH_KIND.product}`)).toBeNull()
    for (const word of MONEY_WORDS) expect(document.body.textContent).not.toContain(word)

    await userEvent.click(within(party).getByRole('link', { name: /CAR-000001/ }))
    expect((await screen.findByTestId('owner-screen')).textContent).toBe(`Cari detay ${PARTY}`)

    await router.navigate(-1)
    // Back is a new route entry: a FRESH read is sent, and until it answers no earlier answer is shown.
    await waitFor(() => expect(searches.map((call) => call.url)).toEqual(['/api/v1/search?q=Zeta', '/api/v1/search?q=Zeta']))
    expect(router.state.location.search).toBe('?q=Zeta')
    expect(screen.queryByTestId(`global-search-group-${SEARCH_KIND.party}`)).toBeNull()
    expect(document.body.textContent).not.toContain('CAR-000001')
    ;(held as unknown as (response: Response) => void)(json(null))
    await screen.findByTestId(`global-search-group-${SEARCH_KIND.party}`)
    expect((screen.getByLabelText(SEARCH_TEXT.pageLabel, { selector: 'input' }) as HTMLInputElement).value).toBe('Zeta')
    expect(within(screen.getByTestId(`global-search-group-${SEARCH_KIND.party}`)).getByText('Zeta Çelik A.Ş.', { exact: false })).toBeTruthy()
  })

  it('opens a sales order with the Sales detail grant', async () => {
    installFetch([SALES_LIST, SALES_DETAIL])
    renderApp('/arama?q=SO-2026')
    const sales = await screen.findByTestId(`global-search-group-${SEARCH_KIND.salesOrder}`)
    expect(within(sales).getByRole('link', { name: /SO-2026-000001/ }).getAttribute('href')).toBe(`/satis/siparisler/${ORDER}`)
    // Without the Cari list grant the Cari "more" link is not offered even if a Cari group came back.
    expect(within(screen.getByTestId(`global-search-group-${SEARCH_KIND.party}`)).queryByRole('link', { name: SEARCH_TEXT.moreInList })).toBeNull()
  })

  it('a new search from the page is a new history entry', async () => {
    const { searches } = installFetch([CARI_LIST])
    const router = renderApp('/arama?q=zeta')
    await screen.findByTestId(`global-search-group-${SEARCH_KIND.party}`)
    const input = screen.getByLabelText(SEARCH_TEXT.pageLabel, { selector: 'input' })
    await userEvent.clear(input)
    await userEvent.type(input, 'ortak')
    await userEvent.click(screen.getByRole('button', { name: SEARCH_TEXT.submit }))
    await waitFor(() => expect(router.state.location.search).toBe('?q=ortak'))
    await waitFor(() => expect(searches.map((call) => call.url).at(-1)).toBe('/api/v1/search?q=ortak'))
    await router.navigate(-1)
    await waitFor(() => expect(router.state.location.search).toBe('?q=zeta'))
  })

  it('an invalid term is explained and nothing is sent', async () => {
    const { searches } = installFetch([CARI_LIST])
    const router = renderApp('/')
    await userEvent.type((await screen.findAllByLabelText(SEARCH_TEXT.boxLabel, { selector: 'input' }))[0], 'a')
    await userEvent.click(screen.getAllByRole('button', { name: SEARCH_TEXT.boxSubmit })[0])
    expect((await screen.findAllByRole('alert'))[0].textContent).toBe(SEARCH_TEXT.termInvalid)
    expect(router.state.location.pathname).toBe('/')
    expect(searches).toEqual([])
  })

  it('no readable kind reads as no result', async () => {
    installFetch([CARI_LIST], (url) => json({ query: new URL(url, 'http://x').searchParams.get('q'), groups: [] }))
    renderApp('/arama?q=zeta')
    expect((await screen.findByTestId('global-search-no-results')).textContent).toContain(SEARCH_TEXT.noResults)
  })

  it('without any searchable list grant the box is not offered and the route reads nothing', async () => {
    const { searches } = installFetch(['logistics.instructions.list'])
    renderApp('/arama?q=zeta')
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryAllByLabelText(SEARCH_TEXT.boxLabel, { selector: 'input' })).toEqual([])
    expect(searches).toEqual([])
  })

  it.each([
    ['a body with a price member', () => json({ ...answer('zeta'), groups: [{ ...answer('zeta').groups[0], items: [{ ...answer('zeta').groups[0].items[0], unitPrice: '10' }] }] }), 'malformed-response'],
    ['a service outage', () => json({ status: 503, code: 'service_unavailable' }, 503), 'service-unavailable'],
  ])('%s shows a failure state, never partial results', async (_, onSearch, kind) => {
    installFetch([CARI_LIST], onSearch)
    renderApp('/arama?q=zeta')
    await waitFor(() => expect(document.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(document.body.textContent).not.toContain('CAR-000001')
    expect(screen.queryByTestId(`global-search-group-${SEARCH_KIND.party}`)).toBeNull()
    expect(screen.queryByTestId('global-search-no-results')).toBeNull()
  })
})
