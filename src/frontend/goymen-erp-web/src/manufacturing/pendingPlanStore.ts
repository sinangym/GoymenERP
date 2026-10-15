import type { SavePlanRequest } from './planCommandApi'

export interface PendingPlanCreate {
  readonly actorId: string
  readonly companyId: string
  readonly idempotencyKey: string
  readonly request: SavePlanRequest
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const VERSION = /^[1-9][0-9]*$/
const KG = /^(0|[1-9][0-9]{0,17})(\.[0-9]{1,20})?$/
const MM = /^(?=.*[1-9])(?:0|[1-9][0-9]*)(?:\.[0-9]{1,3})?$/
const INT32_MAX = 2_147_483_647
const INT32_MIN = -2_147_483_648
const NIL_UUID = '00000000-0000-0000-0000-000000000000'

function exactKeys(value: object, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => own.includes(key))
}

/**
 * M01.3: an authored cut definition is replayed only as the strict `CutToLengthSpecRequestV1` it was sent as: absent
 * (no lines), or at least one line with a non-nil unique id, a signed int32 sequence, a positive length with at most
 * three decimals and a positive int32 count — nothing else. A stored null or empty set is refused (never a clear).
 */
function validCutToLengthSpec(value: unknown): boolean {
  if (value === undefined) return true
  if (typeof value !== 'object' || value === null || Array.isArray(value) || !exactKeys(value, ['lines'])) return false
  const lines = (value as { readonly lines: unknown }).lines
  if (!Array.isArray(lines) || lines.length === 0) return false
  const ids = new Set<string>()
  for (const line of lines as unknown[]) {
    if (typeof line !== 'object' || line === null || Array.isArray(line)
      || !exactKeys(line, ['lineId', 'sequence', 'lengthMm', 'count'])) return false
    const { lineId, sequence, lengthMm, count } = line as Record<string, unknown>
    if (typeof lineId !== 'string' || !UUID.test(lineId) || lineId === NIL_UUID || ids.has(lineId.toLowerCase())) return false
    ids.add(lineId.toLowerCase())
    if (typeof sequence !== 'number' || !Number.isInteger(sequence) || sequence < INT32_MIN || sequence > INT32_MAX) return false
    if (typeof lengthMm !== 'string' || !MM.test(lengthMm)) return false
    if (typeof count !== 'number' || !Number.isInteger(count) || count <= 0 || count > INT32_MAX) return false
  }
  return true
}

/** The recalled attempt is immutable: a later UI change cannot reach the request it replays. */
function deepFreeze<T>(value: T): T {
  if (typeof value === 'object' && value !== null && !Object.isFrozen(value)) {
    Object.freeze(value)
    for (const child of Object.values(value)) deepFreeze(child)
  }
  return value
}

function validSlittingSpec(value: SavePlanRequest['slittingSpec']): boolean {
  // This is an additive request field: legacy snapshots may omit it or store explicit null.
  if (value === undefined || value === null) return true
  if (typeof value !== 'object' || Array.isArray(value) || !Array.isArray(value.lines) || value.lines.length === 0) {
    return false
  }
  if (value.windingTargetKg !== null
    && (!KG.test(value.windingTargetKg) || !/[1-9]/.test(value.windingTargetKg))) return false
  return value.lines.every((line) =>
    typeof line === 'object'
    && line !== null
    && !Array.isArray(line)
    && typeof line.bandWidthMm === 'string'
    && MM.test(line.bandWidthMm)
    && typeof line.count === 'number'
    && Number.isSafeInteger(line.count)
    && line.count > 0
    && line.count <= INT32_MAX)
}

/**
 * M01.3 (K2 #728/6180459736, r5 #66/6187473245): the create slot of an actor and company, bound to the SERVER's session
 * binding (`state.sessionBinding`, from `/identity/me`) — not to `sessionEpoch`, which restarts at 0 in every document,
 * and not to anything the client minted or stored. null = no context. A null binding (the server gave none) still names
 * a slot, and that slot is `unreadable`: a missing binding never reads as absence and never lets a create be stored or sent.
 */
export function pendingPlanCreateKey(
  sessionBinding: string | null,
  actorId: string | null,
  companyId: string | null,
): string | null {
  return actorId === null || companyId === null ? null : JSON.stringify([sessionBinding, actorId, companyId])
}

/**
 * Where an actor's and company's create is stored. The server binding is in the record, so a later server session of the
 * same actor finds — and refuses — it, whatever happened to local storage in between.
 */
const STORED_CREATE_PREFIX = 'goymen.manufacturing.plan-create.v2:'
/** Epoch-keyed slots written before the binding: `…v1:${sessionEpoch}:${actorId}:${companyId}`. Read, never written. */
const LEGACY_CREATE_PREFIX = 'goymen.manufacturing.plan-create.v1:'

interface CreateSlotContext {
  readonly sessionBinding: string | null
  readonly actorId: string
  readonly companyId: string
}

function slotContext(key: string): CreateSlotContext | null {
  try {
    const parsed: unknown = JSON.parse(key)
    if (!Array.isArray(parsed) || parsed.length !== 3) return null
    const [sessionBinding, actorId, companyId] = parsed as unknown[]
    if (sessionBinding !== null && typeof sessionBinding !== 'string') return null
    if (typeof actorId !== 'string' || typeof companyId !== 'string') return null
    return { sessionBinding, actorId, companyId }
  } catch {
    return null
  }
}

function storedCreateName(context: CreateSlotContext): string {
  return STORED_CREATE_PREFIX + JSON.stringify([context.actorId, context.companyId])
}

/** Is any epoch-keyed v1 create of this actor and company still in the tab? A store that cannot be listed counts as yes. */
function legacyCreateOf(context: CreateSlotContext): boolean {
  const suffix = `:${context.actorId}:${context.companyId}`
  try {
    for (let index = 0; index < sessionStorage.length; index += 1) {
      const name = sessionStorage.key(index)
      if (name === null || !name.startsWith(LEGACY_CREATE_PREFIX) || !name.endsWith(suffix)) continue
      if (/^[0-9]+$/.test(name.slice(LEGACY_CREATE_PREFIX.length, name.length - suffix.length))) return true
    }
    return false
  } catch {
    return true
  }
}

function decode(value: unknown, context: CreateSlotContext): PendingPlanCreate | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  if (!exactKeys(value, ['actorId', 'companyId', 'idempotencyKey', 'request', 'sessionBinding'])) return null
  const candidate = value as Partial<PendingPlanCreate> & { readonly sessionBinding?: unknown }
  // The record must name this very actor, company and session.
  if (
    candidate.actorId !== context.actorId ||
    candidate.companyId !== context.companyId ||
    typeof candidate.sessionBinding !== 'string' ||
    candidate.sessionBinding !== context.sessionBinding
  ) {
    return null
  }
  const request = candidate.request
  if (
    typeof candidate.actorId !== 'string' ||
    candidate.actorId.length === 0 ||
    typeof candidate.companyId !== 'string' ||
    !UUID.test(candidate.companyId) ||
    typeof candidate.idempotencyKey !== 'string' ||
    !UUID.test(candidate.idempotencyKey) ||
    typeof request !== 'object' ||
    request === null ||
    Array.isArray(request) ||
    !UUID.test(request.planId) ||
    !UUID.test(request.workCenterId) ||
    !Array.isArray(request.inputs) ||
    request.inputs.length === 0
  ) {
    return null
  }
  if (
    request.inputs.some(
      (input) =>
        !UUID.test(input.parentRollId) ||
        !VERSION.test(input.expectedLabelVersion) ||
        !KG.test(input.plannedKg) ||
        !/[1-9]/.test(input.plannedKg),
    ) ||
    new Set(request.inputs.map((input) => input.parentRollId)).size !== request.inputs.length
  ) {
    return null
  }
  if (
    request.target !== null &&
    ((request.target.basis !== 'InputMass' && request.target.basis !== 'GoodOutputMass') ||
      !KG.test(request.target.kg) ||
      !/[1-9]/.test(request.target.kg))
  ) {
    return null
  }
  if (!validSlittingSpec(request.slittingSpec)) return null
  if (!validCutToLengthSpec((request as { readonly cutToLengthSpec?: unknown }).cutToLengthSpec)) return null
  const pending: PendingPlanCreate = {
    actorId: candidate.actorId,
    companyId: candidate.companyId,
    idempotencyKey: candidate.idempotencyKey,
    request,
  }
  return deepFreeze(pending)
}

/** What the create slot holds: proven nothing, one valid frozen attempt, or something that cannot be trusted either way. */
export type PendingPlanCreateSlot =
  | { readonly state: 'absent' }
  | { readonly state: 'pending'; readonly pending: PendingPlanCreate }
  | { readonly state: 'unreadable' }

const ABSENT: PendingPlanCreateSlot = { state: 'absent' }
const UNREADABLE: PendingPlanCreateSlot = { state: 'unreadable' }

/**
 * M01.3 (K2 #728/6180459736), the revise slot's discipline for the create: a read failure, a malformed or foreign
 * record, another session's record, a missing binding and a legacy epoch-keyed record of the same actor and company are
 * all `unreadable` — never absent, never replayed, never deleted.
 */
export function readPendingPlanCreate(key: string | null): PendingPlanCreateSlot {
  if (key === null) return ABSENT
  const context = slotContext(key)
  if (context === null || context.sessionBinding === null) return UNREADABLE
  let raw: string | null
  try {
    raw = sessionStorage.getItem(storedCreateName(context))
  } catch {
    return UNREADABLE
  }
  if (legacyCreateOf(context)) return UNREADABLE
  if (raw === null) return ABSENT
  let value: unknown
  try {
    value = JSON.parse(raw)
  } catch {
    return UNREADABLE
  }
  const pending = decode(value, context)
  return pending === null ? UNREADABLE : { state: 'pending', pending }
}

export type PlanCreateReservation =
  /** The attempt is durably stored for a reload: it may be sent. */
  | { readonly type: 'reserved' }
  /** Another unresolved create holds the slot; only that exact attempt may be sent. */
  | { readonly type: 'occupied'; readonly pending: PendingPlanCreate }
  /** The slot cannot be read or holds an untrusted record: nothing may be sent for it. */
  | { readonly type: 'unreadable' }
  /** The attempt could not be stored and read back: it is not sent. */
  | { readonly type: 'not-durable' }

function sameAttempt(left: PendingPlanCreate, right: PendingPlanCreate): boolean {
  return JSON.stringify(left) === JSON.stringify(right)
}

/** Reserve before sending, so neither a remount nor a reload can mint a second create. Send only on `reserved`. */
export function reservePendingPlanCreate(key: string | null, snapshot: PendingPlanCreate): PlanCreateReservation {
  if (key === null) return { type: 'unreadable' }
  const slot = readPendingPlanCreate(key)
  if (slot.state === 'unreadable') return { type: 'unreadable' }
  if (slot.state === 'pending') {
    return sameAttempt(slot.pending, snapshot) ? { type: 'reserved' } : { type: 'occupied', pending: slot.pending }
  }
  const context = slotContext(key)
  if (context === null || context.sessionBinding === null) return { type: 'unreadable' }
  const name = storedCreateName(context)
  const raw = JSON.stringify({ ...snapshot, sessionBinding: context.sessionBinding })
  try {
    sessionStorage.setItem(name, raw)
    if (sessionStorage.getItem(name) !== raw) return { type: 'not-durable' }
  } catch {
    return { type: 'not-durable' }
  }
  return { type: 'reserved' }
}

/** Release only the attempt that settled; a newer one is never removed, and a failed removal keeps the same attempt. */
export function releasePendingPlanCreate(key: string | null, idempotencyKey: string): void {
  const slot = readPendingPlanCreate(key)
  const context = key === null ? null : slotContext(key)
  if (slot.state !== 'pending' || slot.pending.idempotencyKey !== idempotencyKey || context === null) return
  try {
    sessionStorage.removeItem(storedCreateName(context))
  } catch {
    // The stored attempt stays pending and can only replay the same request.
  }
}
