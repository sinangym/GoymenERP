/**
 * U-05 "Fiyatlar" capability ids and routes (AT-018). Kept apart from `PRODUCT_CAPABILITY` on purpose: those ids are the
 * GLOBAL Product master's (granted by the global Product authority), while a price list is COMPANY-scoped and granted by
 * the three company names `product.price-lists.read / write / publish` (the ids are not `product.*`: the shell registry
 * reserves that prefix for the global Product master). The server's shell registry publishes these ids
 * (the central step declares `ProductPriceListShellCapabilityProvider`); listing them here grants nothing, and until the
 * server publishes them `CapabilityRoute` fails closed onto the neutral not-found block.
 */
export const PRICE_LIST_CAPABILITY = {
  /** U-05 list. Route `/fiyat-listeleri`, read. */
  list: 'price-lists.list',
  /** U-05 new list. Route `/fiyat-listeleri/yeni`, write. */
  create: 'price-lists.create',
  /** U-05 one list with its versions. Route `/fiyat-listeleri/:priceListId`, read. */
  detail: 'price-lists.detail',
  /** Child control on the detail: revise the draft version / open a new version (write). */
  edit: 'price-lists.edit',
  /** Child control on the detail: publish the draft version (its own name). */
  publish: 'price-lists.publish',
} as const

export type PriceListCapabilityId = (typeof PRICE_LIST_CAPABILITY)[keyof typeof PRICE_LIST_CAPABILITY]

export const PRICE_LIST_ROUTE = {
  list: '/fiyat-listeleri',
  create: '/fiyat-listeleri/yeni',
  detail: '/fiyat-listeleri/:priceListId',
} as const

export function priceListDetailPath(priceListId: string): string {
  return `/fiyat-listeleri/${encodeURIComponent(priceListId)}`
}
