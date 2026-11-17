export const INVENTORY_OPENING_CAPABILITY = 'inventory.opening-stock.record' as const
export const INVENTORY_STOCK_POSITIONS_CAPABILITY = 'inventory.stock-positions.list' as const
export const INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY = 'inventory.physical-allocation.write' as const
export const INVENTORY_FUTURE_ALLOCATION_READ_CAPABILITY = 'inventory.future-allocation.read' as const
export const INVENTORY_FUTURE_ALLOCATION_WRITE_CAPABILITY = 'inventory.future-allocation.write' as const
export const INVENTORY_FUTURE_ALLOCATION_ROUTE = '/stok/satis-siparisleri/:salesOrderId/satirlar/:salesOrderLineId/gelecek-ayirmalar' as const
export const inventoryFutureAllocationPath = (salesOrderId: string, salesOrderLineId: string) =>
  INVENTORY_FUTURE_ALLOCATION_ROUTE.replace(':salesOrderId', encodeURIComponent(salesOrderId)).replace(':salesOrderLineId', encodeURIComponent(salesOrderLineId))
/**
 * R19 split/merge: a CONTROL of the ST-02 label detail (no route of its own), shown only when the server
 * advertises it. The backend re-measures `inventory.label-transformation.record` on every command.
 */
export const INVENTORY_LABEL_TRANSFORMATION_CAPABILITY = 'inventory.label-transformation.record' as const
/** Central authority catalogue owns registration/grants; this module only consumes the advertised capability. */
export const INVENTORY_LABEL_CLASSIFICATION_WRITE_CAPABILITY = 'inventory.label-classification.write' as const
/**
 * ST-05 / AT-037 stock count: CONTROLS of the ST-02 label detail, shown only when the server advertises them.
 * Recording a count and deciding its difference are separate names; the backend re-measures each on every command.
 */
export const INVENTORY_STOCK_COUNT_RECORD_CAPABILITY = 'inventory.stock-count.record' as const
export const INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY = 'inventory.stock-count.approve' as const
/**
 * ST-02 scrap write-off: CONTROLS of the ST-02 label detail, shown only when the server advertises them. Recording a
 * scrap and deciding it are separate names; the backend re-measures each on every command.
 */
export const INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY = 'inventory.stock-scrap.record' as const
export const INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY = 'inventory.stock-scrap.approve' as const

export const INVENTORY_STOCK_POSITIONS_ROUTE = '/stok/pozisyonlar' as const
export const INVENTORY_TRANSFERS_ROUTE = '/stok/transferler' as const
export const INVENTORY_TRANSFER_PLANNING_ROUTE = '/stok/transferler/planla' as const
export const INVENTORY_TRANSFER_PLANNING_CAPABILITY = 'inventory.stock-transfer.write' as const
export const INVENTORY_TRANSFER_CANCEL_CAPABILITY = 'inventory.stock-transfer.cancel' as const

/**
 * ST-04 company movement timeline. Reached from a label detail, never the sidebar; it rides the existing
 * `inventory.stock-positions.list` capability (labels + balances read, company-wide) and adds no name of its own. The
 * API re-measures both reads on every page.
 */
export const INVENTORY_MOVEMENT_TIMELINE_ROUTE = '/stok/hareketler' as const

/**
 * KLN-D42 dated shortage warning (it replaces minimum stock). Reached from the stock positions page; it rides the
 * existing `inventory.stock-positions.list` capability (labels + balances read, company-wide) and adds no name of its
 * own. The API re-measures both reads on every request.
 */
export const INVENTORY_SHORTAGE_WARNINGS_ROUTE = '/stok/eksik-uyarilari' as const
/** SAT-D337: existing stock-report capability; no new permission or capability name. */
export const INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE = '/stok/eksi-stok-denemeleri' as const
export function inventoryNegativeStockAttemptPath(productId: string | null = null, locationId: string | null = null): string {
  const search = new URLSearchParams()
  if (productId !== null) search.set('productId', productId)
  if (locationId !== null) search.set('locationId', locationId)
  const suffix = search.toString()
  return suffix === '' ? INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE : `${INVENTORY_NEGATIVE_STOCK_ATTEMPTS_ROUTE}?${suffix}`
}

/**
 * STK-S33 (KLN-D44, KLN-D41) cross-module label trace and label search. Reached from a label detail, never the sidebar; it
 * rides the existing `inventory.stock-positions.list` capability (labels + balances read, the label-detail permissions)
 * and adds no name of its own. The API re-measures both reads on every request.
 */
export const INVENTORY_LABEL_TRACE_ROUTE = '/stok/etiket-izi' as const
/** URL search parameters of the trace route: the label identity and the direction (`ileri` / `geri`). */
export const LABEL_TRACE_LABEL_PARAM = 'etiket' as const
export const LABEL_TRACE_DIRECTION_PARAM = 'yon' as const
export const LABEL_TRACE_DIRECTION_PARAM_VALUE = { forward: 'ileri', backward: 'geri' } as const

export function inventoryLabelTracePath(labelId: string, direction: 'forward' | 'backward'): string {
  const query = new URLSearchParams({
    [LABEL_TRACE_LABEL_PARAM]: labelId,
    [LABEL_TRACE_DIRECTION_PARAM]: LABEL_TRACE_DIRECTION_PARAM_VALUE[direction],
  })
  return `${INVENTORY_LABEL_TRACE_ROUTE}?${query.toString()}`
}

/** Router state a label detail hands the timeline so its return link reopens the same label (and its ST-01 filter). */
export interface MovementTimelineEntryState {
  readonly movementTimelineFromLabelId: string
  readonly stockPositionsSearch?: string
}

export function movementTimelineEntryState(labelId: string, stockSearch: string | null): MovementTimelineEntryState {
  return stockSearch === null
    ? { movementTimelineFromLabelId: labelId }
    : { movementTimelineFromLabelId: labelId, stockPositionsSearch: stockSearch }
}

export const INVENTORY_OPENING_ROUTE = {
  home: '/stok/acilis',
  create: '/stok/acilis/yeni',
  detail: '/stok/acilis/:countId',
} as const

export function inventoryOpeningDetailPath(countId: string): string {
  return `/stok/acilis/${encodeURIComponent(countId)}`
}
