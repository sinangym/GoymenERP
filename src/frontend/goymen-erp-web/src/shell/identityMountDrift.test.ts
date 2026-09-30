import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import { IDENTITY_CAPABILITY, IDENTITY_NAV_LABEL, IDENTITY_ROUTE } from '../identity/capabilities'
import { identityRoutes } from '../identity/identityRoutes'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'

describe('shell ↔ Identity access-view contract', () => {
  it('mirrors the owner ids, routes and labels, with exactly one navigation entry', () => {
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
    expect(identityRoutes).toHaveLength(2)
    expect(matchRoutes(identityRoutes, '/ayarlar/kullanicilar-ve-yetkiler')?.at(-1)?.route.path).toBe(
      IDENTITY_ROUTE[IDENTITY_CAPABILITY.accessList].slice(1),
    )
    const role = matchRoutes(identityRoutes, '/ayarlar/kullanicilar-ve-yetkiler/roller/role-1')?.at(-1)
    expect(role?.route.path).toBe(IDENTITY_ROUTE[IDENTITY_CAPABILITY.roleDetail].slice(1))
    expect(role?.params.roleId).toBe('role-1')
  })
})
