import { API_BASE, inventoryHeaders } from './api'
import { decodeTransferDetail, type TransferDetail } from './transferContract'
import type { TransferCancelAttempt } from './transferCancelAttempt'

export type TransferCancelOutcome = { readonly type: 'applied'; readonly transfer: TransferDetail; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }

// InventoryEndpoints.Transfers.CancelDispatch publishes a closed set; an unrecognised/malformed response must not
// release an unresolved attempt and invite another command key.
const conflictCodes = new Set(['idempotency_conflict', 'inventory_concurrent_change', 'inventory_transfer_version_conflict',
  'inventory_transfer_state_conflict', 'inventory_label_version_conflict', 'inventory_transfer_cancel_window_closed',
  'inventory_transfer_cancel_window_unreadable', 'inventory_transfer_cancel_confirmation_missing', 'inventory_transfer_cancel_loss_request_pending'])

export async function cancelTransferDispatch(attempt: TransferCancelAttempt): Promise<TransferCancelOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}/inventory/transfers/${encodeURIComponent(attempt.transferId)}/cancel-dispatch`, {
      method: 'POST', cache: 'no-store', headers: { ...inventoryHeaders(attempt.companyId), 'Content-Type': 'application/json',
        'Idempotency-Key': attempt.key }, body: JSON.stringify(attempt.payload),
    })
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  // Authority loss invalidates the captured shell boundary even when an intermediary mangles the problem body.
  // The workspace keeps this command's original key for any later authorised retry.
  if (response.status === 401 || response.status === 403) return { type: 'failed', status: response.status, code: null }
  let body: unknown = null
  try { body = await response.json() } catch { /* A success without its owner view remains unknown. */ }
  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  if (response.status === 200 || response.status === 201) {
    const transfer = decodeTransferDetail(body)
    const replayed = response.status === 200
    const header = response.headers.get('Idempotency-Replayed')
    const correlation = response.headers.get('Idempotency-Original-Correlation-Id')
    const validHeaders = replayed ? header === 'true' && correlation !== null && correlation.trim().length > 0
      : header === null && correlation === null
    if (transfer === null || transfer.companyId !== attempt.companyId || transfer.transferId !== attempt.transferId
      || transfer.status !== 'DispatchCancelled' || !validHeaders) return { type: 'unknown', reason: 'malformed-response' }
    return { type: 'applied', transfer, replayed }
  }
  if (![400, 404, 409, 415].includes(response.status)) return { type: 'unknown', reason: 'malformed-response' }
  const code = typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null
  const replayHeader = response.headers.get('Idempotency-Replayed')
  const originalCorrelation = response.headers.get('Idempotency-Original-Correlation-Id')
  const recordedRefusal = response.status === 409 && code === 'inventory_transfer_cancel_window_closed'
  const replayHeadersValid = replayHeader === null && originalCorrelation === null
    || recordedRefusal && replayHeader === 'true' && originalCorrelation !== null && originalCorrelation.trim().length > 0
  if (!replayHeadersValid || response.status === 400 && code !== 'validation_failed'
    || response.status === 409 && (code === null || !conflictCodes.has(code))
    || [404, 415].includes(response.status) && code !== null) return { type: 'unknown', reason: 'malformed-response' }
  return { type: 'failed', status: response.status, code }
}
