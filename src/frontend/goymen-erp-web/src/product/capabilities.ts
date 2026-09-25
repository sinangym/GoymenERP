/**
 * Product capability ids, routes and nav labels.
 *
 * ┌─ WHY THESE LIVE HERE AND NOT IN `src/shell/capabilities.ts` ───────────────┐
 * │ `src/shell/**` has a single writer (the Cari frontend session), and PR #235 │
 * │ is editing `capabilities.ts` and `router.tsx` right now. So the Product     │
 * │ lane declares its own ids here and hands the shell owner a ready delta —    │
 * │ see ./SHARED_MOUNT_HANDOFF.md for the exact file + symbol + source.         │
 * └────────────────────────────────────────────────────────────────────────────┘
 *
 * PROPOSED_JIT, same status as ./contract.ts. `ERP-000K_SHELL_CONTRACT` makes the
 * capability registry SERVER-owned: listing an id here grants nothing, and the
 * backend registry is the only place a capability can actually come from. The
 * accepted registry publishes no `product.*` id today, which is exactly why the
 * screens below mount nothing in the running app — `CapabilityRoute` fails closed
 * onto the neutral not-found block. That is the intended state until
 * CODEX-BACKEND-URUN publishes the real ids; it is not a bug to route around.
 */

/**
 * The Product capability ids the frontend has an implementation for.
 *
 * Split the same way the Cari ids are, and for the same reason Codex caught on
 * #172: a list grant must not imply a detail grant or a create grant. `PRD-PERM-001=A`
 * currently gives every Product action to exact `SYSTEM_ADMINISTRATOR` only, but
 * that is a decision about WHO holds them, not a reason to collapse three grants
 * into one — the day read is widened past admin, a single id would hand out create
 * with it.
 */
export const PRODUCT_CAPABILITY = {
  /** U-01 Ürünler listesi. Route `/urun`. */
  productsList: 'product.items.list',
  /** U-01 Ürün 360 detayı. Route `/urun/:productId`. */
  productsDetail: 'product.items.detail',
  /** U-02 Yeni ürün — Draft oluşturma. Route `/urun/yeni`. */
  productsCreate: 'product.items.create',
  /**
   * U-02 temel düzenleme — Draft basic edit. Route `/urun/:productId/duzenle`.
   *
   * A SEPARATE grant from create, and from detail. §19.2 makes basic edit part of
   * the first slice, but reading a card and rewriting it are different authorities:
   * `PRD-PERM-001=A` happens to give both to `SYSTEM_ADMINISTRATOR` today, which is
   * a fact about who holds them, not a reason to fuse them. The day read widens
   * past admin, a fused id would hand out write with it.
   */
  productsEdit: 'product.items.edit',
} as const

export type ProductCapabilityId = (typeof PRODUCT_CAPABILITY)[keyof typeof PRODUCT_CAPABILITY]

/**
 * Route path per capability.
 *
 * `/urun/yeni` is declared BEFORE `/urun/:productId` in ./productRoutes.tsx so the
 * static path can never be read as a product id — the same ordering bug Codex found
 * on the Cari routes (#172 P1-2), where a create URL mounted the detail screen and
 * issued a read for a record that cannot exist.
 */
export const PRODUCT_CAPABILITY_ROUTE: Record<ProductCapabilityId, string> = {
  [PRODUCT_CAPABILITY.productsList]: '/urun',
  [PRODUCT_CAPABILITY.productsCreate]: '/urun/yeni',
  [PRODUCT_CAPABILITY.productsDetail]: '/urun/:productId',
  [PRODUCT_CAPABILITY.productsEdit]: '/urun/:productId/duzenle',
}

/** Nav label per capability. Only rendered when the server granted the capability. */
export const PRODUCT_CAPABILITY_NAV_LABEL: Record<ProductCapabilityId, string> = {
  [PRODUCT_CAPABILITY.productsList]: 'Ürünler',
  // K5: create is reached through the list's single action, never from the sidebar.
  [PRODUCT_CAPABILITY.productsCreate]: '',
  // No nav entry: the 360 is reached from a row, never from the sidebar.
  [PRODUCT_CAPABILITY.productsDetail]: '',
  // Nor the edit: it is reached from the card it edits.
  [PRODUCT_CAPABILITY.productsEdit]: '',
}

/** Build the `/urun/:productId` path for a concrete product. */
export function productDetailPath(productId: string): string {
  return `/urun/${encodeURIComponent(productId)}`
}

/** Build the `/urun/:productId/duzenle` path for a concrete product. */
export function productEditPath(productId: string): string {
  return `/urun/${encodeURIComponent(productId)}/duzenle`
}
