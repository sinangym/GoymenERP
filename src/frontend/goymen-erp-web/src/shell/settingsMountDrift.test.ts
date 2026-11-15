import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import {
  SETTINGS_CAPABILITY,
  SETTINGS_EXCHANGE_RATE_CONTROL,
  SETTINGS_NAV_LABEL,
  SETTINGS_ROUTE,
} from '../settings/capabilities'
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

  it('mounts the three static routes', () => {
    expect(settingsRoutes).toHaveLength(3)
    expect(matchRoutes(settingsRoutes, '/ayarlar/iskonto-limitleri')?.at(-1)?.route.path).toBe('ayarlar/iskonto-limitleri')
    expect(matchRoutes(settingsRoutes, '/ayarlar/vergi')?.at(-1)?.route.path).toBe('ayarlar/vergi')
    expect(matchRoutes(settingsRoutes, '/ayarlar/kurlar')?.at(-1)?.route.path).toBe('ayarlar/kurlar')
  })

  it('mirrors the MOD-03 tax master id, route and label, with one navigation entry under Ayarlar', () => {
    expect(CAPABILITY.settingsTaxMaster).toBe(SETTINGS_CAPABILITY.taxMaster)
    expect(CAPABILITY_ROUTE[CAPABILITY.settingsTaxMaster]).toBe(SETTINGS_ROUTE[SETTINGS_CAPABILITY.taxMaster])
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.settingsTaxMaster]).toBe(SETTINGS_NAV_LABEL[SETTINGS_CAPABILITY.taxMaster])
    expect(NAV_CAPABILITIES.filter((item) => item === CAPABILITY.settingsTaxMaster)).toHaveLength(1)
    expect(NAV_GROUPS.find((group) => group.id === 'settings')?.entries).toContain(CAPABILITY.settingsTaxMaster)
  })

  it('mirrors the F-025 exchange rate id, route and label, with one Ana Veriler entry and no control in the sidebar', () => {
    expect(CAPABILITY.settingsExchangeRates).toBe(SETTINGS_CAPABILITY.exchangeRates)
    expect(CAPABILITY_ROUTE[CAPABILITY.settingsExchangeRates]).toBe(SETTINGS_ROUTE[SETTINGS_CAPABILITY.exchangeRates])
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.settingsExchangeRates]).toBe(SETTINGS_NAV_LABEL[SETTINGS_CAPABILITY.exchangeRates])
    expect(NAV_CAPABILITIES.filter((item) => item === CAPABILITY.settingsExchangeRates)).toHaveLength(1)
    const entries = NAV_GROUPS.find((group) => group.id === 'settings')?.entries ?? []
    expect(entries).toContain(CAPABILITY.settingsExchangeRates)
    // Ana Veriler holds it: after the organization entries, before the Sistem Yönetimi section starts.
    expect(entries.indexOf(CAPABILITY.settingsExchangeRates)).toBeLessThan(entries.indexOf(CAPABILITY.identityAssignmentsList))
    for (const control of Object.values(SETTINGS_EXCHANGE_RATE_CONTROL)) {
      expect(NAV_CAPABILITIES as readonly string[]).not.toContain(control)
      expect(Object.keys(CAPABILITY_ROUTE)).not.toContain(control)
    }
  })
})
