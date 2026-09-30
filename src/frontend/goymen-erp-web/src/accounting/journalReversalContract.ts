/**
 * ACC-V1 journal reversal wire contract (Business #66/5827281417): two separate actions.
 *  - propose: `POST /accounting/journal-reversal-proposals` → 201 `AccountingJournalReversalProposalViewV1`,
 *    a durable PENDING proposal; the original journal is unchanged and no reversal journal exists yet;
 *  - approve: `POST /accounting/journal-reversal-proposals/{id}/approval` `{expectedVersion}` → 200, by an actor
 *    OTHER than the proposer; it posts exactly one reversal journal.
 *
 * The decoder is strict: exact keys, the bound company, known status and a status-consistent shape. Anything
 * else is `null` and the screen says so; it never guesses a field.
 */

import { isDateOnly } from '../finance/dateOnly'

export const JOURNAL_REVERSAL_STATUSES = ['Pending', 'Approved', 'Rejected'] as const
export type JournalReversalStatus = (typeof JOURNAL_REVERSAL_STATUSES)[number]

/** Server `AccountingJournalReversalPolicyV1.ReasonMaximumLength`, counted in UTF-16 units as the server does. */
export const JOURNAL_REVERSAL_REASON_MAX = 1024

export interface JournalReversalProposal {
  readonly proposalId: string
  readonly companyId: string
  readonly sourceJournalId: string
  readonly reversalJournalId: string | null
  readonly periodId: string
  readonly transactionDate: string
  readonly accountingDate: string
  readonly valueDate: string | null
  readonly reason: string
  readonly proposedBy: string
  readonly proposedAtUtc: string
  readonly status: JournalReversalStatus
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
  readonly version: number
}

export interface ProposeJournalReversalRequest {
  readonly proposalId: string
  readonly sourceJournalId: string
  readonly periodId: string
  readonly transactionDate: string
  readonly accountingDate: string
  readonly valueDate: string | null
  readonly reason: string
}

const KEYS = [
  'proposalId',
  'companyId',
  'sourceJournalId',
  'reversalJournalId',
  'periodId',
  'transactionDate',
  'accountingDate',
  'valueDate',
  'reason',
  'proposedBy',
  'proposedAtUtc',
  'status',
  'decidedBy',
  'decidedAtUtc',
  'decisionReason',
  'version',
] as const

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = /^0{8}-0{4}-0{4}-0{4}-0{12}$/
const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

export function isReversalGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID_RE.test(value) && !EMPTY_GUID.test(value)
}

const nullableGuid = (value: unknown): value is string | null => value === null || isReversalGuid(value)
const date = (value: unknown): value is string => typeof value === 'string' && isDateOnly(value)
const instant = (value: unknown): value is string =>
  typeof value === 'string' && INSTANT_RE.test(value) && !Number.isNaN(Date.parse(value))
const nullableInstant = (value: unknown): value is string | null => value === null || instant(value)

/** Same identity, ignoring case: the server writes GUIDs in lower-case "D" form, a shell may not. */
export function sameGuid(left: string | null | undefined, right: string | null | undefined): boolean {
  return typeof left === 'string' && typeof right === 'string' && left.toLowerCase() === right.toLowerCase()
}

export function decodeJournalReversalProposal(value: unknown, companyId: string): JournalReversalProposal | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const v = value as Record<string, unknown>
  const own = Object.keys(v)
  if (own.length !== KEYS.length || !KEYS.every((key) => Object.prototype.hasOwnProperty.call(v, key))) return null
  if (!isReversalGuid(v.proposalId) || !isReversalGuid(v.companyId) || !sameGuid(v.companyId, companyId)) return null
  if (!isReversalGuid(v.sourceJournalId) || !nullableGuid(v.reversalJournalId) || !isReversalGuid(v.periodId)) return null
  if (!date(v.transactionDate) || !date(v.accountingDate) || !(v.valueDate === null || date(v.valueDate))) return null
  if (typeof v.reason !== 'string' || v.reason.trim().length === 0 || v.reason.length > JOURNAL_REVERSAL_REASON_MAX) return null
  if (!isReversalGuid(v.proposedBy) || !instant(v.proposedAtUtc)) return null
  if (typeof v.status !== 'string' || !(JOURNAL_REVERSAL_STATUSES as readonly string[]).includes(v.status)) return null
  if (!nullableGuid(v.decidedBy) || !nullableInstant(v.decidedAtUtc)) return null
  if (!(v.decisionReason === null || (typeof v.decisionReason === 'string' && v.decisionReason.length > 0))) return null
  if (typeof v.version !== 'number' || !Number.isSafeInteger(v.version) || v.version < 1) return null

  const status = v.status as JournalReversalStatus
  // The shape must match the status: a pending proposal has no decision and no reversal journal; an approved one
  // names its checker (never the proposer) and exactly one reversal journal distinct from the source.
  if (status === 'Pending') {
    if (v.decidedBy !== null || v.decidedAtUtc !== null || v.reversalJournalId !== null) return null
  } else {
    if (v.decidedBy === null || v.decidedAtUtc === null || sameGuid(v.decidedBy, v.proposedBy)) return null
    if (status === 'Approved') {
      if (v.reversalJournalId === null || sameGuid(v.reversalJournalId, v.sourceJournalId)) return null
    } else if (v.reversalJournalId !== null) {
      return null
    }
  }

  return {
    proposalId: v.proposalId,
    companyId: v.companyId,
    sourceJournalId: v.sourceJournalId,
    reversalJournalId: v.reversalJournalId,
    periodId: v.periodId,
    transactionDate: v.transactionDate,
    accountingDate: v.accountingDate,
    valueDate: v.valueDate,
    reason: v.reason,
    proposedBy: v.proposedBy,
    proposedAtUtc: v.proposedAtUtc,
    status,
    decidedBy: v.decidedBy,
    decidedAtUtc: v.decidedAtUtc,
    decisionReason: v.decisionReason as string | null,
    version: v.version,
  }
}

export type ProposalFormIssue = 'periodId' | 'transactionDate' | 'accountingDate' | 'valueDate' | 'reason'

export interface ProposalForm {
  readonly periodId: string
  readonly transactionDate: string
  readonly accountingDate: string
  readonly valueDate: string
  readonly reason: string
}

/**
 * Local shape check before a command is frozen. It mirrors only the public adapter's syntax (GUID, ISO dates,
 * non-blank reason within the server's bound); every business rule (period open and covering, reversal of a
 * reversal, company) stays the server's.
 */
export function validateProposalForm(form: ProposalForm): ProposalFormIssue | null {
  if (!isReversalGuid(form.periodId.trim())) return 'periodId'
  if (!isDateOnly(form.transactionDate)) return 'transactionDate'
  if (!isDateOnly(form.accountingDate)) return 'accountingDate'
  if (form.valueDate !== '' && !isDateOnly(form.valueDate)) return 'valueDate'
  const reason = form.reason.trim()
  if (reason.length === 0 || reason.length > JOURNAL_REVERSAL_REASON_MAX) return 'reason'
  return null
}

export function proposalRequest(
  proposalId: string,
  sourceJournalId: string,
  form: ProposalForm,
): ProposeJournalReversalRequest {
  return {
    proposalId,
    sourceJournalId,
    periodId: form.periodId.trim(),
    transactionDate: form.transactionDate,
    accountingDate: form.accountingDate,
    valueDate: form.valueDate === '' ? null : form.valueDate,
    reason: form.reason.trim(),
  }
}
