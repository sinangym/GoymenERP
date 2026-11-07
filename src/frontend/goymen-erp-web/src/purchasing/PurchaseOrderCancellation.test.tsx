import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderCancellationPanel } from './PurchaseOrderCancellationPanel'
import type { PurchaseOrderCancelImpact, PurchaseOrderStatus, PurchaseOrderSummary } from './contract'
import { cancelPurchaseOrderRemainder, fetchPurchaseOrderCancelImpact, type OrderCancellationOutcome } from './orderCancellationApi'

const scenario = vi.hoisted(() => ({ report: vi.fn() }))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'
const AT = '2026-11-03T09:00:00Z'
const REASON_LABEL = 'İptal gerekçesi'
const CANCEL = 'Kalanı iptal et'
const clients: QueryClient[] = []

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: scenario.report }),
}))
vi.mock('./orderCancellationApi', () => ({ cancelPurchaseOrderRemainder: vi.fn(), fetchPurchaseOrderCancelImpact: vi.fn() }))

function order(status: PurchaseOrderStatus = 'Approved', version = '4'): PurchaseOrderSummary {
  return {
    id: ORDER, companyId: COMPANY, status, version, supplierPartyId: '44444444-4444-4444-8444-444444444444',
    companyAccountId: '55555555-5555-4555-8555-555555555555', sourceRequestId: null, effectiveOn: '2026-11-03',
    createdAtUtc: AT, updatedAtUtc: AT, lineCount: 1,
  }
}

function impact(over: Partial<PurchaseOrderCancelImpact> = {}): PurchaseOrderCancelImpact {
  return {
    orderId: ORDER, status: 'Approved', version: '4', sourceRequestId: null, cancellable: true, blocker: null, hasAcceptance: true,
    lines: [{ lineId: LINE, lineNo: 1, kind: 'General', productId: '88888888-8888-4888-8888-888888888888',
      unitId: '99999999-9999-4999-8999-999999999999', committed: '10', accepted: '4', toCancel: '6' }],
    ...over,
  }
}

function applied(replayed = false): OrderCancellationOutcome {
  return {
    type: 'applied', replayed,
    response: { detail: { order: order('Cancelled', '5'), lines: [], acceptances: [], cancellation: null }, isReplay: replayed, originalCorrelationId: 'corr-1' },
  }
}

function mount(status: PurchaseOrderStatus = 'Approved', onSettled = vi.fn(async () => undefined)) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  clients.push(client)
  const view = render(<QueryClientProvider client={client}>
    <PurchaseOrderCancellationPanel sessionEpoch={1} fenceKey="SENTETIK-context-a::1" actorId={ACTOR} companyId={COMPANY}
      order={order(status)} onSettled={onSettled} />
  </QueryClientProvider>)
  return { ...view, onSettled }
}

beforeEach(() => {
  scenario.report.mockReset()
  vi.mocked(fetchPurchaseOrderCancelImpact).mockReset().mockResolvedValue({ type: 'ok-single', data: impact() })
  vi.mocked(cancelPurchaseOrderRemainder).mockReset()
  sessionStorage.clear()
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('P-03-DETAIL order remainder cancellation (A1-3, plan API-10/UI-05; No.161 §2)', () => {
  it('shows the server\'s impact preview: the remainder to cancel per line in a named table and the partial acceptance that stays', async () => {
    mount()
    const table = await screen.findByRole('table', { name: 'İptal edilecek kalan, satır bazında' })
    const headers = within(table).getAllByRole('columnheader')
    expect(headers.map((header) => header.textContent)).toEqual(['Satır', 'Taahhüt', 'Kabul', 'İptal edilecek'])
    expect(headers.every((header) => header.getAttribute('scope') === 'col')).toBe(true)
    expect(within(screen.getByTestId('purchase-order-cancel-impact-1')).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['1', '10', '4', '6'])
    expect(screen.getByText('Bu siparişte kısmi kabul var; kabul edilen miktar iptal edilmez.')).toBeTruthy()
    expect(vi.mocked(fetchPurchaseOrderCancelImpact).mock.calls[0].slice(0, 2)).toEqual([COMPANY, ORDER])
  })

  it.each(['Draft', 'Cancelled'] as const)('offers nothing and reads no preview on a %s order', async (status) => {
    mount(status)
    expect(screen.queryByRole('button', { name: CANCEL })).toBeNull()
    expect(screen.queryByTestId('purchase-order-cancellation')).toBeNull()
    expect(fetchPurchaseOrderCancelImpact).not.toHaveBeenCalled()
  })

  it.each([
    ['purchasing_order_nothing_to_cancel', 'Siparişin tamamı kabul edilmiş; iptal edilecek kalan yok.'],
    ['purchasing_order_not_cancellable', 'Yalnız onaylı siparişin kalanı iptal edilir.'],
  ])('names the blocker %s and offers no cancel button', async (blocker, text) => {
    vi.mocked(fetchPurchaseOrderCancelImpact).mockResolvedValue({ type: 'ok-single', data: impact({ cancellable: false, blocker }) })
    mount()
    expect((await screen.findByText(new RegExp(text))).textContent).toContain('Bu sipariş iptal edilemez')
    expect(screen.queryByRole('button', { name: CANCEL })).toBeNull()
  })

  it('requires a reason, refuses one over 4000 characters, then cancels with the preview\'s version and a fresh key and re-reads', async () => {
    vi.mocked(cancelPurchaseOrderRemainder).mockResolvedValue(applied())
    const user = userEvent.setup(); const { onSettled } = mount()
    await user.click(await screen.findByRole('button', { name: CANCEL }))
    expect(screen.getByRole('alert').textContent).toContain('İptal gerekçesi boş olamaz.')
    await user.click(screen.getByLabelText(REASON_LABEL))
    await user.paste('x'.repeat(4001))
    await user.click(screen.getByRole('button', { name: CANCEL }))
    expect(screen.getByRole('alert').textContent).toContain('İptal gerekçesi 4000 karakteri aşamaz; kısaltın.')
    expect(cancelPurchaseOrderRemainder).not.toHaveBeenCalled()
    await user.clear(screen.getByLabelText(REASON_LABEL))
    await user.type(screen.getByLabelText(REASON_LABEL), 'SENTETIK kalan gelmeyecek')
    await user.click(screen.getByRole('button', { name: CANCEL }))
    await screen.findByText('Siparişin kabul edilmemiş kalanı iptal edildi ve kayıt yeniden okundu.')
    const [company, orderId, version, key, reason] = vi.mocked(cancelPurchaseOrderRemainder).mock.calls[0]
    expect([company, orderId, version, reason]).toEqual([COMPANY, ORDER, '4', 'SENTETIK kalan gelmeyecek'])
    expect(key).toMatch(/^[0-9a-f-]{36}$/)
    expect(onSettled).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(vi.mocked(fetchPurchaseOrderCancelImpact).mock.calls.length).toBeGreaterThanOrEqual(2))
    expect(sessionStorage.length).toBe(0)
  })

  it('shows the busy state while cancelling and allows no second click', async () => {
    let release!: (outcome: OrderCancellationOutcome) => void
    vi.mocked(cancelPurchaseOrderRemainder).mockImplementation(() => new Promise((resolve) => { release = resolve }))
    const user = userEvent.setup(); mount()
    await user.type(await screen.findByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: CANCEL }))
    const busy = screen.getByRole('button', { name: 'İptal ediliyor…' }) as HTMLButtonElement
    expect(busy.disabled).toBe(true)
    release(applied())
    await screen.findByText('Siparişin kabul edilmemiş kalanı iptal edildi ve kayıt yeniden okundu.')
    expect(cancelPurchaseOrderRemainder).toHaveBeenCalledTimes(1)
  })

  it('keeps an unknown result and resends exactly the same key, version and reason', async () => {
    vi.mocked(cancelPurchaseOrderRemainder).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup(); mount()
    await user.type(await screen.findByLabelText(REASON_LABEL), 'SENTETIK neden')
    await user.click(screen.getByRole('button', { name: CANCEL }))
    await screen.findByText(/Sonuç bilinmiyor/)
    expect(screen.queryByLabelText(REASON_LABEL)).toBeNull()
    expect(sessionStorage.length).toBe(1)
    vi.mocked(cancelPurchaseOrderRemainder).mockResolvedValueOnce(applied(true))
    await user.click(screen.getByRole('button', { name: 'Aynı iptali yeniden gönder' }))
    await screen.findByText('Önceki iptal yanıtı yeniden gösterildi.')
    const [first, second] = vi.mocked(cancelPurchaseOrderRemainder).mock.calls
    expect(second).toEqual(first)
    expect(sessionStorage.length).toBe(0)
  })

  it('recalls an unresolved cancellation after a reload and offers only the same resend, even once the order reads Cancelled', async () => {
    sessionStorage.setItem(`goymen.purchasing.order-cancellation.v1:1:${ACTOR}:${COMPANY}:${ORDER}`, JSON.stringify({
      actorId: ACTOR, companyId: COMPANY, orderId: ORDER, expectedVersion: '4',
      idempotencyKey: '99999999-9999-4999-8999-999999999999', reason: 'SENTETIK',
    }))
    mount('Cancelled')
    expect(screen.getByRole('button', { name: 'Aynı iptali yeniden gönder' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: CANCEL })).toBeNull()
    expect(fetchPurchaseOrderCancelImpact).not.toHaveBeenCalled()
  })

  it.each([
    ['purchasing_order_nothing_to_cancel', 'Siparişin tamamı kabul edilmiş; iptal edilecek kalan yok.'],
    ['purchasing_order_state', 'Yalnız onaylı siparişin kalanı iptal edilir.'],
    ['version_conflict', 'Sipariş bu sırada değişti; güncel sürüm yeniden okundu.'],
  ])('explains the 409 %s and re-reads the order and the preview', async (code, text) => {
    vi.mocked(cancelPurchaseOrderRemainder).mockResolvedValue({ type: 'failed', status: 409, code, replayed: false })
    const user = userEvent.setup(); const { onSettled } = mount()
    await user.type(await screen.findByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: CANCEL }))
    const alert = await screen.findByText(new RegExp(text))
    expect(alert.textContent).toContain('Kayıt yeniden okundu.')
    await waitFor(() => expect(onSettled).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(vi.mocked(fetchPurchaseOrderCancelImpact).mock.calls.length).toBeGreaterThanOrEqual(2))
    expect(sessionStorage.length).toBe(0)
  })

  it.each([401, 403])('reports HTTP %s to the auth boundary without a re-read', async (status) => {
    vi.mocked(cancelPurchaseOrderRemainder).mockResolvedValue({ type: 'failed', status, code: null, replayed: false })
    const user = userEvent.setup(); const { onSettled } = mount()
    await user.type(await screen.findByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: CANCEL }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(status, { token: 1 }))
    expect(onSettled).not.toHaveBeenCalled()
    expect(sessionStorage.length).toBe(0)
  })

  it.each([401, 403])('reports a preview HTTP %s to the auth boundary and offers no cancel button', async (status) => {
    vi.mocked(fetchPurchaseOrderCancelImpact).mockResolvedValue({ type: 'http', status, body: null })
    mount()
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(status, { token: 1 }))
    expect(await screen.findByText('İptal etkisi okunamadı. Yeniden deneyin.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: CANCEL })).toBeNull()
  })

  it('sends nothing while the preview was read at another version than the order shows', async () => {
    vi.mocked(fetchPurchaseOrderCancelImpact).mockResolvedValue({ type: 'ok-single', data: impact({ version: '5' }) })
    const user = userEvent.setup(); const { onSettled } = mount()
    expect(await screen.findByText('Sipariş, iptal etkisi okunduktan sonra değişti. Kaydı ve etkiyi yeniden okuyun.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: CANCEL })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Etkiyi yeniden oku' }))
    await waitFor(() => expect(onSettled).toHaveBeenCalledTimes(1))
    expect(cancelPurchaseOrderRemainder).not.toHaveBeenCalled()
  })

  it('shows a 422 reason refusal from the server in user words', async () => {
    vi.mocked(cancelPurchaseOrderRemainder).mockResolvedValue({ type: 'failed', status: 422, code: 'purchasing_order_cancellation_reason_required', replayed: false })
    const user = userEvent.setup(); const { onSettled } = mount()
    await user.type(await screen.findByLabelText(REASON_LABEL), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: CANCEL }))
    expect((await screen.findByText(/İptal gerekçesi zorunludur\./)).getAttribute('role')).toBe('alert')
    expect(onSettled).not.toHaveBeenCalled()
  })
})
