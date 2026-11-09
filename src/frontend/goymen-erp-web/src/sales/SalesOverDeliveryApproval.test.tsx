/**
 * SAT-D30 / SAT-D213 / SAT-D216 on S-04: "Fazla teslimi onayla" is offered only behind the `sales.orders.over-delivery.approve`
 * control and only for the lines the server answers as awaiting approval; the command names the draft version, the line and the
 * shipped total shown; a recorded approval re-reads the order, whose status then marks "onaylı fazla teslim"; the server's
 * refusals (shipper not named, self-approval, total changed) are said in Turkish and re-read the order; an unknown outcome is
 * retried with the same key. Component evidence, NOT_ENV: the API answers are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import statusResponseRaw from './examples/status-response.json?raw'
import { SALES_OVER_DELIVERY_APPROVE_CAPABILITY } from './capabilities'
import { OVER_DELIVERY_TEXT as T, ORDER_STATUS_TEXT, PROBLEM_CODE_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const WITH_APPROVE = [...ALL_SALES, SALES_OVER_DELIVERY_APPROVE_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

function detail(version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, approvalState: 'Approved', state: 'Draft', version }
  body.approval = APPROVED
  return body
}

/** Line 1 (1000) shipped 1025: 2.5 % over — the approval band; awaiting, or approved once the version moved. */
function status(approved = false, version = '4'): Body {
  const body = JSON.parse(statusResponseRaw) as Body
  body.draftVersion = version
  Object.assign(body.realization, { state: approved ? 'Completed' : 'AwaitingApproval', warning: false })
  Object.assign(body.realization.lines[0], {
    shipped: '1025', delivered: '0', deviation: '25', band: 'Approval', warning: false,
    state: approved ? 'Completed' : 'AwaitingApproval', approvedOverDelivery: approved,
  })
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

const approvals = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/over-delivery-approval') && call.method === 'POST')

describe('S-04 over-delivery approval (SAT-D30, SAT-D213, SAT-D216)', () => {
  it('approves the awaiting line at the shown total and the re-read status marks the approved over-delivery', async () => {
    let approved = false
    const harness = installFetch({
      capabilities: WITH_APPROVE,
      onDetail: () => json(approved ? detail('5') : detail()),
      onStatus: () => json(approved ? status(true, '5') : status()),
      onOverDeliveryApproval: () => {
        approved = true
        return json({ detail: detail('5'), isReplay: false, originalCorrelationId: 'c-1' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-over-delivery')
    const row = within(section).getByTestId('sales-over-delivery-line-1')
    // The server's numbers only: ordered, shipped and the excess (deviation) — nothing computed here.
    expect(row.textContent).toContain('1000')
    expect(row.textContent).toContain('1025')
    expect(row.textContent).toContain('25')
    fireEvent.click(within(section).getByRole('button', { name: T.approveLabel(1) }))

    await waitFor(() => expect(approvals(harness.calls)).toHaveLength(1))
    const [sent] = approvals(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({ expectedVersion: '4', lineId: LINE, shipped: '1025' })
    await waitFor(() => expect(screen.getByTestId('sales-status-line-1-state-approved-over').textContent)
      .toContain(ORDER_STATUS_TEXT.approvedOverDeliveryMark))
    expect(screen.queryByTestId('sales-over-delivery-approve-1')).toBeNull()
  })

  it('offers nothing without the control, and nothing when no line awaits approval', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()), onStatus: () => json(status()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-status-line-1')
    expect(screen.queryByTestId('sales-over-delivery')).toBeNull()
    cleanup()

    installFetch({ capabilities: WITH_APPROVE, onDetail: () => json(detail()), onStatus: () => json(status(true)) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-status-line-1-state-approved-over')
    expect(screen.queryByTestId('sales-over-delivery')).toBeNull()
  })

  it.each([
    ['sales_over_delivery_shipper_unverified', 'shippedBy'],
    ['sales_over_delivery_self_approval', 'actor'],
    ['sales_over_delivery_shipped_changed', 'shipped'],
  ])('says the recorded refusal %s in Turkish and reads the order again', async (code, field) => {
    let statusReads = 0
    installFetch({
      capabilities: WITH_APPROVE,
      onDetail: () => json(detail()),
      onStatus: () => {
        statusReads += 1
        return json(status())
      },
      onOverDeliveryApproval: () => json({ status: 409, code, errors: [{ field, code }] }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-over-delivery-approve-1'))
    await waitFor(() => expect(screen.getByTestId('sales-over-delivery-notice').textContent)
      .toContain(PROBLEM_CODE_TEXT[code as keyof typeof PROBLEM_CODE_TEXT]))
    expect(screen.getByTestId('sales-over-delivery-notice').getAttribute('role')).toBe('alert')
    await waitFor(() => expect(statusReads).toBeGreaterThan(1))
  })

  it('says the block band and a missing grant without retrying', async () => {
    const harness = installFetch({
      capabilities: WITH_APPROVE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onOverDeliveryApproval: () => json({
        status: 422, code: 'sales_over_delivery_blocked', errors: [{ field: 'lineId', code: 'sales_over_delivery_blocked' }],
      }, 422),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-over-delivery-approve-1'))
    await waitFor(() => expect(screen.getByTestId('sales-over-delivery-notice').textContent)
      .toContain(PROBLEM_CODE_TEXT.sales_over_delivery_blocked))
    expect(screen.getByTestId('sales-over-delivery-notice').textContent).toContain(T.validation)
    expect(approvals(harness.calls)).toHaveLength(1)
  })

  it('keeps the same key when the outcome is unknown and the retry is sent', async () => {
    let attempts = 0
    const harness = installFetch({
      capabilities: WITH_APPROVE,
      onDetail: () => json(detail()),
      onStatus: () => json(status()),
      onOverDeliveryApproval: () => {
        attempts += 1
        return attempts === 1
          ? json({ status: 503 }, 503)
          : json({ detail: detail('5'), isReplay: true, originalCorrelationId: 'c-1' }, 200, { 'Idempotency-Replayed': 'true' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    fireEvent.click(await screen.findByTestId('sales-over-delivery-approve-1'))
    await waitFor(() => expect(screen.getByTestId('sales-over-delivery-notice').textContent).toBe(T.unknown))
    expect(screen.getByTestId('sales-over-delivery-approve-1')).toHaveProperty('disabled', true)
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(approvals(harness.calls)).toHaveLength(2))
    const [first, second] = approvals(harness.calls)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toBe(first.body)
  })
})
