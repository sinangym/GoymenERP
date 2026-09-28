import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, inventoryHeaders } from './api'
import {
  decodeAllocationChangeView,
  decodeLabelAllocations,
  describesChange,
  type AllocationChangeRequest,
  type AllocationChangeView,
  type LabelAllocations,
} from './allocationChangeContract'

export function inventoryLabelAllocationsPath(labelId: string): string {
  return `${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/physical-allocations`
}

export function inventoryAllocationChangePath(allocationId: string): string {
  return `${API_BASE}/inventory/physical-allocations/${encodeURIComponent(allocationId)}/change`
}

export type AllocationChangeOutcome =
  | { readonly type: 'applied'; readonly response: AllocationChangeView; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

async function bodyOf(response: Response): Promise<unknown> {
  try { return await response.json() } catch { return null }
}

function problemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

/**
 * One change attempt. 200 is the change (the first answer carries no replay headers; the same key and content again
 * carries `Idempotency-Replayed: true` + the original correlation). An answer that does not describe exactly this
 * change, any 5xx and any transport failure is UNKNOWN: the caller resends with the same key and payload — never a
 * new key — or abandons and re-reads the label.
 */
export async function changeAllocation(
  allocationId: string,
  request: AllocationChangeRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<AllocationChangeOutcome> {
  let response: Response
  try {
    response = await fetch(inventoryAllocationChangePath(allocationId), {
      method: 'POST',
      headers: {
        ...inventoryHeaders(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(request),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  const body = await bodyOf(response)
  if (response.status === 200) {
    const decoded = decodeAllocationChangeView(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayed = replayHeader?.trim().toLowerCase() === 'true'
    const headersMatch = replayed
      ? typeof originalHeader === 'string' && originalHeader.length > 0
      : replayHeader === null && originalHeader === null
    return decoded === null || !headersMatch || !describesChange(decoded, allocationId, request, context.companyId)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'failed', status: response.status, code: problemCode(body) }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}

export async function fetchLabelAllocations(
  companyId: string,
  labelId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<LabelAllocations>> {
  try {
    const response = await fetch(inventoryLabelAllocationsPath(labelId), {
      headers: inventoryHeaders(companyId), cache: 'no-store', signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeLabelAllocations(body)
    return decoded === null || decoded.labelId.toLowerCase() !== labelId.toLowerCase() ||
      decoded.items.some((item) => item.allocation.companyId.toLowerCase() !== companyId.toLowerCase())
      ? { type: 'malformed' }
      : { type: 'ok-single', data: decoded }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
