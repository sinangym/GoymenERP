/** A1-3 (plan API-10; No.161 §2): `Cancelled` is an approved order whose whole unaccepted remainder was cancelled. */
export type PurchaseOrderStatus = 'Draft' | 'Approved' | 'Cancelled'
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
  /** A1-3: the unaccepted remainder cancelled with the order ("0" until then); absent in a body from before A1-3. */
  readonly cancelled?: string
  /**
   * A2a-1 (API-20): the line's price as the backend calculator answered it; null when the line has none ("fiyat bekliyor") or
   * when the prices are withheld (PRS-02); absent in a body from before A2a-1.
   */
  readonly price?: PurchaseOrderLinePrice | null
}

/** A2a-1 (ALS-D5): a priced line — exact texts; the net and the line amount are the server's, never recomputed here. */
export interface PurchaseOrderLinePrice {
  readonly unitPrice: string
  readonly priceUnitId: string
  readonly discountPercent: string | null
  readonly discountAmount: string | null
  readonly netUnitPrice: string
  readonly lineAmount: string
}

/** A2a-1 (TEK-01): the order's currency as recorded at opening. */
export interface PurchaseOrderCurrency {
  readonly currencyId: string
  readonly currencyCode: string
  readonly scale: number
}

/** A2a-1 (PRS-02): `Withheld` — the caller lacks the price read; every price field is then null. */
export type PurchaseOrderPriceDisclosure = 'Visible' | 'Withheld'

/** A1-3 (No.161 §2): the recorded cancellation of an order's unaccepted remainder. */
export interface PurchaseOrderCancellation {
  readonly cancelledBy: string
  readonly cancelledAtUtc: string
  readonly reason: string
}

/** A1-3 (plan API-10): GET …/orders/{id}/cancel-impact — what a cancellation at `version` would do; reads only. */
export interface PurchaseOrderCancelImpact {
  readonly orderId: string
  readonly status: PurchaseOrderStatus
  readonly version: string
  readonly sourceRequestId: string | null
  readonly cancellable: boolean
  /** The refusal the command would answer: `purchasing_order_not_cancellable` | `purchasing_order_nothing_to_cancel`. */
  readonly blocker: string | null
  readonly hasAcceptance: boolean
  readonly lines: readonly PurchaseOrderCancelImpactLine[]
}

export interface PurchaseOrderCancelImpactLine {
  readonly lineId: string
  readonly lineNo: number
  readonly kind: PurchaseOrderLineKind
  readonly productId: string
  readonly unitId: string
  readonly committed: string
  readonly accepted: string
  readonly toCancel: string
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
  /** A1-3: the recorded cancellation; null unless Cancelled, absent in a body from before A1-3. */
  readonly cancellation?: PurchaseOrderCancellation | null
  /** A2a-1: absent in a body from before A2a-1. */
  readonly priceDisclosure?: PurchaseOrderPriceDisclosure
  /** A2a-1: the order's currency, null without one. */
  readonly currency?: PurchaseOrderCurrency | null
  /** A2a-1 (ALS-D18): a general line has no price yet; null when the prices are withheld. */
  readonly pricePending?: boolean | null
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

/** A2a-1 (ALS-D5): a line's manual price; absent fields are not sent (the price unit defaults to the line's unit on the server). */
export interface CreatePurchaseOrderLinePriceRequest {
  readonly unitPrice: string
  readonly discountPercent?: string
  readonly discountAmount?: string
}

export interface CreatePurchaseOrderLineRequest {
  readonly lineId: string
  readonly productId: string
  readonly expectedProductVersion: string
  readonly unitId: string
  readonly quantity: string
  readonly sourceRequestLineId: string | null
  /** A2a-1: absent = no price ("fiyat bekliyor", ALS-D18). */
  readonly price?: CreatePurchaseOrderLinePriceRequest
}

export interface CreatePurchaseOrderRequest {
  readonly supplierPartyId: string
  readonly companyAccountId: string
  readonly sourceRequestId: string | null
  readonly effectiveOn: string
  readonly lines: readonly CreatePurchaseOrderLineRequest[]
  /** A2a-1 (TEK-01): required by the server once a line is priced; absent otherwise. */
  readonly currencyId?: string
}

/** A2a-1: one currency the purchasing order operation's Settings money profile answers. */
export interface PurchasingCurrencyCandidate {
  readonly currencyId: string
  readonly code: string
  readonly displayLabel: string
}

export interface PurchasingCurrencyCandidates {
  readonly items: readonly PurchasingCurrencyCandidate[]
}

/**
 * A1-1: Approved and Rejected are the purchase manager's decisions (ALS-D2, No.161 §1). A1-1b: Withdrawn is the
 * preparer's or submitter's withdrawal (ALS-D46, ALS-D47), a separate outcome from Rejected.
 */
export type PurchaseRequestStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected' | 'Withdrawn'

/** A1-2 (plan API-02): a line's source module; a manual line carries no source. */
export type PurchaseRequestSourceKind = 'Stock' | 'Sales' | 'Production'

/** A1-2: the summary's source vocabulary — the line kinds plus `Manual` for lines without a source, in this fixed order. */
export type PurchaseRequestSummarySourceKind = 'Manual' | PurchaseRequestSourceKind

export const PURCHASE_REQUEST_SUMMARY_SOURCE_KINDS: readonly PurchaseRequestSummarySourceKind[] = ['Manual', 'Stock', 'Sales', 'Production']

export interface PurchaseRequestSummary {
  readonly id: string
  readonly companyId: string
  readonly status: PurchaseRequestStatus
  readonly version: string
  readonly effectiveOn: string
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly lineCount: number
  /** A1-2: the distinct sources of the request's lines (fixed order). A body before A1-2 has no field. */
  readonly sourceKinds?: readonly PurchaseRequestSummarySourceKind[]
}

/** A1-2: the source module and the source record's opaque identity (its document and the line in it). */
export interface PurchaseRequestLineSource {
  readonly kind: PurchaseRequestSourceKind
  readonly documentId: string
  readonly documentLineId: string
}

export interface PurchaseRequestLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productVersion: string
  readonly unitId: string
  readonly quantity: string
  /** A1-2: null for a manual line; a body before A1-2 has no field — the source is unknown (shown as "—"), never read as manual. */
  readonly source?: PurchaseRequestLineSource | null
}

/** A1-1: one decision on a request — who, when and (a rejection or an A1-1b withdrawal) why. */
export interface PurchaseRequestDecision {
  readonly decidedBy: string
  readonly decidedAtUtc: string
  readonly reason: string | null
}

export interface PurchaseRequestDetail {
  readonly request: PurchaseRequestSummary
  readonly lines: readonly PurchaseRequestLine[]
  /** Null until approved; stays after a later rejection. A body without the field (before A1-1) reads as null. */
  readonly approval?: PurchaseRequestDecision | null
  /** Null until rejected; carries the reason. */
  readonly rejection?: PurchaseRequestDecision | null
  /** A1-1b: null until the preparer or the submitter withdraws the request (ALS-D47); carries the reason. A body before A1-1b reads as null. */
  readonly withdrawal?: PurchaseRequestDecision | null
  /** A1-1b (ALS-D47): the recorded preparer (creator) — one of the two actors who may withdraw — or null if not recorded. */
  readonly preparedBy?: string | null
  /** A1-1b: the recorded submitter — the other actor who may withdraw — or null (Draft, or not recorded). */
  readonly submittedBy?: string | null
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
  /** A1-2: omitted for a manual line (the P-01 form never sends one); a module that opens a request from its need sends it. */
  readonly source?: PurchaseRequestLineSource
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
  return value === 'Draft' || value === 'Approved' || value === 'Cancelled'
}

function requestStatus(value: unknown): value is PurchaseRequestStatus {
  return value === 'Draft' || value === 'Submitted' || value === 'Approved' || value === 'Rejected' || value === 'Withdrawn'
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

/** A string with something other than whitespace in it — the producer's own `IsNullOrWhiteSpace` rule. */
function nonBlankText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

/**
 * The exact #661 supplier identity: a real party id, a code and a legal name that are not blank, and a trade name
 * that is either null or not blank. Anything missing, extra, mistyped or whitespace-only is refused. The values are
 * checked, never trimmed or defaulted: what is shown is exactly what the server sent.
 */
function decodeSupplierDisplay(value: unknown): PurchasingSupplierDisplay | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['partyId', 'code', 'legalName', 'tradeName']) ||
    !guid(source.partyId) || !nonBlankText(source.code) || !nonBlankText(source.legalName) ||
    (source.tradeName !== null && !nonBlankText(source.tradeName))
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

const LINE_FIELDS = [
  'lineId', 'lineNo', 'kind', 'parentLineId', 'sourceRequestLineId', 'productId',
  'productVersion', 'unitId', 'ordered', 'technical', 'committed', 'accepted', 'remaining',
] as const

/** A2a-1: exactly a unit price of zero or more (an explicit 0 is a price), a price unit, at most one discount, and the server's net and line amount. */
function decodeLinePrice(value: unknown): PurchaseOrderLinePrice | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['unitPrice', 'priceUnitId', 'discountPercent', 'discountAmount', 'netUnitPrice', 'lineAmount']) ||
    !text(source.unitPrice) || !DECIMAL.test(source.unitPrice as string) ||
    !guid(source.priceUnitId) ||
    (source.discountPercent !== null && (!text(source.discountPercent) || !DECIMAL.test(source.discountPercent as string))) ||
    (source.discountAmount !== null && (!text(source.discountAmount) || !DECIMAL.test(source.discountAmount as string))) ||
    (source.discountPercent !== null && source.discountAmount !== null) ||
    !text(source.netUnitPrice) || !DECIMAL.test(source.netUnitPrice as string) ||
    !text(source.lineAmount) || !DECIMAL.test(source.lineAmount as string)
  ) return null
  return source as unknown as PurchaseOrderLinePrice
}

/** A2a-1: exactly a currency identity, a non-blank code and a scale within 0..28. */
function decodeCurrency(value: unknown): PurchaseOrderCurrency | null | undefined {
  if (value === null) return null
  const source = record(value)
  return source !== null && exact(source, ['currencyId', 'currencyCode', 'scale']) && guid(source.currencyId) &&
    nonBlankText(source.currencyCode) && Number.isSafeInteger(source.scale) && (source.scale as number) >= 0 && (source.scale as number) <= 28
    ? source as unknown as PurchaseOrderCurrency
    : undefined
}

/**
 * A line before A1-3 (no `cancelled`), after it (`cancelled` a canonical decimal) or after A2a-1 (`cancelled` and `price`, null or
 * a valid price); nothing else.
 */
function decodeLine(value: unknown): PurchaseOrderLine | null {
  const source = record(value)
  if (source === null) return null
  const priced = exact(source, [...LINE_FIELDS, 'cancelled', 'price'])
  const current = priced || exact(source, [...LINE_FIELDS, 'cancelled'])
  if (current && (!text(source.cancelled) || !DECIMAL.test(source.cancelled as string))) return null
  if (priced && source.price !== null && decodeLinePrice(source.price) === null) return null
  if (
    !(current || exact(source, LINE_FIELDS)) ||
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

/** A1-3: null, or exactly a non-blank canceller, an instant and a non-blank reason. */
function decodeCancellation(value: unknown): PurchaseOrderCancellation | null | undefined {
  if (value === null) return null
  const source = record(value)
  return source !== null && exact(source, ['cancelledBy', 'cancelledAtUtc', 'reason']) &&
    nonBlankText(source.cancelledBy) && instant(source.cancelledAtUtc) && nonBlankText(source.reason)
    ? source as unknown as PurchaseOrderCancellation
    : undefined
}

const REQUEST_SUMMARY_FIELDS = ['id', 'companyId', 'status', 'version', 'effectiveOn', 'createdAtUtc', 'updatedAtUtc', 'lineCount'] as const

/** A1-2: distinct known kinds in the fixed order Manual, Stock, Sales, Production; anything else is not trusted. */
function summarySourceKinds(value: unknown): value is readonly PurchaseRequestSummarySourceKind[] {
  if (!Array.isArray(value)) return false
  const positions = value.map((kind) => PURCHASE_REQUEST_SUMMARY_SOURCE_KINDS.indexOf(kind as PurchaseRequestSummarySourceKind))
  return positions.every((position, index) => position >= 0 && (index === 0 || position > positions[index - 1]))
}

function decodeRequestSummary(value: unknown): PurchaseRequestSummary | null {
  const source = record(value)
  if (source === null) return null
  // A1-2: the current summary adds the lines' source kinds; a body before A1-2 lacks the field.
  const sourced = exact(source, [...REQUEST_SUMMARY_FIELDS, 'sourceKinds'])
  if (
    (!exact(source, REQUEST_SUMMARY_FIELDS) && !sourced) ||
    !guid(source.id) || !guid(source.companyId) || !requestStatus(source.status) || !canonicalVersion(source.version) ||
    !dateOnly(source.effectiveOn) || !instant(source.createdAtUtc) || !instant(source.updatedAtUtc) ||
    !Number.isSafeInteger(source.lineCount) || (source.lineCount as number) < 0 ||
    (sourced && (!summarySourceKinds(source.sourceKinds) || ((source.sourceKinds as unknown[]).length === 0) !== (source.lineCount === 0)))
  ) return null
  return source as unknown as PurchaseRequestSummary
}

function decodeRequestLineSource(value: unknown): PurchaseRequestLineSource | null {
  const source = record(value)
  if (
    source === null || !exact(source, ['kind', 'documentId', 'documentLineId']) ||
    (source.kind !== 'Stock' && source.kind !== 'Sales' && source.kind !== 'Production') ||
    !guid(source.documentId) || !guid(source.documentLineId)
  ) return null
  return source as unknown as PurchaseRequestLineSource
}

function decodeRequestDecision(value: unknown, withReason: boolean): PurchaseRequestDecision | null {
  const source = record(value)
  if (
    source === null || !exact(source, ['decidedBy', 'decidedAtUtc', 'reason']) ||
    !text(source.decidedBy) || !(source.decidedBy as string).trim() || !instant(source.decidedAtUtc) ||
    (withReason ? !text(source.reason) || !(source.reason as string).trim() : source.reason !== null)
  ) return null
  return source as unknown as PurchaseRequestDecision
}

function decodeRequestLine(value: unknown): PurchaseRequestLine | null {
  const source = record(value)
  const fields = ['lineId', 'lineNo', 'productId', 'productVersion', 'unitId', 'quantity'] as const
  // A1-2: the current line adds its source (null = manual); a body before A1-2 lacks the field.
  const sourced = source !== null && exact(source, [...fields, 'source'])
  if (
    source === null ||
    (!exact(source, fields) && !sourced) ||
    (sourced && source.source !== null && decodeRequestLineSource(source.source) === null) ||
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
): {
  readonly order: Summary
  readonly lines: readonly PurchaseOrderLine[]
  readonly acceptances: readonly PurchaseOrderAcceptanceHistory[]
  readonly cancellation?: PurchaseOrderCancellation | null
  readonly priceDisclosure?: PurchaseOrderPriceDisclosure
  readonly currency?: PurchaseOrderCurrency | null
  readonly pricePending?: boolean | null
} | null {
  const source = record(value)
  if (source === null || !Array.isArray(source.lines)) return null
  const legacy = exact(source, ['order', 'lines'])
  const withHistory = exact(source, ['order', 'lines', 'acceptances']) && Array.isArray(source.acceptances)
  // A2a-1: the priced body adds the disclosure, the currency and "fiyat bekliyor"; every line then carries its price.
  const priced = exact(source, ['order', 'lines', 'acceptances', 'cancellation', 'priceDisclosure', 'currency', 'pricePending'])
    && Array.isArray(source.acceptances)
  // A1-3: the current body always carries the cancellation (null unless Cancelled) and every line its cancelled share.
  const current = priced || (exact(source, ['order', 'lines', 'acceptances', 'cancellation']) && Array.isArray(source.acceptances))
  if (!legacy && !withHistory && !current) return null
  const order = summaryOf(source.order)
  const lines = source.lines.map(decodeLine)
  if (order === null || lines.some((line) => line === null) || order.lineCount !== lines.length) return null
  const acceptedLines = lines as PurchaseOrderLine[]
  const cancellation = current ? decodeCancellation(source.cancellation) : null
  if (cancellation === undefined) return null
  // Fail closed: the status and the cancellation agree, every line of a current body carries its share, and a body
  // from before A1-3 can never be Cancelled.
  if (current
    ? (order.status === 'Cancelled') !== (cancellation !== null) || acceptedLines.some((line) => line.cancelled === undefined)
    : order.status === 'Cancelled' || acceptedLines.some((line) => line.cancelled !== undefined)) return null
  const acceptances = withHistory || current
    ? (source.acceptances as unknown[]).map(decodeAcceptanceHistory)
    : []
  if (
    acceptances.some((acceptance) => acceptance === null) ||
    new Set(acceptances.map((acceptance) => acceptance?.acceptanceId)).size !== acceptances.length ||
    acceptances.some((acceptance) => !acceptedLines.some((line) => line.lineId === acceptance?.lineId))
  ) return null
  if (priced) {
    const pricing = decodePricing(source, acceptedLines)
    if (pricing === null) return null
    return { order, lines: acceptedLines, acceptances: acceptances as PurchaseOrderAcceptanceHistory[], cancellation, ...pricing }
  }
  if (acceptedLines.some((line) => line.price !== undefined)) return null
  return current
    ? { order, lines: acceptedLines, acceptances: acceptances as PurchaseOrderAcceptanceHistory[], cancellation }
    : { order, lines: acceptedLines, acceptances: acceptances as PurchaseOrderAcceptanceHistory[] }
}

/**
 * A2a-1 (PRS-02, ALS-D18): fails closed unless the disclosure and the prices agree — `Withheld` carries no price and no "fiyat
 * bekliyor"; `Visible` carries a boolean that is exactly "some general line has no price"; a priced line needs the order's
 * currency; a technical line is never priced; every line carries its `price` member.
 */
function decodePricing(source: JsonObject, lines: readonly PurchaseOrderLine[]): {
  readonly priceDisclosure: PurchaseOrderPriceDisclosure
  readonly currency: PurchaseOrderCurrency | null
  readonly pricePending: boolean | null
} | null {
  const currency = decodeCurrency(source.currency)
  if (currency === undefined || lines.some((line) => line.price === undefined)) return null
  if (lines.some((line) => line.kind === 'Technical' && line.price !== null)) return null
  if (source.priceDisclosure === 'Withheld') {
    return source.pricePending === null && lines.every((line) => line.price === null)
      ? { priceDisclosure: 'Withheld', currency, pricePending: null }
      : null
  }
  if (source.priceDisclosure !== 'Visible' || typeof source.pricePending !== 'boolean') return null
  const pending = lines.some((line) => line.kind === 'General' && line.price === null)
  if (source.pricePending !== pending || (currency === null && lines.some((line) => line.price !== null))) return null
  return { priceDisclosure: 'Visible', currency, pricePending: source.pricePending }
}

/** A2a-1: GET …/references/currencies; exact fields, unique currency identities. */
export function decodePurchasingCurrencyCandidates(value: unknown): PurchasingCurrencyCandidates | null {
  const source = record(value)
  if (source === null || !exact(source, ['items']) || !Array.isArray(source.items)) return null
  const items: PurchasingCurrencyCandidate[] = []
  for (const candidate of source.items as unknown[]) {
    const item = record(candidate)
    if (
      item === null || !exact(item, ['currencyId', 'code', 'displayLabel']) || !guid(item.currencyId) ||
      !nonBlankText(item.code) || !nonBlankText(item.displayLabel) || items.some((known) => known.currencyId === item.currencyId)
    ) return null
    items.push(item as unknown as PurchasingCurrencyCandidate)
  }
  return { items }
}

const ORDER_CANCEL_BLOCKERS = ['purchasing_order_not_cancellable', 'purchasing_order_nothing_to_cancel'] as const

/**
 * A1-3 (plan API-10): GET …/cancel-impact. Fails closed: exact fields, a known blocker exactly when not cancellable, and
 * unique lines in order; the preview is information only — the command re-measures everything.
 */
export function decodePurchaseOrderCancelImpact(value: unknown): PurchaseOrderCancelImpact | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['orderId', 'status', 'version', 'sourceRequestId', 'cancellable', 'blocker', 'hasAcceptance', 'lines']) ||
    !guid(source.orderId) || !status(source.status) || !canonicalVersion(source.version) || !nullableGuid(source.sourceRequestId) ||
    typeof source.cancellable !== 'boolean' || typeof source.hasAcceptance !== 'boolean' || !Array.isArray(source.lines) ||
    (source.cancellable
      ? source.blocker !== null
      : !(ORDER_CANCEL_BLOCKERS as readonly unknown[]).includes(source.blocker))
  ) return null
  const lines: PurchaseOrderCancelImpactLine[] = []
  for (const candidate of source.lines as unknown[]) {
    const line = record(candidate)
    if (
      line === null ||
      !exact(line, ['lineId', 'lineNo', 'kind', 'productId', 'unitId', 'committed', 'accepted', 'toCancel']) ||
      !guid(line.lineId) || line.lineNo !== lines.length + 1 || !lineKind(line.kind) || !guid(line.productId) || !guid(line.unitId) ||
      ![line.committed, line.accepted, line.toCancel].every((quantity) => text(quantity) && DECIMAL.test(quantity)) ||
      lines.some((known) => known.lineId === line.lineId)
    ) return null
    lines.push(line as unknown as PurchaseOrderCancelImpactLine)
  }
  return source as unknown as PurchaseOrderCancelImpact
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
  if (source === null || !Array.isArray(source.lines)) return null
  const legacy = exact(source, ['request', 'lines'])
  const decided = exact(source, ['request', 'lines', 'approval', 'rejection'])
  // A1-1b: the current body adds the withdrawal and the recorded preparer and submitter.
  const withdrawable = exact(source, ['request', 'lines', 'approval', 'rejection', 'withdrawal', 'preparedBy', 'submittedBy'])
  const current = decided || withdrawable
  if (!legacy && !current) return null
  const request = decodeRequestSummary(source.request)
  const lines = source.lines.map(decodeRequestLine)
  if (request === null || lines.some((line) => line === null) || request.lineCount !== lines.length) return null
  // A1-2: a body with sources has them on the summary and on every line, and the summary's kinds are exactly the lines'.
  const sourcedLines = (lines as PurchaseRequestLine[]).filter((line) => line.source !== undefined).length
  if (request.sourceKinds === undefined ? sourcedLines !== 0 : sourcedLines !== lines.length) return null
  if (request.sourceKinds !== undefined) {
    const present = new Set((lines as PurchaseRequestLine[]).map((line) => line.source?.kind ?? 'Manual'))
    const expected = PURCHASE_REQUEST_SUMMARY_SOURCE_KINDS.filter((kind) => present.has(kind))
    if (expected.length !== request.sourceKinds.length || expected.some((kind, index) => kind !== request.sourceKinds?.[index])) return null
  }
  const approval = current && source.approval !== null ? decodeRequestDecision(source.approval, false) : null
  const rejection = current && source.rejection !== null ? decodeRequestDecision(source.rejection, true) : null
  if ((current && source.approval !== null && approval === null) || (current && source.rejection !== null && rejection === null)) return null
  const withdrawal = withdrawable && source.withdrawal !== null ? decodeRequestDecision(source.withdrawal, true) : null
  if (withdrawable && source.withdrawal !== null && withdrawal === null) return null
  if (withdrawable && source.submittedBy !== null && (!text(source.submittedBy) || !(source.submittedBy as string).trim())) return null
  if (withdrawable && source.preparedBy !== null && (!text(source.preparedBy) || !(source.preparedBy as string).trim())) return null
  const submittedBy = withdrawable ? source.submittedBy as string | null : null
  const preparedBy = withdrawable ? source.preparedBy as string | null : null
  // The decision facts match the status (the backend's CK_PurchaseRequests_Decision / _Withdrawal); anything else is not trusted.
  const consistent = request.status === 'Approved' ? approval !== null && rejection === null && withdrawal === null
    : request.status === 'Rejected' ? rejection !== null && withdrawal === null
      : request.status === 'Withdrawn' ? withdrawal !== null && approval === null && rejection === null
        : approval === null && rejection === null && withdrawal === null
  if (current && !consistent) return null
  // A1-1b (ALS-D47): only a recorded preparer or submitter withdraws; a Draft has no submitter; an approved or rejected
  // request always has one; a withdrawn one may lack it (its preparer withdrew a request submitted before A1-1).
  if (withdrawable && (request.status === 'Draft' ? submittedBy !== null
    : request.status === 'Submitted' ? false
      : request.status === 'Withdrawn'
        ? withdrawal === null || (withdrawal.decidedBy !== preparedBy && withdrawal.decidedBy !== submittedBy)
        : submittedBy === null)) return null
  if (decided && request.status === 'Withdrawn') return null
  if (legacy && (request.status === 'Approved' || request.status === 'Rejected' || request.status === 'Withdrawn')) return null
  return { request, lines: lines as PurchaseRequestLine[], approval, rejection, withdrawal, preparedBy, submittedBy }
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
