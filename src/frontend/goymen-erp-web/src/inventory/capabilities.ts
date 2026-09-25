export const INVENTORY_OPENING_CAPABILITY = 'inventory.opening-stock.record' as const
export const INVENTORY_STOCK_POSITIONS_CAPABILITY = 'inventory.stock-positions.list' as const
export const INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY = 'inventory.physical-allocation.write' as const
/**
 * R19 split/merge: a CONTROL of the ST-02 label detail (no route of its own), shown only when the server
 * advertises it. The backend re-measures `inventory.label-transformation.record` on every command.
 */
export const INVENTORY_LABEL_TRANSFORMATION_CAPABILITY = 'inventory.label-transformation.record' as const

export const INVENTORY_STOCK_POSITIONS_ROUTE = '/stok/pozisyonlar' as const

export const INVENTORY_OPENING_ROUTE = {
  home: '/stok/acilis',
  create: '/stok/acilis/yeni',
  detail: '/stok/acilis/:countId',
} as const

export function inventoryOpeningDetailPath(countId: string): string {
  return `/stok/acilis/${encodeURIComponent(countId)}`
}
