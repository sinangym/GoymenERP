/**
 * SAT-D61…D70 — the Ayarlar screen of the Sales discount limit groups, their limits and the person → group bindings.
 *
 * Mirrors `SettingsShellCapabilityProvider` exactly (provider 1.6: eleven route capabilities, each on its own server permission,
 * and the exchange rate screen's three command controls). The first,
 * `settings.sales-discount-limits.manage`, company-wide, in one company (not Critical; granted to no role — SAT-D65/D69,
 * a human gate). Listing the id here grants nothing: an ungranted capability closes the route onto the neutral
 * not-found block and draws no sidebar entry. Every request is re-authorized server-side regardless.
 */

export const SETTINGS_CAPABILITY = {
  financeOpeningPeriod: 'settings.finance-opening-period.manage',
  manufacturingParameters: 'settings.manufacturing-parameter-profiles.read',
  manufacturingTolerances: 'settings.manufacturing-tolerance-profiles.read',
  /** Route `/ayarlar/iskonto-limitleri`; it carries its sidebar label. */
  salesDiscountLimits: 'settings.sales-discount-limits.manage',
  /**
   * MOD-03 (SAT-D83, provider 1.1): route `/ayarlar/vergi`, the tax master on its own name `settings.tax-master.manage`
   * (company-wide, not Critical, granted to no role here).
   */
  taxMaster: 'settings.tax-master.manage',
  /**
   * F-025 / B-11 (provider 1.2): route `/ayarlar/kurlar`, the exchange rate days on the read name
   * `settings.exchange-rates.read` (company-wide, not Critical, granted to no role here).
   */
  exchangeRates: 'settings.exchange-rates.read',
  /**
   * F-029 S4b, SAT-D5 (provider 1.3): route `/ayarlar/teslim-toleransi`, the delivery tolerance timeline on its own name
   * `settings.sales-delivery-tolerances.manage` (company-wide, not Critical, granted to no role here).
   */
  salesDeliveryTolerance: 'settings.sales-delivery-tolerances.manage',
  /**
   * F-029 S4b, SAT-D6 (provider 1.3): route `/ayarlar/onay-esikleri`, the approval amount thresholds per currency on its
   * own name `settings.sales-approval-thresholds.manage` (company-wide, not Critical, granted to no role here).
   */
  salesApprovalThresholds: 'settings.sales-approval-thresholds.manage',
  /**
   * KIM-D34 (provider 1.4): route `/ayarlar/giris-deneme-siniri`, the group-wide login attempt limit on its own name
   * `settings.login-rate-limit.manage` (company-wide, not Critical, granted to no role here).
   */
  loginRateLimit: 'settings.login-rate-limit.manage',
  /**
   * F-004, KIM-D37 (provider 1.4): route `/ayarlar/birim-cevrim-politikasi`, the company's unit conversion policy on its
   * own name `settings.unit-conversion-policies.manage` (company-wide, not Critical, granted to no role here).
   */
  unitConversionPolicy: 'settings.unit-conversion-policies.manage',
  /**
   * F-029 S1/S2, URN-D2…D5 (provider 1.5): route `/ayarlar/birim-profilleri`, the company's unit profiles on its own name
   * `settings.unit-profiles.manage` (company-wide, not Critical, granted to no role here).
   */
  unitProfiles: 'settings.unit-profiles.manage',
  moneyProfiles: 'settings.money-profiles.manage',
  /**
   * URT-D89 (provider 1.5): route `/ayarlar/is-merkezi-kapasitesi`, the company's work center capacities (input kg/hour and
   * the optional safety buffer) on its own name `settings.work-center-capacities.manage` (company-wide, not Critical,
   * granted to no role here; the production manager is a persona note only).
   */
  workCenterCapacity: 'settings.work-center-capacities.manage',
  /**
   * API-98, KIM-D49 (provider 1.6): route `/ayarlar/referans-listeleri`, the company's expense type and service type lists on
   * their own name `settings.reference-lists.manage` (company-wide, not Critical, granted to no role here; the purchasing
   * manager and Admin are the decided editors, a note for the human grant only).
   */
  purchasingReferenceLists: 'settings.reference-lists.manage',
  /**
   * B-14, KIM-D51 (provider 1.6): route `/ayarlar/uretim-referans-listeleri`, the company's paint color and scrap kind lists
   * on their own name `settings.production-reference-lists.manage` (company-wide, not Critical, granted to no role here).
   */
  productionReferenceLists: 'settings.production-reference-lists.manage',
} as const

/**
 * The exchange rate screen's command controls (provider 1.2 control capabilities under `exchangeRates`, each on the read
 * name plus its own command name). They gate buttons only, never a route; the server re-authorizes every command.
 * Reverification runs on the import name (the server's `SettingsExchangeRateEndpoints`).
 */
export const SETTINGS_EXCHANGE_RATE_CONTROL = {
  correct: 'settings.exchange-rates.correct',
  markHalfDay: 'settings.exchange-rates.half-day.mark',
  import: 'settings.exchange-rates.import',
} as const

export type SettingsCapabilityId = (typeof SETTINGS_CAPABILITY)[keyof typeof SETTINGS_CAPABILITY]

export const SETTINGS_MANUFACTURING_PROFILE_CONTROL = {
  parameters: { prepare: 'settings.manufacturing-parameter-profiles.prepare', publish: 'settings.manufacturing-parameter-profiles.publish' },
  tolerances: { prepare: 'settings.manufacturing-tolerance-profiles.prepare', publish: 'settings.manufacturing-tolerance-profiles.publish' },
} as const

export const SETTINGS_ROUTE: Record<SettingsCapabilityId, string> = {
  [SETTINGS_CAPABILITY.financeOpeningPeriod]: '/ayarlar/finans-acilis-donemi',
  [SETTINGS_CAPABILITY.manufacturingParameters]: '/ayarlar/uretim-parametre-profilleri',
  [SETTINGS_CAPABILITY.manufacturingTolerances]: '/ayarlar/uretim-tolerans-profilleri',
  [SETTINGS_CAPABILITY.salesDiscountLimits]: '/ayarlar/iskonto-limitleri',
  [SETTINGS_CAPABILITY.taxMaster]: '/ayarlar/vergi',
  [SETTINGS_CAPABILITY.exchangeRates]: '/ayarlar/kurlar',
  [SETTINGS_CAPABILITY.salesDeliveryTolerance]: '/ayarlar/teslim-toleransi',
  [SETTINGS_CAPABILITY.salesApprovalThresholds]: '/ayarlar/onay-esikleri',
  [SETTINGS_CAPABILITY.loginRateLimit]: '/ayarlar/giris-deneme-siniri',
  [SETTINGS_CAPABILITY.unitConversionPolicy]: '/ayarlar/birim-cevrim-politikasi',
  [SETTINGS_CAPABILITY.unitProfiles]: '/ayarlar/birim-profilleri',
  [SETTINGS_CAPABILITY.moneyProfiles]: '/ayarlar/para-profilleri',
  [SETTINGS_CAPABILITY.workCenterCapacity]: '/ayarlar/is-merkezi-kapasitesi',
  [SETTINGS_CAPABILITY.purchasingReferenceLists]: '/ayarlar/referans-listeleri',
  [SETTINGS_CAPABILITY.productionReferenceLists]: '/ayarlar/uretim-referans-listeleri',
}

export const SETTINGS_NAV_LABEL: Record<SettingsCapabilityId, string> = {
  [SETTINGS_CAPABILITY.financeOpeningPeriod]: 'Finans açılış dönemi',
  [SETTINGS_CAPABILITY.manufacturingParameters]: 'Üretim parametre profilleri',
  [SETTINGS_CAPABILITY.manufacturingTolerances]: 'Üretim tolerans profilleri',
  [SETTINGS_CAPABILITY.salesDiscountLimits]: 'İskonto limitleri',
  [SETTINGS_CAPABILITY.taxMaster]: 'Vergi (KDV / tevkifat)',
  [SETTINGS_CAPABILITY.exchangeRates]: 'Kurlar',
  [SETTINGS_CAPABILITY.salesDeliveryTolerance]: 'Teslim toleransı',
  [SETTINGS_CAPABILITY.salesApprovalThresholds]: 'Onay tutar eşikleri',
  [SETTINGS_CAPABILITY.loginRateLimit]: 'Giriş deneme sınırı',
  [SETTINGS_CAPABILITY.unitConversionPolicy]: 'Birim çevrim politikası',
  [SETTINGS_CAPABILITY.unitProfiles]: 'Birim profilleri',
  [SETTINGS_CAPABILITY.moneyProfiles]: 'Para profilleri',
  [SETTINGS_CAPABILITY.workCenterCapacity]: 'İş merkezi kapasitesi',
  [SETTINGS_CAPABILITY.purchasingReferenceLists]: 'Masraf ve hizmet tipleri',
  [SETTINGS_CAPABILITY.productionReferenceLists]: 'Boya rengi ve hurda türü',
}
