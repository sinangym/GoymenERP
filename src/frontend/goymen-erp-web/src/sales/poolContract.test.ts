/**
 * S-01 wire decoders, read against #824/#828's OpenAPI schemas: every member present, nothing extra, text stays text,
 * and a state never shows without its own facts.
 */

import { describe, expect, it } from 'vitest'
import {
  decodeQuoteDetail,
  decodeQuoteLossReasons,
  decodeQuoteMutation,
  decodeQuoteValiditySuggestion,
  decodeQuotePage,
  decodeQuoteSummary,
  decodeRequestDetail,
  decodeRequestMutation,
  decodeRequestPage,
  decodeRequestSummary,
} from './poolContract'
import {
  LOSS_REASON_PRICE,
  ORDER_ID,
  expiryOf,
  lossReasons,
  QUOTE_ID,
  REQUEST_ID,
  quoteDetail,
  quoteLine,
  quoteMutation,
  quoteSummary,
  requestDetail,
  requestMutation,
  requestSummary,
} from './poolFixtures'

const without = (value: Record<string, unknown>, key: string) => {
  const copy = { ...value }
  delete copy[key]
  return copy
}

describe('sales request decoders', () => {
  it('reads the summary, detail, page and mutation as the schema states them', () => {
    expect(decodeRequestSummary(requestSummary())?.documentNo).toBe('SR-2026-000001')
    const detail = decodeRequestDetail(requestDetail(), REQUEST_ID)
    expect(detail?.lines[0].quantity).toBe('1500')
    expect(detail?.decision).toBeNull()
    expect(decodeRequestPage({ items: [requestSummary()], nextCursor: null })?.items).toHaveLength(1)
    expect(decodeRequestMutation(requestMutation(requestDetail()))?.isReplay).toBe(false)
  })

  it('refuses a missing member, an extra member and a wrong case', () => {
    expect(decodeRequestSummary(without(requestSummary(), 'lineCount'))).toBeNull()
    expect(decodeRequestSummary({ ...requestSummary(), price: '1' })).toBeNull()
    expect(decodeRequestSummary({ ...without(requestSummary(), 'documentNo'), DocumentNo: 'SR-1' })).toBeNull()
    expect(decodeRequestDetail(without(requestDetail(), 'decision'), REQUEST_ID)).toBeNull()
    expect(decodeRequestPage({ items: [] })).toBeNull()
  })

  it('refuses numbers standing in for version or quantity text, and an unknown state', () => {
    expect(decodeRequestSummary(requestSummary({ version: 1 }))).toBeNull()
    expect(decodeRequestSummary(requestSummary({ state: 'Draft' }))).toBeNull()
    expect(decodeRequestSummary(requestSummary({ state: 'approved' }))).toBeNull()
    const body = requestDetail()
    ;(body.lines as Record<string, unknown>[])[0].quantity = 1500
    expect(decodeRequestDetail(body, REQUEST_ID)).toBeNull()
  })

  it('binds the detail to the record asked for and to its line count', () => {
    expect(decodeRequestDetail(requestDetail(), 'a1a1a1a1-0000-4000-8000-000000000009')).toBeNull()
    expect(decodeRequestDetail(requestDetail({ lineCount: 2 }), REQUEST_ID)).toBeNull()
  })

  it('never shows a state without its decision facts', () => {
    // Submitted with a decision, Approved without one, Approved with a reason, Rejected without a reason.
    expect(decodeRequestDetail(requestDetail({}, { decision: { decidedBy: 'x', decidedAtUtc: '2026-10-21T09:00:00Z', rejectionReason: null } }), REQUEST_ID)).toBeNull()
    expect(decodeRequestDetail(requestDetail({ state: 'Approved' }, { decision: null }), REQUEST_ID)).toBeNull()
    expect(decodeRequestDetail(requestDetail({ state: 'Approved' }, { decision: { decidedBy: 'x', decidedAtUtc: '2026-10-21T09:00:00Z', rejectionReason: 'neden' } }), REQUEST_ID)).toBeNull()
    expect(decodeRequestDetail(requestDetail({ state: 'Rejected' }, { decision: { decidedBy: 'x', decidedAtUtc: '2026-10-21T09:00:00Z', rejectionReason: null } }), REQUEST_ID)).toBeNull()
    expect(decodeRequestDetail(requestDetail({ state: 'Rejected' }), REQUEST_ID)?.decision?.rejectionReason).toBe('Stok yok')
  })

  it('refuses a page that repeats a row', () => {
    expect(decodeRequestPage({ items: [requestSummary(), requestSummary()], nextCursor: null })).toBeNull()
  })
})

describe('sales quote decoders', () => {
  it('reads the summary, detail, page and mutation as the schema states them', () => {
    expect(decodeQuoteSummary(quoteSummary())?.revision).toBe('Rev.0')
    const detail = decodeQuoteDetail(quoteDetail(), QUOTE_ID)
    expect(detail?.lines[0].unitPrice).toBe('42.5')
    expect(detail?.requestIds).toEqual([REQUEST_ID])
    expect(decodeQuotePage({ items: [quoteSummary()], nextCursor: 'c1' })?.nextCursor).toBe('c1')
    expect(decodeQuoteMutation(quoteMutation(quoteDetail(), true))?.isReplay).toBe(true)
  })

  it('refuses a missing or extra member and a malformed revision', () => {
    expect(decodeQuoteSummary(without(quoteSummary(), 'validUntil'))).toBeNull()
    expect(decodeQuoteSummary({ ...quoteSummary(), total: '1' })).toBeNull()
    expect(decodeQuoteSummary(quoteSummary({ revision: 'R1' }))).toBeNull()
    expect(decodeQuoteDetail({ ...quoteDetail(), extra: null }, QUOTE_ID)).toBeNull()
  })

  it('keeps a price paired with its unit, and a withheld price withheld', () => {
    expect(decodeQuoteDetail(quoteDetail({}, { lines: [quoteLine({ priceUnitId: null })] }), QUOTE_ID)).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({}, { lines: [quoteLine({ unitPrice: 42.5 })] }), QUOTE_ID)).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({}, { lines: [quoteLine({ priceDisclosure: 'Withheld' })] }), QUOTE_ID)).toBeNull()
    const withheld = quoteLine({ unitPrice: null, priceUnitId: null, priceDisclosure: 'Withheld' })
    expect(decodeQuoteDetail(quoteDetail({}, { lines: [withheld] }), QUOTE_ID)?.lines[0].priceDisclosure).toBe('Withheld')
  })

  it('never shows a lifecycle without its facts', () => {
    // Open with a loss; Lost without one; Converted naming another order; Converted without convertedDraftId.
    expect(decodeQuoteDetail(quoteDetail({}, { loss: { reason: 'x', lostBy: 'y', lostAtUtc: '2026-10-23T08:00:00Z', reasonId: null, reasonCode: null, reasonLabel: null } }), QUOTE_ID)).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({ state: 'Lost' }, { loss: null }), QUOTE_ID)).toBeNull()
    expect(
      decodeQuoteDetail(
        quoteDetail({ state: 'Converted', convertedDraftId: ORDER_ID }, {
          conversion: { draftId: 'c3c3c3c3-0000-4000-8000-000000000009', convertedBy: 'satis', convertedAtUtc: '2026-10-23T08:00:00Z' },
        }),
        QUOTE_ID,
      ),
    ).toBeNull()
    expect(decodeQuoteSummary(quoteSummary({ state: 'Converted' }))).toBeNull()
    expect(decodeQuoteSummary(quoteSummary({ convertedDraftId: ORDER_ID }))).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({ state: 'Converted', convertedDraftId: ORDER_ID }), QUOTE_ID)?.conversion?.draftId).toBe(ORDER_ID)
    expect(decodeQuoteDetail(quoteDetail({ state: 'Lost' }), QUOTE_ID)?.loss?.reasonLabel).toBe('Fiyat')
  })

  it('refuses repeated or empty request ids and a blank loss reason', () => {
    expect(decodeQuoteDetail(quoteDetail({}, { requestIds: [REQUEST_ID, REQUEST_ID] }), QUOTE_ID)).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({}, { requestIds: ['00000000-0000-0000-0000-000000000000'] }), QUOTE_ID)).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({ state: 'Lost' }, { loss: { reason: '  ', lostBy: 'y', lostAtUtc: '2026-10-23T08:00:00Z', reasonId: null, reasonCode: null, reasonLabel: null } }), QUOTE_ID)).toBeNull()
  })

  it('SAT-D25: a loss is either a list entry with its code and label or a loss recorded before the list — never half', () => {
    const lost = (loss: Record<string, unknown>) => decodeQuoteDetail(quoteDetail({ state: 'Lost' }, { loss }), QUOTE_ID)
    const base = { lostBy: 'y', lostAtUtc: '2026-10-23T08:00:00Z' }
    expect(lost({ ...base, reason: 'eski serbest metin', reasonId: null, reasonCode: null, reasonLabel: null })?.loss?.reason).toBe('eski serbest metin')
    expect(lost({ ...base, reason: null, reasonId: LOSS_REASON_PRICE, reasonCode: 'FIYAT', reasonLabel: 'Fiyat' })?.loss?.reasonId).toBe(LOSS_REASON_PRICE)
    expect(lost({ ...base, reason: null, reasonId: null, reasonCode: null, reasonLabel: null })).toBeNull()
    expect(lost({ ...base, reason: null, reasonId: LOSS_REASON_PRICE, reasonCode: null, reasonLabel: 'Fiyat' })).toBeNull()
    expect(lost({ ...base, reason: 'x', reasonId: LOSS_REASON_PRICE, reasonCode: 'FIYAT' })).toBeNull()
  })

  it('SAT-D44 / SAT-D24: the creator, revisions, approvals and expiry agree with the quote or nothing is shown', () => {
    const decoded = decodeQuoteDetail(quoteDetail({ version: '2', revision: 'Rev.1' }), QUOTE_ID)
    expect(decoded?.revisions.map((record) => [record.revision, record.revisedBy])).toEqual([['Rev.0', 'satis'], ['Rev.1', 'revize-eden']])
    // A revision list that does not end at the current revision, or whose first entry is not the creator.
    expect(decodeQuoteDetail(quoteDetail({ version: '2', revision: 'Rev.1' }, { revisions: [] }), QUOTE_ID)).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({}, { createdBy: 'baskasi' }), QUOTE_ID)).toBeNull()
    // "Approved" without an approval of the current revision, or an approval of a quote that had not expired.
    expect(decodeQuoteDetail(quoteDetail({ validUntil: '2026-10-20' }, { expiry: expiryOf(quoteSummary({ validUntil: '2026-10-20' }), '2026-10-23', true) }), QUOTE_ID)).toBeNull()
    const approval = { revision: 'Rev.0', validUntil: '2026-10-20', businessDay: '2026-10-23', approvedBy: 'mudur', approvedAtUtc: '2026-10-23T09:00:00Z', note: null }
    expect(decodeQuoteDetail(quoteDetail({ validUntil: '2026-10-20' }, {
      expiryApprovals: [approval], expiry: expiryOf(quoteSummary({ validUntil: '2026-10-20' }), '2026-10-23', true),
    }), QUOTE_ID)?.expiry.isApproved).toBe(true)
    expect(decodeQuoteDetail(quoteDetail({ validUntil: '2026-10-20' }, {
      expiryApprovals: [{ ...approval, businessDay: '2026-10-20' }], expiry: expiryOf(quoteSummary({ validUntil: '2026-10-20' }), '2026-10-23', true),
    }), QUOTE_ID)).toBeNull()
    // "Expired" is the server's word on its own day: a contradicting flag is not shown.
    expect(decodeQuoteDetail(quoteDetail({}, { expiry: { businessDay: '2026-10-23', isExpired: true, isApproved: false } }), QUOTE_ID)).toBeNull()
    expect(decodeQuoteDetail(quoteDetail({ validUntil: null }, { revisions: [{ revision: 'Rev.0', validUntil: null, revisedBy: 'satis', revisedAtUtc: '2026-10-22T08:00:00Z' }], expiry: { businessDay: '2099-01-01', isExpired: false, isApproved: false } }), QUOTE_ID)?.expiry.isExpired).toBe(false)
  })

  it('SAT-D25 / SAT-D39: the list and the suggestion reads are strict', () => {
    expect(decodeQuoteLossReasons(lossReasons())?.reasons.map((reason) => reason.requiresExplanation)).toEqual([false, true])
    expect(decodeQuoteLossReasons(lossReasons({ isDefined: false, profileId: null, profileVersion: null, reasons: [] }))?.isDefined).toBe(false)
    expect(decodeQuoteLossReasons(lossReasons({ isDefined: false }))).toBeNull()
    expect(decodeQuoteLossReasons(lossReasons({ reasons: [{ reasonId: LOSS_REASON_PRICE, code: 'A', label: 'A', requiresExplanation: false }, { reasonId: LOSS_REASON_PRICE, code: 'B', label: 'B', requiresExplanation: true }] }))).toBeNull()
    expect(decodeQuoteLossReasons({ ...lossReasons(), extra: 1 })).toBeNull()
    const suggestion = { businessDate: '2026-10-22', validityDays: 30, suggestedValidUntil: '2026-11-21', profileId: LOSS_REASON_PRICE, profileVersion: '1' }
    expect(decodeQuoteValiditySuggestion(suggestion, '2026-10-22')?.suggestedValidUntil).toBe('2026-11-21')
    expect(decodeQuoteValiditySuggestion(suggestion, '2026-10-23')).toBeNull()
    expect(decodeQuoteValiditySuggestion({ ...suggestion, validityDays: null }, '2026-10-22')).toBeNull()
    expect(decodeQuoteValiditySuggestion({ ...suggestion, validityDays: '30' }, '2026-10-22')).toBeNull()
    expect(decodeQuoteValiditySuggestion({ businessDate: '2026-10-22', validityDays: null, suggestedValidUntil: null, profileId: null, profileVersion: null }, '2026-10-22')?.validityDays).toBeNull()
  })
})
