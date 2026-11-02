import { PURCHASING_REQUESTS_BASE, purchasingHeaders } from './api'
import { decodePurchaseRequestCommandResponse, type PurchaseRequestCommandResponse } from './contract'

/** A1-1 (plan API-01): the two decisions on a request; each has its own route and its own status after it. */
export type RequestDecisionKind = 'approval' | 'rejection'

/** A1-1b (plan API-01b; ALS-D46, ALS-D47): the preparer's or submitter's withdrawal rides the same command shape on its own route. */
export type RequestCommandKind = RequestDecisionKind | 'withdrawal'

export type RequestDecisionOutcome =
  | { readonly type: 'applied'; readonly response: PurchaseRequestCommandResponse; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

const STATUS_AFTER: Record<RequestCommandKind, 'Approved' | 'Rejected' | 'Withdrawn'> = {
  approval: 'Approved', rejection: 'Rejected', withdrawal: 'Withdrawn',
}

function codeOf(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

/**
 * POST …/requests/{id}/approval, …/rejection or …/withdrawal with the Idempotency-Key, the expected version and (a
 * rejection or a withdrawal) the
 * reason. A 200 counts only if it decodes, names this company and request, carries the decision's status and its
 * replay headers agree with the body; anything else that may have been applied is `unknown` (resend the same snapshot).
 */
export async function decidePurchaseRequest(
  companyId: string,
  requestId: string,
  kind: RequestCommandKind,
  expectedVersion: string,
  idempotencyKey: string,
  reason?: string,
): Promise<RequestDecisionOutcome> {
  let response: Response
  try {
    response = await fetch(`${PURCHASING_REQUESTS_BASE}/${encodeURIComponent(requestId)}/${kind}`, {
      method: 'POST',
      headers: {
        ...purchasingHeaders(companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(kind === 'approval' ? { expectedVersion } : { expectedVersion, reason: reason ?? '' }),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { body = null }
  if (response.status === 200) {
    const decoded = decodePurchaseRequestCommandResponse(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayHeadersMatch = decoded !== null && (decoded.isReplay
      ? replayHeader?.trim().toLowerCase() === 'true' && originalHeader === decoded.originalCorrelationId
      : replayHeader === null && originalHeader === null)
    return decoded === null || decoded.detail.request.companyId !== companyId ||
      decoded.detail.request.id !== requestId || decoded.detail.request.status !== STATUS_AFTER[kind] || !replayHeadersMatch
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed: decoded.isReplay }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'failed', status: response.status, code: codeOf(body), replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  }
  return response.status >= 500 ? { type: 'unknown', reason: 'service-unavailable' } : { type: 'unknown', reason: 'unexpected-status' }
}
