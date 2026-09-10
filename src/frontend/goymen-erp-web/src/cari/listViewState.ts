/**
 * The C-01 list VIEW — the query inputs the user chose — kept across leaving the
 * screen and coming back to it.
 *
 * WHY THIS EXISTS. `/cari` → open a counterparty → Back unmounts the list, so the
 * search term and every filter were thrown away and the user landed on an unfiltered
 * first page. Re-typing the search after each record is the single most repeated
 * complaint in the UX-CARI-001 walkthrough.
 *
 * WHAT IS DELIBERATELY NOT KEPT, and why:
 *
 *  - the ROWS. `CapabilityRoute` bumps the route-entry generation on every entry
 *    precisely so a revisit cannot paint rows that were read under a grant the
 *    server may since have revoked (#146/#148). Restoring rows here would re-open
 *    that exact frame from the other side.
 *  - the CURSOR chain. A cursor is an opaque server token bound to one actor,
 *    company, query and snapshot. Replaying a remembered chain on return is a
 *    separate decision with its own failure modes (N reads per entry, and a stale
 *    token surfacing as `invalid_cursor`), not a side effect of remembering a
 *    search box.
 *
 * So the user's QUERY is re-applied and the DATA is re-read, freshly authorized.
 * "Where I was looking" comes back; "what I was shown" is fetched again.
 *
 * SCOPE. One slot, keyed by the shell context namespace (`contextKeyString`: actor
 * + scope mode + company + capability generation + committed revision). A read
 * under a different namespace returns the defaults, so another actor's or another
 * company's view is not merely ignored — it is unreachable. Writing under a new
 * namespace drops the previous entry, which bounds what is retained to the one view
 * currently in use. In memory only: a search term never reaches localStorage, and
 * never a URL either — it can name a real counterparty.
 */

import type {
  AccountStateFilter,
  KindFilter,
  PartyStateFilter,
  RoleFilter,
  RoleMatch,
  SortDirection,
  SortField,
} from './api'

export interface CariListView {
  readonly q: string
  readonly kind: KindFilter | ''
  readonly roles: readonly RoleFilter[]
  readonly roleMatch: RoleMatch
  readonly partyState: PartyStateFilter
  readonly accountState: AccountStateFilter
  readonly sort: SortField
  readonly direction: SortDirection
  /** How many rows one read returns. See `PAGE_SIZE_OPTIONS`. */
  readonly pageSize: number
}

/**
 * NEEDS-SINAN (via ChatGPT C-01 spec): the default party-state filter. `active` is
 * used here because DEC-CAR-003 keeps used records visible but inactive, and an
 * operator listing counterparties normally works with active ones. Passives remain
 * reachable via the filter (AT-013).
 *
 * The account-state default is the contract's own: the company-account state is
 * filtered independently of the Party.
 */
export const DEFAULT_CARI_LIST_VIEW: CariListView = {
  q: '',
  kind: '',
  roles: [],
  roleMatch: 'any',
  partyState: 'active',
  accountState: 'all',
  sort: 'code',
  direction: 'asc',
  pageSize: 50,
}

/**
 * The offered page sizes (UX review §8.7/65).
 *
 * A controlled set rather than a free number: the server bounds `pageSize` at 200,
 * and a text field would invite a value the read has to reject. 50 stays the
 * default — it is what the screen shipped with, and changing what an untouched
 * list asks for is a separate decision from making the size choosable.
 */
export const PAGE_SIZE_OPTIONS: readonly number[] = [10, 25, 50, 100]

/**
 * Whether the view narrows the list.
 *
 * Sort is NOT a filter: it changes the order of the same records, never which
 * records. That is why clearing filters leaves the chosen sort alone, and why a
 * sorted-but-unfiltered empty list still reads "no records" rather than
 * "nothing matched".
 */
export function isFiltered(view: CariListView): boolean {
  return (
    view.q.trim().length > 0 ||
    view.kind !== '' ||
    view.roles.length > 0 ||
    view.partyState !== DEFAULT_CARI_LIST_VIEW.partyState ||
    view.accountState !== DEFAULT_CARI_LIST_VIEW.accountState
  )
}

/**
 * Clears every filter and keeps the sort AND page size the user chose.
 *
 * Neither is a filter: both change how the same matching records are presented,
 * never which records match. Resetting them would undo a choice the user did not
 * ask to undo.
 */
export function clearedFilters(view: CariListView): CariListView {
  return {
    ...DEFAULT_CARI_LIST_VIEW,
    sort: view.sort,
    direction: view.direction,
    pageSize: view.pageSize,
  }
}

let slot: { readonly namespace: string; readonly view: CariListView } | null = null

/** The saved view for this namespace, or the defaults. Never another namespace's. */
export function readCariListView(namespace: string | null): CariListView {
  if (namespace === null || slot === null || slot.namespace !== namespace) {
    return DEFAULT_CARI_LIST_VIEW
  }
  return slot.view
}

/**
 * Saves the view for this namespace, replacing whatever was held before.
 *
 * A null namespace means the shell holds no bound context — mid-switch, for one.
 * Writing then would either save under a name nothing can read back, or clobber the
 * view belonging to the context the user is about to return to. It is a no-op.
 */
export function writeCariListView(namespace: string | null, view: CariListView): void {
  if (namespace === null) return
  slot = { namespace, view }
}

/**
 * Drops the saved view. Tests call this between cases; the module slot outlives a
 * single render, which is the whole point of it, so it has to be cleared explicitly.
 */
export function resetCariListView(): void {
  slot = null
}
