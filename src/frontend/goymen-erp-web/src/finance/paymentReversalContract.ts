/**
 * API-23: maker-checker reversal of a recorded manual bank payment — the strict decoders for
 * `PaymentReversalProposalView`, its page and its command result (contracts/openapi/goymen-erp-v1.json).
 * Any missing, extra or mistyped key, or a status whose decision fields disagree, is not an answer.
 */
import { isDateOnly } from './dateOnly'
import { isBoundUuid, isInstant, type CommandOutcomeName, type Decoded, type ReversalProposalStatus, type ValidationIssue } from './contract'

export interface PaymentReversalProposalView {
  readonly id: string
  readonly paymentId: string
  readonly occurredOn: string
  readonly reason: string
  readonly status: ReversalProposalStatus
  readonly proposedBy: string
  readonly proposedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
  /** The counter money event an approval created; null until approved. */
  readonly reversalEventId: string | null
  readonly version: string
}

export interface PaymentReversalProposalPage {
  readonly items: readonly PaymentReversalProposalView[]
}

export type PaymentReversalCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: PaymentReversalProposalView
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly ValidationIssue[]
    }
  | {
      readonly outcome: Exclude<CommandOutcomeName, 'Succeeded'>
      readonly resource: null
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly ValidationIssue[]
    }

export interface ProposePaymentReversalRequest {
  readonly paymentId: string
  readonly expectedVersion: string
  readonly occurredOn: string
  readonly reason: string
}

export interface ApprovePaymentReversalRequest {
  readonly proposalId: string
  readonly expectedVersion: string
}

export interface RejectPaymentReversalRequest {
  readonly proposalId: string
  readonly expectedVersion: string
  readonly reason: string
}

const FAIL = { ok: false } as const
const ok = <T,>(value: T): Decoded<T> => ({ ok: true, value })

const VIEW_KEYS = [
  'id', 'paymentId', 'occurredOn', 'reason', 'status', 'proposedBy', 'proposedAtUtc',
  'decidedBy', 'decidedAtUtc', 'decisionReason', 'reversalEventId', 'version',
] as const

const RESULT_KEYS = ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'] as const

const STATUSES: readonly string[] = ['Pending', 'Approved', 'Rejected']

const OUTCOMES: readonly string[] = [
  'Succeeded', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Unavailable',
]

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

const isTrimmedText = (value: unknown): value is string =>
  typeof value === 'string' && value.length > 0 && value.trim() === value

const isNullableText = (value: unknown): value is string | null => value === null || isTrimmedText(value)

export function decodePaymentReversalProposal(value: unknown): Decoded<PaymentReversalProposalView> {
  if (!isRecord(value) || !hasExactKeys(value, VIEW_KEYS)) return FAIL
  const { id, paymentId, occurredOn, reason, status, proposedBy, proposedAtUtc, decidedBy, decidedAtUtc,
    decisionReason, reversalEventId, version } = value
  if (!isBoundUuid(id) || !isBoundUuid(paymentId) || typeof occurredOn !== 'string' || !isDateOnly(occurredOn)
    || !isTrimmedText(reason) || typeof status !== 'string' || !STATUSES.includes(status)
    || !isTrimmedText(proposedBy) || !isInstant(proposedAtUtc) || !isNullableText(decidedBy)
    || !(decidedAtUtc === null || isInstant(decidedAtUtc)) || !isNullableText(decisionReason)
    || !(reversalEventId === null || isBoundUuid(reversalEventId)) || !isTrimmedText(version)) {
    return FAIL
  }
  const decided = decidedBy !== null && decidedAtUtc !== null
  const shapeHolds = status === 'Pending'
    ? decidedBy === null && decidedAtUtc === null && decisionReason === null && reversalEventId === null
    : status === 'Approved'
      ? decided && decisionReason === null && reversalEventId !== null
      : decided && decisionReason !== null && reversalEventId === null
  if (!shapeHolds) return FAIL
  return ok({
    id, paymentId, occurredOn, reason, status: status as ReversalProposalStatus, proposedBy,
    proposedAtUtc, decidedBy, decidedAtUtc, decisionReason, reversalEventId, version,
  })
}

/** A detail read answers ONE proposal; a well-formed body about another one is not that answer. */
export function decodePaymentReversalProposalFor(proposalId: string) {
  return (value: unknown): Decoded<PaymentReversalProposalView> => {
    const decoded = decodePaymentReversalProposal(value)
    return decoded.ok && decoded.value.id === proposalId ? decoded : FAIL
  }
}

export function decodePaymentReversalProposalPage(value: unknown): Decoded<PaymentReversalProposalPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['items']) || !Array.isArray(value.items)) return FAIL
  const items: PaymentReversalProposalView[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    const decoded = decodePaymentReversalProposal(raw)
    // The pending list holds pending proposals only, each once.
    if (!decoded.ok || decoded.value.status !== 'Pending' || seen.has(decoded.value.id)) return FAIL
    seen.add(decoded.value.id)
    items.push(decoded.value)
  }
  return ok({ items })
}

export function decodePaymentReversalCommandResult(value: unknown): Decoded<PaymentReversalCommandResult> {
  if (!isRecord(value) || !hasExactKeys(value, RESULT_KEYS)) return FAIL
  const { outcome, resource, isReplay, originalCorrelationId, issues: rawIssues } = value
  if (typeof outcome !== 'string' || !OUTCOMES.includes(outcome) || typeof isReplay !== 'boolean'
    || typeof originalCorrelationId !== 'string' || (isReplay && originalCorrelationId.length === 0)
    || !Array.isArray(rawIssues)) return FAIL
  const issues: ValidationIssue[] = []
  for (const raw of rawIssues) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['field', 'code'])
      || typeof raw.field !== 'string' || typeof raw.code !== 'string' || raw.code.length === 0) return FAIL
    issues.push({ field: raw.field, code: raw.code })
  }
  const common = { isReplay, originalCorrelationId, issues }
  if (outcome === 'Succeeded') {
    const view = decodePaymentReversalProposal(resource)
    return view.ok ? ok({ outcome: 'Succeeded', resource: view.value, ...common }) : FAIL
  }
  if (resource !== null) return FAIL
  return ok({ outcome: outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}
