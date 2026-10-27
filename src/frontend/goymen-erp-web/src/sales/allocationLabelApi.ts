import type { ViewOutcome } from '../shell/uiState'
import { INVENTORY_STOCK_POSITIONS_PATH, inventoryHeaders } from '../inventory/api'
import {
  decodeInventoryStockPositions,
  type InventoryStockPosition,
  type InventoryStockPositions,
} from '../inventory/contract'

/** The picker shows one bounded page; a fuller window asks the user to narrow the label-code search. */
export const ALLOCATION_LABEL_PICKER_LIMIT = 25

export interface AllocationLabelSearch {
  readonly companyId: string
  /** The sales order line's product: Inventory refuses an allocation whose label names another product. */
  readonly productId: string
  /** Label-code fragment; empty lists the product's stocked labels from the start of the code order. */
  readonly q: string
}

/**
 * UI-13 / MOD-12: the allocation panel's label picker reads Inventory's existing company-scoped ST-01
 * stock-position surface (`GET /api/v1/inventory/positions`, legacy shape, no display header). The route
 * demands `inventory.labels.read` + `inventory.balances.read`, which the physical-allocation capability
 * that mounts the panel already requires. It is a read only: Inventory re-validates the label, its version
 * and its product on the allocation command. A row of another company or another product fails closed.
 */
export async function searchAllocationLabels(
  search: AllocationLabelSearch,
  signal?: AbortSignal,
): Promise<ViewOutcome<InventoryStockPositions>> {
  const query = new URLSearchParams({ limit: String(ALLOCATION_LABEL_PICKER_LIMIT), productId: search.productId })
  const q = search.q.trim()
  if (q !== '') query.set('q', q)
  let response: Response
  try {
    response = await fetch(`${INVENTORY_STOCK_POSITIONS_PATH}?${query.toString()}`, {
      headers: inventoryHeaders(search.companyId), cache: 'no-store', signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const decoded = decodeInventoryStockPositions(body)
  if (decoded === null || decoded.items.some((item) =>
    item.companyId.toLowerCase() !== search.companyId.toLowerCase()
    || item.productId.toLowerCase() !== search.productId.toLowerCase())) return { type: 'malformed' }
  return { type: 'ok-single', data: decoded }
}

/** Why Inventory would refuse a sales allocation of this label as ST-01 reports it now (SAT-D20 (1)). */
export type AllocationLabelBlocker = 'notCompanyOwnedAndHeld' | 'noFreeStock' | 'fullyAllocated'

/**
 * SAT-D20 (1): a VIEW-ONLY mirror of the refusals Inventory's allocation create applies to the row it was just read
 * from (`InventoryStore.CreateAsync`): the label needs a positive Free balance (`insufficient_free_stock`), that
 * position must be owned AND held by this company (`InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld`,
 * `owner_not_company`), and some Free kilograms must be left after the active allocations
 * (`insufficient_free_stock`). It reads only what the server reported and decides nothing new: Inventory re-applies
 * every rule (and the label version) on the command, so a row shown selectable can still be refused, and that refusal
 * is what the panel shows. An empty list means "selectable".
 */
export function allocationLabelBlockers(item: InventoryStockPosition, companyId: string): readonly AllocationLabelBlocker[] {
  const company = companyId.toLowerCase()
  const blockers: AllocationLabelBlocker[] = []
  if (item.ownerKind !== 'Company' || item.ownerId.toLowerCase() !== company
    || item.custodianKind !== 'Company' || item.custodianId.toLowerCase() !== company) {
    blockers.push('notCompanyOwnedAndHeld')
  }
  if (!isPositiveKg(item.freePhysicalKg)) blockers.push('noFreeStock')
  else if (!isPositiveKg(item.availableKg)) blockers.push('fullyAllocated')
  return blockers
}

/** SAT-D20 (3): the picker's default filter hides a label whose available kilograms are zero. */
export function hasAvailableKg(item: InventoryStockPosition): boolean {
  return isPositiveKg(item.availableKg)
}

/** The decoder already proved a non-negative canonical decimal; any non-zero digit makes it positive. */
function isPositiveKg(value: string): boolean {
  return /[1-9]/.test(value)
}
