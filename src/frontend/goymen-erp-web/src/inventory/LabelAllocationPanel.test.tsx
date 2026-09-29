import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'
import type {
  AllocationChangeRequest,
  AllocationReleaseRequest,
  LabelAllocation,
  LabelAllocations,
} from './allocationChangeContract'
import type { InventoryLabelDetailWithMovementTimeline } from './labelDetailContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const PARTY = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const ALLOCATION = '77777777-7777-7777-7777-777777777777'
const ORDER = '88888888-8888-8888-8888-888888888888'
const LINE = '99999999-9999-9999-9999-999999999999'
const OTHER_LABEL = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const clients: QueryClient[] = []
const api = vi.hoisted(() => ({ detail: vi.fn(), allocations: vi.fn(), change: vi.fn(), release: vi.fn() }))
const scenario = vi.hoisted(() => ({ canChange: true, report: vi.fn() }))
vi.mock('./labelDetailApi', () => ({ fetchInventoryLabelDetail: api.detail }))
vi.mock('./allocationChangeApi', () => ({
  fetchLabelAllocations: api.allocations, changeAllocation: api.change, releaseAllocation: api.release,
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
    (capability === 'inventory.physical-allocation.write' && scenario.canChange),
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ generation: 1 }), report: scenario.report }),
}))

function detail(): InventoryLabelDetailWithMovementTimeline {
  return {
    labelVersion: '4',
    identity: {
      labelId: LABEL, companyId: COMPANY, code: 'LBL-100', productId: PRODUCT, lotId: LOT,
      heatNumber: null, coilNumber: null, documentStatus: 'Complete', requiredDocuments: [],
      documents: [], prints: [], lot: { id: LOT, heatNumber: null, coilNumber: null },
    },
    balances: {
      labelId: LABEL, physicalKg: '100', freeKg: '100', blockedKg: '0',
      positions: [{ ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
        locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '100' }],
    },
    genealogy: { labelId: LABEL, labelCode: 'LBL-100', windowFull: false, edges: [] },
    movementTimeline: { labelId: LABEL, windowFull: false, entries: [] },
  }
}

function allocation(overrides: Partial<LabelAllocation> = {}): LabelAllocation {
  return {
    allocationId: ALLOCATION, companyId: COMPANY, version: '1', status: 'Active', labelId: LABEL, productId: PRODUCT,
    demandKind: 'sales.order-line', demandId: ORDER, demandLineId: LINE, demandProductVersion: '7',
    allocatedKg: '60', remainingKg: '60', reason: 'Onaylı sipariş', reservedUntilUtc: null,
    createdAtUtc: '2026-09-28T08:00:00Z', updatedAtUtc: '2026-09-28T08:00:00Z', ...overrides,
  }
}

function labelRead(overrides: Partial<LabelAllocations> = {}): LabelAllocations {
  return {
    labelId: LABEL, labelVersion: '4', windowFull: false,
    items: [{ allocation: allocation(), releasedAtUtc: null, releaseReason: null, changes: [] }], ...overrides,
  }
}

function mount(path = `/stok/etiket/${LABEL}`) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}>
    <Routes><Route path="/stok/etiket/:labelId" element={<InventoryLabelDetailPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>)
}

const row = () => screen.findByTestId(`inventory-allocation-${ALLOCATION}`)

beforeEach(() => {
  scenario.canChange = true; scenario.report.mockReset()
  api.detail.mockReset().mockResolvedValue({ type: 'ok-single', data: detail() })
  api.allocations.mockReset().mockResolvedValue({ type: 'ok-single', data: labelRead() })
  api.change.mockReset().mockImplementation(async (_id: string, request: AllocationChangeRequest) => ({
    type: 'applied', replayed: false,
    response: { kind: 'Resize', replaced: null, allocation: allocation({ version: '2', allocatedKg: request.quantityKg,
      remainingKg: request.quantityKg }) },
  }))
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

const RELEASED_ITEM = {
  allocation: allocation({ status: 'Released', remainingKg: '0', version: '2' }),
  releasedAtUtc: '2026-09-29T08:00:00Z', releaseReason: 'Sipariş iptal edildi', changes: [],
}

describe('LabelAllocationPanel on the label detail', () => {
  it('is absent without the server-advertised allocation capability', async () => {
    scenario.canChange = false; mount()
    await screen.findByRole('heading', { name: 'LBL-100' })
    expect(screen.queryByRole('heading', { name: 'Satış tahsisleri' })).toBeNull()
    expect(api.allocations).not.toHaveBeenCalled()
  })

  it('resizes an allocation with a reason at the versions it showed, then rereads the allocations and the label', async () => {
    const user = userEvent.setup(); mount()
    const line = await row()
    await user.click(within(line).getByRole('button', { name: 'Tahsisi revize et' }))
    await user.type(within(line).getByLabelText('Yeni tahsis kg'), '25')
    await user.type(within(line).getByLabelText('Değişiklik gerekçesi'), 'Sipariş azaldı')
    await user.click(within(line).getByRole('button', { name: 'Tahsisi değiştir' }))

    await screen.findByText(/Tahsis güncellendi: 25 kg ayrılmış durumda/)
    const [id, request, context] = api.change.mock.calls[0] as [string, AllocationChangeRequest, { companyId: string }]
    expect(id).toBe(ALLOCATION)
    expect(request).toEqual({
      expectedAllocationVersion: '1', salesOrderId: ORDER, salesOrderLineId: LINE, targetLabelId: LABEL,
      expectedTargetLabelVersion: '4', quantityKg: '25', reason: 'Sipariş azaldı', newAllocationId: null,
    })
    expect(context.companyId).toBe(COMPANY)
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    expect(api.allocations.mock.calls.length).toBeGreaterThanOrEqual(2)
  })

  it('moves to another label with a new allocation id and the target version the user named', async () => {
    api.change.mockImplementationOnce(async (_id: string, request: AllocationChangeRequest) => ({
      type: 'applied', replayed: false,
      response: { kind: 'Move', replaced: allocation({ status: 'Released', remainingKg: '0', version: '2' }),
        allocation: allocation({ allocationId: request.newAllocationId!, labelId: OTHER_LABEL, remainingKg: '60' }) },
    }))
    const user = userEvent.setup(); mount()
    const line = await row()
    await user.click(within(line).getByRole('button', { name: 'Tahsisi revize et' }))
    await user.type(within(line).getByLabelText('Yeni tahsis kg'), '60')
    await user.click(within(line).getByLabelText('Başka etikete taşı'))
    await user.type(within(line).getByLabelText('Hedef etiket kimliği'), OTHER_LABEL)
    await user.type(within(line).getByLabelText('Hedef etiket sürümü'), '9')
    await user.type(within(line).getByLabelText('Değişiklik gerekçesi'), 'Başka rulo')
    await user.click(within(line).getByRole('button', { name: 'Tahsisi değiştir' }))

    await screen.findByText(/Tahsis başka etikete taşındı/)
    const request = api.change.mock.calls[0][1] as AllocationChangeRequest
    expect(request).toMatchObject({ targetLabelId: OTHER_LABEL, expectedTargetLabelVersion: '9', quantityKg: '60' })
    expect(request.newAllocationId).toMatch(/^[0-9a-f-]{36}$/)
  })

  it('refuses the same quantity, a missing reason and an ambiguous decimal before sending', async () => {
    const user = userEvent.setup(); mount()
    const line = await row()
    await user.click(within(line).getByRole('button', { name: 'Tahsisi revize et' }))
    await user.type(within(line).getByLabelText('Yeni tahsis kg'), '60')
    await user.type(within(line).getByLabelText('Değişiklik gerekçesi'), 'Aynı')
    await user.click(within(line).getByRole('button', { name: 'Tahsisi değiştir' }))
    expect(await screen.findByText(/Değişiklik yok/)).toBeTruthy()

    await user.clear(within(line).getByLabelText('Değişiklik gerekçesi'))
    await user.clear(within(line).getByLabelText('Yeni tahsis kg'))
    await user.type(within(line).getByLabelText('Yeni tahsis kg'), '30')
    await user.click(within(line).getByRole('button', { name: 'Tahsisi değiştir' }))
    expect(await screen.findByText(/Değişiklik gerekçesi zorunludur/)).toBeTruthy()

    await user.clear(within(line).getByLabelText('Yeni tahsis kg'))
    await user.type(within(line).getByLabelText('Yeni tahsis kg'), '1.250')
    await user.type(within(line).getByLabelText('Değişiklik gerekçesi'), 'x')
    await user.click(within(line).getByRole('button', { name: 'Tahsisi değiştir' }))
    expect(await screen.findByText(/Yeni tahsis miktarı zorunludur/)).toBeTruthy()
    expect(api.change).not.toHaveBeenCalled()
  })

  it('shows a stale-version refusal by its code and keeps an unknown outcome for the same key', async () => {
    api.change.mockResolvedValueOnce({ type: 'failed', status: 409, code: 'inventory_allocation_version_conflict' })
      .mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup(); mount()
    const line = await row()
    await user.click(within(line).getByRole('button', { name: 'Tahsisi revize et' }))
    await user.type(within(line).getByLabelText('Yeni tahsis kg'), '30')
    await user.type(within(line).getByLabelText('Değişiklik gerekçesi'), 'Revize')
    await user.click(within(line).getByRole('button', { name: 'Tahsisi değiştir' }))
    expect(await screen.findByText(/Tahsis siz bakarken değişti/)).toBeTruthy()

    await user.click(within(line).getByRole('button', { name: 'Tahsisi değiştir' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await waitFor(() => expect(api.change).toHaveBeenCalledTimes(3))
    expect(api.change.mock.calls[2]).toEqual(api.change.mock.calls[1])
    expect(api.change.mock.calls[1][2].idempotencyKey).not.toBe(api.change.mock.calls[0][2].idempotencyKey)
  })

  it('shows the history of a released and a resized allocation without a change control on the released one', async () => {
    const moved = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
    api.allocations.mockResolvedValue({ type: 'ok-single', data: labelRead({ items: [
      { allocation: allocation({ version: '3', allocatedKg: '25', remainingKg: '25' }), releasedAtUtc: null, releaseReason: null,
        changes: [{ changeId: 'cccccccc-cccc-cccc-cccc-cccccccccccc', kind: 'Resize', sourceAllocationId: ALLOCATION,
          sourceVersion: '2', sourceLabelId: LABEL, sourceRemainingKg: '60', targetAllocationId: ALLOCATION, targetLabelId: LABEL,
          targetRemainingKg: '25', reason: 'Sipariş azaldı', changedAtUtc: '2026-09-28T09:00:00Z' }] },
      { allocation: allocation({ allocationId: moved, status: 'Released', remainingKg: '0', version: '2' }),
        releasedAtUtc: '2026-09-28T10:00:00Z', releaseReason: 'Başka rulo',
        changes: [{ changeId: 'dddddddd-dddd-dddd-dddd-dddddddddddd', kind: 'Move', sourceAllocationId: moved,
          sourceVersion: '1', sourceLabelId: LABEL, sourceRemainingKg: '10', targetAllocationId: ALLOCATION,
          targetLabelId: OTHER_LABEL, targetRemainingKg: '10', reason: 'Başka rulo', changedAtUtc: '2026-09-28T10:00:00Z' }] },
    ] }) })
    mount()
    expect(await screen.findByText(/Miktar 60 kg → 25 kg/)).toBeTruthy()
    const released = await screen.findByTestId(`inventory-allocation-${moved}`)
    expect(within(released).getByText('Serbest bırakıldı')).toBeTruthy()
    expect(within(released).getByText(/Başka etikete taşındı/)).toBeTruthy()
    expect(within(released).queryByRole('button', { name: 'Tahsisi revize et' })).toBeNull()
    expect(within(released).queryByRole('button', { name: 'Tahsisi serbest bırak' })).toBeNull()
  })
})

describe('AT-063 release of an allocation on the label detail (existing Inventory release command)', () => {
  beforeEach(() => {
    api.release.mockReset().mockResolvedValue({
      type: 'applied', replayed: false, response: allocation({ status: 'Released', remainingKg: '0', version: '2' }),
    })
  })

  it('releases with a reason at the version it showed, then rereads the allocations and the label', async () => {
    api.allocations.mockResolvedValueOnce({ type: 'ok-single', data: labelRead() })
      .mockResolvedValue({ type: 'ok-single', data: labelRead({ labelVersion: '5', items: [RELEASED_ITEM] }) })
    const user = userEvent.setup(); mount()
    const line = await row()
    await user.click(within(line).getByRole('button', { name: 'Tahsisi serbest bırak' }))
    expect(within(line).getByText(/Kalan 60 kg ayrılmış miktarın tamamı serbest bırakılır/)).toBeTruthy()
    await user.type(within(line).getByLabelText('Serbest bırakma gerekçesi'), '  Sipariş iptal edildi ')
    await user.click(within(line).getByRole('button', { name: 'Serbest bırakmayı onayla' }))

    await screen.findByText(/Tahsis serbest bırakıldı: …77777777 artık aktif değil/)
    const [id, request, context] = api.release.mock.calls[0] as [string, AllocationReleaseRequest, { companyId: string; idempotencyKey: string }]
    expect(id).toBe(ALLOCATION)
    expect(request).toEqual({ expectedAllocationVersion: '1', reason: 'Sipariş iptal edildi' })
    expect(context.companyId).toBe(COMPANY)
    expect(context.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/)
    expect(api.change).not.toHaveBeenCalled()
    await waitFor(() => expect(api.detail).toHaveBeenCalledTimes(2))
    const reread = await row()
    await waitFor(() => expect(within(reread).getByText('Serbest bırakıldı')).toBeTruthy())
    expect(within(reread).getByText(/Serbest bırakma: Sipariş iptal edildi/)).toBeTruthy()
    expect(within(reread).queryByRole('button', { name: 'Tahsisi serbest bırak' })).toBeNull()
  })

  it('refuses a missing reason before sending and can be cancelled', async () => {
    const user = userEvent.setup(); mount()
    const line = await row()
    await user.click(within(line).getByRole('button', { name: 'Tahsisi serbest bırak' }))
    await user.click(within(line).getByRole('button', { name: 'Serbest bırakmayı onayla' }))
    expect(await screen.findByText(/Serbest bırakma gerekçesi zorunludur/)).toBeTruthy()
    await user.click(within(line).getByRole('button', { name: 'Vazgeç' }))
    expect(within(line).queryByLabelText('Serbest bırakma gerekçesi')).toBeNull()
    expect(api.release).not.toHaveBeenCalled()
  })

  it('shows a state refusal by its code, reports 403, and resends an unknown outcome with the same key', async () => {
    api.release.mockResolvedValueOnce({ type: 'failed', status: 409, code: 'inventory_allocation_state_conflict' })
      .mockResolvedValueOnce({ type: 'failed', status: 403, code: null })
      .mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup(); mount()
    const line = await row()
    await user.click(within(line).getByRole('button', { name: 'Tahsisi serbest bırak' }))
    await user.type(within(line).getByLabelText('Serbest bırakma gerekçesi'), 'Bırak')
    await user.click(within(line).getByRole('button', { name: 'Serbest bırakmayı onayla' }))
    expect(await screen.findByText(/Tahsis artık aktif değil .*serbest bırakılamaz/)).toBeTruthy()

    await user.click(within(line).getByRole('button', { name: 'Serbest bırakmayı onayla' }))
    expect(await screen.findByText('Bu şirkette tahsis serbest bırakma yetkiniz yok.')).toBeTruthy()
    expect(scenario.report).toHaveBeenCalledWith(403, { generation: 1 })

    await user.click(within(line).getByRole('button', { name: 'Serbest bırakmayı onayla' }))
    expect(await screen.findByText(/Tahsis serbest bırakmanın sonucu bilinmiyor/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await waitFor(() => expect(api.release).toHaveBeenCalledTimes(4))
    expect(api.release.mock.calls[3]).toEqual(api.release.mock.calls[2])
    expect(api.release.mock.calls[2][2].idempotencyKey).not.toBe(api.release.mock.calls[1][2].idempotencyKey)
  })

  it('arriving from the ST-01 release action opens the release form of the one active allocation, sending nothing', async () => {
    const user = userEvent.setup(); mount(`/stok/etiket/${LABEL}?islem=serbest-birak`)
    const line = await row()
    expect(await within(line).findByRole('group', { name: 'Tahsisi serbest bırak' })).toBeTruthy()
    expect(api.release).not.toHaveBeenCalled()
    await user.click(within(line).getByRole('button', { name: 'Vazgeç' }))
    expect(within(line).queryByRole('group', { name: 'Tahsisi serbest bırak' })).toBeNull()
    expect(within(line).getByRole('button', { name: 'Tahsisi serbest bırak' })).toBeTruthy()
  })

  it('arriving from the release action with several active allocations asks which one, and with none says so', async () => {
    const second = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
    api.allocations.mockResolvedValue({ type: 'ok-single', data: labelRead({ items: [
      { allocation: allocation(), releasedAtUtc: null, releaseReason: null, changes: [] },
      { allocation: allocation({ allocationId: second, allocatedKg: '10', remainingKg: '10' }), releasedAtUtc: null,
        releaseReason: null, changes: [] },
    ] }) })
    mount(`/stok/etiket/${LABEL}?islem=serbest-birak`)
    expect(await screen.findByText('Serbest bırakılacak tahsisi aşağıdan seçin.')).toBeTruthy()
    expect(screen.queryByRole('group', { name: 'Tahsisi serbest bırak' })).toBeNull()
    cleanup()

    api.allocations.mockResolvedValue({ type: 'ok-single', data: labelRead({ items: [RELEASED_ITEM] }) })
    mount(`/stok/etiket/${LABEL}?islem=serbest-birak`)
    expect(await screen.findByText('Bu etikette serbest bırakılacak aktif tahsis yok.')).toBeTruthy()
  })
})
