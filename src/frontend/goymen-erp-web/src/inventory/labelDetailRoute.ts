export const INVENTORY_LABEL_DETAIL_ROUTE = '/stok/etiket/:labelId' as const

export function inventoryLabelDetailPath(labelId: string): string {
  return `/stok/etiket/${encodeURIComponent(labelId)}`
}
