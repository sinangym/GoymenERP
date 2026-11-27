import { describe, expect, it } from 'vitest'
import {
  decodeRoleDetail,
  decodeRoleRequestImpact,
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

const impactCompany='10000000-0000-4000-8000-000000000001'
const codeImpact={companyId:impactCompany,userId:USER_ALICE,roleId:ROLE_OPERATOR,
 currentPermissionCodes:[{code:'identity.profile.read',isCritical:false}],requestedRolePermissionCodes:[{code:'identity.profile.read',isCritical:false},{code:'identity.assignments.read',isCritical:true}],
 addedPermissionCodes:['identity.assignments.read'],alreadyHeldCodes:['identity.profile.read'],roleHasCriticalPermissions:true,hasSegregationOfDutiesConflict:null,expandsScope:null}
it('decodes the exact code-only native impact while preserving unassessed scope and SoD',()=>{
 expect(decodeRoleRequestImpact(codeImpact,impactCompany,USER_ALICE,ROLE_OPERATOR)).toEqual(codeImpact)
 expect(decodeRoleRequestImpact({...codeImpact,hasSegregationOfDutiesConflict:false,expandsScope:true},impactCompany,USER_ALICE,ROLE_OPERATOR)).toMatchObject({hasSegregationOfDutiesConflict:false,expandsScope:true})
})
it.each([{companyId:ROLE_OPERATOR},{userId:ROLE_OPERATOR},{roleId:USER_ALICE},{companyId:impactCompany+'\n'},{extra:true},{currentPermissionCodes:null},
 {currentPermissionCodes:[{code:'identity.profile.read',isCritical:false,scope:'Company'}]},{requestedRolePermissionCodes:[{code:'identity.assignments.read',isCritical:'true'}]},
 {requestedRolePermissionCodes:[{code:'identity.assignments.read',isCritical:true},{code:'identity.assignments.read',isCritical:true}]},
 {addedPermissionCodes:['identity.assignments.read','identity.assignments.read']},{alreadyHeldCodes:['identity.profile.read','identity.profile.read']},
 {addedPermissionCodes:['foreign.permission']},{alreadyHeldCodes:['identity.assignments.read']},{roleHasCriticalPermissions:false},{roleHasCriticalPermissions:null},
 {hasSegregationOfDutiesConflict:undefined},{hasSegregationOfDutiesConflict:'false'},{expandsScope:undefined},{expandsScope:0},
 {requestedRolePermissionCodes:[{code:'  ',isCritical:false}]},{addedPermissionCodes:null},{addedPermissionCodes:[]}])('refuses malformed or foreign native code impact without a safe partial fallback %j',delta=>{
 expect(decodeRoleRequestImpact({...codeImpact,...delta},impactCompany,USER_ALICE,ROLE_OPERATOR)).toBeNull()
})

it('refuses an owner delta that classifies a currently held code as added without manufacturing a replacement',()=>{
 const contradictory={...codeImpact,currentPermissionCodes:codeImpact.requestedRolePermissionCodes}
 expect(decodeRoleRequestImpact(contradictory,impactCompany,USER_ALICE,ROLE_OPERATOR)).toBeNull()
})
it.each([true,false])('refuses a Critical classification toggle for the same current and requested permission code: current=%s',currentCritical=>{
 const contradictory={...codeImpact,currentPermissionCodes:[{code:'identity.profile.read',isCritical:currentCritical}],
  requestedRolePermissionCodes:[{code:'identity.profile.read',isCritical:!currentCritical}],addedPermissionCodes:[],alreadyHeldCodes:['identity.profile.read'],roleHasCriticalPermissions:!currentCritical}
 expect(decodeRoleRequestImpact(contradictory,impactCompany,USER_ALICE,ROLE_OPERATOR)).toBeNull()
})
