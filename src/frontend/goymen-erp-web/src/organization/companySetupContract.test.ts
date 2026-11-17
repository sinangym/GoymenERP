import { describe, expect, it } from 'vitest'
import { decodeAdminCandidates, decodeFirstAdminSelected, decodeSetupCapabilities, decodeSetupCompanies, decodeSetupCompany } from './companySetupContract'

const company = { companyId: 'aaaaaaaa-0000-0000-0000-000000000001', code: 'SENTETIK', legalName: 'SENTETIK şirket', lifecycleState: 'SetupPending', version: 1 }
describe('KIM-D56 setup contract', () => {
  it('requires exact capabilities and never substitutes a missing grant', () => {
    expect(decodeSetupCapabilities({ canOpenCompany: true, canAssignFirstAdmin: false })).toEqual({ canOpenCompany: true, canAssignFirstAdmin: false })
    expect(decodeSetupCapabilities({ canOpenCompany: true })).toBeNull()
    expect(decodeSetupCapabilities({ canOpenCompany: true, canAssignFirstAdmin: true, role: 'SENTETIK' })).toBeNull()
  })
  it('rejects active, foreign-shaped, unsafe-version and duplicate pending rows', () => {
    expect(decodeSetupCompany(company)).toEqual(company)
    expect(decodeSetupCompany({ ...company, companyId: '00000000-0000-0000-0000-000000000000' })).toBeNull()
    expect(decodeSetupCompany({ ...company, lifecycleState: 'Active' })).toBeNull()
    expect(decodeSetupCompany({ ...company, version: Number.MAX_SAFE_INTEGER + 1 })).toBeNull()
    expect(decodeSetupCompany({ ...company, openedByUserId: 'SENTETIK' })).toBeNull()
    expect(decodeSetupCompanies([company, company])).toBeNull()
  })
  it('uses active-user candidates without accepting persona or implicit opener fields', () => {
    const candidate = { userId: company.companyId, displayName: 'SENTETIK seçilecek yönetici' }
    expect(decodeAdminCandidates([candidate])).toEqual([candidate])
    expect(decodeAdminCandidates([{ ...candidate, userId: '00000000-0000-0000-0000-000000000000' }])).toBeNull()
    expect(decodeAdminCandidates([{ ...candidate, isOpener: true }])).toBeNull()
    expect(decodeAdminCandidates([candidate, candidate])).toBeNull()
  })
  it('accepts only the exact selected assignment outcome', () => {
    const selected = { id: company.companyId, version: '1', status: 'Active', roleAssignmentId: company.companyId }
    expect(decodeFirstAdminSelected(selected)).toEqual(selected)
    expect(decodeFirstAdminSelected({ ...selected, roleAssignmentId: null })).toBeNull()
    expect(decodeFirstAdminSelected({ ...selected, status: 'Pending' })).toBeNull()
    expect(decodeFirstAdminSelected({ ...selected, extra: true })).toBeNull()
  })
})
