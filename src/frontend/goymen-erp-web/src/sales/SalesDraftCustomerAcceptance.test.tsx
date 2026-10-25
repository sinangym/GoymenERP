/**
 * Q9 on S-04: "Müşteri kabulünü kaydet" — offered only on an APPROVED, not cancelled order that requires customer
 * acceptance and whose current revision is not yet accepted, and only behind the
 * `sales.orders.customer-acceptance.record` control. One idempotent POST with the expected version; the order is re-read
 * and shows the acceptance as recorded. Test-debt item TEK-13 (the browser spec S04-CA-01 covered this alone until now).
 * Component evidence, NOT_ENV: the API answers are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_CUSTOMER_ACCEPTANCE_RECORD_CAPABILITY } from './capabilities'
import { ORDER_DETAIL_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.approval
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const WITH_ACCEPTANCE = [...ALL_SALES, SALES_CUSTOMER_ACCEPTANCE_RECORD_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: 'onay-1',
  decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

function detail(options: { accepted?: boolean; required?: boolean; approvalState?: string; state?: string; version?: string } = {}): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = options.approvalState ?? 'Approved'
  body.draft.state = options.state ?? 'Draft'
  body.draft.version = options.version ?? '4'
  body.approval = body.draft.approvalState === 'Approved' ? APPROVED
    : body.draft.approvalState === 'PendingApproval' ? { ...APPROVED, decidedBy: null, decidedAtUtc: null }
      : body.approval
  const accepted = options.accepted ?? false
  const required = options.required ?? true
  body.customerAcceptance = {
    required,
    acceptedRevision: accepted ? 'Rev.0' : null,
    acceptedBy: accepted ? 'satis-kabul' : null,
    acceptedAtUtc: accepted ? '2026-09-20T10:00:00Z' : null,
    // A legacy order (no acceptance required) always reads as accepted: the wire carries that, never a pending gate.
    isCurrentRevisionAccepted: accepted || !required,
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

const acceptanceCalls = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/customer-acceptance') && call.method === 'POST')

describe('S-04 customer acceptance (Q9)', () => {
  it('records the acceptance of the current revision once, with the expected version, and shows it after the re-read', async () => {
    let accepted = false
    const harness = installFetch({
      capabilities: WITH_ACCEPTANCE,
      onDetail: () => json(detail({ accepted, version: accepted ? '5' : '4' })),
      onCommand: (command) => {
        expect(command).toBe('customer-acceptance')
        accepted = true
        return json({ detail: detail({ accepted: true, version: '5' }), isReplay: false, originalCorrelationId: 'ca-1' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    expect((await screen.findByTestId('sales-customer-acceptance-state')).textContent).toBe(T.customerAcceptancePending)
    const commands = screen.getByTestId('sales-approval-commands')
    fireEvent.click(within(commands).getByRole('button', { name: T.recordCustomerAcceptance }))

    await waitFor(() => expect(screen.getByTestId('sales-customer-acceptance-state').textContent).toBe(T.customerAcceptanceRecorded))
    const [sent] = acceptanceCalls(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({ expectedVersion: '4' })
    expect(acceptanceCalls(harness.calls)).toHaveLength(1)
    expect(screen.queryByRole('button', { name: T.recordCustomerAcceptance })).toBeNull()
  })

  it.each([
    ['without the control', ALL_SALES, {}],
    ['when the revision is already accepted', WITH_ACCEPTANCE, { accepted: true }],
    ['when the order does not require acceptance', WITH_ACCEPTANCE, { required: false }],
    ['before the order is approved', WITH_ACCEPTANCE, { approvalState: 'PendingApproval' }],
    ['on a cancelled order', WITH_ACCEPTANCE, { state: 'Cancelled' }],
  ] as const)('is not offered %s', async (_name, capabilities, options) => {
    const harness = installFetch({ capabilities: [...capabilities], onDetail: () => json(detail(options)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-customer-acceptance-state')
    expect(screen.queryByRole('button', { name: T.recordCustomerAcceptance })).toBeNull()
    expect(acceptanceCalls(harness.calls)).toHaveLength(0)
  })

  it('says the order moved and re-reads it when the server refuses a stale version', async () => {
    let reads = 0
    installFetch({
      capabilities: WITH_ACCEPTANCE,
      onDetail: () => {
        reads += 1
        return json(detail())
      },
      onCommand: () => json({ status: 409, code: 'version_conflict', errors: [{ field: 'expectedVersion', code: 'version_conflict' }] }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByRole('button', { name: T.recordCustomerAcceptance }))
    await waitFor(() => expect(screen.getByTestId('sales-approval-notice').textContent).toContain(T.versionConflict))
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })
})
