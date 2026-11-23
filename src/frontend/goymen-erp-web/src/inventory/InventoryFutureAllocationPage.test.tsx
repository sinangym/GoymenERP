import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryFutureAllocationPage } from './InventoryFutureAllocationPage'
import { INVENTORY_FUTURE_ALLOCATION_ROUTE, INVENTORY_FUTURE_ALLOCATION_WRITE_CAPABILITY, inventoryFutureAllocationPath } from './capabilities'
import { FUTURE_ALLOCATION_TEXT as T } from './futureAllocationText'

const id = '11111111-1111-4111-8111-111111111111'
const api = vi.hoisted(() => ({ list: vi.fn(), options: vi.fn(), send: vi.fn() }))
const scenario = vi.hoisted(() => ({ write: true, report: vi.fn(), namespace: 'SENTETIK-context' }))
vi.mock('./futureAllocationApi', () => ({ fetchFutureAllocations: api.list, fetchFutureSupplyOptions: api.options, sendFutureAllocation: api.send }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 1, sessionEpoch: 1, activeContext: {
  userId: 'SENTETIK-actor', companyId: '11111111-1111-4111-8111-111111111111', scopeMode: 'single' } }) }))
vi.mock('../shell/shellState', () => ({ contextKey: () => [scenario.namespace], contextKeyString: () => scenario.namespace,
  hasCapability: (_: unknown, cap: string) => cap === 'inventory.future-allocation.write' && scenario.write }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'SENTETIK-snapshot', report: scenario.report }) }))

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[inventoryFutureAllocationPath(id, id)]}>
    <Routes><Route path={INVENTORY_FUTURE_ALLOCATION_ROUTE} element={<InventoryFutureAllocationPage />} /></Routes>
  </MemoryRouter></QueryClientProvider>)
}
beforeEach(() => {
  sessionStorage.clear(); vi.clearAllMocks(); scenario.write = true; scenario.namespace = 'SENTETIK-context'
  api.list.mockResolvedValue({ type: 'ok-single', data: { companyId: id, salesOrderId: id, salesOrderLineId: id, items: [], windowFull: false, unitCodes: {}, evaluatedOn: '2026-11-17', schedules: {} } })
  api.options.mockResolvedValue({ type: 'ok-single', data: { companyId: id, salesOrderId: id, salesOrderLineId: id, productId: id,
    productVersion: '1', demandUnitId: id, demandUnitCode: 'kg', openDemandQuantity: '100', items: [{ purchaseOrderId: id, purchaseOrderLineId: id,
      lineNo: 1, supplierCode: 'SYN', supplierName: 'Sentetik tedarikçi', unitId: id, unitCode: 'kg', allocatableQuantity: '50', reservedQuantity: '10', availableQuantity: '40' }], nextCursor: null } })
})
afterEach(cleanup)
describe('future allocation workflow', () => {
  it('shows the server overdue warning without changing the active allocation or starting a command', async () => {
    scenario.write = false
    api.list.mockResolvedValue({ type: 'ok-single', data: { companyId: id, salesOrderId: id, salesOrderLineId: id,
      evaluatedOn: '2099-11-17', windowFull: false, unitCodes: { [id]: 'kg' },
      schedules: { [id]: { status: 'Scheduled', deliveryDate: '2099-11-16', isOverdue: true } },
      items: [{ allocationId: id, status: 'Active', unitId: id, allocatedQuantity: '10', remainingQuantity: '10', consumedQuantity: '0', releasedQuantity: '0' }] } })
    mount()
    expect(await screen.findByText(/2099-11-16 — Termin geçti/)).not.toBeNull()
    expect(screen.getByText(T.statuses.Active)).not.toBeNull()
    expect(api.send).not.toHaveBeenCalled()
  })
  it('selects an owner source and retries exactly the same payload after 503', async () => {
    api.send.mockResolvedValueOnce({ type: 'unknown', reason: 'service-unavailable' }).mockResolvedValueOnce({ type: 'failed', status: 422, code: null })
    mount()
    await screen.findByRole('option', { name: /Sentetik tedarikçi/ })
    fireEvent.change(screen.getByRole('combobox'), { target: { value: id } })
    fireEvent.change(screen.getByRole('textbox', { name: /Ayrılacak miktar/ }), { target: { value: '5' } })
    fireEvent.change(screen.getByRole('textbox', { name: T.reason }), { target: { value: 'Sentetik gerekçe' } })
    fireEvent.click(screen.getByRole('button', { name: T.create }))
    fireEvent.click(await screen.findByRole('button', { name: T.retry }))
    await screen.findByText(T.refused)
    expect(api.send.mock.calls[0]).toEqual(api.send.mock.calls[1])
    expect(api.send.mock.calls[0][3].payload).toMatchObject({ salesOrderId: id, salesOrderLineId: id,
      purchaseOrderId: id, purchaseOrderLineId: id, unitId: id, quantityText: '5' })
    expect(sessionStorage.length).toBe(0)
  })
  it('keeps an unknown create across an authority refresh (new capability generation) and retries the same attempt', async () => {
    api.send.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' }).mockResolvedValueOnce({ type: 'failed', status: 422, code: null })
    const first = mount()
    await screen.findByRole('option', { name: /Sentetik tedarikçi/ })
    fireEvent.change(screen.getByRole('combobox'), { target: { value: id } })
    fireEvent.change(screen.getByRole('textbox', { name: /Ayrılacak miktar/ }), { target: { value: '5' } })
    fireEvent.change(screen.getByRole('textbox', { name: T.reason }), { target: { value: 'Sentetik gerekçe' } })
    fireEvent.click(screen.getByRole('button', { name: T.create }))
    await screen.findByRole('button', { name: T.retry })
    first.unmount()
    scenario.namespace = 'SENTETIK-context-after-capability-refresh'
    mount()
    fireEvent.click(await screen.findByRole('button', { name: T.retry }))
    await screen.findByText(T.refused)
    expect(api.send).toHaveBeenCalledTimes(2)
    expect(api.send.mock.calls[1]).toEqual(api.send.mock.calls[0])
  })

  it('recalls a stored attempt only with exactly its own members, at the top level and in the payload', async () => {
    const storeKey = `goymen.inventory.future-allocation.v1:1:SENTETIK-actor:${id}:${id}:${id}`
    const create = { kind: 'create', key: 'SENTETIK-key', payload: { allocationId: id, salesOrderId: id, salesOrderLineId: id,
      purchaseOrderId: id, purchaseOrderLineId: id, unitId: id, quantityText: '5', reason: 'Sentetik gerekçe' } }
    const release = { kind: 'release', key: 'SENTETIK-key', allocationId: id,
      payload: { expectedAllocationVersion: '1', quantityText: null, reasonCode: 'manual-release', reasonText: 'Sentetik gerekçe' } }
    const recalled = async (value: unknown) => {
      sessionStorage.setItem(storeKey, JSON.stringify(value))
      mount()
      await screen.findByRole('heading', { level: 1, name: T.title })
      const retry = screen.queryByRole('button', { name: T.retry }) !== null
      cleanup()
      return retry
    }
    expect(await recalled(create)).toBe(true)
    expect(await recalled(release)).toBe(true)
    expect(await recalled({ ...create, extra: 1 })).toBe(false)
    expect(await recalled({ ...release, extra: 1 })).toBe(false)
    expect(await recalled({ ...create, allocationId: id })).toBe(false)
    expect(await recalled({ ...release, allocationId: undefined })).toBe(false)
    expect(await recalled({ ...create, payload: { ...create.payload, extra: 1 } })).toBe(false)
    expect(await recalled({ ...create, payload: { ...create.payload, reason: undefined } })).toBe(false)
    expect(await recalled({ ...release, payload: { ...release.payload, extra: 1 } })).toBe(false)
    expect(await recalled({ ...release, payload: { ...release.payload, reasonText: undefined } })).toBe(false)
    expect(api.send).not.toHaveBeenCalled()
  })

  it('does not fetch source choices or display write controls without the separate write capability', async () => {
    scenario.write = false
    mount()
    await screen.findByRole('heading', { level: 1, name: T.title })
    await waitFor(() => expect(api.list).toHaveBeenCalled())
    expect(api.options).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: T.create })).toBeNull()
    expect(INVENTORY_FUTURE_ALLOCATION_WRITE_CAPABILITY).toBe('inventory.future-allocation.write')
  })
})
