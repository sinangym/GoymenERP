import { describe, expect, it } from 'vitest'
import { decodeImportMappingReferences, decodeImportRecord, decodeImportSupplier, decodeImportProducts } from './invoiceImportAdmissionContract'
import { admissionReferences, admissionSupplier, admissionProducts, admissionSnapshot, admissionResult } from './invoiceImportAdmissionFixtures.testSupport'
import { importTestId as id } from './invoiceImportFixtures.testSupport'
describe('import admission source contracts', () => {
  it('requires exact company, immutable document and profile source while preserving explicit unavailable catalogs', () => {
    const source = admissionSnapshot().request.source, value = admissionReferences()
    expect(decodeImportMappingReferences(value, id(2), id(1), source, '2026-11-17')).toEqual(value)
    for (const raw of [{ ...value, companyId: id(99) }, { ...value, documentVersion: 3 }, { ...value, documentSha256: 'b'.repeat(64) },
      { ...value, invented: true }, { ...value, units: [{ ...value.units[0], profileVersion: 3 }] }])
      expect(decodeImportMappingReferences(raw, id(2), id(1), source, '2026-11-17')).toBeNull()
  })
  it('never manufactures supplier versions or a product from another source', () => {
    expect(decodeImportSupplier(admissionSupplier(), id(2), id(1), id(14), id(15))).not.toBeNull()
    expect(decodeImportSupplier({ ...admissionSupplier(), partyVersion: 4 }, id(2), id(1), id(14), id(15))).toBeNull()
    expect(decodeImportProducts({ ...admissionProducts(), importId: id(99) }, id(2), id(1))).toBeNull()
  })
  it('binds a stored record to every selected identity and source, never a mismatching successful HTTP result', () => {
    const snapshot = admissionSnapshot(), result = admissionResult()
    expect(decodeImportRecord(result, id(2), id(1), snapshot, false)).not.toBeNull()
    expect(decodeImportRecord({ ...result, invoice: { ...result.invoice, supplierPartyId: id(99) } }, id(2), id(1), snapshot, false)).toBeNull()
    expect(decodeImportRecord({ ...result, invoice: { ...result.invoice, lines: [{ ...result.invoice.lines[0], unitId: id(99) }] } }, id(2), id(1), snapshot, false)).toBeNull()
  })
})
