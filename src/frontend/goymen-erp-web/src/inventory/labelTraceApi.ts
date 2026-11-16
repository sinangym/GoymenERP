import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import {
  decodeInventoryLabelTrace,
  decodeInventoryLabelTraceSearch,
  type InventoryLabelTrace,
  type InventoryLabelTraceSearch,
  type LabelTraceDirection,
} from './labelTraceContract'

/**
 * STK-S33 owner reads. Both ride the label-detail permissions (KLN-D44); there is no other source, so a failure here is
 * shown, never answered from a different endpoint.
 */
export const INVENTORY_LABEL_TRACE_SEARCH_PATH = `${API_BASE}/inventory/label-trace/search`

export function inventoryLabelTracePathOf(labelId: string): string {
  return `${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/trace`
}

/** The owner's closed problem code for an unknown, repeated or out-of-bounds query parameter. */
export const LABEL_TRACE_INVALID_QUERY = 'inventory_label_trace_invalid_query'

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

export function labelTraceProblemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

async function read<T>(
  url: string,
  companyId: string,
  decode: (body: unknown) => T | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(url, { headers: inventoryHeaders(companyId), cache: 'no-store', signal })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export function fetchInventoryLabelTrace(
  companyId: string,
  labelId: string,
  direction: LabelTraceDirection,
  signal?: AbortSignal,
): Promise<ViewOutcome<InventoryLabelTrace>> {
  const query = new URLSearchParams({ direction })
  return read(`${inventoryLabelTracePathOf(labelId)}?${query.toString()}`, companyId,
    (body) => decodeInventoryLabelTrace(body, labelId, direction), signal)
}

export function searchInventoryLabelTrace(
  companyId: string,
  text: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<InventoryLabelTraceSearch>> {
  const query = new URLSearchParams({ text })
  return read(`${INVENTORY_LABEL_TRACE_SEARCH_PATH}?${query.toString()}`, companyId, decodeInventoryLabelTraceSearch, signal)
}
