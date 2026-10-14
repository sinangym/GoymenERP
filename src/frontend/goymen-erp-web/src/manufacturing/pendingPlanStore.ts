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

export function pendingPlanCreateKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
): string | null {
  return actorId === null || companyId === null
    ? null
    : `goymen.manufacturing.plan-create.v1:${sessionEpoch}:${actorId}:${companyId}`
}

function decode(value: unknown): PendingPlanCreate | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingPlanCreate>
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
  return deepFreeze(candidate as PendingPlanCreate)
}

export function recallPendingPlanCreate(key: string | null): PendingPlanCreate | null {
  if (key === null) return null
  try {
    const raw = sessionStorage.getItem(key)
    return raw === null ? null : decode(JSON.parse(raw))
  } catch {
    return null
  }
}

export function reservePendingPlanCreate(key: string | null, snapshot: PendingPlanCreate): void {
  if (key === null) return
  try {
    sessionStorage.setItem(key, JSON.stringify(snapshot))
  } catch {
    // Component state still keeps the immutable attempt for this mount.
  }
}

export function releasePendingPlanCreate(key: string | null, idempotencyKey: string): void {
  if (key === null || recallPendingPlanCreate(key)?.idempotencyKey !== idempotencyKey) return
  try {
    sessionStorage.removeItem(key)
  } catch {
    // A stale stored attempt remains fail-closed and can only replay the same request.
  }
}
