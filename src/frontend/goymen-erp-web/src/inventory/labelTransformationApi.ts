import { API_BASE, inventoryHeaders } from './api'
import {
  DEFINITE_UNAVAILABLE_CODES,
  decodeLabelTransformationView,
  describesTransformation,
  type LabelTransformationRequest,
  type LabelTransformationView,
} from './labelTransformationContract'

export const INVENTORY_LABEL_TRANSFORMATIONS_PATH = `${API_BASE}/inventory/label-transformations`

export type RecordLabelTransformationOutcome =
  | { readonly type: 'applied'; readonly response: LabelTransformationView; readonly replayed: boolean }
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
 * Sends one split/merge attempt. 201 is a new record; 200 is the stored answer to the same key and content
 * (`Idempotency-Replayed: true` + the original correlation). Any answer that does not describe exactly this
 * request, any code-less or other 5xx, and any transport failure, is UNKNOWN: the caller keeps the attempt and
 * resends it with the same key and payload — never with a new key. A 503 carrying one of
 * DEFINITE_UNAVAILABLE_CODES is raised before any write, so it is a DEFINITE failure: nothing was recorded.
 */
export async function recordLabelTransformation(
  request: LabelTransformationRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<RecordLabelTransformationOutcome> {
  let response: Response
  try {
    response = await fetch(INVENTORY_LABEL_TRANSFORMATIONS_PATH, {
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
  if (response.status === 200 || response.status === 201) {
    const decoded = decodeLabelTransformationView(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayed = response.status === 200
    const headersMatch = replayed
      ? replayHeader?.trim().toLowerCase() === 'true' && typeof originalHeader === 'string' && originalHeader.length > 0
      : replayHeader === null && originalHeader === null
    return decoded === null || !headersMatch || !describesTransformation(decoded, request, context.companyId)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'failed', status: response.status, code: problemCode(body) }
  }
  const unavailableCode = problemCode(body)
  if (response.status === 503 && unavailableCode !== null && DEFINITE_UNAVAILABLE_CODES.has(unavailableCode)) {
    return { type: 'failed', status: 503, code: unavailableCode }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}
