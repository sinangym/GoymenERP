/**
 * AT-024 on S-04 ("Kısmi teslimat sipariş kalanını doğru tutar"): the "Teslimat durumu" section shows per line the
 * shipped / delivered / remaining / excess quantity exactly as the server answers it (read from Logistics' recorded
 * deliveries), reads it afresh on every open, never invents a number for a failed or foreign answer and derives no
 * tolerance or completion state. Component evidence, NOT_ENV: the API answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeDeliveryProgress, decodeDraftDetail, type DraftDetail } from './contract'
import { ORDER_DETAIL_TEXT } from './salesText'
import { COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.deliveryProgress
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const KG = '44444444-4444-4444-4444-444444444444'

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: 'onay-1',
  decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}
const NOT_YET = { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }

function detail(state = 'Approved', revision = 'Rev.0', version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = state
  body.draft.revision = revision
  body.draft.version = version
  body.approval = state === 'Approved' ? APPROVED : NOT_YET
  return body
}

function progress(values: { shipped: string; delivered: string; remaining: string; excess?: string }, version = '4'): Body {
  return {
    draftVersion: version,
    lines: [{ lineId: LINE, lineNo: 1, orderUnitId: KG, ordered: '1000', excess: '0', ...values }],
  }
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

const progressCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/delivery-progress'))

describe('S-04 delivery progress (AT-024)', () => {
  it('shows the server’s shipped, delivered and remaining quantity per line through a company-bound read-only GET', async () => {
    const harness = installFetch({
      onDetail: () => json(detail()),
      onDeliveryProgress: () => json(progress({ shipped: '600', delivered: '400', remaining: '600' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-delivery-progress')
    await within(section).findByTestId('sales-delivery-progress-line-1')
    expect(within(section).getByRole('heading', { name: T.heading })).toBeTruthy()
    expect(screen.getByTestId('sales-delivery-progress-line-1-shipped').textContent).toBe('600')
    expect(screen.getByTestId('sales-delivery-progress-line-1-delivered').textContent).toBe('400')
    expect(screen.getByTestId('sales-delivery-progress-line-1-remaining').textContent).toBe('600')
    expect(screen.getByTestId('sales-delivery-progress-line-1-excess').textContent).toBe('0')
    const [read] = progressCalls(harness.calls)
    expect(read).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    expect(read.url).toContain(`/sales/drafts/${DRAFT}/delivery-progress`)
  })

  it('reads afresh when the order is opened again, so a delivery recorded meanwhile shows', async () => {
    let delivered = '400'
    const harness = installFetch({
      onDetail: () => json(detail()),
      onDeliveryProgress: () => json(delivered === '400'
        ? progress({ shipped: '600', delivered: '400', remaining: '600' })
        : progress({ shipped: '1000', delivered: '1000', remaining: '0' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await waitFor(() => expect(screen.getByTestId('sales-delivery-progress-line-1-delivered').textContent).toBe('400'))

    cleanup()
    delivered = '1000'
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await waitFor(() => expect(screen.getByTestId('sales-delivery-progress-line-1-delivered').textContent).toBe('1000'))
    expect(screen.getByTestId('sales-delivery-progress-line-1-remaining').textContent).toBe('0')
    expect(progressCalls(harness.calls)).toHaveLength(2)
  })

  it('shows more delivered than ordered as an excess fact with nothing remaining, and no completion or tolerance text', async () => {
    installFetch({
      onDetail: () => json(detail()),
      onDeliveryProgress: () => json(progress({ shipped: '1000.5', delivered: '1000.5', remaining: '0', excess: '0.5' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    await waitFor(() => expect(screen.getByTestId('sales-delivery-progress-line-1-excess').textContent).toBe('0.5'))
    expect(screen.getByTestId('sales-delivery-progress-line-1-remaining').textContent).toBe('0')
    const section = screen.getByTestId('sales-delivery-progress')
    expect(within(section).queryByText(/tamamland|kapandı|tolerans içinde|tolerans dışı/i)).toBeNull()
  })

  it('is not read for a draft that was never approved, and is read for a revised draft back in preparation', async () => {
    const preparing = installFetch({ onDetail: () => json(detail('Preparing')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByTestId('sales-delivery-progress')).toBeNull()
    expect(progressCalls(preparing.calls)).toHaveLength(0)

    cleanup()
    vi.unstubAllGlobals()
    const revised = installFetch({
      onDetail: () => json(detail('Preparing', 'Rev.1')),
      onDeliveryProgress: () => json(progress({ shipped: '400', delivered: '400', remaining: '600' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await waitFor(() => expect(screen.getByTestId('sales-delivery-progress-line-1-delivered').textContent).toBe('400'))
    expect(progressCalls(revised.calls)).toHaveLength(1)
  })

  it('shows no number when the owner cannot answer (503) and offers a read-only retry of the same URL', async () => {
    let failing = true
    const harness = installFetch({
      onDetail: () => json(detail()),
      onDeliveryProgress: () => (failing
        ? json({ status: 503 }, 503)
        : json(progress({ shipped: '600', delivered: '400', remaining: '600' }))),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-delivery-progress')
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('service-unavailable')
    expect(within(section).queryByTestId('sales-delivery-progress-line-1')).toBeNull()

    failing = false
    within(section).getByRole('button').click()
    await waitFor(() => expect(screen.getByTestId('sales-delivery-progress-line-1-delivered').textContent).toBe('400'))
    const reads = progressCalls(harness.calls)
    expect(reads).toHaveLength(2)
    expect(new Set(reads.map((call) => `${call.method} ${call.url}`)).size).toBe(1)
  })

  it('refuses an answer that does not describe exactly this detail and shows it as unexpected, never as numbers', async () => {
    installFetch({
      onDetail: () => json(detail()),
      // Another version than the detail's: stale for this screen.
      onDeliveryProgress: () => json(progress({ shipped: '600', delivered: '400', remaining: '600' }, '3')),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const section = await screen.findByTestId('sales-delivery-progress')
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('malformed-response')
    expect(within(section).queryByTestId('sales-delivery-progress-line-1')).toBeNull()
  })
})

describe('decodeDeliveryProgress', () => {
  const draftDetail = decodeDraftDetail(detail(), DRAFT) as DraftDetail

  it('keeps decimal text verbatim for the detail’s lines', () => {
    expect(decodeDeliveryProgress(progress({ shipped: '600.125', delivered: '0', remaining: '1000' }), draftDetail)).toEqual({
      draftVersion: '4',
      lines: [{
        lineId: LINE, lineNo: 1, orderUnitId: KG, ordered: '1000',
        shipped: '600.125', delivered: '0', remaining: '1000', excess: '0',
      }],
    })
  })

  it.each([
    ['an unknown member', (body: Body) => { body.lines[0].closed = true }],
    ['a missing member', (body: Body) => { delete body.lines[0].excess }],
    ['a number for a quantity', (body: Body) => { body.lines[0].delivered = 400 }],
    ['a negative quantity', (body: Body) => { body.lines[0].remaining = '-1' }],
    ['another line', (body: Body) => { body.lines[0].lineId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' }],
    ['another unit', (body: Body) => { body.lines[0].orderUnitId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' }],
    ['another ordered quantity', (body: Body) => { body.lines[0].ordered = '999' }],
    ['a missing line', (body: Body) => { body.lines = [] }],
    ['another version', (body: Body) => { body.draftVersion = '5' }],
  ])('refuses %s', (_, mutate) => {
    const body = progress({ shipped: '600', delivered: '400', remaining: '600' })
    mutate(body)
    expect(decodeDeliveryProgress(body, draftDetail)).toBeNull()
  })
})
