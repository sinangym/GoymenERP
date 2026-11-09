import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import { AUDIT_CAPABILITY, AUDIT_NAV_LABEL, AUDIT_ROUTE } from '../audit/capabilities'
import { auditRoutes } from '../audit/auditRoutes'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'
import { NAV_GROUPS } from './navigation'

describe('shell ↔ audit log contract (UI-K02, KLN-D2)', () => {
  it('mirrors the owner id, route and label, with exactly one navigation entry', () => {
    expect(CAPABILITY.identityAuditList).toBe(AUDIT_CAPABILITY.list)
    expect(CAPABILITY_ROUTE[CAPABILITY.identityAuditList]).toBe(AUDIT_ROUTE[AUDIT_CAPABILITY.list])
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.identityAuditList]).toBe(AUDIT_NAV_LABEL[AUDIT_CAPABILITY.list])
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.identityAuditList]).toBe('Denetim kaydı')
    expect(NAV_CAPABILITIES.filter((item) => item === CAPABILITY.identityAuditList)).toHaveLength(1)
  })

  it('is drawn in Ayarlar right after the access list, inside the Sistem Yönetimi section', () => {
    const owners = NAV_GROUPS.filter((group) => group.entries.includes(CAPABILITY.identityAuditList))
    expect(owners.map((group) => group.id)).toEqual(['settings'])
    const entries = owners[0].entries
    const access = entries.indexOf(CAPABILITY.identityAssignmentsList)
    expect(entries[access + 1]).toBe(CAPABILITY.identityAuditList)
    expect(entries[access + 2]).toBe(CAPABILITY.inventoryOpeningStockRecord)
    expect(owners[0].subheadings?.[CAPABILITY.identityAssignmentsList]).toBe('Sistem Yönetimi')
    expect(owners[0].subheadings?.[CAPABILITY.identityAuditList]).toBeUndefined()
  })

  it('mounts exactly one static route for the list', () => {
    expect(auditRoutes).toHaveLength(1)
    expect(matchRoutes(auditRoutes, '/ayarlar/denetim-kaydi')?.at(-1)?.route.path).toBe(
      AUDIT_ROUTE[AUDIT_CAPABILITY.list].slice(1),
    )
  })
})
