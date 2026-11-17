import { describe, expect, it } from 'vitest'
import { decodeIncomingInvoice, decodeInvoiceCommand, decodeInvoicePage, decodeInvoiceReceipts } from './incomingInvoiceContract'
import { incomingInvoiceFixture, invoiceReceiptFixture, invoiceTestId as id } from './incomingInvoiceFixtures.testSupport'
describe('Incoming invoice exact read contract (synthetic, NOT_ENV)', () => {
  it('preserves server decimal money and scaled quantity without arithmetic', () => {
    const invoice = incomingInvoiceFixture()
    expect(decodeIncomingInvoice(invoice, invoice.companyId, invoice.id)).toEqual(invoice)
    expect(decodeIncomingInvoice({ ...invoice, amounts: { ...invoice.amounts, net: '10.010' } }, invoice.companyId)).toBeNull()
    expect(decodeIncomingInvoice({ ...invoice, amounts: { ...invoice.amounts, net: 10.01 } }, invoice.companyId)).toBeNull()
  })
  it('rejects foreign identity, unknown status, duplicate lines and a foreign variance', () => {
    const invoice = incomingInvoiceFixture()
    expect(decodeIncomingInvoice(invoice, id(99))).toBeNull()
    expect(decodeIncomingInvoice(invoice, invoice.companyId, id(99))).toBeNull()
    expect(decodeIncomingInvoice({ ...invoice, status: 'Success' }, invoice.companyId)).toBeNull()
    expect(decodeIncomingInvoice({ ...invoice, lines: [invoice.lines[0], invoice.lines[0]] }, invoice.companyId)).toBeNull()
    expect(decodeIncomingInvoice({ ...invoice, variances: [{ invoiceLineId: id(99), kind: 'Price', expected: '1', actual: '2' }] }, invoice.companyId)).toBeNull()
  })
  it('binds candidate quantity to the exact invoice revision, line, product and unit', () => {
    const invoice = incomingInvoiceFixture(), page = invoiceReceiptFixture(), line = invoice.lines[0]
    expect(decodeInvoiceReceipts(page, invoice, line, 0)).toEqual(page)
    expect(decodeInvoiceReceipts({ ...page, invoiceVersion: '2' }, invoice, line, 0)).toBeNull()
    expect(decodeInvoiceReceipts({ ...page, items: [{ ...page.items[0], productId: id(99) }] }, invoice, line, 0)).toBeNull()
    expect(decodeInvoiceReceipts({ ...page, items: [{ ...page.items[0], availableQuantity: '0' }] }, invoice, line, 0)).toBeNull()
    expect(decodeInvoiceReceipts({ ...page, items: [page.items[0], page.items[0]] }, invoice, line, 0)).toBeNull()
  })
  it('rejects duplicate invoice pages and mismatched replay envelopes', () => {
    const invoice = incomingInvoiceFixture(), answer = { invoice, isReplay: true, originalCorrelationId: 'synthetic-correlation', financeOutcome: null }
    expect(decodeInvoicePage({ pageIndex: 0, hasNext: false, items: [invoice, invoice] }, invoice.companyId, 0)).toBeNull()
    expect(decodeInvoiceCommand(answer, invoice.companyId, invoice.id, true)).toEqual(answer)
    expect(decodeInvoiceCommand(answer, invoice.companyId, invoice.id, false)).toBeNull()
  })
})
