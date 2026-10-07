/**
 * AT-002 — which shell capabilities the global search leans on. The search has NO capability or permission of its own:
 * the server searches a kind exactly when the actor holds the read its owner's list already measures, and the shell
 * mirrors that by showing the search only to an actor who holds at least one of those owners' LIST screens. A result
 * opens its owner's DETAIL screen only where the actor holds that screen; otherwise it is plain text. "More" goes to the
 * owner's list only where the list can continue the same search (Cari's `?q=`); otherwise the user is asked to narrow.
 */

import { CAPABILITY, CAPABILITY_ROUTE, type CapabilityId } from '../shell/capabilities'
import { inventoryLabelDetailPath } from '../inventory/labelDetailRoute'
import { SEARCH_KIND, SEARCH_QUERY_PARAM, type SearchKind } from './searchContract'

export const SEARCH_ROUTE = '/arama'

export interface SearchKindScreens {
  /** The owner's list screen: holding it is what makes the kind searchable in the shell. */
  readonly list: CapabilityId
  /** The owner's detail screen a result opens. */
  readonly detail: CapabilityId
  readonly detailPath: (id: string) => string
  /** The owner's list continuing the same search, when the list reads the term from its URL. */
  readonly morePath: ((term: string) => string) | null
}

const withId = (route: string, id: string) => route.replace(/:[A-Za-z]+$/, encodeURIComponent(id))

export const SEARCH_KIND_SCREENS: Readonly<Record<SearchKind, SearchKindScreens>> = {
  [SEARCH_KIND.party]: {
    list: CAPABILITY.cariPartiesList,
    detail: CAPABILITY.cariPartiesDetail,
    detailPath: (id) => withId(CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail], id),
    morePath: (term) => `${CAPABILITY_ROUTE[CAPABILITY.cariPartiesList]}?${new URLSearchParams({ [SEARCH_QUERY_PARAM]: term }).toString()}`,
  },
  [SEARCH_KIND.product]: {
    list: CAPABILITY.productItemsList,
    detail: CAPABILITY.productItemsDetail,
    detailPath: (id) => withId(CAPABILITY_ROUTE[CAPABILITY.productItemsDetail], id),
    morePath: null,
  },
  [SEARCH_KIND.salesOrder]: {
    list: CAPABILITY.salesDraftsList,
    detail: CAPABILITY.salesDraftsDetail,
    detailPath: (id) => withId(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail], id),
    morePath: null,
  },
  [SEARCH_KIND.materialLabel]: {
    // This existing capability requires both Inventory reads, exactly as the label search source does.
    list: CAPABILITY.inventoryStockPositionsList,
    detail: CAPABILITY.inventoryStockPositionsList,
    detailPath: inventoryLabelDetailPath,
    morePath: null,
  },
}

/** Every list capability that makes the search available, in kind order. */
export const SEARCHABLE_LIST_CAPABILITIES: readonly CapabilityId[] = Object.values(SEARCH_KIND_SCREENS).map((screens) => screens.list)

export const searchPath = (term: string) => `${SEARCH_ROUTE}?${new URLSearchParams({ [SEARCH_QUERY_PARAM]: term }).toString()}`
