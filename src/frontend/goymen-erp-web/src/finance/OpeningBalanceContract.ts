/**
 * FIN-S9 — opening balance wire shapes (FNS-D23), decoded strictly like ./CollateralContract.ts: exact member sets, the
 * verified shell company, bound identities, decimals as TEXT. An answer that does not decode is never shown as data.
 *
 * Source: `OpeningBalanceV1.cs` (Finance.Contracts). The wire is camelCase; enums are their names; a `DateOnly` is
 * `YYYY-MM-DD`.
 *
 * - An opening balance is entered only while the Settings opening period is Open. A specialist proposes it, a manager
 *   approves or rejects it, never its proposer (`isOwnProposal` hides the decision controls; the server refuses anyway).
 * - A cash or bank opening names no party and no direction; a party (cari) opening names both.
 * - `version` is the row's concurrency token (a positive long as text) that a decision must name.
 * - The screen learns no actor id.
 * - FNS-D84/D85: an approval writes the one opening movement; its correction is a reversal (proposed, then decided by
 *   another person) that marks the opening `Reversed` and lets a new proposal be entered. `reversal` is the opening's
 *   latest reversal: only an approved opening may carry a pending or rejected one, and only a reversed one an approved one.
 */

import { decodeCurrencyCandidates, type CurrencyCandidates } from './bankAccountContract'
import {
  isBoundUuid,
  isInstant,
  isPositiveInt64Text,
  type CommandOutcomeName,
  type Decoded,
  type ValidationIssue,
} from './contract'
import { isDateOnly } from './dateOnly'
import { isPositiveDecimalText } from './decimalText'

export type OpeningBalanceTargetKind = 'CashAccount' | 'BankAccount' | 'PartyAccount'
export type OpeningBalanceDirection = 'Debit' | 'Credit'
export type OpeningBalanceStatus = 'Proposed' | 'Approved' | 'Rejected' | 'Reversed'
export type OpeningBalanceReversalStatus = 'Proposed' | 'Approved' | 'Rejected'

export interface OpeningBalanceReversalView {
  readonly id: string
  readonly status: OpeningBalanceReversalStatus
  readonly reason: string
  readonly openingPeriodVersion: string
  readonly isOwnProposal: boolean
  readonly proposedAtUtc: string
  readonly decidedAtUtc: string | null
  readonly rejectionReason: string | null
  readonly version: string
}
export type OpeningPeriodState = 'Unavailable' | 'Open' | 'Closed'

export interface OpeningBalanceView {
  readonly id: string
  readonly companyId: string
  readonly targetKind: OpeningBalanceTargetKind
  readonly targetId: string
  readonly partyId: string | null
  readonly currencyId: string
  readonly direction: OpeningBalanceDirection | null
  readonly amount: string
  readonly asOfDate: string
  readonly status: OpeningBalanceStatus
  readonly openingPeriodVersion: string
  readonly isOwnProposal: boolean
  readonly proposedAtUtc: string
  readonly decidedAtUtc: string | null
  readonly rejectionReason: string | null
  readonly version: string
  readonly reversal: OpeningBalanceReversalView | null
}

export interface OpeningBalanceListPage {
  readonly companyId: string
  readonly items: readonly OpeningBalanceView[]
}

export interface OpeningPeriodView {
  readonly companyId: string
  readonly state: OpeningPeriodState
  readonly version: string | null
}

export type OpeningBalanceCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: OpeningBalanceView
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

export type OpeningBalanceCurrencyCandidates = CurrencyCandidates

export interface OpeningBalanceProposeBody {
  readonly targetKind: OpeningBalanceTargetKind
  readonly targetId: string
  readonly partyId: string | null
  readonly currencyId: string
  readonly direction: OpeningBalanceDirection | null
  readonly amount: string
  readonly asOfDate: string
}

export interface OpeningBalanceApproveBody {
  readonly expectedVersion: string
}

export interface OpeningBalanceRejectBody {
  readonly expectedVersion: string
  readonly reason: string
}

/** The reversal proposal names the opening's version; its approval and rejection name the reversal's version. */
export interface OpeningBalanceReversalProposeBody {
  readonly expectedVersion: string
  readonly reason: string
}

const FAIL = { ok: false } as const
const ok = <T,>(value: T): Decoded<T> => ({ ok: true, value })

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

/** Text the server keeps as given: not blank and not padded. */
const isCanonicalText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value === value.trim()

const VIEW_FIELDS = [
  'id', 'companyId', 'targetKind', 'targetId', 'partyId', 'currencyId', 'direction', 'amount', 'asOfDate', 'status',
  'openingPeriodVersion', 'isOwnProposal', 'proposedAtUtc', 'decidedAtUtc', 'rejectionReason', 'version', 'reversal',
] as const

const REVERSAL_FIELDS = [
  'id', 'status', 'reason', 'openingPeriodVersion', 'isOwnProposal', 'proposedAtUtc', 'decidedAtUtc', 'rejectionReason',
  'version',
] as const

const TARGET_KINDS: readonly string[] = ['CashAccount', 'BankAccount', 'PartyAccount']
const DIRECTIONS: readonly string[] = ['Debit', 'Credit']
const STATUSES: readonly string[] = ['Proposed', 'Approved', 'Rejected', 'Reversed']
const REVERSAL_STATUSES: readonly string[] = ['Proposed', 'Approved', 'Rejected']
const PERIOD_STATES: readonly string[] = ['Unavailable', 'Open', 'Closed']
const COMMAND_OUTCOMES: readonly string[] = [
  'Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded',
]

function decodeReversal(value: unknown): OpeningBalanceReversalView | null {
  if (!isRecord(value) || !hasExactKeys(value, REVERSAL_FIELDS)) return null
  if (
    !isBoundUuid(value.id) ||
    typeof value.status !== 'string' || !REVERSAL_STATUSES.includes(value.status) ||
    !isCanonicalText(value.reason) ||
    !isCanonicalText(value.openingPeriodVersion) ||
    typeof value.isOwnProposal !== 'boolean' ||
    !isInstant(value.proposedAtUtc) ||
    !isPositiveInt64Text(value.version)
  ) {
    return null
  }
  if (value.status === 'Proposed') {
    if (value.decidedAtUtc !== null || value.rejectionReason !== null) return null
  } else {
    if (!isInstant(value.decidedAtUtc)) return null
    if (value.status === 'Approved' ? value.rejectionReason !== null : !isCanonicalText(value.rejectionReason)) {
      return null
    }
  }
  return {
    id: value.id,
    status: value.status as OpeningBalanceReversalStatus,
    reason: value.reason,
    openingPeriodVersion: value.openingPeriodVersion,
    isOwnProposal: value.isOwnProposal,
    proposedAtUtc: value.proposedAtUtc as string,
    decidedAtUtc: value.decidedAtUtc as string | null,
    rejectionReason: value.rejectionReason as string | null,
    version: value.version,
  }
}

function decodeView(value: unknown, companyId: string): OpeningBalanceView | null {
  if (!isRecord(value) || !hasExactKeys(value, VIEW_FIELDS)) return null
  if (
    !isBoundUuid(value.id) ||
    value.companyId !== companyId ||
    typeof value.targetKind !== 'string' || !TARGET_KINDS.includes(value.targetKind) ||
    !isBoundUuid(value.targetId) ||
    !isBoundUuid(value.currencyId) ||
    !isPositiveDecimalText(value.amount) ||
    typeof value.asOfDate !== 'string' || !isDateOnly(value.asOfDate) ||
    typeof value.status !== 'string' || !STATUSES.includes(value.status) ||
    !isCanonicalText(value.openingPeriodVersion) ||
    typeof value.isOwnProposal !== 'boolean' ||
    !isInstant(value.proposedAtUtc) ||
    !isPositiveInt64Text(value.version)
  ) {
    return null
  }
  // A party opening names its party and direction; a cash or bank opening names neither.
  const party = value.targetKind === 'PartyAccount'
  if (party) {
    if (!isBoundUuid(value.partyId) || typeof value.direction !== 'string' || !DIRECTIONS.includes(value.direction)) {
      return null
    }
  } else if (value.partyId !== null || value.direction !== null) {
    return null
  }
  // A proposal carries no decision; a decision carries its time, and only a rejection carries a reason.
  if (value.status === 'Proposed') {
    if (value.decidedAtUtc !== null || value.rejectionReason !== null) return null
  } else {
    if (!isInstant(value.decidedAtUtc)) return null
    if (value.status === 'Rejected' ? !isCanonicalText(value.rejectionReason) : value.rejectionReason !== null) {
      return null
    }
  }
  // Only an approved opening carries a pending or rejected reversal; a reversed one carries its approved reversal.
  let reversal: OpeningBalanceReversalView | null = null
  if (value.reversal !== null) {
    reversal = decodeReversal(value.reversal)
    if (reversal === null) return null
  }
  if (value.status === 'Reversed' ? reversal?.status !== 'Approved'
    : value.status === 'Approved' ? reversal?.status === 'Approved' : reversal !== null) {
    return null
  }
  return {
    id: value.id,
    companyId,
    targetKind: value.targetKind as OpeningBalanceTargetKind,
    targetId: value.targetId,
    partyId: party ? value.partyId as string : null,
    currencyId: value.currencyId,
    direction: party ? value.direction as OpeningBalanceDirection : null,
    amount: value.amount,
    asOfDate: value.asOfDate,
    status: value.status as OpeningBalanceStatus,
    openingPeriodVersion: value.openingPeriodVersion,
    isOwnProposal: value.isOwnProposal,
    proposedAtUtc: value.proposedAtUtc as string,
    decidedAtUtc: value.decidedAtUtc as string | null,
    rejectionReason: value.rejectionReason as string | null,
    version: value.version,
    reversal,
  }
}

export function decodeOpeningBalance(value: unknown, companyId: string): Decoded<OpeningBalanceView> {
  const view = decodeView(value, companyId)
  return view === null ? FAIL : ok(view)
}

export function decodeOpeningBalanceListPage(value: unknown, companyId: string): Decoded<OpeningBalanceListPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  const items: OpeningBalanceView[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    const view = decodeView(raw, companyId)
    if (view === null || seen.has(view.id)) return FAIL
    seen.add(view.id)
    items.push(view)
  }
  return ok({ companyId, items })
}

/** Open and Closed carry Settings' key version; Unavailable carries none. */
export function decodeOpeningPeriod(value: unknown, companyId: string): Decoded<OpeningPeriodView> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'state', 'version'])) return FAIL
  if (value.companyId !== companyId || typeof value.state !== 'string' || !PERIOD_STATES.includes(value.state)) {
    return FAIL
  }
  if (value.state === 'Unavailable' ? value.version !== null : !isCanonicalText(value.version)) return FAIL
  return ok({ companyId, state: value.state as OpeningPeriodState, version: value.version as string | null })
}

export function decodeOpeningBalanceCommandResult(value: unknown,
  companyId: string): Decoded<OpeningBalanceCommandResult> {
  if (!isRecord(value) || !hasExactKeys(value, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])) {
    return FAIL
  }
  if (
    typeof value.outcome !== 'string' ||
    !COMMAND_OUTCOMES.includes(value.outcome) ||
    typeof value.isReplay !== 'boolean' ||
    typeof value.originalCorrelationId !== 'string' ||
    (value.isReplay && value.originalCorrelationId.length === 0) ||
    !Array.isArray(value.issues)
  ) {
    return FAIL
  }
  const issues: ValidationIssue[] = []
  for (const raw of value.issues) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['field', 'code'])) return FAIL
    if (typeof raw.field !== 'string' || typeof raw.code !== 'string' || raw.code.length === 0) return FAIL
    issues.push({ field: raw.field, code: raw.code })
  }
  const common = { isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId, issues }
  if (value.outcome === 'Succeeded') {
    const resource = decodeView(value.resource, companyId)
    return resource === null || issues.length > 0 ? FAIL : ok({ outcome: 'Succeeded', resource, ...common })
  }
  if (value.resource !== null) return FAIL
  return ok({ outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}

/** Settings' money currency candidates, as the bank-account master decodes them: as given, no default. */
export function decodeOpeningBalanceCurrencyCandidates(value: unknown,
  companyId: string): Decoded<OpeningBalanceCurrencyCandidates> {
  return decodeCurrencyCandidates(value, companyId)
}
