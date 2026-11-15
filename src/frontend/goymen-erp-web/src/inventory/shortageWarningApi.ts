import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeShortageWarnings, type ShortageWarnings } from './shortageWarningContract'

/** KLN-D42 owner read. There is no other source: a failure here is shown, never answered from another endpoint. */
export const INVENTORY_SHORTAGE_WARNINGS_PATH = `${API_BASE}/inventory/shortage-warnings`

/** The owner's closed problem code for a malformed query. */
export const SHORTAGE_WARNING_INVALID_QUERY = 'inventory_shortage_warning_invalid_query'

/** What the screen asks: every product with open demand, or only those with a warning (the owner's default). */
export interface ShortageWarningQuery {
  readonly onlyWarnings: boolean
}

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

export function shortageWarningProblemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

export async function fetchInventoryShortageWarnings(
  companyId: string,
  query: ShortageWarningQuery,
  signal?: AbortSignal,
): Promise<ViewOutcome<ShortageWarnings>> {
  const search = new URLSearchParams({ onlyWarnings: query.onlyWarnings ? 'true' : 'false' })
  try {
    const response = await fetch(`${INVENTORY_SHORTAGE_WARNINGS_PATH}?${search.toString()}`, {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const warnings = decodeShortageWarnings(body, companyId)
    return warnings === null ? { type: 'malformed' } : { type: 'ok-single', data: warnings }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
