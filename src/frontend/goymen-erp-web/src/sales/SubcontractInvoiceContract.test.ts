/**
 * F2 subcontract service invoice decoder, SENTETIK consumer tests (NOT_ENV): exact members, fail-closed `null` for any shape
 * outside the contract, every kilogram and amount kept as the server's text.
 */
import { describe, expect, it } from 'vitest'
import {
  SUBCONTRACT_INVOICE_DETAIL_FIELDS,
  SUBCONTRACT_INVOICE_LINE_FIELDS,
  SUBCONTRACT_INVOICE_REASON,
  SUBCONTRACT_SERVICE_INVOICES_PATH,
  decodeSubcontractInvoiceDetail,
  subcontractServiceInvoicePath,
} from './SubcontractInvoiceContract'
import { SUBCONTRACT_INVOICE_REASON_TEXT, subcontractInvoiceFieldText, subcontractInvoiceReasonText } from './SubcontractInvoiceText'
import { invoiceNotReadyReasonText } from './invoiceDraftText'
import {
  SUBCONTRACT_INVOICE_ID,
  subcontractInvoiceDetail,
  subcontractInvoiceLine,
} from './subcontractInvoiceTestData'

describe('F2 subcontract service invoice decoder', () => {
  it('decodes the detail verbatim: the net kg, the billed kg and the amounts stay the server text', () => {
    const detail = decodeSubcontractInvoiceDetail(subcontractInvoiceDetail(), SUBCONTRACT_INVOICE_ID)
    expect(detail).not.toBeNull()
    expect(detail?.netKg).toBe('1234.560')
    expect(detail?.lines[0].quantity).toBe('1234.560')
    expect(detail?.lines[0].netUnitPrice).toBe('0.81')
    expect(detail?.lines[0].priceBasisRevision).toBe('Rev.2')
    expect(detail?.totals.payableAmount).toBe('1180.40')
    expect(detail?.state).toBe('Draft')
  })

  it('pins the exact member lists of the detail and the line', () => {
    expect(Object.keys(subcontractInvoiceDetail())).toEqual([...SUBCONTRACT_INVOICE_DETAIL_FIELDS])
    expect(Object.keys(subcontractInvoiceLine())).toEqual([...SUBCONTRACT_INVOICE_LINE_FIELDS])
  })

  it('accepts a create answer by the id it names, and refuses another invoice for a read', () => {
    expect(decodeSubcontractInvoiceDetail(subcontractInvoiceDetail(), null)).not.toBeNull()
    expect(decodeSubcontractInvoiceDetail(subcontractInvoiceDetail(), SUBCONTRACT_INVOICE_ID.toUpperCase())).not.toBeNull()
    expect(decodeSubcontractInvoiceDetail(subcontractInvoiceDetail(), 'f5000000-0000-4000-8000-000000000009')).toBeNull()
  })

  it.each([
    ['an unknown member', { extra: 1 }],
    ['a state other than Draft', { state: 'Approved' }],
    ['a zero net kg', { netKg: '0.000' }],
    ['a negative net kg', { netKg: '-1.5' }],
    ['a numeric net kg', { netKg: 1234.56 }],
    ['a zero weighing revision', { weighingRevision: 0 }],
    ['no lines', { lines: [] }],
    ['a bad version', { version: 'v1' }],
    ['a bad business date', { businessDate: '14.11.2026' }],
    ['a bad instant', { createdAtUtc: 'yesterday' }],
    ['a missing total', { totals: { netAmount: '1' } }],
    ['a non-boolean replay flag', { isReplay: 'false' }],
  ])('refuses %s', (_label, over) => {
    expect(decodeSubcontractInvoiceDetail(subcontractInvoiceDetail(over), null)).toBeNull()
  })

  it('refuses a missing member', () => {
    const body = subcontractInvoiceDetail()
    delete body.weighingId
    expect(decodeSubcontractInvoiceDetail(body, null)).toBeNull()
  })

  it.each([
    ['an unknown line member', { extra: true }],
    ['a zero billed kg', { quantity: '0' }],
    ['a price basis that is not a revision text', { priceBasisRevision: '2' }],
    ['a zero line number', { lineNo: 0 }],
    ['a non-decimal withholding share', { withholdingShare: '2/10' }],
  ])('refuses %s', (_label, over) => {
    expect(decodeSubcontractInvoiceDetail(subcontractInvoiceDetail({ lines: [subcontractInvoiceLine(over)] }), null)).toBeNull()
  })

  it('keeps a withholding share as text', () => {
    const detail = decodeSubcontractInvoiceDetail(
      subcontractInvoiceDetail({ lines: [subcontractInvoiceLine({ withholdingShare: '0.2' })] }), null)
    expect(detail?.lines[0].withholdingShare).toBe('0.2')
  })

  it('builds the routes and encodes the id', () => {
    expect(SUBCONTRACT_SERVICE_INVOICES_PATH).toBe('/sales/subcontract-service-invoices')
    expect(subcontractServiceInvoicePath('a/b')).toBe('/sales/subcontract-service-invoices/a%2Fb')
  })

  it('has a Turkish text for every F2 reason, falls back to the API-32 texts, and shows an unknown code as itself', () => {
    for (const code of Object.values(SUBCONTRACT_INVOICE_REASON)) {
      expect(SUBCONTRACT_INVOICE_REASON_TEXT[code]).toBeTruthy()
      expect(subcontractInvoiceReasonText(code)).toBe(SUBCONTRACT_INVOICE_REASON_TEXT[code])
    }
    expect(subcontractInvoiceReasonText('rate_unresolved')).toBe(invoiceNotReadyReasonText('rate_unresolved'))
    expect(subcontractInvoiceReasonText('some_future_code')).toBe('some_future_code')
    expect(subcontractInvoiceFieldText('weighingId')).toBe('Teslim tartısı')
    expect(subcontractInvoiceFieldText('lines[3]')).toBe('lines[3]')
  })
})
