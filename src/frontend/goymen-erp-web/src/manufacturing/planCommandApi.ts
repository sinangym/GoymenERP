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
 * used. A malformed or foreign record is not replayed and is left in place (never silently deleted).
 *
 * M01.3 (K2 #727/6178721152): an unreadable slot is NOT an empty one. When the first revise was processed and only its
 * answer was lost, a fresh read already shows the moved version, so the expected-version fence would accept a second,
 * newly keyed revise. The slot therefore reads as `absent`, `pending` or `unreadable`; only proven absence lets a new
 * command in, a revise is sent only once its exact attempt is durably stored, and an unresolved revise is never
 * displaced by another command of the same slot.
 *
 * M01.3 (K2 #728/6180459736, r5 #66/6187473245): the stored slot is named by actor, company and plan only, and the
 * record carries the SERVER's session binding (`state.sessionBinding`, from `/identity/me`) — not `sessionEpoch`, which
 * restarts at 0 in every document, and not anything the client minted or stored, which a transient storage failure
 * could revive. The same server session after an F5 finds its attempt; a record of another server session (another
 * binding), a missing binding (the server gave none), or a legacy epoch-keyed v1 record of the same context is
 * `unreadable`, never absent. Nothing is moved, rebound or deleted to make it fit.
 */
const STORED_REVISE_PREFIX = 'goymen.manufacturing.plan-revise.v2:'
/** Epoch-keyed slots written before the binding: `[sessionEpoch, actorId, companyId, planId]`. Read, never written. */
const LEGACY_REVISE_PREFIX = 'goymen.manufacturing.plan-revise.v1:'

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

interface SlotContext {
  /** The server's session binding; null = the server gave none, so nothing stored can be trusted or written. */
  readonly sessionBinding: string | null
  readonly actorId: string
  readonly companyId: string
  readonly planId: string
}

/** The context the slot key names: `[sessionBinding, actorId, companyId, planId]` (see {@link pendingPlanLifecycleKey}). */
function slotContext(key: string): SlotContext | null {
  try {
    const parsed: unknown = JSON.parse(key)
    if (!Array.isArray(parsed) || parsed.length !== 4) return null
    const [sessionBinding, actorId, companyId, planId] = parsed as unknown[]
    if (sessionBinding !== null && typeof sessionBinding !== 'string') return null
    if (typeof actorId !== 'string' || typeof companyId !== 'string' || typeof planId !== 'string') return null
    return { sessionBinding, actorId, companyId, planId }
  } catch {
    return null
  }
}

/** Where a context's revise is stored: the binding is in the record, so a later session finds — and refuses — it. */
function storedReviseName(context: SlotContext): string {
  return STORED_REVISE_PREFIX + JSON.stringify([context.actorId, context.companyId, context.planId])
}

/** Is any epoch-keyed v1 record of this very context still in the tab? A store that cannot be listed counts as yes. */
function legacyReviseOf(context: SlotContext): boolean {
  try {
    for (let index = 0; index < sessionStorage.length; index += 1) {
      const name = sessionStorage.key(index)
      if (name === null || !name.startsWith(LEGACY_REVISE_PREFIX)) continue
      let parsed: unknown
      try {
        parsed = JSON.parse(name.slice(LEGACY_REVISE_PREFIX.length))
      } catch {
        continue
      }
      if (Array.isArray(parsed) && parsed.length === 4 && parsed[1] === context.actorId
        && parsed[2] === context.companyId && parsed[3] === context.planId) return true
    }
    return false
  } catch {
    return true
  }
}

function decodeStoredRevise(context: SlotContext, raw: string): PendingPlanLifecycle | null {
  let value: unknown
  try {
    value = JSON.parse(raw)
  } catch {
    return null
  }
  if (!isRecord(value) || !hasKeys(value, ['actorId', 'companyId', 'planId', 'sessionBinding', 'snapshot'])) return null
  // The record must name the very context of the slot it was read from — and this very session.
  if (value.actorId !== context.actorId || value.companyId !== context.companyId || value.planId !== context.planId
    || typeof value.sessionBinding !== 'string' || value.sessionBinding !== context.sessionBinding) {
    return null
  }
  const snapshot = value.snapshot
  if (!isRecord(snapshot) || !hasKeys(snapshot, ['kind', 'idempotencyKey', 'request']) || snapshot.kind !== 'revise'
    || !guid(snapshot.idempotencyKey) || !validReviseRequest(snapshot.request)) return null
  const pending = { actorId: value.actorId, companyId: value.companyId, planId: value.planId, snapshot }
  return deepFreeze(pending as unknown as PendingPlanLifecycle)
}

function deepFreeze<T>(value: T): T {
  if (typeof value === 'object' && value !== null && !Object.isFrozen(value)) {
    Object.freeze(value)
    for (const child of Object.values(value)) deepFreeze(child)
  }
  return value
}

/** What the slot holds: proven nothing, one valid frozen attempt, or something that cannot be trusted either way. */
export type PendingPlanSlot =
  | { readonly state: 'absent' }
  | { readonly state: 'pending'; readonly pending: PendingPlanLifecycle }
  | { readonly state: 'unreadable' }

const ABSENT: PendingPlanSlot = { state: 'absent' }
const UNREADABLE: PendingPlanSlot = { state: 'unreadable' }

/**
 * The stored side alone: a read failure, a malformed record, a foreign record, another session's record, a missing
 * binding and a legacy epoch-keyed record of the same context are all `unreadable`, never absent.
 */
function storedSlot(key: string): PendingPlanSlot {
  const context = slotContext(key)
  if (context === null || context.sessionBinding === null) return UNREADABLE
  let raw: string | null
  try {
    raw = sessionStorage.getItem(storedReviseName(context))
  } catch {
    return UNREADABLE
  }
  if (legacyReviseOf(context)) return UNREADABLE
  if (raw === null) return ABSENT
  const pending = decodeStoredRevise(context, raw)
  return pending === null ? UNREADABLE : { state: 'pending', pending }
}

function sameAttempt(left: PendingPlanLifecycle, right: PendingPlanLifecycle): boolean {
  return JSON.stringify(left) === JSON.stringify(right)
}

/**
 * The slot of a context, or null when there is no context (no actor, company or plan). A null `sessionBinding` still
 * names a slot — one that is `unreadable`, so a missing binding never reads as absence.
 */
export function pendingPlanLifecycleKey(
  sessionBinding: string | null,
  actorId: string | null,
  companyId: string | null,
  planId: string,
): string | null {
  return actorId === null || companyId === null || planId.length === 0
    ? null
    : JSON.stringify([sessionBinding, actorId, companyId, planId])
}

/** The slot of a context: the in-memory attempt first, then — after a reload — the stored one, strictly decoded. */
export function readPendingPlanLifecycle(key: string | null): PendingPlanSlot {
  if (key === null) return ABSENT
  // Memory is keyed by the binding too: an attempt held for an earlier session in this document is not this one's.
  const held = pendingLifecycle.get(key)
  if (held !== undefined) return { state: 'pending', pending: held }
  const stored = storedSlot(key)
  if (stored.state === 'pending') pendingLifecycle.set(key, stored.pending)
  return stored
}

export type PlanLifecycleReservation =
  /** The attempt holds the slot (a revise also durably stored): it may be sent. */
  | { readonly type: 'reserved' }
  /** Another unresolved revise holds the slot; only that exact attempt may be sent. */
  | { readonly type: 'occupied'; readonly pending: PendingPlanLifecycle }
  /** The slot cannot be read or holds an untrusted record: nothing may be sent for it. */
  | { readonly type: 'unreadable' }
  /** The revise could not be stored for a reload: it is not sent and not held. */
  | { readonly type: 'not-durable' }

/**
 * Reserve before sending so a route remount — or, for a revise, a page reload — cannot mint a second command. Nothing
 * may be sent unless this returns `reserved`.
 */
export function reservePendingPlanLifecycle(key: string | null, pending: PendingPlanLifecycle): PlanLifecycleReservation {
  if (key === null) return { type: 'unreadable' }
  const stored = storedSlot(key)
  if (stored.state === 'unreadable') return { type: 'unreadable' }
  const held = pendingLifecycle.get(key) ?? (stored.state === 'pending' ? stored.pending : undefined)
  if (held !== undefined && held.snapshot.kind === 'revise' && !sameAttempt(held, pending)) {
    return { type: 'occupied', pending: held }
  }
  if (pending.snapshot.kind === 'revise' && !(stored.state === 'pending' && sameAttempt(stored.pending, pending))) {
    const context = slotContext(key)
    if (context === null || context.sessionBinding === null) return { type: 'unreadable' }
    const name = storedReviseName(context)
    const raw = JSON.stringify({ ...pending, sessionBinding: context.sessionBinding })
    try {
      sessionStorage.setItem(name, raw)
      if (sessionStorage.getItem(name) !== raw) return { type: 'not-durable' }
    } catch {
      return { type: 'not-durable' }
    }
  }
  pendingLifecycle.set(key, pending)
  return { type: 'reserved' }
}

/**
 * Release only the command that settled; a newer frozen command in the same slot is never removed. Memory follows
 * storage: if the stored attempt cannot be proven gone, the same attempt stays held (it can only replay its own key and
 * body).
 */
export function releasePendingPlanLifecycle(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  const held = pendingLifecycle.get(key)
  const stored = storedSlot(key)
  if (stored.state === 'unreadable' && held?.snapshot.kind === 'revise') return
  const context = slotContext(key)
  if (stored.state === 'pending' && stored.pending.snapshot.idempotencyKey === idempotencyKey && context !== null) {
    try {
      sessionStorage.removeItem(storedReviseName(context))
    } catch {
      return
    }
  }
  if (held?.snapshot.idempotencyKey === idempotencyKey) pendingLifecycle.delete(key)
}

/** Test isolation only. */
export function clearPendingPlanLifecycles(): void {
  pendingLifecycle.clear()
  try {
    for (const name of Object.keys(sessionStorage)) {
      if (name.startsWith(STORED_REVISE_PREFIX) || name.startsWith(LEGACY_REVISE_PREFIX)) sessionStorage.removeItem(name)
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
