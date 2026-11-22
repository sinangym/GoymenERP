import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DraftDetail } from './contract'
import { decodeOrderKilogramPrices } from './kilogramPriceContract'
import { SalesOrderKilogramPrices } from './SalesKilogramPrices'
import { KILOGRAM_PRICE_TEXT as T } from './salesText'
import type { ShellState } from '../shell/shellState'

// SAT-D436 (AK-4b): the order's kg price read is open (`Computed`). The panel is information beside the order: it shows
// the server's figures, and every unreadable answer is one quiet line — never a second alert over the order. SENTETIK.
const COMPANY = '10000000-0000-4000-8000-000000000001'
const DRAFT = '20000000-0000-4000-8000-000000000001'
const LINE = '30000000-0000-4000-8000-000000000001'
const CURRENCY = '22222222-2222-4222-8222-222222222222'

const api = vi.hoisted(() => ({ fetch: vi.fn() }))
const boundary = vi.hoisted(() => ({ report: vi.fn() }))
vi.mock('./kilogramPriceApi', () => ({ fetchDraftKilogramPrices: api.fetch }))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: boundary.report }),
}))
vi.mock('../shell/shellState', async (original) => ({
  ...(await original<typeof import('../shell/shellState')>()),
  contextKey: () => ['SENTETIK-user', 'single', '10000000-0000-4000-8000-000000000001', 'g1', '1'],
}))

const shell = { routeEntryGeneration: 1 } as unknown as ShellState
const detail = (priceDisclosure: 'Visible' | 'Withheld' = 'Visible') => ({
  draft: { id: DRAFT, version: '4' },
  lines: [{ lineId: LINE, lineNo: 1, priceDisclosure }],
}) as unknown as DraftDetail

const shown = (value: string) => ({ state: 'Shown', value })
const computed = () => decodeOrderKilogramPrices({
  state: 'Computed', currencyId: CURRENCY,
  lines: [{
    lineId: LINE, lineNo: 1, theoreticalKg: '493.8', theoreticalPrice: shown('20.25'), invoicedQuantity: '40',
    invoicedAmount: '10000.00', actualKg: '500.16', actualPrice: shown('19.99'), kgDifference: '6.36', deviationPercent: '1.29',
  }],
  documentTheoreticalPrice: shown('20.25'), documentActualPrice: shown('19.99'),
})!

const clients: QueryClient[] = []
function mount(view = detail()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}>
    <SalesOrderKilogramPrices shell={shell} detail={view} companyId={COMPANY} />
  </QueryClientProvider>)
}

beforeEach(() => { api.fetch.mockReset(); boundary.report.mockReset() })
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('SalesOrderKilogramPrices on the order detail', () => {
  it('renders nothing while the read is in flight, then the computed figures of this order', async () => {
    let release: (value: unknown) => void = () => undefined
    api.fetch.mockReturnValue(new Promise((resolve) => { release = resolve }))
    const view = mount()
    await waitFor(() => expect(api.fetch).toHaveBeenCalledTimes(1))
    expect(api.fetch.mock.calls[0].slice(0, 2)).toEqual([DRAFT, COMPANY])
    expect(view.container.textContent).toBe('')

    release({ type: 'ok-single', data: computed() })
    expect((await screen.findByTestId('sales-kilogram-line-1-theoretical')).textContent).toBe(T.perKg('20.25'))
    expect(screen.getByTestId('sales-kilogram-line-1-actual').textContent).toBe(T.perKg('19.99'))
    expect(screen.getByTestId('sales-kilogram-document-theoretical').textContent).toBe(T.perKg('20.25'))
    expect(screen.getByTestId('sales-kilogram-document-actual').textContent).toBe(T.perKg('19.99'))
    expect(screen.queryByTestId('sales-kilogram-prices-pending')).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('still shows the closed indicator when the server answers FormulaGoldenPending', async () => {
    api.fetch.mockResolvedValue({ type: 'ok-single',
      data: decodeOrderKilogramPrices({ state: 'FormulaGoldenPending', currencyId: CURRENCY })! })
    mount()
    expect((await screen.findByTestId('sales-kilogram-prices-pending')).textContent).toBe(T.pending)
    expect(screen.queryByTestId('sales-kilogram-line-1')).toBeNull()
  })

  it('asks nothing and shows nothing when a line withholds its price', () => {
    const view = mount(detail('Withheld'))
    expect(api.fetch).not.toHaveBeenCalled()
    expect(view.container.textContent).toBe('')
  })

  it.each([
    ['forbidden', { type: 'http', status: 403, body: null }, 403],
    ['unauthenticated', { type: 'http', status: 401, body: null }, 401],
    ['not found', { type: 'http', status: 404, body: null }, 404],
    ['a conflict', { type: 'http', status: 409, body: null }, 409],
    ['a 503', { type: 'http', status: 503, body: null }, 503],
    ['malformed', { type: 'malformed' }, null],
    ['offline', { type: 'offline' }, null],
    ['a network error', { type: 'network-error' }, null],
  ])('says quietly that the kg price is unreadable when the read is %s, without a second alert', async (_name, outcome, status) => {
    api.fetch.mockResolvedValue(outcome)
    mount()
    expect((await screen.findByTestId('sales-kilogram-prices-unreadable')).textContent).toBe(T.unreadable)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByTestId('sales-kilogram-prices')).toBeNull()
    if (status === null) expect(boundary.report).not.toHaveBeenCalled()
    else expect(boundary.report).toHaveBeenCalledWith(status, { token: 1 })
  })
})
