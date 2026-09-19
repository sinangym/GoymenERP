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
   * C-06 — the selected company's operational contact and address editor. Route
   * `/cari/:partyId/bilgiler`, route id `cari.contact-edit`.
   *
   * A SEPARATE grant from the detail, for the reason #182 made the detail separate
   * from the list: holding the read must not imply holding the write. Gating the
   * editor's route on it is also the only way the user is spared an editor whose Save
   * they are not permitted to use — a capability is UI visibility, and the API
   * re-authorizes `counterparty.parties.read` + `counterparty.parties.update.basic`
   * on every request regardless.
   *
   * Listing it here grants nothing; until the server publishes it the route mounts the
   * same neutral not-found block any unknown path gets, and calls nothing.
   */
  cariAccountsContactUpdate: 'cari.accounts.contact.update',
  /**
   * U-01 Ürünler listesi. Route `/urun`, owner CLAUDE-FRONTEND-URUN.
   *
   * Declared here by the shared-integration owner from that lane's handover
   * (`src/product/SHARED_MOUNT_HANDOFF.md`); the values, routes and labels are the ones
   * `src/product/capabilities.ts` declares at pin `8558ce5`, read from that source rather
   * than assumed, and a drift test pins the two together once the Product source is in
   * the same tree.
   *
   * WHAT LISTING IT DOES NOT DO, and it matters more here than for the Cari ids: listing an
   * id grants nothing. Since #316 the server's `ProductShellCapabilityProvider` publishes these
   * four, and the shell grants each one only from the actor's global Product authority (explicit
   * Product master assignments, never a company role). An actor without that authority gets
   * none of them: no mount and no nav entry, and `CapabilityRoute` closes an ungranted route onto
   * the same neutral not-found block any unknown path gets.
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
   * Its counterpart now EXISTS and was read from source: `PRODUCT_CAPABILITY.productsEdit`
   * at #238 pin `8558ce5`, with the same value, the same route and an empty nav label.
   */
  productItemsEdit: 'product.items.edit',
  /**
   * ORG-01 Ayarlar > Organizasyon — the selected company's facilities and their paged
   * locations. Route `/ayarlar/organizasyon`, owner CLAUDE-FRONTEND-ORGANIZASYON.
   *
   * Canonical AV-01/list; ORG-02 below is canonical AV-01/detail. No new catalog row
   * and no new screen count comes with them (CHATGPT-BUSINESS, same task).
   *
   * READ THE NAME CAREFULLY: the value is `organization.sites.list`, but the grant rule
   * behind it is NOT "holds sites.read". Backend #246 publishes this route capability on
   * `organization.locations.read` ALONE. That is the shape Business required in
   * https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5646417811 — when the
   * sites read is missing, the facility name is not fabricated AND the separately
   * authorized locations list is not blocked ("alan bazlı yetki yokluğu boş liste
   * değildir"). So the lane's own source comment, which proposed granting this on
   * sites.read + locations.read together, is SUPERSEDED by #246; the id value it
   * declares is unchanged and is what this file mirrors.
   *
   * The optional facility label is a separate CONTROL capability the server also
   * publishes (`organization.sites.read`, granted on locations.read + sites.read), and
   * the reasoned activate/deactivate inside ORG-02 is another
   * (`organization.locations.status`, on locations.read + status.update). Neither is a
   * route, so neither belongs in this registry — they are `hasCapability` checks inside
   * the lane's screens. Without the label capability that lane starts NO `/sites` GET at
   * all: there is no real 403 to suppress, and the global fail-closed boundary is
   * untouched.
   */
  organizationSitesList: 'organization.sites.list',
  /**
   * ORG-02 one location's card. Route `/ayarlar/organizasyon/lokasyon/:locationId`.
   *
   * A SEPARATE grant from the list for the reason #182 made the Cari detail separate,
   * even though #246 happens to publish both on `organization.locations.read` today:
   * that is a fact about who holds them, not an argument for one id.
   */
  organizationLocationsDetail: 'organization.locations.detail',
  /** S-02 sales order draft list. Route `/satis/siparisler`, owner the Sales lane (SİPARİŞ). */
  salesDraftsList: 'sales.drafts.list',
  /** S-04 Sales order 360, one draft. Route `/satis/siparisler/:draftId`. A SEPARATE grant from the list. */
  salesDraftsDetail: 'sales.drafts.detail',
  /** S-03 new sales order draft. Route `/satis/siparisler/yeni`. A SEPARATE grant from reading. */
  salesDraftsCreate: 'sales.drafts.create',
  /**
   * MFG-01 production order (technical sub-view, MFG-UI D1). Route `/uretim/emir/:orderId`.
   *
   * `manufacturing.orders.facts` is a CONTROL inside that screen (read with `hasCapability`,
   * the `organization.locations.status` precedent), not a route, so it is not in this table.
   */
  manufacturingOrdersDetail: 'manufacturing.orders.detail',
  /** F-02 manual-receipt draft list. Route `/finans/tahsilatlar`, owner the Finance lane (FİNANS). */
  financeReceiptsList: 'finance.receipts.list',
  /** F-02 new draft, entered from a Cari account (`?partyId&companyAccountId`). Route `/finans/tahsilatlar/yeni`. */
  financeReceiptsDraft: 'finance.receipts.draft',
  /** F-02 one draft. Route `/finans/tahsilatlar/:sourceId`. A SEPARATE grant from the list. */
  financeReceiptsDetail: 'finance.receipts.detail',
  /** F-04 bank-account master. Route `/finans/banka-hesaplari`. */
  financeBankAccountsList: 'finance.bank-accounts.list',
  /**
   * F-04 propose a bank account. Route `/finans/banka-hesaplari/yeni`.
   *
   * `finance.bank-accounts.approve` is deliberately NOT here: it is a control of the F-04 list (the
   * provider's `ParentCapabilityId`), not a route, so it belongs in no route registry. The list checks
   * it itself, and the server refuses a self-approval anyway.
   */
  financeBankAccountsPropose: 'finance.bank-accounts.propose',
} as const

export type CapabilityId = (typeof CAPABILITY)[keyof typeof CAPABILITY]

/** Route path per capability, mirroring the backend route declaration. */
export const CAPABILITY_ROUTE: Record<CapabilityId, string> = {
  [CAPABILITY.cariPartiesList]: '/cari',
  [CAPABILITY.cariPartiesCreate]: '/cari/yeni',
  [CAPABILITY.cariAccountsLink]: '/cari/sirkete-bagla',
  [CAPABILITY.cariPartiesDetail]: '/cari/:partyId',
  [CAPABILITY.cariAccountsContactUpdate]: '/cari/:partyId/bilgiler',
  [CAPABILITY.productItemsList]: '/urun',
  // Static before the parameter route, the same rule /cari/yeni follows: a deeper
  // static segment must never be readable as a record id.
  [CAPABILITY.productItemsCreate]: '/urun/yeni',
  // Longer and static-tailed first: 'duzenle' must never be read as a product id, the
  // same ordering the Cari routes follow. The Product lane declares them in this order
  // too (list, create, edit, detail).
  [CAPABILITY.productItemsEdit]: '/urun/:productId/duzenle',
  [CAPABILITY.productItemsDetail]: '/urun/:productId',
  [CAPABILITY.organizationSitesList]: '/ayarlar/organizasyon',
  // The two cannot collide whatever the order: the card carries a further STATIC
  // segment (`lokasyon`) ahead of its parameter, so `/ayarlar/organizasyon` can never
  // match the card and no static segment is readable as a location id — the #172 P1-2
  // rule the Cari and Ürün routes already follow. The Organization lane fixes that
  // shape in `organizationRoutes.tsx` rather than leaving it to this consumer.
  [CAPABILITY.organizationLocationsDetail]: '/ayarlar/organizasyon/lokasyon/:locationId',
  [CAPABILITY.salesDraftsList]: '/satis/siparisler',
  // Static before the parameter route, the /cari/yeni rule: 'yeni' must never be read as a draft id.
  [CAPABILITY.salesDraftsCreate]: '/satis/siparisler/yeni',
  [CAPABILITY.salesDraftsDetail]: '/satis/siparisler/:draftId',
  [CAPABILITY.manufacturingOrdersDetail]: '/uretim/emir/:orderId',
  [CAPABILITY.financeReceiptsList]: '/finans/tahsilatlar',
  // Static before the parameter route, the /cari/yeni rule: 'yeni' must never be read as a source id.
  [CAPABILITY.financeReceiptsDraft]: '/finans/tahsilatlar/yeni',
  [CAPABILITY.financeReceiptsDetail]: '/finans/tahsilatlar/:sourceId',
  [CAPABILITY.financeBankAccountsList]: '/finans/banka-hesaplari',
  [CAPABILITY.financeBankAccountsPropose]: '/finans/banka-hesaplari/yeni',
}

/** Nav label per capability. Only rendered when the server granted the capability. */
export const CAPABILITY_NAV_LABEL: Record<CapabilityId, string> = {
  [CAPABILITY.cariPartiesList]: 'Cari Kartlar',
  [CAPABILITY.cariPartiesCreate]: 'Yeni Cari',
  [CAPABILITY.cariAccountsLink]: 'Cariyi Şirkete Bağla',
  // No nav entry: the detail is reached from a row, never from the sidebar.
  [CAPABILITY.cariPartiesDetail]: '',
  // Nor this one: it is reached from the card of the record it edits.
  [CAPABILITY.cariAccountsContactUpdate]: '',
  [CAPABILITY.productItemsList]: 'Ürünler',
  [CAPABILITY.productItemsCreate]: 'Yeni Ürün',
  // Nor these two, for the same reason as the Cari detail: the 360 is reached from a row
  // and the editor from the card it edits, never from the sidebar. So `NAV_CAPABILITIES`
  // gains two Product entries when the routes are mounted, not four.
  [CAPABILITY.productItemsDetail]: '',
  [CAPABILITY.productItemsEdit]: '',
  [CAPABILITY.organizationSitesList]: 'Organizasyon',
  // Nor this one: the card is reached from a location row, never from the sidebar. So
  // `NAV_CAPABILITIES` gains exactly ONE Organization entry when the routes are mounted.
  [CAPABILITY.organizationLocationsDetail]: '',
  [CAPABILITY.salesDraftsList]: 'Sipariş Taslakları',
  [CAPABILITY.salesDraftsCreate]: 'Yeni Sipariş Taslağı',
  // Nor this one: the 360 is reached from a list row, never from the sidebar.
  [CAPABILITY.salesDraftsDetail]: '',
  // Nor this one: an order is opened by its id, never from the sidebar.
  [CAPABILITY.manufacturingOrdersDetail]: '',
  [CAPABILITY.financeReceiptsList]: 'Tahsilat Taslakları',
  [CAPABILITY.financeBankAccountsList]: 'Banka Hesapları',
  // None of these three: the new draft is entered from a Cari account, the detail from a list row and
  // the propose screen from the F-04 list, never from the sidebar.
  [CAPABILITY.financeReceiptsDraft]: '',
  [CAPABILITY.financeReceiptsDetail]: '',
  [CAPABILITY.financeBankAccountsPropose]: '',
}
