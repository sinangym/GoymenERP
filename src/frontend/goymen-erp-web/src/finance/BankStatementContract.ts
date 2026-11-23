/**
 * WP12 bank statement import and matching — wire contract and strict decoders (exact keys, fail-closed null; amounts stay
 * decimal strings). FNS-D113: a suggestion is only a read; a match is final only when a person confirms it.
 */
import { isBoundUuid, isInstant, isPositiveInt64Text } from './contract'
import { isDateOnly as isDateOnlyText } from './dateOnly'
import { isPositiveDecimalText } from './decimalText'

export type StatementFormat = 'Csv' | 'Mt940' | 'Xlsx'
export type StatementState = 'Reserved' | 'Parsed'
export type StatementDirection = 'Inflow' | 'Outflow'
export type MatchStatus = 'Suggested' | 'Manual' | 'Confirmed'
export type ManualReason = 'NoExactCandidate' | 'CounterpartyMismatch' | 'Ambiguous'
export type CandidateKind = 'Receipt' | 'Payment' | 'ExpensePayment' | 'Transfer'
export type CounterpartyCheck = 'Verified' | 'NotAvailable' | 'Mismatch'
export type CommandOutcome = 'Unavailable' | 'Denied' | 'ValidationFailed' | 'VersionConflict' | 'IdempotencyConflict' | 'Succeeded'

export interface StatementImport {
  readonly id: string; readonly companyId: string; readonly version: string; readonly bankAccountId: string
  readonly format: StatementFormat; readonly bankKey: string | null; readonly state: StatementState
  readonly documentId: string | null; readonly documentVersion: number | null; readonly documentSha256: string | null
  readonly csvMappingProfileId: string | null; readonly csvMappingProfileVersion: number | null
  readonly lineCount: number; readonly duplicateLineCount: number
  readonly firstTransactionOn: string | null; readonly lastTransactionOn: string | null
  readonly reservedBy: string; readonly reservedAtUtc: string; readonly parsedBy: string | null; readonly parsedAtUtc: string | null
}
export interface StatementCandidate { readonly kind: CandidateKind; readonly recordId: string; readonly occurredOn: string; readonly counterpartyCheck: CounterpartyCheck }
export interface StatementMatch {
  readonly id: string; readonly companyId: string; readonly importId: string; readonly lineId: string
  readonly kind: CandidateKind; readonly recordId: string; readonly version: string; readonly state: 'Confirmed' | 'Reversed'
  readonly counterpartyCheck: CounterpartyCheck; readonly confirmedBy: string; readonly confirmedAtUtc: string
  readonly reversalId: string | null; readonly reversedBy: string | null; readonly reversedAtUtc: string | null; readonly reversalReason: string | null
}
export interface StatementSuggestion {
  readonly status: MatchStatus; readonly manualReason: ManualReason | null
  readonly candidate: StatementCandidate | null; readonly match: StatementMatch | null
}
export interface StatementLine {
  readonly id: string; readonly lineNumber: number; readonly transactionOn: string; readonly valueOn: string | null
  readonly direction: StatementDirection; readonly amount: string; readonly balance: string | null; readonly currencyText: string | null
  readonly reference: string | null; readonly description: string; readonly counterpartyIban: string | null
  readonly suggestion: StatementSuggestion
}
export interface StatementDetail { readonly import: StatementImport; readonly lines: readonly StatementLine[]; readonly suggestedCount: number; readonly manualCount: number }
export interface StatementIssue { readonly lineNumber: number; readonly field: string; readonly code: string }
export interface StatementCommandResult {
  readonly outcome: CommandOutcome; readonly resource: StatementImport | null; readonly code: string | null
  readonly issues: readonly StatementIssue[]; readonly replayed: boolean
}
export interface MatchCommandResult { readonly outcome: CommandOutcome; readonly resource: StatementMatch | null; readonly code: string | null; readonly replayed: boolean }

const rec = (v: unknown): Record<string, unknown> | null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Record<string, unknown> : null
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every((k) => Object.hasOwn(v, k))
const text = (v: unknown): v is string => typeof v === 'string' && v.length > 0
const one = <T extends string>(v: unknown, values: readonly T[]): v is T => typeof v === 'string' && (values as readonly string[]).includes(v)
const count = (v: unknown): v is number => Number.isSafeInteger(v) && (v as number) >= 0
const nullable = <T>(v: unknown, check: (x: unknown) => x is T): boolean => v === null || check(v)
const isDateOnly = (v: unknown): v is string => typeof v === 'string' && isDateOnlyText(v)
const signedDecimal = (v: unknown): v is string => typeof v === 'string' && /^-?(0|[1-9]\d*)(\.\d+)?$/.test(v)
const OUTCOMES: readonly CommandOutcome[] = ['Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded']
const KINDS: readonly CandidateKind[] = ['Receipt', 'Payment', 'ExpensePayment', 'Transfer']
const CHECKS: readonly CounterpartyCheck[] = ['Verified', 'NotAvailable', 'Mismatch']
const IMPORT_KEYS = ['id', 'companyId', 'version', 'bankAccountId', 'format', 'bankKey', 'state', 'documentId', 'documentVersion',
  'documentSha256', 'csvMappingProfileId', 'csvMappingProfileVersion', 'lineCount', 'duplicateLineCount', 'firstTransactionOn',
  'lastTransactionOn', 'reservedBy', 'reservedAtUtc', 'parsedBy', 'parsedAtUtc'] as const
const MATCH_KEYS = ['id', 'companyId', 'importId', 'lineId', 'kind', 'recordId', 'version', 'state', 'counterpartyCheck', 'confirmedBy',
  'confirmedAtUtc', 'reversalId', 'reversedBy', 'reversedAtUtc', 'reversalReason'] as const

export function decodeImport(value: unknown, companyId: string): StatementImport | null {
  const v = rec(value)
  if (v === null || !exact(v, IMPORT_KEYS) || !isBoundUuid(v.id) || v.companyId !== companyId || !isPositiveInt64Text(v.version)
    || !isBoundUuid(v.bankAccountId) || !one(v.format, ['Csv', 'Mt940', 'Xlsx'] as const) || !one(v.state, ['Reserved', 'Parsed'] as const)
    || !nullable(v.bankKey, text) || (v.format === 'Csv') !== (v.bankKey !== null)
    || !count(v.lineCount) || !count(v.duplicateLineCount) || !text(v.reservedBy) || !isInstant(v.reservedAtUtc)) return null
  const parsed = v.state === 'Parsed'
  const parseFields = [v.documentId, v.documentVersion, v.documentSha256, v.firstTransactionOn, v.lastTransactionOn, v.parsedBy, v.parsedAtUtc]
  if (parsed ? !isBoundUuid(v.documentId) || !Number.isSafeInteger(v.documentVersion) || (v.documentVersion as number) < 1
      || typeof v.documentSha256 !== 'string' || !/^[0-9A-F]{64}$/.test(v.documentSha256) || !isDateOnly(v.firstTransactionOn)
      || !isDateOnly(v.lastTransactionOn) || !text(v.parsedBy) || !isInstant(v.parsedAtUtc)
    : parseFields.some((x) => x !== null) || v.lineCount !== 0 || v.duplicateLineCount !== 0) return null
  if ((v.format === 'Csv' && parsed) !== (v.csvMappingProfileId !== null) || !nullable(v.csvMappingProfileId, isBoundUuid)
    || (v.csvMappingProfileId === null) !== (v.csvMappingProfileVersion === null)
    || (v.csvMappingProfileVersion !== null && (!Number.isSafeInteger(v.csvMappingProfileVersion) || (v.csvMappingProfileVersion as number) < 1))) return null
  return v as unknown as StatementImport
}

export function decodeMatch(value: unknown, companyId: string): StatementMatch | null {
  const v = rec(value)
  if (v === null || !exact(v, MATCH_KEYS) || !isBoundUuid(v.id) || v.companyId !== companyId || !isBoundUuid(v.importId)
    || !isBoundUuid(v.lineId) || !one(v.kind, KINDS) || !isBoundUuid(v.recordId) || !one(v.counterpartyCheck, CHECKS)
    || v.counterpartyCheck === 'Mismatch' || !text(v.confirmedBy) || !isInstant(v.confirmedAtUtc)) return null
  const reversed = v.state === 'Reversed'
  if (v.state !== 'Confirmed' && !reversed) return null
  if (reversed ? v.version !== '2' || !isBoundUuid(v.reversalId) || !text(v.reversedBy) || !isInstant(v.reversedAtUtc) || !text(v.reversalReason)
    : v.version !== '1' || v.reversalId !== null || v.reversedBy !== null || v.reversedAtUtc !== null || v.reversalReason !== null) return null
  return v as unknown as StatementMatch
}

function candidate(value: unknown): StatementCandidate | null {
  const v = rec(value)
  return v !== null && exact(v, ['kind', 'recordId', 'occurredOn', 'counterpartyCheck']) && one(v.kind, KINDS) && isBoundUuid(v.recordId)
    && isDateOnly(v.occurredOn) && one(v.counterpartyCheck, CHECKS) && v.counterpartyCheck !== 'Mismatch'
    ? v as unknown as StatementCandidate : null
}

function suggestion(value: unknown, companyId: string, lineId: string): StatementSuggestion | null {
  const v = rec(value)
  if (v === null || !exact(v, ['status', 'manualReason', 'candidate', 'match']) || !one(v.status, ['Suggested', 'Manual', 'Confirmed'] as const)) return null
  const c = v.candidate === null ? null : candidate(v.candidate)
  const m = v.match === null ? null : decodeMatch(v.match, companyId)
  if ((v.candidate !== null && c === null) || (v.match !== null && m === null)) return null
  if (v.status === 'Suggested' && (c === null || m !== null || v.manualReason !== null)) return null
  if (v.status === 'Manual' && (c !== null || m !== null || !one(v.manualReason, ['NoExactCandidate', 'CounterpartyMismatch', 'Ambiguous'] as const))) return null
  if (v.status === 'Confirmed' && (m === null || m.state !== 'Confirmed' || m.lineId !== lineId || v.manualReason !== null
    || (c !== null && (c.recordId !== m.recordId || c.kind !== m.kind)))) return null
  return { status: v.status, manualReason: v.manualReason as ManualReason | null, candidate: c, match: m }
}

const LINE_KEYS = ['id', 'lineNumber', 'transactionOn', 'valueOn', 'direction', 'amount', 'balance', 'currencyText', 'reference',
  'description', 'counterpartyIban', 'suggestion'] as const

export function decodeDetail(value: unknown, companyId: string, importId: string): StatementDetail | null {
  const v = rec(value)
  if (v === null || !exact(v, ['import', 'lines', 'suggestedCount', 'manualCount']) || !Array.isArray(v.lines)
    || !count(v.suggestedCount) || !count(v.manualCount)) return null
  const header = decodeImport(v.import, companyId)
  if (header === null || header.id !== importId) return null
  const lines: StatementLine[] = []
  for (const raw of v.lines) {
    const l = rec(raw)
    if (l === null || !exact(l, LINE_KEYS) || !isBoundUuid(l.id) || !Number.isSafeInteger(l.lineNumber) || (l.lineNumber as number) < 0
      || !isDateOnly(l.transactionOn) || !nullable(l.valueOn, isDateOnly) || !one(l.direction, ['Inflow', 'Outflow'] as const)
      || !isPositiveDecimalText(l.amount) || !nullable(l.balance, signedDecimal) || !nullable(l.currencyText, text)
      || !nullable(l.reference, text) || !text(l.description)
      || !(l.counterpartyIban === null || (typeof l.counterpartyIban === 'string' && /^TR\d{24}$/.test(l.counterpartyIban)))) return null
    const s = suggestion(l.suggestion, companyId, l.id as string)
    if (s === null || (s.match !== null && s.match.importId !== importId)) return null
    lines.push({ ...(l as unknown as StatementLine), suggestion: s })
  }
  if (new Set(lines.map((x) => x.id)).size !== lines.length) return null
  if (v.suggestedCount !== lines.filter((x) => x.suggestion.status === 'Suggested').length
    || v.manualCount !== lines.length - (v.suggestedCount as number)) return null
  return { import: header, lines, suggestedCount: v.suggestedCount, manualCount: v.manualCount }
}

export function decodeImportList(value: unknown, companyId: string): readonly StatementImport[] | null {
  const v = rec(value)
  if (v === null || !exact(v, ['companyId', 'imports']) || v.companyId !== companyId || !Array.isArray(v.imports)) return null
  const rows = v.imports.map((x) => decodeImport(x, companyId))
  return rows.some((x) => x === null) || new Set(rows.map((x) => x?.id)).size !== rows.length ? null : rows as StatementImport[]
}

export function decodeCommandResult(value: unknown, companyId: string): StatementCommandResult | null {
  const v = rec(value)
  if (v === null || !exact(v, ['outcome', 'resource', 'code', 'issues', 'replayed']) || !one(v.outcome, OUTCOMES)
    || typeof v.replayed !== 'boolean' || !nullable(v.code, text) || !Array.isArray(v.issues)) return null
  const resource = v.resource === null ? null : decodeImport(v.resource, companyId)
  if ((v.resource !== null && resource === null) || (v.outcome === 'Succeeded') !== (resource !== null)) return null
  const issues: StatementIssue[] = []
  for (const raw of v.issues) {
    const i = rec(raw)
    if (i === null || !exact(i, ['lineNumber', 'field', 'code']) || !count(i.lineNumber) || !text(i.field) || !text(i.code)) return null
    issues.push(i as unknown as StatementIssue)
  }
  return { outcome: v.outcome, resource, code: v.code as string | null, issues, replayed: v.replayed }
}

export function decodeMatchResult(value: unknown, companyId: string): MatchCommandResult | null {
  const v = rec(value)
  if (v === null || !exact(v, ['outcome', 'resource', 'code', 'replayed']) || !one(v.outcome, OUTCOMES)
    || typeof v.replayed !== 'boolean' || !nullable(v.code, text)) return null
  const resource = v.resource === null ? null : decodeMatch(v.resource, companyId)
  if ((v.resource !== null && resource === null) || (v.outcome === 'Succeeded') !== (resource !== null)) return null
  return { outcome: v.outcome, resource, code: v.code as string | null, replayed: v.replayed }
}
