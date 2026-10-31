/**
 * S-01 "Satış Talebi / Teklif Havuzu" — the sales request and sales quote wire contract (plan §3.2, UI-20).
 *
 * Pinned to the backend slices API-20/21 (#824, `SalesRequestContractsV1.cs`) and API-22/23/24 (#828,
 * `SalesQuoteContractsV1.cs`) and to their schemas in `contracts/openapi/goymen-erp-v1.json`. The same strict rules as the
 * order draft contract (./contract.ts) apply:
 *  - every response member must be PRESENT (nullable ones as explicit null); an unknown or missing member, wrong case, or
 *    a number standing in for decimal/version text refuses the whole body;
 *  - decimal and version text stays text, never a JS number;
 *  - the state and its facts must agree (a `Rejected` request carries its reason, an `Open` quote carries no loss and no
 *    conversion, a `Converted` one names the same order twice) — a body that says otherwise is not shown as that state.
 *
 * A decoder returns null for anything it cannot vouch for; the caller fails closed.
 *
 * Open policies are NOT encoded here (no approver count, no validity day count, no loss-reason values, no price permission
 * choice): the server decides — the SAT-D25 loss reasons and the SAT-D39 default validity are Settings records the server
 * reads — and these decoders only read what it answers.
 */

import { LIMITS, type PriceDisclosure } from './contract'
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

/* ── Paths ────────────────────────────────────────────────────────────────── */

export const SALES_REQUEST_PATH = '/sales/requests'
export const salesRequestDetailPath = (id: string) => `${SALES_REQUEST_PATH}/${encodeURIComponent(id)}`
export type SalesRequestDecisionCommand = 'approval' | 'rejection'
export const salesRequestDecisionPath = (id: string, command: SalesRequestDecisionCommand) =>
  `${salesRequestDetailPath(id)}/${command}`

export const SALES_QUOTE_PATH = '/sales/quotes'
export const salesQuoteDetailPath = (id: string) => `${SALES_QUOTE_PATH}/${encodeURIComponent(id)}`
export type SalesQuoteCommand = 'revision' | 'loss' | 'conversion' | 'expiry-approval'
export const salesQuoteCommandPath = (id: string, command: SalesQuoteCommand) => `${salesQuoteDetailPath(id)}/${command}`
/** SAT-D25: the company's closed loss reason list in force on the server's business day. */
export const SALES_QUOTE_LOSS_REASONS_PATH = `${SALES_QUOTE_PATH}/loss-reasons`
/** SAT-D39: the default validity suggested for a new quote on its business date. */
export const salesQuoteValiditySuggestionPath = (businessDate: string) =>
  `${SALES_QUOTE_PATH}/validity-suggestion?${new URLSearchParams({ businessDate }).toString()}`

/** `SalesRequestApiV1` / `SalesQuoteApiV1`: a create (and the conversion) answers 201; every other command 200. */
export const POOL_CREATED_STATUS = 201
export const POOL_COMMAND_STATUS = 200

/** `SalesQuote.MaximumRequests`: the technical bound of `requestIds` (not a policy). */
export const QUOTE_MAXIMUM_REQUESTS = 100

/* ── States ───────────────────────────────────────────────────────────────── */

export const REQUEST_STATE = {
  submitted: 'Submitted',
  approved: 'Approved',
  rejected: 'Rejected',
} as const
export type RequestState = (typeof REQUEST_STATE)[keyof typeof REQUEST_STATE]
const REQUEST_STATES: readonly string[] = Object.values(REQUEST_STATE)
const isRequestState = (v: unknown): v is RequestState => typeof v === 'string' && REQUEST_STATES.includes(v)

export const QUOTE_STATE = {
  open: 'Open',
  converted: 'Converted',
  lost: 'Lost',
} as const
export type QuoteState = (typeof QUOTE_STATE)[keyof typeof QUOTE_STATE]
const QUOTE_STATES: readonly string[] = Object.values(QUOTE_STATE)
const isQuoteState = (v: unknown): v is QuoteState => typeof v === 'string' && QUOTE_STATES.includes(v)

/* ── Request bodies ───────────────────────────────────────────────────────── */

/** `SalesRequestLineRequest`: every optional member is sent (null when unknown), so a retry sends one shape only. */
export interface SalesRequestLineBody {
  readonly lineId: string
  readonly productId: string
  readonly expectedProductVersion: string
  readonly orderUnitId: string
  readonly quantity: string
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

/** `CreateSalesRequestRequest`. No company, actor, state or version: the server's scope owns those. No price. */
export interface CreateSalesRequestBody {
  readonly partyId: string
  readonly companyAccountId: string
  readonly businessDate: string
  readonly lines: readonly SalesRequestLineBody[]
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

/** `SalesDraftStateCommandRequest` as the request decisions use it: `reason` only on a rejection (required there). */
export interface SalesRequestDecisionBody {
  readonly expectedVersion: string
  readonly reason?: string
}

/** `SalesQuoteLineRequest`: a price travels with its own price unit; a discount only beside a price (the server judges). */
export interface SalesQuoteLineBody {
  readonly lineId: string
  readonly productId: string
  readonly expectedProductVersion: string
  readonly orderUnitId: string
  readonly quantity: string
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly discountPercent: string | null
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

/** `CreateSalesQuoteRequest`: `requestIds` names the covered requests of ONE customer (No 163); null covers none. */
export interface CreateSalesQuoteBody {
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly businessDate: string
  readonly lines: readonly SalesQuoteLineBody[]
  readonly requestIds: readonly string[] | null
  readonly validUntil: string | null
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

/** `SalesQuoteRevisionRequest`: every line and the terms replaced; the customer, currency, date and requests stay. */
export interface SalesQuoteRevisionBody {
  readonly expectedVersion: string
  readonly lines: readonly SalesQuoteLineBody[]
  readonly validUntil: string | null
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

/**
 * `SalesQuoteLossRequestV2` (SAT-D25; retires the free-text v1): an entry of the Settings loss reason list and the
 * explanation — required when the entry says so (`requiresExplanation`), otherwise optional (null).
 */
export interface SalesQuoteLossBody {
  readonly expectedVersion: string
  readonly reasonId: string
  readonly explanation: string | null
}

/** `SalesQuoteExpiryApprovalRequest` (SAT-D24 / SAT-D44): approve converting the expired quote's current revision. */
export interface SalesQuoteExpiryApprovalBody {
  readonly expectedVersion: string
  readonly note: string | null
}

/** `SalesQuoteConversionRequest`: "Siparişe dönüştür" — the quote's version and the order's business date. */
export interface SalesQuoteConversionBody {
  readonly expectedVersion: string
  readonly businessDate: string
}

/* ── Views ────────────────────────────────────────────────────────────────── */

export interface SalesRequestSummary {
  readonly id: string
  readonly companyId: string
  readonly documentNo: string
  readonly state: RequestState
  readonly version: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly businessDate: string
  readonly createdAtUtc: string
  readonly lineCount: number
}

export interface SalesRequestLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productVersion: string
  readonly productCode: string
  readonly productName: string
  readonly orderUnitId: string
  readonly orderUnitCode: string
  readonly quantity: string
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

export interface SalesRequestDecision {
  readonly decidedBy: string
  readonly decidedAtUtc: string
  readonly rejectionReason: string | null
}

export interface SalesRequestDetail {
  readonly request: SalesRequestSummary
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
  readonly lines: readonly SalesRequestLine[]
  readonly decision: SalesRequestDecision | null
}

export interface SalesRequestMutation {
  readonly detail: SalesRequestDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

export interface SalesRequestPage {
  readonly items: readonly SalesRequestSummary[]
  readonly nextCursor: string | null
}

export interface SalesQuoteSummary {
  readonly id: string
  readonly companyId: string
  readonly documentNo: string
  readonly state: QuoteState
  readonly version: string
  readonly revision: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly businessDate: string
  readonly validUntil: string | null
  readonly createdAtUtc: string
  readonly lineCount: number
  readonly convertedDraftId: string | null
}

export interface SalesQuoteLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productVersion: string
  readonly productCode: string
  readonly productName: string
  readonly orderUnitId: string
  readonly orderUnitCode: string
  readonly quantity: string
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly discountPercent: string | null
  /** `Withheld` ⇒ the three commercial texts are null BECAUSE the caller may not see them (`sales.orders.price.read`). */
  readonly priceDisclosure: PriceDisclosure
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
}

/**
 * The recorded loss. SAT-D25: `reasonId` / `reasonCode` / `reasonLabel` name the Settings list entry and `reason` is the
 * explanation (null when none was needed). A loss recorded before SAT-D25 has no entry and its free text is `reason`.
 */
export interface SalesQuoteLoss {
  readonly reason: string | null
  readonly lostBy: string
  readonly lostAtUtc: string
  readonly reasonId: string | null
  readonly reasonCode: string | null
  readonly reasonLabel: string | null
}

/** SAT-D44 (1): one recorded revision — who wrote it (Rev.0: who created the quote), when, and its validity date. */
export interface SalesQuoteRevisionRecord {
  readonly revision: string
  readonly validUntil: string | null
  readonly revisedBy: string
  readonly revisedAtUtc: string
}

/** SAT-D24 / SAT-D44 (1): one recorded expired-quote approval. */
export interface SalesQuoteExpiryApproval {
  readonly revision: string
  readonly validUntil: string
  readonly businessDay: string
  readonly approvedBy: string
  readonly approvedAtUtc: string
  readonly note: string | null
}

/** SAT-D24, measured by the server at the read on ITS business day (Europe/Istanbul). */
export interface SalesQuoteExpiry {
  readonly businessDay: string
  readonly isExpired: boolean
  readonly isApproved: boolean
}

export interface SalesQuoteConversion {
  readonly draftId: string
  readonly convertedBy: string
  readonly convertedAtUtc: string
}

export interface SalesQuoteDetail {
  readonly quote: SalesQuoteSummary
  readonly requestIds: readonly string[]
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
  readonly lines: readonly SalesQuoteLine[]
  readonly loss: SalesQuoteLoss | null
  readonly conversion: SalesQuoteConversion | null
  readonly createdBy: string
  readonly revisions: readonly SalesQuoteRevisionRecord[]
  readonly expiryApprovals: readonly SalesQuoteExpiryApproval[]
  readonly expiry: SalesQuoteExpiry
}

/** SAT-D25: one selectable loss reason (an active entry of the list in force). */
export interface SalesQuoteLossReasonOption {
  readonly reasonId: string
  readonly code: string
  readonly label: string
  readonly requiresExplanation: boolean
}

/** SAT-D25: `isDefined` false (no reasons) when Settings holds no list — the loss is then refused by the server. */
export interface SalesQuoteLossReasons {
  readonly businessDay: string
  readonly isDefined: boolean
  readonly profileId: string | null
  readonly profileVersion: string | null
  readonly reasons: readonly SalesQuoteLossReasonOption[]
}

/** SAT-D39: the default validity suggestion; every member but the date null = Settings holds no value (no suggestion). */
export interface SalesQuoteValiditySuggestion {
  readonly businessDate: string
  readonly validityDays: number | null
  readonly suggestedValidUntil: string | null
  readonly profileId: string | null
  readonly profileVersion: string | null
}

export interface SalesQuoteMutation {
  readonly detail: SalesQuoteDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

export interface SalesQuotePage {
  readonly items: readonly SalesQuoteSummary[]
  readonly nextCursor: string | null
}

/** The problem codes the request and quote routes add (contract deltas of S4). Unknown codes still decode. */
export const POOL_PROBLEM_CODE = {
  requestRejectionReasonRequired: 'sales_request_rejection_reason_required',
  requestRejectionReasonTooLong: 'sales_request_rejection_reason_too_long',
  requestHasApprovedOrder: 'sales_request_has_approved_order',
  requestWithoutLines: 'sales_request_without_lines',
  quoteRequestUnknown: 'sales_quote_request_unknown',
  quoteRequestCustomerMismatch: 'sales_quote_request_customer_mismatch',
  quoteRequestUnavailable: 'sales_quote_request_unavailable',
  quoteRequestNotApproved: 'sales_quote_request_not_approved',
  quoteRequestsInvalid: 'sales_quote_requests_invalid',
  quoteLossReasonRequired: 'sales_quote_loss_reason_required',
  quoteLossReasonTooLong: 'sales_quote_loss_reason_too_long',
  quoteValidUntilBeforeDate: 'sales_quote_valid_until_before_date',
  quoteWithoutLines: 'sales_quote_without_lines',
  // SAT-D24: a quote past its validity date (on the SERVER's business day) converts only with a recorded approval.
  quoteExpired: 'sales_quote_expired',
  quoteNotExpired: 'sales_quote_not_expired',
  quoteExpiryAlreadyApproved: 'sales_quote_expiry_already_approved',
  // SAT-D25: the loss names an active entry of the Settings list.
  quoteLossReasonUnknown: 'sales_quote_loss_reason_unknown',
} as const
export type PoolProblemCode = (typeof POOL_PROBLEM_CODE)[keyof typeof POOL_PROBLEM_CODE]

/* ── Decoding ─────────────────────────────────────────────────────────────── */

type Obj = Record<string, unknown>

/** Exactly these members, all present. A missing nullable is as wrong as an extra one. */
function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  const own = Object.keys(obj)
  if (own.length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const DECIMAL_RE = /^[0-9]+(?:\.[0-9]+)?$/
const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const REVISION_RE = /^Rev\.(?:0|[1-9][0-9]*)$/

const isDecimal = (v: unknown, positive: boolean): v is string =>
  typeof v === 'string' && v.length <= LIMITS.decimalText && DECIMAL_RE.test(v) && (!positive || /[1-9]/.test(v))
const isNullable = <T>(v: unknown, check: (x: unknown) => x is T): v is T | null => v === null || check(v)
const isText = (v: unknown, max: number): v is string => typeof v === 'string' && v.length > 0 && v.length <= max
const isFilledText = (v: unknown, max: number): v is string => isText(v, max) && v.trim().length > 0
const isNote = (v: unknown): v is string | null => v === null || (typeof v === 'string' && v.length <= LIMITS.note)
const isInstant = (v: unknown): v is string => typeof v === 'string' && INSTANT_RE.test(v) && !Number.isNaN(Date.parse(v))
const isActor = (v: unknown): v is string => isFilledText(v, 128)
const isOwnerVersion = (v: unknown): v is string => isFilledText(v, LIMITS.versionText)
const isConcurrencyVersion = (v: unknown): v is string => isVersionString(v) && v.length <= LIMITS.versionText
const isPositiveInt = (v: unknown, max: number): v is number =>
  typeof v === 'number' && Number.isInteger(v) && v >= 1 && v <= max
const isPriceDisclosure = (v: unknown): v is PriceDisclosure => v === 'Visible' || v === 'Withheld'
const isCursor = (v: unknown): v is string => isText(v, 4096)

/** The lines of a detail: exactly `lineCount` of them, each identity (id and number) once. */
function decodeLines<L extends { readonly lineId: string; readonly lineNo: number }>(
  value: unknown,
  lineCount: number,
  decode: (entry: unknown) => L | null,
): readonly L[] | null {
  if (!Array.isArray(value) || value.length !== lineCount) return null
  const lines: L[] = []
  const ids = new Set<string>()
  const numbers = new Set<number>()
  for (const entry of value) {
    const line = decode(entry)
    if (line === null || ids.has(line.lineId) || numbers.has(line.lineNo)) return null
    ids.add(line.lineId)
    numbers.add(line.lineNo)
    lines.push(line)
  }
  return lines
}

function decodePage<T extends { readonly id: string }>(
  value: unknown,
  decode: (entry: unknown) => T | null,
): { readonly items: readonly T[]; readonly nextCursor: string | null } | null {
  const o = exact(value, ['items', 'nextCursor'])
  if (o === null || !Array.isArray(o.items) || !isNullable(o.nextCursor, isCursor)) return null
  const items: T[] = []
  const ids = new Set<string>()
  for (const entry of o.items) {
    const item = decode(entry)
    if (item === null || ids.has(item.id)) return null
    ids.add(item.id)
    items.push(item)
  }
  return { items, nextCursor: o.nextCursor }
}

/* ── Request ── */

const REQUEST_SUMMARY_KEYS = [
  'id', 'companyId', 'documentNo', 'state', 'version', 'partyId', 'companyAccountId', 'businessDate', 'createdAtUtc', 'lineCount',
] as const

export function decodeRequestSummary(value: unknown): SalesRequestSummary | null {
  const o = exact(value, REQUEST_SUMMARY_KEYS)
  if (
    o === null ||
    !isReferenceId(o.id) ||
    !isReferenceId(o.companyId) ||
    !isFilledText(o.documentNo, 200) ||
    !isRequestState(o.state) ||
    !isConcurrencyVersion(o.version) ||
    !isReferenceId(o.partyId) ||
    !isReferenceId(o.companyAccountId) ||
    !isDateOnly(o.businessDate) ||
    !isInstant(o.createdAtUtc) ||
    !isPositiveInt(o.lineCount, LIMITS.lines)
  ) {
    return null
  }
  return {
    id: o.id,
    companyId: o.companyId,
    documentNo: o.documentNo,
    state: o.state,
    version: o.version,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    businessDate: o.businessDate,
    createdAtUtc: o.createdAtUtc,
    lineCount: o.lineCount,
  }
}

const REQUEST_LINE_KEYS = [
  'lineId', 'lineNo', 'productId', 'productVersion', 'productCode', 'productName', 'orderUnitId', 'orderUnitCode', 'quantity',
  'requestedDeliveryDate', 'note',
] as const

function decodeRequestLine(value: unknown): SalesRequestLine | null {
  const o = exact(value, REQUEST_LINE_KEYS)
  if (
    o === null ||
    !isReferenceId(o.lineId) ||
    !isPositiveInt(o.lineNo, LIMITS.lines) ||
    !isReferenceId(o.productId) ||
    !isOwnerVersion(o.productVersion) ||
    !isFilledText(o.productCode, 200) ||
    !isFilledText(o.productName, 500) ||
    !isReferenceId(o.orderUnitId) ||
    !isFilledText(o.orderUnitCode, 200) ||
    !isDecimal(o.quantity, true) ||
    !isNullable(o.requestedDeliveryDate, isDateOnly) ||
    !isNote(o.note)
  ) {
    return null
  }
  return {
    lineId: o.lineId,
    lineNo: o.lineNo,
    productId: o.productId,
    productVersion: o.productVersion,
    productCode: o.productCode,
    productName: o.productName,
    orderUnitId: o.orderUnitId,
    orderUnitCode: o.orderUnitCode,
    quantity: o.quantity,
    requestedDeliveryDate: o.requestedDeliveryDate,
    note: o.note,
  }
}

/**
 * The decision facts against the state the summary carries: Submitted holds none; Approved a decision without a reason;
 * Rejected a decision WITH its reason (No 161: a rejection is never without one). Anything else is malformed.
 */
function decodeRequestDecision(value: unknown, state: RequestState): SalesRequestDecision | null | undefined {
  if (value === null) return state === REQUEST_STATE.submitted ? null : undefined
  if (state === REQUEST_STATE.submitted) return undefined
  const o = exact(value, ['decidedBy', 'decidedAtUtc', 'rejectionReason'])
  if (o === null || !isActor(o.decidedBy) || !isInstant(o.decidedAtUtc)) return undefined
  if (state === REQUEST_STATE.approved && o.rejectionReason !== null) return undefined
  if (state === REQUEST_STATE.rejected && !(isFilledText(o.rejectionReason, LIMITS.note))) return undefined
  return { decidedBy: o.decidedBy, decidedAtUtc: o.decidedAtUtc, rejectionReason: o.rejectionReason as string | null }
}

/** `expectedId`: the record the caller asked for. A detail of another record is not an answer. */
export function decodeRequestDetail(value: unknown, expectedId: string | null): SalesRequestDetail | null {
  const o = exact(value, ['request', 'requestedDeliveryDate', 'note', 'lines', 'decision'])
  if (o === null) return null
  const request = decodeRequestSummary(o.request)
  if (request === null || (expectedId !== null && request.id !== expectedId)) return null
  if (!isNullable(o.requestedDeliveryDate, isDateOnly) || !isNote(o.note)) return null
  const lines = decodeLines(o.lines, request.lineCount, decodeRequestLine)
  const decision = decodeRequestDecision(o.decision, request.state)
  if (lines === null || decision === undefined) return null
  return { request, requestedDeliveryDate: o.requestedDeliveryDate, note: o.note, lines, decision }
}

export function decodeRequestMutation(value: unknown): SalesRequestMutation | null {
  const o = exact(value, ['detail', 'isReplay', 'originalCorrelationId'])
  if (o === null || typeof o.isReplay !== 'boolean' || !isText(o.originalCorrelationId, 200)) return null
  const detail = decodeRequestDetail(o.detail, null)
  return detail === null ? null : { detail, isReplay: o.isReplay, originalCorrelationId: o.originalCorrelationId }
}

export function decodeRequestPage(value: unknown): SalesRequestPage | null {
  return decodePage(value, decodeRequestSummary)
}

/* ── Quote ── */

const QUOTE_SUMMARY_KEYS = [
  'id', 'companyId', 'documentNo', 'state', 'version', 'revision', 'partyId', 'companyAccountId', 'currencyId', 'businessDate',
  'validUntil', 'createdAtUtc', 'lineCount', 'convertedDraftId',
] as const

export function decodeQuoteSummary(value: unknown): SalesQuoteSummary | null {
  const o = exact(value, QUOTE_SUMMARY_KEYS)
  if (
    o === null ||
    !isReferenceId(o.id) ||
    !isReferenceId(o.companyId) ||
    !isFilledText(o.documentNo, 200) ||
    !isQuoteState(o.state) ||
    !isConcurrencyVersion(o.version) ||
    typeof o.revision !== 'string' ||
    !REVISION_RE.test(o.revision) ||
    !isReferenceId(o.partyId) ||
    !isReferenceId(o.companyAccountId) ||
    !isReferenceId(o.currencyId) ||
    !isDateOnly(o.businessDate) ||
    !isNullable(o.validUntil, isDateOnly) ||
    !isInstant(o.createdAtUtc) ||
    !isPositiveInt(o.lineCount, LIMITS.lines) ||
    !isNullable(o.convertedDraftId, isReferenceId) ||
    // Only a converted quote names its order (No 162: exactly one), and a converted quote always does.
    (o.state === QUOTE_STATE.converted) !== (o.convertedDraftId !== null)
  ) {
    return null
  }
  return {
    id: o.id,
    companyId: o.companyId,
    documentNo: o.documentNo,
    state: o.state,
    version: o.version,
    revision: o.revision,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    currencyId: o.currencyId,
    businessDate: o.businessDate,
    validUntil: o.validUntil,
    createdAtUtc: o.createdAtUtc,
    lineCount: o.lineCount,
    convertedDraftId: o.convertedDraftId,
  }
}

const QUOTE_LINE_KEYS = [
  'lineId', 'lineNo', 'productId', 'productVersion', 'productCode', 'productName', 'orderUnitId', 'orderUnitCode', 'quantity',
  'unitPrice', 'priceUnitId', 'discountPercent', 'priceDisclosure', 'requestedDeliveryDate', 'note',
] as const

function decodeQuoteLine(value: unknown): SalesQuoteLine | null {
  const o = exact(value, QUOTE_LINE_KEYS)
  if (
    o === null ||
    !isReferenceId(o.lineId) ||
    !isPositiveInt(o.lineNo, LIMITS.lines) ||
    !isReferenceId(o.productId) ||
    !isOwnerVersion(o.productVersion) ||
    !isFilledText(o.productCode, 200) ||
    !isFilledText(o.productName, 500) ||
    !isReferenceId(o.orderUnitId) ||
    !isFilledText(o.orderUnitCode, 200) ||
    !isDecimal(o.quantity, true) ||
    !isNullable(o.unitPrice, (v): v is string => isDecimal(v, false)) ||
    !isNullable(o.priceUnitId, isReferenceId) ||
    // The source pairs them: a price without its unit, or a unit without a price, is refused.
    (o.unitPrice === null) !== (o.priceUnitId === null) ||
    !isNullable(o.discountPercent, (v): v is string => isDecimal(v, false)) ||
    !isPriceDisclosure(o.priceDisclosure) ||
    // Withheld means the texts were withheld: a withheld line carrying a price is not this contract.
    (o.priceDisclosure === 'Withheld' && (o.unitPrice !== null || o.priceUnitId !== null || o.discountPercent !== null)) ||
    !isNullable(o.requestedDeliveryDate, isDateOnly) ||
    !isNote(o.note)
  ) {
    return null
  }
  return {
    lineId: o.lineId,
    lineNo: o.lineNo,
    productId: o.productId,
    productVersion: o.productVersion,
    productCode: o.productCode,
    productName: o.productName,
    orderUnitId: o.orderUnitId,
    orderUnitCode: o.orderUnitCode,
    quantity: o.quantity,
    unitPrice: o.unitPrice,
    priceUnitId: o.priceUnitId,
    discountPercent: o.discountPercent,
    priceDisclosure: o.priceDisclosure,
    requestedDeliveryDate: o.requestedDeliveryDate,
    note: o.note,
  }
}

function decodeLoss(value: unknown): SalesQuoteLoss | null {
  const o = exact(value, ['reason', 'lostBy', 'lostAtUtc', 'reasonId', 'reasonCode', 'reasonLabel'])
  if (o === null || !isNullable(o.reason, (v): v is string => isFilledText(v, LIMITS.note)) || !isActor(o.lostBy) || !isInstant(o.lostAtUtc)) return null
  // Either a SAT-D25 entry with its code and label (the explanation optional), or a loss recorded before it (no entry,
  // its free text is the reason). Half an entry is neither.
  const listed = isReferenceId(o.reasonId) && isFilledText(o.reasonCode, 64) && isFilledText(o.reasonLabel, 200)
  const legacy = o.reasonId === null && o.reasonCode === null && o.reasonLabel === null && o.reason !== null
  if (!listed && !legacy) return null
  return {
    reason: o.reason as string | null,
    lostBy: o.lostBy,
    lostAtUtc: o.lostAtUtc,
    reasonId: listed ? (o.reasonId as string) : null,
    reasonCode: listed ? (o.reasonCode as string) : null,
    reasonLabel: listed ? (o.reasonLabel as string) : null,
  }
}

function decodeRevisionRecords(value: unknown, currentRevision: string): readonly SalesQuoteRevisionRecord[] | null {
  if (!Array.isArray(value) || value.length === 0) return null
  const records: SalesQuoteRevisionRecord[] = []
  for (const [index, entry] of value.entries()) {
    const o = exact(entry, ['revision', 'validUntil', 'revisedBy', 'revisedAtUtc'])
    // Rev.0, Rev.1, … in order: who created the quote and who wrote each revision.
    if (o === null || o.revision !== `Rev.${index}` || !isNullable(o.validUntil, isDateOnly) || !isActor(o.revisedBy) || !isInstant(o.revisedAtUtc)) {
      return null
    }
    records.push({ revision: o.revision, validUntil: o.validUntil, revisedBy: o.revisedBy, revisedAtUtc: o.revisedAtUtc })
  }
  return records[records.length - 1].revision === currentRevision ? records : null
}

function decodeExpiryApprovals(value: unknown): readonly SalesQuoteExpiryApproval[] | null {
  if (!Array.isArray(value)) return null
  const approvals: SalesQuoteExpiryApproval[] = []
  const revisions = new Set<string>()
  for (const entry of value) {
    const o = exact(entry, ['revision', 'validUntil', 'businessDay', 'approvedBy', 'approvedAtUtc', 'note'])
    if (
      o === null ||
      !isText(o.revision, 20) ||
      !REVISION_RE.test(o.revision) ||
      revisions.has(o.revision) ||
      !isDateOnly(o.validUntil) ||
      !isDateOnly(o.businessDay) ||
      !(o.validUntil < o.businessDay) ||
      !isActor(o.approvedBy) ||
      !isInstant(o.approvedAtUtc) ||
      !isNullable(o.note, (v): v is string => isFilledText(v, LIMITS.note))
    ) {
      return null
    }
    revisions.add(o.revision)
    approvals.push({
      revision: o.revision,
      validUntil: o.validUntil,
      businessDay: o.businessDay,
      approvedBy: o.approvedBy,
      approvedAtUtc: o.approvedAtUtc,
      note: o.note as string | null,
    })
  }
  return approvals
}

function decodeExpiry(value: unknown): SalesQuoteExpiry | null {
  const o = exact(value, ['businessDay', 'isExpired', 'isApproved'])
  if (o === null || !isDateOnly(o.businessDay) || typeof o.isExpired !== 'boolean' || typeof o.isApproved !== 'boolean') return null
  return { businessDay: o.businessDay, isExpired: o.isExpired, isApproved: o.isApproved }
}

function decodeConversion(value: unknown): SalesQuoteConversion | null {
  const o = exact(value, ['draftId', 'convertedBy', 'convertedAtUtc'])
  if (o === null || !isReferenceId(o.draftId) || !isActor(o.convertedBy) || !isInstant(o.convertedAtUtc)) return null
  return { draftId: o.draftId, convertedBy: o.convertedBy, convertedAtUtc: o.convertedAtUtc }
}

function decodeRequestIds(value: unknown): readonly string[] | null {
  if (!Array.isArray(value) || value.length > QUOTE_MAXIMUM_REQUESTS) return null
  const ids = new Set<string>()
  for (const id of value) {
    if (!isReferenceId(id) || ids.has(id)) return null
    ids.add(id)
  }
  return [...ids]
}

/** `expectedId`: the record the caller asked for. A detail of another record is not an answer. */
export function decodeQuoteDetail(value: unknown, expectedId: string | null): SalesQuoteDetail | null {
  const o = exact(value, [
    'quote', 'requestIds', 'requestedDeliveryDate', 'note', 'lines', 'loss', 'conversion', 'createdBy', 'revisions', 'expiryApprovals',
    'expiry',
  ])
  if (o === null) return null
  const quote = decodeQuoteSummary(o.quote)
  if (quote === null || (expectedId !== null && quote.id !== expectedId)) return null
  const requestIds = decodeRequestIds(o.requestIds)
  if (requestIds === null || !isNullable(o.requestedDeliveryDate, isDateOnly) || !isNote(o.note)) return null
  const lines = decodeLines(o.lines, quote.lineCount, decodeQuoteLine)
  if (lines === null) return null
  const loss = o.loss === null ? null : decodeLoss(o.loss)
  const conversion = o.conversion === null ? null : decodeConversion(o.conversion)
  if ((o.loss !== null && loss === null) || (o.conversion !== null && conversion === null)) return null
  // The lifecycle and its facts agree: Open has neither, Lost only its loss, Converted only its conversion — of the order
  // the summary names.
  const consistent =
    quote.state === QUOTE_STATE.open
      ? loss === null && conversion === null
      : quote.state === QUOTE_STATE.lost
        ? loss !== null && conversion === null
        : loss === null && conversion !== null && conversion.draftId === quote.convertedDraftId
  if (!consistent || !isActor(o.createdBy)) return null
  const revisions = decodeRevisionRecords(o.revisions, quote.revision)
  const expiryApprovals = decodeExpiryApprovals(o.expiryApprovals)
  const expiry = decodeExpiry(o.expiry)
  if (revisions === null || expiryApprovals === null || expiry === null || revisions[0].revisedBy !== o.createdBy) return null
  // SAT-D24: "approved" means an approval of the CURRENT revision is on record; the two answers agree or neither is shown.
  if (expiry.isApproved !== expiryApprovals.some((approval) => approval.revision === quote.revision)) return null
  // Expired is the server's word, on its own day; a quote without a validity date never expires.
  if (expiry.isExpired !== (quote.validUntil !== null && quote.validUntil < expiry.businessDay)) return null
  return {
    quote,
    requestIds,
    requestedDeliveryDate: o.requestedDeliveryDate,
    note: o.note,
    lines,
    loss,
    conversion,
    createdBy: o.createdBy,
    revisions,
    expiryApprovals,
    expiry,
  }
}

/** SAT-D25: the list read; a malformed entry or a repeated id refuses the whole list. */
export function decodeQuoteLossReasons(value: unknown): SalesQuoteLossReasons | null {
  const o = exact(value, ['businessDay', 'isDefined', 'profileId', 'profileVersion', 'reasons'])
  if (
    o === null ||
    !isDateOnly(o.businessDay) ||
    typeof o.isDefined !== 'boolean' ||
    !isNullable(o.profileId, isReferenceId) ||
    !isNullable(o.profileVersion, (v): v is string => isText(v, 20)) ||
    !Array.isArray(o.reasons)
  ) {
    return null
  }
  const reasons: SalesQuoteLossReasonOption[] = []
  const ids = new Set<string>()
  for (const entry of o.reasons) {
    const r = exact(entry, ['reasonId', 'code', 'label', 'requiresExplanation'])
    if (r === null || !isReferenceId(r.reasonId) || ids.has(r.reasonId) || !isFilledText(r.code, 64) || !isFilledText(r.label, 200)
      || typeof r.requiresExplanation !== 'boolean') {
      return null
    }
    ids.add(r.reasonId)
    reasons.push({ reasonId: r.reasonId, code: r.code, label: r.label, requiresExplanation: r.requiresExplanation })
  }
  // Not defined ⇒ nothing to choose; defined ⇒ its version is named.
  if (o.isDefined ? o.profileId === null || o.profileVersion === null : reasons.length > 0 || o.profileId !== null) return null
  return { businessDay: o.businessDay, isDefined: o.isDefined, profileId: o.profileId, profileVersion: o.profileVersion, reasons }
}

/** SAT-D39: the suggestion read; all four Settings members are present together or absent together. */
export function decodeQuoteValiditySuggestion(value: unknown, businessDate: string): SalesQuoteValiditySuggestion | null {
  const o = exact(value, ['businessDate', 'validityDays', 'suggestedValidUntil', 'profileId', 'profileVersion'])
  if (o === null || o.businessDate !== businessDate) return null
  const none = o.validityDays === null && o.suggestedValidUntil === null && o.profileId === null && o.profileVersion === null
  const some =
    typeof o.validityDays === 'number' &&
    Number.isInteger(o.validityDays) &&
    o.validityDays > 0 &&
    isDateOnly(o.suggestedValidUntil) &&
    o.suggestedValidUntil > businessDate &&
    isReferenceId(o.profileId) &&
    isText(o.profileVersion, 20)
  if (!none && !some) return null
  return {
    businessDate,
    validityDays: some ? (o.validityDays as number) : null,
    suggestedValidUntil: some ? (o.suggestedValidUntil as string) : null,
    profileId: some ? (o.profileId as string) : null,
    profileVersion: some ? (o.profileVersion as string) : null,
  }
}

export function decodeQuoteMutation(value: unknown): SalesQuoteMutation | null {
  const o = exact(value, ['detail', 'isReplay', 'originalCorrelationId'])
  if (o === null || typeof o.isReplay !== 'boolean' || !isText(o.originalCorrelationId, 200)) return null
  const detail = decodeQuoteDetail(o.detail, null)
  return detail === null ? null : { detail, isReplay: o.isReplay, originalCorrelationId: o.originalCorrelationId }
}

export function decodeQuotePage(value: unknown): SalesQuotePage | null {
  return decodePage(value, decodeQuoteSummary)
}
