/**
 * UI-70b S-10 "Müşteri iade faturaları" — the wire shapes of API-40 (SAT-D288…SAT-D294), pinned to
 * `contracts/openapi/goymen-erp-v1.json`:
 *
 *   GET  /api/v1/sales/customer-returns[?invoiceId]       → SalesCustomerReturnListResponse
 *   GET  /api/v1/sales/customer-returns/{id}              → SalesCustomerReturnView
 *   POST /api/v1/sales/customer-returns                   → 201 SalesCustomerReturnView (Draft)
 *   POST /api/v1/sales/customer-returns/{id}/submission   → 200 SalesCustomerReturnView (PendingApproval)
 *   POST /api/v1/sales/customer-returns/{id}/approval     → 200 SalesCustomerReturnView (Approved)
 *   POST /api/v1/sales/customer-returns/{id}/finalization → 200 SalesCustomerReturnView (Finalized)
 *   POST /api/v1/sales/customer-returns/{id}/rejection    → 200 SalesCustomerReturnView (Rejected; SAT-D290 (2, 3))
 *   POST /api/v1/sales/customer-returns/{id}/cancellation → 200 SalesCustomerReturnView (Cancelled; SAT-D290 (2))
 *
 * Sales RECORDS the customer's return invoice against a finalised sales invoice; the ERP never issues it (SAT-D288). Every
 * amount is the server's TEXT (the deterministic backend calculator's answer; the screen never computes, proportions or rounds
 * an amount — SAT-D241 / SAT-D292). The view carries no `isReplay` member: a replay is said only by the headers.
 *
 * Strict and fail-closed: every object carries exactly the declared members, and anything outside these shapes makes the whole
 * answer `null` ("Beklenmeyen yanıt") — never a partial list or a partial detail.
 */
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

export const SALES_CUSTOMER_RETURNS_PATH = '/sales/customer-returns'
export const salesCustomerReturnApiPath = (id: string) => `${SALES_CUSTOMER_RETURNS_PATH}/${encodeURIComponent(id)}`

export function salesCustomerReturnsListPath(invoiceId: string | null): string {
  if (invoiceId === null) return SALES_CUSTOMER_RETURNS_PATH
  return `${SALES_CUSTOMER_RETURNS_PATH}?${new URLSearchParams({ invoiceId }).toString()}`
}

/** `SalesCustomerReturn.MaximumCustomerDocumentNoLength`: the customer's own document number. */
export const CUSTOMER_RETURN_DOCUMENT_NO_MAX = 64
/** `SalesCustomerReturn.MaximumLines` (the order draft's own bound). */
export const CUSTOMER_RETURN_MAX_LINES = 500

/**
 * `SalesCustomerReturn.MaximumReasonLength` (= `SalesInvoice.MaximumReasonLength`): the optional rejection or cancellation
 * reason, measured by the server on the trimmed text.
 */
export const CUSTOMER_RETURN_REASON_MAX = 4000

export const CUSTOMER_RETURN_STATES = ['Draft', 'PendingApproval', 'Approved', 'Finalized', 'Rejected', 'Cancelled'] as const
export type CustomerReturnState = (typeof CUSTOMER_RETURN_STATES)[number]

export interface CustomerReturnAmounts {
  readonly netAmount: string
  readonly vatAmount: string
  readonly withheldVatAmount: string
  readonly lineTotalAmount: string
  readonly derivedPayableAmount: string
  readonly driftAmount: string
}

export interface CustomerReturnSummary {
  readonly id: string
  readonly state: CustomerReturnState
  readonly version: string
  readonly originalInvoiceId: string
  readonly originalDocumentNo: string
  readonly customerDocumentNo: string
  readonly customerDocumentDate: string
  readonly partyId: string
  readonly currencyId: string
  readonly lineTotalAmount: string
  readonly isShipmentLinked: boolean
  readonly needsReview: boolean
  readonly lineCount: number
  readonly createdAtUtc: string
}

export interface CustomerReturnLine {
  readonly lineNo: number
  readonly originalInvoiceLineId: string
  readonly originalLineNo: number
  readonly shipmentId: string
  readonly productId: string
  readonly unitId: string
  readonly quantity: string
  readonly netUnitPrice: string
  readonly vatPercent: string
  readonly withholdingShare: string | null
  readonly isLastReturn: boolean
  readonly needsReview: boolean
  readonly amounts: CustomerReturnAmounts
  readonly functionalAmounts: CustomerReturnAmounts | null
}

export interface CustomerReturnView {
  readonly id: string
  readonly state: CustomerReturnState
  readonly version: string
  readonly originalInvoiceId: string
  readonly originalDocumentNo: string
  readonly shipmentId: string | null
  readonly customerDocumentNo: string
  readonly customerDocumentDate: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly amountScale: number
  readonly rate: string | null
  readonly functionalCurrencyId: string | null
  readonly functionalScale: number | null
  readonly totals: CustomerReturnAmounts
  readonly functionalTotals: CustomerReturnAmounts | null
  readonly needsReview: boolean
  readonly createdBy: string
  readonly createdAtUtc: string
  readonly submittedBy: string | null
  readonly submittedAtUtc: string | null
  readonly approvedBy: string | null
  readonly approvedAtUtc: string | null
  readonly finalizedBy: string | null
  readonly finalizedAtUtc: string | null
  readonly rejectedBy: string | null
  readonly rejectedAtUtc: string | null
  readonly rejectionReason: string | null
  readonly cancelledBy: string | null
  readonly cancelledAtUtc: string | null
  readonly cancellationReason: string | null
  readonly updatedAtUtc: string
  readonly lines: readonly CustomerReturnLine[]
}

type Obj = Record<string, unknown>

function exact(value: unknown, fields: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const object = value as Obj
  const keys = Object.keys(object)
  if (keys.length !== fields.length) return null
  return fields.every((field) => Object.hasOwn(object, field)) ? object : null
}

const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const AMOUNT_RE = /^\d+(?:\.\d+)?$/
/** A drift (SAT-D241) may be negative: payable − (net + VAT − withheld) is signed. */
const SIGNED_AMOUNT_RE = /^-?\d+(?:\.\d+)?$/
const TEXT_MAX = 512

const isInstant = (v: unknown): v is string =>
  typeof v === 'string' && v.length <= 64 && INSTANT_RE.test(v) && !Number.isNaN(Date.parse(v))
const isAmount = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && AMOUNT_RE.test(v)
const isSignedAmount = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && SIGNED_AMOUNT_RE.test(v)
const isText = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= TEXT_MAX
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0
const isPositiveCount = (v: unknown): v is number => isCount(v) && v > 0
const isState = (v: unknown): v is CustomerReturnState =>
  typeof v === 'string' && (CUSTOMER_RETURN_STATES as readonly string[]).includes(v)
const isNullableText = (v: unknown): v is string | null => v === null || isText(v)
const isNullableInstant = (v: unknown): v is string | null => v === null || isInstant(v)
/** The server stores a reason trimmed, never blank, within its bound; whitespace-only is stored as none (null). */
const isNullableReason = (v: unknown): v is string | null =>
  v === null || (typeof v === 'string' && v.length > 0 && v.length <= CUSTOMER_RETURN_REASON_MAX && v.trim() === v)

const AMOUNT_FIELDS = [
  'netAmount',
  'vatAmount',
  'withheldVatAmount',
  'lineTotalAmount',
  'derivedPayableAmount',
  'driftAmount',
] as const

function decodeAmounts(value: unknown): CustomerReturnAmounts | null {
  const o = exact(value, AMOUNT_FIELDS)
  if (
    o === null
    || !isAmount(o.netAmount)
    || !isAmount(o.vatAmount)
    || !isAmount(o.withheldVatAmount)
    || !isAmount(o.lineTotalAmount)
    || !isAmount(o.derivedPayableAmount)
    || !isSignedAmount(o.driftAmount)
  ) {
    return null
  }
  return {
    netAmount: o.netAmount,
    vatAmount: o.vatAmount,
    withheldVatAmount: o.withheldVatAmount,
    lineTotalAmount: o.lineTotalAmount,
    derivedPayableAmount: o.derivedPayableAmount,
    driftAmount: o.driftAmount,
  }
}

function decodeNullableAmounts(value: unknown): CustomerReturnAmounts | null | undefined {
  if (value === null) return null
  return decodeAmounts(value) ?? undefined
}

export const CUSTOMER_RETURN_SUMMARY_FIELDS = [
  'id',
  'state',
  'version',
  'originalInvoiceId',
  'originalDocumentNo',
  'customerDocumentNo',
  'customerDocumentDate',
  'partyId',
  'currencyId',
  'lineTotalAmount',
  'isShipmentLinked',
  'needsReview',
  'lineCount',
  'createdAtUtc',
] as const

function decodeSummary(value: unknown): CustomerReturnSummary | null {
  const o = exact(value, CUSTOMER_RETURN_SUMMARY_FIELDS)
  if (
    o === null
    || !isReferenceId(o.id)
    || !isState(o.state)
    || !isVersionString(o.version)
    || !isReferenceId(o.originalInvoiceId)
    || !isText(o.originalDocumentNo)
    || !isText(o.customerDocumentNo)
    || !isDateOnly(o.customerDocumentDate)
    || !isReferenceId(o.partyId)
    || !isReferenceId(o.currencyId)
    || !isAmount(o.lineTotalAmount)
    || typeof o.isShipmentLinked !== 'boolean'
    || typeof o.needsReview !== 'boolean'
    || !isPositiveCount(o.lineCount)
    || !isInstant(o.createdAtUtc)
  ) {
    return null
  }
  return {
    id: o.id,
    state: o.state,
    version: o.version,
    originalInvoiceId: o.originalInvoiceId,
    originalDocumentNo: o.originalDocumentNo,
    customerDocumentNo: o.customerDocumentNo,
    customerDocumentDate: o.customerDocumentDate,
    partyId: o.partyId,
    currencyId: o.currencyId,
    lineTotalAmount: o.lineTotalAmount,
    isShipmentLinked: o.isShipmentLinked,
    needsReview: o.needsReview,
    lineCount: o.lineCount,
    createdAtUtc: o.createdAtUtc,
  }
}

/**
 * The list as the server wrote it; a repeated id is not this contract. With `invoiceId`, every row must belong to that
 * invoice — a row of another invoice under a filtered read fails the whole answer.
 */
export function decodeCustomerReturnList(value: unknown, invoiceId: string | null): readonly CustomerReturnSummary[] | null {
  const o = exact(value, ['items'])
  if (o === null || !Array.isArray(o.items)) return null
  const items: CustomerReturnSummary[] = []
  const seen = new Set<string>()
  for (const entry of o.items) {
    const item = decodeSummary(entry)
    if (item === null || seen.has(item.id.toLowerCase())) return null
    if (invoiceId !== null && item.originalInvoiceId.toLowerCase() !== invoiceId.toLowerCase()) return null
    seen.add(item.id.toLowerCase())
    items.push(item)
  }
  return items
}

export const CUSTOMER_RETURN_LINE_FIELDS = [
  'lineNo',
  'originalInvoiceLineId',
  'originalLineNo',
  'shipmentId',
  'productId',
  'unitId',
  'quantity',
  'netUnitPrice',
  'vatPercent',
  'withholdingShare',
  'isLastReturn',
  'needsReview',
  'amounts',
  'functionalAmounts',
] as const

function decodeLine(value: unknown, functional: boolean): CustomerReturnLine | null {
  const o = exact(value, CUSTOMER_RETURN_LINE_FIELDS)
  if (
    o === null
    || !isPositiveCount(o.lineNo)
    || !isReferenceId(o.originalInvoiceLineId)
    || !isPositiveCount(o.originalLineNo)
    || !isReferenceId(o.shipmentId)
    || !isReferenceId(o.productId)
    || !isReferenceId(o.unitId)
    || !isAmount(o.quantity)
    || !isAmount(o.netUnitPrice)
    || !isAmount(o.vatPercent)
    || !(o.withholdingShare === null || isAmount(o.withholdingShare))
    || typeof o.isLastReturn !== 'boolean'
    || typeof o.needsReview !== 'boolean'
  ) {
    return null
  }
  const amounts = decodeAmounts(o.amounts)
  const functionalAmounts = decodeNullableAmounts(o.functionalAmounts)
  // A foreign-currency return carries TL amounts on every line; a TL return on none.
  if (amounts === null || functionalAmounts === undefined || (functionalAmounts !== null) !== functional) return null
  return {
    lineNo: o.lineNo,
    originalInvoiceLineId: o.originalInvoiceLineId,
    originalLineNo: o.originalLineNo,
    shipmentId: o.shipmentId,
    productId: o.productId,
    unitId: o.unitId,
    quantity: o.quantity,
    netUnitPrice: o.netUnitPrice,
    vatPercent: o.vatPercent,
    withholdingShare: o.withholdingShare,
    isLastReturn: o.isLastReturn,
    needsReview: o.needsReview,
    amounts,
    functionalAmounts,
  }
}

export const CUSTOMER_RETURN_VIEW_FIELDS = [
  'id',
  'state',
  'version',
  'originalInvoiceId',
  'originalDocumentNo',
  'shipmentId',
  'customerDocumentNo',
  'customerDocumentDate',
  'partyId',
  'companyAccountId',
  'currencyId',
  'amountScale',
  'rate',
  'functionalCurrencyId',
  'functionalScale',
  'totals',
  'functionalTotals',
  'needsReview',
  'createdBy',
  'createdAtUtc',
  'submittedBy',
  'submittedAtUtc',
  'approvedBy',
  'approvedAtUtc',
  'finalizedBy',
  'finalizedAtUtc',
  'rejectedBy',
  'rejectedAtUtc',
  'rejectionReason',
  'cancelledBy',
  'cancelledAtUtc',
  'cancellationReason',
  'updatedAtUtc',
  'lines',
] as const

/** Each state carries exactly the actor facts it has passed through; anything else is not this contract. */
function factsAgreeWithState(o: Obj, state: CustomerReturnState): boolean {
  const submitted = o.submittedBy !== null && o.submittedAtUtc !== null
  // SAT-D11: the approver is neither the preparer nor the submitter (mirrors the domain restore check).
  const approved = o.approvedBy !== null && o.approvedAtUtc !== null
    && o.approvedBy !== o.createdBy && o.approvedBy !== o.submittedBy
  const finalized = o.finalizedBy !== null && o.finalizedAtUtc !== null
  // SAT-D290 (3): the rejecter, like the approver, is neither the preparer nor the submitter.
  const rejected = o.rejectedBy !== null && o.rejectedAtUtc !== null
    && o.rejectedBy !== o.createdBy && o.rejectedBy !== o.submittedBy
  const cancelled = o.cancelledBy !== null && o.cancelledAtUtc !== null
  const none = (by: unknown, at: unknown) => by === null && at === null
  const notRejected = none(o.rejectedBy, o.rejectedAtUtc) && o.rejectionReason === null
  const notCancelled = none(o.cancelledBy, o.cancelledAtUtc) && o.cancellationReason === null
  const open = notRejected && notCancelled
  const notApprovedNorFinalized = none(o.approvedBy, o.approvedAtUtc) && none(o.finalizedBy, o.finalizedAtUtc)
  switch (state) {
    case 'Draft':
      return none(o.submittedBy, o.submittedAtUtc) && notApprovedNorFinalized && open
    case 'PendingApproval':
      return submitted && notApprovedNorFinalized && open
    case 'Approved':
      return submitted && approved && none(o.finalizedBy, o.finalizedAtUtc) && open
    case 'Finalized':
      return submitted && approved && finalized && open
    case 'Rejected':
      return submitted && notApprovedNorFinalized && rejected && notCancelled
    case 'Cancelled':
      // Cancelled before approval: from Draft (not submitted) or from PendingApproval (submitted, both facts or neither).
      return (submitted || none(o.submittedBy, o.submittedAtUtc)) && notApprovedNorFinalized && notRejected && cancelled
    default:
      return false
  }
}

/** The detail of exactly `returnId`; anything else (another id, a broken invariant, a stray member) is `null`. */
export function decodeCustomerReturnView(value: unknown, returnId: string): CustomerReturnView | null {
  const o = exact(value, CUSTOMER_RETURN_VIEW_FIELDS)
  if (
    o === null
    || !isReferenceId(o.id)
    || o.id.toLowerCase() !== returnId.toLowerCase()
    || !isState(o.state)
    || !isVersionString(o.version)
    || !isReferenceId(o.originalInvoiceId)
    || !isText(o.originalDocumentNo)
    || !(o.shipmentId === null || isReferenceId(o.shipmentId))
    || !isText(o.customerDocumentNo)
    || !isDateOnly(o.customerDocumentDate)
    || !isReferenceId(o.partyId)
    || !isReferenceId(o.companyAccountId)
    || !isReferenceId(o.currencyId)
    || !isCount(o.amountScale)
    || typeof o.needsReview !== 'boolean'
    || !isText(o.createdBy)
    || !isInstant(o.createdAtUtc)
    || !isNullableText(o.submittedBy)
    || !isNullableInstant(o.submittedAtUtc)
    || !isNullableText(o.approvedBy)
    || !isNullableInstant(o.approvedAtUtc)
    || !isNullableText(o.finalizedBy)
    || !isNullableInstant(o.finalizedAtUtc)
    || !isNullableText(o.rejectedBy)
    || !isNullableInstant(o.rejectedAtUtc)
    || !isNullableReason(o.rejectionReason)
    || !isNullableText(o.cancelledBy)
    || !isNullableInstant(o.cancelledAtUtc)
    || !isNullableReason(o.cancellationReason)
    || !isInstant(o.updatedAtUtc)
    || !factsAgreeWithState(o, o.state)
    || !Array.isArray(o.lines)
    || o.lines.length === 0
    || o.lines.length > CUSTOMER_RETURN_MAX_LINES
  ) {
    return null
  }
  // The TL side is all or nothing: a rate, a TL currency, its scale and TL totals together, or none of them.
  const functional = o.rate !== null
  if (functional) {
    if (!isAmount(o.rate) || !isReferenceId(o.functionalCurrencyId) || !isCount(o.functionalScale)) return null
  } else if (o.functionalCurrencyId !== null || o.functionalScale !== null) {
    return null
  }
  const totals = decodeAmounts(o.totals)
  const functionalTotals = decodeNullableAmounts(o.functionalTotals)
  if (totals === null || functionalTotals === undefined || (functionalTotals !== null) !== functional) return null
  const lines: CustomerReturnLine[] = []
  const seenLineNo = new Set<number>()
  for (const entry of o.lines) {
    const line = decodeLine(entry, functional)
    if (line === null || seenLineNo.has(line.lineNo)) return null
    // A shipment-linked return holds lines of that one shipment only (SAT-D289).
    if (o.shipmentId !== null && line.shipmentId.toLowerCase() !== (o.shipmentId as string).toLowerCase()) return null
    seenLineNo.add(line.lineNo)
    lines.push(line)
  }
  return {
    id: o.id,
    state: o.state,
    version: o.version,
    originalInvoiceId: o.originalInvoiceId,
    originalDocumentNo: o.originalDocumentNo,
    shipmentId: o.shipmentId as string | null,
    customerDocumentNo: o.customerDocumentNo,
    customerDocumentDate: o.customerDocumentDate,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    currencyId: o.currencyId,
    amountScale: o.amountScale,
    rate: o.rate as string | null,
    functionalCurrencyId: o.functionalCurrencyId as string | null,
    functionalScale: o.functionalScale as number | null,
    totals,
    functionalTotals,
    needsReview: o.needsReview,
    createdBy: o.createdBy,
    createdAtUtc: o.createdAtUtc,
    submittedBy: o.submittedBy as string | null,
    submittedAtUtc: o.submittedAtUtc as string | null,
    approvedBy: o.approvedBy as string | null,
    approvedAtUtc: o.approvedAtUtc as string | null,
    finalizedBy: o.finalizedBy as string | null,
    finalizedAtUtc: o.finalizedAtUtc as string | null,
    rejectedBy: o.rejectedBy as string | null,
    rejectedAtUtc: o.rejectedAtUtc as string | null,
    rejectionReason: o.rejectionReason as string | null,
    cancelledBy: o.cancelledBy as string | null,
    cancelledAtUtc: o.cancelledAtUtc as string | null,
    cancellationReason: o.cancellationReason as string | null,
    updatedAtUtc: o.updatedAtUtc,
    lines,
  }
}
