import { describe, expect, it } from 'vitest'
import { decodeIncomingInvoice, decodeInvoiceCommand, decodeInvoicePage, decodeInvoiceReceipts, decodeInvoiceAdditionalCosts } from './incomingInvoiceContract'
import { incomingInvoiceFixture, invoiceReceiptFixture, invoiceTestId as id } from './incomingInvoiceFixtures.testSupport'
describe('Incoming invoice exact read contract (synthetic, NOT_ENV)', () => {
  it('binds monetary expense choices to the exact invoice revision, currency, type and VAT without a fictitious acceptance', () => {
    const existing = incomingInvoiceFixture(), empty = '00000000-0000-0000-0000-000000000000'
    const line = { ...existing.lines[0], productId: empty, itemType: 'Expense', linePurpose: 'expense' }
    const invoice = { ...existing, lines: [line] }
    const page = { invoiceId: invoice.id, invoiceLineId: line.id, invoiceVersion: invoice.version, currencyId: invoice.currencyId,
      pageIndex: 0, hasNext: false, items: [{ costId: id(90), purchaseOrderId: id(91), purchaseOrderNumber: 'SENTETIK-EXP-1',
        description: 'SENTETIK nakliye', expenseTypeId: null, expenseTypeName: null, businessDate: '2026-11-17',
        netAmount: '10000', availableNetAmount: '4000', vatPercent: line.vatPercent }] }
    expect(decodeIncomingInvoice(invoice, invoice.companyId)).toEqual(invoice)
    expect(decodeInvoiceAdditionalCosts(page, invoice, line, 0)).toEqual(page)
    expect(decodeInvoiceAdditionalCosts({ ...page, invoiceVersion: '99' }, invoice, line, 0)).toBeNull()
    expect(decodeInvoiceAdditionalCosts({ ...page, currencyId: id(92) }, invoice, line, 0)).toBeNull()
    expect(decodeInvoiceAdditionalCosts({ ...page, items: [{ ...page.items[0], acceptanceId: id(93) }] }, invoice, line, 0)).toBeNull()
    expect(decodeInvoiceAdditionalCosts({ ...page, items: [{ ...page.items[0], availableNetAmount: '0' }] }, invoice, line, 0)).toBeNull()
    expect(decodeInvoiceAdditionalCosts({ ...page, items: [{ ...page.items[0], expenseTypeId: id(94), expenseTypeName: 'SENTETIK' }] }, invoice, line, 0)).toBeNull()
  })
  it('binds a typed service acceptance without disguising the service type as a Product', () => {
    const existing = incomingInvoiceFixture(), empty = '00000000-0000-0000-0000-000000000000'
    const line = { ...existing.lines[0], productId: empty, itemType: 'Service', linePurpose: 'service' as const, serviceTypeId: id(90) }
    const invoice = { ...existing, lines: [line] }, page = invoiceReceiptFixture()
    const candidates = { ...page, items: [{ ...page.items[0], productId: empty, serviceTypeId: id(90) }] }
    expect(decodeIncomingInvoice(invoice, invoice.companyId)).toEqual(invoice)
    expect(decodeInvoiceReceipts(candidates, invoice, line, 0)).toEqual(candidates)
    expect(decodeIncomingInvoice({ ...invoice, lines: [{ ...line, productId: id(91) }] }, invoice.companyId)).toBeNull()
    expect(decodeInvoiceReceipts({ ...candidates, items: [{ ...candidates.items[0], serviceTypeId: id(92) }] }, invoice, line, 0)).toBeNull()
  })
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
