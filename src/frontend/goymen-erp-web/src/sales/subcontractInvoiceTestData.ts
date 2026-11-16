/**
 * SENTETIK: synthetic F2 wire bodies (`SalesSubcontractServiceInvoiceDetailView`) for the subcontract service invoice tests.
 * Every declared member is present (the server writes nulls); the kilograms and amounts are text the tests expect to see
 * verbatim, never computed here.
 */
import { amounts } from './invoiceTestData'

type Wire = Record<string, unknown>

export const SUBCONTRACT_INVOICE_ID = 'f5000000-0000-4000-8000-000000000001'
export const SUBCONTRACT_INVOICE_NO = 'SFH-2026-0003'
export const SUBCONTRACT_ORDER_ID = 'c3c3c3c3-0000-4000-8000-0000000000f1'
export const SUBCONTRACT_ORDER_LINE_ID = 'd4d4d4d4-0000-4000-8000-0000000000f1'
export const LOADING_INSTRUCTION_ID = 'e6000000-0000-4000-8000-000000000001'
export const WEIGHING_ID = 'e7000000-0000-4000-8000-000000000001'

export function subcontractInvoiceLine(over: Wire = {}): Wire {
  return {
    lineId: 'f5100000-0000-4000-8000-000000000001',
    lineNo: 1,
    salesOrderLineId: SUBCONTRACT_ORDER_LINE_ID,
    priceBasisRevision: 'Rev.2',
    productId: '22222222-2222-2222-2222-222222222222',
    unitId: '44444444-4444-4444-4444-444444444444',
    quantity: '1234.560',
    netUnitPrice: '0.81',
    vatPercent: '20',
    withholdingShare: null,
    amounts: amounts(),
    ...over,
  }
}

export function subcontractInvoiceDetail(over: Wire = {}): Wire {
  return {
    id: SUBCONTRACT_INVOICE_ID,
    documentNo: SUBCONTRACT_INVOICE_NO,
    state: 'Draft',
    version: '1',
    businessDate: '2026-11-14',
    partyId: '11111111-1111-1111-1111-111111111111',
    companyAccountId: '55555555-5555-5555-5555-555555555555',
    currencyId: '33333333-3333-3333-3333-333333333333',
    amountScale: 2,
    salesOrderId: SUBCONTRACT_ORDER_ID,
    loadingInstructionId: LOADING_INSTRUCTION_ID,
    weighingId: WEIGHING_ID,
    weighingRevision: 1,
    netKg: '1234.560',
    totals: amounts(),
    lines: [subcontractInvoiceLine()],
    createdBy: 'actor-a',
    createdAtUtc: '2026-11-14T09:30:00Z',
    isReplay: false,
    ...over,
  }
}
