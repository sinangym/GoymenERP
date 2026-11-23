import { describe, expect, it } from 'vitest'
import { decodeCollateralAccount, decodeCollateralBankCandidates } from './CollateralContract'
import { account, recorded, CO_COMPANY, CO_ACCOUNT, CO_OTHER_COMPANY, coId } from './collateralFixtures'

const bank = { companyId: CO_COMPANY, bankInstitutionId: coId(7), name: 'Sentetik banka', eftCode: '0007', sourceVersion: 'settings.bank-institutions:synthetic:3' }
describe('K6 explicit issuer bank proof', () => {
  it('preserves leading EFT zeros and the immutable source version alongside a legacy optional bank member', () => {
    expect(decodeCollateralBankCandidates({ companyId: CO_COMPANY, items: [bank] }, CO_COMPANY).ok).toBe(true)
    const result = decodeCollateralAccount(account({ held: [recorded({ bankInstitution: bank })] }), CO_COMPANY, CO_ACCOUNT)
    expect(result.ok && result.value.held[0].bankInstitution).toEqual(bank)
    expect(decodeCollateralAccount(account(), CO_COMPANY, CO_ACCOUNT).ok).toBe(true)
  })
  it('rejects foreign, missing, duplicated and account-derived bank proof', () => {
    for (const bad of [{ ...bank, companyId: CO_OTHER_COMPANY }, { ...bank, sourceVersion: '' }, { ...bank, bankAccountId: coId(9) }, null])
      expect(decodeCollateralAccount(account({ held: [recorded({ bankInstitution: bad })] }), CO_COMPANY, CO_ACCOUNT).ok).toBe(false)
    expect(decodeCollateralBankCandidates({ companyId: CO_COMPANY, items: [bank, bank] }, CO_COMPANY).ok).toBe(false)
  })
})
