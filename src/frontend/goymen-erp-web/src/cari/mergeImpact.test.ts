import { describe, expect, it } from 'vitest'
import { decodeMergeImpact, decodeMergePurposeImpact } from './mergeImpact'
const a = '11111111-1111-1111-1111-111111111111', b = '22222222-2222-2222-2222-222222222222', company = 'aaaaaaaa-0000-0000-0000-000000000001'
const expected = { partyId: a, mergedPartyId: b, companyId: company, expectedVersion: '9007199254740993', mergedPartyExpectedVersion: '9223372036854775807' }
const fixture = () => ({ companyId: company,
  survivor: { partyId: a, code: 'C-000001', legalName: 'SENTETIK ana', version: expected.expectedVersion, isActive: true, mergedIntoPartyId: null },
  merged: { partyId: b, code: 'C-000002', legalName: 'SENTETIK diğer', version: expected.mergedPartyExpectedVersion, isActive: true, mergedIntoPartyId: null },
  holdingAccounts: [{ companyId: company, accountId: a, partyId: a, version: '1', isActive: true }, { companyId: company, accountId: b, partyId: b, version: '2', isActive: false }],
  changes: { partyId: b, mergedIntoPartyId: a, isActive: false },
})
describe('actual selected-pair merge impact wire', () => {
  it('preserves Int64 versions, all account rows and native passive accounts without calculating balances', () => {
    expect(decodeMergeImpact(fixture(), expected)).toEqual(fixture())
  })
  it.each(['company','party','version','accounts','duplicate','foreign-account','changes','unknown','missing'])('refuses %s mismatch without a partial preview', failure => {
    const value = fixture()
    const changed = failure === 'company' ? { ...value, companyId: b }
      : failure === 'party' ? { ...value, merged: { ...value.merged, partyId: a } }
        : failure === 'version' ? { ...value, merged: { ...value.merged, version: '5' } }
          : failure === 'accounts' ? { ...value, holdingAccounts: [] }
            : failure === 'duplicate' ? { ...value, holdingAccounts: [...value.holdingAccounts, value.holdingAccounts[0]] }
              : failure === 'foreign-account' ? { ...value, holdingAccounts: [...value.holdingAccounts, { ...value.holdingAccounts[0], accountId: '33333333-3333-3333-3333-333333333333', partyId: company }] }
                : failure === 'changes' ? { ...value, changes: { ...value.changes, isActive: true } }
                  : failure === 'unknown' ? { ...value, financeCount: 1 } : { ...value, changes: undefined }
    expect(decodeMergeImpact(changed, expected)).toBeNull()
  })
  it.each(['\n','\r','\r\n'])('refuses final terminators %j on new IDs and versions', suffix => {
    const value = fixture()
    expect(decodeMergeImpact({ ...value, companyId: company + suffix }, expected)).toBeNull()
    expect(decodeMergeImpact({ ...value, survivor: { ...value.survivor, version: '1' + suffix } }, { ...expected, expectedVersion: '1' + suffix })).toBeNull()
  })
})

describe('purpose impact wrapper', () => {
  it('retains the exact native human fields and the opaque canonical token', () => {
    const value = { impact: fixture(), seenImpactDigest: 'A'.repeat(64) }
    expect(decodeMergePurposeImpact(value, expected)).toEqual(value)
    expect(decodeMergeImpact(fixture(), expected)).toEqual(fixture())
  })
  it.each(['', 'A'.repeat(63), 'a'.repeat(64), 'A'.repeat(64) + '\n', 'A'.repeat(64) + '\r', ' '.repeat(64), null])('rejects malformed token %j', seenImpactDigest => {
    expect(decodeMergePurposeImpact({ impact: fixture(), seenImpactDigest }, expected)).toBeNull()
  })
  it('rejects bare legacy, unknown root, unknown native field and wrong selected pair', () => {
    const value = { impact: fixture(), seenImpactDigest: 'A'.repeat(64) }
    expect(decodeMergePurposeImpact(fixture(), expected)).toBeNull()
    expect(decodeMergePurposeImpact({ ...value, receipt: {} }, expected)).toBeNull()
    expect(decodeMergePurposeImpact({ ...value, impact: { ...fixture(), amount: '1' } }, expected)).toBeNull()
    expect(decodeMergePurposeImpact(value, { ...expected, mergedPartyId: company })).toBeNull()
  })
})
