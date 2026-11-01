/**
 * SAT-D61…D70 — the Ayarlar screen of the Sales discount limit groups, their limits and the person → group bindings.
 *
 * Mirrors `SettingsShellCapabilityProvider` exactly (provider 1.1: two capabilities, each on its own server permission). The first,
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
} as const

export type SettingsCapabilityId = (typeof SETTINGS_CAPABILITY)[keyof typeof SETTINGS_CAPABILITY]

export const SETTINGS_ROUTE: Record<SettingsCapabilityId, string> = {
  [SETTINGS_CAPABILITY.salesDiscountLimits]: '/ayarlar/iskonto-limitleri',
  [SETTINGS_CAPABILITY.taxMaster]: '/ayarlar/vergi',
}

export const SETTINGS_NAV_LABEL: Record<SettingsCapabilityId, string> = {
  [SETTINGS_CAPABILITY.salesDiscountLimits]: 'İskonto limitleri',
  [SETTINGS_CAPABILITY.taxMaster]: 'Vergi (KDV / tevkifat)',
}
