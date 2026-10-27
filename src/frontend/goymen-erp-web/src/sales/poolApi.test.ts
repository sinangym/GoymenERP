/**
 * S-01 transport: the pinned status, the replay header agreeing with the body, and the answer describing THIS attempt —
 * anything less is `malformed` (the write may exist; the caller keeps the key).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { convertQuote, createQuote, createRequest, fetchQuotePage, fetchRequestDetail, recordQuoteLoss, sendRequestDecision } from './poolApi'
import { decodeQuoteDetail, type CreateSalesQuoteBody, type CreateSalesRequestBody } from './poolContract'
import {
  ACCOUNT,
  COMPANY_A,
  COMPANY_B,
  CURRENCY,
  KG,
  ORDER_ID,
  PARTY,
  PRODUCT,
  QUOTE_ID,
  QUOTE_LINE,
  REQUEST_ID,
  REQUEST_LINE,
  json,
  orderMutation,
  quoteDetail,
  quoteMutation,
  quoteSummary,
  requestDetail,
  requestMutation,
} from './poolFixtures'

afterEach(() => vi.unstubAllGlobals())

function answer(response: Response) {
  const fetch = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => response.clone())
  vi.stubGlobal('fetch', fetch)
  return fetch
}

const CONTEXT = { idempotencyKey: 'k-1', companyId: COMPANY_A }

const REQUEST_BODY: CreateSalesRequestBody = {
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  businessDate: '2026-10-20',
  lines: [{ lineId: REQUEST_LINE, productId: PRODUCT, expectedProductVersion: '7', orderUnitId: KG, quantity: '1500', requestedDeliveryDate: '2026-11-01', note: null }],
  requestedDeliveryDate: null,
  note: null,
}

describe('reads', () => {
  it('refuses a row of another company and a detail of another record', async () => {
    answer(json({ items: [quoteSummary({ companyId: COMPANY_B })], nextCursor: null }))
    expect((await fetchQuotePage(null, COMPANY_A)).type).toBe('malformed')
    answer(json(requestDetail()))
    expect((await fetchRequestDetail('a1a1a1a1-0000-4000-8000-000000000009', COMPANY_A)).type).toBe('malformed')
  })

  it('sends the exact state filter to the server and the company as context', async () => {
    const fetch = answer(json({ items: [], nextCursor: null }))
    await fetchQuotePage('c1', COMPANY_A, undefined, 'Open')
    const [url, init] = fetch.mock.calls[0]
    expect(String(url)).toContain('/sales/quotes?pageSize=50&cursor=c1&state=Open')
    const sent = init?.headers as Record<string, string> | undefined
    expect(sent?.['X-Company-Id']).toBe(COMPANY_A)
  })
})

describe('request commands', () => {
  it('accepts the 201 that describes the sent create and sends the key', async () => {
    const fetch = answer(json(requestMutation(requestDetail()), 201, { Location: `/api/v1/sales/requests/${REQUEST_ID}` }))
    const outcome = await createRequest(REQUEST_BODY, CONTEXT)
    expect(outcome.type).toBe('succeeded')
    const sent = fetch.mock.calls[0][1]?.headers as Record<string, string> | undefined
    expect(sent?.['Idempotency-Key']).toBe('k-1')
  })

  it('reads a 201 for another quantity, a 200, or a replay body without the header as unknown', async () => {
    answer(json(requestMutation(requestDetail({}, { lines: [{ ...(requestDetail().lines as object[])[0], quantity: '1600' }] })), 201))
    expect((await createRequest(REQUEST_BODY, CONTEXT)).type).toBe('malformed')
    answer(json(requestMutation(requestDetail()), 200))
    expect((await createRequest(REQUEST_BODY, CONTEXT)).type).toBe('malformed')
    answer(json(requestMutation(requestDetail(), true), 201))
    expect((await createRequest(REQUEST_BODY, CONTEXT)).type).toBe('malformed')
  })

  it('a decision answers 200 for this request only; a refusal keeps its problem', async () => {
    answer(json(requestMutation(requestDetail({ state: 'Approved', version: '2' })), 200))
    expect((await sendRequestDecision('approval', { expectedVersion: '1' }, { ...CONTEXT, requestId: REQUEST_ID })).type).toBe('succeeded')
    answer(json(requestMutation(requestDetail({ state: 'Approved', version: '2', companyId: COMPANY_B })), 200))
    expect((await sendRequestDecision('approval', { expectedVersion: '1' }, { ...CONTEXT, requestId: REQUEST_ID })).type).toBe('malformed')
    answer(json({ status: 409, title: 'x', code: 'sales_request_has_approved_order' }, 409))
    const refused = await sendRequestDecision('rejection', { expectedVersion: '2', reason: 'r' }, { ...CONTEXT, requestId: REQUEST_ID })
    expect(refused.type === 'http' && refused.problem?.code).toBe('sales_request_has_approved_order')
  })
})

describe('quote commands', () => {
  const body: CreateSalesQuoteBody = {
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    currencyId: CURRENCY,
    businessDate: '2026-10-22',
    lines: [{ lineId: QUOTE_LINE, productId: PRODUCT, expectedProductVersion: '7', orderUnitId: KG, quantity: '1500', unitPrice: '42.5', priceUnitId: KG, discountPercent: null, requestedDeliveryDate: null, note: null }],
    requestIds: [REQUEST_ID],
    validUntil: '2026-11-22',
    requestedDeliveryDate: null,
    note: null,
  }

  it('a create must name the same requests and prices it was sent', async () => {
    answer(json(quoteMutation(quoteDetail()), 201))
    expect((await createQuote(body, CONTEXT)).type).toBe('succeeded')
    answer(json(quoteMutation(quoteDetail({}, { requestIds: [] })), 201))
    expect((await createQuote(body, CONTEXT)).type).toBe('malformed')
    answer(json(quoteMutation(quoteDetail()), 201))
    expect((await createQuote({ ...body, lines: [{ ...body.lines[0], unitPrice: '43' }] }, CONTEXT)).type).toBe('malformed')
  })

  it('a loss answer of another quote is not this command', async () => {
    answer(json(quoteMutation(quoteDetail({ id: 'b2b2b2b2-0000-4000-8000-000000000009', state: 'Lost' })), 200))
    expect((await recordQuoteLoss({ expectedVersion: '1', reason: 'r' }, { ...CONTEXT, quoteId: QUOTE_ID })).type).toBe('malformed')
  })

  it('the conversion answers THIS quote’s order (201 + the order’s Location), else unknown', async () => {
    const quote = decodeQuoteDetail(quoteDetail(), QUOTE_ID)!
    answer(json(orderMutation('2026-10-27'), 201, { Location: `/api/v1/sales/drafts/${ORDER_ID}` }))
    const ok = await convertQuote({ expectedVersion: '1', businessDate: '2026-10-27' }, quote, CONTEXT)
    expect(ok.type === 'succeeded' && ok.data.detail.draft.id).toBe(ORDER_ID)
    // Another business date, another quantity, a Location of another resource: not this conversion.
    answer(json(orderMutation('2026-10-28'), 201))
    expect((await convertQuote({ expectedVersion: '1', businessDate: '2026-10-27' }, quote, CONTEXT)).type).toBe('malformed')
    answer(json(orderMutation('2026-10-27', false, { quantity: '1' }), 201))
    expect((await convertQuote({ expectedVersion: '1', businessDate: '2026-10-27' }, quote, CONTEXT)).type).toBe('malformed')
    // A late same-key replay answers the order as it stands now (revised since): its header still binds it.
    answer(json(orderMutation('2026-10-27', true, { quantity: '1' }), 201, { 'Idempotency-Replayed': 'true' }))
    expect((await convertQuote({ expectedVersion: '1', businessDate: '2026-10-27' }, quote, CONTEXT)).type).toBe('succeeded')
    answer(json(orderMutation('2026-10-28', true), 201, { 'Idempotency-Replayed': 'true' }))
    expect((await convertQuote({ expectedVersion: '1', businessDate: '2026-10-27' }, quote, CONTEXT)).type).toBe('malformed')
    answer(json(orderMutation('2026-10-27'), 201, { Location: `/api/v1/sales/quotes/${QUOTE_ID}` }))
    expect((await convertQuote({ expectedVersion: '1', businessDate: '2026-10-27' }, quote, CONTEXT)).type).toBe('malformed')
  })
})
