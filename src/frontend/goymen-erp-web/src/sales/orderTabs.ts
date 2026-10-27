/** S-04 Sipariş 360 locked tab keys (Master §7.2), kept apart from the component for fast refresh. */

/** Stable tab keys; the detail page keeps the active one in `?sekme=` so a tab can be linked and survives a reload. */
export const ORDER_TAB = {
  lines: 'satirlar',
  allocation: 'tahsis',
  realization: 'gerceklesme',
  invoices: 'faturalar',
  documents: 'belgeler',
  history: 'gecmis',
} as const
export type OrderTab = (typeof ORDER_TAB)[keyof typeof ORDER_TAB]

/** The locked order of the tabs. */
export const ORDER_TABS: readonly OrderTab[] = [
  ORDER_TAB.lines,
  ORDER_TAB.allocation,
  ORDER_TAB.realization,
  ORDER_TAB.invoices,
  ORDER_TAB.documents,
  ORDER_TAB.history,
]

/** An unknown or missing `?sekme=` value opens the first tab; it is never guessed from a near match. */
export function parseOrderTab(value: string | null): OrderTab {
  return ORDER_TABS.find((tab) => tab === value) ?? ORDER_TAB.lines
}
