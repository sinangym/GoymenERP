import { shellRoutes } from './router'
import { workRoutes } from '../work/workRoutes'
import { WORK_INBOX_KINDS } from '../work/workInboxKinds'
import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import { IDENTITY_CAPABILITY, IDENTITY_NAV_LABEL, IDENTITY_ROUTE } from '../identity/capabilities'
import { identityRoutes } from '../identity/identityRoutes'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'

describe('shell ↔ Identity access-view contract', () => {
  it('mirrors the owner ids, routes and labels, with exactly one navigation entry', () => {
    expect(CAPABILITY.identityUsersManage).toBe(IDENTITY_CAPABILITY.userManagement)
    expect(CAPABILITY_ROUTE[CAPABILITY.identityUsersManage]).toBe(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement])
    expect(NAV_CAPABILITIES.filter((item) => item === CAPABILITY.identityUsersManage)).toHaveLength(1)
    expect(CAPABILITY.identityAssignmentsList).toBe(IDENTITY_CAPABILITY.accessList)
    expect(CAPABILITY.identityRolesDetail).toBe(IDENTITY_CAPABILITY.roleDetail)
    expect(CAPABILITY_ROUTE[CAPABILITY.identityAssignmentsList]).toBe(IDENTITY_ROUTE[IDENTITY_CAPABILITY.accessList])
    expect(CAPABILITY_ROUTE[CAPABILITY.identityRolesDetail]).toBe(IDENTITY_ROUTE[IDENTITY_CAPABILITY.roleDetail])
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.identityAssignmentsList]).toBe(
      IDENTITY_NAV_LABEL[IDENTITY_CAPABILITY.accessList],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.identityRolesDetail]).toBe('')
    expect(NAV_CAPABILITIES.filter((item) => item === CAPABILITY.identityAssignmentsList)).toHaveLength(1)
    expect(NAV_CAPABILITIES).not.toContain(CAPABILITY.identityRolesDetail)
  })

  it('mounts the static list before the parameterized role card', () => {
    expect(identityRoutes).toHaveLength(4)
    expect(matchRoutes(identityRoutes, '/hesap/iki-asamali-dogrulama')?.at(-1)?.route.path).toBe('hesap/iki-asamali-dogrulama')
    expect(matchRoutes(identityRoutes, '/ayarlar/kullanicilar')?.at(-1)?.route.path).toBe(IDENTITY_ROUTE[IDENTITY_CAPABILITY.userManagement].slice(1))
    expect(matchRoutes(identityRoutes, '/ayarlar/kullanicilar-ve-yetkiler')?.at(-1)?.route.path).toBe(
      IDENTITY_ROUTE[IDENTITY_CAPABILITY.accessList].slice(1),
    )
    const role = matchRoutes(identityRoutes, '/ayarlar/kullanicilar-ve-yetkiler/roller/role-1')?.at(-1)
    expect(role?.route.path).toBe(IDENTITY_ROUTE[IDENTITY_CAPABILITY.roleDetail].slice(1))
    expect(role?.params.roleId).toBe('role-1')
  })
})

it('pairs one standalone access-grant purpose while keeping native access/role routes and no new sidebar item',()=>{
 const kind='identity.access-grant-request.v1',cap=CAPABILITY.identityAccessGrantWorkDecision,path=`/onaylarim/${kind}/:approvalId`
 expect(cap).toBe('identity.access-grant-request.work-decision');expect(CAPABILITY_ROUTE[cap]).toBe(path);expect(CAPABILITY_NAV_LABEL[cap]).toBe('');expect(NAV_CAPABILITIES).not.toContain(cap)
 expect(WORK_INBOX_KINDS.filter(row=>row.kind===kind)).toEqual([{kind,module:'identity',capability:cap}]);expect(WORK_INBOX_KINDS).toHaveLength(70);expect(new Set(WORK_INBOX_KINDS.map(row=>row.kind)).size).toBe(70)
 const routes=workRoutes.filter(route=>route.path===path.slice(1));expect(routes).toHaveLength(1)
 expect(matchRoutes(shellRoutes,path.replace(':approvalId','d4580000-0000-4000-8000-000000000058'))?.at(-1)?.route).toBe(routes[0])
 expect(IDENTITY_ROUTE[IDENTITY_CAPABILITY.accessList]).toBe('/ayarlar/kullanicilar-ve-yetkiler');expect(IDENTITY_ROUTE[IDENTITY_CAPABILITY.roleDetail]).toBe('/ayarlar/kullanicilar-ve-yetkiler/roller/:roleId')
})
