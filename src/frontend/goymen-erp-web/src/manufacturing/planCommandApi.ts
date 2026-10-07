/**
 * `POST /api/v1/manufacturing/plans` transport.
 *
 * The caller owns one immutable request and one idempotency key. This port never retries,
 * rotates the key or treats a missing response as a refusal: after an unknown outcome the
 * same frozen attempt may be sent again and the server decides whether it is a replay.
 */

import { decodeOrder, decodePlan, type ProductionOrderView, type ProductionPlanView } from './contract'
import { MANUFACTURING_BASE, devHeaders } from './api'
import type { CapturedPlanCommercialContext } from './planRevisionContract'

export type PlanTargetBasis = 'InputMass' | 'GoodOutputMass'

export interface SlittingSpecRequest {
  readonly lines: readonly {
    readonly bandWidthMm: string
    readonly count: number
  }[]
  readonly windingTargetKg: string | null
}

export interface SavePlanRequest {
  readonly planId: string
  readonly workCenterId: string
  readonly inputs: readonly {
    readonly parentRollId: string
    readonly plannedKg: string
    readonly expectedLabelVersion: string
  }[]
  readonly target: { readonly basis: PlanTargetBasis; readonly kg: string } | null
  /** Omitted by legacy callers; explicit null means this plan has no slitting draft content. */
  readonly slittingSpec?: SlittingSpecRequest | null
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

function sameInputs(
  plan: ProductionPlanView,
  request: Pick<SavePlanRequest, 'inputs'> | Pick<RevisePlanRequest, 'inputs'>,
): boolean {
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

function sameTarget(
  plan: ProductionPlanView,
  request: Pick<SavePlanRequest, 'target'> | Pick<RevisePlanRequest, 'target'>,
): boolean {
  if (request.target === null) return plan.target === null
  return (
    plan.target?.basis === request.target.basis &&
    plan.target.kg === canonicalKilograms(request.target.kg)
  )
}

function sameSlittingSpec(plan: ProductionPlanView, request: Pick<SavePlanRequest, 'slittingSpec'>): boolean {
  const sent = request.slittingSpec ?? null
  if (sent === null) return plan.slittingSpec === null
  if (plan.slittingSpec === null || plan.slittingSpec.lines.length !== sent.lines.length) return false
  if ((plan.slittingSpec.windingTargetKg ?? null) !== (sent.windingTargetKg === null
    ? null
    : canonicalKilograms(sent.windingTargetKg))) return false
  return plan.slittingSpec.lines.every((line, index) => {
    const requested = sent.lines[index]
    return line.bandWidthMm === canonicalKilograms(requested.bandWidthMm) && line.count === requested.count
  })
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
    sameTarget(plan, request) &&
    sameSlittingSpec(plan, request)
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

/**
 * MFG-PLAN-EDIT: the Draft plan's new content as a whole, at the version the detail read, with the user's reason.
 * Every kept or added roll carries the label version the screen read for it; Inventory revalidates it.
 */
export interface RevisePlanRequest {
  readonly expectedVersion: number
  readonly workCenterId: string
  readonly inputs: readonly {
    readonly parentRollId: string
    readonly plannedKg: string
    readonly expectedLabelVersion: string
    /** Additive wire member: omitted legacy requests and explicit null both mean company-owned input. */
    readonly commercialContext?: CapturedPlanCommercialContext | null
  }[]
  readonly target: { readonly basis: PlanTargetBasis; readonly kg: string } | null
  readonly reason: string
}

export type PlanLifecycleSnapshot =
  | {
      readonly kind: 'approve'
      readonly idempotencyKey: string
      readonly expectedVersion: number
    }
  | {
      readonly kind: 'create-order'
      readonly idempotencyKey: string
      readonly orderId: string
    }
  | {
      readonly kind: 'revise'
      readonly idempotencyKey: string
      readonly request: RevisePlanRequest
    }

export type PlanLifecycleOutcome<T> =
  | {
      readonly type: 'applied'
      readonly resource: T
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  | {
      readonly type: 'failed'
      readonly status: number
      readonly code: string | null
      readonly field: string | null
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

export interface PendingPlanLifecycle {
  readonly actorId: string
  readonly companyId: string
  readonly planId: string
  readonly snapshot: PlanLifecycleSnapshot
}

const pendingLifecycle = new Map<string, PendingPlanLifecycle>()

export function pendingPlanLifecycleKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
  planId: string,
): string | null {
  return actorId === null || companyId === null || planId.length === 0
    ? null
    : JSON.stringify([sessionEpoch, actorId, companyId, planId])
}

export function recallPendingPlanLifecycle(key: string | null): PendingPlanLifecycle | null {
  return key === null ? null : (pendingLifecycle.get(key) ?? null)
}

/** Reserve before sending so a route remount cannot mint a second command for an unknown first result. */
export function reservePendingPlanLifecycle(key: string | null, pending: PendingPlanLifecycle): void {
  if (key !== null) pendingLifecycle.set(key, pending)
}

/** Release only the command that settled; a newer frozen command in the same slot is never removed. */
export function releasePendingPlanLifecycle(key: string | null, idempotencyKey: string): void {
  if (key === null || pendingLifecycle.get(key)?.snapshot.idempotencyKey !== idempotencyKey) return
  pendingLifecycle.delete(key)
}

/** Test isolation only. */
export function clearPendingPlanLifecycles(): void {
  pendingLifecycle.clear()
}

function replayMetadata(headers: Headers):
  | { readonly replayed: boolean; readonly originalCorrelationId: string | null }
  | null {
  const replayed = replayHeader(headers)
  const originalCorrelationId = header(headers, 'Idempotency-Original-Correlation-Id')
  return replayed === (originalCorrelationId !== null) ? { replayed, originalCorrelationId } : null
}

async function lifecycleRequest(
  url: string,
  companyId: string,
  idempotencyKey: string,
  body: Record<string, unknown>,
  successStatus: number,
  signal?: AbortSignal,
): Promise<
  | { readonly type: 'response'; readonly response: Response; readonly body: unknown }
  | Extract<PlanLifecycleOutcome<never>, { type: 'unknown' }>
> {
  let response: Response
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(body),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', reason: 'aborted' }
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }

  let parsed: unknown = null
  try {
    parsed = await response.json()
  } catch {
    parsed = null
  }

  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  if (response.status >= 200 && response.status < 300 && response.status !== successStatus) {
    return { type: 'unknown', reason: 'unexpected-status' }
  }
  return { type: 'response', response, body: parsed }
}

function lifecycleFailure<T>(response: Response, body: unknown): PlanLifecycleOutcome<T> {
  const metadata = replayMetadata(response.headers)
  if (metadata === null) return { type: 'unknown', reason: 'undecodable' }
  const code = text(body, 'code')
  if ((response.status === 409 || response.status === 422) && code === null) {
    return { type: 'unknown', reason: 'undecodable' }
  }
  if (![400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
    return { type: 'unknown', reason: 'unexpected-status' }
  }
  return {
    type: 'failed',
    status: response.status,
    code,
    field: text(body, 'field'),
    replayed: metadata.replayed,
  }
}

/** Approves exactly the version read by the detail screen. The transport never rotates the key or retries itself. */
export async function approvePlan(
  companyId: string,
  planId: string,
  snapshot: Extract<PlanLifecycleSnapshot, { kind: 'approve' }>,
  signal?: AbortSignal,
): Promise<PlanLifecycleOutcome<ProductionPlanView>> {
  const result = await lifecycleRequest(
    `${MANUFACTURING_BASE}/plans/${encodeURIComponent(planId)}/approval`,
    companyId,
    snapshot.idempotencyKey,
    { expectedVersion: snapshot.expectedVersion },
    200,
    signal,
  )
  if (result.type === 'unknown') return result
  if (result.response.status !== 200) return lifecycleFailure(result.response, result.body)
  const metadata = replayMetadata(result.response.headers)
  const plan = decodePlan(result.body)
  if (
    metadata === null ||
    plan === null ||
    plan.planId !== planId ||
    plan.companyId !== companyId ||
    plan.status !== 'Approved' ||
    plan.version !== snapshot.expectedVersion + 1
  ) {
    return { type: 'unknown', reason: 'undecodable' }
  }
  return { type: 'applied', resource: plan, ...metadata }
}

/** Creates one order from the approved plan. A success is followed by the routed order's own fresh GET. */
export async function createOrder(
  companyId: string,
  planId: string,
  snapshot: Extract<PlanLifecycleSnapshot, { kind: 'create-order' }>,
  signal?: AbortSignal,
): Promise<PlanLifecycleOutcome<ProductionOrderView>> {
  const result = await lifecycleRequest(
    `${MANUFACTURING_BASE}/orders`,
    companyId,
    snapshot.idempotencyKey,
    { orderId: snapshot.orderId, planId },
    201,
    signal,
  )
  if (result.type === 'unknown') return result
  if (result.response.status !== 201) return lifecycleFailure(result.response, result.body)
  const metadata = replayMetadata(result.response.headers)
  const location = header(result.response.headers, 'Location')
  const order = decodeOrder(result.body)
  if (
    metadata === null ||
    order === null ||
    order.orderId !== snapshot.orderId ||
    order.planId !== planId ||
    order.companyId !== companyId ||
    order.status !== 'Open' ||
    order.version !== 1 ||
    location === null ||
    !location.endsWith(`/api/v1/manufacturing/orders/${snapshot.orderId}`)
  ) {
    return { type: 'unknown', reason: 'undecodable' }
  }
  return { type: 'applied', resource: order, ...metadata }
}

/**
 * MFG-PLAN-EDIT: revises the Draft at exactly the version the detail read (`POST /plans/{planId}/revision`). A 200 is
 * trusted only when it describes THIS attempt — the plan, the company, a Draft one version on, the sent work center,
 * inputs and target — otherwise the outcome stays unknown and the same frozen attempt may be sent again. The transport
 * never rotates the key or retries itself.
 */
export async function revisePlan(
  companyId: string,
  planId: string,
  snapshot: Extract<PlanLifecycleSnapshot, { kind: 'revise' }>,
  signal?: AbortSignal,
): Promise<PlanLifecycleOutcome<ProductionPlanView>> {
  const result = await lifecycleRequest(
    `${MANUFACTURING_BASE}/plans/${encodeURIComponent(planId)}/revision`,
    companyId,
    snapshot.idempotencyKey,
    { ...snapshot.request },
    200,
    signal,
  )
  if (result.type === 'unknown') return result
  if (result.response.status !== 200) return lifecycleFailure(result.response, result.body)
  const metadata = replayMetadata(result.response.headers)
  const plan = decodePlan(result.body)
  const request = snapshot.request
  if (
    metadata === null ||
    plan === null ||
    plan.planId !== planId ||
    plan.companyId !== companyId ||
    plan.status !== 'Draft' ||
    plan.version !== request.expectedVersion + 1 ||
    plan.workCenterId !== request.workCenterId ||
    !sameInputs(plan, request) ||
    !sameTarget(plan, request)
  ) {
    return { type: 'unknown', reason: 'undecodable' }
  }
  return { type: 'applied', resource: plan, ...metadata }
}
