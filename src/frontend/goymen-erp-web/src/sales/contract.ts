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
export const salesDraftCommercialRevisionPath = (id: string) => `${salesDraftDetailPath(id)}/commercial-revision`
export const salesDraftPriceFloorExceptionApprovalPath = (id: string) =>
  `${salesDraftDetailPath(id)}/below-floor-exception-approval`
export const salesDraftRevisionsPath = (id: string) => `${salesDraftDetailPath(id)}/revisions`
/** DEC-SAL-002 / AT-023: the quantity revision (POST, the quantity control's name). */
export const salesDraftQuantityRevisionPath = (id: string) => `${salesDraftDetailPath(id)}/quantity-revision`
/** DEC-SAL-003: the delivery-schedule update (POST) and its history (GET). */
export const salesDraftDeliverySchedulePath = (id: string) => `${salesDraftDetailPath(id)}/delivery-schedule`
export const salesDraftDeliverySchedulesPath = (id: string) => `${salesDraftDetailPath(id)}/delivery-schedules`
/** AT-024: per line the delivered and remaining quantity, read from the delivery owner's records (GET). */
export const salesDraftDeliveryProgressPath = (id: string) => `${salesDraftDetailPath(id)}/delivery-progress`
/** DEC-SAL-005: the impact preview of cancelling an approved order (GET, the cancel control's name). */
export const salesDraftCancellationImpactPath = (id: string) => `${salesDraftDetailPath(id)}/cancellation-impact`
/** DEC-SAL-005 / AT-007: the cancel command (POST) and the recorded cancellation (GET, the draft read). */
export const salesDraftCancellationPath = (id: string) => `${salesDraftDetailPath(id)}/cancellation`

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
 * DEC-SAL-005: the lifecycle `state` of an APPROVED order cancelled after its impact preview — terminal; its approval facts,
 * lines and history stay (AT-007). The approval axis still reads `Approved` (who approved stays who approved).
 */
export const CANCELLED_STATE = 'Cancelled' as const
export type LifecycleState = typeof DRAFT_STATE | typeof CANCELLED_STATE

/** Q9: server-derived stage of the same commercial record; legacy stored command results may omit it. */
export const COMMERCIAL_STAGE = {
  quote: 'Quote',
  order: 'Order',
  unavailable: 'Unavailable',
} as const
export type CommercialStage = (typeof COMMERCIAL_STAGE)[keyof typeof COMMERCIAL_STAGE]

/**
 * SALES-A1: the INTERNAL APPROVAL axis (`InternalApprovalState`), a fourth fact beside the lifecycle
 * `state`, the commercial `revision` and the concurrency `version` — never merged into one status.
 * Preparing → PendingApproval → Approved | Rejected (→ PendingApproval again, same Rev.0).
 * D2 (Sinan 5749046401): Withdrawn — the creator took an unapproved draft back; terminal, nothing deleted.
 */
export const APPROVAL_STATE = {
  preparing: 'Preparing',
  pendingApproval: 'PendingApproval',
  approved: 'Approved',
  rejected: 'Rejected',
  withdrawn: 'Withdrawn',
} as const
export type ApprovalState = (typeof APPROVAL_STATE)[keyof typeof APPROVAL_STATE]
const APPROVAL_STATES: readonly string[] = Object.values(APPROVAL_STATE)

/**
 * SALES-A1: the state commands. Each answers 200 with the draft after the transition (the
 * command's result IS the detail view), and 200 + `Idempotency-Replayed` for the same key again.
 * D2: `withdrawal` is the creator's own command (POST …/{id}/withdrawal, optional `reason`).
 */
export const STATE_COMMAND_STATUS = 200
export type DraftStateCommand = 'submission' | 'approval' | 'rejection' | 'withdrawal' | 'customer-acceptance'
export const salesDraftCommandPath = (id: string, command: DraftStateCommand) => `${salesDraftDetailPath(id)}/${command}`

/** `SalesDraftStateCommandRequest`: the expected version as text; `reason` only on a rejection. */
export interface DraftStateCommandRequest {
  readonly expectedVersion: string
  readonly reason?: string
}

/**
 * K3: every line exactly once, with nullable commercial values retained as text. AT-018 / AT-021 (contract delta): the
 * Product price-list version the line is priced from (ids only — the server re-reads the price), the discount as an
 * amount, and the maturity surcharge (vade farkı) as a percent or an amount.
 */
export interface DraftLineCommercialRequest {
  readonly lineId: string
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly discountPercent: string | null
  readonly priceReference: { readonly priceListId: string; readonly versionNo: number } | null
  readonly discountAmount: string | null
  readonly surchargePercent: string | null
  readonly surchargeAmount: string | null
}

export interface DraftCommercialRevisionRequest {
  readonly expectedVersion: string
  readonly lines: readonly DraftLineCommercialRequest[]
}

export interface DraftPriceFloorExceptionApprovalRequest {
  readonly expectedVersion: string
  readonly lines: readonly { readonly lineId: string; readonly reason: string }[]
}

/** DEC-SAL-002: one line's new ordered quantity, exact decimal text in the line's own order unit. */
export interface DraftLineQuantityRequest {
  readonly lineId: string
  readonly quantity: string
}

/**
 * DEC-SAL-002 / AT-023 (`SalesDraftQuantityRevisionRequest`): every member present — the expected version, the reason (why
 * the order changes; the server refuses a blank one) and every line of the draft exactly once with its new quantity. An
 * approved order is archived and re-enters approval; the unit, product, price and delivery plan do not change here.
 */
export interface DraftQuantityRevisionRequest {
  readonly expectedVersion: string
  readonly reason: string
  readonly lines: readonly DraftLineQuantityRequest[]
}

/**
 * DEC-SAL-003 (`SalesDraftDeliveryScheduleRequest`): the planner's delivery-date update — every member present. The header
 * and line dates may be null (none); every line of the draft exactly once, each with EVERY slice it already carries
 * (dates only: no quantity, no new or removed slice). No re-approval follows; the server audits the change.
 */
export interface DraftSliceScheduleRequest {
  readonly sliceId: string
  readonly requestedDeliveryDate: string
}

export interface DraftLineScheduleRequest {
  readonly lineId: string
  readonly requestedDeliveryDate: string | null
  readonly slices: readonly DraftSliceScheduleRequest[]
}

export interface DraftDeliveryScheduleRequest {
  readonly expectedVersion: string
  readonly requestedDeliveryDate: string | null
  readonly lines: readonly DraftLineScheduleRequest[]
}

/** DEC-SAL-003 history: schedule 0 is the draft as created (nobody changed it); every later one is a recorded change. */
export interface DeliveryScheduleSlice {
  readonly sliceId: string
  readonly quantity: string
  readonly requestedDeliveryDate: string
}

export interface DeliveryScheduleLine {
  readonly lineId: string
  readonly lineNo: number
  readonly requestedDeliveryDate: string | null
  readonly slices: readonly DeliveryScheduleSlice[]
}

export interface DeliverySchedule {
  readonly scheduleNo: number
  readonly changedBy: string | null
  readonly changedAtUtc: string | null
  readonly requestedDeliveryDate: string | null
  readonly lines: readonly DeliveryScheduleLine[]
}

export interface DeliverySchedules {
  readonly items: readonly DeliverySchedule[]
}

/**
 * AT-024: one line's quantities as exact decimal text in the line's order unit — `ordered` (the line), `shipped` and
 * `delivered` (the delivery owner's recorded totals), `remaining` (ordered − delivered, never below zero) and `excess`
 * (delivered above the order). Facts only: no completion or tolerance state exists (OPEN-004 is open).
 */
export interface DeliveryProgressLine {
  readonly lineId: string
  readonly lineNo: number
  readonly orderUnitId: string
  readonly ordered: string
  readonly shipped: string
  readonly delivered: string
  readonly remaining: string
  readonly excess: string
}

export interface DeliveryProgress {
  readonly draftVersion: string
  readonly lines: readonly DeliveryProgressLine[]
}

/** `SalesDraftApprovalV1`: who submitted / decided and when; null until it happened. Facts, never derived. */
export interface ApprovalFacts {
  readonly submittedBy: string | null
  readonly submittedAtUtc: string | null
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly rejectionReason: string | null
}

export interface CustomerAcceptanceFacts {
  readonly required: boolean
  readonly acceptedRevision: string | null
  readonly acceptedBy: string | null
  readonly acceptedAtUtc: string | null
  readonly isCurrentRevisionAccepted: boolean
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
  /** The lifecycle axis: `Draft`, or `Cancelled` (DEC-SAL-005) — never merged with the approval axis. */
  readonly state: LifecycleState
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
  /** Q9: never inferred by the client; Unavailable means an older stored wire result omitted the additive member. */
  readonly commercialStage: CommercialStage
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

/** The archived wire snapshot has no current product name, delivery plan or mutable fields. */
export interface DraftRevisionLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productVersion: string
  readonly orderUnitId: string
  readonly quantity: string
  readonly unitPrice: string | null
  readonly priceUnitId: string | null
  readonly discountPercent: string | null
  readonly priceDisclosure: PriceDisclosure
}

export interface DraftRevision {
  readonly revision: string
  readonly submittedBy: string | null
  readonly submittedAtUtc: string | null
  readonly approvedBy: string | null
  readonly approvedAtUtc: string | null
  readonly archivedAtUtc: string
  readonly archivedBy: string
  readonly moneyProfileId: string
  readonly moneyProfileVersion: string
  readonly moneyScale: number
  readonly lines: readonly DraftRevisionLine[]
  /** DEC-SAL-002: why this approved revision was superseded (a quantity revision's reason); null when a price revision archived it. */
  readonly reason: string | null
}

export interface DraftRevisions {
  /** Archived approved snapshots, oldest first; not the current draft. */
  readonly items: readonly DraftRevision[]
}

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
  readonly customerAcceptance: CustomerAcceptanceFacts
  /**
   * D2: the RELATIONSHIP "the signed-in caller is this draft's stored creator", computed on the server — never the
   * creator's identity. The screen mirrors it (the withdrawal control); the server refuses regardless.
   */
  readonly isOwnDraft: boolean
  /** D2: true only when this creator is currently in the SALES_SUPPORT persona. */
  readonly isOwnDecisionRestricted: boolean
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
  // DEC-SAL-005 (contract deltas): an owner records an effect of the order (the errors name each source) — the fail-closed
  // reading refuses; and the impact no longer is the preview the command named (read it again).
  cancellationBlocked: 'sales_cancellation_blocked',
  cancellationImpactChanged: 'sales_cancellation_impact_changed',
  // DEC-SAL-002 (contract delta): an owner links a record to the order (allocation, loading line) — the quantity revision is
  // refused fail-closed, like the cancellation; the errors name each blocking source.
  revisionBlocked: 'sales_revision_blocked',
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

/** Every required member plus only the named additive members; unknown members still fail closed. */
function exactWithOptional(value: unknown, required: readonly string[], optional: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  const allowed = new Set([...required, ...optional])
  for (const key of Object.keys(obj)) if (!allowed.has(key)) return null
  for (const key of required) if (!Object.hasOwn(obj, key)) return null
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
 * D2: Withdrawn carries the decision (the withdrawer, the time, an optional reason) and a submission
 * that is either whole or absent — withdrawn from Preparing has none.
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
          : state === APPROVAL_STATE.rejected
            ? submitted && decided
            : decided && (submitted || (o.submittedBy === null && o.submittedAtUtc === null))
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
  const o = exactWithOptional(value, SUMMARY_KEYS, ['commercialStage'])
  const hasCommercialStage = o !== null && Object.hasOwn(o, 'commercialStage')
  const commercialStage = !hasCommercialStage || o?.commercialStage === null
    ? COMMERCIAL_STAGE.unavailable
    : o?.commercialStage === COMMERCIAL_STAGE.quote || o?.commercialStage === COMMERCIAL_STAGE.order
      ? o.commercialStage
      : null
  if (
    o === null ||
    commercialStage === null ||
    !isReferenceId(o.id) ||
    !isReferenceId(o.companyId) ||
    !isText(o.documentNo, 200) ||
    // Goods Orders in Draft, or (DEC-SAL-005) Cancelled — and only an approved order is ever cancelled. Any other kind or
    // state is a meaning this build was not written for, and is not shown as if it were a Draft.
    o.kind !== ORDER_KIND ||
    (o.state !== DRAFT_STATE && o.state !== CANCELLED_STATE) ||
    (o.state === CANCELLED_STATE && o.approvalState !== APPROVAL_STATE.approved) ||
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
    state: o.state === CANCELLED_STATE ? CANCELLED_STATE : DRAFT_STATE,
    version: o.version as string,
    revision: o.revision,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    currencyId: o.currencyId,
    businessDate: o.businessDate,
    createdAtUtc: o.createdAtUtc,
    lineCount: o.lineCount,
    approvalState: o.approvalState,
    commercialStage,
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

const DETAIL_KEYS = [
  'draft',
  'requestedDeliveryDate',
  'note',
  'lines',
  ...OWNER_REFERENCE_FIELDS,
  'approval',
  'customerAcceptance',
  'isOwnDraft',
  'isOwnDecisionRestricted',
] as const

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
  const customerAcceptance = decodeCustomerAcceptance(o.customerAcceptance, draft.revision)
  if (
    approval === null ||
    customerAcceptance === null ||
    typeof o.isOwnDraft !== 'boolean' ||
    typeof o.isOwnDecisionRestricted !== 'boolean'
  ) return null
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
    customerAcceptance,
    isOwnDraft: o.isOwnDraft,
    isOwnDecisionRestricted: o.isOwnDecisionRestricted,
  }
}

function decodeCustomerAcceptance(value: unknown, currentRevision: string): CustomerAcceptanceFacts | null {
  const o = exact(value, ['required', 'acceptedRevision', 'acceptedBy', 'acceptedAtUtc', 'isCurrentRevisionAccepted'])
  if (o === null || typeof o.required !== 'boolean' || typeof o.isCurrentRevisionAccepted !== 'boolean') return null
  if (!isNullable(o.acceptedRevision, (v): v is string => typeof v === 'string' && /^Rev\.(0|[1-9][0-9]*)$/.test(v))) return null
  if (!isNullable(o.acceptedBy, (v): v is string => isText(v, 128)) || !isNullable(o.acceptedAtUtc, isInstant)) return null
  const empty = o.acceptedRevision === null && o.acceptedBy === null && o.acceptedAtUtc === null
  const complete = o.acceptedRevision !== null && o.acceptedBy !== null && o.acceptedAtUtc !== null
  const consistent = !o.required
    ? empty && o.isCurrentRevisionAccepted
    : empty
      ? !o.isCurrentRevisionAccepted
      : complete && o.isCurrentRevisionAccepted === (o.acceptedRevision === currentRevision)
  return consistent ? {
    required: o.required,
    acceptedRevision: o.acceptedRevision,
    acceptedBy: o.acceptedBy,
    acceptedAtUtc: o.acceptedAtUtc,
    isCurrentRevisionAccepted: o.isCurrentRevisionAccepted,
  } : null
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

/* ── S-04 approved commercial revision history ───────────────────────────── */

const REVISION_KEYS = [
  'revision', 'submittedBy', 'submittedAtUtc', 'approvedBy', 'approvedAtUtc',
  'archivedAtUtc', 'archivedBy', 'moneyProfileId', 'moneyProfileVersion', 'moneyScale', 'lines', 'reason',
] as const
const REVISION_LINE_KEYS = [
  'lineId', 'lineNo', 'productId', 'productVersion', 'orderUnitId', 'quantity',
  'unitPrice', 'priceUnitId', 'discountPercent', 'priceDisclosure',
] as const

// Reuse the Sales primitive shapes; additionally reject trailing line terminators
// ($ in a JavaScript regexp otherwise also matches before a final newline).
const isRevisionId = (v: unknown): v is string => isReferenceId(v) && v.length === 36
const isRevisionDecimal = (v: unknown, positive: boolean): v is string =>
  isDecimal(v, positive) && v.trim() === v
const isRevisionActor = (v: unknown): v is string => isActor(v) && v.trim().length > 0
const isRevisionInstant = (v: unknown): v is string =>
  isInstant(v) && v.trim() === v && isDateOnly(v.slice(0, 10)) &&
  Number(v.slice(11, 13)) < 24 && Number(v.slice(14, 16)) < 60 && Number(v.slice(17, 19)) < 60

function decodeDraftRevisionLine(value: unknown): DraftRevisionLine | null {
  const o = exact(value, REVISION_LINE_KEYS)
  if (
    o === null || !isRevisionId(o.lineId) || !isPositiveInt(o.lineNo, LIMITS.lines) ||
    !isRevisionId(o.productId) || !isOwnerVersion(o.productVersion) ||
    !isRevisionId(o.orderUnitId) || !isRevisionDecimal(o.quantity, true) ||
    !isNullable(o.unitPrice, (v): v is string => isRevisionDecimal(v, false)) ||
    !isNullable(o.priceUnitId, isRevisionId) ||
    (o.unitPrice === null) !== (o.priceUnitId === null) ||
    !isNullable(o.discountPercent, (v): v is string => isRevisionDecimal(v, false)) ||
    !isPriceDisclosure(o.priceDisclosure) ||
    (o.priceDisclosure === 'Withheld' &&
      (o.unitPrice !== null || o.priceUnitId !== null || o.discountPercent !== null))
  ) return null
  return {
    lineId: o.lineId, lineNo: o.lineNo, productId: o.productId,
    productVersion: o.productVersion, orderUnitId: o.orderUnitId, quantity: o.quantity,
    unitPrice: o.unitPrice, priceUnitId: o.priceUnitId,
    discountPercent: o.discountPercent, priceDisclosure: o.priceDisclosure,
  }
}

/**
 * SalesOrderDraftRevisionsResponse at the reserved source: every member is
 * required, even when nullable. The server owns disclosure and archival facts;
 * this decoder neither fills in an approval nor calculates money.
 */
export function decodeDraftRevisions(value: unknown): DraftRevisions | null {
  const response = exact(value, ['items'])
  if (response === null || !Array.isArray(response.items)) return null
  const items: DraftRevision[] = []
  let previousDigits: string | null = null
  for (const entry of response.items) {
    const o = exact(entry, REVISION_KEYS)
    if (
      o === null || typeof o.revision !== 'string' ||
      !REVISION_RE.test(o.revision) || o.revision.trim() !== o.revision ||
      !isNullable(o.submittedBy, isRevisionActor) ||
      !isNullable(o.submittedAtUtc, isRevisionInstant) ||
      !isNullable(o.approvedBy, isRevisionActor) ||
      !isNullable(o.approvedAtUtc, isRevisionInstant) ||
      !isRevisionInstant(o.archivedAtUtc) || !isRevisionActor(o.archivedBy) ||
      !isRevisionId(o.moneyProfileId) || !isOwnerVersion(o.moneyProfileVersion) ||
      typeof o.moneyScale !== 'number' || !Number.isInteger(o.moneyScale) ||
      o.moneyScale < 0 || o.moneyScale > 2147483647 ||
      !Array.isArray(o.lines) || o.lines.length < 1 || o.lines.length > LIMITS.lines ||
      !isNullable(o.reason, (v): v is string => isText(v, LIMITS.note) && v.trim().length > 0)
    ) return null
    // Revision is a .NET Int32 on the server. Compare its canonical digits as
    // text: do not lexically sort Rev.10 ahead of Rev.2 or accept duplicate rows.
    const digits = o.revision.slice(4)
    if (digits.length > 10 || (digits.length === 10 && digits > '2147483647')) return null
    if (previousDigits !== null &&
      (digits.length < previousDigits.length ||
        (digits.length === previousDigits.length && digits <= previousDigits))) return null
    previousDigits = digits
    const lines: DraftRevisionLine[] = []
    const ids = new Set<string>()
    const numbers = new Set<number>()
    for (const rawLine of o.lines) {
      const line = decodeDraftRevisionLine(rawLine)
      if (line === null || ids.has(line.lineId.toLowerCase()) || numbers.has(line.lineNo)) return null
      ids.add(line.lineId.toLowerCase())
      numbers.add(line.lineNo)
      lines.push(line)
    }
    items.push({
      revision: o.revision, submittedBy: o.submittedBy, submittedAtUtc: o.submittedAtUtc,
      approvedBy: o.approvedBy, approvedAtUtc: o.approvedAtUtc,
      archivedAtUtc: o.archivedAtUtc, archivedBy: o.archivedBy,
      moneyProfileId: o.moneyProfileId, moneyProfileVersion: o.moneyProfileVersion,
      moneyScale: o.moneyScale, lines, reason: o.reason,
    })
  }
  return { items }
}

/**
 * DEC-SAL-003: `SalesOrderDraftDeliverySchedulesResponse`, read exactly. Schedule 0 first (no actor, no time), then the
 * recorded changes in ascending order without a gap, each attributed; every line once with its slices once. A body that
 * does not fit is not shown as history.
 */
export function decodeDeliverySchedules(value: unknown): DeliverySchedules | null {
  const response = exact(value, ['items'])
  if (response === null || !Array.isArray(response.items) || response.items.length < 1) return null
  const items: DeliverySchedule[] = []
  for (const [index, entry] of response.items.entries()) {
    const o = exact(entry, ['scheduleNo', 'changedBy', 'changedAtUtc', 'requestedDeliveryDate', 'lines'])
    if (
      o === null || o.scheduleNo !== index ||
      !isNullable(o.changedBy, isRevisionActor) || !isNullable(o.changedAtUtc, isRevisionInstant) ||
      (index === 0) !== (o.changedBy === null) || (o.changedBy === null) !== (o.changedAtUtc === null) ||
      !isNullable(o.requestedDeliveryDate, isDateOnly) ||
      !Array.isArray(o.lines) || o.lines.length < 1 || o.lines.length > LIMITS.lines
    ) return null
    const lines: DeliveryScheduleLine[] = []
    const lineIds = new Set<string>()
    for (const rawLine of o.lines) {
      const l = exact(rawLine, ['lineId', 'lineNo', 'requestedDeliveryDate', 'slices'])
      if (
        l === null || !isRevisionId(l.lineId) || lineIds.has(l.lineId.toLowerCase()) ||
        !isPositiveInt(l.lineNo, LIMITS.lines) || !isNullable(l.requestedDeliveryDate, isDateOnly) ||
        !Array.isArray(l.slices) || l.slices.length > LIMITS.slices
      ) return null
      lineIds.add(l.lineId.toLowerCase())
      const slices: DeliveryScheduleSlice[] = []
      const sliceIds = new Set<string>()
      for (const rawSlice of l.slices) {
        const s = exact(rawSlice, ['sliceId', 'quantity', 'requestedDeliveryDate'])
        if (
          s === null || !isRevisionId(s.sliceId) || sliceIds.has(s.sliceId.toLowerCase()) ||
          !isRevisionDecimal(s.quantity, true) || !isDateOnly(s.requestedDeliveryDate)
        ) return null
        sliceIds.add(s.sliceId.toLowerCase())
        slices.push({ sliceId: s.sliceId, quantity: s.quantity, requestedDeliveryDate: s.requestedDeliveryDate })
      }
      lines.push({ lineId: l.lineId, lineNo: l.lineNo, requestedDeliveryDate: l.requestedDeliveryDate, slices })
    }
    items.push({
      scheduleNo: o.scheduleNo, changedBy: o.changedBy, changedAtUtc: o.changedAtUtc,
      requestedDeliveryDate: o.requestedDeliveryDate, lines,
    })
  }
  return { items }
}

/**
 * AT-024: `SalesOrderDraftDeliveryProgressResponse`, read exactly — and only as the progress OF `detail`: the same
 * version, every line of the detail once in its order with its own order unit and quantity text. Decimal text stays
 * text (never parsed into a JS number); `ordered` is positive, the others may be zero. A body that does not fit is not
 * shown as quantities.
 */
export function decodeDeliveryProgress(value: unknown, detail: DraftDetail): DeliveryProgress | null {
  const response = exact(value, ['draftVersion', 'lines'])
  if (
    response === null || response.draftVersion !== detail.draft.version ||
    !Array.isArray(response.lines) || response.lines.length !== detail.lines.length
  ) return null
  const lines: DeliveryProgressLine[] = []
  for (const [index, entry] of response.lines.entries()) {
    const o = exact(entry, ['lineId', 'lineNo', 'orderUnitId', 'ordered', 'shipped', 'delivered', 'remaining', 'excess'])
    const line = detail.lines[index]
    if (
      o === null || !isRevisionId(o.lineId) || o.lineId.toLowerCase() !== line.lineId.toLowerCase() ||
      o.lineNo !== line.lineNo || !isRevisionId(o.orderUnitId) ||
      o.orderUnitId.toLowerCase() !== line.orderUnitId.toLowerCase() || o.ordered !== line.quantity ||
      !isRevisionDecimal(o.ordered, true) || !isRevisionDecimal(o.shipped, false) ||
      !isRevisionDecimal(o.delivered, false) || !isRevisionDecimal(o.remaining, false) || !isRevisionDecimal(o.excess, false)
    ) return null
    lines.push({
      lineId: o.lineId, lineNo: o.lineNo, orderUnitId: o.orderUnitId, ordered: o.ordered,
      shipped: o.shipped, delivered: o.delivered, remaining: o.remaining, excess: o.excess,
    })
  }
  return { draftVersion: response.draftVersion, lines }
}

/* ── DEC-SAL-005 / AT-007: the cancellation of an approved order ────────────── */

/** The six DEC-SAL-005 categories, the decision's order. */
export const CANCELLATION_SOURCE = {
  activeAllocations: 'inventory.active-allocations',
  linkedPurchasing: 'purchasing.linked-orders',
  linkedProduction: 'manufacturing.linked-orders',
  loadingWithoutDispatchNote: 'logistics.loading-without-dispatch-note',
  uninvoicedDispatchNotes: 'invoicing.uninvoiced-dispatch-notes',
  unmatchedAdvances: 'finance.unmatched-advances',
} as const
export type CancellationSource = (typeof CANCELLATION_SOURCE)[keyof typeof CANCELLATION_SOURCE]
const CANCELLATION_SOURCES: readonly string[] = Object.values(CANCELLATION_SOURCE)

/**
 * What the preview knows of one category: the owner answered nothing (`None`) or records (`Present`, blocks); the owner
 * exists but could not be read (`Unreadable`, blocks — never zero); no module on main records this link (`NoSource`).
 */
export const CANCELLATION_STATUS = { none: 'None', present: 'Present', unreadable: 'Unreadable', noSource: 'NoSource' } as const
export type CancellationStatus = (typeof CANCELLATION_STATUS)[keyof typeof CANCELLATION_STATUS]
const CANCELLATION_STATUSES: readonly string[] = Object.values(CANCELLATION_STATUS)

export interface CancellationImpactSource {
  readonly source: CancellationSource
  readonly status: CancellationStatus
}

/** One line of the preview; a null member means its owner could not be read — never zero in its place. */
export interface CancellationImpactLine {
  readonly lineId: string
  readonly lineNo: number
  readonly orderUnitId: string
  readonly ordered: string
  readonly activeAllocations: number | null
  readonly allocatedKg: string | null
  readonly loadingInstructionLines: number | null
  readonly instructed: string | null
  readonly loaded: string | null
  readonly shipped: string | null
  readonly delivered: string | null
}

export interface CancellationImpact {
  readonly draftVersion: string
  readonly revision: string
  readonly cancellable: boolean
  /** The identity of exactly this preview; the cancel command carries it back. */
  readonly impactToken: string
  readonly sources: readonly CancellationImpactSource[]
  readonly lines: readonly CancellationImpactLine[]
}

/** `SalesDraftCancelRequest`: every member present; the reason is required (the server trims and bounds it). */
export interface DraftCancelRequest {
  readonly expectedVersion: string
  readonly reason: string
  readonly impactToken: string
}

/** AT-007: the recorded cancellation — who, when, why, the revision cancelled and the preview it was decided on. */
export interface CancellationRecord {
  readonly revision: string
  readonly cancelledBy: string
  readonly cancelledAtUtc: string
  readonly reason: string
  readonly impactToken: string
  readonly sources: readonly CancellationImpactSource[]
}

export interface CancellationRead {
  readonly cancellation: CancellationRecord | null
}

const IMPACT_TOKEN_RE = /^[0-9a-f]{64}$/
const isCount = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 2147483647

function decodeCancellationSources(value: unknown): readonly CancellationImpactSource[] | null {
  if (!Array.isArray(value) || value.length !== CANCELLATION_SOURCES.length) return null
  const sources: CancellationImpactSource[] = []
  for (const [index, entry] of value.entries()) {
    const o = exact(entry, ['source', 'status'])
    // The decision's six categories, each once, in its order; a status this build does not know is not shown.
    if (o === null || o.source !== CANCELLATION_SOURCES[index] || typeof o.status !== 'string' || !CANCELLATION_STATUSES.includes(o.status)) return null
    sources.push({ source: o.source as CancellationSource, status: o.status as CancellationStatus })
  }
  return sources
}

/**
 * DEC-SAL-005: `SalesOrderDraftCancellationImpactResponse`, read exactly and only as the preview OF `detail` (its version and
 * revision, every line once in order with its unit and quantity). An owner's numbers are null together or present together,
 * and null exactly when that owner is Unreadable — a body that does not fit is not shown.
 */
export function decodeCancellationImpact(value: unknown, detail: DraftDetail): CancellationImpact | null {
  const o = exact(value, ['draftVersion', 'revision', 'cancellable', 'impactToken', 'sources', 'lines'])
  if (
    o === null || o.draftVersion !== detail.draft.version || o.revision !== detail.draft.revision ||
    typeof o.cancellable !== 'boolean' || typeof o.impactToken !== 'string' || !IMPACT_TOKEN_RE.test(o.impactToken) ||
    !Array.isArray(o.lines) || o.lines.length !== detail.lines.length
  ) return null
  const sources = decodeCancellationSources(o.sources)
  if (sources === null) return null
  const statusOf = (source: CancellationSource) => sources.find((entry) => entry.source === source)?.status
  const allocationsRead = statusOf(CANCELLATION_SOURCE.activeAllocations) !== CANCELLATION_STATUS.unreadable
  const loadingRead = statusOf(CANCELLATION_SOURCE.loadingWithoutDispatchNote) !== CANCELLATION_STATUS.unreadable
  const blocked = sources.some((entry) => entry.status === CANCELLATION_STATUS.present || entry.status === CANCELLATION_STATUS.unreadable)
  if (o.cancellable && blocked) return null
  const lines: CancellationImpactLine[] = []
  for (const [index, entry] of o.lines.entries()) {
    const l = exact(entry, ['lineId', 'lineNo', 'orderUnitId', 'ordered', 'activeAllocations', 'allocatedKg',
      'loadingInstructionLines', 'instructed', 'loaded', 'shipped', 'delivered'])
    const line = detail.lines[index]
    if (
      l === null || !isRevisionId(l.lineId) || l.lineId.toLowerCase() !== line.lineId.toLowerCase() || l.lineNo !== line.lineNo ||
      !isRevisionId(l.orderUnitId) || l.orderUnitId.toLowerCase() !== line.orderUnitId.toLowerCase() || l.ordered !== line.quantity
    ) return null
    const allocation = [l.activeAllocations, l.allocatedKg]
    const loading = [l.loadingInstructionLines, l.instructed, l.loaded, l.shipped, l.delivered]
    const allocationOk = allocationsRead
      ? isCount(l.activeAllocations) && isRevisionDecimal(l.allocatedKg, false)
      : allocation.every((member) => member === null)
    const loadingOk = loadingRead
      ? isCount(l.loadingInstructionLines) && [l.instructed, l.loaded, l.shipped, l.delivered].every((member) => isRevisionDecimal(member, false))
      : loading.every((member) => member === null)
    if (!allocationOk || !loadingOk) return null
    lines.push({
      lineId: l.lineId, lineNo: l.lineNo, orderUnitId: l.orderUnitId, ordered: l.ordered as string,
      activeAllocations: l.activeAllocations as number | null, allocatedKg: l.allocatedKg as string | null,
      loadingInstructionLines: l.loadingInstructionLines as number | null, instructed: l.instructed as string | null,
      loaded: l.loaded as string | null, shipped: l.shipped as string | null, delivered: l.delivered as string | null,
    })
  }
  return { draftVersion: o.draftVersion, revision: o.revision, cancellable: o.cancellable, impactToken: o.impactToken, sources, lines }
}

/** AT-007: `SalesOrderDraftCancellationResponse`, read exactly: null, or the recorded facts with the preview's categories. */
export function decodeCancellation(value: unknown): CancellationRead | null {
  const o = exact(value, ['cancellation'])
  if (o === null) return null
  if (o.cancellation === null) return { cancellation: null }
  const c = exact(o.cancellation, ['revision', 'cancelledBy', 'cancelledAtUtc', 'reason', 'impactToken', 'sources'])
  if (
    c === null || typeof c.revision !== 'string' || !REVISION_RE.test(c.revision) || !isRevisionActor(c.cancelledBy) ||
    !isRevisionInstant(c.cancelledAtUtc) || !isText(c.reason, LIMITS.note) || c.reason.trim().length === 0 ||
    typeof c.impactToken !== 'string' || !IMPACT_TOKEN_RE.test(c.impactToken)
  ) return null
  const sources = decodeCancellationSources(c.sources)
  if (sources === null) return null
  return {
    cancellation: {
      revision: c.revision, cancelledBy: c.cancelledBy, cancelledAtUtc: c.cancelledAtUtc, reason: c.reason,
      impactToken: c.impactToken, sources,
    },
  }
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
