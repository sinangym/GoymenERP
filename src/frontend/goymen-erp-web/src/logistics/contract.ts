/**
 * The Logistics loading-instruction contract — the only file that spells a Logistics wire name.
 *
 * Pinned to LOG-4a (#350, main): `LogisticsInstructionContractsV1.cs` and `LogisticsEndpoints.cs`.
 *
 *   POST /api/v1/logistics/loading-instructions                 → 201 LoadingInstructionResponse
 *                                                                 (again 201 + `Idempotency-Replayed` on the same attempt)
 *   GET  /api/v1/logistics/loading-instructions?pageSize&cursor&status → LoadingInstructionListResponse
 *   GET  /api/v1/logistics/loading-instructions/{id}            → LoadingInstructionDetailView
 *   POST /api/v1/logistics/loading-instructions/{id}/issue      → 200 LoadingInstructionResponse (Draft → Issued)
 *   POST /api/v1/logistics/loading-instructions/{id}/lines/{lineId}/loads → 200 LoadingInstructionResponse
 *
 * Rules taken from the source rather than assumed:
 *  - every response member is PRESENT, nullable ones as explicit null; unknown members, wrong case
 *    and numbers standing in for decimal/version strings are refused;
 *  - every quantity (§7.4 shares and remainders) is exact TEXT, kept verbatim, never a JS number (Ö2);
 *  - the client sends no product: the Sales owner answers the line's product from the APPROVED line;
 *  - nothing here carries a price, a currency or any finance field (Business 5743412166, the
 *    Sevkiyatçı rule), and the Sales reads used by the line picker decode only what the picker needs.
 *
 * A decoder returns null for anything it cannot vouch for; the caller fails closed.
 */

export const LOGISTICS_CONTRACT_STATUS = 'PINNED_SOURCE' as const

export const LOGISTICS_PATH = '/logistics/loading-instructions'
export const instructionDetailPath = (id: string) => `${LOGISTICS_PATH}/${encodeURIComponent(id)}`
export const issuePath = (id: string) => `${instructionDetailPath(id)}/issue`
export const loadsPath = (id: string, lineId: string) => `${instructionDetailPath(id)}/lines/${encodeURIComponent(lineId)}/loads`

/** `LogisticsApiV1`: a first commit answers 201; its replay 201 + `Idempotency-Replayed: true` (ADR-011 §4). */
export const CREATED_STATUS = 201
/** A transition (issue / load) answers 200 with the instruction after it; the same key again 200 + the replay header. */
export const MUTATION_STATUS = 200
export const REPLAY_HEADER = 'Idempotency-Replayed'
export const ORIGINAL_CORRELATION_HEADER = 'Idempotency-Original-Correlation-Id'

export const INSTRUCTION_STATUS = { draft: 'Draft', issued: 'Issued' } as const
export type InstructionStatus = (typeof INSTRUCTION_STATUS)[keyof typeof INSTRUCTION_STATUS]
const INSTRUCTION_STATUSES: readonly string[] = Object.values(INSTRUCTION_STATUS)

export const MEASUREMENT = { measured: 'Measured', allocatedFromVehicleWeight: 'AllocatedFromVehicleWeight' } as const
export type Measurement = (typeof MEASUREMENT)[keyof typeof MEASUREMENT]
const MEASUREMENTS: readonly string[] = Object.values(MEASUREMENT)

export const SHARE_KIND = { loaded: 'Loaded', shipped: 'Shipped', delivered: 'Delivered' } as const
export type ShareKind = (typeof SHARE_KIND)[keyof typeof SHARE_KIND]
const SHARE_KINDS: readonly string[] = Object.values(SHARE_KIND)

/** Technical bounds from `LogisticsJson` / the endpoints. Not owner scale or precision. */
export const LIMITS = {
  lines: 500,
  decimalText: 128,
  versionText: 19,
  idempotencyKey: 128,
  cursor: 8192,
  /** Ö2's kilogram cap; the owner's profile may be tighter and the server measures it. */
  kilogramScale: 3,
  /** The decimal(18,3) column: at most 15 integer digits (the #350 P2-1 / P3-2 rule). */
  integerDigits: 15,
} as const

/* ── Requests ─────────────────────────────────────────────────────────────── */

export interface InstructionLineRequest {
  /** Client-owned, minted once when the line is picked, preserved on retry. */
  readonly lineId: string
  readonly sourceOrderId: string
  readonly sourceOrderLineId: string
  readonly unitId: string
  /** Exact decimal text (Ö2), sent as typed after canonicalisation, never a number. */
  readonly quantity: string
}

/** `CreateLoadingInstructionRequest`. No company, actor, product or version: the server's scope and the owners own those. */
export interface CreateInstructionRequest {
  readonly customerPartyId: string
  readonly companyAccountId: string
  readonly effectiveOn: string
  readonly lines: readonly InstructionLineRequest[]
}

/** `IssueLoadingInstructionRequest`: the expected version as text. */
export interface IssueRequest {
  readonly expectedVersion: string
}

/** `RecordLoadRequest`: one loaded share; the load id is the client's and travels with the snapshot. */
export interface RecordLoadRequest {
  readonly expectedVersion: string
  readonly loadId: string
  readonly quantity: string
  readonly measurement: Measurement
}

/* ── Responses ────────────────────────────────────────────────────────────── */

export interface InstructionSummary {
  readonly id: string
  readonly companyId: string
  readonly status: InstructionStatus
  readonly version: string
  readonly customerPartyId: string
  readonly companyAccountId: string
  readonly effectiveOn: string
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly lineCount: number
}

/** The four §7.4 shares and the derived remainders, all exact text; the Sales binding (D4). */
export interface InstructionLine {
  readonly lineId: string
  readonly lineNo: number
  readonly sourceOrderId: string
  readonly sourceOrderLineId: string
  readonly sourceOrderRevision: number
  readonly productId: string
  readonly unitId: string
  readonly instructed: string
  readonly loaded: string
  readonly shipped: string
  readonly delivered: string
  readonly remainingToLoad: string
  readonly loadedNotShipped: string
  readonly shippedNotDelivered: string
  readonly isBlocked: boolean
}

export interface LoadingShare {
  readonly shareId: string
  readonly lineId: string
  readonly kind: ShareKind
  readonly quantity: string
  readonly measurement: Measurement | null
  readonly sourceShipmentId: string | null
  readonly recordedAtUtc: string
}

export interface InstructionDetail {
  readonly instruction: InstructionSummary
  readonly lines: readonly InstructionLine[]
  readonly shares: readonly LoadingShare[]
}

export interface InstructionMutation {
  readonly detail: InstructionDetail
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

export interface InstructionPage {
  readonly items: readonly InstructionSummary[]
  /** null is the end of the collection. Opaque; the client never builds one. */
  readonly nextCursor: string | null
}

export interface LogisticsFieldError {
  readonly field: string
  readonly code: string
}

export interface LogisticsProblem {
  readonly status: number
  readonly title: string
  readonly code: string | null
  readonly correlationId: string | null
  readonly errors: readonly LogisticsFieldError[]
}

/** `LogisticsProblemCodesV1` plus the shared codes the routes answer. 503s carry only the platform's `service_unavailable`. */
export const LOGISTICS_PROBLEM_CODE = {
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  invalidCursor: 'invalid_cursor',
  accountState: 'account_state',
  orderLineState: 'order_line_state',
  versionConflict: 'version_conflict',
  instructionState: 'logistics_instruction_state',
  loadExceedsInstructed: 'logistics_load_exceeds_instructed',
  lineBlocked: 'logistics_line_blocked',
  duplicateShare: 'logistics_duplicate_share_id',
  sameKeyPayloadMismatch: 'same_key_payload_mismatch',
  concurrentChange: 'concurrent_change',
  unsupportedUnit: 'unsupported_unit',
  serviceUnavailable: 'service_unavailable',
} as const

export type LogisticsProblemCode = (typeof LOGISTICS_PROBLEM_CODE)[keyof typeof LOGISTICS_PROBLEM_CODE]

/* ── The Sales reads the line picker uses (read-only, ASSUMPTION-LOG4B-1) ──── */

export const SALES_DRAFT_PATH = '/sales/drafts'
export const salesDraftDetailPath = (id: string) => `${SALES_DRAFT_PATH}/${encodeURIComponent(id)}`
export const SALES_APPROVED_STATE = 'Approved' as const

/** Only what the customer select needs from an APPROVED draft. Nothing commercial is read. */
export interface ApprovedDraftSummary {
  readonly id: string
  readonly companyId: string
  readonly documentNo: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly version: string
}

/** Only what the line picker needs. `unitPrice`, `priceUnitId`, `discountPercent` and the currency are never read. */
export interface ApprovedDraftLine {
  readonly lineId: string
  readonly lineNo: number
  readonly productId: string
  readonly productName: string
  readonly orderUnitId: string
  readonly quantity: string
}

export interface ApprovedDraftPage {
  readonly items: readonly ApprovedDraftSummary[]
  readonly nextCursor: string | null
}

/* ── Primitives ───────────────────────────────────────────────────────────── */

type Obj = Record<string, unknown>

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const DATE_ONLY_RE = /^(\d{4})-(\d{2})-(\d{2})$/
const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
/** The wire's decimal grammar (`LogisticsJson.RequireDecimalShape`): digits, one inner point, a non-zero digit. */
const DECIMAL_RE = /^[0-9]+(?:\.[0-9]+)?$/
const NON_NEGATIVE_DECIMAL_RE = /^[0-9]+(?:\.[0-9]+)?$/

/** A non-empty UUID. The all-zero UUID is not a reference to anything. */
export function isReferenceId(value: unknown): value is string {
  return typeof value === 'string' && UUID_RE.test(value) && /[1-9a-f]/i.test(value)
}

/** `YYYY-MM-DD` naming a day that exists. */
export function isDateOnly(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const match = DATE_ONLY_RE.exec(value)
  if (match === null) return false
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1 || month < 1 || month > 12 || day < 1) return false
  return day <= new Date(Date.UTC(year, month, 0)).getUTCDate()
}

/** A concurrency version: a lossless positive integer carried as a string (never a JSON number). */
export function isVersionString(value: unknown): value is string {
  return typeof value === 'string' && /^[1-9][0-9]*$/.test(value) && value.length <= LIMITS.versionText
}

const isInstant = (v: unknown): v is string => typeof v === 'string' && INSTANT_RE.test(v) && !Number.isNaN(Date.parse(v))
const isText = (v: unknown, max: number): v is string => typeof v === 'string' && v.length > 0 && v.length <= max
const isNullable = <T>(v: unknown, check: (x: unknown) => x is T): v is T | null => v === null || check(v)
const isNonNegativeInt = (v: unknown, max: number): v is number =>
  typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= max
const isPositiveInt = (v: unknown, max: number): v is number => isNonNegativeInt(v, max) && v >= 1

/** A share or remainder as the server writes it: a non-negative decimal text ("0" is a value). */
export const isQuantityText = (v: unknown): v is string =>
  typeof v === 'string' && v.length <= LIMITS.decimalText && NON_NEGATIVE_DECIMAL_RE.test(v)

/** A quantity the client may send: the wire grammar with a non-zero digit. */
export const isPositiveQuantityText = (v: unknown): v is string =>
  typeof v === 'string' && v.length <= LIMITS.decimalText && DECIMAL_RE.test(v) && /[1-9]/.test(v)

export type QuantityInputError = 'required' | 'invalid' | 'ambiguous-separator' | 'not-positive' | 'scale' | 'too-wide'

export type QuantityInput =
  | { readonly ok: true; readonly value: string }
  | { readonly ok: false; readonly error: QuantityInputError }

/**
 * Read what a person typed into a quantity field, without guessing and without a JS number.
 * `,` and `.` are both accepted as THE decimal separator; `1.000` with a short integer part is
 * refused as ambiguous (one thousand or one?). The client-side scale (≤ 3, Ö2's kg cap) and width
 * (≤ 15 integer digits, the column) checks only spare a round trip: the server decides (#350 P2-1).
 */
export function parseQuantityInput(raw: string): QuantityInput {
  const text = raw.trim()
  if (text.length === 0) return { ok: false, error: 'required' }
  const match = /^([0-9]+)(?:([.,])([0-9]+))?$/.exec(text)
  if (match === null) return { ok: false, error: 'invalid' }
  const integerDigits = match[1]
  const separator = match[2]
  const fractionDigits = match[3] ?? ''
  if (separator === '.' && fractionDigits.length === 3 && integerDigits.length <= 3) {
    return { ok: false, error: 'ambiguous-separator' }
  }
  const integer = integerDigits.replace(/^0+(?=\d)/, '')
  const fraction = fractionDigits.replace(/0+$/, '')
  const value = fraction.length > 0 ? `${integer}.${fraction}` : integer
  if (!/[1-9]/.test(value)) return { ok: false, error: 'not-positive' }
  if (fraction.length > LIMITS.kilogramScale) return { ok: false, error: 'scale' }
  if (integer.replace(/^0+/, '').length > LIMITS.integerDigits) return { ok: false, error: 'too-wide' }
  return { ok: true, value }
}

/** Exactly these members, all present. A missing nullable is as wrong as an extra one. */
function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  const own = Object.keys(obj)
  if (own.length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

/** At least these members present; others ignored. For the read-only Sales bodies the picker consumes. */
function atLeast(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

/* ── Decoding ─────────────────────────────────────────────────────────────── */

const SUMMARY_KEYS = [
  'id', 'companyId', 'status', 'version', 'customerPartyId', 'companyAccountId', 'effectiveOn',
  'createdAtUtc', 'updatedAtUtc', 'lineCount',
] as const

export function decodeInstructionSummary(value: unknown): InstructionSummary | null {
  const o = exact(value, SUMMARY_KEYS)
  if (
    o === null ||
    !isReferenceId(o.id) ||
    !isReferenceId(o.companyId) ||
    typeof o.status !== 'string' ||
    !INSTRUCTION_STATUSES.includes(o.status) ||
    !isVersionString(o.version) ||
    !isReferenceId(o.customerPartyId) ||
    !isReferenceId(o.companyAccountId) ||
    !isDateOnly(o.effectiveOn) ||
    !isInstant(o.createdAtUtc) ||
    !isInstant(o.updatedAtUtc) ||
    !isPositiveInt(o.lineCount, LIMITS.lines)
  ) {
    return null
  }
  return {
    id: o.id,
    companyId: o.companyId,
    status: o.status as InstructionStatus,
    version: o.version,
    customerPartyId: o.customerPartyId,
    companyAccountId: o.companyAccountId,
    effectiveOn: o.effectiveOn,
    createdAtUtc: o.createdAtUtc,
    updatedAtUtc: o.updatedAtUtc,
    lineCount: o.lineCount,
  }
}

const LINE_KEYS = [
  'lineId', 'lineNo', 'sourceOrderId', 'sourceOrderLineId', 'sourceOrderRevision', 'productId', 'unitId',
  'instructed', 'loaded', 'shipped', 'delivered', 'remainingToLoad', 'loadedNotShipped', 'shippedNotDelivered', 'isBlocked',
] as const

function decodeLine(value: unknown): InstructionLine | null {
  const o = exact(value, LINE_KEYS)
  if (
    o === null ||
    !isReferenceId(o.lineId) ||
    !isPositiveInt(o.lineNo, LIMITS.lines) ||
    !isReferenceId(o.sourceOrderId) ||
    !isReferenceId(o.sourceOrderLineId) ||
    !isNonNegativeInt(o.sourceOrderRevision, Number.MAX_SAFE_INTEGER) ||
    !isReferenceId(o.productId) ||
    !isReferenceId(o.unitId) ||
    !isPositiveQuantityText(o.instructed) ||
    !isQuantityText(o.loaded) ||
    !isQuantityText(o.shipped) ||
    !isQuantityText(o.delivered) ||
    !isQuantityText(o.remainingToLoad) ||
    !isQuantityText(o.loadedNotShipped) ||
    !isQuantityText(o.shippedNotDelivered) ||
    typeof o.isBlocked !== 'boolean'
  ) {
    return null
  }
  return {
    lineId: o.lineId,
    lineNo: o.lineNo,
    sourceOrderId: o.sourceOrderId,
    sourceOrderLineId: o.sourceOrderLineId,
    sourceOrderRevision: o.sourceOrderRevision,
    productId: o.productId,
    unitId: o.unitId,
    instructed: o.instructed,
    loaded: o.loaded,
    shipped: o.shipped,
    delivered: o.delivered,
    remainingToLoad: o.remainingToLoad,
    loadedNotShipped: o.loadedNotShipped,
    shippedNotDelivered: o.shippedNotDelivered,
    isBlocked: o.isBlocked,
  }
}

const SHARE_KEYS = ['shareId', 'lineId', 'kind', 'quantity', 'measurement', 'sourceShipmentId', 'recordedAtUtc'] as const

function decodeShare(value: unknown): LoadingShare | null {
  const o = exact(value, SHARE_KEYS)
  if (
    o === null ||
    !isReferenceId(o.shareId) ||
    !isReferenceId(o.lineId) ||
    typeof o.kind !== 'string' ||
    !SHARE_KINDS.includes(o.kind) ||
    !isPositiveQuantityText(o.quantity) ||
    !isNullable(o.measurement, (v): v is string => typeof v === 'string' && MEASUREMENTS.includes(v)) ||
    !isNullable(o.sourceShipmentId, isReferenceId) ||
    !isInstant(o.recordedAtUtc)
  ) {
    return null
  }
  // A load names how it was measured; a shipment names neither; a delivery names its source shipment.
  const kind = o.kind as ShareKind
  if (kind === SHARE_KIND.loaded && (o.measurement === null || o.sourceShipmentId !== null)) return null
  if (kind === SHARE_KIND.shipped && (o.measurement !== null || o.sourceShipmentId !== null)) return null
  if (kind === SHARE_KIND.delivered && (o.measurement !== null || o.sourceShipmentId === null)) return null
  return {
    shareId: o.shareId,
    lineId: o.lineId,
    kind,
    quantity: o.quantity,
    measurement: o.measurement as Measurement | null,
    sourceShipmentId: o.sourceShipmentId,
    recordedAtUtc: o.recordedAtUtc,
  }
}

/** `expectedId`: the record the caller asked for. A detail of another record is not an answer. */
export function decodeInstructionDetail(value: unknown, expectedId: string | null): InstructionDetail | null {
  const o = exact(value, ['instruction', 'lines', 'shares'])
  if (o === null) return null
  const instruction = decodeInstructionSummary(o.instruction)
  if (instruction === null || (expectedId !== null && instruction.id !== expectedId)) return null
  if (!Array.isArray(o.lines) || o.lines.length !== instruction.lineCount || !Array.isArray(o.shares)) return null
  const lines: InstructionLine[] = []
  const lineIds = new Set<string>()
  const lineNos = new Set<number>()
  for (const entry of o.lines) {
    const line = decodeLine(entry)
    if (line === null || lineIds.has(line.lineId) || lineNos.has(line.lineNo)) return null
    lineIds.add(line.lineId)
    lineNos.add(line.lineNo)
    lines.push(line)
  }
  const shares: LoadingShare[] = []
  const shareIds = new Set<string>()
  for (const entry of o.shares) {
    const share = decodeShare(entry)
    // A share of a line the instruction does not have is not this instruction's.
    if (share === null || shareIds.has(share.shareId) || !lineIds.has(share.lineId)) return null
    shareIds.add(share.shareId)
    shares.push(share)
  }
  return { instruction, lines, shares }
}

export function decodeInstructionMutation(value: unknown): InstructionMutation | null {
  const o = exact(value, ['detail', 'isReplay', 'originalCorrelationId'])
  if (o === null || typeof o.isReplay !== 'boolean' || !isText(o.originalCorrelationId, 200)) return null
  const detail = decodeInstructionDetail(o.detail, null)
  if (detail === null) return null
  return { detail, isReplay: o.isReplay, originalCorrelationId: o.originalCorrelationId }
}

export function decodeInstructionPage(value: unknown): InstructionPage | null {
  const o = exact(value, ['items', 'nextCursor'])
  if (o === null || !Array.isArray(o.items) || !isNullable(o.nextCursor, (v): v is string => isText(v, LIMITS.cursor))) {
    return null
  }
  const items: InstructionSummary[] = []
  const ids = new Set<string>()
  for (const entry of o.items) {
    const item = decodeInstructionSummary(entry)
    if (item === null || ids.has(item.id)) return null
    ids.add(item.id)
    items.push(item)
  }
  return { items, nextCursor: o.nextCursor }
}

/**
 * A problem body, read leniently: platform 401/403/404 may be plain ProblemDetails with extra
 * members and no `code`. Only what the contract names is taken; a body that is not a problem
 * at all yields null — the status still speaks.
 */
export function decodeProblem(value: unknown, status: number): LogisticsProblem | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const o = value as Obj
  if (o.status !== undefined && o.status !== status) return null
  const errors: LogisticsFieldError[] = []
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

/* ── The Sales reads (approved drafts) ───────────────────────────────────── */

/**
 * An APPROVED draft row as the customer select needs it. The Sales list is read with
 * `approvalState=Approved`; a row of any other state is refused here as well, so no picker ever
 * offers a line of a draft that is not approved. Extra members (the commercial ones) are ignored.
 */
export function decodeApprovedDraftSummary(value: unknown): ApprovedDraftSummary | null {
  const o = atLeast(value, ['id', 'companyId', 'documentNo', 'partyId', 'companyAccountId', 'version', 'approvalState'])
  if (
    o === null ||
    !isReferenceId(o.id) ||
    !isReferenceId(o.companyId) ||
    !isText(o.documentNo, 200) ||
    !isReferenceId(o.partyId) ||
    !isReferenceId(o.companyAccountId) ||
    typeof o.version !== 'string' ||
    !/^[1-9][0-9]*$/.test(o.version) ||
    o.approvalState !== SALES_APPROVED_STATE
  ) {
    return null
  }
  return {
    id: o.id,
    companyId: o.companyId,
    documentNo: o.documentNo,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    version: o.version,
  }
}

export function decodeApprovedDraftPage(value: unknown): ApprovedDraftPage | null {
  const o = exact(value, ['items', 'nextCursor'])
  if (o === null || !Array.isArray(o.items) || !isNullable(o.nextCursor, (v): v is string => isText(v, LIMITS.cursor))) {
    return null
  }
  const items: ApprovedDraftSummary[] = []
  const ids = new Set<string>()
  for (const entry of o.items) {
    const item = decodeApprovedDraftSummary(entry)
    if (item === null || ids.has(item.id)) return null
    ids.add(item.id)
    items.push(item)
  }
  return { items, nextCursor: o.nextCursor }
}

/**
 * The lines of one APPROVED draft, only the picker's members. The detail body's `draft` must be
 * the draft asked for, approved, and of the company it was read under. The money members of a
 * line (`unitPrice`, `priceUnitId`, `discountPercent`) and the header's currency are never read:
 * the result carries no such key.
 */
export function decodeApprovedDraftLines(
  value: unknown,
  expectedId: string,
  companyId: string,
): { readonly draft: ApprovedDraftSummary; readonly lines: readonly ApprovedDraftLine[] } | null {
  const o = atLeast(value, ['draft', 'lines'])
  if (o === null || !Array.isArray(o.lines)) return null
  const draft = decodeApprovedDraftSummary(o.draft)
  if (draft === null || draft.id !== expectedId || draft.companyId !== companyId) return null
  const lines: ApprovedDraftLine[] = []
  const ids = new Set<string>()
  for (const entry of o.lines) {
    const l = atLeast(entry, ['lineId', 'lineNo', 'productId', 'productName', 'orderUnitId', 'quantity'])
    if (
      l === null ||
      !isReferenceId(l.lineId) ||
      ids.has(l.lineId) ||
      !isPositiveInt(l.lineNo, LIMITS.lines) ||
      !isReferenceId(l.productId) ||
      !isText(l.productName, 500) ||
      !isReferenceId(l.orderUnitId) ||
      !isPositiveQuantityText(l.quantity)
    ) {
      return null
    }
    ids.add(l.lineId)
    lines.push({
      lineId: l.lineId,
      lineNo: l.lineNo,
      productId: l.productId,
      productName: l.productName,
      orderUnitId: l.orderUnitId,
      quantity: l.quantity,
    })
  }
  return { draft, lines }
}
