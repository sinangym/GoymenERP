/**
 * ST-04 company movement timeline (`GET /api/v1/inventory/movements`, `InventoryMovementTimelinePageV1`).
 *
 * A strict, fail-closed decoder: exact keys, closed enums, canonical kg text, and the same per-kind line shape the
 * owner validates before it answers. It reads Inventory's immutable movements as stored; it infers no reversal, no
 * source-module fact and no label filter.
 */

export const MOVEMENT_KINDS = [
  'Receipt', 'Issue', 'Block', 'Release', 'Return', 'CountAdjustment', 'Scrap', 'TransferDispatch', 'TransferAccept',
] as const
export type InventoryMovementKind = typeof MOVEMENT_KINDS[number]

export interface InventoryMovementLine {
  readonly sequence: number
  readonly direction: 'In' | 'Out'
  readonly labelId: string
  readonly labelCode: string
  readonly ownerKind: 'Company' | 'Counterparty'
  readonly ownerId: string
  readonly custodianKind: 'Company' | 'Counterparty'
  readonly custodianId: string
  readonly locationId: string
  readonly productId: string
  readonly block: 'Free' | 'Blocked'
  readonly quantityKg: string
}

export interface InventoryMovement {
  readonly movementId: string
  readonly kind: InventoryMovementKind
  readonly occurredAtUtc: string
  readonly sourceKind: string
  readonly sourceId: string
  readonly sourceLineId: string | null
  readonly sourceGrain: 'Whole' | 'Line'
  readonly lines: readonly InventoryMovementLine[]
}

export interface InventoryMovementTimelinePage {
  readonly items: readonly InventoryMovement[]
  readonly nextCursor: string | null
}

/** Owner bounds: `limit` is 1..100 and a cursor is at most 4096 characters. */
export const MOVEMENT_TIMELINE_MAX_LIMIT = 100
const MAX_CURSOR_LENGTH = 4096
const MAX_SOURCE_KIND_LENGTH = 64
const MAX_LABEL_CODE_LENGTH = 64

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** Canonical positive kg: no leading or trailing zeros, at most 3 decimals (Ö2). `0` is refused separately. */
const CANONICAL_KG = /^(0|[1-9]\d*)(\.\d{0,2}[1-9])?$/
/** UTC instant as the owner writes it: an explicit `Z` or `+00:00` offset. */
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$/
function hasControl(value: string): boolean {
  for (const character of value) {
    const code = character.codePointAt(0)!
    if (code <= 0x1f || (code >= 0x7f && code <= 0x9f)) return true
  }
  return false
}

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function guid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID
}

function trimmedText(value: unknown, max: number): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= max && value === value.trim() &&
    !hasControl(value)
}

function instant(value: unknown): value is string {
  return typeof value === 'string' && UTC_INSTANT.test(value) && Number.isFinite(Date.parse(value))
}

export function isCanonicalKg(value: unknown): value is string {
  return typeof value === 'string' && CANONICAL_KG.test(value) && value !== '0'
}

function party(value: unknown): value is 'Company' | 'Counterparty' {
  return value === 'Company' || value === 'Counterparty'
}

function line(value: unknown): InventoryMovementLine | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'sequence', 'direction', 'labelId', 'labelCode', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId',
    'locationId', 'productId', 'block', 'quantityKg',
  ]) || !Number.isSafeInteger(source.sequence) || (source.sequence as number) < 1 ||
    (source.direction !== 'In' && source.direction !== 'Out') || !guid(source.labelId) ||
    !trimmedText(source.labelCode, MAX_LABEL_CODE_LENGTH) ||
    !party(source.ownerKind) || !guid(source.ownerId) || !party(source.custodianKind) || !guid(source.custodianId) ||
    !guid(source.locationId) || !guid(source.productId) ||
    (source.block !== 'Free' && source.block !== 'Blocked') || !isCanonicalKg(source.quantityKg)) return null
  return {
    sequence: source.sequence as number,
    direction: source.direction,
    labelId: source.labelId,
    labelCode: source.labelCode,
    ownerKind: source.ownerKind,
    ownerId: source.ownerId,
    custodianKind: source.custodianKind,
    custodianId: source.custodianId,
    locationId: source.locationId,
    productId: source.productId,
    block: source.block,
    quantityKg: source.quantityKg,
  }
}

function validBlockChange(kind: 'Block' | 'Release', lines: readonly InventoryMovementLine[]): boolean {
  if (lines.length !== 2) return false
  const from = lines.filter((item) => item.direction === 'Out')
  const to = lines.filter((item) => item.direction === 'In')
  if (from.length !== 1 || to.length !== 1) return false
  const [a, b] = [from[0], to[0]]
  return a.block === (kind === 'Block' ? 'Free' : 'Blocked') && b.block === (kind === 'Block' ? 'Blocked' : 'Free') &&
    a.labelId === b.labelId && a.labelCode === b.labelCode &&
    a.ownerKind === b.ownerKind && a.ownerId === b.ownerId &&
    a.custodianKind === b.custodianKind && a.custodianId === b.custodianId &&
    a.locationId === b.locationId && a.productId === b.productId && a.quantityKg === b.quantityKg
}

function linesFitKind(kind: InventoryMovementKind, lines: readonly InventoryMovementLine[]): boolean {
  switch (kind) {
    case 'Receipt':
    case 'Return':
    case 'TransferAccept':
      return lines.every((item) => item.direction === 'In')
    case 'Issue':
      return lines.every((item) => item.direction === 'Out' && item.block === 'Free')
    case 'TransferDispatch':
      return lines.every((item) => item.direction === 'Out')
    case 'Scrap':
      return lines.length === 1 && lines[0].direction === 'Out'
    case 'CountAdjustment':
      return lines.length === 1
    case 'Block':
    case 'Release':
      return validBlockChange(kind, lines)
  }
}

function movement(value: unknown): InventoryMovement | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'movementId', 'kind', 'occurredAtUtc', 'sourceKind', 'sourceId', 'sourceLineId', 'sourceGrain', 'lines',
  ]) || !guid(source.movementId) || !(MOVEMENT_KINDS as readonly unknown[]).includes(source.kind) ||
    !instant(source.occurredAtUtc) || !trimmedText(source.sourceKind, MAX_SOURCE_KIND_LENGTH) ||
    !guid(source.sourceId) || (source.sourceLineId !== null && !guid(source.sourceLineId)) ||
    (source.sourceGrain !== 'Whole' && source.sourceGrain !== 'Line') ||
    (source.sourceGrain === 'Whole') !== (source.sourceLineId === null) ||
    !Array.isArray(source.lines) || source.lines.length === 0) return null
  const decoded = source.lines.map(line)
  if (decoded.some((item) => item === null)) return null
  const lines = decoded as InventoryMovementLine[]
  if (lines.some((item, index) => index > 0 && lines[index - 1].sequence >= item.sequence)) return null
  const kind = source.kind as InventoryMovementKind
  if (!linesFitKind(kind, lines)) return null
  return {
    movementId: source.movementId,
    kind,
    occurredAtUtc: source.occurredAtUtc,
    sourceKind: source.sourceKind,
    sourceId: source.sourceId,
    sourceLineId: source.sourceLineId as string | null,
    sourceGrain: source.sourceGrain,
    lines,
  }
}

/**
 * One owner page. `limit` is the limit the request asked for: a page longer than it, a duplicate movement, an
 * out-of-order (older-before-newer) entry, or a continuation without any item is refused as malformed.
 */
export function decodeInventoryMovementTimelinePage(value: unknown, limit: number): InventoryMovementTimelinePage | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'nextCursor']) || !Array.isArray(source.items) ||
    source.items.length > limit ||
    (source.nextCursor !== null && !trimmedText(source.nextCursor, MAX_CURSOR_LENGTH)) ||
    (source.nextCursor !== null && source.items.length === 0)) return null
  const decoded = source.items.map(movement)
  if (decoded.some((item) => item === null)) return null
  const items = decoded as InventoryMovement[]
  if (new Set(items.map((item) => item.movementId.toLowerCase())).size !== items.length ||
    items.some((item, index) => index > 0 &&
      Date.parse(items[index - 1].occurredAtUtc) < Date.parse(item.occurredAtUtc))) return null
  return { items, nextCursor: source.nextCursor as string | null }
}

/**
 * A continuation must extend the pages already shown: no movement twice, and nothing newer than the last one shown.
 * Returns false when the owner's answer cannot honestly follow what is on screen.
 */
export function continuesTimeline(
  shown: readonly InventoryMovement[],
  next: readonly InventoryMovement[],
): boolean {
  if (shown.length === 0 || next.length === 0) return true
  const seen = new Set(shown.map((item) => item.movementId.toLowerCase()))
  return next.every((item) => !seen.has(item.movementId.toLowerCase())) &&
    Date.parse(shown[shown.length - 1].occurredAtUtc) >= Date.parse(next[0].occurredAtUtc)
}
