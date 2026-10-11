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
  scopeMode: 'single' as 'single' | 'all',
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
      userId: ACTOR, companyId: scenario.companyId, scopeMode: scenario.scopeMode,
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
  return { rerender: () => view.rerender(element()), unmount: () => view.unmount() }
}

const supplierCell = (orderId: string) => screen.getByTestId(`purchase-order-supplier-${orderId}`)

beforeEach(() => {
  scenario.companyId = COMPANY_A
  scenario.namespace = 'ctx-a'
  scenario.scopeMode = 'single'
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
    // The long name is whole in the cell; that it wraps instead of being cut is the page stylesheet's rule (below).
    expect(cell.getAttribute('style')).toBeNull()
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

/**
 * PURCHASING-DS-LIST-01: the same list on GOY-DS-001 — the shared table on a wide screen, one labelled card per
 * order on a phone, the filter in a labelled field and the continuation on the design-system button. The facts,
 * the reads and the gates are the ones the list already had.
 */
describe('PurchaseOrderListPage on the design system (PURCHASING-DS-LIST-01)', () => {
  const nodeProcess = (globalThis as unknown as {
    readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
  }).process
  const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
  const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
  // Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
  const pageCss = fs
    .readFileSync(path.resolve(nodeProcess.cwd(), 'src/purchasing/PurchaseOrderListPage.module.css'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
  const rule = (selector: string) => {
    const start = pageCss.indexOf(`\n${selector} {`)
    return start < 0 ? '' : pageCss.slice(start, pageCss.indexOf('}', start))
  }

  /** The phone width as the page reads it: `matchMedia('(max-width: 767px)')`, switchable mid-test. */
  function installViewport(phone: boolean) {
    const viewport = { phone }
    const listeners = new Set<() => void>()
    vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
      get matches() { return viewport.phone },
      media: query,
      onchange: null,
      addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
      removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    })))
    return {
      set(next: boolean) {
        viewport.phone = next
        act(() => { for (const listener of [...listeners]) listener() })
      },
    }
  }

  const twoOrders = () => json({
    items: [
      order(ORDER_1, COMPANY_A, long, { lineCount: 12, effectiveOn: '2026-09-05', updatedAtUtc: '2026-09-22T10:15:00Z' }),
      order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2, status: 'Approved', lineCount: 1 }),
    ],
    nextCursor: null,
  })

  it('draws one named table: column headers, one row per order in the server order, every fact of the read', async () => {
    installFetch(twoOrders)
    mount()
    const table = await screen.findByRole('table', { name: 'Şirketin satın alma siparişleri' })
    expect([...table.querySelectorAll('thead th')].every((th) => th.getAttribute('scope') === 'col')).toBe(true)
    const rows = [...table.querySelectorAll('tbody tr')]
    expect(rows.map((row) => row.getAttribute('data-order-id'))).toEqual([ORDER_1, ORDER_2])
    expect(rows.every((row) => row.querySelectorAll('td').length === 6)).toBe(true)
    const first = [...rows[0].querySelectorAll('td')].map((td) => td.textContent)
    expect(first[0]).toBe(`…${ORDER_1.slice(-8)}`)
    expect(first[1]).toBe('Taslak')
    expect(first[3]).toBe('05.09.2026')
    expect(first[4]).toBe('12')
    expect(first[5]).toBe(new Date('2026-09-22T10:15:00Z').toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' }))
    expect(rows[1].querySelectorAll('td')[1].textContent).toBe('Onaylandı')
    // The page is one labelled region under its own heading; the cards are not drawn beside the table.
    const heading = screen.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })
    expect(screen.getByTestId('purchase-order-list').getAttribute('aria-labelledby')).toBe(heading.id)
    expect(screen.queryByTestId('purchase-order-cards')).toBeNull()
    // No amount, tax, currency or total is drawn: the read carries none.
    expect(document.body.textContent).not.toMatch(/Tutar|KDV|Toplam|₺/)
  })

  it('shows the status as text with a tone; the colour is never the only signal', async () => {
    installFetch(twoOrders)
    mount()
    const table = await screen.findByRole('table')
    const tones = [...table.querySelectorAll('tbody tr')].map((row) => {
      const status = row.querySelector('[data-tone]') as HTMLElement
      return [status.textContent, status.getAttribute('data-tone')]
    })
    expect(tones).toEqual([['Taslak', 'neutral'], ['Onaylandı', 'success']])
  })

  it('never reformats a business date the contract refuses: the whole page fails closed instead', async () => {
    installFetch(() => json({ items: [order(ORDER_1, COMPANY_A, long, { effectiveOn: '2026-9-5' })], nextCursor: null }))
    mount()
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(document.body.textContent).not.toContain('2026')
  })

  it('opens an order only with purchasing.orders.detail; without it the same reference is plain text', async () => {
    installFetch(twoOrders)
    mount()
    const table = await screen.findByRole('table')
    expect(within(table).queryAllByRole('link')).toHaveLength(0)
    expect(within(table).getByText(`…${ORDER_1.slice(-8)}`).tagName).toBe('SPAN')
  })

  it('links each order to its own detail with purchasing.orders.detail', async () => {
    scenario.capabilities = ['purchasing.orders.list', 'purchasing.orders.detail']
    installFetch(twoOrders)
    mount()
    const table = await screen.findByRole('table')
    expect(within(table).getAllByRole('link').map((link) => [link.textContent, link.getAttribute('href')])).toEqual([
      [`…${ORDER_1.slice(-8)}`, `/satinalma/siparisler/${ORDER_1}`],
      [`…${ORDER_2.slice(-8)}`, `/satinalma/siparisler/${ORDER_2}`],
    ])
  })

  it('offers the create action as one link, only with purchasing.orders.create', async () => {
    installFetch(twoOrders)
    const withoutGrant = mount()
    await screen.findByRole('table')
    expect(screen.queryByRole('link', { name: 'Yeni' })).toBeNull()
    withoutGrant.unmount()

    scenario.capabilities = ['purchasing.orders.list', 'purchasing.orders.create']
    mount()
    await screen.findByRole('table')
    const create = screen.getByRole('link', { name: 'Yeni' })
    expect(create.getAttribute('href')).toBe('/satinalma/siparisler/yeni')
    // A navigation stays a link: no button is nested in it.
    expect(create.querySelector('button')).toBeNull()
  })

  it('labels the status filter and offers exactly the server\'s states', async () => {
    installFetch(twoOrders)
    mount()
    await screen.findByRole('table')
    const filter = screen.getByLabelText('Durum') as HTMLSelectElement
    expect(filter.tagName).toBe('SELECT')
    expect([...filter.options].map((option) => [option.value, option.textContent])).toEqual([
      ['', 'Tümü'], ['Draft', 'Taslak'], ['Approved', 'Onaylandı'],
    ])
    expect(filter.value).toBe('')
  })

  it('on a phone draws one labelled card per order instead of the table, with the same facts in the same order', async () => {
    installViewport(true)
    scenario.capabilities = ['purchasing.orders.list', 'purchasing.orders.detail', 'cari.parties.detail']
    installFetch(twoOrders)
    mount()
    const list = await screen.findByRole('list', { name: 'Şirketin satın alma siparişleri' })
    expect(screen.queryByRole('table')).toBeNull()
    const cardItems = within(list).getAllByRole('listitem')
    expect(cardItems.map((card) => card.getAttribute('data-order-id'))).toEqual([ORDER_1, ORDER_2])

    const card = cardItems[0]
    expect(within(card).getByRole('link', { name: `…${ORDER_1.slice(-8)}` }).getAttribute('href')).toBe(`/satinalma/siparisler/${ORDER_1}`)
    expect((card.querySelector('[data-tone]') as HTMLElement).textContent).toBe('Taslak')
    // Every fact carries its own label: a card has no column headers to lean on.
    expect(supplierCell(ORDER_1).textContent).toBe(`CAR-000042 — ${LONG_LEGAL}${T.supplierTradeName}: Karadeniz Döküm`)
    expect(card.querySelector('dl > div > dd')?.contains(supplierCell(ORDER_1))).toBe(true)
    expect([...card.querySelectorAll('dl > div')].map((fact) => [fact.querySelector('dt')?.textContent, fact.querySelector('dd')?.textContent]).slice(1)).toEqual([
      ['İş tarihi', '05.09.2026'],
      ['Satır', '12'],
      ['Güncellendi', new Date('2026-09-22T10:15:00Z').toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })],
    ])
    expect([...card.querySelectorAll('dl > div > dt')].map((label) => label.textContent)).toEqual([T.supplierColumn, 'İş tarihi', 'Satır', 'Güncellendi'])
    expect(card.textContent).toContain('Sipariş')
    // The supplier is the same record-bound identity, once, linked under the same Cari grant.
    expect(screen.getAllByTestId(`purchase-order-supplier-${ORDER_1}`)).toHaveLength(1)
    expect(within(supplierCell(ORDER_1)).getByRole('link').getAttribute('href')).toBe(`/cari/${PARTY_1}`)
    expect(supplierCell(ORDER_1).querySelectorAll('[data-supplier-display="record"]')).toHaveLength(1)
  })

  it('on a phone keeps the gates: no order link and no Cari link without their own grants', async () => {
    installViewport(true)
    installFetch(twoOrders)
    mount()
    const list = await screen.findByRole('list', { name: 'Şirketin satın alma siparişleri' })
    expect(within(list).queryAllByRole('link')).toHaveLength(0)
    expect(supplierCell(ORDER_1).textContent).toContain(`CAR-000042 — ${LONG_LEGAL}`)
    expect(screen.queryByRole('link', { name: 'Yeni' })).toBeNull()
  })

  it('switching between table and cards is layout only: the same rows, no new read', async () => {
    const viewport = installViewport(false)
    const calls = installFetch(twoOrders)
    mount()
    await screen.findByRole('table')
    expect(calls).toHaveLength(1)

    viewport.set(true)
    expect(screen.queryByRole('table')).toBeNull()
    expect(within(screen.getByTestId('purchase-order-cards')).getAllByRole('listitem')).toHaveLength(2)
    viewport.set(false)
    expect(screen.getByRole('table').querySelectorAll('tbody tr')).toHaveLength(2)
    expect(screen.queryByTestId('purchase-order-cards')).toBeNull()
    expect(calls).toHaveLength(1)
  })

  it('on a phone continues with the same cursor and appends the next cards', async () => {
    installViewport(true)
    const calls = installFetch((url) => url.searchParams.get('cursor') === 'next-1'
      ? json({ items: [order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2 })], nextCursor: null })
      : json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: 'next-1' }))
    mount()
    await screen.findByTestId('purchase-order-cards')
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await waitFor(() => expect(within(screen.getByTestId('purchase-order-cards')).getAllByRole('listitem')).toHaveLength(2))
    expect(calls.map((call) => call.url.searchParams.get('cursor'))).toEqual([null, 'next-1'])
    // The end of the list: nothing more is offered.
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()
  })

  it('while the next page is on its way the button is busy and a second press sends nothing', async () => {
    let release: (response: Response) => void = () => {}
    const calls = installFetch((url) => url.searchParams.get('cursor') === 'next-1'
      ? new Promise<Response>((resolve) => { release = resolve })
      : json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: 'next-1' }))
    mount()
    await screen.findByRole('table')
    const more = screen.getByRole('button', { name: 'Daha fazla göster' })
    await userEvent.click(more)
    const busy = await screen.findByRole('button', { name: 'Sonraki sayfa yükleniyor…' }) as HTMLButtonElement
    expect(busy.disabled).toBe(true)
    expect(busy.getAttribute('aria-busy')).toBe('true')
    expect(busy.getAttribute('type')).toBe('button')
    await userEvent.click(busy)
    expect(calls.filter((call) => call.url.searchParams.get('cursor') === 'next-1')).toHaveLength(1)
    // The rows already read stay on screen while it loads.
    expect(supplierCell(ORDER_1).textContent).toContain(LONG_LEGAL)

    await act(async () => release(json({ items: [order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2 })], nextCursor: null })))
    await waitFor(() => expect(screen.getByRole('table').querySelectorAll('tbody tr')).toHaveLength(2))
  })

  it('a failed next page says so beside the rows already read and offers only the explicit same-page retry', async () => {
    const calls = installFetch((url) => url.searchParams.get('cursor') === 'next-1'
      ? json({ code: 'service_unavailable' }, 503)
      : json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: 'next-1' }))
    mount()
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await screen.findByRole('button', { name: 'Aynı sayfayı yeniden dene' })
    expect(screen.getByRole('table').querySelectorAll('tbody tr')).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()
    // Nothing was resent on its own.
    expect(calls.filter((call) => call.url.searchParams.get('cursor') === 'next-1')).toHaveLength(1)
  })

  it('a filter change drops the finished list\'s cursor: the filtered list continues with its own', async () => {
    const calls = installFetch((url) => {
      const status = url.searchParams.get('status')
      const cursor = url.searchParams.get('cursor')
      if (status === 'Approved') {
        return cursor === 'approved-2'
          ? json({ items: [order(ORDER_3, COMPANY_A, short, { supplierPartyId: PARTY_2, status: 'Approved' })], nextCursor: null })
          : json({ items: [order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2, status: 'Approved' })], nextCursor: 'approved-2' })
      }
      return cursor === 'next-1'
        ? json({ items: [order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2, status: 'Approved' })], nextCursor: null })
        : json({ items: [order(ORDER_1, COMPANY_A, long)], nextCursor: 'next-1' })
    })
    mount()
    await screen.findByRole('table')
    // Read the unfiltered list to its end: nothing more is offered.
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await waitFor(() => expect(screen.getByRole('table').querySelectorAll('tbody tr')).toHaveLength(2))
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()

    // The filtered list starts over and has a next page of its own.
    await userEvent.selectOptions(screen.getByLabelText('Durum'), 'Approved')
    await waitFor(() => expect(screen.getByRole('table').querySelectorAll('tbody tr')).toHaveLength(1))
    expect(screen.queryByTestId(`purchase-order-supplier-${ORDER_1}`)).toBeNull()
    await userEvent.click(await screen.findByRole('button', { name: 'Daha fazla göster' }))
    await waitFor(() => expect(screen.getByRole('table').querySelectorAll('tbody tr')).toHaveLength(2))
    expect(calls.map((call) => call.url.search)).toEqual(['', '?cursor=next-1', '?status=Approved', '?cursor=approved-2&status=Approved'])
  })

  it('tells an empty company from an empty filter, keeping the filter in reach', async () => {
    installFetch(() => json({ items: [], nextCursor: null }))
    mount()
    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByTestId('purchase-order-cards')).toBeNull()

    await userEvent.selectOptions(screen.getByLabelText('Durum'), 'Approved')
    await waitFor(() => expect(document.querySelector('[data-state="empty-filtered"]')).not.toBeNull())
    expect(document.querySelector('[data-state="empty-no-data"]')).toBeNull()
    expect((screen.getByLabelText('Durum') as HTMLSelectElement).value).toBe('Approved')
  })

  it('shows loading first, then a failed first read with a retry that reads the same list again', async () => {
    let attempt = 0
    let release: (response: Response) => void = () => {}
    const calls = installFetch(() => {
      attempt += 1
      if (attempt === 1) return new Promise<Response>((resolve) => { release = resolve })
      return twoOrders()
    })
    mount()
    await waitFor(() => expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull())
    // The heading and the filter are there before any row is.
    expect(screen.getByRole('heading', { level: 1, name: 'Satın alma siparişleri' })).toBeTruthy()
    expect(screen.getByLabelText('Durum')).toBeTruthy()

    await act(async () => release(json({ code: 'service_unavailable' }, 503)))
    const failed = await waitFor(() => {
      const state = document.querySelector('[data-state]:not([data-state="initial-loading"])')
      expect(state).not.toBeNull()
      return state as HTMLElement
    })
    expect(screen.queryByRole('table')).toBeNull()
    await userEvent.click(within(failed).getByRole('button'))
    await screen.findByRole('table')
    expect(calls).toHaveLength(2)
    expect(calls.every((call) => call.url.pathname === '/api/v1/purchasing/orders' && call.companyId === COMPANY_A)).toBe(true)
  })

  it('without a single committed company it reads nothing and says so', async () => {
    scenario.scopeMode = 'all'
    const calls = installFetch(twoOrders)
    mount()
    await waitFor(() => expect(document.querySelector('[data-state="no-authorized-company"]')).not.toBeNull())
    expect(calls).toHaveLength(0)
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('keeps the supplier wrapping and the page on design-system tokens (page stylesheet)', () => {
    // A long name wraps; no rule of this page cuts text or forces the supplier onto one line.
    expect(rule('.supplier')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(rule('.supplier')).not.toMatch(/nowrap|overflow:\s*hidden/)
    expect(rule('.cardFacts dd')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(pageCss).not.toMatch(/text-overflow|line-clamp/)
    // The cards may not push the page sideways: their grid tracks can shrink below their content.
    expect(rule('.cardFacts')).toMatch(/minmax\(0,\s*1fr\)/)
    // Colours come from tokens only.
    expect(pageCss).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgb\(|hsl\(/)
    // The phone layout switches at the shell's compact width.
    expect(pageCss).toContain('@media (max-width: 767px)')
  })
})

/**
 * PURCHASING-SUPPLIER-FILTER-UI-01: a row's own supplier becomes the list's server filter (`supplierPartyId`,
 * #671). The party comes only from that row's record-bound identity; nothing is filtered on the client; the
 * active filter stays visible and removable; every filter change starts the cursor over; a refused filter is
 * shown as refused, never replaced by the unfiltered list.
 */
describe('PurchaseOrderListPage supplier filter (PURCHASING-SUPPLIER-FILTER-UI-01)', () => {
  const longOrder = (over: Record<string, unknown> = {}) => order(ORDER_1, COMPANY_A, long, over)
  const shortOrder = (over: Record<string, unknown> = {}) => order(ORDER_2, COMPANY_A, short, { supplierPartyId: PARTY_2, ...over })
  const shortOrder3 = (over: Record<string, unknown> = {}) => order(ORDER_3, COMPANY_A, short, { supplierPartyId: PARTY_2, ...over })
  const page = (items: unknown[], nextCursor: string | null = null) => json({ items, nextCursor })
  const queryOf = (url: URL) => Object.fromEntries(url.searchParams.entries())
  const filterButton = (orderId: string) => screen.getByTestId(`purchase-order-filter-supplier-${orderId}`)
  const chip = () => screen.queryByTestId('purchase-order-supplier-filter')
  const clearButton = () => screen.getByRole('button', { name: 'Tedarikçi filtresini kaldır' })
  const rowIds = () => [...document.querySelectorAll('tbody tr[data-order-id]')].map((row) => row.getAttribute('data-order-id'))

  /** Both suppliers unfiltered; only the asked supplier's orders when the server is given `supplierPartyId`. */
  function serveBySupplier() {
    return installFetch((url) => {
      const supplierPartyId = url.searchParams.get('supplierPartyId')
      if (supplierPartyId === PARTY_2) return page([shortOrder()])
      if (supplierPartyId === PARTY_1) return page([longOrder()])
      return page([longOrder(), shortOrder()])
    })
  }

  it('offers each row its own supplier as a filter, named for that supplier', async () => {
    serveBySupplier()
    mount()
    await screen.findByRole('table')
    expect(chip()).toBeNull()
    const first = filterButton(ORDER_1)
    expect(first.tagName).toBe('BUTTON')
    expect(first.getAttribute('type')).toBe('button')
    // What is drawn is the one word; the supplier is in the accessible name, so fifty rows are fifty different buttons.
    expect(screen.getByRole('button', { name: `Filtrele: CAR-000042 — ${LONG_LEGAL}` })).toBe(first)
    expect(screen.getByRole('button', { name: 'Filtrele: CAR-000007 — Kısa Ltd.' })).toBe(filterButton(ORDER_2))
    // The identity element still holds the supplier and nothing else.
    expect(supplierCell(ORDER_2).textContent).toBe('CAR-000007 — Kısa Ltd.')
    expect(supplierCell(ORDER_2).contains(filterButton(ORDER_2))).toBe(false)
  })

  it('applies the row’s supplier as the server’s supplierPartyId query and shows the active filter', async () => {
    const calls = serveBySupplier()
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2]))
    // One filtered read, on the same endpoint and company — the party is the row's own, nothing was looked up.
    expect(calls.map((call) => queryOf(call.url))).toEqual([{}, { supplierPartyId: PARTY_2 }])
    expect(calls.every((call) => call.url.pathname === '/api/v1/purchasing/orders' && call.companyId === COMPANY_A)).toBe(true)
    const active = chip() as HTMLElement
    expect(active.getAttribute('role')).toBe('group')
    expect(active.getAttribute('aria-label')).toBe('Tedarikçi filtresi')
    expect(active.textContent).toBe('Tedarikçi filtresiCAR-000007 — Kısa Ltd.Tedarikçi filtresini kaldır')
    // The remove control says which filter it removes, and takes the focus the vanished row button held.
    const valueId = clearButton().getAttribute('aria-describedby') as string
    expect(document.getElementById(valueId)?.textContent).toBe('CAR-000007 — Kısa Ltd.')
    expect(document.activeElement).toBe(clearButton())
    // Every row is that supplier's now, so the action is not repeated on them.
    expect(screen.queryByTestId(`purchase-order-filter-supplier-${ORDER_2}`)).toBeNull()
    expect(screen.queryByRole('button', { name: /^Filtrele/ })).toBeNull()
  })

  it('removes the filter with one unfiltered read and gives the row actions back', async () => {
    const calls = serveBySupplier()
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_1))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1]))
    await userEvent.click(clearButton())
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1, ORDER_2]))
    expect(chip()).toBeNull()
    expect(queryOf(calls.at(-1)!.url)).toEqual({})
    expect(calls.filter((call) => call.url.searchParams.has('supplierPartyId'))).toHaveLength(1)
    expect(filterButton(ORDER_1)).toBeTruthy()
    expect(filterButton(ORDER_2)).toBeTruthy()
    // The remove control is gone; focus lands on the filter that is still there.
    expect(document.activeElement).toBe(screen.getByLabelText('Durum'))
  })

  it('sends the supplier together with the status, and keeps it when the status changes', async () => {
    const calls = installFetch((url) => page(url.searchParams.get('supplierPartyId') === PARTY_2
      ? [shortOrder({ status: url.searchParams.get('status') ?? 'Draft' })]
      : [longOrder({ status: url.searchParams.get('status') ?? 'Draft' }), shortOrder({ status: url.searchParams.get('status') ?? 'Draft' })]))
    mount()
    await screen.findByRole('table')
    await userEvent.selectOptions(screen.getByLabelText('Durum'), 'Approved')
    await waitFor(() => expect(queryOf(calls.at(-1)!.url)).toEqual({ status: 'Approved' }))
    await userEvent.click(await screen.findByTestId(`purchase-order-filter-supplier-${ORDER_2}`))
    await waitFor(() => expect(queryOf(calls.at(-1)!.url)).toEqual({ status: 'Approved', supplierPartyId: PARTY_2 }))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2]))
    await userEvent.selectOptions(screen.getByLabelText('Durum'), 'Draft')
    await waitFor(() => expect(queryOf(calls.at(-1)!.url)).toEqual({ status: 'Draft', supplierPartyId: PARTY_2 }))
    expect(chip()?.textContent).toContain('CAR-000007 — Kısa Ltd.')
    // Removing the supplier keeps the status the user chose.
    await userEvent.click(clearButton())
    await waitFor(() => expect(queryOf(calls.at(-1)!.url)).toEqual({ status: 'Draft' }))
    expect((screen.getByLabelText('Durum') as HTMLSelectElement).value).toBe('Draft')
  })

  it('starts the cursor over when the filter is applied and again when it is removed', async () => {
    const calls = installFetch((url) => {
      const cursor = url.searchParams.get('cursor')
      if (url.searchParams.get('supplierPartyId') === PARTY_2) {
        return cursor === 'supplier-2' ? page([shortOrder3()]) : page([shortOrder()], 'supplier-2')
      }
      return cursor === 'next-1' ? page([shortOrder()]) : page([longOrder()], 'next-1')
    })
    mount()
    await screen.findByRole('table')
    // The unfiltered list, read to its end.
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1, ORDER_2]))
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()

    // APPLY: no cursor is carried in; the pages already read are dropped; the filtered list has its own next page.
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2]))
    await userEvent.click(await screen.findByRole('button', { name: 'Daha fazla göster' }))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2, ORDER_3]))
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()

    // REMOVE: back to the first unfiltered page only, its continuation offered from the start.
    await userEvent.click(clearButton())
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1]))
    await userEvent.click(await screen.findByRole('button', { name: 'Daha fazla göster' }))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1, ORDER_2]))
    expect(calls.map((call) => queryOf(call.url))).toEqual([
      {},
      { cursor: 'next-1' },
      { supplierPartyId: PARTY_2 },
      { cursor: 'supplier-2', supplierPartyId: PARTY_2 },
      {},
      { cursor: 'next-1' },
    ])
  })

  it('a failed next page of the filtered list keeps its rows and retries the same cursor with the same supplier', async () => {
    let attempt = 0
    const calls = installFetch((url) => {
      if (url.searchParams.get('supplierPartyId') !== PARTY_2) return page([longOrder(), shortOrder()])
      if (url.searchParams.get('cursor') !== 'supplier-2') return page([shortOrder()], 'supplier-2')
      attempt += 1
      return attempt === 1 ? json({ code: 'service_unavailable' }, 503) : page([shortOrder3()])
    })
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2]))
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    const retry = await screen.findByRole('button', { name: 'Aynı sayfayı yeniden dene' })
    expect(rowIds()).toEqual([ORDER_2])
    expect(chip()?.textContent).toContain('CAR-000007 — Kısa Ltd.')
    await userEvent.click(retry)
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2, ORDER_3]))
    expect(calls.filter((call) => call.url.searchParams.get('cursor') === 'supplier-2').map((call) => queryOf(call.url))).toEqual([
      { cursor: 'supplier-2', supplierPartyId: PARTY_2 },
      { cursor: 'supplier-2', supplierPartyId: PARTY_2 },
    ])
  })

  it('applying the filter abandons a next page in flight: its late answer is never drawn', async () => {
    let release: (response: Response) => void = () => {}
    installFetch((url) => {
      if (url.searchParams.get('supplierPartyId') === PARTY_2) return page([shortOrder()])
      if (url.searchParams.get('cursor') === 'next-1') return new Promise<Response>((resolve) => { release = resolve })
      return page([longOrder(), shortOrder()], 'next-1')
    })
    mount()
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await screen.findByRole('button', { name: 'Sonraki sayfa yükleniyor…' })
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2]))
    await act(async () => release(page([shortOrder3()])))
    expect(rowIds()).toEqual([ORDER_2])
    expect(screen.queryByRole('button', { name: 'Sonraki sayfa yükleniyor…' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aynı sayfayı yeniden dene' })).toBeNull()
  })

  it.each([
    ['the company', () => { scenario.companyId = COMPANY_B; scenario.namespace = 'ctx-b' }, COMPANY_B],
    ['the actor', () => { scenario.namespace = 'ctx-a-second-actor' }, COMPANY_A],
  ])('a change of %s drops the supplier filter: the next list is read unfiltered', async (_what, change, expectedCompany) => {
    const calls = installFetch((url, companyId) => {
      if (companyId === COMPANY_B) return page([order(ORDER_3, COMPANY_B, short, { supplierPartyId: PARTY_2 })])
      return url.searchParams.get('supplierPartyId') === PARTY_1 ? page([longOrder()]) : page([longOrder(), shortOrder()])
    })
    const view = mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_1))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1]))
    expect(chip()).not.toBeNull()
    const before = calls.length

    change()
    act(() => view.rerender())
    await waitFor(() => expect(calls.length).toBe(before + 1))
    await waitFor(() => expect(rowIds()).toEqual(expectedCompany === COMPANY_B ? [ORDER_3] : [ORDER_1, ORDER_2]))
    expect(queryOf(calls.at(-1)!.url)).toEqual({})
    expect(calls.at(-1)?.companyId).toBe(expectedCompany)
    expect(chip()).toBeNull()
    expect(document.body.textContent).not.toContain('Tedarikçi filtresi')
  })

  it('a filter the server refuses is shown as refused: no unfiltered fallback, and the filter stays removable', async () => {
    const calls = installFetch((url) => url.searchParams.has('supplierPartyId')
      ? json({ code: 'validation_failed' }, 400)
      : page([longOrder(), shortOrder()]))
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(document.body.textContent).not.toContain('CAR-000042')
    // The refused filter is still named, and nothing was re-read without it.
    expect(chip()?.textContent).toContain('CAR-000007 — Kısa Ltd.')
    expect(calls.map((call) => queryOf(call.url))).toEqual([{}, { supplierPartyId: PARTY_2 }])
    // Retrying asks the same filtered question again — never the unfiltered one.
    await userEvent.click(within(document.querySelector('[data-state="malformed-response"]') as HTMLElement).getByRole('button'))
    await waitFor(() => expect(calls).toHaveLength(3))
    expect(queryOf(calls[2].url)).toEqual({ supplierPartyId: PARTY_2 })
    // Only the user removes it.
    await userEvent.click(clearButton())
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1, ORDER_2]))
    expect(chip()).toBeNull()
  })

  it('refuses a "filtered" answer that holds another supplier’s order instead of calling it filtered', async () => {
    installFetch((url) => url.searchParams.has('supplierPartyId') ? page([shortOrder(), longOrder()]) : page([longOrder(), shortOrder()]))
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
    expect(chip()?.textContent).toContain('CAR-000007 — Kısa Ltd.')
  })

  it('refuses a filtered answer of another company', async () => {
    installFetch((url) => url.searchParams.has('supplierPartyId')
      ? page([order(ORDER_3, COMPANY_B, short, { supplierPartyId: PARTY_2 })])
      : page([longOrder(), shortOrder()]))
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it.each([401, 403])('a %i on the filtered read reports to the auth boundary and draws no rows', async (status) => {
    const reported: number[] = []
    scenario.report = (code) => { reported.push(code) }
    installFetch((url) => url.searchParams.has('supplierPartyId') ? json({ code: 'refused' }, status) : page([longOrder(), shortOrder()]))
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(reported).toEqual([status]))
    expect(screen.queryByRole('table')).toBeNull()
    expect(document.body.textContent).not.toContain('CAR-000042')
  })

  it('a supplier with no order in the chosen status is an empty filter, with the filter still shown', async () => {
    installFetch((url) => url.searchParams.get('supplierPartyId') === PARTY_2 && url.searchParams.get('status') === 'Approved'
      ? page([])
      : page(url.searchParams.has('supplierPartyId') ? [shortOrder()] : [longOrder(), shortOrder()]))
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_2]))
    await userEvent.selectOptions(screen.getByLabelText('Durum'), 'Approved')
    await waitFor(() => expect(document.querySelector('[data-state="empty-filtered"]')).not.toBeNull())
    expect(document.querySelector('[data-state="empty-no-data"]')).toBeNull()
    expect(chip()?.textContent).toContain('CAR-000007 — Kısa Ltd.')
    expect(clearButton()).toBeTruthy()
  })

  it('an empty answer under the supplier filter alone is an empty filter, not an empty company', async () => {
    installFetch((url) => url.searchParams.has('supplierPartyId') ? page([]) : page([longOrder(), shortOrder()]))
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect(document.querySelector('[data-state="empty-filtered"]')).not.toBeNull())
    expect(document.querySelector('[data-state="empty-no-data"]')).toBeNull()
    expect((screen.getByLabelText('Durum') as HTMLSelectElement).value).toBe('')
    expect(chip()?.textContent).toContain('CAR-000007 — Kısa Ltd.')
  })

  it('needs no grant beyond the list’s own, and issues no Cari read', async () => {
    scenario.capabilities = ['purchasing.orders.list']
    const calls = serveBySupplier()
    mount()
    await screen.findByRole('table')
    await userEvent.click(filterButton(ORDER_1))
    await waitFor(() => expect(rowIds()).toEqual([ORDER_1]))
    expect(within(screen.getByRole('table')).queryAllByRole('link')).toHaveLength(0)
    expect(calls.every((call) => call.url.pathname === '/api/v1/purchasing/orders')).toBe(true)
  })

  it('on a phone the cards offer the same filter and the active filter stays visible and removable', async () => {
    vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
      matches: true, media: query, onchange: null,
      addEventListener: () => {}, removeEventListener: () => {}, addListener: () => {}, removeListener: () => {}, dispatchEvent: () => false,
    })))
    const calls = serveBySupplier()
    mount()
    const cards = await screen.findByTestId('purchase-order-cards')
    expect(screen.queryByRole('table')).toBeNull()
    const card = cards.querySelector(`li[data-order-id="${ORDER_2}"]`) as HTMLElement
    // The action sits in the supplier fact of the card, beside the identity; the facts keep their four labels.
    expect(card.querySelector('dl > div > dd')?.contains(filterButton(ORDER_2))).toBe(true)
    expect([...card.querySelectorAll('dl > div > dt')].map((label) => label.textContent)).toEqual([T.supplierColumn, 'İş tarihi', 'Satır', 'Güncellendi'])
    await userEvent.click(filterButton(ORDER_2))
    await waitFor(() => expect([...screen.getByTestId('purchase-order-cards').querySelectorAll('li[data-order-id]')].map((item) => item.getAttribute('data-order-id'))).toEqual([ORDER_2]))
    expect(queryOf(calls.at(-1)!.url)).toEqual({ supplierPartyId: PARTY_2 })
    expect(chip()?.textContent).toContain('CAR-000007 — Kısa Ltd.')
    await userEvent.click(clearButton())
    await waitFor(() => expect(screen.getByTestId('purchase-order-cards').querySelectorAll('li[data-order-id]')).toHaveLength(2))
    expect(chip()).toBeNull()
  })

  it('keeps the active filter readable on a narrow screen (page stylesheet)', () => {
    const nodeProcess = (globalThis as unknown as {
      readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
    }).process
    const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
    const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
    const css = fs
      .readFileSync(path.resolve(nodeProcess.cwd(), 'src/purchasing/PurchaseOrderListPage.module.css'), 'utf8')
      .replace(/\/\*[\s\S]*?\*\//g, '')
    const rule = (selector: string) => {
      const start = css.indexOf(`\n${selector} {`)
      return start < 0 ? '' : css.slice(start, css.indexOf('}', start))
    }
    // The supplier in the active filter wraps like it does in the list, and the group may wrap its remove control.
    expect(rule('.activeFilterValue')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(rule('.activeFilterValue')).toMatch(/min-width:\s*0/)
    expect(rule('.activeFilter')).toMatch(/flex-wrap:\s*wrap/)
    expect(rule('.activeFilter')).not.toMatch(/nowrap|overflow:\s*hidden/)
    // The supplier inside a row action's name is not drawn and takes no room: left unclipped, a long name there
    // would widen the whole page.
    expect(rule('.srOnly')).toMatch(/position:\s*absolute/)
    expect(rule('.srOnly')).toMatch(/overflow:\s*hidden/)
    expect(rule('.srOnly')).toMatch(/width:\s*1px/)
    // Beside the identity the action keeps its width and the identity may shrink and wrap.
    expect(rule('.supplierCell')).toMatch(/min-width:\s*0/)
    expect(rule('.supplier')).toMatch(/min-width:\s*0/)
    expect(css).not.toMatch(/text-overflow|line-clamp|#[0-9a-fA-F]{3,8}\b|rgb\(|hsl\(/)
  })
})
