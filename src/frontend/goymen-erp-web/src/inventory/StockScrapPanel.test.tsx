import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'
import type { InventoryLabelDetailWithMovementTimeline } from './labelDetailContract'
import type { StockScrapDecisionRequest, StockScrapRequest, StockScrapView } from './stockScrapContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const PARTY = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const SCRAP = '77777777-7777-7777-7777-777777777777'
const MOVEMENT = '88888888-8888-8888-8888-888888888888'
const clients: QueryClient[] = []
const api = vi.hoisted(() => ({ detail: vi.fn(), record: vi.fn(), decide: vi.fn(), scraps: vi.fn() }))
const scenario = vi.hoisted(() => ({
  canRecord: true, canApprove: false, report: vi.fn(), namespace: 'SENTETIK-a', userId: 'actor-a', routeEntry: 1,
}))
vi.mock('./labelDetailApi', () => ({ fetchInventoryLabelDetail: api.detail }))
vi.mock('./stockScrapApi', () => ({
  recordStockScrap: api.record, decideStockScrap: api.decide, fetchLabelStockScraps: api.scraps,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: scenario.routeEntry,
    activeContext: { userId: scenario.userId, companyId: COMPANY, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => [scenario.namespace],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) =>
    capability === 'inventory.stock-positions.list' ||
    (capability === 'inventory.stock-scrap.record' && scenario.canRecord) ||
    (capability === 'inventory.stock-scrap.approve' && scenario.canApprove),
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ generation: 1 }), report: scenario.report }),
}))

function detail(): InventoryLabelDetailWithMovementTimeline {
  return {
    labelVersion: '3',
    identity: {
      labelId: LABEL, companyId: COMPANY, code: 'LBL-100', productId: PRODUCT, lotId: LOT,
      heatNumber: null, coilNumber: 'COIL-9', classification: null, documentStatus: 'Complete', requiredDocuments: [],
      documents: [], prints: [], lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null },
    },
    balances: {
      labelId: LABEL, physicalKg: '1250.5', freeKg: '0', blockedKg: '1250.5',
      positions: [{ ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
        locationId: LOCATION, productId: PRODUCT, block: 'Blocked', quantityKg: '1250.5' }],
    },
    genealogy: { labelId: LABEL, labelCode: 'LBL-100', windowFull: false, edges: [] },
    movementTimeline: { labelId: LABEL, windowFull: false, entries: [] },
  }
}

function view(overrides: Partial<StockScrapView> = {}): StockScrapView {
  return {
    scrapId: SCRAP, companyId: COMPANY, labelId: LABEL, labelVersion: '3', ownerKind: 'Company', ownerId: PARTY,
    custodianKind: 'Company', custodianId: PARTY, locationId: LOCATION, productId: PRODUCT, block: 'Blocked',
    bookKg: '1250.5', scrapKg: '50.5', remainingKg: '1200', scrappedAtUtc: '2026-09-28T08:00:00Z',
    reason: 'Paslanma', recordedAtUtc: '2026-09-28T08:00:01Z', status: 'PendingApproval', decision: null,
    ...overrides,
  }
}

function recorded(request: StockScrapRequest) {
  return {
    type: 'applied', replayed: false,
    response: view({ scrapId: request.scrapId, scrapKg: request.scrapKg, reason: request.reason,
      scrappedAtUtc: request.scrappedAtUtc }),
  }
}

function mount(client = newClient()) {
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[`/stok/etiket/${LABEL}`]}>
    <Routes><Route path="/stok/etiket/:labelId" element={<InventoryLabelDetailPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>)
}

function newClient() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return client
}

const panel = () => screen.findByRole('heading', { name: 'Hurda / fire çıkışı' })

beforeEach(() => {
  scenario.canRecord = true; scenario.canApprove = false; scenario.report.mockReset()
  scenario.namespace = 'SENTETIK-a'; scenario.userId = 'actor-a'; scenario.routeEntry = 1
  api.detail.mockReset().mockResolvedValue({ type: 'ok-single', data: detail() })
  api.scraps.mockReset().mockResolvedValue({ type: 'ok-single', data: { labelId: LABEL, items: [], windowFull: false } })
  api.record.mockReset().mockImplementation(async (request: StockScrapRequest) => recorded(request))
  api.decide.mockReset()
})
afterEach(() => {
  cleanup(); clients.splice(0).forEach((client) => client.clear())
  try { sessionStorage.clear() } catch { /* no storage */ }
})

describe('StockScrapPanel on the label detail', () => {
  it('is absent without either server-advertised scrap capability', async () => {
    scenario.canRecord = false; mount()
    await screen.findByRole('heading', { name: 'LBL-100' })
    expect(screen.queryByRole('heading', { name: 'Hurda / fire çıkışı' })).toBeNull()
    expect(api.scraps).not.toHaveBeenCalled()
  })

  it('records a scrap of a blocked piece with its reason against the version it showed, then rereads', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await user.type(screen.getByLabelText('Hurda kg'), '50,5')
    expect(screen.getByText('Onaydan sonra kalan: 1200 kg')).toBeTruthy()
    await user.type(screen.getByLabelText('Hurda gerekçesi'), 'Paslanma')
    await user.click(screen.getByRole('button', { name: 'Hurdayı kaydet' }))

    await screen.findByText(/Hurda kaydedildi: 50.5 kg, başka bir yetkilinin onayını bekliyor/)
    const [request, context] = api.record.mock.calls[0] as [StockScrapRequest, { companyId: string; idempotencyKey: string }]
    expect(request).toMatchObject({ labelId: LABEL, expectedLabelVersion: '3', scrapKg: '50.5', reason: 'Paslanma' })
    expect(context.companyId).toBe(COMPANY)
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    expect(api.scraps.mock.calls.length).toBeGreaterThanOrEqual(2)
  })

  it('refuses zero, more than the book, and a missing reason before sending', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await user.type(screen.getByLabelText('Hurda gerekçesi'), 'Hasar')
    await user.type(screen.getByLabelText('Hurda kg'), '0')
    await user.click(screen.getByRole('button', { name: 'Hurdayı kaydet' }))
    expect(await screen.findByText(/Hurda miktarı zorunludur/)).toBeTruthy()
    await user.clear(screen.getByLabelText('Hurda kg'))
    await user.type(screen.getByLabelText('Hurda kg'), '1250,501')
    await user.click(screen.getByRole('button', { name: 'Hurdayı kaydet' }))
    expect(await screen.findByText(/defter miktarından \(1250.5 kg\) fazla olamaz/)).toBeTruthy()
    await user.clear(screen.getByLabelText('Hurda kg'))
    await user.clear(screen.getByLabelText('Hurda gerekçesi'))
    await user.type(screen.getByLabelText('Hurda kg'), '1')
    await user.click(screen.getByRole('button', { name: 'Hurdayı kaydet' }))
    expect(await screen.findByText(/Hurda gerekçesi zorunludur/)).toBeTruthy()
    expect(api.record).not.toHaveBeenCalled()
  })

  it('keeps an unknown outcome and resends the same key and payload', async () => {
    const user = userEvent.setup(); mount(); await panel()
    api.record.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    await user.type(screen.getByLabelText('Hurda kg'), '10')
    await user.type(screen.getByLabelText('Hurda gerekçesi'), 'Ezilme')
    await user.click(screen.getByRole('button', { name: 'Hurdayı kaydet' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))

    await waitFor(() => expect(api.record).toHaveBeenCalledTimes(2))
    expect(api.record.mock.calls[1]).toEqual(api.record.mock.calls[0])
  })

  it('lets an approver approve a pending scrap with a note and a refusal leaves the decision open for a new attempt', async () => {
    scenario.canRecord = false; scenario.canApprove = true
    api.scraps.mockResolvedValue({ type: 'ok-single', data: { labelId: LABEL, items: [view()], windowFull: false } })
    api.decide.mockResolvedValueOnce({ type: 'failed', status: 409, code: 'inventory_stock_scrap_stale' })
      .mockImplementationOnce(async (scrapId: string, request: StockScrapDecisionRequest) => ({
        type: 'applied', replayed: false,
        response: view({ scrapId, status: 'Approved', decision: { decision: 'Approved',
          decidedAtUtc: '2026-09-28T09:00:00Z', reason: request.reason, scrapMovementId: MOVEMENT } }),
      }))
    const user = userEvent.setup(); mount(); await panel()
    expect(screen.queryByRole('button', { name: 'Hurdayı kaydet' })).toBeNull()
    await user.type(await screen.findByLabelText('Karar notu'), 'Kalite raporu uygun')
    await user.click(screen.getByRole('button', { name: 'Hurdayı onayla' }))
    expect(await screen.findByText(/Hurda girildikten sonra etiketin stoğu değişti/)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Hurdayı onayla' }))
    await screen.findByText(/Hurda onaylandı ve stoktan düşüldü: 50.5 kg; etikette 1200 kg kaldı/)
    expect(api.decide.mock.calls[1].slice(0, 2)).toEqual([SCRAP, { decision: 'Approve', reason: 'Kalite raporu uygun' }])
    expect(api.decide.mock.calls[0][2].idempotencyKey).not.toBe(api.decide.mock.calls[1][2].idempotencyKey)
  })

  it('shows the scrap history with its status and decision', async () => {
    api.scraps.mockResolvedValue({ type: 'ok-single', data: { labelId: LABEL, windowFull: false, items: [view({
      status: 'Approved', decision: { decision: 'Approved', decidedAtUtc: '2026-09-28T09:00:00Z', reason: 'Uygun',
        scrapMovementId: MOVEMENT } })] } })
    mount(); await panel()
    expect(await screen.findByText('Onaylandı (stoktan düşüldü)')).toBeTruthy()
    expect(screen.getByText('Paslanma')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Hurdayı onayla' })).toBeNull()
  })

  it('hides the decision on a pending scrap the viewer recorded, across a reopen, but not for another actor', async () => {
    scenario.canApprove = true
    let stored: StockScrapRequest | null = null
    api.record.mockImplementation(async (request: StockScrapRequest) => { stored = request; return recorded(request) })
    api.scraps.mockImplementation(async () => ({ type: 'ok-single', data: { labelId: LABEL, windowFull: false,
      items: stored === null ? [] : [view({ scrapId: stored.scrapId })] } }))
    const user = userEvent.setup(); const first = mount(); await panel()
    await user.type(screen.getByLabelText('Hurda kg'), '10')
    await user.type(screen.getByLabelText('Hurda gerekçesi'), 'Ezilme')
    await user.click(screen.getByRole('button', { name: 'Hurdayı kaydet' }))
    expect(await screen.findByText(/Bu hurdayı siz kaydettiniz/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Hurdayı onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reddet' })).toBeNull()
    first.unmount()

    scenario.routeEntry = 2; const second = mount(); await panel()
    expect(await screen.findByText(/Bu hurdayı siz kaydettiniz/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Hurdayı onayla' })).toBeNull()
    second.unmount()

    scenario.namespace = 'SENTETIK-b'; scenario.userId = 'actor-b'; scenario.routeEntry = 3; mount(); await panel()
    expect(await screen.findByRole('button', { name: 'Hurdayı onayla' })).toBeTruthy()
    expect(screen.queryByText(/Bu hurdayı siz kaydettiniz/)).toBeNull()
  })

  it('stops offering a decision the server refused as self-approval; the 409 stays authoritative', async () => {
    scenario.canRecord = false; scenario.canApprove = true
    api.scraps.mockResolvedValue({ type: 'ok-single', data: { labelId: LABEL, items: [view()], windowFull: false } })
    api.decide.mockResolvedValueOnce({ type: 'failed', status: 409, code: 'inventory_stock_scrap_self_approval' })
    const user = userEvent.setup(); mount(); await panel()
    await user.type(await screen.findByLabelText('Karar notu'), 'Kendim onaylıyorum')
    await user.click(screen.getByRole('button', { name: 'Hurdayı onayla' }))
    expect(await screen.findByText(/Kendi girdiğiniz hurdayı onaylayamaz/)).toBeTruthy()
    expect(await screen.findByText(/Bu hurdayı siz kaydettiniz/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Hurdayı onayla' })).toBeNull()
    expect(api.decide).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['a new route entry', () => { scenario.routeEntry = 2 }],
    ['another actor', () => { scenario.namespace = 'SENTETIK-b'; scenario.userId = 'actor-b' }],
  ])('never shows the cached list of another entry on reopen: %s', async (_name, change) => {
    api.scraps.mockResolvedValueOnce({ type: 'ok-single', data: { labelId: LABEL, items: [view()], windowFull: false } })
    const client = newClient(); const first = mount(client); await panel()
    expect(await screen.findByText('Paslanma')).toBeTruthy()
    first.unmount()

    change()
    api.scraps.mockImplementation(() => new Promise(() => { /* the new entry's read is still in flight */ }))
    mount(client); await panel()
    expect(screen.getByText('Hurda kayıtları yükleniyor…')).toBeTruthy()
    expect(screen.queryByText('Paslanma')).toBeNull()
  })
})
