export const INVENTORY_OPENING_CAPABILITY = 'inventory.opening-stock.record' as const

export const INVENTORY_OPENING_ROUTE = {
  home: '/stok/acilis',
  create: '/stok/acilis/yeni',
  detail: '/stok/acilis/:countId',
} as const

export function inventoryOpeningDetailPath(countId: string): string {
  return `/stok/acilis/${encodeURIComponent(countId)}`
}
