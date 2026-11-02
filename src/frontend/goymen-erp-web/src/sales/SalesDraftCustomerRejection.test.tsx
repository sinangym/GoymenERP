/**
 * SAT-D115 (EK14) on S-04 / Order 360: "Müşteri reddini kaydet" beside the acceptance of a NEW revision (an earlier one was
 * accepted), behind the same `sales.orders.customer-acceptance.record` control. The reason is required (the screen says so
 * before sending; the server refuses it too). One idempotent POST with the expected version and the reason; the order is
 * re-read and shows the rejection, its reason and the revision the order continues on. 403 / 409 / 503 keep their own notices.
 * Component evidence, NOT_ENV: the API answers are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_CUSTOMER_ACCEPTANCE_RECORD_CAPABILITY } from './capabilities'
import { decodeDraftDetail } from './contract'
import { customerAcceptedFallback } from './orderStatus'
import { HEADER_REVISION_TEXT, ORDER_DETAIL_TEXT, ORDER_STATUS_TEXT } from './salesText'
import { ALL_SALES, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.approval
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const WITH_ACCEPTANCE = [...ALL_SALES, SALES_CUSTOMER_ACCEPTANCE_RECORD_CAPABILITY]
const REASON = 'Müşteri 15 kg istemedi'

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: 'onay-1',
  decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

/** Rev.1 approved inside; Rev.0 accepted by the customer; Rev.1 rejected when `rejected`. */
function detail(options: { rejected?: boolean; version?: string; acceptedRevision?: string | null } = {}): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = 'Approved'
  body.draft.state = 'Draft'
  body.draft.revision = 'Rev.1'
  body.draft.version = options.version ?? '8'
  body.approval = APPROVED
  const acceptedRevision = options.acceptedRevision === undefined ? 'Rev.0' : options.acceptedRevision
  body.customerAcceptance = {
    required: true,
    acceptedRevision,
    acceptedBy: acceptedRevision === null ? null : 'satis-kabul',
    acceptedAtUtc: acceptedRevision === null ? null : '2026-09-20T10:00:00Z',
    isCurrentRevisionAccepted: false,
    rejections: options.rejected
      ? [{ revision: 'Rev.1', rejectedBy: 'satis-ret', rejectedAtUtc: '2026-09-21T10:00:00Z', reason: REASON }]
      : [],
  }
  return body
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

const rejectionCalls = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/customer-rejection') && call.method === 'POST')

describe('S-04 customer rejection (SAT-D115)', () => {
  it('requires a reason, records the rejection once and shows the revision the order continues on', async () => {
    let rejected = false
    const harness = installFetch({
      capabilities: WITH_ACCEPTANCE,
      onDetail: () => json(detail({ rejected, version: rejected ? '9' : '8' })),
      onCommand: (command) => {
        expect(command).toBe('customer-rejection')
        rejected = true
        return json({ detail: detail({ rejected: true, version: '9' }), isReplay: false, originalCorrelationId: 'cr-1' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    expect((await screen.findByTestId('sales-customer-acceptance-state')).textContent).toBe(T.customerAcceptancePending)
    const commands = screen.getByTestId('sales-approval-commands')
    // Without a reason nothing is sent; the screen says why.
    fireEvent.click(within(commands).getByRole('button', { name: T.recordCustomerRejection }))
    expect((await within(commands).findByRole('alert')).textContent).toBe(T.customerRejectionReasonRequired)
    expect(within(commands).getByLabelText(T.customerRejectionReasonLabel).getAttribute('aria-invalid')).toBe('true')
    expect(rejectionCalls(harness.calls)).toHaveLength(0)

    fireEvent.change(within(commands).getByLabelText(T.customerRejectionReasonLabel), { target: { value: `  ${REASON} ` } })
    fireEvent.click(within(commands).getByRole('button', { name: T.recordCustomerRejection }))

    await waitFor(() => expect(screen.getByTestId('sales-customer-acceptance-state').textContent).toBe(T.customerRejected))
    const [sent] = rejectionCalls(harness.calls)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({ expectedVersion: '8', reason: REASON })
    expect(rejectionCalls(harness.calls)).toHaveLength(1)
    expect(screen.getByTestId('sales-customer-rejection-reason').textContent).toBe(REASON)
    expect(screen.getByTestId('sales-customer-rejection-note').textContent).toBe(T.customerRejectionNote('Rev.0'))
    // A rejected revision is neither rejected again nor accepted from the screen.
    expect(screen.queryByRole('button', { name: T.recordCustomerRejection })).toBeNull()
    expect(screen.queryByRole('button', { name: T.recordCustomerAcceptance })).toBeNull()
  })

  it.each([
    ['without the control', ALL_SALES, {}],
    ['for a revision never accepted before (Rev.0 behaviour unchanged)', WITH_ACCEPTANCE, { acceptedRevision: null }],
  ] as const)('is not offered %s', async (_name, capabilities, options) => {
    const harness = installFetch({ capabilities: [...capabilities], onDetail: () => json(detail(options)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-customer-acceptance-state')
    expect(screen.queryByRole('button', { name: T.recordCustomerRejection })).toBeNull()
    expect(rejectionCalls(harness.calls)).toHaveLength(0)
  })

  it.each([
    ['403', 403, { status: 403, code: 'forbidden' }, T.forbidden],
    ['409', 409, { status: 409, code: 'sales_state_conflict', errors: [{ field: 'approvalState', code: 'sales_state_conflict' }] }, T.conflict],
    ['503', 503, { status: 503, code: 'sales_store_unavailable' }, T.unknown],
  ] as const)('shows its own notice on %s', async (_name, status, problem, text) => {
    installFetch({
      capabilities: WITH_ACCEPTANCE,
      onDetail: () => json(detail()),
      onCommand: () => json(problem, status),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const commands = await screen.findByTestId('sales-approval-commands')
    fireEvent.change(within(commands).getByLabelText(T.customerRejectionReasonLabel), { target: { value: REASON } })
    fireEvent.click(within(commands).getByRole('button', { name: T.recordCustomerRejection }))
    await waitFor(() => expect(screen.getByTestId('sales-approval-notice').textContent).toContain(text))
    if (status === 503) expect(screen.getByRole('button', { name: T.retry })).toBeTruthy()
  })

  it('decodes the rejections strictly and derives the accepted fallback revision', () => {
    const decoded = decodeDraftDetail(detail({ rejected: true, version: '9' }), null)
    expect(decoded?.customerAcceptance.rejections).toEqual([
      { revision: 'Rev.1', rejectedBy: 'satis-ret', rejectedAtUtc: '2026-09-21T10:00:00Z', reason: REASON },
    ])
    expect(customerAcceptedFallback(decoded!)).toBe('Rev.0')
    // The member is additive: absent means none.
    const legacy = detail()
    delete legacy.customerAcceptance.rejections
    expect(decodeDraftDetail(legacy, null)?.customerAcceptance.rejections).toEqual([])
    expect(customerAcceptedFallback(decodeDraftDetail(legacy, null)!)).toBeNull()
    // A rejection of the accepted revision, an unknown member or a blank reason fails closed.
    for (const broken of [
      { revision: 'Rev.0', rejectedBy: 'x', rejectedAtUtc: '2026-09-21T10:00:00Z', reason: REASON },
      { revision: 'Rev.1', rejectedBy: 'x', rejectedAtUtc: '2026-09-21T10:00:00Z', reason: '' },
      { revision: 'Rev.1', rejectedBy: 'x', rejectedAtUtc: '2026-09-21T10:00:00Z', reason: REASON, extra: 1 },
    ]) {
      const body = detail()
      body.customerAcceptance.rejections = [broken]
      expect(decodeDraftDetail(body, null)).toBeNull()
    }
  })

  it('has the Turkish texts of the acceptance axis and of the SAT-D152 / SAT-D157 refusals', () => {
    expect(ORDER_STATUS_TEXT.acceptance.Rejected).toMatch(/reddetti/)
    expect(HEADER_REVISION_TEXT.fieldCode.sales_header_revision_quote_customer).toMatch(/Tekliften dönen siparişin carisi değiştirilemez/)
    expect(HEADER_REVISION_TEXT.fieldCode.sales_due_terms_amount_not_splittable).toMatch(/^Tutar bu kadar taksite bölünemez/)
  })
})
