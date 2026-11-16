import { describe, expect, it } from 'vitest'
import { decodeAccessGrantOutcome, decodeAccessGrantRequests } from './accessGrantContract'
import { decodeRoleSummaries, decodeUserDetail } from './contract'
import { USER_ALICE, USER_BORA, ROLE_OPERATOR, assignment, roleSummary, userDetail } from './testHarness'

describe('KIM-S6 access command contracts', () => {
  it('requires persisted versions and owner classified criticality for the write-capable read view', () => {
    const detail = userDetail(USER_ALICE, 'SENTETIK')
    expect(decodeUserDetail(detail, USER_ALICE)).not.toBeNull()
    expect(decodeUserDetail({ ...detail, assignments: [assignment(ROLE_OPERATOR, 'SENTETIK', 'SENTETIK', { version: '01' })] }, USER_ALICE)).toBeNull()
    expect(decodeUserDetail({ ...detail, assignments: [assignment(ROLE_OPERATOR, 'SENTETIK', 'SENTETIK', { version: undefined })] }, USER_ALICE)).toBeNull()
    expect(decodeRoleSummaries([roleSummary(ROLE_OPERATOR, 'SENTETIK', 'SENTETIK', { isCritical: undefined })])).toBeNull()
  })

  it('rejects malformed or duplicate pending requests without guessing a version or status', () => {
    const row = { requestId: ROLE_OPERATOR, requestedBy: { userId: USER_ALICE, displayName: 'SENTETIK' }, target: { userId: USER_BORA, displayName: 'SENTETIK' }, roleId: ROLE_OPERATOR, roleCode: 'SENTETIK', roleName: 'SENTETIK', status: 'Pending', requestedAtUtc: '2026-11-16T10:00:00Z', validFromUtc: null, validUntilUtc: null, reason: null, version: '7' }
    expect(decodeAccessGrantRequests([row])?.[0].version).toBe('7')
    expect(decodeAccessGrantRequests([row, row])).toBeNull()
    expect(decodeAccessGrantRequests([{ ...row, status: 'Approved' }])).toBeNull()
    expect(decodeAccessGrantRequests([{ ...row, version: '9223372036854775808' }])).toBeNull()
    expect(decodeAccessGrantOutcome({ id: ROLE_OPERATOR, version: '1', status: 'Revoked', roleAssignmentId: null })).toMatchObject({ status: 'Revoked' })
    expect(decodeAccessGrantOutcome({ id: ROLE_OPERATOR, version: 1, status: 'Revoked', roleAssignmentId: null })).toBeNull()
  })
})
