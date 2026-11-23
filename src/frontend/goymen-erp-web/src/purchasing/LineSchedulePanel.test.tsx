import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LineSchedulePanel } from './LineSchedulePanel'
import type { PurchaseOrderDeliverySlice, PurchaseOrderLine, PurchaseOrderSummary } from './contract'
import { fetchPurchaseOrderLineScheduleHistory } from './lineScheduleApi'
import type { PurchaseOrderLineScheduleHistory } from './lineScheduleContract'
import { LINE_SCHEDULE_TEXT as T } from './lineScheduleText'
import type { ViewOutcome } from '../shell/uiState'

const scenario = vi.hoisted(() => ({ report: vi.fn() }))
const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'
const OTHER_LINE = '66666666-6666-4666-8666-666666666666'
const SLICE_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const SLICE_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const SLICE_C = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'
const RECORDER = 'SENTETIK-recorder'
const AT = '2026-11-03T09:00:00Z'
const clients: QueryClient[] = []

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: scenario.report }),
}))
vi.mock('./lineScheduleApi', () => ({ fetchPurchaseOrderLineScheduleHistory: vi.fn(), setPurchaseOrderLineSchedule: vi.fn() }))

const retimed: readonly PurchaseOrderDeliverySlice[] = [
  { sliceId: SLICE_B, sliceNo: 1, quantity: '4', deliveryDate: '2026-11-20' },
  { sliceId: SLICE_C, sliceNo: 2, quantity: '6', deliveryDate: '2026-12-04' },
]

function order(version = '5'): PurchaseOrderSummary {
  return {
    id: ORDER, companyId: COMPANY, status: 'Approved', version, supplierPartyId: '44444444-4444-4444-8444-444444444444',
    companyAccountId: '55555555-5555-4555-8555-555555555555', sourceRequestId: null, effectiveOn: '2026-11-03',
    createdAtUtc: AT, updatedAtUtc: AT, lineCount: 2,
  }
}

function line(lineId: string, lineNo: number, deliverySchedule?: readonly PurchaseOrderDeliverySlice[]): PurchaseOrderLine {
  return {
    lineId, lineNo, kind: 'General', parentLineId: null, sourceRequestLineId: null,
    productId: '88888888-8888-4888-8888-888888888888', productVersion: '3', unitId: '99999999-9999-4999-8999-999999999999',
    ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10', cancelled: '0',
    ...(deliverySchedule === undefined ? {} : { deliverySchedule }),
  }
}

function history(over: Partial<PurchaseOrderLineScheduleHistory> = {}): PurchaseOrderLineScheduleHistory {
  return {
    orderId: ORDER, lineId: LINE, orderVersion: '5',
    revisions: [
      { orderVersion: '3', recordedBy: RECORDER, recordedAtUtc: '2026-11-03T09:10:00Z',
        slices: [{ sliceId: SLICE_A, sliceNo: 1, quantity: '10', deliveryDate: '2026-11-20' }] },
      { orderVersion: '5', recordedBy: RECORDER, recordedAtUtc: '2026-11-04T08:00:00Z', slices: retimed },
    ],
    ...over,
  }
}

function mount(lines: readonly PurchaseOrderLine[] = [line(LINE, 1, retimed)], version = '5') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}>
    <LineSchedulePanel fenceKey="SENTETIK-context-a::1" companyId={COMPANY} order={order(version)} lines={lines} />
  </QueryClientProvider>)
}

beforeEach(() => {
  scenario.report.mockReset()
  vi.mocked(fetchPurchaseOrderLineScheduleHistory).mockReset().mockResolvedValue({ type: 'ok-single', data: history() })
})
afterEach(() => { cleanup(); clients.splice(0).forEach((client) => client.clear()) })

describe('P-05 order line delivery schedule, read only (F-027; ALS-D94, ALS-D81)', () => {
  it('draws each line\'s current termin from the detail as a named table and reads no history until asked', () => {
    mount([line(LINE, 1, retimed), line(OTHER_LINE, 2, []), line('55555555-5555-4555-8555-555555555556', 3)])
    const table = screen.getByRole('table', { name: T.currentCaption(1) })
    const headers = within(table).getAllByRole('columnheader')
    expect(headers.map((header) => header.textContent)).toEqual([T.sliceNo, T.quantity, T.deliveryDate])
    expect(headers.every((header) => header.getAttribute('scope') === 'col')).toBe(true)
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent)))
      .toEqual([['1', '4', '20.11.2026'], ['2', '6', '04.12.2026']])
    expect(within(rows[0]).getByText('20.11.2026').getAttribute('datetime')).toBe('2026-11-20')
    expect(within(screen.getByTestId('purchase-order-line-schedule-2')).getByText(T.currentEmpty)).toBeTruthy()
    expect(within(screen.getByTestId('purchase-order-line-schedule-3')).getByText(T.currentUnavailable)).toBeTruthy()
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(fetchPurchaseOrderLineScheduleHistory).not.toHaveBeenCalled()
  })

  it('on request lists every revision oldest first with recorder and UTC time, the approved termin change marked current', async () => {
    const user = userEvent.setup()
    mount()
    const toggle = screen.getByRole('button', { name: T.showHistory(1) })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    await user.click(toggle)
    const panel = await screen.findByTestId('purchase-order-line-schedule-history-1')
    expect(vi.mocked(fetchPurchaseOrderLineScheduleHistory).mock.calls[0].slice(0, 3)).toEqual([COMPANY, ORDER, LINE])
    expect(screen.getByRole('button', { name: T.hideHistory(1) }).getAttribute('aria-expanded')).toBe('true')
    expect(within(panel).getByText(T.historyReadAt('5'))).toBeTruthy()
    const items = within(panel).getAllByRole('listitem')
    expect(items.map((item) => item.getAttribute('data-testid'))).toEqual([
      'purchase-order-line-schedule-revision-1-3', 'purchase-order-line-schedule-revision-1-5',
    ])
    expect(within(items[0]).getByRole('heading').textContent).toBe(T.revisionHeading('3'))
    expect(within(items[1]).getByRole('heading').textContent).toBe(`${T.revisionHeading('5')} · ${T.currentMarker}`)
    expect(within(items[0]).getByText(RECORDER)).toBeTruthy()
    expect(within(items[1]).getByText('2026-11-04T08:00:00Z').getAttribute('datetime')).toBe('2026-11-04T08:00:00Z')
    const first = within(items[0]).getByRole('table', { name: T.revisionCaption(1, '3') })
    expect(within(first).getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent)))
      .toEqual([['1', '10', '20.11.2026']])
    const second = within(items[1]).getByRole('table', { name: T.revisionCaption(1, '5') })
    expect(within(second).getAllByRole('row')).toHaveLength(3)
    expect(screen.queryByText(T.historyNewer)).toBeNull()
    expect(scenario.report).not.toHaveBeenCalled()
  })

  it('says a line without any recorded revision has none', async () => {
    vi.mocked(fetchPurchaseOrderLineScheduleHistory).mockResolvedValue({ type: 'ok-single', data: history({ orderVersion: '2', revisions: [] }) })
    const user = userEvent.setup()
    mount([line(LINE, 1, [])], '2')
    await user.click(screen.getByRole('button', { name: T.showHistory(1) }))
    expect((await screen.findByText(T.historyEmpty)).getAttribute('data-state')).toBe('empty-no-data')
    expect(screen.queryByRole('listitem')).toBeNull()
  })

  it('warns when the history was read beside a newer order version than the page shows', async () => {
    vi.mocked(fetchPurchaseOrderLineScheduleHistory).mockResolvedValue({ type: 'ok-single', data: history({ orderVersion: '6' }) })
    const user = userEvent.setup()
    mount([line(LINE, 1, retimed)], '5')
    await user.click(screen.getByRole('button', { name: T.showHistory(1) }))
    expect((await screen.findByText(T.historyNewer)).getAttribute('role')).toBe('status')
  })

  it.each<[string, ViewOutcome<PurchaseOrderLineScheduleHistory>, string, number | null]>([
    ['401', { type: 'http', status: 401 }, 'unauthenticated', 401],
    ['403', { type: 'http', status: 403 }, 'forbidden', 403],
    ['404', { type: 'http', status: 404 }, 'not-found', null],
    ['503', { type: 'http', status: 503, body: { code: 'service_unavailable' } }, 'service-unavailable', null],
    ['malformed', { type: 'malformed' }, 'malformed-response', null],
    ['network error', { type: 'network-error' }, 'network-error', null],
    ['offline', { type: 'offline' }, 'offline', null],
  ])('a %s history read is its own state, never a schedule, and only 401/403 reach the auth boundary', async (_, outcome, kind, reported) => {
    vi.mocked(fetchPurchaseOrderLineScheduleHistory).mockResolvedValue(outcome)
    const user = userEvent.setup()
    const { container } = mount()
    await user.click(screen.getByRole('button', { name: T.showHistory(1) }))
    await screen.findByText((_, element) => element?.getAttribute('data-state') === kind)
    expect(container.querySelector(`[data-state="${kind}"]`)?.textContent).toContain(T.historySubject)
    expect(screen.queryByTestId('purchase-order-line-schedule-history-1')).toBeNull()
    if (reported === null) expect(scenario.report).not.toHaveBeenCalled()
    else expect(scenario.report).toHaveBeenCalledWith(reported, { token: 1 })
  })

  it('a retry after a 503 reads the history again and then shows it', async () => {
    vi.mocked(fetchPurchaseOrderLineScheduleHistory)
      .mockResolvedValueOnce({ type: 'http', status: 503 })
      .mockResolvedValueOnce({ type: 'ok-single', data: history() })
    const user = userEvent.setup()
    const { container } = mount()
    await user.click(screen.getByRole('button', { name: T.showHistory(1) }))
    await screen.findByText((_, element) => element?.getAttribute('data-state') === 'service-unavailable')
    const block = container.querySelector('[data-state="service-unavailable"]') as HTMLElement
    await user.click(within(block).getByRole('button'))
    expect(await screen.findByTestId('purchase-order-line-schedule-history-1')).toBeTruthy()
    expect(fetchPurchaseOrderLineScheduleHistory).toHaveBeenCalledTimes(2)
  })
})
