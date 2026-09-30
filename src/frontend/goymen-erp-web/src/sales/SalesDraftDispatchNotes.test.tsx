/**
 * AT-055 on S-04, under "Teslimat durumu" (AT-024): the dispatch-note drafts of this order's shipments are read from the
 * Logistics owner (company-bound, read-only), listed with the order's line numbers and shipped quantities, and linked to the
 * loading instruction's detail at the draft's anchor. Nothing is read without the Logistics instruction detail capability,
 * and an answer about another order is not shown. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { LOGISTICS_CAPABILITY } from '../logistics/capabilities'
import { ORDER_DETAIL_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.dispatchNotes
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const KG = '44444444-4444-4444-4444-444444444444'
const INSTRUCTION = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'
const NOTE = '6f6f6f6f-6f6f-4f6f-8f6f-6f6f6f6f6f6f'

type Body = Record<string, any>

function approvedDetail(): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = 'Approved'
  body.approval = { submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
    rejectionReason: null }
  return body
}

const notes = (salesOrderId = DRAFT, state: Record<string, unknown> = {}) => ({
  salesOrderId,
  items: [{
    dispatchNoteId: NOTE,
    instructionId: INSTRUCTION,
    status: 'Draft',
    number: null,
    eDispatchNumber: null,
    ...state,
    createdAtUtc: '2026-09-19T10:00:00Z',
    lines: [{ lineNo: 1, sourceOrderLineId: LINE, shipmentId: '51515151-5151-4151-8151-515151515151', unitId: KG, quantity: '600' }],
  }],
  hasMore: false,
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('S-04 dispatch-note drafts (AT-055)', () => {
  it('lists the drafts of this order through a company-bound read and links each to its instruction at the draft', async () => {
    const harness = installFetch({
      capabilities: [...ALL_SALES, LOGISTICS_CAPABILITY.instructionsDetail],
      onDetail: () => json(approvedDetail()),
      onDispatchNotes: () => json(notes()),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const block = await screen.findByTestId('sales-dispatch-notes')
    const link = await within(block).findByTestId(`sales-dispatch-note-link-${NOTE}`)
    expect(link.getAttribute('href')).toBe(`/lojistik/yukleme-talimatlari/${INSTRUCTION}#irsaliye-${NOTE}`)
    expect(link.textContent).toBe(`${T.draft} …${NOTE.slice(-8)}`)
    expect(within(block).getByTestId(`sales-dispatch-note-${NOTE}`).textContent).toContain(`${T.line} 1: 600`)
    expect(within(block).getByTestId(`sales-dispatch-note-status-${NOTE}`).textContent).toBe(T.status.Draft)
    expect(within(block).queryByTestId(`sales-dispatch-note-number-${NOTE}`)).toBeNull()
    const read = harness.calls.find((call) => call.url.includes('/logistics/dispatch-notes'))
    expect(read).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    expect(read?.url).toBe(`/api/v1/logistics/dispatch-notes?salesOrderId=${DRAFT}`)
  })

  it('reads nothing without the Logistics instruction detail, and shows no list for an answer about another order', async () => {
    const harness = installFetch({ onDetail: () => json(approvedDetail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-delivery-progress')
    expect(screen.queryByTestId('sales-dispatch-notes')).toBeNull()
    expect(harness.calls.some((call) => call.url.includes('/logistics/'))).toBe(false)
    cleanup()
    vi.unstubAllGlobals()

    installFetch({
      capabilities: [...ALL_SALES, LOGISTICS_CAPABILITY.instructionsDetail],
      onDetail: () => json(approvedDetail()),
      onDispatchNotes: () => json(notes('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb')),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const block = await screen.findByTestId('sales-dispatch-notes')
    await waitFor(() => expect(within(block).queryByTestId('sales-dispatch-notes-list')).toBeNull())
    await waitFor(() => expect(within(block).queryByText(`${T.draft} …${NOTE.slice(-8)}`)).toBeNull())
  })

  it('shows each note state with its ERP number and e-Irsaliye number; a cancelled note stays listed', async () => {
    installFetch({
      capabilities: [...ALL_SALES, LOGISTICS_CAPABILITY.instructionsDetail],
      onDetail: () => json(approvedDetail()),
      onDispatchNotes: () => json(notes(DRAFT, { status: 'Cancelled', number: 'IRS-000004', eDispatchNumber: 'GYM2026000000004' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const block = await screen.findByTestId('sales-dispatch-notes')
    expect((await within(block).findByTestId(`sales-dispatch-note-status-${NOTE}`)).textContent).toBe(T.status.Cancelled)
    expect(within(block).getByTestId(`sales-dispatch-note-number-${NOTE}`).textContent).toBe('IRS-000004')
    expect(within(block).getByTestId(`sales-dispatch-note-e-number-${NOTE}`).textContent).toBe('GYM2026000000004')
    cleanup()
    vi.unstubAllGlobals()

    installFetch({
      capabilities: [...ALL_SALES, LOGISTICS_CAPABILITY.instructionsDetail],
      onDetail: () => json(approvedDetail()),
      onDispatchNotes: () => json(notes(DRAFT, { status: 'Approved', number: 'IRS-000005' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const approved = await screen.findByTestId('sales-dispatch-notes')
    expect((await within(approved).findByTestId(`sales-dispatch-note-status-${NOTE}`)).textContent).toBe(T.status.Approved)
    expect(within(approved).getByTestId(`sales-dispatch-note-number-${NOTE}`).textContent).toBe('IRS-000005')
    expect(within(approved).queryByTestId(`sales-dispatch-note-e-number-${NOTE}`)).toBeNull()
  })

  it('an Approved note without a number is not an answer', async () => {
    installFetch({
      capabilities: [...ALL_SALES, LOGISTICS_CAPABILITY.instructionsDetail],
      onDetail: () => json(approvedDetail()),
      onDispatchNotes: () => json(notes(DRAFT, { status: 'Approved' })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const block = await screen.findByTestId('sales-dispatch-notes')
    await within(block).findByText(/Beklenmeyen yanıt/)
    expect(within(block).queryByTestId('sales-dispatch-notes-list')).toBeNull()
    await waitFor(() => expect(within(block).queryByTestId(`sales-dispatch-note-${NOTE}`)).toBeNull())
  })
})
