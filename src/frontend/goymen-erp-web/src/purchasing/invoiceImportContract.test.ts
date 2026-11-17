import { describe, expect, it } from 'vitest'
import { decodeImportPreview, decodeImportReservation } from './invoiceImportContract'
import { importPreviewFixture, importRequestFixture, importReservationFixture, importTestId as id } from './invoiceImportFixtures.testSupport'
describe('Purchase invoice source decoder (synthetic, NOT_ENV)', () => {
  const decode = (value: unknown) => decodeImportPreview(value, id(2), id(1), importRequestFixture())
  it('preserves decimal strings, leading-zero codes and separate exchange-rate purposes', () => {
    const invoice = decode(importPreviewFixture())!.invoice!
    expect(invoice.lines[0].quantity).toBe('0.123456789123456789')
    expect(invoice.lines[0].sellerCode).toBe('0007')
    expect(invoice.totals.LineExtensionAmount.value).toBe('10.123456789123456789')
    expect(invoice.rates[0].purpose).toBe('TaxExchangeRate')
  })
  it.each([
    { companyId: id(99) }, { importId: id(99) }, { documentId: id(99) }, { documentVersion: 3 }, { documentSha256: 'b'.repeat(64) },
    { version: '2' }, { invented: true }, { invoice: null }, { issues: [{ field: 'Currency', code: 'missing' }] },
  ])('rejects an unbound or contradictory answer %j', delta => expect(decode({ ...importPreviewFixture(), ...delta })).toBeNull())
  it('accepts a source refusal without presenting a partial invoice', () => {
    const refused = { ...importPreviewFixture(), invoice: null, issues: [{ field: 'file', code: 'ubl_invoice_required' }] }
    expect(decode(refused)).toEqual(refused)
  })
  it('rejects numeric money, invalid dates, duplicate line identifiers and extra nested financial fields', () => {
    const source = importPreviewFixture()
    for (const invoice of [
      { ...source.invoice!, invoiceDate: '2026-02-31' },
      { ...source.invoice!, lines: [source.invoice!.lines[0], source.invoice!.lines[0]] },
      { ...source.invoice!, totals: { PayableAmount: { value: 11.12, currency: 'USD' } } },
      { ...source.invoice!, taxes: [{ ...source.invoice!.taxes[0], extraAmount: '1' }] },
    ]) expect(decode({ ...source, invoice })).toBeNull()
  })
  it('requires replay metadata to match the actual response header and company', () => {
    const source = { ...importReservationFixture(), isReplay: true }
    expect(decodeImportReservation(source, id(2), true, 'synthetic-correlation')).toEqual(source)
    expect(decodeImportReservation(source, id(2), true, null)).toBeNull()
    expect(decodeImportReservation(source, id(2), false, 'synthetic-correlation')).toBeNull()
    expect(decodeImportReservation(source, id(99), true, 'synthetic-correlation')).toBeNull()
  })
})
