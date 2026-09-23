export type PurchaseOrderStatus = 'Draft' | 'Approved'
export type PurchaseOrderLineKind = 'General' | 'Technical'

export interface PurchaseOrderSummary {
  readonly id: string
  readonly companyId: string
  readonly status: PurchaseOrderStatus
  readonly version: string
  readonly supplierPartyId: string
  readonly companyAccountId: string
  readonly sourceRequestId: string | null
  readonly effectiveOn: string
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly lineCount: number
}

export interface PurchaseOrderLine {
  readonly lineId: string
  readonly lineNo: number
  readonly kind: PurchaseOrderLineKind
  readonly parentLineId: string | null
  readonly sourceRequestLineId: string | null
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly ordered: string
  readonly technical: string
  readonly committed: string
  readonly accepted: string
  readonly remaining: string
}

export interface PurchaseOrderDetail {
  readonly order: PurchaseOrderSummary
  readonly lines: readonly PurchaseOrderLine[]
}

export interface PurchaseOrderList {
  readonly items: readonly PurchaseOrderSummary[]
  readonly nextCursor: string | null
}

export interface PurchaseOrderCommandResponse {
  readonly detail: PurchaseOrderDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

/** Product-owned, save-time-bound purchase candidate. No Sales unit or conversion is implied. */
export interface ProductPurchaseCandidate {
  readonly productId: string
  readonly productVersion: string
  readonly code: string
  readonly name: string
  readonly unitId: string
  readonly unitCode: string
  readonly profileId: string
  readonly profileVersion: string
}

export interface ProductPurchaseCandidates {
  readonly items: readonly ProductPurchaseCandidate[]
  /** The bounded source window filled; this does not claim that more eligible rows exist. */
  readonly searchWindowFull: boolean
}

/** Cari-owned active Supplier account offered for a Purchasing order. */
export interface PurchasingSupplierOption {
  readonly supplierPartyId: string
  readonly companyAccountId: string
  readonly label: string
}

export interface PurchasingSupplierOffer {
  readonly items: readonly PurchasingSupplierOption[]
  readonly nextCursor: string | null
}

export interface CreatePurchaseOrderLineRequest {
  readonly lineId: string
  readonly productId: string
  readonly expectedProductVersion: string
  readonly unitId: string
  readonly quantity: string
  readonly sourceRequestLineId: string | null
}

export interface CreatePurchaseOrderRequest {
  readonly supplierPartyId: string
  readonly companyAccountId: string
  readonly sourceRequestId: string | null
  readonly effectiveOn: string
  readonly lines: readonly CreatePurchaseOrderLineRequest[]
}

type JsonObject = Record<string, unknown>
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const DATE = /^\d{4}-\d{2}-\d{2}$/
const INSTANT = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|([+-])(\d{2}):(\d{2}))$/
const VERSION = /^[1-9]\d*$/
const DECIMAL = /^(0|[1-9]\d*)(\.\d+)?$/
const MAX_INT64 = 9223372036854775807n

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as JsonObject)
    : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

function guid(value: unknown): value is string {
  return text(value) && GUID.test(value) && value.toLowerCase() !== NIL_GUID
}

function nullableGuid(value: unknown): value is string | null {
  return value === null || guid(value)
}

function status(value: unknown): value is PurchaseOrderStatus {
  return value === 'Draft' || value === 'Approved'
}

function lineKind(value: unknown): value is PurchaseOrderLineKind {
  return value === 'General' || value === 'Technical'
}

function canonicalVersion(value: unknown): value is string {
  if (typeof value !== 'string' || value.length > 19 || !VERSION.test(value)) return false
  return BigInt(value) <= MAX_INT64
}

function dateOnly(value: unknown): value is string {
  if (typeof value !== 'string' || !DATE.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  if (year < 1 || month < 1 || month > 12 || day < 1) return false
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  return day <= days[month - 1]
}

function instant(value: unknown): value is string {
  if (typeof value !== 'string' || value.length > 64) return false
  const match = INSTANT.exec(value)
  if (match === null || !dateOnly(match[1])) return false
  const hour = Number(match[2])
  const minute = Number(match[3])
  const second = Number(match[4])
  const offsetHour = match[5] === 'Z' ? 0 : Number(match[7])
  const offsetMinute = match[5] === 'Z' ? 0 : Number(match[8])
  return hour <= 23 && minute <= 59 && second <= 59 && offsetHour <= 14 && offsetMinute <= 59
    && (offsetHour < 14 || offsetMinute === 0) && Number.isFinite(Date.parse(value))
}

function decodeSummary(value: unknown): PurchaseOrderSummary | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, [
      'id', 'companyId', 'status', 'version', 'supplierPartyId', 'companyAccountId',
      'sourceRequestId', 'effectiveOn', 'createdAtUtc', 'updatedAtUtc', 'lineCount',
    ]) ||
    !guid(source.id) || !guid(source.companyId) || !status(source.status) || !canonicalVersion(source.version) ||
    !guid(source.supplierPartyId) || !guid(source.companyAccountId) || !nullableGuid(source.sourceRequestId) ||
    !dateOnly(source.effectiveOn) || !instant(source.createdAtUtc) || !instant(source.updatedAtUtc) ||
    !Number.isSafeInteger(source.lineCount) || (source.lineCount as number) < 0
  ) return null
  return source as unknown as PurchaseOrderSummary
}

function decodeLine(value: unknown): PurchaseOrderLine | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, [
      'lineId', 'lineNo', 'kind', 'parentLineId', 'sourceRequestLineId', 'productId',
      'productVersion', 'unitId', 'ordered', 'technical', 'committed', 'accepted', 'remaining',
    ]) ||
    !guid(source.lineId) || !Number.isSafeInteger(source.lineNo) || (source.lineNo as number) < 1 ||
    !lineKind(source.kind) || !nullableGuid(source.parentLineId) || !nullableGuid(source.sourceRequestLineId) ||
    !guid(source.productId) || !canonicalVersion(source.productVersion) || !guid(source.unitId)
  ) return null
  for (const name of ['ordered', 'technical', 'committed', 'accepted', 'remaining'] as const) {
    if (!text(source[name]) || !DECIMAL.test(source[name] as string)) return null
  }
  return source as unknown as PurchaseOrderLine
}

function decodeProductPurchaseCandidate(value: unknown): ProductPurchaseCandidate | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, [
      'productId', 'productVersion', 'code', 'name', 'unitId', 'unitCode', 'profileId', 'profileVersion',
    ]) ||
    !guid(source.productId) || !canonicalVersion(source.productVersion) ||
    !text(source.code) || !(source.code as string).trim() ||
    !text(source.name) || !(source.name as string).trim() ||
    !guid(source.unitId) || !text(source.unitCode) || !(source.unitCode as string).trim() ||
    !guid(source.profileId) || !canonicalVersion(source.profileVersion)
  ) return null
  return source as unknown as ProductPurchaseCandidate
}

export function decodePurchaseOrderList(value: unknown): PurchaseOrderList | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'nextCursor']) || !Array.isArray(source.items)) return null
  if (source.nextCursor !== null && !text(source.nextCursor)) return null
  const items = source.items.map(decodeSummary)
  return items.some((item) => item === null)
    ? null
    : { items: items as PurchaseOrderSummary[], nextCursor: source.nextCursor as string | null }
}

export function decodePurchaseOrderDetail(value: unknown): PurchaseOrderDetail | null {
  const source = record(value)
  if (source === null || !exact(source, ['order', 'lines']) || !Array.isArray(source.lines)) return null
  const order = decodeSummary(source.order)
  const lines = source.lines.map(decodeLine)
  if (order === null || lines.some((line) => line === null) || order.lineCount !== lines.length) return null
  return { order, lines: lines as PurchaseOrderLine[] }
}

export function decodePurchaseOrderCommandResponse(value: unknown): PurchaseOrderCommandResponse | null {
  const source = record(value)
  if (source === null || !exact(source, ['detail', 'isReplay', 'originalCorrelationId'])) return null
  const detail = decodePurchaseOrderDetail(source.detail)
  if (detail === null || typeof source.isReplay !== 'boolean' || !text(source.originalCorrelationId)) return null
  return { detail, isReplay: source.isReplay, originalCorrelationId: source.originalCorrelationId }
}

export function decodeProductPurchaseCandidates(value: unknown): ProductPurchaseCandidates | null {
  const source = record(value)
  if (
    source === null || !exact(source, ['items', 'searchWindowFull']) ||
    !Array.isArray(source.items) || typeof source.searchWindowFull !== 'boolean'
  ) return null
  const items = source.items.map(decodeProductPurchaseCandidate)
  if (items.some((item) => item === null)) return null
  const candidates = items as ProductPurchaseCandidate[]
  if (new Set(candidates.map((item) => item.productId)).size !== candidates.length) return null
  return { items: candidates, searchWindowFull: source.searchWindowFull }
}
