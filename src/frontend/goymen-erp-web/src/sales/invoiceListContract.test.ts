import { describe, expect, it } from 'vitest'
import { decodeSalesInvoicePage, salesInvoicesListPath } from './invoiceListContract'

const ID = 'f1000000-0000-4000-8000-000000000001'
const ID2 = 'f1000000-0000-4000-8000-000000000002'
const REF = '11111111-1111-4111-8111-111111111111'

const summary = (over: Record<string, unknown> = {}) => ({
  id: ID,
  documentNo: 'SF-2026-0007',
  state: 'Draft',
  version: '1',
  businessDate: '2026-11-01',
  partyId: REF,
  companyAccountId: REF,
  currencyId: REF,
  amountDisclosure: 'Visible',
  payableAmount: '10.00',
  functionalPayableAmount: null,
  lineCount: 0,
  createdAtUtc: '2026-11-01T08:00:00.123Z',
  ...over,
})

describe('API-32 invoice list contract', () => {
  it('builds the list path with the page size and the cursor only', () => {
    expect(salesInvoicesListPath(null)).toBe('/sales/invoices?pageSize=50')
    expect(salesInvoicesListPath('a b')).toBe('/sales/invoices?pageSize=50&cursor=a+b')
  })

  it('decodes an exact page, keeping amounts as text', () => {
    const decoded = decodeSalesInvoicePage({ items: [summary({ functionalPayableAmount: '350.10' })], nextCursor: 'next' })
    expect(decoded?.items[0].amountDisclosure).toBe('Visible')
    expect(decoded?.items[0].payableAmount).toBe('10.00')
    expect(decoded?.items[0].functionalPayableAmount).toBe('350.10')
    expect(decoded?.nextCursor).toBe('next')
  })

  it.each([
    ['null', null],
    ['a missing nextCursor', { items: [] }],
    ['an empty cursor', { items: [], nextCursor: '' }],
    ['an extra page member', { items: [], nextCursor: null, total: 0 }],
    ['a missing member', { items: [{ ...summary(), lineCount: undefined }], nextCursor: null }],
    ['an unknown state', { items: [summary({ state: 'Paid' })], nextCursor: null }],
    ['a numeric version', { items: [summary({ version: 1 })], nextCursor: null }],
    ['a string line count', { items: [summary({ lineCount: '2' })], nextCursor: null }],
    ['a fractional line count', { items: [summary({ lineCount: 1.5 })], nextCursor: null }],
    ['a signed amount', { items: [summary({ payableAmount: '-1.00' })], nextCursor: null }],
    ['an all-zero id', { items: [summary({ id: '00000000-0000-0000-0000-000000000000' })], nextCursor: null }],
    ['a date-time business date', { items: [summary({ businessDate: '2026-11-01T00:00:00Z' })], nextCursor: null }],
    ['a blank document number', { items: [summary({ documentNo: ' ' })], nextCursor: null }],
    ['a missing amountDisclosure', { items: [{ ...summary(), amountDisclosure: undefined }], nextCursor: null }],
    ['an unknown disclosure', { items: [summary({ amountDisclosure: 'Hidden' })], nextCursor: null }],
    ['a visible row without its payable amount', { items: [summary({ payableAmount: null })], nextCursor: null }],
    ['a withheld row carrying a payable amount', { items: [summary({ amountDisclosure: 'Withheld' })], nextCursor: null }],
    ['a withheld row showing zero', { items: [summary({ amountDisclosure: 'Withheld', payableAmount: '0' })], nextCursor: null }],
    ['a withheld row carrying a TL amount', {
      items: [summary({ amountDisclosure: 'Withheld', payableAmount: null, functionalPayableAmount: '0.00' })], nextCursor: null }],
    ['a withheld row with an empty amount', { items: [summary({ amountDisclosure: 'Withheld', payableAmount: '' })], nextCursor: null }],
    ['a repeated id', { items: [summary(), summary()], nextCursor: null }],
  ])('refuses %s', (_label, body) => {
    expect(decodeSalesInvoicePage(JSON.parse(JSON.stringify(body)))).toBeNull()
  })

  it('decodes a withheld row (SAT-D238) with both amounts null, never a zero', () => {
    const decoded = decodeSalesInvoicePage({
      items: [summary({ amountDisclosure: 'Withheld', payableAmount: null, functionalPayableAmount: null })],
      nextCursor: null,
    })
    expect(decoded?.items[0]).toMatchObject({ amountDisclosure: 'Withheld', payableAmount: null, functionalPayableAmount: null })
  })

  it('accepts two distinct invoices in the server order', () => {
    const decoded = decodeSalesInvoicePage({ items: [summary({ id: ID2 }), summary()], nextCursor: null })
    expect(decoded?.items.map((item) => item.id)).toEqual([ID2, ID])
  })
})
