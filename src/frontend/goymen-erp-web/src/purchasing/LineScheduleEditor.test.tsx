import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LineScheduleEditor } from './LineScheduleEditor'
import { LineSchedulePanel } from './LineSchedulePanel'
import type { PurchaseOrderDeliverySlice, PurchaseOrderLine, PurchaseOrderStatus, PurchaseOrderSummary } from './contract'
import { setPurchaseOrderLineSchedule, type LineScheduleOutcome } from './lineScheduleApi'
import { LINE_SCHEDULE_PROBLEM_TEXT, LINE_SCHEDULE_TEXT as T } from './lineScheduleText'
import { pendingLineScheduleKey, recallPendingLineSchedule } from './pendingLineScheduleStore'

const scenario = vi.hoisted(() => ({ report: vi.fn() }))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = 'SENTETIK-actor'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'
const SLICE_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const SLICE_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const NEW_ID = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'
const KEY = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee'
const AT = '2026-11-03T09:00:00Z'
const clients: QueryClient[] = []

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: scenario.report }),
}))
vi.mock('./lineScheduleApi', () => ({ setPurchaseOrderLineSchedule: vi.fn(), fetchPurchaseOrderLineScheduleHistory: vi.fn() }))

const recorded: readonly PurchaseOrderDeliverySlice[] = [
  { sliceId: SLICE_A, sliceNo: 1, quantity: '4', deliveryDate: '2026-11-20' },
  { sliceId: SLICE_B, sliceNo: 2, quantity: '6', deliveryDate: '2026-12-04' },
]

function order(status: PurchaseOrderStatus = 'Approved', version = '5'): PurchaseOrderSummary {
  return {
    id: ORDER, companyId: COMPANY, status, version, supplierPartyId: '44444444-4444-4444-8444-444444444444',
    companyAccountId: '55555555-5555-4555-8555-555555555555', sourceRequestId: null, effectiveOn: '2026-11-03',
    createdAtUtc: AT, updatedAtUtc: AT, lineCount: 1,
  }
}

function line(deliverySchedule: readonly PurchaseOrderDeliverySlice[] = recorded, kind: PurchaseOrderLine['kind'] = 'General'): PurchaseOrderLine {
  return {
    lineId: LINE, lineNo: 1, kind, parentLineId: null, sourceRequestLineId: null,
    productId: '88888888-8888-4888-8888-888888888888', productVersion: '3', unitId: '99999999-9999-4999-8999-999999999999',
    ordered: '10', technical: '0', committed: '10', accepted: '4', remaining: '6', cancelled: '0', deliverySchedule,
  }
}

function applied(replayed = false): LineScheduleOutcome {
  return {
    type: 'applied', replayed,
    response: { detail: { order: order('Approved', '6'), lines: [line()], acceptances: [], cancellation: null }, isReplay: replayed, originalCorrelationId: 'corr-1' },
  }
}

function mount(options: { status?: PurchaseOrderStatus; current?: PurchaseOrderLine; onSettled?: () => Promise<unknown> } = {}) {
  const onSettled = options.onSettled ?? vi.fn(async () => undefined)
  const view = render(<LineScheduleEditor sessionEpoch={1} fenceKey="SENTETIK-context-a::1" actorId={ACTOR} companyId={COMPANY}
    order={order(options.status)} line={options.current ?? line()} onSettled={onSettled} />)
  return { ...view, onSettled }
}

function valueOf(element: HTMLElement): string {
  return (element as HTMLInputElement).value
}

function disabledOf(element: HTMLElement): boolean {
  return (element as HTMLInputElement | HTMLButtonElement).disabled
}

function storeKey() {
  return pendingLineScheduleKey(1, ACTOR, COMPANY, ORDER, LINE)
}

beforeEach(() => {
  sessionStorage.clear()
  scenario.report.mockReset()
  vi.mocked(setPurchaseOrderLineSchedule).mockReset()
  vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValue(KEY)
})
afterEach(() => { cleanup(); vi.restoreAllMocks(); clients.splice(0).forEach((client) => client.clear()) })

describe('P-05 order line termin change control (F-027 UI-07; EK16 §2a, ALS-D94, ALS-D81)', () => {
  it('starts from the server\'s slices and sends their ids, the entered values and the read version under a fresh key', async () => {
    vi.mocked(setPurchaseOrderLineSchedule).mockResolvedValue(applied())
    const { onSettled } = mount()
    expect(valueOf(screen.getByLabelText(T.editQuantity(1)))).toBe('4')
    expect(valueOf(screen.getByLabelText(T.editDeliveryDate(2)))).toBe('2026-12-04')
    fireEvent.change(screen.getByLabelText(T.editDeliveryDate(2)), { target: { value: '2026-12-18' } })
    await userEvent.clear(screen.getByLabelText(T.editQuantity(1)))
    await userEvent.type(screen.getByLabelText(T.editQuantity(1)), '4,0')
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(setPurchaseOrderLineSchedule).toHaveBeenCalledWith(COMPANY, ORDER, LINE, '5', KEY, [
      { sliceId: SLICE_A, quantity: '4', deliveryDate: '2026-11-20' },
      { sliceId: SLICE_B, quantity: '6', deliveryDate: '2026-12-18' },
    ])
    expect((await screen.findByRole('status')).textContent).toContain(T.applied)
    expect(onSettled).toHaveBeenCalledTimes(1)
    expect(recallPendingLineSchedule(storeKey())).toBeNull()
  })

  it('offers one row with the ordered quantity when nothing is recorded and refuses a row without a date locally', async () => {
    mount({ current: line([]) })
    expect(valueOf(screen.getByLabelText(T.editQuantity(1)))).toBe('10')
    expect(valueOf(screen.getByLabelText(T.editDeliveryDate(1)))).toBe('')
    expect(screen.queryByRole('button', { name: T.removeSlice(1) })).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(screen.getByRole('alert').textContent).toContain(T.dateRequired(1))
    expect(setPurchaseOrderLineSchedule).not.toHaveBeenCalled()
  })

  it('adds a slice with a fresh id, removes one, and judges only the shape (the total is the server\'s)', async () => {
    vi.mocked(setPurchaseOrderLineSchedule).mockResolvedValue(applied())
    vi.mocked(globalThis.crypto.randomUUID).mockReturnValueOnce(NEW_ID).mockReturnValue(KEY)
    mount()
    await userEvent.click(screen.getByRole('button', { name: T.addSlice }))
    await userEvent.type(screen.getByLabelText(T.editQuantity(3)), '0')
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(screen.getByRole('alert').textContent).toContain(T.quantityNotPositive(3))
    expect(screen.getByRole('alert').textContent).toContain(T.dateRequired(3))
    await userEvent.clear(screen.getByLabelText(T.editQuantity(3)))
    await userEvent.type(screen.getByLabelText(T.editQuantity(3)), '1.5')
    fireEvent.change(screen.getByLabelText(T.editDeliveryDate(3)), { target: { value: '2027-01-08' } })
    await userEvent.click(screen.getByRole('button', { name: T.removeSlice(1) }))
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    // 6 + 1.5 is not the ordered 10: the control still sends it; the server answers the total.
    expect(setPurchaseOrderLineSchedule).toHaveBeenCalledWith(COMPANY, ORDER, LINE, '5', KEY, [
      { sliceId: SLICE_B, quantity: '6', deliveryDate: '2026-12-04' },
      { sliceId: NEW_ID, quantity: '1.5', deliveryDate: '2027-01-08' },
    ])
  })

  it.each([
    ['a technical line', { current: line([], 'Technical') }],
    ['a cancelled order', { status: 'Cancelled' as const }],
  ])('shows no form for %s', (_name, options) => {
    mount(options)
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
  })

  it('keeps an unknown outcome with the form frozen and resends the same key, version and slices', async () => {
    vi.mocked(setPurchaseOrderLineSchedule).mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' }).mockResolvedValueOnce(applied(true))
    const { onSettled } = mount()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect(disabledOf(screen.getByLabelText(T.editQuantity(1)))).toBe(true)
    expect(recallPendingLineSchedule(storeKey())).toMatchObject({ idempotencyKey: KEY, expectedVersion: '5' })
    vi.mocked(globalThis.crypto.randomUUID).mockReturnValue(NEW_ID)
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    const [first, second] = vi.mocked(setPurchaseOrderLineSchedule).mock.calls
    expect(second).toEqual(first)
    expect((await screen.findByRole('status')).textContent).toContain(T.replayed)
    expect(onSettled).toHaveBeenCalledTimes(1)
    expect(recallPendingLineSchedule(storeKey())).toBeNull()
  })

  it('recalls an unresolved attempt after a remount, even where the form would no longer be offered', async () => {
    vi.mocked(setPurchaseOrderLineSchedule).mockResolvedValue({ type: 'unknown', reason: 'service-unavailable' })
    const first = mount()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    first.unmount()
    mount({ status: 'Cancelled' })
    expect(screen.getByText(T.unknown)).toBeTruthy()
    expect(disabledOf(screen.getByRole('button', { name: T.retry }))).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: T.abandon }))
    expect(recallPendingLineSchedule(storeKey())).toBeNull()
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
  })

  it('shows a rule refusal in user words with each field problem and does not re-read the order', async () => {
    vi.mocked(setPurchaseOrderLineSchedule).mockResolvedValue({
      type: 'failed', status: 422, code: 'validation_failed', replayed: false,
      problems: [{ field: 'slices', code: 'purchasing_schedule_received_portion_locked' }],
    })
    const { onSettled } = mount()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain(LINE_SCHEDULE_PROBLEM_TEXT.validation_failed)
    expect(within(alert).getByRole('listitem').textContent).toContain(LINE_SCHEDULE_PROBLEM_TEXT.purchasing_schedule_received_portion_locked)
    expect(onSettled).not.toHaveBeenCalled()
    expect(disabledOf(screen.getByLabelText(T.editQuantity(1)))).toBe(false)
  })

  it('re-reads the order after a version conflict and reports 401/403 to the auth boundary', async () => {
    vi.mocked(setPurchaseOrderLineSchedule).mockResolvedValueOnce({
      type: 'failed', status: 409, code: 'version_conflict', replayed: false, problems: [{ field: 'expectedVersion', code: 'version_conflict' }],
    })
    const { onSettled } = mount()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect((await screen.findByRole('alert')).textContent).toContain(T.reread)
    await waitFor(() => expect(onSettled).toHaveBeenCalledTimes(1))
    vi.mocked(setPurchaseOrderLineSchedule).mockResolvedValueOnce({ type: 'failed', status: 403, code: null, replayed: false, problems: [] })
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(scenario.report).toHaveBeenCalledWith(403, { token: 1 }))
    expect(onSettled).toHaveBeenCalledTimes(1)
  })
})

describe('P-05 line schedule section with and without the termin change capability', () => {
  function mountPanel(withEditor: boolean) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
    clients.push(client)
    return render(<QueryClientProvider client={client}>
      <LineSchedulePanel fenceKey="SENTETIK-context-a::1" companyId={COMPANY} order={order()} lines={[line()]}
        editor={withEditor ? { sessionEpoch: 1, actorId: ACTOR, onSettled: async () => undefined } : null} />
    </QueryClientProvider>)
  }

  it('stays read only without the capability and offers the control beside the current termin with it', () => {
    mountPanel(false)
    expect(screen.queryByTestId('purchase-order-line-schedule-editor-1')).toBeNull()
    expect(screen.queryByRole('textbox')).toBeNull()
    cleanup()
    mountPanel(true)
    const editor = screen.getByTestId('purchase-order-line-schedule-editor-1')
    expect(within(editor).getByRole('heading', { name: T.editHeading(1) })).toBeTruthy()
    expect(valueOf(within(editor).getByLabelText(T.editQuantity(2)))).toBe('6')
  })
})
