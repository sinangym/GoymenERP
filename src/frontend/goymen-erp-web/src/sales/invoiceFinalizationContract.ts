/**
 * API-36 (PR #986): the Sales invoice detail (`GET /sales/invoices/{id}`, `SalesInvoiceDetailView`), the order's invoices
 * (`GET /sales/drafts/{id}/invoices`, API-09) and the two finalization commands
 * (`POST /sales/invoices/{id}/finalization`, `POST /sales/invoices/{id}/finance-admission`).
 *
 * Strict and fail-closed: every object must carry exactly the members `contracts/openapi/goymen-erp-v1.json` declares (the
 * server writes nulls, so a nullable member is present as `null`), integers are JSON numbers (strict number handling), every
 * amount, rate and percentage stays the server's TEXT (never a JS `number`), and a value outside the shapes below makes the
 * whole answer `null` — "Beklenmeyen yanıt" — never a partial object. Nothing here adds, rounds or compares an amount.
 */
import { LIMITS } from './contract'
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

type Obj = Record<string, unknown>

const record = (value: unknown): Obj | null =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Obj) : null

/** The object's key set equals `fields` exactly; an extra or a missing member is not this contract. */
function exact(value: unknown, fields: readonly string[]): Obj | null {
  const object = record(value)
  if (object === null) return null
  const keys = Object.keys(object)
  if (keys.length !== fields.length) return null
  return fields.every((field) => Object.hasOwn(object, field)) ? object : null
}

const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const MAX_INSTANT_LENGTH = 64
/** An unsigned decimal as the server's formatter writes it (`F{scale}` or the price formatter): digits, one optional fraction. */
const AMOUNT_RE = /^\d+(?:\.\d+)?$/
/** The match difference is the official amount less the invoice amount: it may be negative. */
const SIGNED_AMOUNT_RE = /^-?\d+(?:\.\d+)?$/
const MAX_TEXT_LENGTH = 512

const isInstant = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= MAX_INSTANT_LENGTH && INSTANT_RE.test(value) && !Number.isNaN(Date.parse(value))
const isAmount = (value: unknown): value is string => typeof value === 'string' && AMOUNT_RE.test(value)
const isSignedAmount = (value: unknown): value is string => typeof value === 'string' && SIGNED_AMOUNT_RE.test(value)
const isText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= MAX_TEXT_LENGTH
const isNullableText = (value: unknown): value is string | null => value === null || isText(value)
const isNullableInstant = (value: unknown): value is string | null => value === null || isInstant(value)
const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const isPositiveCount = (value: unknown): value is number => isCount(value) && value >= 1

function list<T>(value: unknown, item: (entry: unknown) => T | null): readonly T[] | null {
  if (!Array.isArray(value)) return null
  const out: T[] = []
  for (const entry of value) {
    const decoded = item(entry)
    if (decoded === null) return null
    out.push(decoded)
  }
  return out
}

/* ── Paths ────────────────────────────────────────────────────────────────── */

const segment = (id: string) => encodeURIComponent(id)
export const salesInvoicePath = (invoiceId: string) => `/sales/invoices/${segment(invoiceId)}`
export const salesInvoiceFinalizationPath = (invoiceId: string) => `${salesInvoicePath(invoiceId)}/finalization`
export const salesInvoiceFinanceAdmissionPath = (invoiceId: string) => `${salesInvoicePath(invoiceId)}/finance-admission`
export const salesDraftInvoicesPath = (draftId: string) => `/sales/drafts/${segment(draftId)}/invoices`

/* ── Enumerations ─────────────────────────────────────────────────────────── */

export const INVOICE_STATES = ['Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled', 'Finalized'] as const
export type InvoiceState = (typeof INVOICE_STATES)[number]
const isInvoiceState = (value: unknown): value is InvoiceState =>
  typeof value === 'string' && (INVOICE_STATES as readonly string[]).includes(value)

export const FINANCE_STATUSES = ['waiting', 'admitted'] as const
export type FinanceStatus = (typeof FINANCE_STATUSES)[number]
const isFinanceStatus = (value: unknown): value is FinanceStatus =>
  typeof value === 'string' && (FINANCE_STATUSES as readonly string[]).includes(value)

/** MOD-04 F2a: the accounting voucher has no source yet; this is the only value the server answers. */
export const ACCOUNTING_STATUSES = ['no_source'] as const
export type AccountingStatus = (typeof ACCOUNTING_STATUSES)[number]
const isAccountingStatus = (value: unknown): value is AccountingStatus =>
  typeof value === 'string' && (ACCOUNTING_STATUSES as readonly string[]).includes(value)

const FUNCTIONAL_ADJUSTED = ['Net', 'Vat', 'Withholding'] as const
type FunctionalAdjusted = (typeof FUNCTIONAL_ADJUSTED)[number]
const isFunctionalAdjusted = (value: unknown): value is FunctionalAdjusted =>
  typeof value === 'string' && (FUNCTIONAL_ADJUSTED as readonly string[]).includes(value)

/**
 * The finance ledger's last outcome, as the server writes it: the answer's name lower-cased, then optionally `:` and a short
 * ASCII reason (`OutcomeText`, at most 40 reason characters). `refused:denied` is Finance's authority refusal.
 */
export const LAST_OUTCOME_CATEGORIES = ['unavailable', 'admitted', 'refused', 'keyconflict', 'retry'] as const
export type LastOutcomeCategory = (typeof LAST_OUTCOME_CATEGORIES)[number]
const LAST_OUTCOME_RE = /^(unavailable|admitted|refused|keyconflict|retry)(?::([a-z0-9_.-]{1,40}))?$/

export interface LastOutcome {
  readonly category: LastOutcomeCategory
  readonly reason: string | null
}

export function parseLastOutcome(value: string): LastOutcome | null {
  const match = LAST_OUTCOME_RE.exec(value)
  if (match === null) return null
  return { category: match[1] as LastOutcomeCategory, reason: match[2] ?? null }
}

/* ── Detail ───────────────────────────────────────────────────────────────── */

export interface InvoiceAmounts {
  readonly netAmount: string
  readonly vatAmount: string
  readonly withheldVatAmount: string
  readonly grossAmount: string
  readonly payableAmount: string
}
const AMOUNTS_FIELDS = ['netAmount', 'vatAmount', 'withheldVatAmount', 'grossAmount', 'payableAmount'] as const

function decodeAmounts(value: unknown): InvoiceAmounts | null {
  const o = exact(value, AMOUNTS_FIELDS)
  if (o === null || !AMOUNTS_FIELDS.every((field) => isAmount(o[field]))) return null
  return {
    netAmount: o.netAmount as string,
    vatAmount: o.vatAmount as string,
    withheldVatAmount: o.withheldVatAmount as string,
    grossAmount: o.grossAmount as string,
    payableAmount: o.payableAmount as string,
  }
}
const decodeNullableAmounts = (value: unknown): InvoiceAmounts | null | undefined =>
  value === null ? null : (decodeAmounts(value) ?? undefined)

export interface InvoiceRate {
  readonly rate: string
  readonly rateDay: string
  readonly bulletinDate: string
  readonly functionalCurrencyId: string
  readonly functionalScale: number
}
const RATE_FIELDS = ['rate', 'rateDay', 'bulletinDate', 'functionalCurrencyId', 'functionalScale'] as const

function decodeRate(value: unknown): InvoiceRate | null {
  const o = exact(value, RATE_FIELDS)
  if (o === null || !isAmount(o.rate) || !isDateOnly(o.rateDay) || !isDateOnly(o.bulletinDate)
    || !isReferenceId(o.functionalCurrencyId) || !isCount(o.functionalScale)) return null
  return { rate: o.rate, rateDay: o.rateDay, bulletinDate: o.bulletinDate, functionalCurrencyId: o.functionalCurrencyId, functionalScale: o.functionalScale }
}

export interface InvoiceVatGroup {
  readonly vatPercent: string
  readonly lineCount: number
  readonly amounts: InvoiceAmounts
  readonly functional: InvoiceAmounts | null
}
const VAT_GROUP_FIELDS = ['vatPercent', 'lineCount', 'amounts', 'functional'] as const

function decodeVatGroup(value: unknown): InvoiceVatGroup | null {
  const o = exact(value, VAT_GROUP_FIELDS)
  if (o === null || !isAmount(o.vatPercent) || !isPositiveCount(o.lineCount)) return null
  const amounts = decodeAmounts(o.amounts)
  const functional = decodeNullableAmounts(o.functional)
  if (amounts === null || functional === undefined) return null
  return { vatPercent: o.vatPercent, lineCount: o.lineCount, amounts, functional }
}

/** The planned instalment of the invoice (before finalization), in the server's order. */
export interface InvoiceInstalment {
  readonly sequenceNo: number
  readonly dueDate: string
  readonly sharePercent: string
  readonly amount: string
}
const INSTALMENT_FIELDS = ['sequenceNo', 'dueDate', 'sharePercent', 'amount'] as const

function decodeInstalment(value: unknown): InvoiceInstalment | null {
  const o = exact(value, INSTALMENT_FIELDS)
  if (o === null || !isPositiveCount(o.sequenceNo) || !isDateOnly(o.dueDate) || !isAmount(o.sharePercent) || !isAmount(o.amount)) return null
  return { sequenceNo: o.sequenceNo, dueDate: o.dueDate, sharePercent: o.sharePercent, amount: o.amount }
}

export interface InvoiceLine {
  readonly lineId: string
  readonly lineNo: number
  readonly quantity: string
  readonly netUnitPrice: string
  readonly vatPercent: string
  readonly amounts: InvoiceAmounts
}
const LINE_FIELDS = [
  'lineId', 'lineNo', 'shipmentId', 'dispatchNoteId', 'dispatchNoteNumber', 'noteLineNo', 'shipmentDate', 'salesOrderId',
  'salesOrderLineId', 'priceBasisRevision', 'productId', 'unitId', 'quantity', 'netUnitPrice', 'vatPercent', 'withholdingShare',
  'amounts', 'functional',
] as const
const LINE_FUNCTIONAL_FIELDS = ['amounts', 'adjusted', 'needsReview'] as const

function decodeLineFunctional(value: unknown): true | null {
  const o = exact(value, LINE_FUNCTIONAL_FIELDS)
  if (o === null || typeof o.needsReview !== 'boolean' || decodeAmounts(o.amounts) === null) return null
  const adjusted = list(o.adjusted, (entry) => (isFunctionalAdjusted(entry) ? entry : null))
  return adjusted === null ? null : true
}

function decodeLine(value: unknown): InvoiceLine | null {
  const o = exact(value, LINE_FIELDS)
  if (
    o === null || !isReferenceId(o.lineId) || !isPositiveCount(o.lineNo) || !isReferenceId(o.shipmentId)
    || !isReferenceId(o.dispatchNoteId) || !isText(o.dispatchNoteNumber) || !isPositiveCount(o.noteLineNo)
    || !isDateOnly(o.shipmentDate) || !isReferenceId(o.salesOrderId) || !isReferenceId(o.salesOrderLineId)
    || !isText(o.priceBasisRevision) || !isReferenceId(o.productId) || !isReferenceId(o.unitId) || !isAmount(o.quantity)
    || !isAmount(o.netUnitPrice) || !isAmount(o.vatPercent) || !(o.withholdingShare === null || isAmount(o.withholdingShare))
  ) return null
  const amounts = decodeAmounts(o.amounts)
  if (amounts === null || (o.functional !== null && decodeLineFunctional(o.functional) === null)) return null
  return { lineId: o.lineId, lineNo: o.lineNo, quantity: o.quantity, netUnitPrice: o.netUnitPrice, vatPercent: o.vatPercent, amounts }
}

export const SALES_INVOICE_DECISION_FIELDS = [
  'submittedBy', 'submittedAtUtc', 'decidedBy', 'decidedAtUtc', 'decisionReason', 'cancelledBy', 'cancelledAtUtc', 'cancellationReason',
] as const

/**
 * A decision or cancellation reason: the server accepts up to its approval-reason limit (`LIMITS.note`, 4000), wider than a
 * name — a long reason must not turn the whole detail into "Beklenmeyen yanıt".
 */
const isNullableReason = (value: unknown): value is string | null =>
  value === null || (typeof value === 'string' && value.trim().length > 0 && value.length <= LIMITS.note)

/** API-33: who sent the draft to approval, who decided, who cancelled — the server's record, shown as written. */
export interface InvoiceDecision {
  readonly submittedBy: string | null
  readonly submittedAtUtc: string | null
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
  readonly cancelledBy: string | null
  readonly cancelledAtUtc: string | null
  readonly cancellationReason: string | null
}

function decodeDecision(value: unknown): InvoiceDecision | null {
  const o = exact(value, SALES_INVOICE_DECISION_FIELDS)
  if (
    o === null || !isNullableText(o.submittedBy) || !isNullableInstant(o.submittedAtUtc) || !isNullableText(o.decidedBy)
    || !isNullableInstant(o.decidedAtUtc) || !isNullableReason(o.decisionReason) || !isNullableText(o.cancelledBy)
    || !isNullableInstant(o.cancelledAtUtc) || !isNullableReason(o.cancellationReason)
  ) return null
  return {
    submittedBy: o.submittedBy, submittedAtUtc: o.submittedAtUtc, decidedBy: o.decidedBy, decidedAtUtc: o.decidedAtUtc,
    decisionReason: o.decisionReason, cancelledBy: o.cancelledBy, cancelledAtUtc: o.cancelledAtUtc,
    cancellationReason: o.cancellationReason,
  }
}

/** The recorded match of the official e-invoice (SAT-D14): the server's amounts and its tolerance, shown, never judged. */
export interface InvoiceMatch {
  readonly officialInvoiceNo: string
  readonly ettn: string
  readonly invoiceAmount: string
  readonly officialAmount: string
  readonly difference: string
  readonly tolerance: string
  readonly matchedBy: string
  readonly matchedAtUtc: string
  readonly differenceApprovedBy: string | null
  readonly differenceReason: string | null
}
const MATCH_FIELDS = [
  'officialInvoiceNo', 'ettn', 'invoiceAmount', 'officialAmount', 'difference', 'tolerance', 'matchedBy', 'matchedAtUtc',
  'differenceApprovedBy', 'differenceReason',
] as const

function decodeMatch(value: unknown): InvoiceMatch | null {
  const o = exact(value, MATCH_FIELDS)
  if (
    o === null || !isText(o.officialInvoiceNo) || !isText(o.ettn) || !isAmount(o.invoiceAmount) || !isAmount(o.officialAmount)
    || !isSignedAmount(o.difference) || !isAmount(o.tolerance) || !isText(o.matchedBy) || !isInstant(o.matchedAtUtc)
    || !isNullableText(o.differenceApprovedBy) || !isNullableText(o.differenceReason)
  ) return null
  return {
    officialInvoiceNo: o.officialInvoiceNo, ettn: o.ettn, invoiceAmount: o.invoiceAmount, officialAmount: o.officialAmount,
    difference: o.difference, tolerance: o.tolerance, matchedBy: o.matchedBy, matchedAtUtc: o.matchedAtUtc,
    differenceApprovedBy: o.differenceApprovedBy, differenceReason: o.differenceReason,
  }
}

/** The finalized instalment: the open item Finance holds (or will hold) for it, with the server's amount text. */
export interface FinalInstalment {
  readonly sequenceNo: number
  readonly dueDate: string
  readonly amount: string
  readonly functionalAmount: string | null
  readonly openItemId: string
  readonly installmentId: string
}
const FINAL_INSTALMENT_FIELDS = ['sequenceNo', 'dueDate', 'amount', 'functionalAmount', 'openItemId', 'installmentId'] as const

function decodeFinalInstalment(value: unknown): FinalInstalment | null {
  const o = exact(value, FINAL_INSTALMENT_FIELDS)
  if (
    o === null || !isPositiveCount(o.sequenceNo) || !isDateOnly(o.dueDate) || !isAmount(o.amount)
    || !(o.functionalAmount === null || isAmount(o.functionalAmount)) || !isReferenceId(o.openItemId) || !isReferenceId(o.installmentId)
  ) return null
  return {
    sequenceNo: o.sequenceNo, dueDate: o.dueDate, amount: o.amount, functionalAmount: o.functionalAmount,
    openItemId: o.openItemId, installmentId: o.installmentId,
  }
}

export interface InvoiceFinalization {
  readonly admissionId: string
  readonly finalizedVersion: string
  readonly finalizedBy: string
  readonly finalizedAtUtc: string
  readonly financeStatus: FinanceStatus
  readonly admittedAtUtc: string | null
  readonly attemptCount: number
  readonly lastAttemptAtUtc: string | null
  /** The raw ledger text (shown as is) and its parsed form. */
  readonly lastOutcome: string | null
  readonly lastOutcomeParsed: LastOutcome | null
  readonly accountingStatus: AccountingStatus
  readonly instalments: readonly FinalInstalment[]
}
const FINALIZATION_FIELDS = [
  'admissionId', 'finalizedVersion', 'finalizedBy', 'finalizedAtUtc', 'financeStatus', 'admittedAtUtc', 'attemptCount',
  'lastAttemptAtUtc', 'lastOutcome', 'accountingStatus', 'instalments',
] as const

function decodeFinalization(value: unknown): InvoiceFinalization | null {
  const o = exact(value, FINALIZATION_FIELDS)
  if (
    o === null || !isReferenceId(o.admissionId) || !isVersionString(o.finalizedVersion) || !isText(o.finalizedBy)
    || !isInstant(o.finalizedAtUtc) || !isFinanceStatus(o.financeStatus) || !isNullableInstant(o.admittedAtUtc)
    || !isCount(o.attemptCount) || !isNullableInstant(o.lastAttemptAtUtc) || !(o.lastOutcome === null || typeof o.lastOutcome === 'string')
    || !isAccountingStatus(o.accountingStatus)
  ) return null
  const parsed = o.lastOutcome === null ? null : parseLastOutcome(o.lastOutcome)
  if (o.lastOutcome !== null && parsed === null) return null
  // The ledger's own invariants: an attempt has a time and an outcome; admitted ⇔ an admission time.
  if ((o.attemptCount === 0) !== (o.lastAttemptAtUtc === null)) return null
  if ((o.attemptCount === 0) !== (o.lastOutcome === null)) return null
  if ((o.financeStatus === 'admitted') !== (o.admittedAtUtc !== null)) return null
  const instalments = list(o.instalments, decodeFinalInstalment)
  if (instalments === null) return null
  return {
    admissionId: o.admissionId, finalizedVersion: o.finalizedVersion, finalizedBy: o.finalizedBy, finalizedAtUtc: o.finalizedAtUtc,
    financeStatus: o.financeStatus, admittedAtUtc: o.admittedAtUtc, attemptCount: o.attemptCount, lastAttemptAtUtc: o.lastAttemptAtUtc,
    lastOutcome: o.lastOutcome, lastOutcomeParsed: parsed, accountingStatus: o.accountingStatus, instalments,
  }
}

export interface SalesInvoiceDetail {
  readonly id: string
  readonly documentNo: string
  readonly state: InvoiceState
  readonly version: string
  readonly businessDate: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly amountScale: number
  readonly exchangeRate: InvoiceRate | null
  readonly totals: InvoiceAmounts
  readonly functionalTotals: InvoiceAmounts | null
  readonly vatGroups: readonly InvoiceVatGroup[]
  readonly instalments: readonly InvoiceInstalment[]
  readonly lines: readonly InvoiceLine[]
  readonly createdBy: string
  readonly createdAtUtc: string
  /** API-33's approval record; `null` until the draft is first sent to approval or cancelled. */
  readonly decision: InvoiceDecision | null
  readonly paymentAllocationWarning: string | null
  readonly isReplay: boolean
  readonly match: InvoiceMatch | null
  readonly finalization: InvoiceFinalization | null
}
export const SALES_INVOICE_DETAIL_FIELDS = [
  'id', 'documentNo', 'state', 'version', 'businessDate', 'partyId', 'companyAccountId', 'currencyId', 'amountScale',
  'exchangeRate', 'totals', 'functionalTotals', 'vatGroups', 'instalments', 'lines', 'createdBy', 'createdAtUtc', 'decision',
  'paymentAllocationWarning', 'isReplay', 'match', 'finalization',
] as const
export const SALES_INVOICE_FINALIZATION_FIELDS = FINALIZATION_FIELDS
export const SALES_INVOICE_FINAL_INSTALMENT_FIELDS = FINAL_INSTALMENT_FIELDS
export const SALES_INVOICE_MATCH_FIELDS = MATCH_FIELDS

/** The detail of exactly `invoiceId`; anything else (another id, a broken invariant, a stray member) is `null`. */
export function decodeSalesInvoiceDetail(value: unknown, invoiceId: string): SalesInvoiceDetail | null {
  const o = exact(value, SALES_INVOICE_DETAIL_FIELDS)
  if (
    o === null || !isReferenceId(o.id) || o.id.toLowerCase() !== invoiceId.toLowerCase() || !isText(o.documentNo)
    || !isInvoiceState(o.state) || !isVersionString(o.version) || !isDateOnly(o.businessDate) || !isReferenceId(o.partyId)
    || !isReferenceId(o.companyAccountId) || !isReferenceId(o.currencyId) || !isCount(o.amountScale) || !isText(o.createdBy)
    || !isInstant(o.createdAtUtc) || !isNullableText(o.paymentAllocationWarning) || typeof o.isReplay !== 'boolean'
  ) return null
  const exchangeRate = o.exchangeRate === null ? null : decodeRate(o.exchangeRate)
  const totals = decodeAmounts(o.totals)
  const functionalTotals = decodeNullableAmounts(o.functionalTotals)
  const vatGroups = list(o.vatGroups, decodeVatGroup)
  const instalments = list(o.instalments, decodeInstalment)
  const lines = list(o.lines, decodeLine)
  const match = o.match === null ? null : decodeMatch(o.match)
  const finalization = o.finalization === null ? null : decodeFinalization(o.finalization)
  const decision = o.decision === null ? null : decodeDecision(o.decision)
  if (
    (o.exchangeRate !== null && exchangeRate === null) || totals === null || functionalTotals === undefined || vatGroups === null
    || instalments === null || lines === null || (o.decision !== null && decision === null)
    || (o.match !== null && match === null) || (o.finalization !== null && finalization === null)
  ) return null
  // A finalization exists exactly on a finalized invoice.
  if ((o.state === 'Finalized') !== (finalization !== null)) return null
  return {
    id: o.id, documentNo: o.documentNo, state: o.state, version: o.version, businessDate: o.businessDate, partyId: o.partyId,
    companyAccountId: o.companyAccountId, currencyId: o.currencyId, amountScale: o.amountScale, exchangeRate, totals,
    functionalTotals, vatGroups, instalments, lines, createdBy: o.createdBy, createdAtUtc: o.createdAtUtc, decision,
    paymentAllocationWarning: o.paymentAllocationWarning, isReplay: o.isReplay, match, finalization,
  }
}

/* ── The order's invoices (API-09) ────────────────────────────────────────── */

export interface DraftInvoiceItem {
  readonly id: string
  readonly documentNo: string
  readonly state: InvoiceState
  readonly businessDate: string
  readonly orderLineCount: number
  readonly invoiceLineCount: number
  readonly holdsShares: boolean
  readonly createdAtUtc: string
}
export interface DraftInvoices {
  readonly items: readonly DraftInvoiceItem[]
  readonly hasMore: boolean
}
export const SALES_DRAFT_INVOICES_FIELDS = ['items', 'hasMore'] as const
export const SALES_DRAFT_INVOICE_ITEM_FIELDS = [
  'id', 'documentNo', 'state', 'businessDate', 'orderLineCount', 'invoiceLineCount', 'holdsShares', 'createdAtUtc',
] as const

function decodeDraftInvoiceItem(value: unknown): DraftInvoiceItem | null {
  const o = exact(value, SALES_DRAFT_INVOICE_ITEM_FIELDS)
  if (
    o === null || !isReferenceId(o.id) || !isText(o.documentNo) || !isInvoiceState(o.state) || !isDateOnly(o.businessDate)
    || !isCount(o.orderLineCount) || !isCount(o.invoiceLineCount) || typeof o.holdsShares !== 'boolean' || !isInstant(o.createdAtUtc)
  ) return null
  return {
    id: o.id, documentNo: o.documentNo, state: o.state, businessDate: o.businessDate, orderLineCount: o.orderLineCount,
    invoiceLineCount: o.invoiceLineCount, holdsShares: o.holdsShares, createdAtUtc: o.createdAtUtc,
  }
}

export function decodeDraftInvoices(value: unknown): DraftInvoices | null {
  const o = exact(value, SALES_DRAFT_INVOICES_FIELDS)
  if (o === null || typeof o.hasMore !== 'boolean') return null
  const items = list(o.items, decodeDraftInvoiceItem)
  if (items === null) return null
  // One invoice is listed once.
  if (new Set(items.map((item) => item.id.toLowerCase())).size !== items.length) return null
  return { items, hasMore: o.hasMore }
}

/* ── Commands ─────────────────────────────────────────────────────────────── */

/** `SalesDraftStateCommandRequest` read with the version only: any other member is a 400. */
export interface InvoiceFinalizationRequest {
  readonly expectedVersion: string
}
export const INVOICE_FINALIZATION_KIND = { finalize: 'finalize', resend: 'resend' } as const
export type InvoiceFinalizationKind = (typeof INVOICE_FINALIZATION_KIND)[keyof typeof INVOICE_FINALIZATION_KIND]

export const invoiceFinalizationCommandPath = (kind: InvoiceFinalizationKind, invoiceId: string) =>
  kind === INVOICE_FINALIZATION_KIND.finalize ? salesInvoiceFinalizationPath(invoiceId) : salesInvoiceFinanceAdmissionPath(invoiceId)

/** Where Finance stands with a finalized invoice, read from the server's ledger facts only. */
export const FINANCE_STAGE = {
  pending: 'pending',
  sentWaiting: 'sent-waiting',
  accepted: 'accepted',
  refused: 'refused',
} as const
export type FinanceStage = (typeof FINANCE_STAGE)[keyof typeof FINANCE_STAGE]

export function financeStage(finalization: InvoiceFinalization): FinanceStage {
  if (finalization.financeStatus === 'admitted') return FINANCE_STAGE.accepted
  if (finalization.attemptCount === 0 || finalization.lastOutcomeParsed === null) return FINANCE_STAGE.pending
  if (finalization.lastOutcomeParsed.category === 'refused') return FINANCE_STAGE.refused
  return FINANCE_STAGE.sentWaiting
}
