/**
 * SAT-D251..SAT-D255 (EK50) / API-11: the order source plan — the wire shapes of `GET/POST /sales/drafts/{id}/source-plan` and
 * the strict decoder of the read. Every quantity stays the server's canonical text: the screen sums nothing, derives no stock
 * part (SAT-D255: the allocation owner's figure) and no locked part (SAT-D253: the delivered total). A read is data only when it
 * describes exactly the detail it was read for (version, every active line, its number, unit and quantity) — else malformed.
 */
import type { DraftDetail } from './contract'
import { isReferenceId, isVersionString } from './wirePrimitives'

export const SOURCE_PLAN_SUGGESTION = {
  stock: 'Stock',
  production: 'Production',
  purchasing: 'Purchasing',
  unavailable: 'Unavailable',
} as const
export type SourcePlanSuggestion = (typeof SOURCE_PLAN_SUGGESTION)[keyof typeof SOURCE_PLAN_SUGGESTION]

export const SOURCE_PLAN_SUGGESTION_REASON = {
  allocationCoversRemainder: 'AllocationCoversRemainder',
  freeStockEnough: 'FreeStockEnough',
  freeStockUnavailable: 'FreeStockUnavailable',
  supplyTypeUnmapped: 'SupplyTypeUnmapped',
  lineDelivered: 'LineDelivered',
  deliveryUnreadable: 'DeliveryUnreadable',
  allocationUnreadable: 'AllocationUnreadable',
  unitUnsupported: 'UnitUnsupported',
} as const
export type SourcePlanSuggestionReason = (typeof SOURCE_PLAN_SUGGESTION_REASON)[keyof typeof SOURCE_PLAN_SUGGESTION_REASON]

export const SOURCE_PLAN_STOCK_STATUS = { derived: 'Derived', unreadable: 'Unreadable' } as const
export type SourcePlanStockStatus = (typeof SOURCE_PLAN_STOCK_STATUS)[keyof typeof SOURCE_PLAN_STOCK_STATUS]

export const SOURCE_PLAN_WARNING = {
  planAllocationMismatch: 'PlanAllocationMismatch',
  planDeliveryMismatch: 'PlanDeliveryMismatch',
  planQuantityMismatch: 'PlanQuantityMismatch',
  linkedSupplyOpen: 'LinkedSupplyOpen',
  linkedSupplyUnreadable: 'LinkedSupplyUnreadable',
  deliveryUnreadable: 'DeliveryUnreadable',
  allocationUnreadable: 'AllocationUnreadable',
  unitUnsupported: 'UnitUnsupported',
  noPlan: 'NoPlan',
} as const
export type SourcePlanWarning = (typeof SOURCE_PLAN_WARNING)[keyof typeof SOURCE_PLAN_WARNING]

export const SOURCE_PLAN_LOCK_REASON = {
  orderState: 'OrderState',
  lineClosed: 'LineClosed',
  lineDelivered: 'LineDelivered',
  deliveryUnreadable: 'DeliveryUnreadable',
  allocationUnreadable: 'AllocationUnreadable',
  unitUnsupported: 'UnitUnsupported',
} as const
export type SourcePlanLockReason = (typeof SOURCE_PLAN_LOCK_REASON)[keyof typeof SOURCE_PLAN_LOCK_REASON]

export const SOURCE_PLAN_SUPPLY_KIND = { production: 'Production', purchasing: 'Purchasing' } as const
export type SourcePlanSupplyKind = (typeof SOURCE_PLAN_SUPPLY_KIND)[keyof typeof SOURCE_PLAN_SUPPLY_KIND]
export const SOURCE_PLAN_LINK_STATUS = { none: 'None', present: 'Present', unreadable: 'Unreadable' } as const
export type SourcePlanLinkStatus = (typeof SOURCE_PLAN_LINK_STATUS)[keyof typeof SOURCE_PLAN_LINK_STATUS]

export interface SourcePlanSaved {
  readonly draftVersion: string
  readonly lineQuantity: string
  readonly locked: string
  readonly stock: string
  readonly production: string
  readonly purchasing: string
  readonly subcontract: string
  readonly linkedSupplyConfirmed: boolean
  readonly savedBy: string
  readonly savedAtUtc: string
}

export interface SourcePlanLine {
  readonly lineId: string
  readonly lineNo: number
  readonly orderUnitId: string
  readonly quantity: string
  /** SAT-D253: the delivered (locked) part; null when the delivery owner cannot be read. */
  readonly locked: string | null
  readonly remaining: string | null
  readonly stockStatus: SourcePlanStockStatus
  /** SAT-D255: the stock part the allocation owner answers; null ("okunamadı") exactly when `stockStatus` is Unreadable. */
  readonly stock: string | null
  readonly suggestion: SourcePlanSuggestion
  readonly suggestionReason: SourcePlanSuggestionReason
  readonly saved: SourcePlanSaved | null
  readonly writable: boolean
  readonly lockReason: SourcePlanLockReason | null
  readonly warnings: readonly SourcePlanWarning[]
}

export interface SourcePlanLinkedSupply {
  readonly kind: SourcePlanSupplyKind
  readonly status: SourcePlanLinkStatus
}

export interface SourcePlan {
  readonly version: string
  readonly revision: string
  readonly writable: boolean
  readonly linkedSupply: readonly SourcePlanLinkedSupply[]
  readonly lines: readonly SourcePlanLine[]
}

/** `SalesDraftSourcePlanRequest`: every member present; the stock and locked parts are never sent (the server derives them). */
export interface SourcePlanRequest {
  readonly expectedVersion: string
  readonly confirmLinkedSupply: boolean
  readonly lines: readonly SourcePlanLineRequest[]
}
export interface SourcePlanLineRequest {
  readonly lineId: string
  readonly production: string
  readonly purchasing: string
  readonly subcontract: string
}

type Obj = Record<string, unknown>
const DECIMAL = /^(?:0|[1-9]\d*)(?:\.\d+)?$/
const REVISION = /^Rev\.(?:0|[1-9]\d*)$/

const exact = (value: unknown, keys: readonly string[]): Obj | null => {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const object = value as Obj
  const actual = Object.keys(object)
  return actual.length === keys.length && actual.every((key) => keys.includes(key)) ? object : null
}
const decimal = (value: unknown): value is string => typeof value === 'string' && DECIMAL.test(value)
const nullableDecimal = (value: unknown): value is string | null => value === null || decimal(value)
const instant = (value: unknown): value is string =>
  typeof value === 'string' && value.trim() !== '' && value.length <= 64 && !Number.isNaN(Date.parse(value))
const oneOf = <T extends string>(value: unknown, choices: readonly T[]): value is T =>
  typeof value === 'string' && choices.includes(value as T)
const positiveInteger = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 1

/** Exact decimal order without a JS number (a 20-digit quantity must not lose digits). */
export function compareDecimalText(left: string, right: string): number {
  const [leftWhole, leftFraction = ''] = left.split('.')
  const [rightWhole, rightFraction = ''] = right.split('.')
  const scale = Math.max(leftFraction.length, rightFraction.length)
  const scaledLeft = BigInt(`${leftWhole}${leftFraction.padEnd(scale, '0')}`)
  const scaledRight = BigInt(`${rightWhole}${rightFraction.padEnd(scale, '0')}`)
  return scaledLeft < scaledRight ? -1 : scaledLeft > scaledRight ? 1 : 0
}

const SAVED_FIELDS = [
  'draftVersion', 'lineQuantity', 'locked', 'stock', 'production', 'purchasing', 'subcontract', 'linkedSupplyConfirmed',
  'savedBy', 'savedAtUtc',
] as const

function decodeSaved(value: unknown): SourcePlanSaved | null {
  const o = exact(value, SAVED_FIELDS)
  if (o === null || !isVersionString(o.draftVersion) || !decimal(o.lineQuantity) || !decimal(o.locked) || !decimal(o.stock)
    || !decimal(o.production) || !decimal(o.purchasing) || !decimal(o.subcontract)
    || typeof o.linkedSupplyConfirmed !== 'boolean' || typeof o.savedBy !== 'string' || o.savedBy.trim() === ''
    || !instant(o.savedAtUtc)) return null
  // SAT-D253: a recorded plan was balanced on an open remainder — the locked part is below the line quantity.
  if (compareDecimalText(o.locked, o.lineQuantity) >= 0) return null
  return {
    draftVersion: o.draftVersion,
    lineQuantity: o.lineQuantity,
    locked: o.locked,
    stock: o.stock,
    production: o.production,
    purchasing: o.purchasing,
    subcontract: o.subcontract,
    linkedSupplyConfirmed: o.linkedSupplyConfirmed,
    savedBy: o.savedBy,
    savedAtUtc: o.savedAtUtc,
  }
}

const LINE_FIELDS = [
  'lineId', 'lineNo', 'orderUnitId', 'quantity', 'locked', 'remaining', 'stockStatus', 'stock', 'suggestion',
  'suggestionReason', 'saved', 'writable', 'lockReason', 'warnings',
] as const

function decodeLine(value: unknown): SourcePlanLine | null {
  const o = exact(value, LINE_FIELDS)
  if (o === null || !isReferenceId(o.lineId) || !positiveInteger(o.lineNo) || !isReferenceId(o.orderUnitId)
    || !decimal(o.quantity) || !nullableDecimal(o.locked) || !nullableDecimal(o.remaining)
    || !oneOf(o.stockStatus, Object.values(SOURCE_PLAN_STOCK_STATUS)) || !nullableDecimal(o.stock)
    || !oneOf(o.suggestion, Object.values(SOURCE_PLAN_SUGGESTION))
    || !oneOf(o.suggestionReason, Object.values(SOURCE_PLAN_SUGGESTION_REASON))
    || typeof o.writable !== 'boolean'
    || !(o.lockReason === null || oneOf(o.lockReason, Object.values(SOURCE_PLAN_LOCK_REASON)))
    || !Array.isArray(o.warnings)) return null
  const warnings = o.warnings as unknown[]
  if (!warnings.every((warning) => oneOf(warning, Object.values(SOURCE_PLAN_WARNING)))
    || new Set(warnings).size !== warnings.length) return null
  // The locked part and the remainder come together (the delivery owner answered or it did not), never above the quantity.
  if ((o.locked === null) !== (o.remaining === null)) return null
  if (o.locked !== null && compareDecimalText(o.locked, o.quantity) > 0) return null
  if (o.remaining !== null && compareDecimalText(o.remaining, o.quantity) > 0) return null
  // SAT-D255: "okunamadı" carries no figure; a derived stock part always does (never a silent zero).
  if ((o.stockStatus === SOURCE_PLAN_STOCK_STATUS.unreadable) !== (o.stock === null)) return null
  // A writable line has no lock reason and every fact a save needs; a line-level lock is never writable.
  if (o.writable && o.lockReason !== null) return null
  if (o.writable && (o.locked === null || o.stock === null)) return null
  const saved = o.saved === null ? null : decodeSaved(o.saved)
  if (o.saved !== null && saved === null) return null
  if ((saved === null) !== warnings.includes(SOURCE_PLAN_WARNING.noPlan)) return null
  return {
    lineId: o.lineId,
    lineNo: o.lineNo,
    orderUnitId: o.orderUnitId,
    quantity: o.quantity,
    locked: o.locked,
    remaining: o.remaining,
    stockStatus: o.stockStatus,
    stock: o.stock,
    suggestion: o.suggestion,
    suggestionReason: o.suggestionReason,
    saved,
    writable: o.writable,
    lockReason: o.lockReason,
    warnings: warnings as SourcePlanWarning[],
  }
}

function decodeLinked(value: unknown): SourcePlanLinkedSupply | null {
  const o = exact(value, ['kind', 'status'])
  if (o === null || !oneOf(o.kind, Object.values(SOURCE_PLAN_SUPPLY_KIND))
    || !oneOf(o.status, Object.values(SOURCE_PLAN_LINK_STATUS))) return null
  return { kind: o.kind, status: o.status }
}

/**
 * The plan read of `detail`, or null: the exact key set, the detail's version, one entry per supply kind, and exactly the
 * detail's active lines (same id, number, unit and quantity text) — a plan for another revision or record is not this order's.
 */
export function decodeSourcePlan(value: unknown, detail: DraftDetail): SourcePlan | null {
  const o = exact(value, ['version', 'revision', 'writable', 'linkedSupply', 'lines'])
  if (o === null || !isVersionString(o.version) || o.version !== detail.draft.version
    || typeof o.revision !== 'string' || !REVISION.test(o.revision) || o.revision !== detail.draft.revision
    || typeof o.writable !== 'boolean' || !Array.isArray(o.linkedSupply) || !Array.isArray(o.lines)) return null
  const linkedSupply = o.linkedSupply.map(decodeLinked)
  if (linkedSupply.some((entry) => entry === null)) return null
  const kinds = linkedSupply.map((entry) => entry!.kind)
  if (kinds.length !== Object.values(SOURCE_PLAN_SUPPLY_KIND).length || new Set(kinds).size !== kinds.length) return null
  const lines = o.lines.map(decodeLine)
  if (lines.some((line) => line === null) || lines.length !== detail.lines.length) return null
  const byId = new Map(detail.lines.map((line) => [line.lineId, line]))
  const seen = new Set<string>()
  for (const line of lines as SourcePlanLine[]) {
    const own = byId.get(line.lineId)
    if (own === undefined || seen.has(line.lineId) || own.lineNo !== line.lineNo || own.orderUnitId !== line.orderUnitId
      || own.quantity !== line.quantity) return null
    seen.add(line.lineId)
    // An order-level refusal locks every line; on a writable order only a named reason locks a line.
    if (!o.writable && line.writable) return null
    if (o.writable && !line.writable && line.lockReason === null) return null
  }
  return {
    version: o.version,
    revision: o.revision,
    writable: o.writable,
    linkedSupply: linkedSupply as SourcePlanLinkedSupply[],
    lines: lines as SourcePlanLine[],
  }
}
