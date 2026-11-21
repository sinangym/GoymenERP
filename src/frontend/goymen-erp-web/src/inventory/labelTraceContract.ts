/**
 * STK-S33 (KLN-D44, KLN-D41) cross-module label trace (`GET /api/v1/inventory/labels/{labelId}/trace`,
 * `InventoryLabelTraceV1`) and its label search (`GET /api/v1/inventory/label-trace/search`, `InventoryLabelTraceSearchV1`).
 *
 * Strict, fail-closed decoders: exact keys, closed enums, canonical kg text, UTC instants and the same cross-references the
 * owner builds (the root is the first node at depth 0, every link and event names a walked label). The production shares
 * and the parties are the owners' own answers passed through; nothing is recomputed or guessed here.
 */
import { isCanonicalKg, MOVEMENT_KINDS, type InventoryMovementKind } from './movementTimelineContract'

export const LABEL_TRACE_DIRECTIONS = ['forward', 'backward'] as const
export type LabelTraceDirection = typeof LABEL_TRACE_DIRECTIONS[number]

export const LABEL_TRACE_STATUSES = ['Answered', 'NotAnswered', 'Partial', 'Denied', 'Unavailable'] as const
export type LabelTraceStatus = typeof LABEL_TRACE_STATUSES[number]

export const LABEL_TRACE_STEPS = [
  'SupplierReceipt', 'SupplierReturn', 'ProductionIssue', 'ProductionReturn', 'ProductionOutput',
  'ProductionOutputReversal', 'Shipment', 'CustomerReturn',
] as const
export type LabelTraceStep = typeof LABEL_TRACE_STEPS[number]

export const LABEL_TRACE_LINK_KINDS = ['Split', 'Merge', 'Manufacturing'] as const
export type LabelTraceLinkKind = typeof LABEL_TRACE_LINK_KINDS[number]

export type LabelTracePartyRole = 'Supplier' | 'Customer'
export type LabelTraceMatch = 'LabelCode' | 'SupplierLabelNumber'

export interface LabelTraceNode {
  readonly labelId: string
  readonly code: string
  readonly depth: number
}

export interface LabelTraceLink {
  readonly transformationId: string
  readonly kind: LabelTraceLinkKind
  readonly occurredAtUtc: string
  readonly sourceLabelId: string
  readonly outputLabelId: string
  readonly sourceQuantityKg: string
  readonly outputQuantityKg: string
  readonly productionOrderId: string | null
  readonly consumedKg: string | null
  readonly consumedShare: string | null
  readonly shareStatus: string | null
}

export interface LabelTraceEvent {
  readonly movementId: string
  readonly lineNumber: number
  readonly labelId: string
  readonly step: LabelTraceStep
  readonly movementKind: InventoryMovementKind
  readonly occurredAtUtc: string
  readonly sourceKind: string
  readonly sourceId: string
  readonly sourceLineId: string | null
  readonly direction: 'In' | 'Out'
  readonly quantityKg: string
  readonly partyId: string | null
  readonly supplierLabelNumber: string | null
}

export interface LabelTraceParty {
  readonly partyId: string
  readonly role: LabelTracePartyRole
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

export interface InventoryLabelTrace {
  readonly labelId: string
  readonly labelCode: string
  readonly direction: LabelTraceDirection
  readonly labels: readonly LabelTraceNode[]
  readonly links: readonly LabelTraceLink[]
  readonly events: readonly LabelTraceEvent[]
  readonly parties: readonly LabelTraceParty[]
  readonly productionStatus: LabelTraceStatus
  readonly partyStatus: LabelTraceStatus
  readonly partyNameStatus: LabelTraceStatus
  readonly windowFull: boolean
}

export interface LabelTraceSearchItem {
  readonly labelId: string
  readonly code: string
  readonly supplierLabelNumber: string | null
  readonly matchedBy: LabelTraceMatch
}

export interface InventoryLabelTraceSearch {
  readonly items: readonly LabelTraceSearchItem[]
  readonly windowFull: boolean
  readonly supplierSearchStatus: LabelTraceStatus
}

/** Owner read bounds (`InventoryLabelTraceApiV1`): technical windows, not business rules. */
export const LABEL_TRACE_MAX_LABELS = 200
export const LABEL_TRACE_MAX_EVENTS = 200
export const LABEL_TRACE_MAX_SEARCH_RESULTS = 25
export const LABEL_TRACE_MIN_SEARCH_LENGTH = 2
export const LABEL_TRACE_MAX_SEARCH_LENGTH = 64
const MAX_LABEL_CODE_LENGTH = 64
const MAX_SUPPLIER_LABEL_NUMBER_LENGTH = 64
const MAX_SOURCE_KIND_LENGTH = 64
const MAX_OWNER_TEXT_LENGTH = 400

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** UTC instant as the owner writes it: an explicit `Z` or `+00:00` offset. */
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$/
/** The owner's exact non-negative decimal as invariant text without trailing zeros (formatting only, never rounded). */
const PLAIN_DECIMAL = /^(0|[1-9]\d*)(\.\d*[1-9])?$/

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

function nullableGuid(value: unknown): value is string | null {
  return value === null || guid(value)
}

function text(value: unknown, max: number): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= max && value === value.trim() &&
    !hasControl(value)
}

function nullableText(value: unknown, max: number): value is string | null {
  return value === null || text(value, max)
}

function instant(value: unknown): value is string {
  return typeof value === 'string' && UTC_INSTANT.test(value) && Number.isFinite(Date.parse(value))
}

function plainDecimal(value: unknown): value is string {
  return typeof value === 'string' && PLAIN_DECIMAL.test(value)
}

function share(value: unknown): value is string | null {
  return value === null || (plainDecimal(value) && Number(value) <= 1)
}

function oneOf<T extends string>(values: readonly T[], value: unknown): value is T {
  return typeof value === 'string' && (values as readonly string[]).includes(value)
}

function sameId(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase()
}

function node(value: unknown): LabelTraceNode | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelId', 'code', 'depth']) || !guid(source.labelId) ||
    !text(source.code, MAX_LABEL_CODE_LENGTH) || !Number.isSafeInteger(source.depth) || (source.depth as number) < 0) {
    return null
  }
  return { labelId: source.labelId, code: source.code, depth: source.depth as number }
}

function link(value: unknown): LabelTraceLink | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'transformationId', 'kind', 'occurredAtUtc', 'sourceLabelId', 'outputLabelId', 'sourceQuantityKg', 'outputQuantityKg',
    'productionOrderId', 'consumedKg', 'consumedShare', 'shareStatus',
  ]) || !guid(source.transformationId) || !oneOf(LABEL_TRACE_LINK_KINDS, source.kind) || !instant(source.occurredAtUtc) ||
    !guid(source.sourceLabelId) || !guid(source.outputLabelId) || !isCanonicalKg(source.sourceQuantityKg) ||
    !isCanonicalKg(source.outputQuantityKg) || !nullableGuid(source.productionOrderId) ||
    !(source.consumedKg === null || plainDecimal(source.consumedKg)) || !share(source.consumedShare) ||
    !nullableText(source.shareStatus, MAX_OWNER_TEXT_LENGTH)) return null
  // The production owner's answer is all-or-nothing per edge and exists only on a manufacturing edge.
  const answered = source.productionOrderId !== null
  if (answered && (source.kind !== 'Manufacturing' || source.consumedKg === null || source.shareStatus === null)) return null
  if (!answered && (source.consumedKg !== null || source.consumedShare !== null || source.shareStatus !== null)) return null
  return {
    transformationId: source.transformationId,
    kind: source.kind,
    occurredAtUtc: source.occurredAtUtc,
    sourceLabelId: source.sourceLabelId,
    outputLabelId: source.outputLabelId,
    sourceQuantityKg: source.sourceQuantityKg,
    outputQuantityKg: source.outputQuantityKg,
    productionOrderId: source.productionOrderId,
    consumedKg: source.consumedKg as string | null,
    consumedShare: source.consumedShare,
    shareStatus: source.shareStatus,
  }
}

function event(value: unknown): LabelTraceEvent | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'movementId', 'lineNumber', 'labelId', 'step', 'movementKind', 'occurredAtUtc', 'sourceKind', 'sourceId', 'sourceLineId',
    'direction', 'quantityKg', 'partyId', 'supplierLabelNumber',
  ]) || !guid(source.movementId) || !Number.isSafeInteger(source.lineNumber) || (source.lineNumber as number) < 1 ||
    !guid(source.labelId) || !oneOf(LABEL_TRACE_STEPS, source.step) || !oneOf(MOVEMENT_KINDS, source.movementKind) ||
    !instant(source.occurredAtUtc) || !text(source.sourceKind, MAX_SOURCE_KIND_LENGTH) || !guid(source.sourceId) ||
    !nullableGuid(source.sourceLineId) || (source.direction !== 'In' && source.direction !== 'Out') ||
    !isCanonicalKg(source.quantityKg) || !nullableGuid(source.partyId) ||
    !nullableText(source.supplierLabelNumber, MAX_SUPPLIER_LABEL_NUMBER_LENGTH)) return null
  // A party exists only for a receipt (supplier) or a shipment (customer); the supplier's number only for a receipt.
  if (source.partyId !== null && source.step !== 'SupplierReceipt' && source.step !== 'Shipment') return null
  if (source.supplierLabelNumber !== null && source.step !== 'SupplierReceipt') return null
  return {
    movementId: source.movementId,
    lineNumber: source.lineNumber as number,
    labelId: source.labelId,
    step: source.step,
    movementKind: source.movementKind,
    occurredAtUtc: source.occurredAtUtc,
    sourceKind: source.sourceKind,
    sourceId: source.sourceId,
    sourceLineId: source.sourceLineId,
    direction: source.direction,
    quantityKg: source.quantityKg,
    partyId: source.partyId,
    supplierLabelNumber: source.supplierLabelNumber,
  }
}

function party(value: unknown): LabelTraceParty | null {
  const source = record(value)
  if (source === null || !exact(source, ['partyId', 'role', 'code', 'legalName', 'tradeName']) || !guid(source.partyId) ||
    (source.role !== 'Supplier' && source.role !== 'Customer') || !text(source.code, MAX_OWNER_TEXT_LENGTH) ||
    !text(source.legalName, MAX_OWNER_TEXT_LENGTH) || !nullableText(source.tradeName, MAX_OWNER_TEXT_LENGTH)) return null
  return {
    partyId: source.partyId, role: source.role, code: source.code, legalName: source.legalName, tradeName: source.tradeName,
  }
}

function list<T>(value: unknown, max: number, decode: (item: unknown) => T | null): T[] | null {
  if (!Array.isArray(value) || value.length > max) return null
  const items: T[] = []
  for (const item of value) {
    const decoded = decode(item)
    if (decoded === null) return null
    items.push(decoded)
  }
  return items
}

/** The role the owner answers for a trace step's party: the supplier of a receipt, the customer of a shipment. */
export function partyRoleOf(step: LabelTraceStep): LabelTracePartyRole | null {
  if (step === 'SupplierReceipt') return 'Supplier'
  if (step === 'Shipment') return 'Customer'
  return null
}

export function decodeInventoryLabelTrace(
  value: unknown,
  expectedLabelId: string,
  expectedDirection: LabelTraceDirection,
): InventoryLabelTrace | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'labelId', 'labelCode', 'direction', 'labels', 'links', 'events', 'parties', 'productionStatus', 'partyStatus',
    'partyNameStatus', 'windowFull',
  ]) || !guid(source.labelId) || !sameId(source.labelId, expectedLabelId) || !text(source.labelCode, MAX_LABEL_CODE_LENGTH) ||
    source.direction !== expectedDirection || !oneOf(LABEL_TRACE_STATUSES, source.productionStatus) ||
    !oneOf(LABEL_TRACE_STATUSES, source.partyStatus) || !oneOf(LABEL_TRACE_STATUSES, source.partyNameStatus) ||
    typeof source.windowFull !== 'boolean') return null

  const labels = list(source.labels, LABEL_TRACE_MAX_LABELS, node)
  // The walk itself is bounded by labels, so its edges are bounded by the same window per walked label pair.
  const links = list(source.links, LABEL_TRACE_MAX_LABELS * LABEL_TRACE_MAX_LABELS, link)
  const events = list(source.events, LABEL_TRACE_MAX_EVENTS, event)
  const parties = list(source.parties, LABEL_TRACE_MAX_EVENTS, party)
  if (labels === null || links === null || events === null || parties === null || labels.length === 0) return null

  const root = labels[0]
  if (!sameId(root.labelId, source.labelId) || root.code !== source.labelCode || root.depth !== 0) return null
  const walked = new Set<string>()
  for (const item of labels) {
    const id = item.labelId.toLowerCase()
    if (walked.has(id) || (item !== root && item.depth === 0)) return null
    walked.add(id)
  }
  const linkKeys = new Set<string>()
  for (const item of links) {
    const key = `${item.transformationId}:${item.sourceLabelId}:${item.outputLabelId}`.toLowerCase()
    if (linkKeys.has(key) || !walked.has(item.sourceLabelId.toLowerCase()) || !walked.has(item.outputLabelId.toLowerCase())) {
      return null
    }
    linkKeys.add(key)
  }
  const eventKeys = new Set<string>()
  for (const item of events) {
    const key = `${item.movementId}:${item.lineNumber}`.toLowerCase()
    if (eventKeys.has(key) || !walked.has(item.labelId.toLowerCase())) return null
    eventKeys.add(key)
  }
  const partyKeys = new Set<string>()
  for (const item of parties) {
    const key = `${item.role}:${item.partyId.toLowerCase()}`
    // A named party is one an event of the same role names; the owner names nobody else.
    if (partyKeys.has(key) || !events.some((entry) => entry.partyId !== null && sameId(entry.partyId, item.partyId) &&
      partyRoleOf(entry.step) === item.role)) return null
    partyKeys.add(key)
  }

  return {
    labelId: source.labelId,
    labelCode: source.labelCode,
    direction: expectedDirection,
    labels,
    links,
    events,
    parties,
    productionStatus: source.productionStatus,
    partyStatus: source.partyStatus,
    partyNameStatus: source.partyNameStatus,
    windowFull: source.windowFull,
  }
}

function searchItem(value: unknown): LabelTraceSearchItem | null {
  const source = record(value)
  if (source === null || !exact(source, ['labelId', 'code', 'supplierLabelNumber', 'matchedBy']) || !guid(source.labelId) ||
    !text(source.code, MAX_LABEL_CODE_LENGTH) ||
    !nullableText(source.supplierLabelNumber, MAX_SUPPLIER_LABEL_NUMBER_LENGTH) ||
    (source.matchedBy !== 'LabelCode' && source.matchedBy !== 'SupplierLabelNumber') ||
    (source.matchedBy === 'SupplierLabelNumber' && source.supplierLabelNumber === null)) return null
  return {
    labelId: source.labelId, code: source.code, supplierLabelNumber: source.supplierLabelNumber, matchedBy: source.matchedBy,
  }
}

export function decodeInventoryLabelTraceSearch(value: unknown): InventoryLabelTraceSearch | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'windowFull', 'supplierSearchStatus']) ||
    typeof source.windowFull !== 'boolean' || !oneOf(LABEL_TRACE_STATUSES, source.supplierSearchStatus)) return null
  const items = list(source.items, LABEL_TRACE_MAX_SEARCH_RESULTS, searchItem)
  if (items === null) return null
  const seen = new Set<string>()
  for (const item of items) {
    const id = item.labelId.toLowerCase()
    if (seen.has(id)) return null
    seen.add(id)
  }
  return { items, windowFull: source.windowFull, supplierSearchStatus: source.supplierSearchStatus }
}

/**
 * The owner's own search-text rule, checked before a request so an obviously invalid text costs no round trip: trimmed,
 * 2..64 characters, no control characters. The owner re-checks it and answers 400 with its closed code.
 */
export function normalizeLabelTraceSearchText(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length >= LABEL_TRACE_MIN_SEARCH_LENGTH && trimmed.length <= LABEL_TRACE_MAX_SEARCH_LENGTH &&
    !hasControl(trimmed) ? trimmed : null
}
