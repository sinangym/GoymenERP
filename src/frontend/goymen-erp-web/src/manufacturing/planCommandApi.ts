/**
 * `POST /api/v1/manufacturing/plans` transport.
 *
 * The caller owns one immutable request and one idempotency key. This port never retries,
 * rotates the key or treats a missing response as a refusal: after an unknown outcome the
 * same frozen attempt may be sent again and the server decides whether it is a replay.
 */

import { decodePlan, type ProductionPlanView } from './contract'
import { MANUFACTURING_BASE, devHeaders } from './api'

export type PlanTargetBasis = 'InputMass' | 'GoodOutputMass'

export interface SavePlanRequest {
  readonly planId: string
  readonly workCenterId: string
  readonly inputs: readonly {
    readonly parentRollId: string
    readonly plannedKg: string
    readonly expectedLabelVersion: string
  }[]
  readonly target: { readonly basis: PlanTargetBasis; readonly kg: string } | null
}

export interface SavePlanContext {
  readonly companyId: string
  readonly idempotencyKey: string
}

export type SavePlanOutcome =
  | {
      readonly type: 'applied'
      readonly plan: ProductionPlanView
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  | {
      readonly type: 'failed'
      readonly status: number
      readonly code: string | null
      readonly field: string | null
      /** True only for a failure this endpoint actually stores and replays. */
      readonly replayed: boolean
    }
  | {
      readonly type: 'unknown'
      readonly reason:
        | 'aborted'
        | 'offline'
        | 'network-error'
        | 'service-unavailable'
        | 'unexpected-status'
        | 'undecodable'
    }

function text(body: unknown, name: string): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const value = (body as Record<string, unknown>)[name]
  return typeof value === 'string' && value.length > 0 ? value : null
}

function header(headers: Headers, name: string): string | null {
  const value = headers.get(name)
  return value !== null && value.trim().length > 0 ? value.trim() : null
}

function replayHeader(headers: Headers): boolean {
  return header(headers, 'Idempotency-Replayed')?.toLowerCase() === 'true'
}

/** The server returns its decimal text without insignificant trailing zeroes. */
function canonicalKilograms(value: string): string {
  if (!value.includes('.')) return value
  return value.replace(/0+$/, '').replace(/\.$/, '')
}

function sameInputs(plan: ProductionPlanView, request: SavePlanRequest): boolean {
  if (plan.inputs.length !== request.inputs.length) return false
  const remaining = [...plan.inputs]
  for (const sent of request.inputs) {
    const index = remaining.findIndex(
      (answered) =>
        answered.parentRollId === sent.parentRollId &&
        answered.plannedKg === canonicalKilograms(sent.plannedKg),
    )
    if (index < 0) return false
    remaining.splice(index, 1)
  }
  return remaining.length === 0
}

function sameTarget(plan: ProductionPlanView, request: SavePlanRequest): boolean {
  if (request.target === null) return plan.target === null
  return (
    plan.target?.basis === request.target.basis &&
    plan.target.kg === canonicalKilograms(request.target.kg)
  )
}

function describesAttempt(plan: ProductionPlanView, request: SavePlanRequest, companyId: string): boolean {
  return (
    plan.planId === request.planId &&
    plan.companyId === companyId &&
    plan.workCenterId === request.workCenterId &&
    plan.status === 'Draft' &&
    plan.approvedByUserId === null &&
    plan.approvedAtUtc === null &&
    sameInputs(plan, request) &&
    sameTarget(plan, request)
  )
}

function verifiedFailureReplay(status: number, code: string | null, rawReplay: boolean): boolean {
  if (!rawReplay) return false
  return status === 404 || (status === 409 && code === 'inventory_label_version_conflict')
}

/** Sends one frozen create attempt; no response or an unreadable response remains UNKNOWN. */
export async function savePlan(
  request: SavePlanRequest,
  context: SavePlanContext,
  signal?: AbortSignal,
): Promise<SavePlanOutcome> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/plans`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': context.companyId,
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(request),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', reason: 'aborted' }
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }

  const rawReplay = replayHeader(response.headers)
  const originalCorrelationId = header(response.headers, 'Idempotency-Original-Correlation-Id')
  if (response.status === 201) {
    const plan = decodePlan(body)
    const location = header(response.headers, 'Location')
    const replayHeadersMatch = rawReplay ? originalCorrelationId !== null : originalCorrelationId === null
    if (
      plan === null ||
      location === null ||
      !location.endsWith(`/api/v1/manufacturing/plans/${plan.planId}`) ||
      !replayHeadersMatch ||
      !describesAttempt(plan, request, context.companyId)
    ) {
      return { type: 'unknown', reason: 'undecodable' }
    }
    return { type: 'applied', plan, replayed: rawReplay, originalCorrelationId }
  }

  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  if (response.status >= 200 && response.status < 300) return { type: 'unknown', reason: 'unexpected-status' }

  const code = text(body, 'code')
  const field = text(body, 'field')
  if (response.status === 409 || response.status === 422) {
    if (code === null) return { type: 'unknown', reason: 'undecodable' }
  } else if (![400, 401, 403, 404, 415].includes(response.status)) {
    return { type: 'unknown', reason: 'unexpected-status' }
  }
  return {
    type: 'failed',
    status: response.status,
    code,
    field,
    replayed: verifiedFailureReplay(response.status, code, rawReplay),
  }
}
