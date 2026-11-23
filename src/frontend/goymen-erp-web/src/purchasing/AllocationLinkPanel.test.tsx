import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AllocationLinkPanel } from './AllocationLinkPanel'
import type { PurchaseOrderLine, PurchaseOrderStatus, PurchaseOrderSummary } from './contract'
import type { PurchaseOrderFutureAllocationLinks } from './allocationLinkContract'
import {
  cancelPurchaseOrderWithAllocationRelease,
  fetchPurchaseOrderFutureAllocations,
  releasePurchaseOrderFutureAllocation,
} from './allocationLinkApi'

const scenario = vi.hoisted(() => ({ report: vi.fn() }))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'
const ALLOCATION_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const ALLOCATION_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const AT = '2026-11-12T09:00:00Z'
const clients: QueryClient[] = []

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: scenario.report }),
}))
vi.mock('./allocationLinkApi', () => ({
  cancelPurchaseOrderWithAllocationRelease: vi.fn(),
  fetchPurchaseOrderFutureAllocations: vi.fn(),
  releasePurchaseOrderFutureAllocation: vi.fn(),
}))

function order(status: PurchaseOrderStatus = 'Approved', version = '4'): PurchaseOrderSummary {
  return {
    id: ORDER, companyId: COMPANY, status, version, supplierPartyId: '44444444-4444-4444-8444-444444444444',
    companyAccountId: '55555555-5555-4555-8555-555555555555', sourceRequestId: null, effectiveOn: '2026-11-03',
    createdAtUtc: AT, updatedAtUtc: AT, lineCount: 1,
  }
}

const LINES = [{ lineId: LINE, lineNo: 1 } as unknown as PurchaseOrderLine]

function view(over: Partial<PurchaseOrderFutureAllocationLinks> = {}): PurchaseOrderFutureAllocationLinks {
  const link = (allocationId: string, version: string) => ({
    allocationId, version, lineId: LINE, salesOrderId: '66666666-6666-4666-8666-666666666666',
    salesOrderLineId: '88888888-8888-4888-8888-888888888888', unitId: '99999999-9999-4999-8999-999999999999',
    allocatedQuantity: '5', remainingQuantity: '3', createdAtUtc: AT,
  })
  return { orderId: ORDER, orderVersion: '4', links: [link(ALLOCATION_A, '2'), link(ALLOCATION_B, '7')], ...over }
}

function mount(canCancel = true, status: PurchaseOrderStatus = 'Approved', onSettled = vi.fn(async () => undefined)) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  clients.push(client)
  render(<QueryClientProvider client={client}>
    <AllocationLinkPanel fenceKey="SENTETIK-context-a::1" actorId={ACTOR} companyId={COMPANY} order={order(status)}
      lines={LINES} canCancel={canCancel} onSettled={onSettled} />
  </QueryClientProvider>)
  return { onSettled }
}

beforeEach(() => {
  scenario.report.mockReset()
  vi.mocked(fetchPurchaseOrderFutureAllocations).mockReset().mockResolvedValue({ type: 'ok-single', data: view() })
  vi.mocked(releasePurchaseOrderFutureAllocation).mockReset()
  vi.mocked(cancelPurchaseOrderWithAllocationRelease).mockReset()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('P-03-DETAIL future allocation links (F-017 §3.5 F17-4; ALS-D83/ALS-D85)', () => {
  it('lists the active links in a named table with the owner quantities', async () => {
    mount()
    const table = await screen.findByRole('table', { name: 'Etkin gelecek tedarik ayırmaları' })
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent))
      .toEqual(['Satır', 'Satış siparişi', 'Ayrılan', 'Kalan ayrılmış', 'Ayrılma zamanı', 'İşlem'])
    expect(within(screen.getByTestId(`purchase-order-future-allocation-${ALLOCATION_A}`)).getAllByRole('cell')[0].textContent).toBe('1')
  })

  it('shows the empty state and offers no drop-and-cancel', async () => {
    vi.mocked(fetchPurchaseOrderFutureAllocations).mockResolvedValue({ type: 'ok-single', data: view({ links: [] }) })
    mount()
    await screen.findByTestId('purchase-order-future-allocations-empty')
    expect(screen.queryByTestId('purchase-order-cancel-with-allocations')).toBeNull()
  })

  it.each(['Draft', 'Cancelled'] as const)('reads nothing on a %s order', async (status) => {
    mount(true, status)
    expect(screen.queryByTestId('purchase-order-future-allocations')).toBeNull()
    expect(fetchPurchaseOrderFutureAllocations).not.toHaveBeenCalled()
  })

  it('hides itself on 403 without questioning the session', async () => {
    vi.mocked(fetchPurchaseOrderFutureAllocations).mockResolvedValue({ type: 'http', status: 403 })
    mount()
    await waitFor(() => expect(fetchPurchaseOrderFutureAllocations).toHaveBeenCalled())
    await waitFor(() => expect(screen.queryByTestId('purchase-order-future-allocations')).toBeNull())
    expect(scenario.report).not.toHaveBeenCalled()
  })

  it.each([
    [{ type: 'offline' } as const, 'Çevrimdışısınız; ayırmalar okunamadı.'],
    [{ type: 'network-error' } as const, 'Ağ hatası; ayırmalar okunamadı.'],
    [{ type: 'malformed' } as const, 'Sunucu yanıtı doğrulanamadı; ayırmalar gösterilmiyor.'],
    [{ type: 'http', status: 503 } as const, 'Ayırmalar şu anda okunamıyor.'],
    [{ type: 'http', status: 404 } as const, 'Sipariş bulunamadı.'],
  ])('names the read failure %o and offers a re-read', async (outcome, text) => {
    vi.mocked(fetchPurchaseOrderFutureAllocations).mockResolvedValue(outcome)
    mount()
    expect((await screen.findByRole('alert')).textContent).toBe(text)
    expect(screen.getByRole('button', { name: 'Yeniden oku' })).toBeTruthy()
  })

  it('treats a links view read at another order version as stale and offers no command', async () => {
    vi.mocked(fetchPurchaseOrderFutureAllocations).mockResolvedValue({ type: 'ok-single', data: view({ orderVersion: '3' }) })
    mount()
    await screen.findByText('Sipariş bu sırada değişti; ayırmalar yeniden okunmalı.')
    expect(screen.queryByRole('button', { name: 'Bağı düşür' })).toBeNull()
  })

  it('requires a reason, then drops one link with its own version and a fresh key and re-reads', async () => {
    vi.mocked(releasePurchaseOrderFutureAllocation).mockResolvedValue({ type: 'applied', replayed: false,
      response: { orderId: ORDER, orderVersion: '4', allocationId: ALLOCATION_B, isReplay: false, originalCorrelationId: 'corr-1' } })
    const user = userEvent.setup(); const { onSettled } = mount()
    const row = await screen.findByTestId(`purchase-order-future-allocation-${ALLOCATION_B}`)
    await user.click(within(row).getByRole('button', { name: 'Bağı düşür' }))
    await user.click(screen.getByRole('button', { name: 'Bağı düşürmeyi onayla' }))
    expect(screen.getByRole('alert').textContent).toBe('Gerekçe zorunludur.')
    expect(releasePurchaseOrderFutureAllocation).not.toHaveBeenCalled()
    await user.type(screen.getByLabelText('Gerekçe'), 'SENTETIK müşteri vazgeçti')
    await user.click(screen.getByRole('button', { name: 'Bağı düşürmeyi onayla' }))
    await screen.findByText('Bağ düşürüldü; ayrılmış miktar serbest kaldı.')
    const [company, orderId, orderVersion, allocation, key, reason] = vi.mocked(releasePurchaseOrderFutureAllocation).mock.calls[0]
    expect([company, orderId, orderVersion, allocation, reason])
      .toEqual([COMPANY, ORDER, '4', { allocationId: ALLOCATION_B, expectedAllocationVersion: '7' }, 'SENTETIK müşteri vazgeçti'])
    expect(key).toMatch(/^[0-9a-f-]{36}$/)
    expect(onSettled).toHaveBeenCalledTimes(1)
  })

  it('keeps an unknown drop for a resend with the same key and body', async () => {
    vi.mocked(releasePurchaseOrderFutureAllocation).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      .mockResolvedValueOnce({ type: 'applied', replayed: true,
        response: { orderId: ORDER, orderVersion: '4', allocationId: ALLOCATION_A, isReplay: true, originalCorrelationId: 'corr-1' } })
    const user = userEvent.setup(); mount()
    const row = await screen.findByTestId(`purchase-order-future-allocation-${ALLOCATION_A}`)
    await user.click(within(row).getByRole('button', { name: 'Bağı düşür' }))
    await user.type(screen.getByLabelText('Gerekçe'), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: 'Bağı düşürmeyi onayla' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı işlemi yeniden gönder' }))
    await screen.findByText('Bu işlem daha önce kaydedilmişti; kayıtlı sonuç gösterildi.')
    const calls = vi.mocked(releasePurchaseOrderFutureAllocation).mock.calls
    expect(calls).toHaveLength(2)
    expect(calls[1]).toEqual(calls[0])
  })

  it('cancels the remainder only with the confirmed exact set and a reason', async () => {
    vi.mocked(cancelPurchaseOrderWithAllocationRelease).mockResolvedValue({ type: 'failed', status: 409, code: 'purchasing_future_allocation_set_changed' })
    const user = userEvent.setup(); mount()
    const card = await screen.findByTestId('purchase-order-cancel-with-allocations')
    await user.click(within(card).getByRole('button', { name: 'Kalanı ve ayırmaları iptal et' }))
    expect(screen.getByRole('alert').textContent).toBe('Ayırmaların serbest bırakılmasını onaylayın.')
    await user.click(within(card).getByRole('checkbox'))
    await user.type(within(card).getByLabelText('İptal gerekçesi'), 'SENTETIK tedarik gelmeyecek')
    await user.click(within(card).getByRole('button', { name: 'Kalanı ve ayırmaları iptal et' }))
    await screen.findByText(/Ayırma listesi bu sırada değişti/)
    const [company, orderId, version, seen, , reason] = vi.mocked(cancelPurchaseOrderWithAllocationRelease).mock.calls[0]
    expect([company, orderId, version, reason]).toEqual([COMPANY, ORDER, '4', 'SENTETIK tedarik gelmeyecek'])
    expect(seen).toEqual([
      { allocationId: ALLOCATION_A, expectedAllocationVersion: '2' },
      { allocationId: ALLOCATION_B, expectedAllocationVersion: '7' },
    ])
  })

  it('offers no drop-and-cancel without the cancel capability', async () => {
    mount(false)
    await screen.findByRole('table', { name: 'Etkin gelecek tedarik ayırmaları' })
    expect(screen.queryByTestId('purchase-order-cancel-with-allocations')).toBeNull()
  })

  it('reports a 401 on a command to the auth boundary', async () => {
    vi.mocked(releasePurchaseOrderFutureAllocation).mockResolvedValue({ type: 'failed', status: 401, code: null })
    const user = userEvent.setup(); mount()
    const row = await screen.findByTestId(`purchase-order-future-allocation-${ALLOCATION_A}`)
    await user.click(within(row).getByRole('button', { name: 'Bağı düşür' }))
    await user.type(screen.getByLabelText('Gerekçe'), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: 'Bağı düşürmeyi onayla' }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(401, { token: 1 }))
  })
})
