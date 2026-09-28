/**
 * DEC-SAL-002 / AT-023 on S-04: "Revizyon oluştur" is offered only behind the `sales.orders.quantity.write` control, for a
 * Preparing / Rejected / Approved order; it sends every line's new quantity and a required reason under one idempotency key;
 * a recorded revision re-reads the order (Rev.1, back in preparation) and the revision history shows Rev.0 with its reason
 * and the quantity that followed it. A blocked revision (an allocation or loading line in its owner) says why and changes
 * nothing. Component evidence, NOT_ENV: the API answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import revisionsRaw from './examples/revisions-response.json?raw'
import { SALES_QUANTITY_WRITE_CAPABILITY } from './capabilities'
import { buildQuantityRevisionRequest, sameQuantity } from './quantityRevision'
import { resetPendingQuantityRevisions } from './pendingQuantityRevisions'
import { ORDER_DETAIL_TEXT, REVISION_HISTORY_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.quantityRevision
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const WITH_QUANTITY = [...ALL_SALES, SALES_QUANTITY_WRITE_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}
const NOT_YET = { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }

function detail(approvalState = 'Approved', version = '4', revision = 'Rev.0', quantity = '1000'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = approvalState
  body.draft.version = version
  body.draft.revision = revision
  body.approval = approvalState === 'Approved' ? APPROVED
    : approvalState === 'PendingApproval' ? { ...NOT_YET, submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z' }
      : NOT_YET
  body.lines[0].quantity = quantity
  return body
}

function archivedRev0(reason: string | null): Body {
  const body = JSON.parse(revisionsRaw) as Body
  body.items[0].lines[0].lineId = LINE
  body.items[0].lines[0].quantity = '1000'
  body.items[0].reason = reason
  return body
}

afterEach(() => {
  cleanup()
  resetPendingQuantityRevisions()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const revisionPosts = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/quantity-revision') && call.method === 'POST')

describe('S-04 quantity revision (DEC-SAL-002, AT-023)', () => {
  it('is not offered without the quantity control', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-draft-revision')
    expect(screen.queryByTestId('sales-quantity-revision')).toBeNull()
    expect(screen.queryByText(T.open)).toBeNull()
  })

  it('revises an approved order with a reason under one key, re-reads Rev.1 and shows Rev.0 with its reason and the diff', async () => {
    let revised = false
    const harness = installFetch({
      capabilities: WITH_QUANTITY,
      onDetail: () => json(revised ? detail('Preparing', '5', 'Rev.1', '1200') : detail()),
      onQuantityRevision: () => {
        revised = true
        return json({ detail: detail('Preparing', '5', 'Rev.1', '1200'), isReplay: false, originalCorrelationId: 'c-1' })
      },
      onRevisions: () => json(revised ? archivedRev0('SENTETIK müşteri 1200 istedi') : { items: [] }),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const entry = await screen.findByTestId('sales-quantity-revision')
    fireEvent.click(within(entry).getByRole('button', { name: T.open }))
    const form = await screen.findByTestId('sales-quantity-revision-form')
    expect(within(form).getByText(T.approvedWarning)).toBeTruthy()

    // A reason is required and nothing is sent without one; an unchanged quantity is not a revision either.
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await within(form).findByText(T.localError(null, 'reason-required'))).toBeTruthy()
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'SENTETIK müşteri 1200 istedi' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await within(form).findByText(T.localError(null, 'no-change'))).toBeTruthy()
    expect(revisionPosts(harness.calls)).toHaveLength(0)

    fireEvent.change(within(form).getByLabelText(T.quantity('44444444-4444-4444-4444-444444444444')), { target: { value: '1200' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))

    await waitFor(() => expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.1'))
    const [sent] = revisionPosts(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({
      expectedVersion: '4', reason: 'SENTETIK müşteri 1200 istedi', lines: [{ lineId: LINE, quantity: '1200' }],
    })
    expect(await screen.findByText(T.completed)).toBeTruthy()

    // The history (read afresh for the new version) shows Rev.0 as approved, with the reason and the quantity after it.
    await waitFor(() => expect(screen.getByTestId('sales-revision-Rev.0-reason').textContent).toBe('SENTETIK müşteri 1200 istedi'))
    expect(screen.getByTestId('sales-revision-Rev.0-line-1-next').textContent).toBe(`1200 (${REVISION_HISTORY_TEXT.changed})`)
    expect(within(screen.getByTestId('sales-revision-Rev.0')).getByText('onay-1', { exact: false })).toBeTruthy()
  })

  it('says why a blocked revision was refused and changes nothing', async () => {
    const harness = installFetch({
      capabilities: WITH_QUANTITY,
      onDetail: () => json(detail()),
      onQuantityRevision: () => json({
        status: 409, title: 'Conflict', code: 'sales_revision_blocked', correlationId: 'c-2',
        errors: [
          { field: 'inventory.active-allocations', code: 'Present' },
          { field: 'logistics.loading-without-dispatch-note', code: 'Present' },
        ],
      }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    fireEvent.click(within(await screen.findByTestId('sales-quantity-revision')).getByRole('button', { name: T.open }))
    const form = await screen.findByTestId('sales-quantity-revision-form')
    fireEvent.change(within(form).getByLabelText(T.quantity('44444444-4444-4444-4444-444444444444')), { target: { value: '1200' } })
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'neden' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))

    const alert = await within(form).findByRole('alert')
    expect(alert.textContent).toContain(T.blockedSource('inventory.active-allocations'))
    expect(alert.textContent).toContain(T.blockedSource('logistics.loading-without-dispatch-note'))
    expect(revisionPosts(harness.calls)).toHaveLength(1)
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
  })

  it('is closed while the order waits for a decision', async () => {
    installFetch({ capabilities: WITH_QUANTITY, onDetail: () => json(detail('PendingApproval', '3')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const entry = await screen.findByTestId('sales-quantity-revision')
    expect((within(entry).getByRole('button', { name: T.open }) as HTMLButtonElement).disabled).toBe(true)
    expect(within(entry).getByText(T.stateUnavailable)).toBeTruthy()
  })
})

describe('quantity revision request', () => {
  const row = (quantity: string) => ({ lineId: LINE, lineNo: 1, productName: 'x', orderUnitId: 'u', current: '1.25', quantity })

  it('builds every line with its trimmed text and the reason as typed', () => {
    expect(buildQuantityRevisionRequest('7', ' neden ', [row(' 2.5 ')])).toEqual({
      ok: true, payload: { expectedVersion: '7', reason: ' neden ', lines: [{ lineId: LINE, quantity: '2.5' }] },
    })
  })

  it.each([['', 'quantity-format'], ['0', 'quantity-format'], ['1,5', 'quantity-format'], ['-1', 'quantity-format'], ['1.250', 'no-change']])(
    'refuses %j locally (%s)', (quantity, reason) => {
      const built = buildQuantityRevisionRequest('7', 'neden', [row(quantity)])
      expect(built.ok).toBe(false)
      expect(built.ok ? null : built.reason).toBe(reason)
    })

  it('compares quantities by value without floating point', () => {
    expect(sameQuantity('1.250', '1.25')).toBe(true)
    expect(sameQuantity('001', '1')).toBe(true)
    expect(sameQuantity('0.10', '0.1')).toBe(true)
    expect(sameQuantity('1.2', '1.25')).toBe(false)
  })
})
