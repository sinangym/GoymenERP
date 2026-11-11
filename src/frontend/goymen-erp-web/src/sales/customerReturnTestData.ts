/**
 * SENTETIK: synthetic API-40 customer return wire bodies (UI-70b S-10). Every member the OpenAPI schema declares is present
 * (the server writes nulls); the amounts are text the tests expect to see verbatim, never computed on the screen.
 */
import { INVOICE_ID, INVOICE_NO, amounts, finalizedDetail } from './invoiceTestData'
import { ACCOUNT, CURRENCY, KG, PARTY, PRODUCT } from './poolFixtures'

type Wire = Record<string, unknown>

export const RETURN_ID = 'f5000000-0000-4000-8000-000000000001'
export const RETURN_2 = 'f5000000-0000-4000-8000-000000000002'
export const INVOICE_LINE_1 = 'f6000000-0000-4000-8000-000000000001'
export const INVOICE_LINE_2 = 'f6000000-0000-4000-8000-000000000002'
export const SHIPMENT_1 = 'f7000000-0000-4000-8000-000000000001'
export const SHIPMENT_2 = 'f7000000-0000-4000-8000-000000000002'
export const RETURN_DOCUMENT_NO = 'MIF-2026-0001'
export const PREPARER = 'company-a-preparer'
export const APPROVER = 'company-a-approver'
const TL = '33333333-3333-3333-3333-333333333334'

export const returnAmounts = (over: Wire = {}): Wire => ({
  netAmount: '200.00',
  vatAmount: '40.00',
  withheldVatAmount: '0.00',
  lineTotalAmount: '240.00',
  derivedPayableAmount: '240.00',
  driftAmount: '0.00',
  ...over,
})

export const returnLine = (over: Wire = {}): Wire => ({
  lineNo: 1,
  originalInvoiceLineId: INVOICE_LINE_1,
  originalLineNo: 1,
  shipmentId: SHIPMENT_1,
  productId: PRODUCT,
  unitId: KG,
  quantity: '2.000',
  netUnitPrice: '100.00',
  vatPercent: '20',
  withholdingShare: null,
  isLastReturn: false,
  needsReview: false,
  amounts: returnAmounts(),
  functionalAmounts: null,
  ...over,
})

const ACTORS: Record<string, Wire> = {
  Draft: { submittedBy: null, submittedAtUtc: null, approvedBy: null, approvedAtUtc: null, finalizedBy: null, finalizedAtUtc: null },
  PendingApproval: { submittedBy: PREPARER, submittedAtUtc: '2026-11-05T09:10:00Z', approvedBy: null, approvedAtUtc: null, finalizedBy: null, finalizedAtUtc: null },
  Approved: { submittedBy: PREPARER, submittedAtUtc: '2026-11-05T09:10:00Z', approvedBy: APPROVER, approvedAtUtc: '2026-11-05T10:00:00Z', finalizedBy: null, finalizedAtUtc: null },
  Finalized: { submittedBy: PREPARER, submittedAtUtc: '2026-11-05T09:10:00Z', approvedBy: APPROVER, approvedAtUtc: '2026-11-05T10:00:00Z', finalizedBy: APPROVER, finalizedAtUtc: '2026-11-05T11:00:00Z' },
}

const VERSION: Record<string, string> = { Draft: '1', PendingApproval: '2', Approved: '3', Finalized: '4' }

/** A TL return of the synthetic invoice in `state`, with exactly the actor facts that state has passed through. */
export function returnView(state = 'Draft', over: Wire = {}): Wire {
  return {
    id: RETURN_ID,
    state,
    version: VERSION[state],
    originalInvoiceId: INVOICE_ID,
    originalDocumentNo: INVOICE_NO,
    shipmentId: null,
    customerDocumentNo: RETURN_DOCUMENT_NO,
    customerDocumentDate: '2026-11-05',
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    currencyId: CURRENCY,
    amountScale: 2,
    rate: null,
    functionalCurrencyId: null,
    functionalScale: null,
    totals: returnAmounts(),
    functionalTotals: null,
    needsReview: false,
    createdBy: PREPARER,
    createdAtUtc: '2026-11-05T09:00:00Z',
    ...ACTORS[state],
    updatedAtUtc: '2026-11-05T09:00:00Z',
    lines: [returnLine()],
    ...over,
  }
}

/** A foreign-currency return: the TL side complete on the header and on every line. */
export const foreignReturnView = (over: Wire = {}): Wire =>
  returnView('Draft', {
    rate: '34.5000',
    functionalCurrencyId: TL,
    functionalScale: 2,
    functionalTotals: returnAmounts({ lineTotalAmount: '8280.00' }),
    lines: [returnLine({ functionalAmounts: returnAmounts({ lineTotalAmount: '8280.00' }) })],
    ...over,
  })

export const returnSummary = (over: Wire = {}): Wire => ({
  id: RETURN_ID,
  state: 'Draft',
  version: '1',
  originalInvoiceId: INVOICE_ID,
  originalDocumentNo: INVOICE_NO,
  customerDocumentNo: RETURN_DOCUMENT_NO,
  customerDocumentDate: '2026-11-05',
  partyId: PARTY,
  currencyId: CURRENCY,
  lineTotalAmount: '240.00',
  isShipmentLinked: false,
  needsReview: false,
  lineCount: 1,
  createdAtUtc: '2026-11-05T09:00:00Z',
  ...over,
})

/** An invoice line of the synthetic invoice as API-36 writes it (shipment, dispatch note, price basis). */
export const invoiceLine = (over: Wire = {}): Wire => ({
  lineId: INVOICE_LINE_1,
  lineNo: 1,
  shipmentId: SHIPMENT_1,
  dispatchNoteId: 'f8000000-0000-4000-8000-000000000001',
  dispatchNoteNumber: 'IRS-2026-0001',
  noteLineNo: 1,
  shipmentDate: '2026-10-30',
  salesOrderId: 'f9000000-0000-4000-8000-000000000001',
  salesOrderLineId: 'f9100000-0000-4000-8000-000000000001',
  priceBasisRevision: '1',
  productId: PRODUCT,
  unitId: KG,
  quantity: '10.000',
  netUnitPrice: '100.00',
  vatPercent: '20',
  withholdingShare: null,
  amounts: amounts(),
  functional: null,
  ...over,
})

/** The finalized invoice with two lines on two shipments. */
export const finalizedInvoiceWithLines = (): Wire =>
  finalizedDetail(undefined, {
    lines: [
      invoiceLine(),
      invoiceLine({
        lineId: INVOICE_LINE_2,
        lineNo: 2,
        shipmentId: SHIPMENT_2,
        dispatchNoteId: 'f8000000-0000-4000-8000-000000000002',
        dispatchNoteNumber: 'IRS-2026-0002',
        salesOrderLineId: 'f9100000-0000-4000-8000-000000000002',
      }),
    ],
  })
