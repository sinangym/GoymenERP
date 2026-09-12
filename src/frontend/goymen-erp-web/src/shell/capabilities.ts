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
  /**
   * U-01 Ürünler listesi. Route `/urun`, owner CLAUDE-FRONTEND-URUN.
   *
   * Declared here by the shared-integration owner from that lane's handover
   * (`src/product/SHARED_MOUNT_HANDOFF.md`, PR #238 head `8689232`); the values are the
   * ones `src/product/capabilities.ts` declares, and a drift test pins the two together
   * once the Product source is in the same tree.
   *
   * WHAT LISTING IT DOES NOT DO, and it matters more here than for the Cari ids: the
   * server publishes no `product.*` capability at all yet. So these three grant nothing,
   * mount nothing and render no nav entry — `CapabilityRoute` closes an ungranted route
   * onto the same neutral not-found block any unknown path gets. That is the intended
   * state until Backend publishes the Product registry, not a gap.
   */
  productItemsList: 'product.items.list',
  /**
   * U-01 Ürün 360. Route `/urun/:productId`. A SEPARATE grant from the list, for the
   * reason #182/#172 made the Cari detail separate: holding the list must not imply
   * holding the card.
   */
  productItemsDetail: 'product.items.detail',
  /** U-02 Yeni Ürün — Draft creation only. Route `/urun/yeni`. */
  productItemsCreate: 'product.items.create',
  /**
   * U-02 basic edit of a created Draft. Route `/urun/:productId/duzenle`.
   *
   * A SEPARATE grant from the detail for the reason the Product lane gives and the Cari
   * ids already follow: reading a card and rewriting it are different authorities.
   * `PRD-PERM-001=A` happens to give both to `SYSTEM_ADMINISTRATOR` alone today, but that
   * is a fact about who holds them, not an argument for one id — the day read widens past
   * admin, a fused id would hand out the write with it.
   *
   * Pinned by its owner (Business §19.2 via #181 `3dfe58e`, relayed by the Product lane)
   * BEFORE its own symbol exists: at #238 head `8689232` that lane declares three ids, not
   * four. The value and the route below are the parts this file needs and they are fixed;
   * the counterpart property name is read from `src/product/capabilities.ts` when the
   * drift test lands with the follow-up commit, never assumed.
   */
  productItemsEdit: 'product.items.edit',
} as const

export type CapabilityId = (typeof CAPABILITY)[keyof typeof CAPABILITY]

/** Route path per capability, mirroring the backend route declaration. */
export const CAPABILITY_ROUTE: Record<CapabilityId, string> = {
  [CAPABILITY.cariPartiesList]: '/cari',
  [CAPABILITY.cariPartiesCreate]: '/cari/yeni',
  [CAPABILITY.cariAccountsLink]: '/cari/sirkete-bagla',
  [CAPABILITY.cariPartiesDetail]: '/cari/:partyId',
  [CAPABILITY.productItemsList]: '/urun',
  // Static before the parameter route, the same rule /cari/yeni follows: a deeper
  // static segment must never be readable as a record id.
  [CAPABILITY.productItemsCreate]: '/urun/yeni',
  // Longer and static-tailed first: 'duzenle' must never be read as a product id, the
  // same ordering the Cari routes follow.
  [CAPABILITY.productItemsEdit]: '/urun/:productId/duzenle',
  [CAPABILITY.productItemsDetail]: '/urun/:productId',
}

/** Nav label per capability. Only rendered when the server granted the capability. */
export const CAPABILITY_NAV_LABEL: Record<CapabilityId, string> = {
  [CAPABILITY.cariPartiesList]: 'Cari Kartlar',
  [CAPABILITY.cariPartiesCreate]: 'Yeni Cari',
  [CAPABILITY.cariAccountsLink]: 'Cariyi Şirkete Bağla',
  // No nav entry: the detail is reached from a row, never from the sidebar.
  [CAPABILITY.cariPartiesDetail]: '',
  [CAPABILITY.productItemsList]: 'Ürünler',
  [CAPABILITY.productItemsCreate]: 'Yeni Ürün',
  // Nor these two, for the same reason as the Cari detail: the 360 is reached from a row
  // and the editor from the card it edits, never from the sidebar. So `NAV_CAPABILITIES`
  // gains two entries when the routes are mounted, not four.
  [CAPABILITY.productItemsDetail]: '',
  [CAPABILITY.productItemsEdit]: '',
}
