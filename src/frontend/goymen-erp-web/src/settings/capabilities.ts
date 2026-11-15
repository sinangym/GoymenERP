/**
 * SAT-D61…D70 — the Ayarlar screen of the Sales discount limit groups, their limits and the person → group bindings.
 *
 * Mirrors `SettingsShellCapabilityProvider` exactly (provider 1.3: five route capabilities, each on its own server permission,
 * and the exchange rate screen's three command controls). The first,
 * `settings.sales-discount-limits.manage`, company-wide, in one company (not Critical; granted to no role — SAT-D65/D69,
 * a human gate). Listing the id here grants nothing: an ungranted capability closes the route onto the neutral
 * not-found block and draws no sidebar entry. Every request is re-authorized server-side regardless.
 */

export const SETTINGS_CAPABILITY = {
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

export const SETTINGS_ROUTE: Record<SettingsCapabilityId, string> = {
  [SETTINGS_CAPABILITY.salesDiscountLimits]: '/ayarlar/iskonto-limitleri',
  [SETTINGS_CAPABILITY.taxMaster]: '/ayarlar/vergi',
  [SETTINGS_CAPABILITY.exchangeRates]: '/ayarlar/kurlar',
  [SETTINGS_CAPABILITY.salesDeliveryTolerance]: '/ayarlar/teslim-toleransi',
  [SETTINGS_CAPABILITY.salesApprovalThresholds]: '/ayarlar/onay-esikleri',
}

export const SETTINGS_NAV_LABEL: Record<SettingsCapabilityId, string> = {
  [SETTINGS_CAPABILITY.salesDiscountLimits]: 'İskonto limitleri',
  [SETTINGS_CAPABILITY.taxMaster]: 'Vergi (KDV / tevkifat)',
  [SETTINGS_CAPABILITY.exchangeRates]: 'Kurlar',
  [SETTINGS_CAPABILITY.salesDeliveryTolerance]: 'Teslim toleransı',
  [SETTINGS_CAPABILITY.salesApprovalThresholds]: 'Onay tutar eşikleri',
}
