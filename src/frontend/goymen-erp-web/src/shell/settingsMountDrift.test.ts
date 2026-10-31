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

  it('mounts the one static route', () => {
    expect(settingsRoutes).toHaveLength(1)
    expect(matchRoutes(settingsRoutes, '/ayarlar/iskonto-limitleri')?.at(-1)?.route.path).toBe('ayarlar/iskonto-limitleri')
  })
})
