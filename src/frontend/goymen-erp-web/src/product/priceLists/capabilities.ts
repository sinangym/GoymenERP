/**
 * U-05 "Fiyatlar" capability ids and routes (AT-018). Kept apart from `PRODUCT_CAPABILITY` on purpose: those ids are the
 * GLOBAL Product master's (granted by the global Product authority), while a price list is COMPANY-scoped and granted by
 * the three company names `product.price-lists.read / write / publish`. The server's shell registry publishes these ids
 * (the central step declares `ProductPriceListShellCapabilityProvider`); listing them here grants nothing, and until the
 * server publishes them `CapabilityRoute` fails closed onto the neutral not-found block.
 */
export const PRICE_LIST_CAPABILITY = {
  /** U-05 list. Route `/fiyat-listeleri`, read. */
  list: 'product.price-lists.list',
  /** U-05 new list. Route `/fiyat-listeleri/yeni`, write. */
  create: 'product.price-lists.create',
  /** U-05 one list with its versions. Route `/fiyat-listeleri/:priceListId`, read. */
  detail: 'product.price-lists.detail',
  /** Child control on the detail: revise the draft version / open a new version (write). */
  edit: 'product.price-lists.edit',
  /** Child control on the detail: publish the draft version (its own name). */
  publish: 'product.price-lists.publish',
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
