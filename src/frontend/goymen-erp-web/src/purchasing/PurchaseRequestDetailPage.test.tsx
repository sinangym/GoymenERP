import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderDetailPage } from './PurchaseOrderDetailPage'
import { PurchaseRequestDetailPage } from './PurchaseRequestDetailPage'
import { fetchPurchaseOrder, fetchPurchaseRequest } from './api'
import { purchaseOrderDetailPath, purchaseRequestDetailPath, PURCHASING_CAPABILITY } from './capabilities'
import type { PurchaseOrderDetail, PurchaseRequestDetail } from './contract'
import type { ViewOutcome } from '../shell/uiState'

const scenario = vi.hoisted(() => ({
  namespace: 'SENTETIK-context-a' as string | null,
  companyId: '22222222-2222-4222-8222-222222222222',
  generation: 1,
  canReadOrder: true,
  canReadRequest: true,
  report: vi.fn(),
}))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const OTHER_COMPANY = '88888888-8888-4888-8888-888888888888'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const ORDER = '11111111-1111-4111-8111-111111111111'
const REQUEST = '66666666-6666-4666-8666-666666666666'
const OTHER_REQUEST = '77777777-7777-4777-8777-777777777777'
const RETURN = 'Kaynak siparişe dön'
const clients: QueryClient[] = []

vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: scenario.generation,
    activeContext: { userId: ACTOR, companyId: scenario.companyId, scopeMode: 'single', capabilities: [] },
  }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => scenario.namespace === null ? null : [scenario.namespace],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) =>
    capability === 'purchasing.orders.detail' ? scenario.canReadOrder
      : capability === 'purchasing.requests.detail' ? scenario.canReadRequest : false,
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: scenario.generation }), report: scenario.report }),
}))
vi.mock('./api', () => ({ fetchPurchaseOrder: vi.fn(), fetchPurchaseRequest: vi.fn() }))
vi.mock('./approvalApi', () => ({ approvePurchaseOrder: vi.fn() }))
vi.mock('./requestSubmissionApi', () => ({ submitPurchaseRequest: vi.fn() }))

function requestDetail(companyId = COMPANY, id = REQUEST): PurchaseRequestDetail {
  return {
    request: {
      id, companyId, status: 'Submitted', version: '3', effectiveOn: '2026-09-23',
      createdAtUtc: '2026-09-23T09:00:00Z', updatedAtUtc: '2026-09-23T10:00:00Z', lineCount: 0,
    },
    lines: [],
    acceptances: [],
  }
}
function orderDetail(): PurchaseOrderDetail {
  return {
    order: {
      id: ORDER, companyId: COMPANY, status: 'Approved', version: '4', sourceRequestId: REQUEST,
      supplierPartyId: '44444444-4444-4444-8444-444444444444',
      companyAccountId: '55555555-5555-4555-8555-555555555555', effectiveOn: '2026-09-23',
      createdAtUtc: '2026-09-23T09:00:00Z', updatedAtUtc: '2026-09-23T10:00:00Z', lineCount: 0,
    },
    lines: [],
  }
}
function origin() { return { ns: 'SENTETIK-context-a', requestId: REQUEST, orderId: ORDER } }
function mount(state: unknown = origin(), pathname = purchaseRequestDetailPath(REQUEST)) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  clients.push(client)
  const element = () => <QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[{ pathname, state }]}>
      <Routes>
        <Route path="/satinalma/talepler/:requestId" element={<PurchaseRequestDetailPage />} />
        <Route path="/satinalma/siparisler/:orderId" element={<PurchaseOrderDetailPage />} />
      </Routes>
    </MemoryRouter>
  </QueryClientProvider>
  const view = render(element())
  return { ...view, refresh: () => view.rerender(element()) }
}
async function ready() { await screen.findByText(REQUEST, { exact: true }) }
function expectNoReturn() { expect(screen.queryByRole('link', { name: RETURN })).toBeNull() }

beforeEach(() => {
  scenario.namespace = 'SENTETIK-context-a'; scenario.companyId = COMPANY; scenario.generation = 1
  scenario.canReadOrder = true; scenario.canReadRequest = true; scenario.report.mockReset()
  vi.mocked(fetchPurchaseRequest).mockReset().mockResolvedValue({ type: 'ok-single', data: requestDetail() })
  vi.mocked(fetchPurchaseOrder).mockReset().mockResolvedValue({ type: 'ok-single', data: orderDetail() })
  sessionStorage.clear()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('PurchaseRequestDetailPage context-bound order return', () => {
  it('reads the request, returns to the exact canonical order and fetches its server state', async () => {
    const user = userEvent.setup(); mount(); await ready()
    expect(screen.getByRole('link', { name: RETURN }).getAttribute('href')).toBe(purchaseOrderDetailPath(ORDER))
    expect(fetchPurchaseOrder).not.toHaveBeenCalled()
    await user.click(screen.getByRole('link', { name: RETURN }))
    await screen.findByText(ORDER, { exact: true })
    expect(screen.getByText('Onaylandı')).toBeTruthy()
    expect(fetchPurchaseOrder).toHaveBeenCalledWith(COMPANY, ORDER, expect.any(AbortSignal))
  })

  it('makes the complete real-component order/request/order round trip without storing a return URL', async () => {
    const user = userEvent.setup(); mount(null, purchaseOrderDetailPath(ORDER))
    await screen.findByText(ORDER, { exact: true })
    await user.click(screen.getByRole('link', { name: REQUEST }))
    await ready()
    expect(fetchPurchaseRequest).toHaveBeenCalledWith(COMPANY, REQUEST, expect.any(AbortSignal))
    await user.click(screen.getByRole('link', { name: RETURN }))
    await screen.findByText(ORDER, { exact: true })
    await waitFor(() => expect(fetchPurchaseOrder).toHaveBeenCalledTimes(2))
  })

  it.each([
    ['direct entry', null], ['string', '/satinalma/siparisler/anything'], ['array', []],
    ['missing namespace', { requestId: REQUEST, orderId: ORDER }],
    ['other namespace', { ...origin(), ns: 'SENTETIK-context-b' }],
    ['other request', { ...origin(), requestId: OTHER_REQUEST }],
    ['external URL', { ...origin(), orderId: 'https://example.invalid' }],
    ['relative path', { ...origin(), orderId: '../other' }],
    ['query suffix', { ...origin(), orderId: `${ORDER}?company=other` }],
    ['empty Guid', { ...origin(), orderId: '00000000-0000-0000-0000-000000000000' }],
    ['noncanonical id', { ...origin(), orderId: 'AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA' }],
    ['extra returnTo', { ...origin(), returnTo: '/admin' }],
    ['extra generation', { ...origin(), generation: 1 }],
    ['numeric id', { ...origin(), orderId: 42 }],
  ])('withholds return for %s', async (_name, state) => {
    mount(state); await ready(); expectNoReturn()
  })

  it.each([PURCHASING_CAPABILITY.orderDetail, PURCHASING_CAPABILITY.requestDetail])(
    'removes return immediately when %s is revoked', async (capability) => {
      const view = mount(); await ready()
      expect(screen.getByRole('link', { name: RETURN })).toBeTruthy()
      if (capability === PURCHASING_CAPABILITY.orderDetail) scenario.canReadOrder = false
      else scenario.canReadRequest = false
      view.refresh(); expectNoReturn()
    },
  )

  it('does not recapture an old hint when the same route entry generation changes', async () => {
    const view = mount(); await ready(); scenario.generation += 1
    view.refresh(); expectNoReturn(); await ready(); expectNoReturn()
  })

  it('withholds a prior namespace even when the company itself is unchanged', async () => {
    const view = mount(); await ready(); scenario.namespace = 'SENTETIK-new-authority-revision'
    view.refresh(); expectNoReturn(); await ready(); expectNoReturn()
  })

  it('does not expose the old order after switching company', async () => {
    const view = mount(); await ready()
    scenario.namespace = 'SENTETIK-context-b'; scenario.companyId = OTHER_COMPANY
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'http', status: 404 })
    view.refresh(); expectNoReturn()
    await screen.findByText('Bulunamadı — satın alma talebi'); expectNoReturn()
    expect(fetchPurchaseOrder).not.toHaveBeenCalled()
  })

  it('discards a delayed old-context response instead of restoring its return link', async () => {
    let release!: (outcome: ViewOutcome<PurchaseRequestDetail>) => void
    vi.mocked(fetchPurchaseRequest).mockImplementationOnce(() => new Promise((resolve) => { release = resolve }))
    const view = mount()
    await waitFor(() => expect(fetchPurchaseRequest).toHaveBeenCalledTimes(1)); expectNoReturn()
    scenario.namespace = 'SENTETIK-context-b'; scenario.companyId = OTHER_COMPANY
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'http', status: 404 })
    view.refresh(); await screen.findByText('Bulunamadı — satın alma talebi')
    await act(async () => release({ type: 'ok-single', data: requestDetail() }))
    expectNoReturn(); expect(screen.queryByText(REQUEST, { exact: true })).toBeNull()
  })

  it.each([401, 403])('reports HTTP %s to the existing auth boundary and exposes no return', async (status) => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'http', status })
    mount(); await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(status, { token: 1 }))
    expectNoReturn(); expect(fetchPurchaseOrder).not.toHaveBeenCalled()
  })

  it('offers retry after a failed read and shows the return only after a successful read', async () => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValueOnce({ type: 'network-error' })
    const user = userEvent.setup(); mount()
    await screen.findByRole('button', { name: 'Tekrar dene' }); expectNoReturn()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await ready(); expect(screen.getByRole('link', { name: RETURN })).toBeTruthy()
  })

  it.each([
    requestDetail(OTHER_COMPANY), requestDetail(COMPANY, OTHER_REQUEST),
  ])('withholds return if the loaded request identity does not match the entry', async (data) => {
    vi.mocked(fetchPurchaseRequest).mockResolvedValue({ type: 'ok-single', data })
    mount(); await screen.findByText(data.request.id, { exact: true }); expectNoReturn()
  })

  it('keeps an unresolved context closed and does not start a read', () => {
    scenario.namespace = null; mount(); expectNoReturn(); expect(fetchPurchaseRequest).not.toHaveBeenCalled()
  })
})
