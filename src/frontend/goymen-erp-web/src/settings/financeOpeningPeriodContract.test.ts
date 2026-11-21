import { describe, expect, it } from 'vitest'
import { COMPANY_A, COMPANY_B } from './testHarness'
import { decodeFinanceOpeningPeriod } from './financeOpeningPeriodContract'
describe('KIM-S19 finance opening period contract', () => {
  it('keeps a missing record distinct from open or closed', () => {
    const empty = { companyId: COMPANY_A, state: 0, version: '0', versions: [] }
    expect(decodeFinanceOpeningPeriod(empty, COMPANY_A)?.state).toBe(0)
    expect(decodeFinanceOpeningPeriod(empty, COMPANY_B)).toBeNull()
    expect(decodeFinanceOpeningPeriod({ ...empty, state: 1 }, COMPANY_A)).toBeNull()
    expect(decodeFinanceOpeningPeriod({ ...empty, version: '1' }, COMPANY_A)).toBeNull()
    expect(decodeFinanceOpeningPeriod({ ...empty, state: 'Open' }, COMPANY_A)).toBeNull()
  })
  it('refuses a state that disagrees with its persisted current version', () => {
    const version = { version: '2', open: false, recordedBy: 'SENTETIK', recordedAtUtc: '2026-11-16T10:00:00Z', reason: 'SENTETIK' }
    const view = { companyId: COMPANY_A, state: 2, version: '2', versions: [version] }
    expect(decodeFinanceOpeningPeriod(view, COMPANY_A)?.versions[0].open).toBe(false)
    expect(decodeFinanceOpeningPeriod({ ...view, state: 1 }, COMPANY_A)).toBeNull()
    expect(decodeFinanceOpeningPeriod({ ...view, versions: [version, version] }, COMPANY_A)).toBeNull()
  })
})
