/**
 * API-85a — the party credit limit master's wire shapes (F018 plan §L1; FNS-D27/D27a/D45), decoded strictly like
 * ./contract.ts: exact member sets, the verified shell company, bound identities, decimals as TEXT. An answer that
 * does not decode is never shown as data.
 *
 * Source: `PartyCreditLimitV1.cs` (Finance.Contracts). The wire is camelCase; enums are their names.
 *
 * - A record is one entry of an account's limit history: a Set carries a currency and an amount (> 0, exact text,
 *   never rounded); a Remove carries neither ("no limit" — never zero, never unlimited).
 * - `revision` on an account is the number the next proposal must name ("0" before the account ever
 *   had an approved record). An approved record carries the revision it made; a proposal carries none.
 * - `version` is the record's own concurrency token (canonical positive int64 text), named by approve/reject.
 * - FNS-D45: every record is a proposal decided by a second person. The wire still names `isDirect`, and it is always
 *   `false`; a record that says otherwise does not decode (an approver's one-step change no longer exists).
 * - The screen learns no actor id; only whether the pending proposal is the caller's own.
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
import { isPositiveDecimalText } from './decimalText'

export type CreditLimitKind = 'Set' | 'Remove'
export type CreditLimitStatus = 'Proposed' | 'Approved' | 'Rejected' | 'Superseded'

export interface CreditLimitRecord {
  readonly id: string
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly kind: CreditLimitKind
  readonly currencyId: string | null
  readonly amount: string | null
  readonly status: CreditLimitStatus
  readonly revision: string | null
  readonly rejectionReason: string | null
  readonly proposedAtUtc: string
  readonly decidedAtUtc: string | null
  readonly supersededAtUtc: string | null
  readonly version: string
}

export interface CreditLimitAccount {
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly effective: CreditLimitRecord | null
  readonly pending: CreditLimitRecord | null
  readonly revision: string
  readonly isOwnPending: boolean
}

export interface CreditLimitListPage {
  readonly companyId: string
  readonly items: readonly CreditLimitAccount[]
}

export interface CreditLimitHistoryPage {
  readonly companyId: string
  readonly companyAccountId: string
  readonly items: readonly CreditLimitRecord[]
}

export type CreditLimitCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: CreditLimitRecord
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

export type CreditLimitCurrencyCandidates = CurrencyCandidates

// ---------------------------------------------------------------------------------
// Requests (camelCase members of the C# request records; the company is never in a body)
// ---------------------------------------------------------------------------------

export interface CreditLimitProposeBody {
  readonly partyId: string
  readonly companyAccountId: string
  readonly kind: CreditLimitKind
  readonly currencyId: string | null
  readonly amount: string | null
  readonly expectedRevision: string
}

export interface CreditLimitApprovalBody {
  readonly expectedVersion: string
}

export interface CreditLimitRejectionBody {
  readonly expectedVersion: string
  readonly reason: string
}

// ---------------------------------------------------------------------------------
// Decoders
// ---------------------------------------------------------------------------------

const FAIL = { ok: false } as const
const ok = <T,>(value: T): Decoded<T> => ({ ok: true, value })

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

/** The account revision: "0" or a canonical positive int64. */
export function isRevisionText(value: unknown): value is string {
  return value === '0' || isPositiveInt64Text(value)
}

const RECORD_FIELDS = [
  'id', 'companyId', 'partyId', 'companyAccountId', 'kind', 'currencyId', 'amount', 'status', 'isDirect', 'revision',
  'rejectionReason', 'proposedAtUtc', 'decidedAtUtc', 'supersededAtUtc', 'version',
] as const

const ACCOUNT_FIELDS = ['companyId', 'partyId', 'companyAccountId', 'effective', 'pending', 'revision', 'isOwnPending'] as const

const KINDS: readonly string[] = ['Set', 'Remove']
const STATUSES: readonly string[] = ['Proposed', 'Approved', 'Rejected', 'Superseded']
const COMMAND_OUTCOMES: readonly string[] = [
  'Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded',
]

const isNullableInstant = (value: unknown): value is string | null => value === null || isInstant(value)

function decodeRecord(value: unknown, companyId: string): CreditLimitRecord | null {
  if (!isRecord(value) || !hasExactKeys(value, RECORD_FIELDS)) return null
  const {
    id, partyId, companyAccountId, kind, currencyId, amount, status, isDirect, revision, rejectionReason,
    proposedAtUtc, decidedAtUtc, supersededAtUtc, version,
  } = value
  if (value.companyId !== companyId) return null
  if (!isBoundUuid(id) || !isBoundUuid(partyId) || !isBoundUuid(companyAccountId)) return null
  if (typeof kind !== 'string' || !KINDS.includes(kind)) return null
  if (typeof status !== 'string' || !STATUSES.includes(status)) return null
  // FNS-D45: no record is ever a one-step change.
  if (isDirect !== false || !isPositiveInt64Text(version)) return null
  if (!isInstant(proposedAtUtc) || !isNullableInstant(decidedAtUtc) || !isNullableInstant(supersededAtUtc)) return null
  if (revision !== null && !isPositiveInt64Text(revision)) return null

  // A Set names one currency and a positive amount; a Remove names neither.
  if (kind === 'Set' ? !isBoundUuid(currencyId) || !isPositiveDecimalText(amount) : currencyId !== null || amount !== null) {
    return null
  }

  // Only a rejection carries a reason; the stage stamps must agree with the status.
  if (status === 'Rejected') {
    if (typeof rejectionReason !== 'string' || rejectionReason.trim().length === 0) return null
    if (revision !== null || decidedAtUtc === null || supersededAtUtc !== null) return null
  } else if (rejectionReason !== null) {
    return null
  }
  if (status === 'Proposed' && (revision !== null || decidedAtUtc !== null || supersededAtUtc !== null)) return null
  if (status === 'Approved' && (revision === null || decidedAtUtc === null || supersededAtUtc !== null)) return null
  if (status === 'Superseded' && supersededAtUtc === null) return null

  return {
    id,
    companyId,
    partyId,
    companyAccountId,
    kind: kind as CreditLimitKind,
    currencyId: currencyId as string | null,
    amount: amount as string | null,
    status: status as CreditLimitStatus,
    revision,
    rejectionReason: rejectionReason as string | null,
    proposedAtUtc,
    decidedAtUtc,
    supersededAtUtc,
    version,
  }
}

function decodeAccount(value: unknown, companyId: string): CreditLimitAccount | null {
  if (!isRecord(value) || !hasExactKeys(value, ACCOUNT_FIELDS)) return null
  const { partyId, companyAccountId, revision, isOwnPending } = value
  if (value.companyId !== companyId || !isBoundUuid(partyId) || !isBoundUuid(companyAccountId)) return null
  if (!isRevisionText(revision) || typeof isOwnPending !== 'boolean') return null

  const effective = value.effective === null ? null : decodeRecord(value.effective, companyId)
  const pending = value.pending === null ? null : decodeRecord(value.pending, companyId)
  if ((value.effective !== null && effective === null) || (value.pending !== null && pending === null)) return null
  // An account is shown only for a record it has.
  if (effective === null && pending === null) return null
  for (const record of [effective, pending]) {
    if (record !== null && (record.companyAccountId !== companyAccountId || record.partyId !== partyId)) return null
  }
  // The effective record is the account's latest approved one and names the account revision.
  if (effective !== null && (effective.status !== 'Approved' || effective.revision !== revision)) return null
  if (effective === null && revision !== '0') return null
  if (pending !== null && pending.status !== 'Proposed') return null
  if (isOwnPending && pending === null) return null

  return { companyId, partyId, companyAccountId, effective, pending, revision, isOwnPending }
}

/** One account, answering the account that was asked for. */
export function decodeCreditLimitAccount(value: unknown, companyId: string, companyAccountId: string): Decoded<CreditLimitAccount> {
  const account = decodeAccount(value, companyId)
  return account === null || account.companyAccountId !== companyAccountId ? FAIL : ok(account)
}

export function decodeCreditLimitListPage(value: unknown, companyId: string): Decoded<CreditLimitListPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  const items: CreditLimitAccount[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['account'])) return FAIL
    const account = decodeAccount(raw.account, companyId)
    if (account === null || seen.has(account.companyAccountId)) return FAIL
    seen.add(account.companyAccountId)
    items.push(account)
  }
  return ok({ companyId, items })
}

export function decodeCreditLimitHistoryPage(value: unknown, companyId: string,
  companyAccountId: string): Decoded<CreditLimitHistoryPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'companyAccountId', 'items'])) return FAIL
  if (value.companyId !== companyId || value.companyAccountId !== companyAccountId || !Array.isArray(value.items)) return FAIL
  const items: CreditLimitRecord[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    const record = decodeRecord(raw, companyId)
    if (record === null || record.companyAccountId !== companyAccountId || seen.has(record.id)) return FAIL
    seen.add(record.id)
    items.push(record)
  }
  // At most one approved record is effective at a time.
  if (items.filter((record) => record.status === 'Approved').length > 1) return FAIL
  return ok({ companyId, companyAccountId, items })
}

export function decodeCreditLimitCommandResult(value: unknown, companyId: string): Decoded<CreditLimitCommandResult> {
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
    const resource = decodeRecord(value.resource, companyId)
    // A success carries its record and no issue.
    return resource === null || issues.length > 0 ? FAIL : ok({ outcome: 'Succeeded', resource, ...common })
  }
  if (value.resource !== null) return FAIL
  return ok({ outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}

/** Settings' money currency candidates, as the bank-account master decodes them: as given, no default. */
export function decodeCreditLimitCurrencyCandidates(value: unknown, companyId: string): Decoded<CreditLimitCurrencyCandidates> {
  return decodeCurrencyCandidates(value, companyId)
}
