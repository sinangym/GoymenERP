import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'
import type { InventoryStockPosition } from './contract'
import type { InventoryLabelDetailWithMovementTimeline } from './labelDetailContract'
import type { LabelTransformationRequest } from './labelTransformationContract'
import { inventoryLabelDetailPath } from './labelDetailRoute'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const PARTY = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const OTHER = '77777777-7777-7777-7777-777777777777'
const clients: QueryClient[] = []
const api = vi.hoisted(() => ({ detail: vi.fn(), record: vi.fn(), positions: vi.fn() }))
const scenario = vi.hoisted(() => ({ canTransform: true, report: vi.fn() }))
vi.mock('./labelDetailApi', () => ({ fetchInventoryLabelDetail: api.detail }))
vi.mock('./labelTransformationApi', () => ({ recordLabelTransformation: api.record }))
vi.mock('./api', async (importOriginal) => ({ ...await importOriginal<typeof import('./api')>(), fetchStockPositions: api.positions }))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: COMPANY, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => ['SENTETIK-a'],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: (_shell: unknown, capability: string) =>
    capability === 'inventory.stock-positions.list' ||
    (capability === 'inventory.label-transformation.record' && scenario.canTransform),
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ generation: 1 }), report: scenario.report }),
}))

function detail(overrides: Partial<InventoryLabelDetailWithMovementTimeline> = {}): InventoryLabelDetailWithMovementTimeline {
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
    ...overrides,
  }
}

function candidate(overrides: Partial<InventoryStockPosition> = {}): InventoryStockPosition {
  return {
    companyId: COMPANY, labelId: OTHER, labelVersion: '5', labelCode: 'LBL-200', productId: PRODUCT,
    lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null }, heatNumber: null, coilNumber: 'COIL-9',
    documentStatus: 'Complete', ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
    locationId: LOCATION, physicalKg: '100', freePhysicalKg: '100', blockedPhysicalKg: '0',
    activeAllocationKg: '0', availableKg: '100', ...overrides,
  }
}

function sentRequest(): LabelTransformationRequest { return api.record.mock.calls.at(-1)![0] as LabelTransformationRequest }

function applied(request: LabelTransformationRequest, replayed = false) {
  return {
    type: 'applied', replayed,
    response: {
      transformationId: request.transformationId, companyId: COMPANY, kind: request.kind,
      occurredAtUtc: request.occurredAtUtc, reason: request.reason,
      sourceLabelIds: request.sources.map((source) => source.labelId),
      outputLabelIds: request.outputs.map((output) => output.labelId),
    },
  }
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[`/stok/etiket/${LABEL}`]}>
    <Routes><Route path="/stok/etiket/:labelId" element={<InventoryLabelDetailPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>)
}

const panel = () => screen.findByRole('heading', { name: 'Bölme / birleştirme' })

async function fillSplit(user: ReturnType<typeof userEvent.setup>, rows: readonly [string, string][]) {
  for (const [index, [code, kg]] of rows.entries()) {
    await user.type(screen.getByLabelText(`Çıktı ${index + 1} etiket kodu`), code)
    await user.type(screen.getByLabelText(`Çıktı ${index + 1} kg`), kg)
  }
  await user.type(screen.getByLabelText('Gerekçe'), 'Müşteri paket ağırlığı')
}

beforeEach(() => {
  scenario.canTransform = true; scenario.report.mockReset()
  api.detail.mockReset().mockResolvedValue({ type: 'ok-single', data: detail() })
  api.record.mockReset().mockImplementation(async (request: LabelTransformationRequest) => applied(request))
  api.positions.mockReset().mockResolvedValue({ type: 'ok-single', data: { items: [candidate()], searchWindowFull: false } })
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()); sessionStorage.clear() })

describe('LabelTransformationPanel on the label detail', () => {
  it('is absent without the server-advertised command capability', async () => {
    scenario.canTransform = false; mount()
    await screen.findByRole('heading', { name: 'LBL-100' })
    expect(screen.queryByRole('heading', { name: 'Bölme / birleştirme' })).toBeNull()
  })

  it('splits the whole label, then rereads it and links the new labels', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600,5'], ['LBL-100-B', '650']])
    expect(screen.getByText('Kaynak: 1250.5 kg · Çıktı toplamı: 1250.5 kg')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))

    await screen.findByRole('heading', { name: 'Bölme kaydedildi' })
    const request = sentRequest()
    expect(request).toMatchObject({
      kind: 'Split', sources: [{ labelId: LABEL, expectedLabelVersion: '3' }], reason: 'Müşteri paket ağırlığı',
    })
    expect(request.outputs.map((output) => [output.code, output.quantityKg])).toEqual([['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    expect(request.occurredAtUtc).toMatch(/Z$/)
    const context = api.record.mock.calls[0][1] as { companyId: string; idempotencyKey: string }
    expect(context.companyId).toBe(COMPANY)
    expect(screen.getByRole('link', { name: 'LBL-100-A' }).getAttribute('href')).toBe(inventoryLabelDetailPath(request.outputs[0].labelId))
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    expect(sessionStorage.length).toBe(0)
  })

  it('refuses a split whose outputs do not add up to the source, without sending', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    expect((await screen.findAllByRole('alert')).some((node) => /1250\.5 kg/.test(node.textContent ?? ''))).toBe(true)
    expect(api.record).not.toHaveBeenCalled()
  })

  it('refuses duplicate output codes and more than three kg decimals', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-X', '600.5'], ['LBL-X', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    expect(await screen.findByText(/Çıktı etiket kodları birbirinden farklı olmalıdır/)).toBeTruthy()
    await user.clear(screen.getByLabelText('Çıktı 2 etiket kodu')); await user.type(screen.getByLabelText('Çıktı 2 etiket kodu'), 'LBL-Y')
    await user.clear(screen.getByLabelText('Çıktı 2 kg')); await user.type(screen.getByLabelText('Çıktı 2 kg'), '649,9999')
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    expect(await screen.findByText(/Çıktı 2: etiket kodu ve en fazla 3 ondalıklı/)).toBeTruthy()
    expect(api.record).not.toHaveBeenCalled()
  })

  it('keeps an unknown outcome and resends exactly the same key and payload', async () => {
    api.record.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    await screen.findByText(/sonucu bilinmiyor/)
    const [firstRequest, firstContext] = api.record.mock.calls[0]
    expect(sessionStorage.length).toBe(1)
    await user.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await screen.findByRole('heading', { name: 'Bölme kaydedildi' })
    expect(api.record.mock.calls[1]).toEqual([firstRequest, firstContext])
    expect(sessionStorage.length).toBe(0)
  })

  it('restores a remembered unknown attempt after the page is reopened', async () => {
    api.record.mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
    const user = userEvent.setup(); const view = mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    await screen.findByText(/sonucu bilinmiyor/)
    view.unmount(); mount()
    await screen.findByText(/sonucu bilinmiyor/)
    expect(screen.queryByRole('button', { name: 'Bölmeyi kaydet' })).toBeNull()
  })

  it('shows the writer refusal and lets the person correct the form', async () => {
    api.record.mockResolvedValueOnce({ type: 'failed', status: 409, code: 'inventory_label_active_allocation_conflict' })
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    expect(await screen.findByText(/aktif tahsis var/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Bölmeyi kaydet' }) as HTMLButtonElement).disabled).toBe(false)
    expect(sessionStorage.length).toBe(0)
  })

  it('reports a forbidden command to the auth boundary', async () => {
    api.record.mockResolvedValueOnce({ type: 'failed', status: 403, code: 'forbidden' })
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    await screen.findByText(/yetkiniz yok/)
    expect(scenario.report).toHaveBeenCalledWith(403, { generation: 1 })
  })

  it('merges only compatible labels found in stock and sends their whole physical total', async () => {
    api.positions.mockResolvedValue({ type: 'ok-single', data: { searchWindowFull: false, items: [
      candidate(),
      candidate({ labelId: '88888888-8888-8888-8888-888888888888', labelCode: 'LBL-300', lot: { id: OTHER, heatNumber: null, coilNumber: null } }),
      candidate({ labelId: '99999999-9999-9999-9999-999999999999', labelCode: 'LBL-400', activeAllocationKg: '10' }),
    ] } })
    const user = userEvent.setup(); mount(); await panel()
    await user.click(screen.getByLabelText('Birleştir'))
    await user.type(screen.getByLabelText('Etiket ara'), 'LBL')
    await user.click(screen.getByRole('button', { name: 'Etiketleri getir' }))
    const list = await screen.findByRole('list', { name: 'Aday etiketler' })
    expect(within(list).getByText('(Farklı ürün, lot veya iz bilgisi)')).toBeTruthy()
    expect(within(list).getByText('(Aktif tahsisi var)')).toBeTruthy()
    await user.click(within(list).getByRole('button', { name: 'Ekle: LBL-200' }))
    expect(screen.getByText('Birleşik paket: LBL-100 + LBL-200 = 1350.5 kg')).toBeTruthy()
    await user.type(screen.getByLabelText('Birleşik paket etiket kodu'), 'LBL-100-200')
    await user.type(screen.getByLabelText('Gerekçe'), 'Sevk paketi')
    await user.click(screen.getByRole('button', { name: 'Birleştirmeyi kaydet' }))

    await screen.findByRole('heading', { name: 'Birleştirme kaydedildi' })
    const request = sentRequest()
    expect(request.kind).toBe('Merge')
    expect(request.sources).toEqual([
      { labelId: LABEL, expectedLabelVersion: '3' }, { labelId: OTHER, expectedLabelVersion: '5' },
    ])
    expect(request.outputs).toHaveLength(1)
    expect(request.outputs[0]).toMatchObject({ code: 'LBL-100-200', quantityKg: '1350.5' })
    expect(api.positions).toHaveBeenCalledWith(COMPANY, 'LBL', expect.any(AbortSignal))
  })

  it('offers no command for a label without exactly one physical position', async () => {
    api.detail.mockResolvedValue({ type: 'ok-single', data: detail({
      balances: { labelId: LABEL, physicalKg: '0', freeKg: '0', blockedKg: '0', positions: [] },
    }) })
    mount(); await panel()
    expect(screen.getByText(/tek bir fiziksel pozisyonda stoğu olmalı/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Bölmeyi kaydet' })).toBeNull()
  })

  it('settles the remembered attempt when a success lands after the panel is gone', async () => {
    let resolve!: (value: unknown) => void
    api.record.mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    const user = userEvent.setup(); const view = mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    await screen.findByText('Kaydediliyor…')
    expect(sessionStorage.length).toBe(1)
    view.unmount()
    await act(async () => resolve(applied(sentRequest())))
    expect(sessionStorage.length).toBe(0)
    mount(); await panel()
    expect(screen.queryByText(/sonucu bilinmiyor/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Bölmeyi kaydet' })).toBeTruthy()
  })

  it('keeps an unknown attempt after unmount so the reopen still offers the same-key resend', async () => {
    let resolve!: (value: unknown) => void
    api.record.mockImplementationOnce(() => new Promise((done) => { resolve = done }))
    const user = userEvent.setup(); const view = mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    await screen.findByText('Kaydediliyor…')
    view.unmount()
    await act(async () => resolve({ type: 'unknown', reason: 'network-error' }))
    expect(sessionStorage.length).toBe(1)
  })

  it('lets the person abandon an unknown attempt, rereads the label and reopens the form', async () => {
    api.record.mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' })
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    await screen.findByText(/sonucu bilinmiyor/)
    await user.click(screen.getByRole('button', { name: 'Denemeyi bırak' }))
    expect(sessionStorage.length).toBe(0)
    expect(screen.getByRole('button', { name: 'Bölmeyi kaydet' })).toBeTruthy()
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    expect(api.record).toHaveBeenCalledTimes(1)
  })

  it('treats a coded pre-write 503 as definite: nothing recorded, form editable, no resend offered', async () => {
    api.record.mockResolvedValueOnce({ type: 'failed', status: 503, code: 'inventory_permission_unregistered' })
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.5'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    expect(await screen.findByText(/henüz yayımlanmadı; hiçbir şey kaydedilmedi/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Aynı anahtarla yeniden gönder' })).toBeNull()
    expect((screen.getByRole('button', { name: 'Bölmeyi kaydet' }) as HTMLButtonElement).disabled).toBe(false)
    expect(sessionStorage.length).toBe(0)
  })

  it('refuses output codes that differ only by case, as the writer does', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['K-1', '600.5'], ['k-1', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    expect(await screen.findByText(/birbirinden farklı olmalıdır/)).toBeTruthy()
    expect(api.record).not.toHaveBeenCalled()
  })

  it('asks for a comma when a kg value is ambiguous', async () => {
    const user = userEvent.setup(); mount(); await panel()
    await fillSplit(user, [['LBL-100-A', '600.500'], ['LBL-100-B', '650']])
    await user.click(screen.getByRole('button', { name: 'Bölmeyi kaydet' }))
    expect(await screen.findByText(/"600.500" belirsiz. Ondalık için virgül kullanın/)).toBeTruthy()
    expect(api.record).not.toHaveBeenCalled()
  })

  it('compares merge candidates by label id regardless of case and refuses multi-position labels', async () => {
    const twice = '88888888-8888-8888-8888-888888888888'
    api.positions.mockResolvedValue({ type: 'ok-single', data: { searchWindowFull: false, items: [
      candidate({ labelId: LABEL.toUpperCase(), labelCode: 'LBL-100' }),
      candidate({ labelId: twice, labelCode: 'LBL-TWO' }),
      candidate({ labelId: twice.toUpperCase(), labelCode: 'LBL-TWO', locationId: PARTY }),
      candidate(),
    ] } })
    const user = userEvent.setup(); mount(); await panel()
    await user.click(screen.getByLabelText('Birleştir'))
    await user.click(screen.getByRole('button', { name: 'Etiketleri getir' }))
    const list = await screen.findByRole('list', { name: 'Aday etiketler' })
    expect(within(list).getByText('(Bu etiketin kendisi)')).toBeTruthy()
    expect(within(list).getAllByText('(Birden fazla fiziksel pozisyonu var)')).toHaveLength(2)
    await user.click(within(list).getByRole('button', { name: 'Ekle: LBL-200' }))
    api.positions.mockResolvedValue({ type: 'ok-single', data: { searchWindowFull: false, items: [
      candidate({ labelId: OTHER.toUpperCase() }),
    ] } })
    await user.click(screen.getByRole('button', { name: 'Etiketleri getir' }))
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Aday etiketler' })).getByText('(seçildi)')).toBeTruthy())
    expect(within(screen.getByRole('list', { name: 'Seçilen etiketler' })).getAllByRole('listitem')).toHaveLength(1)
  })
})
