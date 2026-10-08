import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeInventoryMovementTimelinePage, type InventoryMovementTimelinePage } from './movementTimelineContract'

/** ST-04 owner read. There is no other source: a failure here is shown, never answered from a different endpoint. */
export const INVENTORY_MOVEMENT_TIMELINE_PATH = `${API_BASE}/inventory/movements`

/**
 * Rows asked per page. Every page — the first and each continuation — sends this same limit, because the owner binds
 * a cursor to the limit it was issued for. Within the owner's 1..100 bound; a display choice, not a business rule.
 */
export const MOVEMENT_TIMELINE_PAGE_SIZE = 25

/** The owner's closed problem code for a malformed query, an expired cursor or a cursor not issued to this caller. */
export const MOVEMENT_TIMELINE_INVALID_QUERY = 'inventory_movement_timeline_invalid_query'

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

export function movementTimelineProblemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

export async function fetchInventoryMovementTimeline(
  companyId: string,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<InventoryMovementTimelinePage>> {
  const query = new URLSearchParams({ limit: String(MOVEMENT_TIMELINE_PAGE_SIZE) })
  if (cursor !== null) query.set('cursor', cursor)
  try {
    const response = await fetch(`${INVENTORY_MOVEMENT_TIMELINE_PATH}?${query.toString()}`, {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const page = decodeInventoryMovementTimelinePage(body, MOVEMENT_TIMELINE_PAGE_SIZE)
    return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
