/**
 * The U-01 list VIEW — the query inputs the user chose — kept across leaving the
 * screen and coming back to it.
 *
 * Same reasoning as the C-01 list view state, written after a walkthrough found
 * that opening a record and pressing Back threw away the search and every filter.
 * A local module rather than an import from `src/cari/**`, which has its own writer.
 *
 * WHAT IS DELIBERATELY NOT KEPT:
 *  - the ROWS. `CapabilityRoute` bumps the route-entry generation on every entry so
 *    a revisit cannot paint rows read under a grant the server may since have
 *    revoked. Restoring rows would re-open that frame from the other side.
 *  - the CURSOR chain. It is an opaque server token; replaying a remembered chain
 *    on return is a separate decision with its own failure modes, not a side effect
 *    of remembering a search box.
 *
 * In memory only: a Product search term can name a real customer's part, so it
 * never reaches localStorage and never a URL.
 */

import type { ProductRoleName } from './contract'
import type { LifecycleFilter } from './api'

export interface ProductListView {
  readonly q: string
  readonly lifecycle: LifecycleFilter
  /** ONE role, or `''` for no role filter — the query takes a single value. */
  readonly role: ProductRoleName | ''
  readonly pageSize: number
}

/**
 * ASSUMPTION-PRD-001 (reversible, UX-level). The default lifecycle filter is `all`,
 * not `Active`.
 *
 * The C-01 list defaults to active counterparties and the mirror-image choice was
 * tempting. It is wrong here: every new Product is born a DRAFT, and the operator
 * who just created one arrives on this list to find it. An `Active` default would
 * hide their own new record behind a filter they did not set.
 *
 * `pageSize` 50 is the contract's own default (`ProductListQuery.PageSize = 50`).
 */
export const DEFAULT_PRODUCT_LIST_VIEW: ProductListView = {
  q: '',
  lifecycle: 'all',
  role: '',
  pageSize: 50,
}

/** The offered page sizes, inside the endpoint's 1..100 bound. */
export const PAGE_SIZE_OPTIONS: readonly number[] = [10, 25, 50, 100]

/**
 * Whether the view narrows the list.
 *
 * `pageSize` is not a filter: it changes how many of the same matching records are
 * shown, never which match. That is why a page-size change leaves an empty result
 * reading "no records" rather than "nothing matched".
 */
export function isFiltered(view: ProductListView): boolean {
  return (
    view.q.trim().length > 0 ||
    view.lifecycle !== DEFAULT_PRODUCT_LIST_VIEW.lifecycle ||
    view.role !== DEFAULT_PRODUCT_LIST_VIEW.role
  )
}

/** Clears every filter and keeps the page size the user chose. */
export function clearedFilters(view: ProductListView): ProductListView {
  return { ...DEFAULT_PRODUCT_LIST_VIEW, pageSize: view.pageSize }
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

/** Drops the saved view. Tests call this between cases; the slot outlives a render. */
export function resetProductListView(): void {
  slot = null
  openedRecord = null
}
