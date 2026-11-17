import { describe, expect, it } from 'vitest'
import { decodeBankCatalog, decodeBankInstitutions, decodeCollateralRatePreview } from './collateralPolicyContract'
const company = '11111111-1111-1111-1111-111111111111'; const id = '22222222-2222-2222-2222-222222222222'
const entry = { bankInstitutionId: id, name: 'Sentetik banka', eftCode: '0007', active: true, sourceVersion: 'settings.bank-institutions:synthetic:1' }
describe('KIM-S19 bank and K6 source contracts', () => {
  it('distinguishes a missing bank source from an explicitly published empty catalog and preserves leading EFT zeros', () => {
    const missing = { outcome: 'Unavailable', companyId: company, entries: [], sourceVersion: null, reason: 'not_configured' }
    expect(decodeBankCatalog(missing, company)?.outcome).toBe('Unavailable')
    expect(decodeBankCatalog({ ...missing, entries: [entry] }, company)).toBeNull()
    const available = { outcome: 'Available', companyId: company, entries: [entry], sourceVersion: entry.sourceVersion, reason: null }
    expect(decodeBankCatalog(available, company)?.entries[0].eftCode).toBe('0007')
    expect(decodeBankCatalog({ ...available, entries: [] }, company)?.outcome).toBe('Available')
    expect(decodeBankCatalog({ ...available, entries: [entry, entry] }, company)).toBeNull()
    expect(decodeBankCatalog({ ...available, entries: [{ ...entry, sourceVersion: 'foreign-proof' }] }, company)).toBeNull()
  })
  it('never fabricates a bank from an account or leaks another company source', () => {
    expect(decodeBankInstitutions({ companyId: company, businessDate: '2026-11-18', versions: [] }, company)?.versions).toEqual([])
    expect(decodeBankCatalog({ outcome: 'Available', companyId: id, entries: [entry], sourceVersion: entry.sourceVersion, reason: null }, company)).toBeNull()
    expect(decodeBankCatalog({ outcome: 'Available', companyId: company, entries: [{ ...entry, accountId: id }], sourceVersion: entry.sourceVersion, reason: null }, company)).toBeNull()
  })
  it('displays only the backend rate and scope proof, with unavailable never becoming zero', () => {
    expect(decodeCollateralRatePreview({ outcome: 'Available', ratePercent: '40', sourceVersion: 'source:1', scope: 'TypeBankRemainingDays', reason: null })?.ratePercent).toBe('40')
    expect(decodeCollateralRatePreview({ outcome: 'Unavailable', ratePercent: null, sourceVersion: null, scope: null, reason: 'unreadable' })?.ratePercent).toBeNull()
    expect(decodeCollateralRatePreview({ outcome: 'Unavailable', ratePercent: '0', sourceVersion: null, scope: null, reason: 'unreadable' })).toBeNull()
  })
})
