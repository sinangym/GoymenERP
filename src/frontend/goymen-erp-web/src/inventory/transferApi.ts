import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import { decodeTransferDetail, decodeTransferList, type TransferDetail, type TransferList } from './transferContract'

async function read<T>(path: string, companyId: string, decode: (body: unknown) => T | null,
  belongs: (value: T) => boolean, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(`${API_BASE}/inventory/transfers${path}`, { headers: inventoryHeaders(companyId), cache: 'no-store', signal })
    let body: unknown = null
    try { body = await response.json() } catch { /* unknown body remains malformed */ }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decode(body)
    return data === null || !belongs(data) ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return { type: navigator.onLine ? 'network-error' : 'offline' }
  }
}

export const fetchTransfers = (companyId: string, signal?: AbortSignal) =>
  read<TransferList>('', companyId, decodeTransferList, page => page.companyId === companyId, signal)
export const fetchTransfer = (companyId: string, transferId: string, signal?: AbortSignal) =>
  read<TransferDetail>(`/${encodeURIComponent(transferId)}`, companyId, decodeTransferDetail,
    transfer => transfer.companyId === companyId && transfer.transferId === transferId, signal)
