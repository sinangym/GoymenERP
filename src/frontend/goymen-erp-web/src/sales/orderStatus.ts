/**
 * DEC-SAL-001 / API-08 / UI-15: the order's seven status axes — the contract of
 * `GET /api/v1/sales/drafts/{id}/status` (`GetSalesOrderDraftStatusV1`, `SalesOrderDraftStatusResponse`, #832).
 *
 * The server answers every axis separately and derives no summary state; this file only reads what it answered. Rules
 * taken from the backend contract (`SalesDraftStatusV1.cs`, the Sales README "Order status axes"), not assumed:
 *  - every member is present, nullable ones as explicit null; unknown members, unknown states and numbers standing in for
 *    decimal text are refused — a body that does not fit is never shown as a status;
 *  - the answer is bound to the detail on screen: the same version, commercial revision, stored lifecycle, internal
 *    approval and customer acceptance, and every line of that detail exactly once with its own number and order unit;
 *  - one line shape per state (the contract's "one shape per state"): an `Unreadable` / `PendingReapproval` line carries
 *    no quantity beyond `ordered`; a `PolicyUnavailable` line carries the facts but no direction, deviation or band; a
 *    `NotStarted` line is `Under` by exactly `ordered`; `warning` only on a `Completed` warn-band line (SAT-D29/D31);
 *    `approvedOverDelivery` only on a `Completed` over-delivery (SAT-D30);
 *  - the tolerance (SAT-D5) was asked for the order's business date (SAT-D29) and is inside its own window;
 *  - `readyToClose` (SAT-D32) is true only when ALL seven axes are complete. `NoSource`, `Unreadable` and
 *    `PolicyUnavailable` are never complete, so a `true` beside any of them is a contradiction and refused.
 *
 * Decimal text stays text, never parsed into a JS number. A decoder returns null for anything it cannot vouch for; the
 * caller fails closed.
 */

import { APPROVAL_STATE, CANCELLED_STATE, CLOSED_STATE, DRAFT_STATE, salesDraftDetailPath, type ApprovalState, type DraftDetail } from './contract'
import { isDateOnly, isReferenceId } from './wirePrimitives'

/** API-08: the seven axes of one order (GET, under the draft read). */
export const salesDraftStatusPath = (id: string) => `${salesDraftDetailPath(id)}/status`

/** `SalesDraftStatusAxisStatesV1`: the stable wire names of every axis state. */
export const STATUS_AXIS_STATE = {
  noSource: 'NoSource',
  unreadable: 'Unreadable',
  policyUnavailable: 'PolicyUnavailable',
  notRequired: 'NotRequired',
  pending: 'Pending',
  accepted: 'Accepted',
  draft: 'Draft',
  active: 'Active',
  notStarted: 'NotStarted',
  partial: 'Partial',
  completed: 'Completed',
  outsideTolerance: 'OutsideTolerance',
  closedWithRemainder: 'ClosedWithRemainder',
  awaitingApproval: 'AwaitingApproval',
  pendingReapproval: 'PendingReapproval',
} as const

/** SAT-D32: the lifecycle axis — the stored Draft / Cancelled / Closed, and `Active` derived once the order was approved. */
export const LIFECYCLE_AXIS_STATES = [STATUS_AXIS_STATE.draft, STATUS_AXIS_STATE.active, CANCELLED_STATE, CLOSED_STATE] as const
export type LifecycleAxisState = (typeof LIFECYCLE_AXIS_STATES)[number]

/** The customer-acceptance axis of the CURRENT commercial revision ("Vazgeçildi" is never emitted: no declination exists). */
export const ACCEPTANCE_AXIS_STATES = [STATUS_AXIS_STATE.notRequired, STATUS_AXIS_STATE.pending, STATUS_AXIS_STATE.accepted] as const
export type AcceptanceAxisState = (typeof ACCEPTANCE_AXIS_STATES)[number]

/**
 * Source/allocation (MOD-05, API-11), invoicing (S5) and financial closure (MOD-04): their owner modules do not exist on
 * main, so the only state the contract answers for them is `NoSource` — never complete, never zero. A state an owner adds
 * later is refused here until this screen names it.
 */
export const OWNERLESS_AXIS_STATES = [STATUS_AXIS_STATE.noSource] as const
export type OwnerlessAxisState = (typeof OWNERLESS_AXIS_STATES)[number]

/** The realization axis and its lines (SAT-D5, SAT-D15–D17, SAT-D29–D31). */
export const REALIZATION_STATES = [
  STATUS_AXIS_STATE.notStarted,
  STATUS_AXIS_STATE.partial,
  STATUS_AXIS_STATE.completed,
  STATUS_AXIS_STATE.outsideTolerance,
  STATUS_AXIS_STATE.closedWithRemainder,
  STATUS_AXIS_STATE.awaitingApproval,
  STATUS_AXIS_STATE.pendingReapproval,
  STATUS_AXIS_STATE.policyUnavailable,
  STATUS_AXIS_STATE.unreadable,
] as const
export type RealizationState = (typeof REALIZATION_STATES)[number]

/** `|shipped − ordered|` is measured in this direction (`None` exactly on the ordered quantity). */
export const REALIZATION_DIRECTIONS = ['None', 'Under', 'Over'] as const
export type RealizationDirection = (typeof REALIZATION_DIRECTIONS)[number]

/** The tolerance band the deviation fell in (bounds inclusive, SAT-D15). */
export const REALIZATION_BANDS = ['Within', 'Warn', 'Approval', 'Block'] as const
export type RealizationBand = (typeof REALIZATION_BANDS)[number]

/** SAT-D5: the approved Settings delivery tolerance the realization was judged with, asked for the order's business date. */
export interface DeliveryTolerance {
  readonly profileId: string
  readonly profileVersion: number
  readonly warnPercent: string
  readonly approvalPercent: string
  readonly blockPercent: string
  readonly effectiveFrom: string
  readonly effectiveThrough: string | null
  readonly evaluatedOn: string
}

/** `SalesDraftLineRealizationV1`: one order line in its order unit; null members are "not read" or "not measured". */
export interface LineRealization {
  readonly lineId: string
  readonly lineNo: number
  readonly orderUnitId: string
  readonly ordered: string
  readonly shipped: string | null
  readonly delivered: string | null
  readonly state: RealizationState
  readonly direction: RealizationDirection | null
  readonly deviation: string | null
  readonly band: RealizationBand | null
  readonly warning: boolean
  readonly approvedOverDelivery: boolean
  readonly closedRemainder: string | null
  readonly approvedReturned: string | null
}

export interface RealizationAxis {
  readonly state: RealizationState
  readonly warning: boolean
  /** The revision whose approved quantities the lines were measured against ("Rev.N"); null only for `PendingReapproval`. */
  readonly basisRevision: string | null
  readonly tolerance: DeliveryTolerance | null
  readonly lines: readonly LineRealization[]
}

export interface OrderStatus {
  readonly draftVersion: string
  readonly revision: string
  readonly lifecycle: LifecycleAxisState
  readonly internalApproval: ApprovalState
  readonly customerAcceptance: { readonly state: AcceptanceAxisState; readonly acceptedRevision: string | null }
  readonly source: OwnerlessAxisState
  readonly realization: RealizationAxis
  readonly invoicing: OwnerlessAxisState
  readonly financialClosure: OwnerlessAxisState
  readonly readyToClose: boolean
}

/** The three axes whose owner module does not exist on main, in the order the card shows them. */
export const OWNERLESS_AXES = ['source', 'invoicing', 'financialClosure'] as const
export type OwnerlessAxis = (typeof OWNERLESS_AXES)[number]

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

const oneOf = <T extends string>(values: readonly T[], v: unknown): v is T =>
  typeof v === 'string' && (values as readonly string[]).includes(v)

/** Canonical wire decimal as the server writes it: no sign, exponent, padding, leading zeros or trailing fraction zeros. */
const DECIMAL_RE = /^(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?$/
/** Decimal text kept verbatim from the server (a quantity may carry its scale, e.g. "12.500"). */
const QUANTITY_RE = /^[0-9]+(?:\.[0-9]+)?$/
const REVISION_RE = /^Rev\.(?:0|[1-9][0-9]*)$/
/** The wire types are OpenAPI `int32` / `int64`; a JS number is exact only up to 2^53 − 1. */
const INT32_MAX = 2147483647
/** A decimal text longer than this is not a quantity this screen can vouch for (the Sales technical bound). */
const DECIMAL_TEXT_MAX = 128

const isQuantity = (v: unknown, positive: boolean): v is string =>
  typeof v === 'string' && v.length <= DECIMAL_TEXT_MAX && QUANTITY_RE.test(v) && (!positive || /[1-9]/.test(v))
const isNullableQuantity = (v: unknown): v is string | null => v === null || isQuantity(v, false)
const isPercent = (v: unknown): v is string => typeof v === 'string' && v.length <= DECIMAL_TEXT_MAX && DECIMAL_RE.test(v)
const isRevision = (v: unknown): v is string => typeof v === 'string' && REVISION_RE.test(v)
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
/** The same decimal value as TEXT (scale-insensitive: "6" and "6.000"), never through a JS number. Both are `isQuantity`. */
const canonicalDecimal = (text: string) => {
  const [whole, fraction = ''] = text.split('.')
  const trimmed = fraction.replace(/0+$/, '')
  return `${whole.replace(/^0+(?=\d)/, '')}${trimmed.length > 0 ? `.${trimmed}` : ''}`
}
const sameDecimal = (a: string, b: string) => canonicalDecimal(a) === canonicalDecimal(b)

/** The three states that are never complete (contract: "NoSource, Unreadable and PolicyUnavailable are never complete"). */
const NEVER_COMPLETE: readonly string[] = [STATUS_AXIS_STATE.noSource, STATUS_AXIS_STATE.unreadable, STATUS_AXIS_STATE.policyUnavailable]
/** Line states that appear only as the realization axis's own state. */
const AXIS_WIDE_LINE_STATES: readonly string[] = [STATUS_AXIS_STATE.unreadable, STATUS_AXIS_STATE.pendingReapproval]

function decodeTolerance(value: unknown, businessDate: string): DeliveryTolerance | null | undefined {
  if (value === null) return null
  const o = exact(value, ['profileId', 'profileVersion', 'warnPercent', 'approvalPercent', 'blockPercent', 'effectiveFrom',
    'effectiveThrough', 'evaluatedOn'])
  if (
    o === null || !isReferenceId(o.profileId) ||
    typeof o.profileVersion !== 'number' || !Number.isSafeInteger(o.profileVersion) || o.profileVersion < 1 ||
    !isPercent(o.warnPercent) || !isPercent(o.approvalPercent) || !isPercent(o.blockPercent) ||
    !isDateOnly(o.effectiveFrom) || !(o.effectiveThrough === null || isDateOnly(o.effectiveThrough)) ||
    !isDateOnly(o.evaluatedOn) ||
    // SAT-D29: the policy in force on the ORDER's business date, inside its own window (ISO dates compare as text).
    o.evaluatedOn !== businessDate || o.effectiveFrom > o.evaluatedOn ||
    (o.effectiveThrough !== null && o.effectiveThrough < o.evaluatedOn)
  ) return undefined
  return {
    profileId: o.profileId, profileVersion: o.profileVersion, warnPercent: o.warnPercent, approvalPercent: o.approvalPercent,
    blockPercent: o.blockPercent, effectiveFrom: o.effectiveFrom, effectiveThrough: o.effectiveThrough, evaluatedOn: o.evaluatedOn,
  }
}

const LINE_KEYS = ['lineId', 'lineNo', 'orderUnitId', 'ordered', 'shipped', 'delivered', 'state', 'direction', 'deviation',
  'band', 'warning', 'approvedOverDelivery', 'closedRemainder', 'approvedReturned'] as const

/** One line, in the one shape its state allows; null when it does not fit. */
function decodeLine(value: unknown): LineRealization | null {
  const o = exact(value, LINE_KEYS)
  if (
    o === null || !isReferenceId(o.lineId) ||
    typeof o.lineNo !== 'number' || !Number.isInteger(o.lineNo) || o.lineNo < 1 || o.lineNo > INT32_MAX ||
    !isReferenceId(o.orderUnitId) || !isQuantity(o.ordered, true) || !oneOf(REALIZATION_STATES, o.state) ||
    !isNullableQuantity(o.shipped) || !isNullableQuantity(o.delivered) ||
    !(o.direction === null || oneOf(REALIZATION_DIRECTIONS, o.direction)) || !isNullableQuantity(o.deviation) ||
    !(o.band === null || oneOf(REALIZATION_BANDS, o.band)) ||
    typeof o.warning !== 'boolean' || typeof o.approvedOverDelivery !== 'boolean' ||
    !isNullableQuantity(o.closedRemainder) || !isNullableQuantity(o.approvedReturned)
  ) return null
  const line: LineRealization = {
    lineId: o.lineId, lineNo: o.lineNo, orderUnitId: o.orderUnitId, ordered: o.ordered, shipped: o.shipped,
    delivered: o.delivered, state: o.state, direction: o.direction, deviation: o.deviation, band: o.band,
    warning: o.warning, approvedOverDelivery: o.approvedOverDelivery, closedRemainder: o.closedRemainder,
    approvedReturned: o.approvedReturned,
  }
  const unmeasured = line.direction === null && line.deviation === null && line.band === null
  const unmarked = !line.warning && !line.approvedOverDelivery
  switch (line.state) {
    case STATUS_AXIS_STATE.unreadable:
    case STATUS_AXIS_STATE.pendingReapproval:
      // Nothing was read or judged: only the identity and the quantity it would be measured against.
      return unmeasured && unmarked && line.shipped === null && line.delivered === null &&
        line.closedRemainder === null && line.approvedReturned === null ? line : null
    case STATUS_AXIS_STATE.policyUnavailable:
      // No approved tolerance: the facts stay visible, nothing is judged.
      return unmeasured && unmarked && line.shipped !== null && line.delivered !== null ? line : null
    case STATUS_AXIS_STATE.notStarted:
      // One shape in every branch: "Under" by exactly the ordered quantity, no band.
      return line.shipped !== null && line.delivered !== null && line.direction === 'Under' &&
        line.deviation !== null && sameDecimal(line.deviation, line.ordered) && line.band === null && unmarked ? line : null
    default:
      if (line.shipped === null || line.delivered === null || line.direction === null || line.deviation === null) return null
      // SAT-D29 / SAT-D31: the warning marks a line counted Completed in spite of a warn-band deviation.
      if (line.warning && (line.state !== STATUS_AXIS_STATE.completed || line.band !== 'Warn')) return null
      // SAT-D30: an approved approval-band over-delivery is Completed.
      if (line.approvedOverDelivery && (line.state !== STATUS_AXIS_STATE.completed || line.direction !== 'Over' || line.warning)) return null
      return line
  }
}

/** The realization axis bound to `detail`; null when it does not fit. */
function decodeRealization(value: unknown, detail: DraftDetail): RealizationAxis | null {
  const o = exact(value, ['state', 'warning', 'basisRevision', 'tolerance', 'lines'])
  if (
    o === null || !oneOf(REALIZATION_STATES, o.state) || typeof o.warning !== 'boolean' ||
    !(o.basisRevision === null || isRevision(o.basisRevision)) || !Array.isArray(o.lines) ||
    o.lines.length !== detail.lines.length
  ) return null
  const state = o.state
  const tolerance = decodeTolerance(o.tolerance, detail.draft.businessDate)
  if (tolerance === undefined) return null
  // Only PendingReapproval has no measurement basis.
  if ((state === STATUS_AXIS_STATE.pendingReapproval) !== (o.basisRevision === null)) return null

  const answered = new Map<string, LineRealization>()
  for (const entry of o.lines) {
    const line = decodeLine(entry)
    if (line === null || answered.has(line.lineId.toLowerCase())) return null
    answered.set(line.lineId.toLowerCase(), line)
  }
  const lines: LineRealization[] = []
  for (const own of detail.lines) {
    const line = answered.get(own.lineId.toLowerCase())
    if (line === undefined || line.lineNo !== own.lineNo || !sameId(line.orderUnitId, own.orderUnitId)) return null
    // Measured against the current quantities (or, with no approved basis, shown with them): `ordered` is the line's own.
    if ((o.basisRevision === null || o.basisRevision === detail.draft.revision) && line.ordered !== own.quantity) return null
    lines.push(line)
  }

  const states = lines.map((line) => line.state)
  if (AXIS_WIDE_LINE_STATES.includes(state)) {
    // The whole axis is unread or unjudged: every line says the same, no tolerance, no warning.
    if (o.warning || tolerance !== null || states.some((s) => s !== state)) return null
  } else if (state === STATUS_AXIS_STATE.policyUnavailable) {
    // No approved tolerance: the lines that needed one say so, the others had nothing to judge.
    if (
      o.warning || tolerance !== null || !states.includes(STATUS_AXIS_STATE.policyUnavailable) ||
      states.some((s) => s !== STATUS_AXIS_STATE.policyUnavailable && s !== STATUS_AXIS_STATE.notStarted)
    ) return null
  } else {
    if (states.some((s) => AXIS_WIDE_LINE_STATES.includes(s) || s === STATUS_AXIS_STATE.policyUnavailable)) return null
    // The axis warning is exactly "some line carries the warning".
    if (o.warning !== lines.some((line) => line.warning)) return null
    // A judged deviation needs the tolerance it was judged with.
    if (lines.some((line) => line.band !== null) && tolerance === null) return null
  }
  return { state, warning: o.warning, basisRevision: o.basisRevision, tolerance, lines }
}

function decodeOwnerless(value: unknown): OwnerlessAxisState | null {
  const o = exact(value, ['state'])
  return o !== null && oneOf(OWNERLESS_AXIS_STATES, o.state) ? o.state : null
}

/**
 * API-08: `SalesOrderDraftStatusResponse`, read exactly — and only as the status OF `detail` (see the file comment).
 * Null when anything does not fit; the screen then shows no axis at all.
 */
export function decodeOrderStatus(value: unknown, detail: DraftDetail): OrderStatus | null {
  const o = exact(value, ['draftVersion', 'revision', 'lifecycle', 'internalApproval', 'customerAcceptance', 'source',
    'realization', 'invoicing', 'financialClosure', 'readyToClose'])
  if (
    o === null || typeof o.draftVersion !== 'string' || o.draftVersion !== detail.draft.version ||
    o.revision !== detail.draft.revision || typeof o.readyToClose !== 'boolean'
  ) return null

  const lifecycleAxis = exact(o.lifecycle, ['state'])
  if (lifecycleAxis === null || !oneOf(LIFECYCLE_AXIS_STATES, lifecycleAxis.state)) return null
  const lifecycle = lifecycleAxis.state
  // The stored lifecycle is the detail's; `Active` is derived from a stored `Draft` only.
  const stored = lifecycle === STATUS_AXIS_STATE.active ? DRAFT_STATE : lifecycle
  if (stored !== detail.draft.state) return null

  const approvalAxis = exact(o.internalApproval, ['state'])
  if (approvalAxis === null || approvalAxis.state !== detail.draft.approvalState || !oneOf(Object.values(APPROVAL_STATE), approvalAxis.state)) return null

  const acceptance = exact(o.customerAcceptance, ['state', 'acceptedRevision'])
  const facts = detail.customerAcceptance
  if (
    acceptance === null || !oneOf(ACCEPTANCE_AXIS_STATES, acceptance.state) ||
    !(acceptance.acceptedRevision === null || isRevision(acceptance.acceptedRevision)) ||
    acceptance.acceptedRevision !== facts.acceptedRevision ||
    acceptance.state !== (!facts.required ? STATUS_AXIS_STATE.notRequired
      : facts.isCurrentRevisionAccepted ? STATUS_AXIS_STATE.accepted : STATUS_AXIS_STATE.pending)
  ) return null

  const source = decodeOwnerless(o.source)
  const invoicing = decodeOwnerless(o.invoicing)
  const financialClosure = decodeOwnerless(o.financialClosure)
  const realization = decodeRealization(o.realization, detail)
  if (source === null || invoicing === null || financialClosure === null || realization === null) return null

  // SAT-D32: "kapanmaya hazır" only when every axis is complete — never beside an axis that cannot be.
  if (o.readyToClose && [source, invoicing, financialClosure, realization.state].some((s) => NEVER_COMPLETE.includes(s))) return null

  return {
    draftVersion: o.draftVersion,
    revision: o.revision,
    lifecycle,
    internalApproval: approvalAxis.state,
    customerAcceptance: { state: acceptance.state, acceptedRevision: acceptance.acceptedRevision },
    source,
    realization,
    invoicing,
    financialClosure,
    readyToClose: o.readyToClose,
  }
}
