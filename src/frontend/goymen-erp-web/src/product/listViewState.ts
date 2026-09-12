/**
 * The U-01 list VIEW — the query inputs the user chose — kept across leaving the
 * screen and coming back to it.
 *
 * Same shape and same reasoning as the C-01 list view state, which was written
 * after a UX walkthrough found that opening a record and pressing Back threw away
 * the search term and every filter. It is a local module rather than an import
 * from `src/cari/**` because that lane has its own single writer.
 *
 * WHAT IS DELIBERATELY NOT KEPT:
 *
 *  - the ROWS. `CapabilityRoute` bumps the route-entry generation on every entry so
 *    a revisit cannot paint rows read under a grant the server may since have
 *    revoked. Restoring rows here would re-open that frame from the other side.
 *  - the CURSOR chain. §8.3 makes the cursor an opaque server token; replaying a
 *    remembered chain on return is a separate decision with its own failure modes,
 *    not a side effect of remembering a search box.
 *
 * SCOPE. One slot, keyed by the shell context namespace. A read under a different
 * namespace returns the defaults, so another actor's view is unreachable rather
 * than merely ignored. In memory only: a Product search term can name a real
 * customer's part, so it never reaches localStorage or a URL.
 */

import type { ProductRoleName } from './contract'
import type {
  CompletenessFilter,
  LifecycleFilter,
  ProductSortField,
  RoleMatch,
  SortDirection,
} from './api'

export interface ProductListView {
  readonly q: string
  readonly lifecycle: LifecycleFilter
  readonly family: string
  readonly roles: readonly ProductRoleName[]
  readonly roleMatch: RoleMatch
  readonly completeness: CompletenessFilter
  readonly sort: ProductSortField
  readonly direction: SortDirection
  readonly pageSize: number
}

/**
 * ASSUMPTION-PRD-001 (reversible, UX-level). The default lifecycle filter is
 * `all`, not `Active`.
 *
 * The C-01 list defaults to active counterparties, and the mirror-image choice was
 * tempting. It is wrong here: §6.1 makes every new Product a DRAFT, and the
 * operator who just created one arrives on this list to find it. An `Active`
 * default would hide their own new record behind a filter they did not set, on the
 * one screen where they are most likely to be looking for it.
 *
 * Recorded rather than asked because it changes only what an untouched list shows
 * and every state stays reachable from the filter (§8.1). If CHATGPT-BUSINESS
 * pins a different default in the U-01 query contract, this constant is the only
 * thing that changes.
 */
export const DEFAULT_PRODUCT_LIST_VIEW: ProductListView = {
  q: '',
  lifecycle: 'all',
  family: '',
  roles: [],
  roleMatch: 'any',
  completeness: 'all',
  sort: 'code',
  direction: 'asc',
  pageSize: 50,
}

/** The offered page sizes. A controlled set, not a free number the read must reject. */
export const PAGE_SIZE_OPTIONS: readonly number[] = [10, 25, 50, 100]

/**
 * Whether the view narrows the list.
 *
 * Sort is NOT a filter: it changes the order of the same records, never which
 * records. That is why a sorted-but-unfiltered empty list reads "no records"
 * rather than "nothing matched" — two different §13 states with two different
 * things for the user to do.
 */
export function isFiltered(view: ProductListView): boolean {
  return (
    view.q.trim().length > 0 ||
    view.lifecycle !== DEFAULT_PRODUCT_LIST_VIEW.lifecycle ||
    view.family.trim().length > 0 ||
    view.roles.length > 0 ||
    view.completeness !== DEFAULT_PRODUCT_LIST_VIEW.completeness
  )
}

/** Clears every filter and keeps the sort AND page size the user chose. */
export function clearedFilters(view: ProductListView): ProductListView {
  return {
    ...DEFAULT_PRODUCT_LIST_VIEW,
    sort: view.sort,
    direction: view.direction,
    pageSize: view.pageSize,
  }
}

let slot: { readonly namespace: string; readonly view: ProductListView } | null = null

/** The saved view for this namespace, or the defaults. Never another namespace's. */
export function readProductListView(namespace: string | null): ProductListView {
  if (namespace === null || slot === null || slot.namespace !== namespace) {
    return DEFAULT_PRODUCT_LIST_VIEW
  }
  return slot.view
}

/**
 * Saves the view for this namespace, replacing whatever was held before.
 *
 * A null namespace means the shell holds no bound context — mid-switch, for one.
 * Writing then would save under a name nothing can read back, or clobber the view
 * belonging to the context the user is about to return to. It is a no-op.
 */
export function writeProductListView(namespace: string | null, view: ProductListView): void {
  if (namespace === null) return
  slot = { namespace, view }
}

/**
 * The record the user opened FROM the list, held until they come back to it.
 *
 * Restoring the query without restoring the position leaves a keyboard user at the
 * top of the document after every record. Only the id is kept, and only until it is
 * read once: returning to the list is a one-time event, and a marker left behind
 * would move focus again on an unrelated later visit.
 */
let openedRecord: { readonly namespace: string; readonly productId: string } | null = null

export function rememberOpenedRecord(namespace: string | null, productId: string): void {
  if (namespace === null) return
  openedRecord = { namespace, productId }
}

/** The record to return to, cleared by the read. */
export function takeOpenedRecord(namespace: string | null): string | null {
  if (namespace === null || openedRecord === null || openedRecord.namespace !== namespace) {
    return null
  }
  const { productId } = openedRecord
  openedRecord = null
  return productId
}

/**
 * Drops the saved view. Tests call this between cases; the module slot outlives a
 * single render, which is the whole point of it.
 */
export function resetProductListView(): void {
  slot = null
  openedRecord = null
}
