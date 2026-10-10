import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderListPage } from './PurchaseOrderListPage'
import { PURCHASE_ORDER_DETAIL_TEXT as T } from './purchaseOrderDetailText'

const COMPANY_A = '22222222-2222-4222-8222-222222222222'
const COMPANY_B = '99999999-9999-4999-8999-999999999999'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const ORDER_1 = '11111111-1111-4111-8111-111111111111'
const ORDER_2 = '88888888-8888-4888-8888-888888888888'
const ORDER_3 = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const PARTY_1 = '44444444-4444-4444-8444-444444444444'
const PARTY_2 = '66666666-6666-4666-8666-666666666666'
const ACCOUNT = '55555555-5555-4555-8555-555555555555'
const LONG_LEGAL = 'KARADENİZ ÇELİK DÖKÜM MAKİNA İMALAT SANAYİ VE DIŞ TİCARET ANONİM ŞİRKETİ GEBZE ORGANİZE SANAYİ BÖLGESİ ŞUBESİ'

const scenario = vi.hoisted(() => ({
  companyId: '22222222-2222-4222-8222-222222222222',
  namespace: 'ctx-a',
  capabilities: [] as string[],
  report: (() => {}) as (status: number, snapshot: unknown) => void,
}))

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    phase: 'idle',
    pendingTarget: null,
    sessionEpoch: 1,
    routeEntryGeneration: 1,
    activeContext: {
      userId: ACTOR, companyId: scenario.companyId, scopeMode: 'single',
      capabilityGeneration: 'published-v1', capabilities: scenario.capabilities,
    },
  }),
}))

vi.mock('../shell/shellState', async () => {
  const actual = await vi.importActual<typeof import('../shell/shellState')>('../shell/shellState')
  return { ...actual, contextKey: () => [scenario.namespace], contextKeyString: () => scenario.namespace }
})

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: (status: number, snapshot: unknown) => scenario.report(status, snapshot) }),
}))

/** One order as GET list answers it (#661 `PurchaseOrderReadSummaryViewV1`). */
function order(id: string, companyId: string, supplierDisplay: unknown, over: Record<string, unknown> = {}) {
  return {
    id, companyId, status: 'Draft', version: '1', supplierPartyId: PARTY_1, companyAccountId: ACCOUNT,
    sourceRequestId: null, effectiveOn: '2026-09-22', createdAtUtc: '2026-09-22T10:00:00Z',
    updatedAtUtc: '2026-09-22T10:00:00Z', lineCount: 2,
    ...(supplierDisplay === undefined ? {} : { supplierDisplay }),
    ...over,
  }
}

const long = { partyId: PARTY_1, code: 'CAR-000042', legalName: LONG_LEGAL, tradeName: 'Karadeniz Döküm' }
const short = { partyId: PARTY_2, code: 'CAR-000007', legalName: 'Kısa Ltd.', tradeName: null }

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function installFetch(answer: (url: URL, companyId: string | null) => Response | Promise<Response>) {
  const calls: { readonly url: URL; readonly companyId: string | null }[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(typeof input === 'string' ? input : input.toString(), 'http://local')
    const headers = new Headers(init?.headers)
    const companyId = headers.get('X-Company-Id')
    calls.push({ url, companyId })
    return answer(url, companyId)
  }))
  return calls
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const element = () => <QueryClientProvider client={client}><MemoryRouter><PurchaseOrderListPage /></MemoryRouter></QueryClientProvider>
  const view = render(element())
  return { rerender: () => view.rerender(element()) }
}

const supplierCell = (orderId: string) => screen.getByTestId(`purchase-order-supplier-${orderId}`)

beforeEach(() => {
  scenario.companyId = COMPANY_A
  scenario.namespace = 'ctx-a'
  scenario.capabilities = ['purchasing.orders.list']
  scenario.report = () => {}
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => vi.unstubAllGlobals())

describe('PurchaseOrderListPage record-bound supplier identity (#661)', () => {
  it('shows the supplier code and the whole legal name instead of a shortened account id', async () => {
    installFetch(() => json({ items: [order(ORDER_1, COMPANY_A, long), order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2 })], nextCursor: null }))
    mount()
    const table = await screen.findByRole('table')
    expect([...table.querySelectorAll('thead th')].map((th) => th.textContent)).toEqual([
      'Sipariş', 'Durum', T.supplierColumn, 'İş tarihi', 'Satır', 'Güncellendi',
    ])
    const cell = supplierCell(ORDER_1)
    expect(cell.textContent).toBe(`CAR-000042 — ${LONG_LEGAL}${T.supplierTradeName}: Karadeniz Döküm`)
    // The long name may wrap; it is never clipped or kept on one line.
    expect(cell.style.whiteSpace).toBe('normal')
    expect(cell.style.overflowWrap).toBe('anywhere')
    expect(supplierCell(ORDER_2).textContent).toBe('CAR-000007 — Kısa Ltd.')
    // No raw account id is offered as an identity any more.
    expect(table.textContent).not.toContain(ACCOUNT.slice(-8))
    expect(table.textContent).not.toContain(ACCOUNT)
  })

  it('links the supplier to the Cari card only with cari.parties.detail; the order link keeps its own grant', async () => {
    scenario.capabilities = ['purchasing.orders.list', 'cari.parties.detail']
    installFetch(() => json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: null }))
    mount()
    await screen.findByRole('table')
    const links = within(supplierCell(ORDER_1)).getAllByRole('link')
    expect(links).toHaveLength(1)
    expect(links[0].getAttribute('href')).toBe(`/cari/${PARTY_1}`)
    expect(links[0].textContent).toBe(`CAR-000042 — ${LONG_LEGAL}`)
    // Holding the Cari card does not open the order: that link needs purchasing.orders.detail.
    expect(within(screen.getByRole('table')).getAllByRole('link')).toHaveLength(1)
  })

  it('a restricted reader sees the same identity as plain text', async () => {
    scenario.capabilities = ['purchasing.orders.list', 'purchasing.orders.detail', 'cari.parties.list']
    installFetch(() => json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: null }))
    mount()
    await screen.findByRole('table')
    expect(supplierCell(ORDER_1).textContent).toContain(`CAR-000042 — ${LONG_LEGAL}`)
    expect(supplierCell(ORDER_1).querySelector('a')).toBeNull()
  })

  it.each([
    ['without supplierDisplay (a legacy-shaped read)', order(ORDER_1, COMPANY_A, undefined)],
    ['with a supplier bound to another party', order(ORDER_1, COMPANY_A, { ...long, partyId: PARTY_2 })],
    ['with an extra supplier field', order(ORDER_1, COMPANY_A, { ...long, taxNumber: '1' })],
    ['with an empty supplier code', order(ORDER_1, COMPANY_A, { ...long, code: '' })],
  ])('fails closed on a list %s: no rows, no name, no fallback id', async (_name, bad) => {
    installFetch(() => json({ items: [order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2 }), bad], nextCursor: null }))
    mount()
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByText(/Kısa Ltd\./)).toBeNull()
    expect(document.body.textContent).not.toContain(ACCOUNT.slice(-8))
  })

  it('keeps the status filter and the cursor: the same single read per page, identity included', async () => {
    const calls = installFetch((url) => url.searchParams.get('cursor') === 'next-1'
      ? json({ items: [order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2 })], nextCursor: null })
      : json({ items: [order(ORDER_1, COMPANY_A, long, { status: url.searchParams.get('status') ?? 'Draft' })], nextCursor: url.searchParams.get('status') === null ? 'next-1' : null }))
    mount()
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await waitFor(() => expect(supplierCell(ORDER_2).textContent).toBe('CAR-000007 — Kısa Ltd.'))
    expect(supplierCell(ORDER_1).textContent).toContain('CAR-000042')
    // Two pages, two reads — and nothing but the orders endpoint (no Cari lookup, no N+1).
    expect(calls).toHaveLength(2)
    expect(calls.every((call) => call.url.pathname === '/api/v1/purchasing/orders' && call.companyId === COMPANY_A)).toBe(true)

    await userEvent.selectOptions(screen.getByLabelText(/Durum/), 'Approved')
    await waitFor(() => expect(calls.at(-1)?.url.searchParams.get('status')).toBe('Approved'))
    expect(calls.at(-1)?.url.searchParams.get('cursor')).toBeNull()
    await waitFor(() => expect(screen.queryByTestId(`purchase-order-supplier-${ORDER_2}`)).toBeNull())
  })

  it('a failed next page keeps the rows already read and retries the same cursor', async () => {
    let attempt = 0
    const calls = installFetch((url) => {
      if (url.searchParams.get('cursor') !== 'next-1') return json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: 'next-1' })
      attempt += 1
      return attempt === 1 ? json({ code: 'service_unavailable' }, 503) : json({ items: [order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2 })], nextCursor: null })
    })
    mount()
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Aynı sayfayı yeniden dene' }))
    await waitFor(() => expect(supplierCell(ORDER_2).textContent).toBe('CAR-000007 — Kısa Ltd.'))
    expect(supplierCell(ORDER_1).textContent).toContain(LONG_LEGAL)
    expect(calls.filter((call) => call.url.searchParams.get('cursor') === 'next-1')).toHaveLength(2)
  })

  it('a company switch never leaves the previous company’s supplier on screen', async () => {
    scenario.capabilities = ['purchasing.orders.list', 'cari.parties.detail']
    const calls = installFetch((_url, companyId) => companyId === COMPANY_B
      ? json({ items: [order(ORDER_3, COMPANY_B, short, { supplierPartyId: PARTY_2 })], nextCursor: null })
      : json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: null }))
    const view = mount()
    await waitFor(() => expect(supplierCell(ORDER_1).textContent).toContain(LONG_LEGAL))

    scenario.companyId = COMPANY_B
    scenario.namespace = 'ctx-b'
    act(() => view.rerender())
    await waitFor(() => expect(supplierCell(ORDER_3).textContent).toBe('CAR-000007 — Kısa Ltd.'))
    expect(screen.queryByText(new RegExp(LONG_LEGAL))).toBeNull()
    expect(document.querySelector(`a[href="/cari/${PARTY_1}"]`)).toBeNull()
    expect(calls.at(-1)?.companyId).toBe(COMPANY_B)
  })

  it('refuses a page whose orders belong to another company', async () => {
    installFetch(() => json({ items: [order(ORDER_1, COMPANY_B, long)], nextCursor: null }))
    mount()
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText(new RegExp(LONG_LEGAL))).toBeNull()
  })

  it.each([401, 403])('a %i reports to the auth boundary and shows no supplier', async (status) => {
    const reported: number[] = []
    scenario.report = (code) => { reported.push(code) }
    installFetch(() => json({ code: 'refused' }, status))
    mount()
    await waitFor(() => expect(reported).toEqual([status]))
    expect(screen.queryByRole('table')).toBeNull()
    expect(document.body.textContent).not.toContain('CAR-000042')
  })
})
