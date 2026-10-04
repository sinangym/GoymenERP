import { API_BASE, inventoryHeaders } from './api'
import {
  decodeLabelClassificationView,
  describesLabelClassification,
  type LabelClassificationRequest,
  type LabelClassificationView,
} from './labelClassificationContract'

export type LabelClassificationOutcome =
  | { readonly type: 'applied'; readonly response: LabelClassificationView; readonly replayed: boolean }
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

export async function classifyInventoryLabel(
  labelId: string,
  request: LabelClassificationRequest,
  context: { readonly companyId: string; readonly idempotencyKey: string },
): Promise<LabelClassificationOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/classification`, {
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
    const decoded = decodeLabelClassificationView(body)
    const replayed = response.status === 200
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const headersMatch = replayed
      ? replayHeader?.trim().toLowerCase() === 'true' && typeof originalHeader === 'string' && originalHeader.length > 0
      : replayHeader === null && originalHeader === null
    return decoded === null || !headersMatch || !describesLabelClassification(decoded, labelId, request)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'failed', status: response.status, code: problemCode(body) }
  }
  if (response.status === 503 && problemCode(body) === 'inventory_label_classification_permission_unregistered') {
    return { type: 'failed', status: 503, code: 'inventory_label_classification_permission_unregistered' }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}
