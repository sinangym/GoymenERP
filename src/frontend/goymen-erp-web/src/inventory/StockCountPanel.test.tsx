import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'
import type { InventoryLabelDetailWithMovementTimeline } from './labelDetailContract'
import type { StockCountDecisionRequest, StockCountRequest, StockCountView } from './stockCountContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const PARTY = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const COUNT = '77777777-7777-7777-7777-777777777777'
const MOVEMENT = '88888888-8888-8888-8888-888888888888'
const clients: QueryClient[] = []
const api = vi.hoisted(() => ({ detail: vi.fn(), record: vi.fn(), decide: vi.fn(), counts: vi.fn() }))
const scenario = vi.hoisted(() => ({ canRecord: true, canApprove: false, report: vi.fn() }))
vi.mock('./labelDetailApi', () => ({ fetchInventoryLabelDetail: api.detail }))
vi.mock('./stockCountApi', () => ({
  recordStockCount: api.record, decideStockCount: api.decide, fetchLabelStockCounts: api.counts,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: COMPANY, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => ['SENTETIK-a'],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) =>
    capability === 'inventory.stock-positions.list' ||
    (capability === 'inventory.stock-count.record' && scenario.canRecord) ||
    (capability === 'inventory.stock-count.approve' && scenario.canApprove),
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ generation: 1 }), report: scenario.report }),
}))

function detail(): InventoryLabelDetailWithMovementTimeline {
  return {
    labelVersion: '3',
    identity: {
      labelId: LABEL, companyId: COMPANY, code: 'LBL-100', productId: PRODUCT, lotId: LOT,
      heatNumber: null, coilNumber: 'COIL-9', documentStatus: 'Complete', requiredDocuments: [],
      documents: [], prints: [], lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null },
    },
    balances: {
      labelId: LABEL, physicalKg: '1250.5', freeKg: '1250.5', blockedKg: '0',
      positions: [{ ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
        locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '1250.5' }],
    },
    genealogy: { labelId: LABEL, labelCode: 'LBL-100', windowFull: false, edges: [] },
    movementTimeline: { labelId: LABEL, windowFull: false, entries: [] },
  }
}

function view(overrides: Partial<StockCountView> = {}): StockCountView {
  return {
    countId: COUNT, companyId: COMPANY, labelId: LABEL, labelVersion: '3', ownerKind: 'Company', ownerId: PARTY,
    custodianKind: 'Company', custodianId: PARTY, locationId: LOCATION, productId: PRODUCT, block: 'Free',
    bookKg: '1250.5', countedKg: '1248', differenceKg: '-2.5', countedAtUtc: '2026-09-27T08:00:00Z',
    reason: 'Rulo tartıldı', recordedAtUtc: '2026-09-27T08:00:01Z', status: 'PendingApproval', decision: null,
    ...overrides,
  }
}

function recorded(request: StockCountRequest) {
  return {
    type: 'applied', replayed: false,
    response: view({ countId: request.countId, countedKg: request.countedKg, reason: request.reason,
      countedAtUtc: request.countedAtUtc }),
  }
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[`/stok/etiket/${LABEL}`]}>
    <Routes><Route path="/stok/etiket/:labelId" element={<InventoryLabelDetailPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>)
}

const panel = () => screen.findByRole('heading', { name: 'Sayım ve fark (ST-05)' })

beforeEach(() => {
  scenario.canRecord = true; scenario.canApprove = false; scenario.report.mockReset()
  api.detail.mockReset().mockResolvedValue({ type: 'ok-single', data: detail() })
  api.counts.mockReset().mockResolvedValue({ type: 'ok-single', data: { labelId: LABEL, items: [], windowFull: false } })
  api.record.mockReset().mockImplementation(async (request: StockCountRequest) => recorded(request))
  api.decide.mockReset()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('StockCountPanel on the label detail', () => {
  it('is absent without either server-advertised count capability', async () => {
    scenario.canRecord = false; mount()
    await screen.findByRole('heading', { name: 'LBL-100' })
    expect(screen.queryByRole('heading', { name: 'Sayım ve fark (ST-05)' })).toBeNull()
    expect(api.counts).not.toHaveBeenCalled()
  })

  it('records a count with its reason against the version it showed, then rereads counts and the label', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await user.type(screen.getByLabelText('Sayılan kg'), '1248')
    expect(screen.getByText('Fark: -2.5 kg')).toBeTruthy()
    await user.type(screen.getByLabelText('Fark gerekçesi'), 'Rulo tartıldı')
    await user.click(screen.getByRole('button', { name: 'Sayımı kaydet' }))

    await screen.findByText(/fark -2.5 kg, başka bir yetkilinin onayını bekliyor/)
    const [request, context] = api.record.mock.calls[0] as [StockCountRequest, { companyId: string; idempotencyKey: string }]
    expect(request).toMatchObject({ labelId: LABEL, expectedLabelVersion: '3', countedKg: '1248', reason: 'Rulo tartıldı' })
    expect(context.companyId).toBe(COMPANY)
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    expect(api.counts.mock.calls.length).toBeGreaterThanOrEqual(2)
  })

  it('accepts a zero count and refuses a count without a reason before sending', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await user.type(screen.getByLabelText('Sayılan kg'), '0')
    expect(screen.getByText('Fark: -1250.5 kg')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Sayımı kaydet' }))
    expect(await screen.findByText(/Fark gerekçesi zorunludur/)).toBeTruthy()
    expect(api.record).not.toHaveBeenCalled()
  })

  it('keeps an unknown outcome and resends the same key and payload', async () => {
    const user = userEvent.setup(); mount(); await panel()
    api.record.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    await user.type(screen.getByLabelText('Sayılan kg'), '1250,5')
    await user.type(screen.getByLabelText('Fark gerekçesi'), 'Kontrol sayımı')
    await user.click(screen.getByRole('button', { name: 'Sayımı kaydet' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))

    await waitFor(() => expect(api.record).toHaveBeenCalledTimes(2))
    expect(api.record.mock.calls[1]).toEqual(api.record.mock.calls[0])
  })

  it('lets an approver approve a pending difference with a note and shows the maker-checker refusal text', async () => {
    scenario.canRecord = false; scenario.canApprove = true
    api.counts.mockResolvedValue({ type: 'ok-single', data: { labelId: LABEL, items: [view()], windowFull: false } })
    api.decide.mockResolvedValueOnce({ type: 'failed', status: 409, code: 'inventory_stock_count_self_approval' })
      .mockImplementationOnce(async (countId: string, request: StockCountDecisionRequest) => ({
        type: 'applied', replayed: false,
        response: view({ countId, status: 'Approved', decision: { decision: 'Approved',
          decidedAtUtc: '2026-09-27T09:00:00Z', reason: request.reason, adjustmentMovementId: MOVEMENT } }),
      }))
    const user = userEvent.setup(); mount(); await panel()
    expect(screen.queryByRole('button', { name: 'Sayımı kaydet' })).toBeNull()
    await user.type(await screen.findByLabelText('Karar notu'), 'Tartım fişi uygun')
    await user.click(screen.getByRole('button', { name: 'Farkı onayla' }))
    expect(await screen.findByText(/Kendi girdiğiniz sayım farkını onaylayamaz/)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Farkı onayla' }))
    await screen.findByText(/Sayım farkı onaylandı ve stoğa işlendi: -2.5 kg/)
    expect(api.decide.mock.calls[1].slice(0, 2)).toEqual([COUNT, { decision: 'Approve', reason: 'Tartım fişi uygun' }])
    expect(api.decide.mock.calls[0][2].idempotencyKey).not.toBe(api.decide.mock.calls[1][2].idempotencyKey)
  })

  it('shows the count history with its status and decision', async () => {
    api.counts.mockResolvedValue({ type: 'ok-single', data: { labelId: LABEL, windowFull: false, items: [view({
      status: 'Approved', decision: { decision: 'Approved', decidedAtUtc: '2026-09-27T09:00:00Z', reason: 'Uygun',
        adjustmentMovementId: MOVEMENT } })] } })
    mount(); await panel()
    expect(await screen.findByText('Onaylandı (stoğa işlendi)')).toBeTruthy()
    expect(screen.getByText('Rulo tartıldı')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Farkı onayla' })).toBeNull()
  })
})
