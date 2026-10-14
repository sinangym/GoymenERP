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

/**
 * `CutToLengthSpecRequestV1`: the ordered cut lines — stored ones sent back exactly as read, authored ones exactly as
 * validated (M01.3) — in list order, never re-sorted by sequence.
 */
export interface CutToLengthSpecRequest {
  readonly lines: readonly {
    readonly lineId: string
    readonly sequence: number
    readonly lengthMm: string
    readonly count: number
  }[]
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
  /** M01.3: the authored ordered cut lines; omitted when the plan has none (never an empty or implicit clear). */
  readonly cutToLengthSpec?: CutToLengthSpecRequest
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

/** Exact: the same lines in the same order, the same ids, signed sequences, canonical lengths and counts. */
function sameCutToLengthSpec(
  plan: ProductionPlanView,
  request: Pick<RevisePlanRequest, 'cutToLengthSpec'>,
): boolean {
  const sent = request.cutToLengthSpec ?? null
  if (sent === null) return plan.cutToLengthSpec === null
  if (plan.cutToLengthSpec === null || plan.cutToLengthSpec.lines.length !== sent.lines.length) return false
  return plan.cutToLengthSpec.lines.every((line, index) => {
    const requested = sent.lines[index]
    return (
      line.lineId.toLowerCase() === requested.lineId.toLowerCase() &&
      line.sequence === requested.sequence &&
      line.lengthMm === canonicalKilograms(requested.lengthMm) &&
      line.count === requested.count
    )
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
    sameSlittingSpec(plan, request) &&
    // Exactly the sent cut lines (or none when none were sent): order, ids, sequences, canonical lengths and counts.
    sameCutToLengthSpec(plan, request)
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
  /**
   * M01.2: the plan's stored cut lines, carried unchanged by an edit of other fields. Omitted when the plan has none —
   * the legacy request shape. The server reads an omitted member as null and would clear stored lines, so a plan that
   * has them always sends them.
   */
  readonly cutToLengthSpec?: CutToLengthSpecRequest
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

/**
 * M01.3 (K2 P1): an unresolved REVISE also lives in the tab's sessionStorage, the same session-scoped store the pending
 * create uses (pendingPlanStore), so a real page reload — which recreates this module and empties the map — can still
 * re-send the same key and the same frozen body. Approve and create-order stay in memory only, as before.
 *
 * The slot name is the full context key (session epoch, actor, company, plan). A recalled record is never trusted: it
 * must be exactly this contract AND name the very context of the slot it was read from; only then is it deep-frozen and
 * used. A malformed or foreign record is not replayed and is left in place (never silently deleted); the server's
 * expected-version fence still refuses any second revise built on a version that the first one already moved.
 */
const STORED_REVISE_PREFIX = 'goymen.manufacturing.plan-revise.v1:'

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_UUID = '00000000-0000-0000-0000-000000000000'
const LABEL_VERSION = /^[1-9][0-9]*$/
const KG = /^(0|[1-9][0-9]{0,17})(\.[0-9]{1,20})?$/
const MM = /^(?=.*[1-9])(?:0|[1-9][0-9]*)(?:\.[0-9]{1,3})?$/
const INT32_MIN = -2_147_483_648
const INT32_MAX = 2_147_483_647

type Json = Record<string, unknown>

function isRecord(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasKeys(value: Json, required: readonly string[], optional: readonly string[] = []): boolean {
  const own = Object.keys(value)
  return required.every((key) => own.includes(key)) && own.every((key) => required.includes(key) || optional.includes(key))
}

function guid(value: unknown): boolean {
  return typeof value === 'string' && UUID.test(value) && value !== NIL_UUID
}

function positiveKg(value: unknown): boolean {
  return typeof value === 'string' && KG.test(value) && /[1-9]/.test(value)
}

function int32(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= INT32_MIN && value <= INT32_MAX
}

function validCommercialContext(value: unknown): boolean {
  if (value === null) return true
  return isRecord(value)
    && hasKeys(value, ['partyId', 'companyPartyAccountId', 'partyVersion', 'accountVersion'])
    && guid(value.partyId)
    && guid(value.companyPartyAccountId)
    && typeof value.partyVersion === 'string' && value.partyVersion.length > 0
    && typeof value.accountVersion === 'string' && value.accountVersion.length > 0
}

function validCutLines(value: unknown): boolean {
  if (!isRecord(value) || !hasKeys(value, ['lines']) || !Array.isArray(value.lines) || value.lines.length === 0) return false
  const ids = new Set<string>()
  return (value.lines as unknown[]).every((line) => {
    if (!isRecord(line) || !hasKeys(line, ['lineId', 'sequence', 'lengthMm', 'count']) || !guid(line.lineId)) return false
    const id = (line.lineId as string).toLowerCase()
    if (ids.has(id)) return false
    ids.add(id)
    return int32(line.sequence)
      && typeof line.lengthMm === 'string' && MM.test(line.lengthMm)
      && int32(line.count) && line.count > 0
  })
}

/** Exactly a `RevisePlanRequest` the edit form sends; a cut definition, when present, is never null or empty. */
function validReviseRequest(value: unknown): boolean {
  if (!isRecord(value)
    || !hasKeys(value, ['expectedVersion', 'workCenterId', 'inputs', 'target', 'reason'], ['cutToLengthSpec'])) return false
  const { expectedVersion, workCenterId, inputs, target, reason } = value
  if (typeof expectedVersion !== 'number' || !Number.isSafeInteger(expectedVersion) || expectedVersion < 1) return false
  if (!guid(workCenterId) || !Array.isArray(inputs) || inputs.length === 0) return false
  const rolls = new Set<string>()
  for (const input of inputs as unknown[]) {
    if (!isRecord(input)
      || !hasKeys(input, ['parentRollId', 'plannedKg', 'expectedLabelVersion'], ['commercialContext'])
      || !guid(input.parentRollId)
      || !positiveKg(input.plannedKg)
      || typeof input.expectedLabelVersion !== 'string' || !LABEL_VERSION.test(input.expectedLabelVersion)
      || (Object.hasOwn(input, 'commercialContext') && !validCommercialContext(input.commercialContext))) return false
    const roll = (input.parentRollId as string).toLowerCase()
    if (rolls.has(roll)) return false
    rolls.add(roll)
  }
  if (target !== null && (!isRecord(target) || !hasKeys(target, ['basis', 'kg'])
    || (target.basis !== 'InputMass' && target.basis !== 'GoodOutputMass') || !positiveKg(target.kg))) return false
  if (typeof reason !== 'string' || reason.length === 0 || reason.length > 256 || reason.trim() !== reason) return false
  return !Object.hasOwn(value, 'cutToLengthSpec') || validCutLines(value.cutToLengthSpec)
}

/** The context the slot key names: `[sessionEpoch, actorId, companyId, planId]` (see {@link pendingPlanLifecycleKey}). */
function slotContext(key: string): { readonly actorId: string; readonly companyId: string; readonly planId: string } | null {
  try {
    const parsed: unknown = JSON.parse(key)
    if (!Array.isArray(parsed) || parsed.length !== 4 || typeof parsed[0] !== 'number') return null
    const [, actorId, companyId, planId] = parsed as unknown[]
    if (typeof actorId !== 'string' || typeof companyId !== 'string' || typeof planId !== 'string') return null
    return { actorId, companyId, planId }
  } catch {
    return null
  }
}

function decodeStoredRevise(key: string, raw: string): PendingPlanLifecycle | null {
  const context = slotContext(key)
  let value: unknown
  try {
    value = JSON.parse(raw)
  } catch {
    return null
  }
  if (context === null || !isRecord(value) || !hasKeys(value, ['actorId', 'companyId', 'planId', 'snapshot'])) return null
  // The record must name the very context of the slot it was read from.
  if (value.actorId !== context.actorId || value.companyId !== context.companyId || value.planId !== context.planId) {
    return null
  }
  const snapshot = value.snapshot
  if (!isRecord(snapshot) || !hasKeys(snapshot, ['kind', 'idempotencyKey', 'request']) || snapshot.kind !== 'revise'
    || !guid(snapshot.idempotencyKey) || !validReviseRequest(snapshot.request)) return null
  return deepFreeze(value as unknown as PendingPlanLifecycle)
}

function deepFreeze<T>(value: T): T {
  if (typeof value === 'object' && value !== null && !Object.isFrozen(value)) {
    Object.freeze(value)
    for (const child of Object.values(value)) deepFreeze(child)
  }
  return value
}

function storedRevise(key: string): PendingPlanLifecycle | null {
  try {
    const raw = sessionStorage.getItem(STORED_REVISE_PREFIX + key)
    return raw === null ? null : decodeStoredRevise(key, raw)
  } catch {
    return null
  }
}

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
  if (key === null) return null
  const held = pendingLifecycle.get(key)
  if (held !== undefined) return held
  // After a reload: only a strictly valid revise of exactly this context, frozen before use.
  const recalled = storedRevise(key)
  if (recalled !== null) pendingLifecycle.set(key, recalled)
  return recalled
}

/** Reserve before sending so a route remount — or, for a revise, a page reload — cannot mint a second command. */
export function reservePendingPlanLifecycle(key: string | null, pending: PendingPlanLifecycle): void {
  if (key === null) return
  pendingLifecycle.set(key, pending)
  try {
    if (pending.snapshot.kind === 'revise') {
      sessionStorage.setItem(STORED_REVISE_PREFIX + key, JSON.stringify(pending))
    } else {
      // A newer command took this slot: the stored revise is replaced here exactly as it is in memory.
      sessionStorage.removeItem(STORED_REVISE_PREFIX + key)
    }
  } catch {
    // The in-memory slot still holds the immutable attempt for this module life.
  }
}

/** Release only the command that settled; a newer frozen command in the same slot is never removed. */
export function releasePendingPlanLifecycle(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (pendingLifecycle.get(key)?.snapshot.idempotencyKey === idempotencyKey) pendingLifecycle.delete(key)
  if (storedRevise(key)?.snapshot.idempotencyKey !== idempotencyKey) return
  try {
    sessionStorage.removeItem(STORED_REVISE_PREFIX + key)
  } catch {
    // A stale stored attempt stays fail-closed: it can only replay the same key and body in the same context.
  }
}

/** Test isolation only. */
export function clearPendingPlanLifecycles(): void {
  pendingLifecycle.clear()
  try {
    for (const name of Object.keys(sessionStorage)) {
      if (name.startsWith(STORED_REVISE_PREFIX)) sessionStorage.removeItem(name)
    }
  } catch {
    // Nothing stored.
  }
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
 * inputs, target and exact cut lines — otherwise the outcome stays unknown and the same frozen attempt may be sent again. The transport
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
    !sameTarget(plan, request) ||
    !sameCutToLengthSpec(plan, request)
  ) {
    return { type: 'unknown', reason: 'undecodable' }
  }
  return { type: 'applied', resource: plan, ...metadata }
}
