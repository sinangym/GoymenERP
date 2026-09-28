import { describe, expect, it } from 'vitest'
import {
  decodeRoleDetail,
  decodeRoleSummaries,
  decodeUserDetail,
  decodeUserSummaries,
} from './contract'
import { accessListPath, isWellFormedId, roleDetailPath } from './capabilities'
import {
  ROLE_OPERATOR,
  USER_ALICE,
  assignment,
  roleDetail,
  roleSummary,
  userDetail,
  userSummary,
} from './testHarness'

describe('identity access-view contract', () => {
  it('decodes the server shapes and passes no extra field through', () => {
    const [user] = decodeUserSummaries([{ ...userSummary(USER_ALICE, 'Alice'), email: 'x@y.z' }]) ?? []
    expect(user).toEqual(userSummary(USER_ALICE, 'Alice'))
    expect(Object.keys(user ?? {})).not.toContain('email')

    const detail = decodeUserDetail({ ...userDetail(USER_ALICE, 'Alice'), subject: 'company-a-user' }, USER_ALICE)
    expect(detail?.assignments).toHaveLength(1)
    expect(Object.keys(detail ?? {})).not.toContain('subject')

    expect(decodeRoleSummaries([roleSummary(ROLE_OPERATOR, 'OPERATOR', 'Operatör')])).toHaveLength(1)
    expect(decodeRoleDetail(roleDetail(ROLE_OPERATOR, 'OPERATOR', 'Operatör'), ROLE_OPERATOR)?.holders).toHaveLength(1)
  })

  it('fails the whole unpaged list closed on one bad row', () => {
    expect(decodeUserSummaries([userSummary(USER_ALICE, 'Alice'), { userId: 'x' }])).toBeNull()
    expect(decodeUserSummaries([userSummary(USER_ALICE, 'Alice', { effectiveAssignmentCount: 3 })])).toBeNull()
    expect(decodeRoleSummaries([roleSummary(ROLE_OPERATOR, 'OPERATOR', 'Operatör', { holderCount: -1 })])).toBeNull()
    expect(decodeUserSummaries({})).toBeNull()
  })

  it('rejects unknown validity and scope tokens, bad instants and a detail for another record', () => {
    const withAssignment = (overrides: Record<string, unknown>) =>
      userDetail(USER_ALICE, 'Alice', { assignments: [assignment(ROLE_OPERATOR, 'OPERATOR', 'Operatör', overrides)] })
    expect(decodeUserDetail(withAssignment({ validity: 'maybe' }), USER_ALICE)).toBeNull()
    expect(decodeUserDetail(withAssignment({ scopes: [{ type: 'Tenant', id: USER_ALICE }] }), USER_ALICE)).toBeNull()
    expect(decodeUserDetail(withAssignment({ validFromUtc: '2026-01-01' }), USER_ALICE)).toBeNull()
    expect(decodeUserDetail(withAssignment({ approvedBy: { userId: USER_ALICE } }), USER_ALICE)).toBeNull()
    expect(decodeUserDetail(userDetail(USER_ALICE, 'Alice'), ROLE_OPERATOR)).toBeNull()
    expect(decodeUserDetail(userDetail(USER_ALICE, 'Alice', { assignments: [] }), USER_ALICE)).toBeNull()
    expect(decodeRoleDetail(roleDetail(ROLE_OPERATOR, 'OPERATOR', 'Operatör'), USER_ALICE)).toBeNull()
  })

  it('builds paths that carry only a well-formed selection', () => {
    expect(isWellFormedId(USER_ALICE)).toBe(true)
    expect(isWellFormedId('yeni')).toBe(false)
    expect(accessListPath('../x')).toBe('/ayarlar/kullanicilar-ve-yetkiler')
    expect(accessListPath(USER_ALICE)).toBe(`/ayarlar/kullanicilar-ve-yetkiler?kullanici=${USER_ALICE}`)
    expect(roleDetailPath(ROLE_OPERATOR, 'bad')).toBe(`/ayarlar/kullanicilar-ve-yetkiler/roller/${ROLE_OPERATOR}`)
    expect(roleDetailPath(ROLE_OPERATOR, USER_ALICE)).toBe(
      `/ayarlar/kullanicilar-ve-yetkiler/roller/${ROLE_OPERATOR}?kullanici=${USER_ALICE}`,
    )
  })
})
