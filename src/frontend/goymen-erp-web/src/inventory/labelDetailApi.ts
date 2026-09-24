import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import {
  combineInventoryLabelDetail,
  decodeInventoryLabelBalances,
  decodeInventoryLabelIdentity,
  type InventoryLabelDetail,
} from './labelDetailContract'

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

function requestOutcome(status: number, body: unknown): ViewOutcome<never> {
  return { type: 'http', status, body }
}

export async function fetchInventoryLabelDetail(
  companyId: string,
  labelId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<InventoryLabelDetail>> {
  const path = `${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}`
  try {
    const [identityResponse, balancesResponse] = await Promise.all([
      fetch(path, { headers: inventoryHeaders(companyId), cache: 'no-store', signal }),
      fetch(`${path}/balances`, { headers: inventoryHeaders(companyId), cache: 'no-store', signal }),
    ])
    const [identityBody, balancesBody] = await Promise.all([bodyOf(identityResponse), bodyOf(balancesResponse)])
    if (identityResponse.status !== 200) return requestOutcome(identityResponse.status, identityBody)
    if (balancesResponse.status !== 200) return requestOutcome(balancesResponse.status, balancesBody)

    const identity = decodeInventoryLabelIdentity(identityBody)
    const balances = decodeInventoryLabelBalances(balancesBody)
    if (identity === null || balances === null) return { type: 'malformed' }
    const detail = combineInventoryLabelDetail(identity, balances, companyId, labelId)
    return detail === null ? { type: 'malformed' } : { type: 'ok-single', data: detail }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
