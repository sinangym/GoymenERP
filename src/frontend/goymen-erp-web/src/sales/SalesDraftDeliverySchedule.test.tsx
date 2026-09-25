/**
 * DEC-SAL-003 on S-04: the planner's delivery-date update (dates only, no re-approval) behind its own shell control, one
 * idempotent attempt per snapshot, the detail and the schedule history re-read after a recorded change; and the strict
 * decoding of the history. Component evidence, NOT_ENV: the API answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY } from './capabilities'
import { decodeDeliverySchedules } from './contract'
import { buildDeliveryScheduleRequest, deliveryScheduleRowsFromDetail } from './deliverySchedule'
import { resetPendingDeliverySchedules } from './pendingDeliverySchedules'
import { ORDER_DETAIL_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.deliverySchedule
const H = ORDER_DETAIL_TEXT.deliveryScheduleHistory
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const SLICE_A = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
const SLICE_B = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const WITH_SCHEDULE = [...ALL_SALES, SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: 'onay-1',
  decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

function detail(state = 'Approved', version = '4', dates?: { header?: string | null; line?: string | null; slices?: [string, string] }): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = state
  body.draft.version = version
  body.approval = state === 'Approved' ? APPROVED
    : state === 'Withdrawn' ? { ...APPROVED, submittedBy: null, submittedAtUtc: null, decidedBy: 'satis-1' }
      : { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }
  if (dates?.header !== undefined) body.requestedDeliveryDate = dates.header
  if (dates?.line !== undefined) body.lines[0].requestedDeliveryDate = dates.line
  if (dates?.slices !== undefined) {
    body.lines[0].requestedDeliverySlices[0].requestedDeliveryDate = dates.slices[0]
    body.lines[0].requestedDeliverySlices[1].requestedDeliveryDate = dates.slices[1]
  }
  return body
}

function history(changed: boolean): Body {
  const original = {
    scheduleNo: 0, changedBy: null, changedAtUtc: null, requestedDeliveryDate: null,
    lines: [{ lineId: LINE, lineNo: 1, requestedDeliveryDate: '2026-09-20', slices: [
      { sliceId: SLICE_A, quantity: '400', requestedDeliveryDate: '2026-09-20' },
      { sliceId: SLICE_B, quantity: '600', requestedDeliveryDate: '2026-09-22' },
    ] }],
  }
  if (!changed) return { items: [original] }
  return { items: [original, {
    scheduleNo: 1, changedBy: 'planlama-1', changedAtUtc: '2026-09-26T10:00:00Z', requestedDeliveryDate: null,
    lines: [{ lineId: LINE, lineNo: 1, requestedDeliveryDate: '2026-10-05', slices: [
      { sliceId: SLICE_A, quantity: '400', requestedDeliveryDate: '2026-10-01' },
      { sliceId: SLICE_B, quantity: '600', requestedDeliveryDate: '2026-09-22' },
    ] }],
  }] }
}

afterEach(() => {
  cleanup()
  resetPendingDeliverySchedules()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function setDate(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } })
}

describe('S-04 delivery-schedule update (DEC-SAL-003)', () => {
  it('without the schedule control there is no form, and the history shows the dates as created', async () => {
    installFetch({ onDetail: () => json(detail()), onDeliverySchedules: () => json(history(false)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByTestId('sales-delivery-schedule-form')).toBeNull()
    const original = await screen.findByTestId('sales-delivery-schedule-0')
    expect(within(original).getByText(H.original)).toBeTruthy()
    expect(within(original).getAllByText(H.notChanged).length).toBe(2)
  })

  it('an approved order moves its dates without re-approval: one keyed POST, then the detail and the history are re-read', async () => {
    let changed = false
    const harness = installFetch({
      capabilities: WITH_SCHEDULE,
      onDetail: () => json(changed ? detail('Approved', '5', { line: '2026-10-05', slices: ['2026-10-01', '2026-09-22'] }) : detail()),
      onDeliverySchedules: () => json(history(changed)),
      onDeliverySchedule: () => {
        changed = true
        return json({ detail: detail('Approved', '5', { line: '2026-10-05', slices: ['2026-10-01', '2026-09-22'] }), isReplay: false, originalCorrelationId: 'corr-1' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-delivery-schedule-form')

    setDate(T.lineDate, '2026-10-05')
    setDate(T.sliceDate(1, '400'), '2026-10-01')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))

    await screen.findByText(T.completed)
    const posts = harness.calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0].url).toContain(`/sales/drafts/${DRAFT}/delivery-schedule`)
    expect(posts[0].companyId).toBe(COMPANY_A)
    expect(posts[0].idempotencyKey).not.toBeNull()
    expect(JSON.parse(posts[0].body as string)).toEqual({
      expectedVersion: '4',
      requestedDeliveryDate: null,
      lines: [{ lineId: LINE, requestedDeliveryDate: '2026-10-05', slices: [
        { sliceId: SLICE_A, requestedDeliveryDate: '2026-10-01' },
        { sliceId: SLICE_B, requestedDeliveryDate: '2026-09-22' },
      ] }],
    })
    // Still approved: the state badge did not move; the history now shows the planner's change.
    await waitFor(() => expect(screen.getByTestId('sales-draft-approval-state').textContent).toBe('Onaylı'))
    const change = await screen.findByTestId('sales-delivery-schedule-1')
    expect(within(change).getByText('planlama-1')).toBeTruthy()
  })

  it('a form that moves no date is refused locally and sends nothing', async () => {
    const harness = installFetch({ capabilities: WITH_SCHEDULE, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-delivery-schedule-form')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))
    expect((await screen.findByRole('alert')).textContent).toBe(T.noChange)
    expect(harness.calls.filter((call) => call.method === 'POST')).toHaveLength(0)
  })

  it('an unknown outcome keeps the key and the values; the retry resends the same snapshot', async () => {
    let attempts = 0
    const harness = installFetch({
      capabilities: WITH_SCHEDULE,
      onDetail: () => json(detail()),
      onDeliverySchedule: () => {
        attempts += 1
        if (attempts === 1) throw new TypeError('network down')
        return json({ detail: detail('Approved', '5', { line: '2026-10-05' }), isReplay: true, originalCorrelationId: 'corr-1' },
          200, { 'Idempotency-Replayed': 'true' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-delivery-schedule-form')
    setDate(T.lineDate, '2026-10-05')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.lineDate) as HTMLInputElement).value).toBe('2026-10-05')

    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.replayed)
    const posts = harness.calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[1].idempotencyKey).toBe(posts[0].idempotencyKey)
    expect(posts[1].body).toBe(posts[0].body)
  })

  it('a refused grant is told as such and nothing is recorded on the screen', async () => {
    installFetch({
      capabilities: WITH_SCHEDULE,
      onDetail: () => json(detail()),
      onDeliverySchedule: () => json({ status: 403, title: 'Forbidden' }, 403),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-delivery-schedule-form')
    setDate(T.headerDate, '2026-10-10')
    await userEvent.click(screen.getByRole('button', { name: T.submit }))
    expect((await screen.findByText(new RegExp(T.forbidden))).textContent).toContain(T.forbidden)
  })

  it('a withdrawn draft shows the form closed', async () => {
    installFetch({ capabilities: WITH_SCHEDULE, onDetail: () => json(detail('Withdrawn', '3')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-delivery-schedule-form')
    expect(screen.getByText(T.stateUnavailable)).toBeTruthy()
    expect((screen.getByRole('button', { name: T.submit }) as HTMLButtonElement).disabled).toBe(true)
  })
})

describe('delivery-schedule request and history contract', () => {
  it('builds every line once with every existing slice and refuses an empty slice date or no change', () => {
    const current = deliveryScheduleRowsFromDetail(JSON.parse(JSON.stringify(detail())) as never)
    const moved = { ...current, headerDate: '2026-10-10' }
    const built = buildDeliveryScheduleRequest('4', moved, current)
    expect(built).toEqual({ ok: true, payload: {
      expectedVersion: '4', requestedDeliveryDate: '2026-10-10',
      lines: [{ lineId: LINE, requestedDeliveryDate: '2026-09-20', slices: [
        { sliceId: SLICE_A, requestedDeliveryDate: '2026-09-20' }, { sliceId: SLICE_B, requestedDeliveryDate: '2026-09-22' },
      ] }],
    } })
    expect(buildDeliveryScheduleRequest('4', current, current)).toEqual({ ok: false, reason: 'no-change' })
    const emptySlice = { ...current, lines: [{ ...current.lines[0], slices: [{ ...current.lines[0].slices[0], date: '' }, current.lines[0].slices[1]] }] }
    expect(buildDeliveryScheduleRequest('4', emptySlice, current)).toEqual({ ok: false, reason: 'slice-date-required', lineNo: 1 })
    expect(buildDeliveryScheduleRequest('4', { ...current, headerDate: '2026-13-01' }, current)).toEqual({ ok: false, reason: 'invalid-header-date' })
  })

  it('reads a history exactly and refuses one that does not fit', () => {
    expect(decodeDeliverySchedules(history(true))?.items.map((item) => item.scheduleNo)).toEqual([0, 1])
    const outOfOrder = history(true)
    outOfOrder.items[1].scheduleNo = 2
    expect(decodeDeliverySchedules(outOfOrder)).toBeNull()
    const attributedOriginal = history(false)
    attributedOriginal.items[0].changedBy = 'someone'
    expect(decodeDeliverySchedules(attributedOriginal)).toBeNull()
    const anonymousChange = history(true)
    anonymousChange.items[1].changedBy = null
    expect(decodeDeliverySchedules(anonymousChange)).toBeNull()
    const extra = history(false)
    extra.items[0].approved = true
    expect(decodeDeliverySchedules(extra)).toBeNull()
    const duplicateSlice = history(false)
    duplicateSlice.items[0].lines[0].slices[1].sliceId = SLICE_A
    expect(decodeDeliverySchedules(duplicateSlice)).toBeNull()
    expect(decodeDeliverySchedules({ items: [] })).toBeNull()
  })
})
