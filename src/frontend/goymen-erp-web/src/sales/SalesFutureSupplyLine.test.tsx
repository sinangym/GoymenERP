import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesFutureSupplyLine } from './SalesFutureSupplyLine'
import { SALES_FUTURE_SUPPLY_TEXT as T } from './salesFutureSupplyText'
import { FUTURE_ALLOCATION_TEXT as F } from '../inventory/futureAllocationText'
import type { FutureAllocationList } from '../inventory/futureAllocationContract'

const mocks = vi.hoisted(() => ({ read: vi.fn(), report: vi.fn() }))
vi.mock('../inventory/futureAllocationApi', () => ({ fetchFutureAllocations: mocks.read }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'SENTETIK-scope', report: mocks.report }) }))
const id = '11111111-1111-4111-8111-111111111111'
const props = { canRead: true, companyId: id, orderId: id, lineId: id, namespace: 'synthetic-scope', actorId: 'synthetic-actor', routeEntry: 1, orderVersion: '1' }
const list: FutureAllocationList = { companyId: id, salesOrderId: id, salesOrderLineId: id, windowFull: false,
  evaluatedOn: '2026-11-17', unitCodes: { [id]: 'kg' }, schedules: { [id]: { status: 'Scheduled', deliveryDate: '2026-11-16', isOverdue: true } },
  items: [{ allocationId: id, companyId: id, version: '1', status: 'Active', salesOrderId: id, salesOrderLineId: id,
    purchaseOrderId: id, purchaseOrderLineId: id, productId: id, productVersion: '1', unitId: id,
    allocatedQuantity: '100', remainingQuantity: '60', consumedQuantity: '0', releasedQuantity: '40', manualSequence: null,
    reason: 'Sentetik gerekçe', releaseReasonCode: 'purchasing-link-dropped', releaseOrigin: 'purchasing', releaseReason: 'Sentetik kaynak gerekçesi',
    createdAtUtc: '2026-11-16T09:00:00Z', updatedAtUtc: '2026-11-16T09:00:00Z', releasedAtUtc: null }] }

function mount(overrides: Partial<typeof props> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter><SalesFutureSupplyLine {...props} {...overrides} /></MemoryRouter></QueryClientProvider>)
}
beforeEach(() => { mocks.read.mockReset(); mocks.report.mockReset() })
afterEach(cleanup)

describe('F17-5 actual future supply view', () => {
  it('does not read without the independent Inventory read capability or before expansion', async () => {
    const denied = mount({ canRead: false })
    expect(screen.queryByText(T.show)).toBeNull(); expect(mocks.read).not.toHaveBeenCalled()
    denied.unmount(); mount()
    expect(screen.getByText(T.show)).toBeTruthy(); expect(mocks.read).not.toHaveBeenCalled()
  })

  it('shows original owner quantities, overdue flag and release reason without allocating or recalculating', async () => {
    mocks.read.mockResolvedValue({ type: 'ok-single', data: list })
    mount(); fireEvent.click(screen.getByText(T.show))
    expect(await screen.findByText(/60 kg/)).toBeTruthy()
    expect(screen.getByText(/Termin geçti/)).toBeTruthy()
    expect(screen.getByText(/Kaynak bağı düşürüldü/)).toBeTruthy()
    expect(screen.getByText(/Sentetik kaynak gerekçesi/)).toBeTruthy()
    expect(screen.getByRole('link', { name: T.choose }).getAttribute('href')).toContain(`/satis-siparisleri/${id}/satirlar/${id}/`)
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })

  it.each([[401, 'Oturum sonlandı'], [403, 'Yetkiniz yok'], [404, 'Bulunamadı'], [409, 'Çakışma'], [503, 'Servis geçici olarak kullanılamıyor']] as const)('keeps an HTTP %s distinct from empty supply', async (status, title) => {
    mocks.read.mockResolvedValue({ type: 'http', status })
    mount(); fireEvent.click(screen.getByText(T.show))
    await waitFor(() => expect(mocks.report).toHaveBeenCalledWith(status, 'SENTETIK-scope'))
    expect(await screen.findByText(title)).toBeTruthy()
    expect(screen.queryByText('Kayıt yok')).toBeNull()
    expect(screen.queryByText(/60 kg/)).toBeNull()
  })

  it.each([['offline', 'Bağlantı yok'], ['network-error', 'Sunucuya ulaşılamadı'], ['malformed', 'Beklenmeyen yanıt']] as const)('shows %s without substituting zero supply', async (type, title) => {
    mocks.read.mockResolvedValue({ type })
    mount(); fireEvent.click(screen.getByText(T.show))
    expect(await screen.findByText(title)).toBeTruthy()
    expect(screen.queryByText('Kayıt yok')).toBeNull()
  })

  it('shows loading and then an actual empty owner result separately', async () => {
    let finish!: (value: unknown) => void
    mocks.read.mockReturnValue(new Promise(resolve => { finish = resolve }))
    mount(); fireEvent.click(screen.getByText(T.show))
    expect(screen.getByText('Yükleniyor')).toBeTruthy()
    finish({ type: 'ok-single', data: { ...list, items: [], schedules: {}, unitCodes: {} } })
    expect(await screen.findByText('Kayıt yok')).toBeTruthy()
  })

  it('never treats an unavailable schedule as a current date or an automatic release', async () => {
    mocks.read.mockResolvedValue({ type: 'ok-single', data: { ...list, schedules: { [id]: { status: 'Unavailable', deliveryDate: null, isOverdue: null } } } })
    mount(); fireEvent.click(screen.getByText(T.show))
    expect(await screen.findByText(new RegExp(F.scheduleUnavailable))).toBeTruthy()
    expect(screen.queryByText(new RegExp(F.onTime))).toBeNull()
    expect(mocks.read).toHaveBeenCalledTimes(1)
  })
})
