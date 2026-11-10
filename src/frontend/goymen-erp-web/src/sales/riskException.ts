/**
 * API-07 (EK21 SAT-D153…D162; Sinan 2026-11-10): the single-shipment risk exception wire contract of an order, decoded strictly.
 * An answer counts only with exactly the documented keys, a known state, and a decision that agrees with that state (an approval
 * names its decider and its Istanbul business date, a rejection its decider and reason, a request and a withdrawal neither). Anything else is
 * refused as a whole (`null`), never partly shown. No field carries an amount (SAT-D162).
 */
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

export const RISK_EXCEPTION_STATE = {
  requested: 'Requested', approved: 'Approved', rejected: 'Rejected', withdrawn: 'Withdrawn',
} as const
export type RiskExceptionState = (typeof RISK_EXCEPTION_STATE)[keyof typeof RISK_EXCEPTION_STATE]

/** The server's reason bound (`SalesShipmentRiskException.MaximumReasonLength`); a longer reason is refused, never cut. */
export const RISK_EXCEPTION_REASON_MAX = 4000

export interface RiskExceptionRequest {
  readonly loadingInstructionId: string
  readonly reason: string
}

export interface RiskExceptionDecisionRequest {
  readonly expectedVersion: string
  readonly reason: string | null
}

/** EK56 SAT-D286: `withdrawal` is the requester's own command, not a decision; it shares the route shape and the body. */
export type RiskExceptionDecision = 'approval' | 'rejection' | 'withdrawal'

export interface RiskException {
  readonly orderId: string
  readonly exceptionNo: number
  readonly loadingInstructionId: string
  readonly state: RiskExceptionState
  readonly version: string
  readonly reason: string
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly rejectionReason: string | null
  readonly validOn: string | null
  readonly isValidToday: boolean
}

export const riskExceptionsPath = (draftId: string) => `/sales/drafts/${encodeURIComponent(draftId)}/risk-exceptions`
export const riskExceptionDecisionPath = (draftId: string, exceptionNo: number, decision: RiskExceptionDecision) =>
  `${riskExceptionsPath(draftId)}/${exceptionNo}/${decision}`

type Obj = Record<string, unknown>

const KEYS = [
  'orderId', 'exceptionNo', 'loadingInstructionId', 'state', 'version', 'reason', 'requestedBy', 'requestedAtUtc',
  'decidedBy', 'decidedAtUtc', 'rejectionReason', 'validOn', 'isValidToday',
] as const

const exact = (value: unknown, keys: readonly string[]): Obj | null => {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const object = value as Obj
  const actual = Object.keys(object)
  return actual.length === keys.length && actual.every((key) => keys.includes(key)) ? object : null
}
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const instant = (value: unknown): value is string =>
  typeof value === 'string' && value.trim() !== '' && !Number.isNaN(Date.parse(value))
const states: readonly string[] = Object.values(RISK_EXCEPTION_STATE)

/** One exception of `orderId`; null unless every key is present, typed and consistent with the state. */
export function decodeRiskException(value: unknown, orderId: string): RiskException | null {
  const o = exact(value, KEYS)
  if (o === null || !isReferenceId(o.orderId) || o.orderId.toLowerCase() !== orderId.toLowerCase()
    || typeof o.exceptionNo !== 'number' || !Number.isInteger(o.exceptionNo) || o.exceptionNo < 1
    || !isReferenceId(o.loadingInstructionId) || typeof o.state !== 'string' || !states.includes(o.state)
    || !isVersionString(o.version) || !text(o.reason) || o.reason.length > RISK_EXCEPTION_REASON_MAX
    || !text(o.requestedBy) || !instant(o.requestedAtUtc) || typeof o.isValidToday !== 'boolean') return null
  const state = o.state as RiskExceptionState
  const undecided = o.decidedBy === null && o.decidedAtUtc === null && o.rejectionReason === null && o.validOn === null
  const decided = text(o.decidedBy) && instant(o.decidedAtUtc)
    // SAT-D153: the decider is never the requester (the server refuses it; an answer saying otherwise is not believed).
    && o.decidedBy.trim() !== o.requestedBy.trim()
  const consistent = state === RISK_EXCEPTION_STATE.requested
    ? undecided && o.version === '1' && !o.isValidToday
    : state === RISK_EXCEPTION_STATE.approved
      ? decided && o.rejectionReason === null && isDateOnly(o.validOn) && o.version === '2'
      : state === RISK_EXCEPTION_STATE.rejected
        ? decided && text(o.rejectionReason) && o.validOn === null && o.version === '2' && !o.isValidToday
        // EK56 SAT-D286: a withdrawal records no decision; the withdrawer is the requester by rule.
        : undecided && o.version === '2' && !o.isValidToday
  if (!consistent) return null
  return {
    orderId: o.orderId,
    exceptionNo: o.exceptionNo,
    loadingInstructionId: o.loadingInstructionId,
    state,
    version: o.version,
    reason: o.reason,
    requestedBy: o.requestedBy,
    requestedAtUtc: o.requestedAtUtc,
    decidedBy: o.decidedBy as string | null,
    decidedAtUtc: o.decidedAtUtc as string | null,
    rejectionReason: o.rejectionReason as string | null,
    validOn: o.validOn as string | null,
    isValidToday: o.isValidToday,
  }
}

/** The order's list (newest first); null when the envelope or any item is refused, or two items share a number. */
export function decodeRiskExceptionList(value: unknown, orderId: string): readonly RiskException[] | null {
  const o = exact(value, ['items'])
  if (o === null || !Array.isArray(o.items)) return null
  const items: RiskException[] = []
  for (const raw of o.items) {
    const item = decodeRiskException(raw, orderId)
    if (item === null || items.some((seen) => seen.exceptionNo === item.exceptionNo)) return null
    items.push(item)
  }
  return items
}
