import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import {
  decodeInventoryLabelDetail,
  type InventoryLabelDetailWithMovementTimeline,
} from './labelDetailContract'

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

export async function fetchInventoryLabelDetail(
  companyId: string,
  labelId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<InventoryLabelDetailWithMovementTimeline>> {
  const path = `${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/detail`
  try {
    const response = await fetch(path, { headers: inventoryHeaders(companyId), cache: 'no-store', signal })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const detail = decodeInventoryLabelDetail(body, companyId, labelId)
    return detail === null ? { type: 'malformed' } : { type: 'ok-single', data: detail }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
