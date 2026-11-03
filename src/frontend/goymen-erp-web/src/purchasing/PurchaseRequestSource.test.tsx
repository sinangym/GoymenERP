import { cleanup, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseRequestCreatePage } from './PurchaseRequestCreatePage'
import { PurchaseRequestDetailPage } from './PurchaseRequestDetailPage'
import { PurchaseRequestListPage } from './PurchaseRequestListPage'
import { fetchPurchaseRequest, fetchPurchaseRequests } from './api'
import { purchaseRequestDetailPath } from './capabilities'
import type { PurchaseRequestDetail, PurchaseRequestLine, PurchaseRequestSummary } from './contract'

/**
 * A1-2 (plan API-02; Üretim A-2, Satış S-A1): the request line's source on P-01 — read-only. The detail shows each line's
 * source (manual, or the module with its opaque reference), the list shows the request's source kinds, a body before A1-2
 * shows "—" (unknown, never "manual"), and the create form says it opens a manual request. SENTETIK.
 */
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const REQUEST = '66666666-6666-4666-8666-666666666666'
const PRODUCT = '44444444-4444-4444-8444-444444444444'
const UNIT = '55555555-5555-4555-8555-555555555555'
const SALES_ORDER = 'a1a1a1a1-0000-4000-8000-0000000000a1'
const SALES_LINE = 'a1a1a1a1-0000-4000-8000-0000000000b2'
const clients: QueryClient[] = []

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: ACTOR, companyId: COMPANY, scopeMode: 'single', capabilities: [] },
  }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => ['SENTETIK-context-a'],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) => capability === 'purchasing.requests.detail',
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
vi.mock('./api', () => ({ fetchPurchaseRequest: vi.fn(), fetchPurchaseRequests: vi.fn(), fetchProductPurchaseCandidates: vi.fn() }))
vi.mock('./requestSubmissionApi', () => ({ submitPurchaseRequest: vi.fn() }))

function line(lineNo: number, source: PurchaseRequestLine['source']): PurchaseRequestLine {
  return { lineId: `33333333-3333-4333-8333-00000000000${lineNo}`, lineNo, productId: PRODUCT, productVersion: '7', unitId: UNIT, quantity: '5', source }
}
function summary(sourceKinds: PurchaseRequestSummary['sourceKinds'], lineCount: number): PurchaseRequestSummary {
  return {
    id: REQUEST, companyId: COMPANY, status: 'Draft', version: '3', effectiveOn: '2026-11-03',
    createdAtUtc: '2026-11-03T09:00:00Z', updatedAtUtc: '2026-11-03T10:00:00Z', lineCount, sourceKinds,
  }
}
function detail(lines: readonly PurchaseRequestLine[], sourceKinds: PurchaseRequestSummary['sourceKinds']): PurchaseRequestDetail {
  return { request: summary(sourceKinds, lines.length), lines, approval: null, rejection: null, withdrawal: null, preparedBy: ACTOR, submittedBy: null }
}
function mount(element: React.ReactElement, pathname: string, route: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[pathname]}><Routes><Route path={route} element={element} /></Routes></MemoryRouter></QueryClientProvider>)
}

beforeEach(() => {
  vi.mocked(fetchPurchaseRequest).mockReset()
  vi.mocked(fetchPurchaseRequests).mockReset()
  sessionStorage.clear()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('P-01 request line source (A1-2, read-only)', () => {
  it('shows each line source on the detail: the module with its opaque reference, or manual', async () => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({
      type: 'ok-single',
      data: detail([line(1, { kind: 'Sales', documentId: SALES_ORDER, documentLineId: SALES_LINE }), line(2, null)], ['Manual', 'Sales']),
    })
    mount(<PurchaseRequestDetailPage />, purchaseRequestDetailPath(REQUEST), '/satinalma/talepler/:requestId')
    await screen.findByText(REQUEST, { exact: true })

    expect(screen.getByRole('columnheader', { name: 'Kaynak' })).toBeTruthy()
    const rows = screen.getAllByRole('row').slice(1)
    expect(within(rows[0]).getByText('Satış', { exact: false })).toBeTruthy()
    expect(within(rows[0]).getByText('belge …000000a1 · satır …000000b2')).toBeTruthy()
    expect(within(rows[1]).getByText('Manuel')).toBeTruthy()
    // Read-only: no control to set or change a source.
    expect(screen.queryByRole('combobox')).toBeNull()
  })

  it('shows an unknown source as "—" for a body before A1-2, never as manual', async () => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data: detail([line(1, undefined)], undefined) })
    mount(<PurchaseRequestDetailPage />, purchaseRequestDetailPath(REQUEST), '/satinalma/talepler/:requestId')
    await screen.findByText(REQUEST, { exact: true })

    const row = screen.getAllByRole('row')[1]
    expect(within(row).getByText('—')).toBeTruthy()
    expect(within(row).queryByText('Manuel')).toBeNull()
  })

  it('shows the request source kinds in the list in the server order', async () => {
    vi.mocked(fetchPurchaseRequests).mockResolvedValue({
      type: 'ok-single',
      data: { items: [summary(['Manual', 'Production'], 2), { ...summary(undefined, 1), id: '77777777-7777-4777-8777-777777777777' }], nextCursor: null },
    })
    mount(<PurchaseRequestListPage />, '/satinalma/talepler', '/satinalma/talepler')
    await screen.findByTestId('purchase-request-list')

    expect(screen.getByRole('columnheader', { name: 'Kaynak' })).toBeTruthy()
    const rows = screen.getAllByRole('row').slice(1)
    expect(within(rows[0]).getByText('Manuel, Üretim')).toBeTruthy()
    expect(within(rows[1]).getByText('—')).toBeTruthy()
  })

  it('tells the preparer the form opens a manual request', () => {
    mount(<PurchaseRequestCreatePage />, '/satinalma/talepler/yeni', '/satinalma/talepler/yeni')
    expect(screen.getByText(/Kaynak: Manuel\./)).toBeTruthy()
  })
})
