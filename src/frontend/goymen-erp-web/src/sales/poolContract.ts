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
 * Open policies are NOT encoded here (no approver count, no quote validity enforcement, no closed loss-reason list, no
 * price permission choice): the server decides and these decoders only read what it answers.
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
export type SalesQuoteCommand = 'revision' | 'loss' | 'conversion'
export const salesQuoteCommandPath = (id: string, command: SalesQuoteCommand) => `${salesQuoteDetailPath(id)}/${command}`

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

/** `SalesQuoteLossRequest`: the free-text reason is required (no closed reason list is decided). */
export interface SalesQuoteLossBody {
  readonly expectedVersion: string
  readonly reason: string
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

export interface SalesQuoteLoss {
  readonly reason: string
  readonly lostBy: string
  readonly lostAtUtc: string
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
  const o = exact(value, ['reason', 'lostBy', 'lostAtUtc'])
  if (o === null || !isFilledText(o.reason, LIMITS.note) || !isActor(o.lostBy) || !isInstant(o.lostAtUtc)) return null
  return { reason: o.reason, lostBy: o.lostBy, lostAtUtc: o.lostAtUtc }
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
  const o = exact(value, ['quote', 'requestIds', 'requestedDeliveryDate', 'note', 'lines', 'loss', 'conversion'])
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
  if (!consistent) return null
  return { quote, requestIds, requestedDeliveryDate: o.requestedDeliveryDate, note: o.note, lines, loss, conversion }
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
