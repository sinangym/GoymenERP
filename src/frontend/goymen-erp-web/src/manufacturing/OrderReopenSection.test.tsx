import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { OrderReopenSection } from './OrderReopenSection'
import { reopenOrder } from './orderReopenApi'
import { ORDER_REOPEN_TEXT as T } from './orderReopenText'
import { decodeOrder } from './contract'
import { APPROVER, COMPANY_A, order } from './testHarness'
vi.mock('./orderReopenApi', async original => ({ ...await original<typeof import('./orderReopenApi')>(), reopenOrder: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))
const closed = decodeOrder(order({ status: 'Closed', version: 4, closedByUserId: APPROVER, closedAtUtc: '2026-11-16T20:00:00Z' }))!
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.clearAllMocks(); sessionStorage.clear() })
function ui(allowed = true, refresh = vi.fn(async () => {})) {
  return <OrderReopenSection order={closed} companyId={COMPANY_A} namespace="synthetic-reopening" allowed={allowed}
    blocked={false} onRefresh={refresh} onLocked={vi.fn()} />
}
function submit() {
  fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'Synthetic correction' } })
  fireEvent.click(screen.getByRole('button', { name: T.save }))
}
describe('reasoned order reopening', () => {
  it('hides the command from readers without the separate capability', () => {
    render(ui(false)); expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })
  it('keeps an unknown attempt and its reason across reload and resends exactly the same key and version', async () => {
    vi.mocked(reopenOrder).mockResolvedValue({ type: 'unknown' }); const first = render(ui()); submit()
    expect(await screen.findByText(T.unknown)).toBeTruthy(); const original = vi.mocked(reopenOrder).mock.calls[0]
    first.unmount(); render(ui())
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).value).toBe('Synthetic correction')
    expect((screen.getByLabelText(T.reason) as HTMLTextAreaElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.resend }))
    await waitFor(() => expect(reopenOrder).toHaveBeenCalledTimes(2))
    expect(vi.mocked(reopenOrder).mock.calls[1]).toEqual(original)
  })
  it('never submits when local persistence fails and explains a definite closed-period refusal', async () => {
    const first = render(ui()); const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('synthetic unavailable') })
    submit(); expect((await screen.findByRole('alert')).textContent).toContain(T.blocked); expect(reopenOrder).not.toHaveBeenCalled()
    first.unmount(); spy.mockRestore(); sessionStorage.clear()
    vi.mocked(reopenOrder).mockResolvedValue({ type: 'failed', status: 422, code: 'manufacturing_order_reopen_period_not_open' })
    const refresh = vi.fn(async () => {}); render(ui(true, refresh)); submit()
    expect(await screen.findByText(T.period)).toBeTruthy(); expect(refresh).toHaveBeenCalledTimes(1); expect(sessionStorage.length).toBe(0)
  })
  it('explains that a recognized output transfer must be undone through its own flow before retrying reopening', async () => {
    vi.mocked(reopenOrder).mockResolvedValue({ type: 'failed', status: 409, code: 'manufacturing_cost_transfer_reversal_required' })
    const refresh = vi.fn(async () => {}); render(ui(true, refresh)); submit()
    expect(await screen.findByText(T.transfer)).toBeTruthy()
    expect(refresh).toHaveBeenCalledTimes(1); expect(sessionStorage.length).toBe(0)
    expect(reopenOrder).toHaveBeenCalledTimes(1)
  })
})
