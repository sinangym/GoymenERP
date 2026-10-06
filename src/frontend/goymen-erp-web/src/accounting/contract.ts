/**
 * Accounting wire contract (ACC-V0) — the views of `AccountingApiV1.cs`, decoded STRICTLY.
 *
 * Wire facts, from the backend as implemented (System.Text.Json Web camelCase):
 *  - dates are `yyyy-MM-dd` text, instants ISO text; the period version is a JSON number;
 *  - amounts are lossless invariant decimal TEXT (never a JSON number): they are shown as the
 *    server sent them and never summed, rounded or parsed here;
 *  - a period names its company; any other company than the active one is refused, never shown.
 *
 * A body that does not match is `null`: the caller treats it as a malformed answer, never as data.
 */

import { isNonNegativeDecimalText, isPositiveDecimalText } from '../finance/decimalText'
import { isDateOnly } from '../finance/dateOnly'

export const PERIOD_STATUSES = ['Proposed', 'Open', 'Closed'] as const
export type PeriodStatus = (typeof PERIOD_STATUSES)[number]

export const JOURNAL_KINDS = ['Original', 'Reversal', 'Correction'] as const
export type JournalKind = (typeof JOURNAL_KINDS)[number]

export interface PeriodView {
  readonly periodId: string
  readonly companyId: string
  readonly startsOn: string
  readonly endsOn: string
  readonly status: PeriodStatus
  readonly version: number
  readonly preparedByUserId: string
  readonly preparedAtUtc: string
  readonly approvedByUserId: string | null
  readonly approvedAtUtc: string | null
  readonly closedByUserId: string | null
  readonly closedAtUtc: string | null
  /** The caller prepared this proposal: no approval is offered (Ö4); the server refuses one anyway. */
  readonly isOwnProposal: boolean
}

export interface PeriodPage {
  readonly items: readonly PeriodView[]
  readonly nextCursor: string | null
}

export interface JournalSummary {
  readonly journalId: string
  readonly kind: JournalKind
  readonly sourceId: string
  readonly postingPurpose: string
  readonly postingRuleVersion: string
  readonly currencyId: string
  readonly transactionDate: string
  readonly accountingDate: string
  readonly valueDate: string | null
  readonly periodId: string
  readonly linkedJournalId: string | null
  readonly totalDebit: string
  readonly totalCredit: string
  readonly lineCount: number
  readonly storedAtUtc: string
}

export interface JournalLineView {
  readonly lineNumber: number
  readonly accountId: string
  readonly debit: string
  readonly credit: string
}

export interface JournalDetail {
  readonly journal: JournalSummary
  readonly lines: readonly JournalLineView[]
}

export interface JournalPage {
  readonly items: readonly JournalSummary[]
  readonly nextCursor: string | null
}

export interface PostingExceptionView {
  readonly sourceId: string
  readonly postingPurpose: string
  readonly postingRuleVersion: string
  readonly missingMappingKeys: readonly string[]
  readonly firstMessageId: string
}

export interface PendingValuationView {
  readonly sourceId: string
  readonly postingPurpose: string
  readonly postingRuleVersion: string
  readonly linesAwaitingAmount: number
  readonly firstMessageId: string
}

export interface PostingQueues {
  readonly exceptions: readonly PostingExceptionView[]
  readonly pending: readonly PendingValuationView[]
}

export const NORMAL_JOURNAL_DRAFT_STATUSES = ['Draft', 'Approved'] as const
export type NormalJournalDraftStatus = (typeof NORMAL_JOURNAL_DRAFT_STATUSES)[number]
export type NormalJournalSide = 'Debit' | 'Credit'

export interface NormalJournalDraftLine {
  readonly lineNumber: number
  readonly mappingKey: string
  readonly side: NormalJournalSide
  readonly accountId: string
  readonly amount: string
}

export interface NormalJournalDraft {
  readonly draftId: string
  readonly companyId: string
  readonly version: number
  readonly status: NormalJournalDraftStatus
  readonly preparedByUserId: string
  readonly preparedAtUtc: string
  readonly approvedByUserId: string | null
  readonly approvedAtUtc: string | null
  readonly periodId: string | null
  readonly postedJournalId: string | null
  readonly sourceId: string
  readonly postingPurpose: string
  readonly postingRuleVersion: string
  readonly accountPlanSourceVersion: string
  readonly accountPlanSourceSha256: string
  readonly currencyId: string
  readonly transactionDate: string
  readonly accountingDate: string
  readonly valueDate: string | null
  readonly lines: readonly NormalJournalDraftLine[]
}

export interface CreateNormalJournalDraftLine {
  readonly accountId: string
  readonly side: NormalJournalSide
  readonly amount: string
}

export interface CreateNormalJournalDraftRequest {
  readonly draftId: string
  readonly sourceId: string
  readonly transactionDate: string
  readonly accountingDate: string
  readonly valueDate: string | null
  readonly expectedPreparationVersion: string
  readonly lines: readonly CreateNormalJournalDraftLine[]
}

export interface NormalJournalDraftSourceMapping {
  readonly mappingKey: string
  readonly side: NormalJournalSide
  readonly accountId: string
  readonly accountCode: string
  readonly accountName: string
}

export interface NormalJournalDraftSourcePreparation {
  readonly companyId: string
  readonly sourceId: string
  readonly preparationVersion: string
  readonly sourceVersion: string
  readonly ownerProfileId: string
  readonly ownerProfileVersion: string
  readonly currencyId: string
  readonly postingPurpose: string
  readonly postingRuleVersion: string
  readonly accountPlanSourceVersion: string
  readonly accountPlanSourceSha256: string
  readonly mappings: readonly NormalJournalDraftSourceMapping[]
}

// ---------------------------------------------------------------------------------
// Primitive readers
// ---------------------------------------------------------------------------------

type Json = Record<string, unknown>

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const INSTANT_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

function record(value: unknown): Json | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Json) : null
}

function hasExactKeys(value: Json, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID_RE.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)
}

function isNullableGuid(value: unknown): value is string | null {
  return value === null || isGuid(value)
}

function isDate(value: unknown): value is string {
  return typeof value === 'string' && isDateOnly(value)
}

function isInstant(value: unknown): value is string {
  return typeof value === 'string' && INSTANT_RE.test(value) && !Number.isNaN(Date.parse(value))
}

function isNullableInstant(value: unknown): value is string | null {
  return value === null || isInstant(value)
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/i.test(value)
}

export function decodeNormalJournalDraftSourcePreparation(
  value: unknown,
  companyId: string,
  sourceId: string,
): NormalJournalDraftSourcePreparation | null {
  const v = record(value)
  const keys = [
    'companyId', 'sourceId', 'preparationVersion', 'sourceVersion', 'ownerProfileId',
    'ownerProfileVersion', 'currencyId', 'postingPurpose', 'postingRuleVersion',
    'accountPlanSourceVersion', 'accountPlanSourceSha256', 'mappings',
  ] as const
  if (v === null || !hasExactKeys(v, keys) || !Array.isArray(v.mappings)
      || v.companyId !== companyId || v.sourceId !== sourceId
      || !isSha256(v.preparationVersion) || !isText(v.sourceVersion)
      || !isText(v.ownerProfileId) || !isText(v.ownerProfileVersion)
      || !isGuid(v.currencyId) || !isText(v.postingPurpose) || !isText(v.postingRuleVersion)
      || !isText(v.accountPlanSourceVersion) || !isSha256(v.accountPlanSourceSha256)) return null
  const mappings: NormalJournalDraftSourceMapping[] = []
  for (const item of v.mappings) {
    const mapping = record(item)
    if (mapping === null || !hasExactKeys(mapping,
      ['mappingKey', 'side', 'accountId', 'accountCode', 'accountName'])
      || !isText(mapping.mappingKey) || (mapping.side !== 'Debit' && mapping.side !== 'Credit')
      || !isGuid(mapping.accountId) || !isText(mapping.accountCode) || !isText(mapping.accountName)) return null
    mappings.push({
      mappingKey: mapping.mappingKey,
      side: mapping.side,
      accountId: mapping.accountId,
      accountCode: mapping.accountCode,
      accountName: mapping.accountName,
    })
  }
  if (mappings.length < 2
      || new Set(mappings.map(mapping => `${mapping.side}:${mapping.accountId}`)).size !== mappings.length) return null
  return {
    companyId, sourceId, preparationVersion: v.preparationVersion, sourceVersion: v.sourceVersion,
    ownerProfileId: v.ownerProfileId, ownerProfileVersion: v.ownerProfileVersion,
    currencyId: v.currencyId, postingPurpose: v.postingPurpose,
    postingRuleVersion: v.postingRuleVersion, accountPlanSourceVersion: v.accountPlanSourceVersion,
    accountPlanSourceSha256: v.accountPlanSourceSha256, mappings,
  }
}

function isCount(value: unknown, minimum: number): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum
}

function isCursor(value: unknown): value is string | null {
  return value === null || isText(value)
}

const NORMAL_DRAFT_KEYS = [
  'draftId',
  'companyId',
  'version',
  'status',
  'preparedByUserId',
  'preparedAtUtc',
  'approvedByUserId',
  'approvedAtUtc',
  'periodId',
  'postedJournalId',
  'sourceId',
  'postingPurpose',
  'postingRuleVersion',
  'accountPlanSourceVersion',
  'accountPlanSourceSha256',
  'currencyId',
  'transactionDate',
  'accountingDate',
  'valueDate',
  'lines',
] as const

export function decodeNormalJournalDraft(value: unknown, companyId: string): NormalJournalDraft | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, NORMAL_DRAFT_KEYS) || !Array.isArray(v.lines)) return null
  if (!isGuid(v.draftId) || v.companyId !== companyId || !isCount(v.version, 1)) return null
  if (typeof v.status !== 'string' || !(NORMAL_JOURNAL_DRAFT_STATUSES as readonly string[]).includes(v.status)) return null
  if (!isGuid(v.preparedByUserId) || !isInstant(v.preparedAtUtc)) return null
  if (!isNullableGuid(v.approvedByUserId) || !isNullableInstant(v.approvedAtUtc)) return null
  if (!isNullableGuid(v.periodId) || !isNullableGuid(v.postedJournalId)) return null
  if (!isGuid(v.sourceId) || !isText(v.postingPurpose) || !isText(v.postingRuleVersion)) return null
  if (!isText(v.accountPlanSourceVersion) || typeof v.accountPlanSourceSha256 !== 'string'
      || !/^[0-9a-f]{64}$/i.test(v.accountPlanSourceSha256) || !isGuid(v.currencyId)) return null
  if (!isDate(v.transactionDate) || !isDate(v.accountingDate) || !(v.valueDate === null || isDate(v.valueDate))) return null

  const status = v.status as NormalJournalDraftStatus
  const unapproved = v.approvedByUserId === null && v.approvedAtUtc === null && v.periodId === null && v.postedJournalId === null
  const approved = isGuid(v.approvedByUserId) && isInstant(v.approvedAtUtc) && isGuid(v.periodId)
    && v.postedJournalId === v.draftId
  if ((status === 'Draft' && !unapproved) || (status === 'Approved' && !approved)) return null

  const lines: NormalJournalDraftLine[] = []
  for (let index = 0; index < v.lines.length; index += 1) {
    const line = record(v.lines[index])
    if (line === null || !hasExactKeys(line, ['lineNumber', 'mappingKey', 'side', 'accountId', 'amount'])) return null
    if (line.lineNumber !== index + 1 || !isText(line.mappingKey)
        || (line.side !== 'Debit' && line.side !== 'Credit') || !isGuid(line.accountId)
        || !isPositiveDecimalText(line.amount)) return null
    lines.push({
      lineNumber: line.lineNumber,
      mappingKey: line.mappingKey,
      side: line.side,
      accountId: line.accountId,
      amount: line.amount,
    })
  }
  if (lines.length < 2) return null

  return {
    draftId: v.draftId,
    companyId,
    version: v.version,
    status,
    preparedByUserId: v.preparedByUserId,
    preparedAtUtc: v.preparedAtUtc,
    approvedByUserId: v.approvedByUserId,
    approvedAtUtc: v.approvedAtUtc,
    periodId: v.periodId,
    postedJournalId: v.postedJournalId,
    sourceId: v.sourceId,
    postingPurpose: v.postingPurpose,
    postingRuleVersion: v.postingRuleVersion,
    accountPlanSourceVersion: v.accountPlanSourceVersion,
    accountPlanSourceSha256: v.accountPlanSourceSha256,
    currencyId: v.currencyId,
    transactionDate: v.transactionDate,
    accountingDate: v.accountingDate,
    valueDate: v.valueDate,
    lines,
  }
}

// ---------------------------------------------------------------------------------
// Periods
// ---------------------------------------------------------------------------------

const PERIOD_KEYS = [
  'periodId',
  'companyId',
  'startsOn',
  'endsOn',
  'status',
  'version',
  'preparedByUserId',
  'preparedAtUtc',
  'approvedByUserId',
  'approvedAtUtc',
  'closedByUserId',
  'closedAtUtc',
  'isOwnProposal',
] as const

export function decodePeriod(value: unknown, companyId: string): PeriodView | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, PERIOD_KEYS)) return null
  if (!isGuid(v.periodId) || v.companyId !== companyId || !isDate(v.startsOn) || !isDate(v.endsOn)) return null
  if ((v.endsOn as string) < (v.startsOn as string)) return null
  if (typeof v.status !== 'string' || !(PERIOD_STATUSES as readonly string[]).includes(v.status)) return null
  if (!isCount(v.version, 1) || !isGuid(v.preparedByUserId) || !isInstant(v.preparedAtUtc)) return null
  if (!isNullableGuid(v.approvedByUserId) || !isNullableInstant(v.approvedAtUtc)) return null
  if (!isNullableGuid(v.closedByUserId) || !isNullableInstant(v.closedAtUtc)) return null
  if (typeof v.isOwnProposal !== 'boolean') return null

  // The facts follow the status exactly (the server's CK_Periods_Status): nothing else is shown.
  const approved = v.approvedByUserId !== null && v.approvedAtUtc !== null
  const noApproval = v.approvedByUserId === null && v.approvedAtUtc === null
  const closed = v.closedByUserId !== null && v.closedAtUtc !== null
  const notClosed = v.closedByUserId === null && v.closedAtUtc === null
  const status = v.status as PeriodStatus
  const consistent =
    (status === 'Proposed' && noApproval && notClosed) ||
    (status === 'Open' && approved && notClosed && !v.isOwnProposal) ||
    (status === 'Closed' && approved && closed && !v.isOwnProposal)
  if (!consistent) return null
  // Ö4 holds in what is shown too: an approval is never the preparer's.
  if (approved && v.approvedByUserId === v.preparedByUserId) return null

  return {
    periodId: v.periodId,
    companyId,
    startsOn: v.startsOn as string,
    endsOn: v.endsOn as string,
    status,
    version: v.version,
    preparedByUserId: v.preparedByUserId,
    preparedAtUtc: v.preparedAtUtc,
    approvedByUserId: v.approvedByUserId,
    approvedAtUtc: v.approvedAtUtc,
    closedByUserId: v.closedByUserId,
    closedAtUtc: v.closedAtUtc,
    isOwnProposal: v.isOwnProposal,
  }
}

export function decodePeriodPage(value: unknown, companyId: string): PeriodPage | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, ['items', 'nextCursor']) || !Array.isArray(v.items) || !isCursor(v.nextCursor)) {
    return null
  }
  const items: PeriodView[] = []
  for (const item of v.items) {
    const period = decodePeriod(item, companyId)
    if (period === null) return null
    items.push(period)
  }
  if (new Set(items.map((item) => item.periodId)).size !== items.length) return null
  return { items, nextCursor: v.nextCursor }
}

// ---------------------------------------------------------------------------------
// Journals (M-04, read-only)
// ---------------------------------------------------------------------------------

const JOURNAL_KEYS = [
  'journalId',
  'kind',
  'sourceId',
  'postingPurpose',
  'postingRuleVersion',
  'currencyId',
  'transactionDate',
  'accountingDate',
  'valueDate',
  'periodId',
  'linkedJournalId',
  'totalDebit',
  'totalCredit',
  'lineCount',
  'storedAtUtc',
] as const

export function decodeJournalSummary(value: unknown): JournalSummary | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, JOURNAL_KEYS)) return null
  if (!isGuid(v.journalId) || typeof v.kind !== 'string' || !(JOURNAL_KINDS as readonly string[]).includes(v.kind)) return null
  if (!isGuid(v.sourceId) || !isText(v.postingPurpose) || !isText(v.postingRuleVersion) || !isGuid(v.currencyId)) return null
  if (!isDate(v.transactionDate) || !isDate(v.accountingDate) || !(v.valueDate === null || isDate(v.valueDate))) return null
  if (!isGuid(v.periodId) || !isNullableGuid(v.linkedJournalId) || !isInstant(v.storedAtUtc)) return null
  // Amounts are the server's text: never a JSON number, never a negative or an exponent.
  if (!isNonNegativeDecimalText(v.totalDebit) || !isNonNegativeDecimalText(v.totalCredit)) return null
  if (!isCount(v.lineCount, 1)) return null
  // An original stands alone; a reversal or a correction names the journal it answers.
  const kind = v.kind as JournalKind
  if ((kind === 'Original') !== (v.linkedJournalId === null)) return null

  return {
    journalId: v.journalId,
    kind,
    sourceId: v.sourceId,
    postingPurpose: v.postingPurpose,
    postingRuleVersion: v.postingRuleVersion,
    currencyId: v.currencyId,
    transactionDate: v.transactionDate as string,
    accountingDate: v.accountingDate as string,
    valueDate: v.valueDate as string | null,
    periodId: v.periodId,
    linkedJournalId: v.linkedJournalId,
    totalDebit: v.totalDebit,
    totalCredit: v.totalCredit,
    lineCount: v.lineCount,
    storedAtUtc: v.storedAtUtc,
  }
}

export function decodeJournalPage(value: unknown): JournalPage | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, ['items', 'nextCursor']) || !Array.isArray(v.items) || !isCursor(v.nextCursor)) {
    return null
  }
  const items: JournalSummary[] = []
  for (const item of v.items) {
    const journal = decodeJournalSummary(item)
    if (journal === null) return null
    items.push(journal)
  }
  return { items, nextCursor: v.nextCursor }
}

function decodeLine(value: unknown): JournalLineView | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, ['lineNumber', 'accountId', 'debit', 'credit'])) return null
  if (!isCount(v.lineNumber, 1) || !isGuid(v.accountId)) return null
  if (!isNonNegativeDecimalText(v.debit) || !isNonNegativeDecimalText(v.credit)) return null
  return { lineNumber: v.lineNumber, accountId: v.accountId, debit: v.debit, credit: v.credit }
}

export function decodeJournalDetail(value: unknown): JournalDetail | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, ['journal', 'lines']) || !Array.isArray(v.lines)) return null
  const journal = decodeJournalSummary(v.journal)
  if (journal === null) return null
  const lines: JournalLineView[] = []
  for (const item of v.lines) {
    const line = decodeLine(item)
    if (line === null) return null
    lines.push(line)
  }
  // The detail is the same journal the summary counts: no line invented or dropped.
  if (lines.length !== journal.lineCount) return null
  if (lines.some((line, index) => line.lineNumber !== index + 1)) return null
  return { journal, lines }
}

// ---------------------------------------------------------------------------------
// Queues (M-01, read-only)
// ---------------------------------------------------------------------------------

function decodeException(value: unknown): PostingExceptionView | null {
  const v = record(value)
  if (
    v === null ||
    !hasExactKeys(v, ['sourceId', 'postingPurpose', 'postingRuleVersion', 'missingMappingKeys', 'firstMessageId'])
  ) {
    return null
  }
  if (!isGuid(v.sourceId) || !isText(v.postingPurpose) || !isText(v.postingRuleVersion) || !isText(v.firstMessageId)) {
    return null
  }
  if (!Array.isArray(v.missingMappingKeys) || v.missingMappingKeys.length === 0) return null
  if (!v.missingMappingKeys.every(isText)) return null
  return {
    sourceId: v.sourceId,
    postingPurpose: v.postingPurpose,
    postingRuleVersion: v.postingRuleVersion,
    missingMappingKeys: v.missingMappingKeys as string[],
    firstMessageId: v.firstMessageId,
  }
}

function decodePending(value: unknown): PendingValuationView | null {
  const v = record(value)
  if (
    v === null ||
    !hasExactKeys(v, ['sourceId', 'postingPurpose', 'postingRuleVersion', 'linesAwaitingAmount', 'firstMessageId'])
  ) {
    return null
  }
  if (!isGuid(v.sourceId) || !isText(v.postingPurpose) || !isText(v.postingRuleVersion) || !isText(v.firstMessageId)) {
    return null
  }
  if (!isCount(v.linesAwaitingAmount, 1)) return null
  return {
    sourceId: v.sourceId,
    postingPurpose: v.postingPurpose,
    postingRuleVersion: v.postingRuleVersion,
    linesAwaitingAmount: v.linesAwaitingAmount,
    firstMessageId: v.firstMessageId,
  }
}

export function decodePostingQueues(value: unknown): PostingQueues | null {
  const v = record(value)
  if (v === null || !hasExactKeys(v, ['exceptions', 'pending']) || !Array.isArray(v.exceptions) || !Array.isArray(v.pending)) {
    return null
  }
  const exceptions: PostingExceptionView[] = []
  for (const item of v.exceptions) {
    const entry = decodeException(item)
    if (entry === null) return null
    exceptions.push(entry)
  }
  const pending: PendingValuationView[] = []
  for (const item of v.pending) {
    const entry = decodePending(item)
    if (entry === null) return null
    pending.push(entry)
  }
  return { exceptions, pending }
}

// ---------------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------------

/**
 * One frozen period command. `idempotencyKey` (and for a proposal the `periodId`) is minted ONCE
 * per snapshot: a resend of an unknown outcome sends exactly this again.
 */
export type PeriodCommandSnapshot =
  | {
      readonly operation: 'propose'
      readonly idempotencyKey: string
      readonly periodId: string
      readonly startsOn: string
      readonly endsOn: string
    }
  | {
      readonly operation: 'approve' | 'close'
      readonly idempotencyKey: string
      readonly periodId: string
      readonly expectedVersion: number
    }

/** Exactly `ProposePeriodRequestV1` or `PeriodCommandRequestV1`: no company, nothing else. */
export function periodCommandBody(snapshot: PeriodCommandSnapshot): Record<string, unknown> {
  return snapshot.operation === 'propose'
    ? { periodId: snapshot.periodId, startsOn: snapshot.startsOn, endsOn: snapshot.endsOn }
    : { expectedVersion: snapshot.expectedVersion }
}
