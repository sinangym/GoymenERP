/**
 * The Sales order draft contract — the only file that spells a Sales wire name.
 *
 * STATUS: `PINNED_SOURCE`, not live. Pinned to Sales Backend draft #257 @ 2c1239e
 * (`src/backend/Modules/Sales/CONTRACT_V1.md`, `SalesDraftContractsV1.cs`, and the 13
 * JSON examples copied verbatim into ./examples), on Business #254 @ 6f0cd10. The
 * Backend says it plainly: "local DTO/decoder/example preparation, not an implemented
 * HTTP API". No route is mapped, no permission exists, nothing here proves a server.
 *
 * Rules taken from the source rather than assumed:
 *  - request optionality does not make a response member optional: every response
 *    member must be PRESENT, nullable ones as explicit null; unknown members, wrong
 *    case and numbers standing in for decimal/version strings are refused;
 *  - decimal and version text is kept verbatim, never parsed into a JS number;
 *  - `version` (concurrency) and `revision` (commercial, `Rev.0`) are separate facts;
 *  - consumers must check non-empty server ids, positive version text, kind, state,
 *    revision and line identities before treating a response as success.
 *
 * A decoder returns null for anything it cannot vouch for; the caller fails closed.
 */

import { unplannedQuantity } from './deliveryPlan'
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

export const SALES_CONTRACT_STATUS = 'PINNED_SOURCE' as const

export const SALES_DRAFT_PATH = '/sales/drafts'
export const salesDraftDetailPath = (id: string) => `${SALES_DRAFT_PATH}/${encodeURIComponent(id)}`

/**
 * `SalesDraftApiV1`: a first commit answers 201; the same accepted attempt answers 201 again with
 * `Idempotency-Replayed: true` (and `isReplay` in the body) — ADR-011 §4 replays the persisted
 * result, a delta on #257 which said 200 (decided on #321).
 */
export const CREATED_STATUS = 201
export const REPLAY_HEADER = 'Idempotency-Replayed'
export const REV_0 = 'Rev.0'
export const ORDER_KIND = 'Order' as const
export const DRAFT_STATE = 'Draft' as const

/**
 * SALES-A1: the INTERNAL APPROVAL axis (`InternalApprovalState`), a fourth fact beside the lifecycle
 * `state`, the commercial `revision` and the concurrency `version` — never merged into one status.
 * Preparing → PendingApproval → Approved | Rejected (→ PendingApproval again, same Rev.0).
 */
export const APPROVAL_STATE = {
  preparing: 'Preparing',
  pendingApproval: 'PendingApproval',
  approved: 'Approved',
  rejected: 'Rejected',
} as const
export type ApprovalState = (typeof APPROVAL_STATE)[keyof typeof APPROVAL_STATE]
const APPROVAL_STATES: readonly string[] = Object.values(APPROVAL_STATE)

/**
 * SALES-A1: the three state commands. Each answers 200 with the draft after the transition (the
 * command's result IS the detail view), and 200 + `Idempotency-Replayed` for the same key again.
 */
export const STATE_COMMAND_STATUS = 200
export type DraftStateCommand = 'submission' | 'approval' | 'rejection'
export const salesDraftCommandPath = (id: string, command: DraftStateCommand) => `${salesDraftDetailPath(id)}/${command}`

/** `SalesDraftStateCommandRequest`: the expected version as text; `reason` only on a rejection. */
export interface DraftStateCommandRequest {
  readonly expectedVersion: string
  readonly reason?: string
}

/** `SalesDraftApprovalV1`: who submitted / decided and when; null until it happened. Facts, never derived. */
export interface ApprovalFacts {
  readonly submittedBy: string | null
  readonly submittedAtUtc: string | null
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly rejectionReason: string | null
}

/** Technical bounds from `SalesDraftJson.ReadCreate`. Not owner scale or precision. */
export const LIMITS = {
  lines: 500,
  slices: 500,
  note: 4000,
  decimalText: 128,
  versionText: 128,
} as const

/* ── Request ──────────────────────────────────────────────────────────────── */

export interface OwnerReference {
  readonly id: string
  readonly expectedVersion: string
}

export interface DeliverySlice {
  /** Client-owned, minted once when the slice is added, preserved on retry. */
  readonly sliceId: string
  readonly orderUnitId: string
  readonly quantity: string
  readonly requestedDeliveryDate: string
}

export interface DraftLineRequest {
  /** Client-owned, minted once when the line is added, preserved on retry. */
  readonly lineId: string
  readonly productId: string
  readonly expectedProductVersion: string
  readonly orderUnitId: string
  readonly quantity: string
  /** A supplied price requires its own `priceUnitId`; null is unknown, `0` is not null. */
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
  /** null: unknown plan. []: no planned slices. Neither is a delivery. */
  readonly requestedDeliverySlices: readonly DeliverySlice[] | null
  /** null is unknown, never zero. */
  readonly discountPercent: string | null
}

/**
 * `CreateSalesOrderDraftRequest`. Every optional member is SENT, as null when unknown.
 * The source treats missing and null alike on create; sending both forms the same way
 * keeps the frozen snapshot — and so the idempotency comparison — one shape only.
 * No company, actor, version, state or approval: the server's scope owns those.
 */
export interface DraftCreateRequest {
  readonly kind: typeof ORDER_KIND
  readonly partyId: string
  readonly companyAccountId: string
  readonly businessDate: string
  readonly currencyId: string
  readonly lines: readonly DraftLineRequest[]
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
  readonly deliveryAddress: OwnerReference | null
  readonly contact: OwnerReference | null
  readonly responsible: OwnerReference | null
  readonly paymentTerms: OwnerReference | null
  readonly sourceStrategy: OwnerReference | null
  readonly package: OwnerReference | null
}

export const OWNER_REFERENCE_FIELDS = [
  'deliveryAddress',
  'contact',
  'responsible',
  'paymentTerms',
  'sourceStrategy',
  'package',
] as const

/* ── Responses ────────────────────────────────────────────────────────────── */

export interface DraftSummary {
  readonly id: string
  readonly companyId: string
  /** Opaque display text; its format is illustrative in the source. */
  readonly documentNo: string
  readonly kind: typeof ORDER_KIND
  readonly state: typeof DRAFT_STATE
  readonly version: string
  readonly revision: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly businessDate: string
  readonly createdAtUtc: string
  readonly lineCount: number
  /** SALES-A1: the internal approval axis. */
  readonly approvalState: ApprovalState
}

export interface DraftLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productVersion: string
  readonly productCode: string
  readonly productName: string
  readonly orderUnitId: string
  readonly quantity: string
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
  readonly requestedDeliverySlices: readonly DeliverySlice[] | null
  readonly discountPercent: string | null
  /**
   * K3 P2 (Sinan 5748258299): whether THIS caller may see the line's price, unit and discount. Never absent:
   * a null price cannot be told apart from a price of nothing and from a price the caller may not see, so
   * the server says which. `Withheld` ⇒ the three commercial members are null BECAUSE of it; `Visible` with
   * nulls ⇒ there is none. Decided by the narrow `sales.orders.price.read`; the draft read alone never discloses.
   */
  readonly priceDisclosure: PriceDisclosure
}

export type PriceDisclosure = 'Visible' | 'Withheld'
const PRICE_DISCLOSURES: readonly PriceDisclosure[] = ['Visible', 'Withheld']
const isPriceDisclosure = (v: unknown): v is PriceDisclosure => typeof v === 'string' && (PRICE_DISCLOSURES as readonly string[]).includes(v)

export interface DraftDetail {
  readonly draft: DraftSummary
  readonly requestedDeliveryDate: string | null
  readonly note: string | null
  readonly lines: readonly DraftLine[]
  readonly deliveryAddress: OwnerReference | null
  readonly contact: OwnerReference | null
  readonly responsible: OwnerReference | null
  readonly paymentTerms: OwnerReference | null
  readonly sourceStrategy: OwnerReference | null
  readonly package: OwnerReference | null
  /** SALES-A1: the approval facts; consistent with `draft.approvalState` or the detail is malformed. */
  readonly approval: ApprovalFacts
}

export interface DraftMutation {
  readonly detail: DraftDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

export interface DraftPage {
  readonly items: readonly DraftSummary[]
  /** null is the end of the collection. Opaque; the client never builds one. */
  readonly nextCursor: string | null
}

export interface SalesFieldError {
  readonly field: string
  readonly code: string
}

export interface SalesProblem {
  readonly status: number
  readonly title: string
  readonly code: string | null
  readonly correlationId: string | null
  readonly errors: readonly SalesFieldError[]
}

/** `SalesDraftFieldCodesV1.All`. */
export const SALES_PROBLEM_CODE = {
  validationFailed: 'validation_failed',
  companyMismatch: 'company_mismatch',
  passiveCompanyAccount: 'passive_company_account',
  unsupportedProduct: 'unsupported_product',
  unsupportedUnit: 'unsupported_unit',
  invalidQuantity: 'invalid_quantity',
  invalidDecimalText: 'invalid_decimal_text',
  sameKeyPayloadMismatch: 'same_key_payload_mismatch',
  settingsProviderUnavailable: 'settings_provider_unavailable',
  settingsAccessDenied: 'settings_access_denied',
  // The Sales API slice's own answers (#66 card, decisions 2–4; ADR-011 §3/§7/§8): each one is a
  // contract delta on #257, marked in the backend README. The API's 503s carry only the platform's
  // `service_unavailable`; provider codes never reach the wire. Unknown codes still decode; they
  // only lose their text.
  unsupportedCurrency: 'unsupported_currency',
  accountState: 'account_state',
  versionConflict: 'version_conflict',
  concurrentChange: 'concurrent_change',
  commercialNotAvailable: 'commercial_not_available',
  ownerReferenceUnavailable: 'owner_reference_unavailable',
  // SALES-A2a: a delivery address / contact the Cari owner does not list (or lists inactive) for the
  // draft's party account (422). A stale reference version is the terminal `version_conflict`.
  ownerReferenceInvalid: 'owner_reference_invalid',
  commandMetadataInvalid: 'command_metadata_invalid',
  invalidCursor: 'invalid_cursor',
  // SALES-A1 (contract deltas): the transition is not allowed from the draft's approval state
  // (409, terminal for the key), and a rejection reason over the technical bound (422).
  stateConflict: 'sales_state_conflict',
  approvalReasonTooLong: 'sales_approval_reason_too_long',
} as const

export type SalesProblemCode = (typeof SALES_PROBLEM_CODE)[keyof typeof SALES_PROBLEM_CODE]

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
  typeof v === 'string' &&
  v.length <= LIMITS.decimalText &&
  DECIMAL_RE.test(v) &&
  (!positive || /[1-9]/.test(v))
const isNullable = <T>(v: unknown, check: (x: unknown) => x is T): v is T | null => v === null || check(v)
const isText = (v: unknown, max: number): v is string => typeof v === 'string' && v.length > 0 && v.length <= max
const isNote = (v: unknown): v is string | null => v === null || (typeof v === 'string' && v.length <= LIMITS.note)
const isOwnerVersion = (v: unknown): v is string =>
  typeof v === 'string' && v.trim().length > 0 && v.length <= LIMITS.versionText
const isPositiveInt = (v: unknown, max: number): v is number =>
  typeof v === 'number' && Number.isInteger(v) && v >= 1 && v <= max

function decodeOwnerReference(value: unknown): OwnerReference | null | undefined {
  if (value === null) return null
  const o = exact(value, ['id', 'expectedVersion'])
  if (o === null || !isReferenceId(o.id) || !isOwnerVersion(o.expectedVersion)) return undefined
  return { id: o.id, expectedVersion: o.expectedVersion }
}

function decodeSlices(value: unknown): readonly DeliverySlice[] | null | undefined {
  if (value === null) return null
  if (!Array.isArray(value) || value.length > LIMITS.slices) return undefined
  const ids = new Set<string>()
  const slices: DeliverySlice[] = []
  for (const entry of value) {
    const o = exact(entry, ['sliceId', 'orderUnitId', 'quantity', 'requestedDeliveryDate'])
    if (
      o === null ||
      !isReferenceId(o.sliceId) ||
      ids.has(o.sliceId) ||
      !isReferenceId(o.orderUnitId) ||
      !isDecimal(o.quantity, true) ||
      !isDateOnly(o.requestedDeliveryDate)
    ) {
      return undefined
    }
    ids.add(o.sliceId)
    slices.push({
      sliceId: o.sliceId,
      orderUnitId: o.orderUnitId,
      quantity: o.quantity,
      requestedDeliveryDate: o.requestedDeliveryDate,
    })
  }
  return slices
}

const SUMMARY_KEYS = [
  'id', 'companyId', 'documentNo', 'kind', 'state', 'version', 'revision', 'partyId',
  'companyAccountId', 'currencyId', 'businessDate', 'createdAtUtc', 'lineCount', 'approvalState',
] as const

const isApprovalState = (v: unknown): v is ApprovalState => typeof v === 'string' && APPROVAL_STATES.includes(v)
const isInstant = (v: unknown): v is string => typeof v === 'string' && INSTANT_RE.test(v) && !Number.isNaN(Date.parse(v))
const isActor = (v: unknown): v is string => isText(v, 128)

/**
 * SALES-A1: the approval facts, read exactly and checked against the state the summary carries:
 * Preparing holds none; PendingApproval the submission only; Approved both, no reason; Rejected both.
 * A body that says "approved" without a decision is not shown as approved.
 */
function decodeApproval(value: unknown, state: ApprovalState): ApprovalFacts | null {
  const o = exact(value, ['submittedBy', 'submittedAtUtc', 'decidedBy', 'decidedAtUtc', 'rejectionReason'])
  if (
    o === null ||
    !isNullable(o.submittedBy, isActor) ||
    !isNullable(o.submittedAtUtc, isInstant) ||
    !isNullable(o.decidedBy, isActor) ||
    !isNullable(o.decidedAtUtc, isInstant) ||
    !isNullable(o.rejectionReason, (v): v is string => isText(v, LIMITS.note))
  ) {
    return null
  }
  const submitted = o.submittedBy !== null && o.submittedAtUtc !== null
  const decided = o.decidedBy !== null && o.decidedAtUtc !== null
  const noDecision = o.decidedBy === null && o.decidedAtUtc === null && o.rejectionReason === null
  const consistent =
    state === APPROVAL_STATE.preparing
      ? !submitted && o.submittedBy === null && o.submittedAtUtc === null && noDecision
      : state === APPROVAL_STATE.pendingApproval
        ? submitted && noDecision
        : state === APPROVAL_STATE.approved
          ? submitted && decided && o.rejectionReason === null
          : submitted && decided
  if (!consistent) return null
  return {
    submittedBy: o.submittedBy,
    submittedAtUtc: o.submittedAtUtc,
    decidedBy: o.decidedBy,
    decidedAtUtc: o.decidedAtUtc,
    rejectionReason: o.rejectionReason,
  }
}

export function decodeDraftSummary(value: unknown): DraftSummary | null {
  const o = exact(value, SUMMARY_KEYS)
  if (
    o === null ||
    !isReferenceId(o.id) ||
    !isReferenceId(o.companyId) ||
    !isText(o.documentNo, 200) ||
    // The first slice creates goods Orders in Draft only. Any other kind or state is a
    // meaning this build was not written for, and is not shown as if it were a Draft.
    o.kind !== ORDER_KIND ||
    o.state !== DRAFT_STATE ||
    !isVersionString(o.version) ||
    (o.version as string).length > LIMITS.versionText ||
    typeof o.revision !== 'string' ||
    !REVISION_RE.test(o.revision) ||
    !isReferenceId(o.partyId) ||
    !isReferenceId(o.companyAccountId) ||
    !isReferenceId(o.currencyId) ||
    !isDateOnly(o.businessDate) ||
    typeof o.createdAtUtc !== 'string' ||
    !INSTANT_RE.test(o.createdAtUtc) ||
    Number.isNaN(Date.parse(o.createdAtUtc)) ||
    !isPositiveInt(o.lineCount, LIMITS.lines) ||
    !isApprovalState(o.approvalState)
  ) {
    return null
  }
  return {
    id: o.id,
    companyId: o.companyId,
    documentNo: o.documentNo,
    kind: ORDER_KIND,
    state: DRAFT_STATE,
    version: o.version as string,
    revision: o.revision,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    currencyId: o.currencyId,
    businessDate: o.businessDate,
    createdAtUtc: o.createdAtUtc,
    lineCount: o.lineCount,
    approvalState: o.approvalState,
  }
}

const LINE_KEYS = [
  'lineId', 'lineNo', 'productId', 'productVersion', 'productCode', 'productName', 'orderUnitId',
  'quantity', 'unitPrice', 'priceUnitId', 'requestedDeliveryDate', 'note',
  'requestedDeliverySlices', 'discountPercent', 'priceDisclosure',
] as const

function decodeLine(value: unknown): DraftLine | null {
  const o = exact(value, LINE_KEYS)
  if (o === null) return null
  const slices = decodeSlices(o.requestedDeliverySlices)
  if (
    !isReferenceId(o.lineId) ||
    !isPositiveInt(o.lineNo, LIMITS.lines) ||
    !isReferenceId(o.productId) ||
    !isOwnerVersion(o.productVersion) ||
    !isText(o.productCode, 200) ||
    !isText(o.productName, 500) ||
    !isReferenceId(o.orderUnitId) ||
    !isDecimal(o.quantity, true) ||
    !isNullable(o.unitPrice, (v): v is string => isDecimal(v, false)) ||
    !isNullable(o.priceUnitId, isReferenceId) ||
    // The source pairs them: a price without its unit, or a unit without a price, is refused.
    (o.unitPrice === null) !== (o.priceUnitId === null) ||
    !isNullable(o.requestedDeliveryDate, isDateOnly) ||
    !isNote(o.note) ||
    slices === undefined ||
    !isNullable(o.discountPercent, (v): v is string => isDecimal(v, false)) ||
    !isPriceDisclosure(o.priceDisclosure) ||
    // Withheld means the texts were withheld: a withheld line carrying a price is not this contract.
    (o.priceDisclosure === 'Withheld' && (o.unitPrice !== null || o.priceUnitId !== null || o.discountPercent !== null))
  ) {
    return null
  }
  // A plan the server accepted cannot exceed the line or use another unit.
  if (!unplannedQuantity({ orderUnitId: o.orderUnitId, quantity: o.quantity }, slices).ok) return null
  return {
    lineId: o.lineId,
    lineNo: o.lineNo,
    productId: o.productId,
    productVersion: o.productVersion,
    productCode: o.productCode,
    productName: o.productName,
    orderUnitId: o.orderUnitId,
    quantity: o.quantity,
    unitPrice: o.unitPrice,
    priceUnitId: o.priceUnitId,
    requestedDeliveryDate: o.requestedDeliveryDate,
    note: o.note,
    requestedDeliverySlices: slices,
    discountPercent: o.discountPercent,
    priceDisclosure: o.priceDisclosure,
  }
}

const DETAIL_KEYS = ['draft', 'requestedDeliveryDate', 'note', 'lines', ...OWNER_REFERENCE_FIELDS, 'approval'] as const

/** `expectedId`: the record the caller asked for. A detail of another record is not an answer. */
export function decodeDraftDetail(value: unknown, expectedId: string | null): DraftDetail | null {
  const o = exact(value, DETAIL_KEYS)
  if (o === null) return null
  const draft = decodeDraftSummary(o.draft)
  if (draft === null || (expectedId !== null && draft.id !== expectedId)) return null
  if (!isNullable(o.requestedDeliveryDate, isDateOnly) || !isNote(o.note)) return null
  if (!Array.isArray(o.lines) || o.lines.length !== draft.lineCount) return null
  const lines: DraftLine[] = []
  const lineIds = new Set<string>()
  const lineNos = new Set<number>()
  for (const entry of o.lines) {
    const line = decodeLine(entry)
    if (line === null || lineIds.has(line.lineId) || lineNos.has(line.lineNo)) return null
    lineIds.add(line.lineId)
    lineNos.add(line.lineNo)
    lines.push(line)
  }
  const references: Partial<Record<(typeof OWNER_REFERENCE_FIELDS)[number], OwnerReference | null>> = {}
  for (const field of OWNER_REFERENCE_FIELDS) {
    const reference = decodeOwnerReference(o[field])
    if (reference === undefined) return null
    references[field] = reference
  }
  const approval = decodeApproval(o.approval, draft.approvalState)
  if (approval === null) return null
  return {
    draft,
    requestedDeliveryDate: o.requestedDeliveryDate,
    note: o.note,
    lines,
    deliveryAddress: references.deliveryAddress ?? null,
    contact: references.contact ?? null,
    responsible: references.responsible ?? null,
    paymentTerms: references.paymentTerms ?? null,
    sourceStrategy: references.sourceStrategy ?? null,
    package: references.package ?? null,
    approval,
  }
}

export function decodeDraftMutation(value: unknown): DraftMutation | null {
  const o = exact(value, ['detail', 'isReplay', 'originalCorrelationId'])
  if (o === null || typeof o.isReplay !== 'boolean' || !isText(o.originalCorrelationId, 200)) return null
  const detail = decodeDraftDetail(o.detail, null)
  if (detail === null) return null
  return { detail, isReplay: o.isReplay, originalCorrelationId: o.originalCorrelationId }
}

export function decodeDraftPage(value: unknown): DraftPage | null {
  const o = exact(value, ['items', 'nextCursor'])
  if (o === null || !Array.isArray(o.items) || !isNullable(o.nextCursor, (v): v is string => isText(v, 4096))) {
    return null
  }
  const items: DraftSummary[] = []
  const ids = new Set<string>()
  for (const entry of o.items) {
    const item = decodeDraftSummary(entry)
    if (item === null || ids.has(item.id)) return null
    ids.add(item.id)
    items.push(item)
  }
  return { items, nextCursor: o.nextCursor }
}

/**
 * A problem body, read leniently: platform 401/403/404 may be plain ProblemDetails
 * with extra members and no `code`. Only what the contract names is taken, and a
 * body that is not a problem at all yields null — the status still speaks.
 */
export function decodeProblem(value: unknown, status: number): SalesProblem | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const o = value as Obj
  if (o.status !== undefined && o.status !== status) return null
  const errors: SalesFieldError[] = []
  if (Array.isArray(o.errors)) {
    for (const entry of o.errors) {
      if (typeof entry !== 'object' || entry === null) continue
      const e = entry as Obj
      if (typeof e.field === 'string' && typeof e.code === 'string') errors.push({ field: e.field, code: e.code })
    }
  }
  return {
    status,
    title: typeof o.title === 'string' ? o.title : '',
    code: typeof o.code === 'string' ? o.code : null,
    correlationId: typeof o.correlationId === 'string' ? o.correlationId : null,
    errors,
  }
}

/* ── SALES-A2a: the two reference LIST reads of the create form's selects ────── */

/**
 * `GET /api/v1/sales/references/currencies?effectiveOn=` → `SalesCurrencyOptionsV1`: the Settings owner's
 * currency candidates of the company, each revalidated for the Sales operation on the date, as
 * `SalesCurrencyReferenceV1` rows. The code is a label; `currencyId` is the only identity sent back.
 */
export interface CurrencyOption {
  readonly currencyId: string
  readonly code: string
  readonly profileId: string
  readonly profileVersion: string
}

/**
 * `GET /api/v1/sales/references/contact-information?partyId=&companyAccountId=` →
 * `SalesContactInformationOptionsV1`: the Cari owner's ACTIVE delivery addresses and contact persons of
 * one party account, with the owner's versions — what a draft stores as `{id, expectedVersion}`.
 * `text` is the owner's address joined for display; no channel, phone or e-mail crosses into Sales.
 */
export interface AddressOption {
  readonly id: string
  readonly version: string
  readonly purpose: string
  readonly text: string
}

export interface ContactOption {
  readonly id: string
  readonly version: string
  readonly displayName: string
  readonly jobTitle: string | null
  readonly purpose: string
}

export interface ContactInformationOptions {
  readonly partyId: string
  readonly companyAccountId: string
  readonly accountVersion: string
  readonly addresses: readonly AddressOption[]
  readonly contacts: readonly ContactOption[]
}

/**
 * No price, discount, amount or payment member has any place on these reads (Sinan §4 #66
 * 5738113623: a reference read never carries a price). A body that names one anywhere — even
 * unused — is not the contract this build was written for, and is refused as malformed.
 */
const PRICE_LIKE_KEY = /price|discount|amount|payment|fiyat|iskonto|tutar/i

export function hasPriceLikeKey(value: unknown): boolean {
  if (Array.isArray(value)) return value.some(hasPriceLikeKey)
  if (typeof value !== 'object' || value === null) return false
  return Object.entries(value as Obj).some(([key, member]) => PRICE_LIKE_KEY.test(key) || hasPriceLikeKey(member))
}

const CURRENCY_OPTION_KEYS = ['companyId', 'effectiveOn', 'currencyId', 'code', 'label', 'profileId', 'profileVersion'] as const
const isNullableText = (v: unknown, max: number): v is string | null => v === null || isText(v, max)

/** The list bound to the company and date it was read for; any row of another company or date fails the whole read. */
export function decodeCurrencyOptions(value: unknown, companyId: string, effectiveOn: string): readonly CurrencyOption[] | null {
  if (hasPriceLikeKey(value)) return null
  const o = exact(value, ['companyId', 'effectiveOn', 'currencies'])
  if (o === null || o.companyId !== companyId || o.effectiveOn !== effectiveOn || !Array.isArray(o.currencies)) return null
  const currencies: CurrencyOption[] = []
  const ids = new Set<string>()
  for (const entry of o.currencies) {
    const c = exact(entry, CURRENCY_OPTION_KEYS)
    if (
      c === null ||
      c.companyId !== companyId ||
      c.effectiveOn !== effectiveOn ||
      !isReferenceId(c.currencyId) ||
      ids.has(c.currencyId) ||
      !isText(c.code, 64) ||
      !isText(c.label, 200) ||
      !isReferenceId(c.profileId) ||
      !isOwnerVersion(c.profileVersion)
    ) {
      return null
    }
    ids.add(c.currencyId)
    currencies.push({ currencyId: c.currencyId, code: c.code, profileId: c.profileId, profileVersion: c.profileVersion })
  }
  return currencies
}

const ADDRESS_OPTION_KEYS = ['id', 'version', 'purpose', 'text', 'countryCode', 'line1', 'line2', 'city', 'district', 'postalCode'] as const
const CONTACT_OPTION_KEYS = ['id', 'version', 'displayName', 'jobTitle', 'purpose'] as const

/** The options bound to the company and the party account they were read for. */
export function decodeContactInformationOptions(
  value: unknown,
  companyId: string,
  partyId: string,
  companyAccountId: string,
): ContactInformationOptions | null {
  if (hasPriceLikeKey(value)) return null
  const o = exact(value, ['companyId', 'partyId', 'companyAccountId', 'accountVersion', 'addresses', 'contacts'])
  if (
    o === null ||
    o.companyId !== companyId ||
    o.partyId !== partyId ||
    o.companyAccountId !== companyAccountId ||
    !isOwnerVersion(o.accountVersion) ||
    !Array.isArray(o.addresses) ||
    !Array.isArray(o.contacts)
  ) {
    return null
  }
  const addresses: AddressOption[] = []
  const addressIds = new Set<string>()
  for (const entry of o.addresses) {
    const a = exact(entry, ADDRESS_OPTION_KEYS)
    if (
      a === null ||
      !isReferenceId(a.id) ||
      addressIds.has(a.id) ||
      !isOwnerVersion(a.version) ||
      !isText(a.purpose, 64) ||
      !isText(a.text, 1000) ||
      !isText(a.countryCode, 8) ||
      !isText(a.line1, 500) ||
      !isNullableText(a.line2, 500) ||
      !isNullableText(a.city, 200) ||
      !isNullableText(a.district, 200) ||
      !isNullableText(a.postalCode, 32)
    ) {
      return null
    }
    addressIds.add(a.id)
    addresses.push({ id: a.id, version: a.version, purpose: a.purpose, text: a.text })
  }
  const contacts: ContactOption[] = []
  const contactIds = new Set<string>()
  for (const entry of o.contacts) {
    const c = exact(entry, CONTACT_OPTION_KEYS)
    if (
      c === null ||
      !isReferenceId(c.id) ||
      contactIds.has(c.id) ||
      !isOwnerVersion(c.version) ||
      !isText(c.displayName, 200) ||
      !isNullableText(c.jobTitle, 200) ||
      !isText(c.purpose, 64)
    ) {
      return null
    }
    contactIds.add(c.id)
    contacts.push({ id: c.id, version: c.version, displayName: c.displayName, jobTitle: c.jobTitle, purpose: c.purpose })
  }
  return { partyId, companyAccountId, accountVersion: o.accountVersion, addresses, contacts }
}
