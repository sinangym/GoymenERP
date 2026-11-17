import type { IncomingInvoice, InvoiceReceiptPage } from './incomingInvoiceContract'
export const invoiceTestId = (index: number) => `10000000-0000-4000-8000-${String(index).padStart(12, '0')}`
export function incomingInvoiceFixture(): IncomingInvoice {
  const amounts = { net: '10.01', vat: '2.00', withholding: '0.00', payable: '12.01' }
  return { id: invoiceTestId(1), companyId: invoiceTestId(2), version: '1', status: 'Recorded', supplierPartyId: invoiceTestId(3),
    companyAccountId: invoiceTestId(4), currencyId: invoiceTestId(5), functionalCurrencyId: invoiceTestId(5), moneyScale: 2, functionalScale: 2,
    officialRate: null, officialNumber: 'SENTETIK-2026-0001', ettn: invoiceTestId(6), invoiceDate: '2026-11-17', documentId: invoiceTestId(7),
    documentVersion: '1', amounts, functionalAmounts: null, lines: [{ id: invoiceTestId(8), lineNo: 1, productId: invoiceTestId(9),
      unitId: invoiceTestId(10), quantity: '2.000000', itemType: 'Material', linePurpose: 'Inventory', vatPercent: '20', amounts, functionalAmounts: null }],
    variances: [], varianceDecision: null, finalizedVersion: null }
}
export function invoiceReceiptFixture(): InvoiceReceiptPage {
  const invoice = incomingInvoiceFixture()
  return { invoiceId: invoice.id, invoiceLineId: invoice.lines[0].id, invoiceVersion: invoice.version, pageIndex: 0, hasNext: false,
    items: [{ purchaseOrderId: invoiceTestId(11), purchaseOrderNumber: 'SENTETIK-PO-01', purchaseOrderLineId: invoiceTestId(12),
      orderLineNo: 1, acceptanceId: invoiceTestId(13), acceptedAtUtc: '2026-11-17T10:00:00+00:00', productId: invoice.lines[0].productId,
      unitId: invoice.lines[0].unitId, availableQuantity: '2' }] }
}
