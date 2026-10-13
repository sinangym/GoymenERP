import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { FINANCE_CAPABILITY, FINANCE_ROUTE, openItemDetailPath } from './capabilities'
import { OpenItemDetailPage, OpenItemListPage } from './OpenItemReadPage'
import { OPEN_ITEM_TEXT as T } from './openItemReadText'
import { CAPABILITY } from '../shell/capabilities'

// Synthetic consumer tests, NOT_ENV: they prove neither real grants nor a real browser, API or SQL.
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const shell = vi.hoisted(() => ({
  capabilities: [] as string[],
  codes: null as ReadonlyMap<string, string> | null,
  state: { activeContext: null as null | { scopeMode: string; companyId: string | null; userId: string }, routeEntryGeneration: 0 },
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', async (original) => ({
  ...await original<typeof import('../shell/shellContext')>(),
  useShellState: () => shell.state,
}))
vi.mock('../shell/shellState', async (original) => ({
  ...await original<typeof import('../shell/shellState')>(),
  hasCapability: (_state: unknown, id: string) => shell.capabilities.includes(id),
  contextKey: (state: typeof shell.state) => state.activeContext === null
    ? null : [state.activeContext.userId, state.activeContext.scopeMode, state.activeContext.companyId ?? 'group', 'g', '0'],
}))
vi.mock('./currencyCodes', async (original) => ({
  ...await original<typeof import('./currencyCodes')>(),
  WithCurrencyCodes: ({ children }: { readonly children: (codes: ReadonlyMap<string, string> | null) => ReactNode }) => <>{children(shell.codes)}</>,
}))

const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '99999999-9999-4999-8999-999999999999'
const CURRENCY = '22222222-2222-4222-8222-222222222222'
const SOURCE = 'bbbbbbbb-0000-4000-8000-000000000002'
const SOURCE_LINE = 'cccccccc-0000-4000-8000-000000000003'
const PARTY = 'dddddddd-0000-4000-8000-000000000004'
const ACCOUNT = 'eeeeeeee-0000-4000-8000-000000000005'
const id = (n: number) => `aaaaaaaa-0000-4000-8000-${String(n).padStart(12, '0')}`
const LIST_URL = '/api/v1/finance/open-items'

const visible = (amount: string) => ({ disclosure: 'Visible', money: { amount, currencyId: CURRENCY } })
const redacted = { disclosure: 'Redacted', money: null }
const openItem = (n: number, patch: Record<string, unknown> = {}) => ({
  id: id(n), companyId: COMPANY, sourceKind: 'official.invoice-installment.v1', sourceId: SOURCE, sourceLineId: SOURCE_LINE,
  sourceVersion: '4', partyId: PARTY, companyAccountId: ACCOUNT, currencyId: CURRENCY, version: '1', dueOn: '2026-10-31',
  status: 'Open', createdAtUtc: '2026-10-13T08:00:00+00:00', initialAmount: visible('1000.51'),
  outstandingAmount: visible('400.50'), ...patch,
})
const hidden = (n: number, patch: Record<string, unknown> = {}) =>
  openItem(n, { status: null, initialAmount: redacted, outstandingAmount: redacted, ...patch })
const page = (items: readonly unknown[], nextCursor: string | null = null, companyId = COMPANY) => ({ companyId, items, nextCursor })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

interface Call { readonly url: string; readonly method: string; readonly company: string }
function serve(answer: (call: Call, index: number) => Response | Promise<Response>) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    const call = { url, method: init.method ?? 'GET', company: (init.headers as Record<string, string>)['X-Company-Id'] }
    calls.push(call)
    return answer(call, calls.length - 1)
  }))
  return calls
}

function signIn(companyId: string | null = COMPANY, userId = 'actor-a') {
  shell.state = { activeContext: { scopeMode: companyId === null ? 'group' : 'single', companyId, userId }, routeEntryGeneration: 0 }
}

function screens(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const tree = () => <QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path={FINANCE_ROUTE.openItemsList} element={<OpenItemListPage />} />
        <Route path={FINANCE_ROUTE.openItemsDetail} element={<OpenItemDetailPage />} />
      </Routes>
    </MemoryRouter>
  </QueryClientProvider>
  const view = render(tree())
  return { ...view, again: () => view.rerender(tree()) }
}

const rowLink = (n: number) => document.querySelector(`a[href="${openItemDetailPath(id(n))}"]`)
const headers = () => screen.getAllByRole('columnheader').map((cell) => cell.textContent)

beforeEach(() => {
  shell.capabilities = [FINANCE_CAPABILITY.openItemsList, FINANCE_CAPABILITY.openItemsDetail]
  shell.codes = null
  signIn()
  boundary.report.mockClear()
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('FI04 open item list', () => {
  it('shows a metadata-only caller the rows without money, status or anything worked out from them', async () => {
    const calls = serve(() => json(page([hidden(1), hidden(2, { dueOn: null })])))
    const { container } = screens(FINANCE_ROUTE.openItemsList)

    expect(await screen.findByRole('heading', { level: 1, name: T.heading })).toBeTruthy()
    await waitFor(() => expect(rowLink(1)).not.toBeNull())
    expect(headers()).toEqual([T.record, T.sourceKind, T.dueOn, T.createdAt])
    expect(screen.getByText(new RegExp(T.amountsHidden.replace('.', '\\.')))).toBeTruthy()
    expect(screen.queryByText(T.statusOpen, { exact: true })).toBeNull()
    expect(screen.queryByText(T.statusClosed, { exact: true })).toBeNull()
    const html = container.innerHTML
    for (const forbidden of ['1000.51', '1.000,51', '400.50', '0,00', 'Toplam', 'Gecik', 'data-status', 'data-tone']) {
      expect(html).not.toContain(forbidden)
    }
    expect(screen.getAllByText('Onaylı resmî fatura taksiti')).toHaveLength(2)
    expect(screen.getByText('31.10.2026')).toBeTruthy()
    expect(screen.getByText(T.noDueOn)).toBeTruthy()
    // The list names a record by its short reference; the long identities stay out of it.
    for (const identity of [SOURCE, SOURCE_LINE, PARTY, ACCOUNT, CURRENCY]) expect(html).not.toContain(identity)
    expect(calls).toEqual([{ url: LIST_URL, method: 'GET', company: COMPANY }])
  })

  it('shows a caller with the amount authority the stored status and amounts, exactly', async () => {
    shell.codes = new Map([[CURRENCY, 'TRY']])
    serve(() => json(page([openItem(1), openItem(2, { status: 'Closed', outstandingAmount: visible('0.00') })])))
    screens(FINANCE_ROUTE.openItemsList)

    await waitFor(() => expect(rowLink(1)).not.toBeNull())
    expect(headers()).toEqual([T.record, T.sourceKind, T.dueOn, T.status, T.initialAmount, T.outstandingAmount, T.currency, T.createdAt])
    const first = within(rowLink(1)!.closest('tr')!)
    expect(first.getByText(T.statusOpen, { exact: true })).toBeTruthy()
    expect(first.getByText('1.000,51')).toBeTruthy()
    expect(first.getByText('400,50')).toBeTruthy()
    expect(first.getByText('TRY')).toBeTruthy()
    const second = within(rowLink(2)!.closest('tr')!)
    expect(second.getByText(T.statusClosed, { exact: true })).toBeTruthy()
    expect(second.getByText('0,00')).toBeTruthy()
    expect(screen.queryByText(new RegExp(T.amountsHidden.replace('.', '\\.')))).toBeNull()
  })

  it('names a currency only from a real read, and an unknown source kind as the server sent it', async () => {
    serve(() => json(page([openItem(1, { sourceKind: 'future.kind.v9' })])))
    screens(FINANCE_ROUTE.openItemsList)

    await waitFor(() => expect(rowLink(1)).not.toBeNull())
    expect(screen.getByText('future.kind.v9')).toBeTruthy()
    expect(screen.getByText('ref. …22222222')).toBeTruthy()
    expect(screen.queryByText('TRY')).toBeNull()
  })

  it('gives each row one link whose name says which item it opens; without the detail capability it is plain text', async () => {
    serve(() => json(page([hidden(1)])))
    const first = screens(FINANCE_ROUTE.openItemsList)
    const link = await screen.findByRole('link', { name: 'Açık kalem …000000000001'.replace('000000000001', '00000001') + ': Onaylı resmî fatura taksiti, vade 31.10.2026' })
    expect(link.textContent).toBe('…00000001')
    expect(document.querySelectorAll(`a[href^="${FINANCE_ROUTE.openItemsList}/"]`)).toHaveLength(1)
    first.unmount()

    shell.capabilities = [FINANCE_CAPABILITY.openItemsList]
    screens(FINANCE_ROUTE.openItemsList)
    expect(await screen.findByText('…00000001')).toBeTruthy()
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('draws labelled cards with the same single link on a phone', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('1023'), addEventListener: vi.fn(), removeEventListener: vi.fn() }))
    serve(() => json(page([openItem(1), hidden(2)])))
    screens(FINANCE_ROUTE.openItemsList)

    const cards = await screen.findByRole('list', { name: T.tableLabel })
    expect(screen.queryByRole('table')).toBeNull()
    expect(within(cards).getAllByRole('listitem')).toHaveLength(2)
    expect(document.querySelectorAll(`a[href^="${FINANCE_ROUTE.openItemsList}/"]`)).toHaveLength(2)
    const visibleCard = within(rowLink(1)!.closest('li')!)
    expect(visibleCard.getByText(T.initialAmount)).toBeTruthy()
    expect(visibleCard.getByText('1.000,51')).toBeTruthy()
    // A row whose amounts are withheld says so in words; it is never zero or closed.
    const withheldCard = within(rowLink(2)!.closest('li')!)
    expect(withheldCard.getAllByText(T.redacted)).toHaveLength(3)
    expect(withheldCard.queryByText(T.statusClosed)).toBeNull()
  })

  it('appends the next page with the same opaque cursor, drops a repeated row, and ends without a total', async () => {
    const calls = serve((call) => call.url === LIST_URL
      ? json(page([openItem(1), openItem(2)], 'c+1/='))
      : json(page([openItem(2), openItem(3)], null)))
    screens(FINANCE_ROUTE.openItemsList)

    await userEvent.click(await screen.findByRole('button', { name: T.loadMore }))
    await waitFor(() => expect(rowLink(3)).not.toBeNull())
    expect(new URL(calls[1].url, 'http://x').searchParams.get('cursor')).toBe('c+1/=')
    expect(document.querySelectorAll(`a[href^="${FINANCE_ROUTE.openItemsList}/"]`)).toHaveLength(3)
    expect(screen.queryByRole('button', { name: T.loadMore })).toBeNull()
    expect(calls.every((call) => call.method === 'GET')).toBe(true)
  })

  it('keeps the rows when the next page fails and retries the same cursor only when asked', async () => {
    let failed = false
    const calls = serve((call) => {
      if (call.url === LIST_URL) return json(page([openItem(1)], 'cursor-2'))
      if (!failed) { failed = true; throw new TypeError('failed') }
      return json(page([openItem(51)], null))
    })
    screens(FINANCE_ROUTE.openItemsList)

    await userEvent.click(await screen.findByRole('button', { name: T.loadMore }))
    await waitFor(() => expect(document.querySelector('[data-state="network-error"]')).not.toBeNull())
    expect(rowLink(1)).not.toBeNull()
    expect(rowLink(51)).toBeNull()
    expect(calls).toHaveLength(2)

    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(rowLink(51)).not.toBeNull())
    expect(calls[2].url).toBe(calls[1].url)
    expect(document.querySelector('[data-state="network-error"]')).toBeNull()
  })

  it.each([
    [401, 'unauthenticated'], [403, 'forbidden'], [503, 'service-unavailable'], [400, 'malformed-response'],
  ])('shows HTTP %i as its own state with no rows', async (status, state) => {
    serve(() => json({ status }, status))
    screens(FINANCE_ROUTE.openItemsList)

    await waitFor(() => expect(document.querySelector(`[data-state="${state}"]`)).not.toBeNull())
    expect(screen.getByRole('heading', { level: 1, name: T.heading })).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
    expect(boundary.report).toHaveBeenCalledTimes(status === 401 || status === 403 ? 1 : 0)
  })

  it('tells an empty company from a failed read, and a retry reads a fresh first page', async () => {
    const calls = serve((_call, index) => index === 0
      ? json({ status: 503 }, 503)
      : index === 1 ? new Response('{"items":[', { status: 200 }) : json(page([])))
    screens(FINANCE_ROUTE.openItemsList)

    await waitFor(() => expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(calls.map((call) => call.url)).toEqual([LIST_URL, LIST_URL, LIST_URL])
  })

  it('drops the rows at once when the company or the actor changes, and never shows the old context’s late answer', async () => {
    let releaseOld: (response: Response) => void = () => {}
    const calls = serve((call) => {
      if (call.company === OTHER_COMPANY) return json(page([], null, OTHER_COMPANY))
      if (call.url !== LIST_URL) return new Promise<Response>((resolve) => { releaseOld = resolve })
      return json(page([openItem(1)], 'cursor-2'))
    })
    const view = screens(FINANCE_ROUTE.openItemsList)
    await userEvent.click(await screen.findByRole('button', { name: T.loadMore }))
    expect(calls).toHaveLength(2)

    signIn(OTHER_COMPANY)
    view.again()
    expect(rowLink(1)).toBeNull()
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    releaseOld(json(page([openItem(51)], null)))
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(rowLink(51)).toBeNull()
    expect(rowLink(1)).toBeNull()

    signIn(COMPANY, 'actor-b')
    view.again()
    expect(document.querySelector('[data-state="empty-no-data"]')).toBeNull()
    await waitFor(() => expect(rowLink(1)).not.toBeNull())
    expect(calls.at(-1)).toEqual({ url: LIST_URL, method: 'GET', company: COMPANY })
  })

  it('reads nothing without a single committed company', async () => {
    signIn(null)
    const calls = serve(() => json(page([])))
    screens(FINANCE_ROUTE.openItemsList)
    expect(document.querySelector('[data-state="no-authorized-company"]')).not.toBeNull()
    expect(calls).toHaveLength(0)
  })
})

describe('FI04 open item detail', () => {
  it('opens from its row, shows the disclosed facts and keeps the identities under technical details', async () => {
    shell.capabilities.push(CAPABILITY.cariPartiesDetail)
    const calls = serve((call) => call.url === LIST_URL ? json(page([openItem(1)])) : json(openItem(1)))
    screens(FINANCE_ROUTE.openItemsList)

    await userEvent.click(await screen.findByRole('link', { name: /^Açık kalem …00000001/ }))
    expect(await screen.findByRole('heading', { level: 1, name: T.detailHeading })).toBeTruthy()
    await waitFor(() => expect(screen.getByText('1.000,51')).toBeTruthy())
    expect(calls.at(-1)).toEqual({ url: `${LIST_URL}/${id(1)}`, method: 'GET', company: COMPANY })
    expect(screen.getByText(T.statusOpen, { exact: true })).toBeTruthy()
    expect(screen.getByText('400,50')).toBeTruthy()
    expect(screen.getByRole('link', { name: T.openParty }).getAttribute('href')).toBe(`/cari/${PARTY}`)

    const technical = screen.getByText(T.technical).closest('details')!
    expect(technical.open).toBe(false)
    for (const identity of [id(1), SOURCE, SOURCE_LINE, PARTY, ACCOUNT, CURRENCY, 'official.invoice-installment.v1']) {
      expect(within(technical).getByText(identity)).toBeTruthy()
    }
    await userEvent.click(screen.getByRole('link', { name: T.back }))
    expect(await screen.findByRole('heading', { level: 1, name: T.heading })).toBeTruthy()
    await waitFor(() => expect(rowLink(1)).not.toBeNull())
  })

  it('shows a metadata-only caller no money and no status, and offers no party link without Cari’s capability', async () => {
    serve(() => json(hidden(1)))
    const { container } = screens(openItemDetailPath(id(1)))

    expect(await screen.findByText(T.amountsHidden)).toBeTruthy()
    expect(screen.getByText('31.10.2026')).toBeTruthy()
    for (const label of [T.status, T.initialAmount, T.outstandingAmount, T.currency]) expect(screen.queryByText(label, { exact: true })).toBeNull()
    expect(screen.queryByText(T.statusOpen, { exact: true })).toBeNull()
    expect(screen.queryByText(T.statusClosed, { exact: true })).toBeNull()
    expect(container.innerHTML).not.toContain('1000.51')
    expect(container.innerHTML).not.toContain('0,00')
    expect(screen.queryByRole('link', { name: T.openParty })).toBeNull()
  })

  it.each([
    [401, 'unauthenticated'], [403, 'forbidden'], [404, 'not-found'], [503, 'service-unavailable'],
  ])('shows HTTP %i as its own state under the same heading', async (status, state) => {
    serve(() => json({ status }, status))
    screens(openItemDetailPath(id(1)))

    await waitFor(() => expect(document.querySelector(`[data-state="${state}"]`)).not.toBeNull())
    expect(screen.getByRole('heading', { level: 1, name: T.detailHeading })).toBeTruthy()
    expect(screen.queryByText(T.technical)).toBeNull()
    expect(screen.queryAllByRole('button', { name: 'Tekrar dene' })).toHaveLength(status === 503 ? 1 : 0)
  })

  it('refuses an answer for another item and an address that is no item id', async () => {
    const calls = serve(() => json(openItem(2)))
    const first = screens(openItemDetailPath(id(1)))
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText('1.000,51')).toBeNull()
    first.unmount()

    screens(`${FINANCE_ROUTE.openItemsList}/yeni`)
    expect(document.querySelector('[data-state="not-found"]')).not.toBeNull()
    expect(calls).toHaveLength(1)
  })

  it('closes the open detail when the company changes and never shows its late answer', async () => {
    let release: (response: Response) => void = () => {}
    serve((call) => call.company === COMPANY
      ? new Promise<Response>((resolve) => { release = resolve })
      : json({ status: 404 }, 404))
    const view = screens(openItemDetailPath(id(1)))
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())

    signIn(OTHER_COMPANY)
    view.again()
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    release(json(openItem(1)))
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(screen.queryByText('1.000,51')).toBeNull()
    expect(screen.queryByText(T.technical)).toBeNull()
  })
})
