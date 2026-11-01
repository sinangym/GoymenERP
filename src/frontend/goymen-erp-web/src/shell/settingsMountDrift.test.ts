import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import { SETTINGS_CAPABILITY, SETTINGS_NAV_LABEL, SETTINGS_ROUTE } from '../settings/capabilities'
import { settingsRoutes } from '../settings/settingsRoutes'
import { NAV_CAPABILITIES } from './AppShell'
import { CAPABILITY, CAPABILITY_NAV_LABEL, CAPABILITY_ROUTE } from './capabilities'
import { NAV_GROUPS } from './navigation'

describe('shell ↔ Settings discount limit screen contract (SAT-D61…D70)', () => {
  it('mirrors the owner id, route and label, with exactly one navigation entry under Ayarlar', () => {
    expect(CAPABILITY.settingsSalesDiscountLimits).toBe(SETTINGS_CAPABILITY.salesDiscountLimits)
    expect(CAPABILITY_ROUTE[CAPABILITY.settingsSalesDiscountLimits]).toBe(
      SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesDiscountLimits],
    )
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.settingsSalesDiscountLimits]).toBe(
      SETTINGS_NAV_LABEL[SETTINGS_CAPABILITY.salesDiscountLimits],
    )
    expect(NAV_CAPABILITIES.filter((item) => item === CAPABILITY.settingsSalesDiscountLimits)).toHaveLength(1)
    const settings = NAV_GROUPS.find((group) => group.id === 'settings')
    expect(settings?.entries).toContain(CAPABILITY.settingsSalesDiscountLimits)
  })

  it('mounts the two static routes', () => {
    expect(settingsRoutes).toHaveLength(2)
    expect(matchRoutes(settingsRoutes, '/ayarlar/iskonto-limitleri')?.at(-1)?.route.path).toBe('ayarlar/iskonto-limitleri')
    expect(matchRoutes(settingsRoutes, '/ayarlar/vergi')?.at(-1)?.route.path).toBe('ayarlar/vergi')
  })

  it('mirrors the MOD-03 tax master id, route and label, with one navigation entry under Ayarlar', () => {
    expect(CAPABILITY.settingsTaxMaster).toBe(SETTINGS_CAPABILITY.taxMaster)
    expect(CAPABILITY_ROUTE[CAPABILITY.settingsTaxMaster]).toBe(SETTINGS_ROUTE[SETTINGS_CAPABILITY.taxMaster])
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.settingsTaxMaster]).toBe(SETTINGS_NAV_LABEL[SETTINGS_CAPABILITY.taxMaster])
    expect(NAV_CAPABILITIES.filter((item) => item === CAPABILITY.settingsTaxMaster)).toHaveLength(1)
    expect(NAV_GROUPS.find((group) => group.id === 'settings')?.entries).toContain(CAPABILITY.settingsTaxMaster)
  })
})
