/**
 * AT-021 / SAT-D1 on S-04: the below-floor exception approval control. It appears only for an actor holding
 * `sales.orders.price-floor-exception.approve` while the server reports a pending (not yet approved) floor exception; a
 * reason is required on every pending line before anything is sent; it sends exactly the server's pending lines with the
 * pricing version; a recorded approval re-reads the order. Test-debt item TEK-13 (only the unknown-outcome paths were
 * covered, inside SalesDraftLinePricing.test.tsx). Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY } from './capabilities'
import { LINE_PRICING_TEXT as P } from './salesText'
import { resetPendingPriceFloorApprovals } from './pendingPriceFloorApprovals'
import { ALL_SALES, COMPANY_A, KG, installFetch, json, renderSalesApp, type RecordedCall } from './testHarness'

type Body = Record<string, unknown> & { draft: Record<string, unknown>; lines: Record<string, unknown>[] }

const LINE = '66666666-6666-6666-6666-666666666666'
const LIST = 'abababab-abab-4bab-8bab-abababababab'
const path = '/satis/siparisler/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const WITH_FLOOR = [...ALL_SALES, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY]

const reference = {
  priceListId: LIST, priceListName: 'Yurt içi satış', versionNo: 2, validFrom: '2026-09-01', validThrough: null, currencyCode: 'TRY',
  priceUnitId: KG, unitPrice: '42000.50', floorUnitPrice: '40000', vatBasis: 'ExcludingVat',
  lowerQuantity: '10', upperQuantity: '20',
}

function detail(version: string): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, version, companyId: COMPANY_A }
  body.lines = [{ ...body.lines[0], unitPrice: '39000', priceUnitId: KG }]
  return body
}

const PENDING = { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: null, approvedAtUtc: null, reason: null }
const APPROVED = { ...PENDING, approvedBy: 'mudur', approvedAtUtc: '2026-10-01T09:00:00Z', reason: 'SENTETIK sözleşme' }

function pricing(version: string, floorException: Record<string, unknown> | null) {
  return {
    version,
    lines: [{ lineId: LINE, lineNo: 1, unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference,
      discountPercent: null, discountAmount: null, surchargePercent: null, surchargeAmount: null, netUnitPrice: '39000',
      floorException }],
  }
}

afterEach(() => {
  resetPendingPriceFloorApprovals()
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AT-021 below-floor exception approval control', () => {
  it('requires a reason, sends the pending line with the pricing version once, and disappears after the re-read', async () => {
    let approved = false
    const posts: RecordedCall[] = []
    installFetch({
      capabilities: WITH_FLOOR,
      onDetail: () => json(detail(approved ? '4' : '3')),
      onLinePricing: () => json(pricing(approved ? '4' : '3', approved ? APPROVED : PENDING)),
      onPriceFloorExceptionApproval: (call) => {
        posts.push(call)
        approved = true
        return json({ detail: detail('4'), isReplay: false, originalCorrelationId: 'floor-1' })
      },
    })
    renderSalesApp(path)

    const control = await screen.findByTestId('sales-price-floor-approval')
    const approve = within(control).getByRole('button', { name: P.floorApproval.approve }) as HTMLButtonElement
    // Nothing can be sent without a reason on every pending line.
    expect(approve.disabled).toBe(true)
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), '   ')
    expect(approve.disabled).toBe(true)
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), 'SENTETIK sözleşme')
    expect(approve.disabled).toBe(false)

    await userEvent.click(approve)
    await waitFor(() => expect(screen.queryByTestId('sales-price-floor-approval')).toBeNull())
    expect(posts).toHaveLength(1)
    expect(posts[0].companyId).toBe(COMPANY_A)
    expect(posts[0].idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(posts[0].body as string)).toEqual({ expectedVersion: '3', lines: [{ lineId: LINE, reason: 'SENTETIK sözleşme' }] })
  })

  it('is not shown without the approve control, even when an exception is pending', async () => {
    installFetch({
      capabilities: [...ALL_SALES],
      onDetail: () => json(detail('3')),
      onLinePricing: () => json(pricing('3', PENDING)),
    })
    renderSalesApp(path)
    await screen.findByTestId('sales-order-summary')
    await waitFor(() => expect(screen.queryByTestId('sales-price-floor-approval')).toBeNull())
  })

  it.each([
    ['no floor exception', null],
    ['an already approved exception', APPROVED],
  ] as const)('is not shown when the server reports %s', async (_name, floorException) => {
    installFetch({
      capabilities: WITH_FLOOR,
      onDetail: () => json(detail('3')),
      onLinePricing: () => json(pricing('3', floorException)),
    })
    renderSalesApp(path)
    await screen.findByTestId('sales-order-summary')
    await waitFor(() => expect(screen.queryByTestId('sales-price-floor-approval')).toBeNull())
  })

  it('shows a refusal and offers a correction without inventing an approval', async () => {
    installFetch({
      capabilities: WITH_FLOOR,
      onDetail: () => json(detail('3')),
      onLinePricing: () => json(pricing('3', PENDING)),
      onPriceFloorExceptionApproval: () => json({ status: 422, code: 'validation_failed', errors: [{ field: 'lines', code: 'validation_failed' }] }, 422),
    })
    renderSalesApp(path)

    const control = await screen.findByTestId('sales-price-floor-approval')
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), 'SENTETIK sözleşme')
    await userEvent.click(within(control).getByRole('button', { name: P.floorApproval.approve }))
    expect(await within(control).findByRole('alert')).toBeTruthy()
    expect(within(control).getByRole('alert').textContent).toBe(P.floorApproval.failed)
    expect(within(control).getByRole('button', { name: P.floorApproval.correct })).toBeTruthy()
  })
})
