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

  it('mounts the eight static routes', () => {
    expect(settingsRoutes).toHaveLength(8)
    expect(matchRoutes(settingsRoutes, '/ayarlar/iskonto-limitleri')?.at(-1)?.route.path).toBe('ayarlar/iskonto-limitleri')
    expect(matchRoutes(settingsRoutes, '/ayarlar/vergi')?.at(-1)?.route.path).toBe('ayarlar/vergi')
    expect(matchRoutes(settingsRoutes, '/ayarlar/kurlar')?.at(-1)?.route.path).toBe('ayarlar/kurlar')
    expect(matchRoutes(settingsRoutes, '/ayarlar/teslim-toleransi')?.at(-1)?.route.path).toBe('ayarlar/teslim-toleransi')
    expect(matchRoutes(settingsRoutes, '/ayarlar/onay-esikleri')?.at(-1)?.route.path).toBe('ayarlar/onay-esikleri')
    expect(matchRoutes(settingsRoutes, '/ayarlar/giris-deneme-siniri')?.at(-1)?.route.path).toBe(
      'ayarlar/giris-deneme-siniri',
    )
    expect(matchRoutes(settingsRoutes, '/ayarlar/birim-cevrim-politikasi')?.at(-1)?.route.path).toBe(
      'ayarlar/birim-cevrim-politikasi',
    )
    expect(matchRoutes(settingsRoutes, '/ayarlar/birim-profilleri')?.at(-1)?.route.path).toBe('ayarlar/birim-profilleri')
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

  it('mirrors the F-029 S4b sales policy ids, routes and labels, each with one Ayarlar entry before Sistem Yönetimi', () => {
    const pairs = [
      [CAPABILITY.settingsSalesDeliveryTolerance, SETTINGS_CAPABILITY.salesDeliveryTolerance],
      [CAPABILITY.settingsSalesApprovalThresholds, SETTINGS_CAPABILITY.salesApprovalThresholds],
    ] as const
    const entries = NAV_GROUPS.find((group) => group.id === 'settings')?.entries ?? []
    for (const [shellId, settingsId] of pairs) {
      expect(shellId).toBe(settingsId)
      expect(CAPABILITY_ROUTE[shellId]).toBe(SETTINGS_ROUTE[settingsId])
      expect(CAPABILITY_NAV_LABEL[shellId]).toBe(SETTINGS_NAV_LABEL[settingsId])
      expect(NAV_CAPABILITIES.filter((item) => item === shellId)).toHaveLength(1)
      expect(entries).toContain(shellId)
      expect(entries.indexOf(shellId)).toBeLessThan(entries.indexOf(CAPABILITY.identityAssignmentsList))
    }
    expect(SETTINGS_CAPABILITY.salesDeliveryTolerance).toBe('settings.sales-delivery-tolerances.manage')
    expect(SETTINGS_CAPABILITY.salesApprovalThresholds).toBe('settings.sales-approval-thresholds.manage')
  })

  it('mirrors the KIM-D34 login attempt limit, KIM-D37 unit conversion and F-029 unit profile ids, routes and labels, one entry each', () => {
    const pairs = [
      [CAPABILITY.settingsLoginRateLimit, SETTINGS_CAPABILITY.loginRateLimit],
      [CAPABILITY.settingsUnitConversionPolicy, SETTINGS_CAPABILITY.unitConversionPolicy],
      [CAPABILITY.settingsUnitProfiles, SETTINGS_CAPABILITY.unitProfiles],
    ] as const
    const entries = NAV_GROUPS.find((group) => group.id === 'settings')?.entries ?? []
    for (const [shellId, settingsId] of pairs) {
      expect(shellId).toBe(settingsId)
      expect(CAPABILITY_ROUTE[shellId]).toBe(SETTINGS_ROUTE[settingsId])
      expect(CAPABILITY_NAV_LABEL[shellId]).toBe(SETTINGS_NAV_LABEL[settingsId])
      expect(NAV_CAPABILITIES.filter((item) => item === shellId)).toHaveLength(1)
      expect(entries.filter((item) => item === shellId)).toHaveLength(1)
    }
    // Units come before rates inside Ana Veriler; the sign-in limit is a Sistem Yönetimi entry; opening stock stays last.
    expect(entries.indexOf(CAPABILITY.settingsUnitConversionPolicy)).toBeLessThan(
      entries.indexOf(CAPABILITY.settingsExchangeRates),
    )
    expect(entries.indexOf(CAPABILITY.settingsUnitProfiles)).toBeGreaterThan(
      entries.indexOf(CAPABILITY.settingsUnitConversionPolicy),
    )
    expect(entries.indexOf(CAPABILITY.settingsUnitProfiles)).toBeLessThan(entries.indexOf(CAPABILITY.settingsExchangeRates))
    expect(entries.indexOf(CAPABILITY.settingsLoginRateLimit)).toBeGreaterThan(
      entries.indexOf(CAPABILITY.identityAssignmentsList),
    )
    expect(entries.at(-1)).toBe('inventory.opening-stock.record')
    expect(SETTINGS_CAPABILITY.loginRateLimit).toBe('settings.login-rate-limit.manage')
    expect(SETTINGS_CAPABILITY.unitConversionPolicy).toBe('settings.unit-conversion-policies.manage')
    expect(SETTINGS_CAPABILITY.unitProfiles).toBe('settings.unit-profiles.manage')
  })
})
