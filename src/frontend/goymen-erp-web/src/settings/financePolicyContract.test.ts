import { describe, expect, it } from 'vitest'
import { decodeFinancePolicies, decodeFinancePolicyPreview } from './financePolicyContract'
import { clearPendingFinancePolicies, financePolicyPendingKey, recallFinancePolicy, releaseFinancePolicy, reserveFinancePolicy } from './pendingFinancePolicies'
const company = '11111111-1111-1111-1111-111111111111'
const id = '22222222-2222-2222-2222-222222222222'
const other = '33333333-3333-3333-3333-333333333333'
const references = { companyId: company, operations: [{ permission: 'finance.cash-transfer.commit', name: 'Virman' }], roles: [{ roleId: id, code: 'Synthetic', name: 'Sentetik rol', sourceVersion: 'role:hash1' }], cashAccounts: [{ cashAccountId: id, currencyId: other, name: 'Sentetik kasa', version: '2' }], currencies: [{ currencyId: other, code: 'TRY' }] }
const header = { companyId: company, policyId: id, version: 1, stateVersion: 2, state: 'Published', validFrom: '2026-11-18', validThrough: null, retiredFrom: null, preparedBy: 'Synthetic', preparedAtUtc: '2026-11-18T09:00:00Z', publishedBy: 'Synthetic', publishedAtUtc: '2026-11-18T09:00:00Z', reason: 'Synthetic reason' }
export const financePolicyFixture = { companyId: company, businessDate: '2026-11-18', references, approvalPolicies: [], cashCountDifferencePolicies: [] }
describe('KIM-S19 strict financial policy sources', () => {
  it('keeps an actual empty history distinct from an explicitly published zero threshold', () => {
    expect(decodeFinancePolicies(financePolicyFixture, company)?.cashCountDifferencePolicies).toEqual([])
    const raw = { ...financePolicyFixture, cashCountDifferencePolicies: [{ ...header, cashAccountId: id, cashAccountVersion: '2', currencyId: other, amountThreshold: '0' }] }
    expect(decodeFinancePolicies(raw, company)?.cashCountDifferencePolicies[0].amountThreshold).toBe('0')
    expect(decodeFinancePolicies({ ...raw, cashCountDifferencePolicies: [{ ...raw.cashCountDifferencePolicies[0], amountThreshold: null }] }, company)).toBeNull()
  })
  it('rejects foreign company, unknown fields, duplicate identities, noncanonical money and invalid lifecycle proofs', () => {
    expect(decodeFinancePolicies(financePolicyFixture, other)).toBeNull()
    expect(decodeFinancePolicies({ ...financePolicyFixture, assumedThreshold: '0' }, company)).toBeNull()
    expect(decodeFinancePolicies({ ...financePolicyFixture, references: { ...references, roles: [references.roles[0], references.roles[0]] } }, company)).toBeNull()
    const row = { ...header, cashAccountId: id, cashAccountVersion: '2', currencyId: other, amountThreshold: '1.00' }
    expect(decodeFinancePolicies({ ...financePolicyFixture, cashCountDifferencePolicies: [row] }, company)).toBeNull()
    expect(decodeFinancePolicies({ ...financePolicyFixture, cashCountDifferencePolicies: [{ ...row, amountThreshold: '1', publishedAtUtc: null }] }, company)).toBeNull()
  })
  it('requires unavailable preview to carry no invented threshold result', () => {
    const missing = { outcome: 'Unavailable', policyId: null, version: null, exceedsThreshold: null, reason: 'settings_finance_policy_not_configured' }
    expect(decodeFinancePolicyPreview(missing, 'cash-count-differences')).not.toBeNull()
    expect(decodeFinancePolicyPreview({ ...missing, exceedsThreshold: false }, 'cash-count-differences')).toBeNull()
    expect(decodeFinancePolicyPreview({ outcome: 'Available', policyId: id, version: 1, exceedsThreshold: false, reason: null }, 'cash-count-differences')).not.toBeNull()
  })
  it('keeps an unknown body/key across capability generations but never another actor company or session', () => {
    clearPendingFinancePolicies()
    const key = financePolicyPendingKey(1, 'actor-a', company, 'approval-matrices')!
    const command = { key: 'same-command-key', suffix: 'approval-matrices', body: { expectedVersion: '0', reason: 'Synthetic frozen payload' } }
    reserveFinancePolicy(key, command)
    expect(recallFinancePolicy(financePolicyPendingKey(1, 'actor-a', company, 'approval-matrices'))).toEqual(command)
    expect(recallFinancePolicy(financePolicyPendingKey(1, 'actor-b', company, 'approval-matrices'))).toBeNull()
    expect(recallFinancePolicy(financePolicyPendingKey(1, 'actor-a', other, 'approval-matrices'))).toBeNull()
    expect(recallFinancePolicy(financePolicyPendingKey(2, 'actor-a', company, 'approval-matrices'))).toBeNull()
    releaseFinancePolicy(key, 'a-different-key'); expect(recallFinancePolicy(key)).toEqual(command)
    releaseFinancePolicy(key, command.key); expect(recallFinancePolicy(key)).toBeNull()
  })
})
