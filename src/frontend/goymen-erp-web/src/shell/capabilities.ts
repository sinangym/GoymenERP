/**
 * Server-published capability ids the shell knows how to mount.
 *
 * These MUST match the accepted backend registry exactly (Codex PR #149,
 * `CounterpartyShellCapabilityProvider`, sourced from `CARI_UI_VERTICAL_SLICE_v1.json`).
 * A drift here means the shell silently stops mounting a granted feature, so the ids
 * are asserted against the backend provider constants in a test.
 *
 * Listing an id here grants nothing. The server decides availability; this is only the
 * set the frontend has an implementation for.
 */

export const CAPABILITY = {
  /** C-01 Cari Kartlar list. Route `/cari`, owner CHATGPT-BUSINESS/CARI. */
  cariPartiesList: 'cari.parties.list',
  /**
   * C-02 Yeni Cari — the create subset (`scopeVariant=create-subset` is route
   * metadata, not part of the screen id). Route `/cari/yeni`, permission
   * `counterparty.parties.create`, single-company context only.
   */
  cariPartiesCreate: 'cari.parties.create',
  /**
   * CARI-LINK-WORKSPACE-001 — link an existing Party to the TARGET company.
   * Route `/cari/sirkete-bagla`, permissions `counterparty.accounts.create` +
   * `counterparty.parties.read` company-wide in the target, single-company only.
   */
  cariAccountsLink: 'cari.accounts.link',
  /**
   * C-03 Cari 360 detail. Route `/cari/:partyId`, same owner.
   *
   * Declared by the server as a SEPARATE grant (#182), so holding the list does
   * not imply holding the detail. An earlier version of the C-03 screen mounted
   * under the list capability — which meant a list-only snapshot could open the
   * detail and call its API. Codex caught that on #172.
   */
  cariPartiesDetail: 'cari.parties.detail',
} as const

export type CapabilityId = (typeof CAPABILITY)[keyof typeof CAPABILITY]

/** Route path per capability, mirroring the backend route declaration. */
export const CAPABILITY_ROUTE: Record<CapabilityId, string> = {
  [CAPABILITY.cariPartiesList]: '/cari',
  [CAPABILITY.cariPartiesCreate]: '/cari/yeni',
  [CAPABILITY.cariAccountsLink]: '/cari/sirkete-bagla',
  [CAPABILITY.cariPartiesDetail]: '/cari/:partyId',
}

/** Nav label per capability. Only rendered when the server granted the capability. */
export const CAPABILITY_NAV_LABEL: Record<CapabilityId, string> = {
  [CAPABILITY.cariPartiesList]: 'Cari Kartlar',
  [CAPABILITY.cariPartiesCreate]: 'Yeni Cari',
  [CAPABILITY.cariAccountsLink]: 'Cariyi Şirkete Bağla',
  // No nav entry: the detail is reached from a row, never from the sidebar.
  [CAPABILITY.cariPartiesDetail]: '',
}
