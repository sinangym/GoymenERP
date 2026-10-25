/**
 * DEC-SAL-005 / AT-007 on S-04: the impact preview is read from the server only when the user asks for it, behind the
 * `sales.orders.cancel` control, and shows every category's status as the owners answered — "kaynak yok" and "okunamadı" are
 * never shown as 0; only a cancellable preview offers the command, which needs a reason and carries the preview's token under
 * one idempotency key; a recorded cancellation re-reads the order, which then reads "İptal edildi" with its recorded reason and
 * offers no other command. Component evidence, NOT_ENV: the API answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeCancellation, decodeCancellationImpact, decodeDraftDetail, decodeDraftSummary, type DraftDetail } from './contract'
import { SALES_CANCEL_CAPABILITY, SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY } from './capabilities'
import { ORDER_DETAIL_TEXT, PROBLEM_CODE_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.cancellation
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const KG = '44444444-4444-4444-4444-444444444444'
const TOKEN = 'a'.repeat(64)
const WITH_CANCEL = [...ALL_SALES, SALES_CANCEL_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: 'onay-1',
  decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}
const NOT_YET = { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }

function detail(approvalState = 'Approved', state = 'Draft', version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = approvalState
  body.draft.state = state
  body.draft.version = version
  body.approval = approvalState === 'Approved' ? APPROVED : NOT_YET
  return body
}

const SOURCES = [
  'inventory.active-allocations', 'purchasing.linked-orders', 'manufacturing.linked-orders',
  'logistics.loading-without-dispatch-note', 'invoicing.uninvoiced-dispatch-notes', 'finance.unmatched-advances',
]

function impact(options: { allocations?: string; loading?: string; count?: number | null; cancellable?: boolean; token?: string } = {}): Body {
  const allocations = options.allocations ?? 'None'
  const loading = options.loading ?? 'None'
  const allocationNull = allocations === 'Unreadable'
  const loadingNull = loading === 'Unreadable'
  return {
    draftVersion: '4',
    revision: 'Rev.0',
    cancellable: options.cancellable ?? (allocations === 'None' && loading === 'None'),
    impactToken: options.token ?? TOKEN,
    sources: SOURCES.map((source, index) => ({
      source,
      status: index === 0 ? allocations : index === 3 ? loading : 'NoSource',
    })),
    lines: [{
      lineId: LINE, lineNo: 1, orderUnitId: KG, ordered: '1000',
      activeAllocations: allocationNull ? null : (options.count ?? 0), allocatedKg: allocationNull ? null : (options.count ? '250' : '0'),
      loadingInstructionLines: loadingNull ? null : 0, instructed: loadingNull ? null : '0', loaded: loadingNull ? null : '0',
      shipped: loadingNull ? null : '0', delivered: loadingNull ? null : '0',
    }],
  }
}

const RECORD = {
  cancellation: {
    revision: 'Rev.0', cancelledBy: 'satis-iptal', cancelledAtUtc: '2026-09-27T10:00:00Z', reason: 'Müşteri vazgeçti',
    impactToken: TOKEN, sources: SOURCES.map((source, index) => ({ source, status: index === 0 || index === 3 ? 'None' : 'NoSource' })),
  },
}

afterEach(() => {
  cleanup()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const impactCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/cancellation-impact'))
const cancelCalls = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/cancellation') && call.method === 'POST')

describe('S-04 cancellation of an approved order (DEC-SAL-005, AT-007)', () => {
  it('reads the preview only when asked, cancels with a reason and the preview token, and reopens Cancelled with its record', async () => {
    let cancelled = false
    const harness = installFetch({
      capabilities: WITH_CANCEL,
      onDetail: () => json(cancelled ? detail('Approved', 'Cancelled', '5') : detail()),
      onCancellationImpact: () => json(impact()),
      onCancel: () => {
        cancelled = true
        return json({ detail: detail('Approved', 'Cancelled', '5'), isReplay: false, originalCorrelationId: 'c-1' })
      },
      onCancellation: () => json(RECORD),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-cancellation')
    // "Etki önizlemesi olmadan iptal yok": nothing is read and no command is offered before the user asks.
    expect(impactCalls(harness.calls)).toHaveLength(0)
    expect(within(section).queryByTestId('sales-cancellation-submit')).toBeNull()

    fireEvent.click(within(section).getByTestId('sales-cancellation-open'))
    await within(section).findByTestId('sales-cancellation-impact')
    const [read] = impactCalls(harness.calls)
    expect(read).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    expect(screen.getByTestId('sales-cancellation-source-inventory.active-allocations').getAttribute('data-status')).toBe('None')
    // No module on main records a purchasing link: "kaynak yok", never zero.
    expect(screen.getByTestId('sales-cancellation-source-purchasing.linked-orders').textContent).toContain(T.statusText.NoSource)
    expect(screen.getByTestId('sales-cancellation-line-1-allocations').textContent).toBe('0')

    // A reason is required before anything is sent.
    fireEvent.click(screen.getByTestId('sales-cancellation-submit'))
    expect(await screen.findByText(T.reasonRequired)).toBeTruthy()
    expect(cancelCalls(harness.calls)).toHaveLength(0)

    fireEvent.change(screen.getByLabelText(T.reasonLabel), { target: { value: '  Müşteri vazgeçti  ' } })
    fireEvent.click(screen.getByTestId('sales-cancellation-submit'))

    await screen.findByTestId('sales-cancellation-record')
    const [sent] = cancelCalls(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({ expectedVersion: '4', reason: 'Müşteri vazgeçti', impactToken: TOKEN })
    // The order is re-read: Cancelled beside its unchanged approval, its record, and no cancel or schedule command.
    await waitFor(() => expect(screen.getByTestId('sales-draft-lifecycle-state').textContent).toBe(ORDER_DETAIL_TEXT.stateCancelled))
    expect(screen.getByTestId('sales-draft-approval-state').textContent).toBe('Onaylı')
    expect(screen.getByTestId('sales-approval-decided-by').textContent).toBe('onay-1')
    await waitFor(() => expect(screen.getByTestId('sales-cancellation-reason-text').textContent).toBe('Müşteri vazgeçti'))
    expect(screen.getByTestId('sales-cancellation-by').textContent).toBe('satis-iptal')
    expect(screen.queryByTestId('sales-cancellation')).toBeNull()
  })

  it('shows what blocks the cancellation and offers no command while an owner records an effect', async () => {
    const harness = installFetch({
      capabilities: WITH_CANCEL,
      onDetail: () => json(detail()),
      onCancellationImpact: () => json(impact({ allocations: 'Present', count: 1 })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-cancellation-open'))
    await screen.findByTestId('sales-cancellation-blocked')
    expect(screen.getByTestId('sales-cancellation-source-inventory.active-allocations').textContent).toContain(T.statusText.Present)
    expect(screen.getByTestId('sales-cancellation-line-1-allocations').textContent).toBe('1')
    expect(screen.queryByTestId('sales-cancellation-submit')).toBeNull()
    expect(cancelCalls(harness.calls)).toHaveLength(0)
  })

  it('never shows an unreadable owner as 0 and offers no command', async () => {
    installFetch({
      capabilities: WITH_CANCEL,
      onDetail: () => json(detail()),
      onCancellationImpact: () => json(impact({ allocations: 'Unreadable', loading: 'Unreadable' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-cancellation-open'))
    await screen.findByTestId('sales-cancellation-blocked')
    expect(screen.getByTestId('sales-cancellation-line-1-allocations').textContent).toBe(T.unreadable)
    expect(screen.getByTestId('sales-cancellation-line-1-loading').textContent).toBe(T.unreadable)
    expect(screen.queryByTestId('sales-cancellation-submit')).toBeNull()
  })

  it('reads the preview again when the impact changed after the user saw it', async () => {
    let reads = 0
    const harness = installFetch({
      capabilities: WITH_CANCEL,
      onDetail: () => json(detail()),
      onCancellationImpact: () => {
        reads += 1
        return json(reads === 1 ? impact() : impact({ allocations: 'Present', count: 1 }))
      },
      onCancel: () => json({
        status: 409, code: 'sales_cancellation_impact_changed', errors: [{ field: 'impactToken', code: 'sales_cancellation_impact_changed' }],
      }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-cancellation-open'))
    fireEvent.change(await screen.findByLabelText(T.reasonLabel), { target: { value: 'neden' } })
    fireEvent.click(screen.getByTestId('sales-cancellation-submit'))

    await waitFor(() => expect(screen.getByTestId('sales-cancellation-notice').textContent)
      .toContain(PROBLEM_CODE_TEXT.sales_cancellation_impact_changed))
    await screen.findByTestId('sales-cancellation-blocked')
    expect(impactCalls(harness.calls).length).toBeGreaterThanOrEqual(2)
    expect(cancelCalls(harness.calls)).toHaveLength(1)
  })

  it('is absent without the cancel control and for an order that is not approved', async () => {
    const withoutControl = installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByTestId('sales-cancellation')).toBeNull()
    expect(impactCalls(withoutControl.calls)).toHaveLength(0)

    cleanup()
    vi.unstubAllGlobals()
    const preparing = installFetch({ capabilities: WITH_CANCEL, onDetail: () => json(detail('Preparing')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByTestId('sales-cancellation')).toBeNull()
    expect(impactCalls(preparing.calls)).toHaveLength(0)
  })

  it('shows a cancelled order closed: its record, no preview and a disabled schedule form', async () => {
    installFetch({
      capabilities: [...WITH_CANCEL, SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY],
      onDetail: () => json(detail('Approved', 'Cancelled', '5')),
      onCancellation: () => json(RECORD),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    expect((await screen.findByTestId('sales-draft-state')).textContent).toContain(ORDER_DETAIL_TEXT.stateCancelled)
    await waitFor(() => expect(screen.getByTestId('sales-cancellation-reason-text').textContent).toBe('Müşteri vazgeçti'))
    expect(screen.queryByTestId('sales-cancellation-open')).toBeNull()
    const schedule = screen.getByTestId('sales-delivery-schedule-form')
    expect(within(schedule).getByText(ORDER_DETAIL_TEXT.deliverySchedule.stateUnavailable)).toBeTruthy()
  })
})

describe('cancellation wire decoders', () => {
  const approvedDetail = () => decodeDraftDetail(detail(), DRAFT) as DraftDetail

  it('reads a preview only as the preview of this detail, with nulls exactly for an unreadable owner', () => {
    expect(decodeCancellationImpact(impact(), approvedDetail())?.cancellable).toBe(true)
    expect(decodeCancellationImpact(impact({ allocations: 'Unreadable' }), approvedDetail())?.lines[0].activeAllocations).toBeNull()
    // Cancellable while a source blocks is a contradiction; so is a number for an unreadable owner, or a zero stood in for null.
    expect(decodeCancellationImpact(impact({ allocations: 'Present', count: 1, cancellable: true }), approvedDetail())).toBeNull()
    const numbersForUnreadable = impact({ allocations: 'Unreadable' })
    numbersForUnreadable.lines[0].activeAllocations = 0
    expect(decodeCancellationImpact(numbersForUnreadable, approvedDetail())).toBeNull()
    const nullForReadable = impact()
    nullForReadable.lines[0].allocatedKg = null
    expect(decodeCancellationImpact(nullForReadable, approvedDetail())).toBeNull()
    // Another version, another category order or a malformed token is not this preview.
    expect(decodeCancellationImpact({ ...impact(), draftVersion: '5' }, approvedDetail())).toBeNull()
    expect(decodeCancellationImpact({ ...impact(), sources: [...impact().sources].reverse() }, approvedDetail())).toBeNull()
    expect(decodeCancellationImpact(impact({ token: 'not-a-token' }), approvedDetail())).toBeNull()
  })

  it('reads the lifecycle Cancelled only beside an approved order, and a record exactly', () => {
    expect(decodeDraftSummary(detail('Approved', 'Cancelled').draft)?.state).toBe('Cancelled')
    expect(decodeDraftSummary(detail('Preparing', 'Cancelled').draft)).toBeNull()
    // No 30: Closed is a known terminal state now (beside Approved); a state this build does not know is still refused.
    expect(decodeDraftSummary(detail('Approved', 'Closed').draft)?.state).toBe('Closed')
    expect(decodeDraftSummary(detail('Approved', 'Archived').draft)).toBeNull()
    expect(decodeCancellation({ cancellation: null })).toEqual({ cancellation: null })
    expect(decodeCancellation(RECORD)?.cancellation?.reason).toBe('Müşteri vazgeçti')
    expect(decodeCancellation({ cancellation: { ...RECORD.cancellation, reason: '  ' } })).toBeNull()
    expect(decodeCancellation({ cancellation: { ...RECORD.cancellation, extra: true } })).toBeNull()
  })
})
