import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useNavigate, useParams } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'
import type { InventoryLabelDetailWithMovementTimeline } from './labelDetailContract'
import type { ViewOutcome } from '../shell/uiState'
import { productDetailPath } from '../product/capabilities'
import { inventoryOpeningDetailPath } from './capabilities'
import { inventoryLabelDetailPath } from './labelDetailRoute'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const PARTY = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const SOURCE_LABEL = '77777777-7777-7777-7777-777777777777'
const TRANSFORMATION = '88888888-8888-8888-8888-888888888888'
const MOVEMENT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const OTHER_COMPANY = '99999999-9999-9999-9999-999999999999'
const clients: QueryClient[] = []
const api = vi.hoisted(() => ({ detail: vi.fn() }))
const documents = vi.hoisted(() => ({ panel: vi.fn() }))
const scenario = vi.hoisted(() => ({
  companyId: '11111111-1111-1111-1111-111111111111', namespace: 'SENTETIK-a' as string | null,
  generation: 1, canReadProduct: true, canReadLabel: true, canOpenOpening: true, report: vi.fn(),
}))
vi.mock('./labelDetailApi', () => ({ fetchInventoryLabelDetail: api.detail }))
vi.mock('../documents/DocumentsPanel', () => ({
  DocumentsPanel: (props: unknown) => {
    documents.panel(props)
    return <section data-testid="inventory-label-documents" />
  },
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: scenario.generation,
    activeContext: { userId: 'actor-a', companyId: scenario.companyId, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => scenario.namespace === null ? null : [scenario.namespace],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) =>
    (capability === 'product.items.detail' && scenario.canReadProduct) ||
    (capability === 'inventory.stock-positions.list' && scenario.canReadLabel) ||
    (capability === 'inventory.opening-stock.record' && scenario.canOpenOpening),
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ generation: scenario.generation }), report: scenario.report }),
}))
function fixture(): InventoryLabelDetailWithMovementTimeline {
  return {
    labelVersion: '3',
    identity: {
      labelId: LABEL, companyId: COMPANY, code: 'LBL-100', productId: PRODUCT, lotId: LOT,
      heatNumber: null, coilNumber: 'COIL-9', classification: null, documentStatus: 'Complete', requiredDocuments: ['MTC'],
      documents: [{ kind: 'MTC', attachedAtUtc: '2026-09-24T08:00:00Z' }],
      prints: [{ sequence: 1, printedAtUtc: '2026-09-24T08:01:00Z', reason: null }],
      lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null },
    },
    balances: {
      labelId: LABEL, physicalKg: '1250.5', freeKg: '1200', blockedKg: '50.5',
      positions: [
        { ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
          locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '1200' },
        { ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
          locationId: LOCATION, productId: PRODUCT, block: 'Blocked', quantityKg: '50.5' },
      ],
    },
    genealogy: {
      labelId: LABEL, labelCode: 'LBL-100', windowFull: false,
      edges: [{
        transformationId: TRANSFORMATION, kind: 'Split', occurredAtUtc: '2026-09-24T07:59:00Z',
        sourceLabelId: SOURCE_LABEL, sourceCode: 'LBL-090', sourceLabelVersion: '2', sourceQuantityKg: '1250.5',
        outputLabelId: LABEL, outputCode: 'LBL-100', outputLabelVersion: '3', outputQuantityKg: '1250.5',
      }],
    },
    movementTimeline: {
      labelId: LABEL, windowFull: false,
      entries: [{
        movementId: MOVEMENT, kind: 'Receipt', occurredAtUtc: '2026-09-24T07:58:00Z',
        sourceKind: 'inventory.opening-count', sourceId: TRANSFORMATION, sourceLineId: SOURCE_LABEL,
        sourceGrain: 'Line',
        lines: [{ sequence: 1, direction: 'In', ownerKind: 'Company', ownerId: PARTY,
          custodianKind: 'Company', custodianId: PARTY, locationId: LOCATION,
          productId: PRODUCT, block: 'Free', quantityKg: '1250.5' }],
      }],
    },
  }
}
function ProductRouteReceipt() {
  const { productId } = useParams(); const navigate = useNavigate()
  return <><h1>Product route: {productId}</h1><button type="button" onClick={() => {
    scenario.generation += 1; void navigate(-1)
  }}>Test back</button></>
}
function OpeningRouteReceipt() {
  const { countId } = useParams(); const navigate = useNavigate()
  return <><h1>Opening route: {countId}</h1><button type="button" onClick={() => {
    scenario.generation += 1; void navigate(-1)
  }}>Test opening back</button></>
}
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 60_000 } } })
  clients.push(client)
  const element = () => <QueryClientProvider client={client}><MemoryRouter initialEntries={[`/stok/etiket/${LABEL}`]}>
    <Routes>
      <Route path="/stok/etiket/:labelId" element={<InventoryLabelDetailPage />} />
      <Route path="/urun/:productId" element={<ProductRouteReceipt />} />
      <Route path="/stok/acilis/:countId" element={<OpeningRouteReceipt />} />
    </Routes>
  </MemoryRouter></QueryClientProvider>
  const view = render(element()); return { ...view, refresh: () => view.rerender(element()) }
}
const ready = () => screen.findByRole('heading', { name: 'LBL-100' })
const productLink = () => screen.queryByRole('link', { name: 'Ürün kartı' })
beforeEach(() => {
  scenario.companyId = COMPANY; scenario.namespace = 'SENTETIK-a'; scenario.generation = 1
  scenario.canReadProduct = true; scenario.canReadLabel = true; scenario.canOpenOpening = true; scenario.report.mockReset()
  api.detail.mockReset().mockResolvedValue({ type: 'ok-single', data: fixture() })
  documents.panel.mockReset()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('InventoryLabelDetailPage', () => {
  it('reopens one actual label identity together with its ownership, location and quantities', async () => {
    mount(); await ready()
    expect(screen.getByText(LABEL)).not.toBeNull()
    expect(screen.getByText(PRODUCT)).not.toBeNull()
    expect(screen.getAllByText(LOCATION)).toHaveLength(3)
    expect(screen.getByText('1250.5 kg', { selector: 'dd' })).not.toBeNull()
    expect(screen.getByText('1200 kg', { selector: 'td' })).not.toBeNull()
    expect(screen.getByRole('heading', { name: 'Soy ağacı' })).not.toBeNull()
    expect(screen.getByRole('heading', { name: 'Stok hareketleri' })).not.toBeNull()
    expect(screen.getByText('inventory.opening-count')).not.toBeNull()
    expect(screen.getByText('1250.5 kg', { selector: 'td' })).not.toBeNull()
    expect(screen.getByRole('link', { name: 'LBL-090' }).getAttribute('href')).toBe(inventoryLabelDetailPath(SOURCE_LABEL))
    expect(screen.getByRole('link', { name: 'LBL-100' }).getAttribute('href')).toBe(inventoryLabelDetailPath(LABEL))
    expect(screen.getByText('Gerekli belgeler: MTC')).not.toBeNull()
    expect(api.detail).toHaveBeenCalledWith(COMPANY, LABEL, expect.any(AbortSignal))
    expect(documents.panel).toHaveBeenCalledWith({
      ownerKind: 'inventory.material-label',
      ownerId: LABEL,
      companyId: COMPANY,
      namespace: 'SENTETIK-a',
      entryGeneration: 1,
    })
  })
  it('renders transfer dispatch and acceptance movements in the existing timeline', async () => {
    const base = fixture()
    api.detail.mockResolvedValue({ type: 'ok-single', data: {
      ...base,
      movementTimeline: { ...base.movementTimeline, entries: [
        { ...base.movementTimeline.entries[0], kind: 'TransferDispatch', lines: [
          { ...base.movementTimeline.entries[0].lines[0], direction: 'Out' },
        ] },
        { ...base.movementTimeline.entries[0], movementId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          kind: 'TransferAccept', lines: base.movementTimeline.entries[0].lines },
      ] },
    } })

    mount(); await ready()

    expect(screen.getByText('Transfer çıkışı')).not.toBeNull()
    expect(screen.getByText('Transfer kabulü')).not.toBeNull()
  })
  it('keeps a forbidden read in the shared terminal error state', async () => {
    api.detail.mockResolvedValueOnce({ type: 'http', status: 403, body: { status: 403 } }); mount()
    expect(await screen.findByText(/yetkiniz yok/i)).not.toBeNull()
    expect(screen.queryByRole('heading', { name: 'LBL-100' })).toBeNull(); expect(productLink()).toBeNull()
    expect(scenario.report).toHaveBeenCalledWith(403, { generation: 1 })
  })
  it('opens the actual canonical Product id, then rereads the same label after return', async () => {
    const user = userEvent.setup(); mount(); await ready()
    expect(productLink()?.getAttribute('href')).toBe(productDetailPath(PRODUCT))
    await user.click(screen.getByRole('link', { name: 'Ürün kartı' }))
    await screen.findByRole('heading', { name: `Product route: ${PRODUCT}` })
    api.detail.mockResolvedValueOnce({ type: 'ok-single', data: { ...fixture(), labelVersion: '4' } })
    await user.click(screen.getByRole('button', { name: 'Test back' })); await ready()
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    expect(screen.getByText(LABEL)).toBeTruthy(); expect(screen.getByText('4', { selector: 'dd' })).toBeTruthy()
  })
  it('opens the stored opening-count source and rereads the same label on return', async () => {
    const user = userEvent.setup(); mount(); await ready()
    const link = screen.getByRole('link', { name: 'Açılış sayımını aç' })
    expect(link.getAttribute('href')).toBe(inventoryOpeningDetailPath(TRANSFORMATION))
    await user.click(link)
    await screen.findByRole('heading', { name: `Opening route: ${TRANSFORMATION}` })
    api.detail.mockResolvedValueOnce({ type: 'ok-single', data: { ...fixture(), labelVersion: '4' } })
    await user.click(screen.getByRole('button', { name: 'Test opening back' })); await ready()
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    expect(screen.getByText('4', { selector: 'dd' })).toBeTruthy()
  })
  it('keeps the opening source inert without its existing capability', async () => {
    scenario.canOpenOpening = false; mount(); await ready()
    expect(screen.queryByRole('link', { name: 'Açılış sayımını aç' })).toBeNull()
    expect(screen.getByText('inventory.opening-count')).toBeTruthy()
  })
  it('keeps other source kinds inert even with the opening capability', async () => {
    const data = fixture()
    api.detail.mockResolvedValue({ type: 'ok-single', data: {
      ...data, movementTimeline: { ...data.movementTimeline, entries: [{
        ...data.movementTimeline.entries[0], kind: 'CountAdjustment', sourceKind: 'inventory.stock-count',
      }] },
    } })
    mount(); await ready()
    expect(screen.queryByRole('link', { name: 'Açılış sayımını aç' })).toBeNull()
    expect(screen.getByText('inventory.stock-count')).toBeTruthy()
  })
  it('keeps the Product identity and card label inert without the Product detail capability', async () => {
    scenario.canReadProduct = false; mount(); await ready()
    expect(productLink()).toBeNull(); expect(screen.getByText(PRODUCT)).toBeTruthy()
    expect(screen.getByText('Ürün kartı')).toBeTruthy()
  })
  it('keeps genealogy labels inert without the inventory label read capability', async () => {
    scenario.canReadLabel = false; mount(); await ready()
    expect(screen.queryByRole('link', { name: 'LBL-090' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'LBL-100' })).toBeNull()
    expect(screen.getByRole('cell', { name: /^LBL-090/ })).toBeTruthy()
  })
  it('removes navigation immediately on a capability change without changing the label facts', async () => {
    const view = mount(); await ready(); scenario.canReadProduct = false; view.refresh()
    expect(productLink()).toBeNull(); expect(screen.getByText(LABEL)).toBeTruthy()
  })
  it('does not show an old cached Product link while a new entry is being read', async () => {
    const view = mount(); await ready()
    let resolve!: (result: ViewOutcome<InventoryLabelDetailWithMovementTimeline>) => void
    api.detail.mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    scenario.generation += 1; view.refresh()
    expect(productLink()).toBeNull(); expect(screen.queryByText(PRODUCT)).toBeNull()
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    await act(async () => resolve({ type: 'ok-single', data: fixture() })); await ready()
  })
  it('does not resurrect a delayed reply from the previous company', async () => {
    let resolve!: (result: ViewOutcome<InventoryLabelDetailWithMovementTimeline>) => void
    api.detail.mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    const view = mount(); await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(1))
    scenario.companyId = OTHER_COMPANY; scenario.namespace = 'SENTETIK-b'
    documents.panel.mockClear()
    api.detail.mockResolvedValue({ type: 'http', status: 404 }); view.refresh()
    await screen.findByText('Bulunamadı — etiket')
    await act(async () => resolve({ type: 'ok-single', data: fixture() }))
    expect(productLink()).toBeNull(); expect(screen.queryByText(PRODUCT)).toBeNull()
    expect(documents.panel).not.toHaveBeenCalled()
  })
  it('reports 401 and hides all record navigation', async () => {
    api.detail.mockResolvedValueOnce({ type: 'http', status: 401 }); mount()
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(401, { generation: 1 }))
    expect(productLink()).toBeNull()
  })
  it.each(['network-error', 'malformed'] as const)('does not navigate on %s and retains read retry', async (type) => {
    api.detail.mockResolvedValueOnce({ type }); const user = userEvent.setup(); mount()
    await screen.findByRole('button', { name: 'Tekrar dene' }); expect(productLink()).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' })); await ready(); expect(productLink()).toBeTruthy()
  })
  it.each([
    { ...fixture(), identity: { ...fixture().identity, companyId: OTHER_COMPANY } },
    { ...fixture(), identity: { ...fixture().identity, labelId: LOT } },
    { ...fixture(), balances: { ...fixture().balances, labelId: LOT } },
    { ...fixture(), genealogy: { ...fixture().genealogy, labelId: LOT } },
    { ...fixture(), movementTimeline: { ...fixture().movementTimeline, labelId: LOT } },
  ])('does not derive a link from mismatched loaded identities', async (data) => {
    api.detail.mockResolvedValue({ type: 'ok-single', data }); mount()
    await screen.findByText('Beklenmeyen yanıt — etiket'); expect(productLink()).toBeNull()
  })
  it('does not read a record without a resolved context', () => {
    scenario.namespace = null; mount(); expect(api.detail).not.toHaveBeenCalled(); expect(productLink()).toBeNull()
  })
})
