import type { ViewOutcome } from '../shell/uiState'
import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { decodePurchaseOrderCommandResponse, type PurchaseOrderCommandResponse } from './contract'
import { decodePurchaseOrderLineScheduleHistory, type PurchaseOrderLineScheduleHistory } from './lineScheduleContract'

/**
 * F-027 (ALS-D94): this company's order line schedule history. A 200 must decode strictly and be the order and line asked for;
 * any other status is handed to the caller with its body (the shell classifies 401/403/404/5xx); no answer is offline or a
 * network error, an aborted read is fence-stale.
 */
export async function fetchPurchaseOrderLineScheduleHistory(
  companyId: string,
  orderId: string,
  lineId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<PurchaseOrderLineScheduleHistory>> {
  try {
    const response = await fetch(
      `${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/lines/${encodeURIComponent(lineId)}/schedule-history`,
      { headers: purchasingHeaders(companyId), signal },
    )
    let body: unknown = null
    try { body = await response.json() } catch { body = null }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const history = decodePurchaseOrderLineScheduleHistory(body, orderId, lineId)
    return history === null ? { type: 'malformed' } : { type: 'ok-single', data: history }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

/** One slice of a schedule change as sent: an existing slice keeps its id (ALS-D81), an added one carries a fresh id. */
export interface LineScheduleSliceInput {
  readonly sliceId: string
  /** The canonical decimal text of the quantity in the line's unit. */
  readonly quantity: string
  /** `yyyy-MM-dd`. */
  readonly deliveryDate: string
}

/** A field-level problem the server named (e.g. `slices` / `purchasing_schedule_total_mismatch`). */
export interface LineScheduleFieldProblem {
  readonly field: string
  readonly code: string
}

/**
 * F-027 (UI-07, EK16 §2a; ALS-D94): the outcome of a line termin change. `applied` is a 200 whose decoded body is this company's
 * order with the line asked for, carrying exactly the slice ids sent, and replay headers that agree with the body; a definite
 * refusal is `failed` with the top-level code and the field problems; anything else (no answer, a 5xx, a body that cannot be
 * trusted) is `unknown` — the caller keeps the same key, version and slices for an explicit resend.
 */
export type LineScheduleOutcome =
  | { readonly type: 'applied'; readonly response: PurchaseOrderCommandResponse; readonly replayed: boolean }
  | {
    readonly type: 'failed'
    readonly status: number
    readonly code: string | null
    readonly problems: readonly LineScheduleFieldProblem[]
    readonly replayed: boolean
  }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'undecodable' | 'unexpected-status' }

function topLevelCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Record<string, unknown>).code
  return typeof code === 'string' && code.length > 0 ? code : null
}

function fieldProblems(body: unknown): LineScheduleFieldProblem[] {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return []
  const errors = (body as Record<string, unknown>).errors
  if (!Array.isArray(errors)) return []
  const problems: LineScheduleFieldProblem[] = []
  for (const error of errors) {
    if (typeof error !== 'object' || error === null || Array.isArray(error)) continue
    const { field, code } = error as Record<string, unknown>
    if (typeof field === 'string' && typeof code === 'string' && code.length > 0) problems.push({ field, code })
  }
  return problems
}

function describesSchedule(response: PurchaseOrderCommandResponse, companyId: string, orderId: string, lineId: string,
  slices: readonly LineScheduleSliceInput[]): boolean {
  const { order, lines } = response.detail
  if (order.companyId !== companyId || order.id !== orderId) return false
  const line = lines.find((candidate) => candidate.lineId === lineId)
  const answered = line?.deliverySchedule
  if (answered === undefined || answered.length !== slices.length) return false
  const sent = new Set(slices.map((slice) => slice.sliceId.toLowerCase()))
  return answered.every((slice) => sent.has(slice.sliceId.toLowerCase()))
}

export async function setPurchaseOrderLineSchedule(
  companyId: string,
  orderId: string,
  lineId: string,
  expectedVersion: string,
  idempotencyKey: string,
  slices: readonly LineScheduleSliceInput[],
): Promise<LineScheduleOutcome> {
  let response: Response
  try {
    response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(orderId)}/lines/${encodeURIComponent(lineId)}/schedule`, {
      method: 'POST',
      headers: {
        ...purchasingHeaders(companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify({
        expectedVersion,
        slices: slices.map((slice) => ({ sliceId: slice.sliceId, quantity: slice.quantity, deliveryDate: slice.deliveryDate })),
      }),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  try { body = await response.json() } catch { body = null }
  if (response.status === 200) {
    const decoded = decodePurchaseOrderCommandResponse(body)
    const replayHeader = response.headers.get('Idempotency-Replayed')
    const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
    const replayHeadersMatch = decoded !== null && (
      decoded.isReplay
        ? replayHeader?.trim().toLowerCase() === 'true' && originalHeader === decoded.originalCorrelationId
        : replayHeader === null && originalHeader === null
    )
    return decoded === null || !replayHeadersMatch || !describesSchedule(decoded, companyId, orderId, lineId, slices)
      ? { type: 'unknown', reason: 'undecodable' }
      : { type: 'applied', response: decoded, replayed: decoded.isReplay }
  }
  if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return {
      type: 'failed',
      status: response.status,
      code: topLevelCode(body),
      problems: fieldProblems(body),
      replayed: response.headers.get('Idempotency-Replayed')?.trim().toLowerCase() === 'true',
    }
  }
  return response.status >= 500
    ? { type: 'unknown', reason: 'service-unavailable' }
    : { type: 'unknown', reason: 'unexpected-status' }
}
