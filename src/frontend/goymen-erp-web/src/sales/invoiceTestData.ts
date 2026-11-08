/**
 * SENTETIK: synthetic API-09 / API-36 wire bodies for the invoice finalization tests. Every member the OpenAPI schema declares
 * is present (the server writes nulls); the amounts are text the tests expect to see verbatim, never computed.
 */

export const INVOICE_ID = 'f1000000-0000-4000-8000-000000000001'
export const OTHER_INVOICE_ID = 'f1000000-0000-4000-8000-000000000002'
export const INVOICE_NO = 'SF-2026-0007'
const PARTY = '11111111-1111-1111-1111-111111111111'
const ACCOUNT = '55555555-5555-5555-5555-555555555555'
const CURRENCY = '33333333-3333-3333-3333-333333333333'
export const OPEN_ITEM_1 = 'f2000000-0000-4000-8000-000000000001'
export const OPEN_ITEM_2 = 'f2000000-0000-4000-8000-000000000002'

type Wire = Record<string, unknown>

export const amounts = (payable = '1180.40'): Wire => ({
  netAmount: '1000.34',
  vatAmount: '200.07',
  withheldVatAmount: '20.01',
  grossAmount: '1200.41',
  payableAmount: payable,
})

export const plannedInstalments = (): Wire[] => [
  { sequenceNo: 1, dueDate: '2026-12-01', sharePercent: '33.3333', amount: '393.47' },
  { sequenceNo: 2, dueDate: '2027-01-01', sharePercent: '66.6667', amount: '786.93' },
]

export const finalInstalments = (): Wire[] => [
  { sequenceNo: 1, dueDate: '2026-12-01', amount: '393.47', functionalAmount: null, openItemId: OPEN_ITEM_1, installmentId: 'f3000000-0000-4000-8000-000000000001' },
  { sequenceNo: 2, dueDate: '2027-01-01', amount: '786.93', functionalAmount: '25210.77', openItemId: OPEN_ITEM_2, installmentId: 'f3000000-0000-4000-8000-000000000002' },
]

export const matchOf = (over: Wire = {}): Wire => ({
  officialInvoiceNo: 'GIB2026000000123',
  ettn: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
  invoiceAmount: '1180.40',
  officialAmount: '1180.40',
  difference: '0.00',
  tolerance: '0.05',
  matchedBy: 'company-a-user',
  matchedAtUtc: '2026-11-02T10:00:00Z',
  differenceApprovedBy: null,
  differenceReason: null,
  ...over,
})

/** A finalized ledger: by default not yet sent to Finance (no attempt). */
export const finalizationOf = (over: Wire = {}): Wire => ({
  admissionId: 'f4000000-0000-4000-8000-000000000001',
  finalizedVersion: '5',
  finalizedBy: 'company-a-user',
  finalizedAtUtc: '2026-11-03T09:00:00Z',
  financeStatus: 'waiting',
  admittedAtUtc: null,
  attemptCount: 0,
  lastAttemptAtUtc: null,
  lastOutcome: null,
  accountingStatus: 'no_source',
  instalments: finalInstalments(),
  ...over,
})

export const sentWaiting = (outcome = 'unavailable'): Wire =>
  finalizationOf({ attemptCount: 2, lastAttemptAtUtc: '2026-11-03T09:05:00Z', lastOutcome: outcome })
export const admitted = (): Wire =>
  finalizationOf({ financeStatus: 'admitted', admittedAtUtc: '2026-11-03T09:06:00Z', attemptCount: 1, lastAttemptAtUtc: '2026-11-03T09:06:00Z', lastOutcome: 'admitted' })

/** An approved, matched invoice that is not finalized yet. */
export function invoiceDetail(over: Wire = {}): Wire {
  return {
    id: INVOICE_ID,
    documentNo: INVOICE_NO,
    state: 'Approved',
    version: '4',
    businessDate: '2026-11-01',
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    currencyId: CURRENCY,
    amountScale: 2,
    exchangeRate: null,
    totals: amounts(),
    functionalTotals: null,
    vatGroups: [{ vatPercent: '20', lineCount: 1, amounts: amounts(), functional: null }],
    instalments: plannedInstalments(),
    lines: [],
    createdBy: 'company-a-user',
    createdAtUtc: '2026-11-01T08:00:00Z',
    decision: null,
    paymentAllocationWarning: null,
    isReplay: false,
    match: matchOf(),
    finalization: null,
    ...over,
  }
}

export const finalizedDetail = (finalization: Wire = finalizationOf(), over: Wire = {}): Wire =>
  invoiceDetail({ state: 'Finalized', version: '5', finalization, ...over })

export const draftInvoiceItem = (over: Wire = {}): Wire => ({
  id: INVOICE_ID,
  documentNo: INVOICE_NO,
  state: 'Approved',
  businessDate: '2026-11-01',
  orderLineCount: 3,
  invoiceLineCount: 2,
  holdsShares: true,
  createdAtUtc: '2026-11-01T08:00:00Z',
  ...over,
})
