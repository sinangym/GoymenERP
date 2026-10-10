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

/**
 * The supplier identity bound to one Purchasing order (#661 `PurchasingSupplierDisplayV1`). Record-bound: it comes with
 * the order the caller is already authorized to read, never from a separate Cari lookup.
 */
export interface PurchasingSupplierDisplay {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

/** GET list/detail summary (`PurchaseOrderReadSummaryViewV1`): the order plus its record-bound supplier identity. */
export interface PurchaseOrderReadSummary extends PurchaseOrderSummary {
  readonly supplierDisplay: PurchasingSupplierDisplay
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

export interface PurchaseOrderAcceptanceHistory {
  readonly acceptanceId: string
  readonly lineId: string
  readonly quantity: string
  readonly recordedAtUtc: string
}

export interface PurchaseOrderDetail {
  readonly order: PurchaseOrderSummary
  readonly lines: readonly PurchaseOrderLine[]
  readonly acceptances: readonly PurchaseOrderAcceptanceHistory[]
}

export interface PurchaseOrderList {
  readonly items: readonly PurchaseOrderSummary[]
  readonly nextCursor: string | null
}

/** What GET detail answers. Create/approve command responses keep the legacy `PurchaseOrderDetail` shape. */
export interface PurchaseOrderReadDetail extends PurchaseOrderDetail {
  readonly order: PurchaseOrderReadSummary
}

/** What GET list answers. */
export interface PurchaseOrderReadList extends PurchaseOrderList {
  readonly items: readonly PurchaseOrderReadSummary[]
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

export type PurchaseRequestStatus = 'Draft' | 'Submitted'

export interface PurchaseRequestSummary {
  readonly id: string
  readonly companyId: string
  readonly status: PurchaseRequestStatus
  readonly version: string
  readonly effectiveOn: string
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly lineCount: number
}

export interface PurchaseRequestLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly quantity: string
}

export interface PurchaseRequestDetail {
  readonly request: PurchaseRequestSummary
  readonly lines: readonly PurchaseRequestLine[]
}

export interface PurchaseRequestList {
  readonly items: readonly PurchaseRequestSummary[]
  readonly nextCursor: string | null
}

export interface PurchaseRequestCommandResponse {
  readonly detail: PurchaseRequestDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

export interface CreatePurchaseRequestLineRequest {
  readonly lineId: string
  readonly productId: string
  readonly expectedProductVersion: string
  readonly unitId: string
  readonly quantity: string
}

export interface CreatePurchaseRequestRequest {
  readonly effectiveOn: string
  readonly lines: readonly CreatePurchaseRequestLineRequest[]
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

function requestStatus(value: unknown): value is PurchaseRequestStatus {
  return value === 'Draft' || value === 'Submitted'
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

const SUMMARY_FIELDS = [
  'id', 'companyId', 'status', 'version', 'supplierPartyId', 'companyAccountId',
  'sourceRequestId', 'effectiveOn', 'createdAtUtc', 'updatedAtUtc', 'lineCount',
] as const

/**
 * The exact #661 supplier identity: a real party id, a non-empty code and legal name, and a trade name that is a
 * string or null. Anything missing, extra or mistyped is refused; nothing is defaulted.
 */
function decodeSupplierDisplay(value: unknown): PurchasingSupplierDisplay | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['partyId', 'code', 'legalName', 'tradeName']) ||
    !guid(source.partyId) || !text(source.code) || !text(source.legalName) ||
    (source.tradeName !== null && typeof source.tradeName !== 'string')
  ) return null
  return source as unknown as PurchasingSupplierDisplay
}

/**
 * The supplier identity an order carries, or null when it carries none that can be trusted: absent (a legacy
 * command-shaped order), malformed, or naming a different party than the order's own `supplierPartyId`.
 */
export function supplierDisplayOf(order: PurchaseOrderSummary): PurchasingSupplierDisplay | null {
  const display = decodeSupplierDisplay((order as { readonly supplierDisplay?: unknown }).supplierDisplay)
  return display !== null && display.partyId.toLowerCase() === order.supplierPartyId.toLowerCase() ? display : null
}

/**
 * GET list/detail summary. Fails closed: the read DTO must carry exactly the legacy fields plus a valid
 * `supplierDisplay` bound to this order's `supplierPartyId`. A legacy-shaped read is not accepted here.
 */
function decodeReadSummary(value: unknown): PurchaseOrderReadSummary | null {
  const source = record(value)
  if (source === null || !exact(source, [...SUMMARY_FIELDS, 'supplierDisplay'])) return null
  const { supplierDisplay, ...legacy } = source
  const summary = decodeSummary(legacy)
  if (summary === null) return null
  const display = decodeSupplierDisplay(supplierDisplay)
  if (display === null || display.partyId.toLowerCase() !== summary.supplierPartyId.toLowerCase()) return null
  return { ...summary, supplierDisplay: display }
}

/** The legacy `PurchaseOrderSummaryView`: what create/approve command responses still carry. */
function decodeSummary(value: unknown): PurchaseOrderSummary | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, SUMMARY_FIELDS) ||
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

function decodeAcceptanceHistory(value: unknown): PurchaseOrderAcceptanceHistory | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['acceptanceId', 'lineId', 'quantity', 'recordedAtUtc']) ||
    !guid(source.acceptanceId) || !guid(source.lineId) ||
    !text(source.quantity) || !DECIMAL.test(source.quantity as string) || !/[1-9]/.test(source.quantity as string) ||
    !instant(source.recordedAtUtc)
  ) return null
  return source as unknown as PurchaseOrderAcceptanceHistory
}

function decodeRequestSummary(value: unknown): PurchaseRequestSummary | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['id', 'companyId', 'status', 'version', 'effectiveOn', 'createdAtUtc', 'updatedAtUtc', 'lineCount']) ||
    !guid(source.id) || !guid(source.companyId) || !requestStatus(source.status) || !canonicalVersion(source.version) ||
    !dateOnly(source.effectiveOn) || !instant(source.createdAtUtc) || !instant(source.updatedAtUtc) ||
    !Number.isSafeInteger(source.lineCount) || (source.lineCount as number) < 0
  ) return null
  return source as unknown as PurchaseRequestSummary
}

function decodeRequestLine(value: unknown): PurchaseRequestLine | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['lineId', 'lineNo', 'productId', 'productVersion', 'unitId', 'quantity']) ||
    !guid(source.lineId) || !Number.isSafeInteger(source.lineNo) || (source.lineNo as number) < 1 ||
    !guid(source.productId) || !canonicalVersion(source.productVersion) || !guid(source.unitId) ||
    !text(source.quantity) || !DECIMAL.test(source.quantity as string) || !/[1-9]/.test(source.quantity as string)
  ) return null
  return source as unknown as PurchaseRequestLine
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

/** GET list (`PurchaseOrderReadListResponseV1`). */
export function decodePurchaseOrderList(value: unknown): PurchaseOrderReadList | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'nextCursor']) || !Array.isArray(source.items)) return null
  if (source.nextCursor !== null && !text(source.nextCursor)) return null
  const items = source.items.map(decodeReadSummary)
  return items.some((item) => item === null)
    ? null
    : { items: items as PurchaseOrderReadSummary[], nextCursor: source.nextCursor as string | null }
}

/** GET detail (`PurchaseOrderReadDetailViewV1`). */
export function decodePurchaseOrderDetail(value: unknown): PurchaseOrderReadDetail | null {
  return decodeDetailWith(value, decodeReadSummary)
}

/** The detail body shared by reads and command responses; only the order summary's shape differs. */
function decodeDetailWith<Summary extends PurchaseOrderSummary>(
  value: unknown,
  summaryOf: (value: unknown) => Summary | null,
): { readonly order: Summary; readonly lines: readonly PurchaseOrderLine[]; readonly acceptances: readonly PurchaseOrderAcceptanceHistory[] } | null {
  const source = record(value)
  if (source === null || !Array.isArray(source.lines)) return null
  const legacy = exact(source, ['order', 'lines'])
  const current = exact(source, ['order', 'lines', 'acceptances']) && Array.isArray(source.acceptances)
  if (!legacy && !current) return null
  const order = summaryOf(source.order)
  const lines = source.lines.map(decodeLine)
  if (order === null || lines.some((line) => line === null) || order.lineCount !== lines.length) return null
  const acceptedLines = lines as PurchaseOrderLine[]
  const acceptances = current
    ? (source.acceptances as unknown[]).map(decodeAcceptanceHistory)
    : []
  if (
    acceptances.some((acceptance) => acceptance === null) ||
    new Set(acceptances.map((acceptance) => acceptance?.acceptanceId)).size !== acceptances.length ||
    acceptances.some((acceptance) => !acceptedLines.some((line) => line.lineId === acceptance?.lineId))
  ) return null
  return { order, lines: acceptedLines, acceptances: acceptances as PurchaseOrderAcceptanceHistory[] }
}

export function decodePurchaseOrderCommandResponse(value: unknown): PurchaseOrderCommandResponse | null {
  const source = record(value)
  if (source === null || !exact(source, ['detail', 'isReplay', 'originalCorrelationId'])) return null
  // Create/approve answer the legacy detail (no `supplierDisplay`); a read-shaped body is not a command response.
  const detail = decodeDetailWith(source.detail, decodeSummary)
  if (detail === null || typeof source.isReplay !== 'boolean' || !text(source.originalCorrelationId)) return null
  return { detail, isReplay: source.isReplay, originalCorrelationId: source.originalCorrelationId }
}

export function decodePurchaseRequestList(value: unknown): PurchaseRequestList | null {
  const source = record(value)
  if (source === null || !exact(source, ['items', 'nextCursor']) || !Array.isArray(source.items)) return null
  if (source.nextCursor !== null && !text(source.nextCursor)) return null
  const items = source.items.map(decodeRequestSummary)
  return items.some((item) => item === null)
    ? null
    : { items: items as PurchaseRequestSummary[], nextCursor: source.nextCursor as string | null }
}

export function decodePurchaseRequestDetail(value: unknown): PurchaseRequestDetail | null {
  const source = record(value)
  if (source === null || !exact(source, ['request', 'lines']) || !Array.isArray(source.lines)) return null
  const request = decodeRequestSummary(source.request)
  const lines = source.lines.map(decodeRequestLine)
  if (request === null || lines.some((line) => line === null) || request.lineCount !== lines.length) return null
  return { request, lines: lines as PurchaseRequestLine[] }
}

export function decodePurchaseRequestCommandResponse(value: unknown): PurchaseRequestCommandResponse | null {
  const source = record(value)
  if (source === null || !exact(source, ['detail', 'isReplay', 'originalCorrelationId'])) return null
  const detail = decodePurchaseRequestDetail(source.detail)
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
