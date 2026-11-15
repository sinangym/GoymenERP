/**
 * API-85b — the customer collateral master's wire shapes (F018 plan §L2; FNS-D57, FNS-D31, FNS-D58), decoded strictly
 * like ./CreditLimitContract.ts: exact member sets, the verified shell company, bound identities, decimals as TEXT. An
 * answer that does not decode is never shown as data.
 *
 * Source: `PartyCollateralV1.cs` (Finance.Contracts). The wire is camelCase; enums are their names; a `DateOnly` is
 * `YYYY-MM-DD`.
 *
 * - A record is one revision of a collateral. A Record starts it at revision "1"; a Change or a Release is a new
 *   revision that supersedes the previous one. A Release is a reverse record (the previous values, Kind = Release,
 *   with a reason), never a delete. Only a Release carries a reason.
 * - `revision` is the number the next change or release must name; `version` is the row's own concurrency token.
 * - The weighted collateral (amount x the Settings type rate / 100) and the documentary risk are the server's figures,
 *   per currency, never added across currencies and never recomputed here. The documentary risk is shown SEPARATELY
 *   (FNS-D58); nothing on this screen adds it to a risk total.
 * - The screen learns no actor id.
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
import { isNonNegativeDecimalText, isPositiveDecimalText } from './decimalText'

export type CollateralDirection = 'Received' | 'Given'
export type CollateralSource = 'FinanceManualEntry'
export type CollateralKind = 'Record' | 'Change' | 'Release'
export type CollateralStatus = 'Effective' | 'Superseded'
export type CollateralFigureState = 'Available' | 'Unavailable' | 'Withheld'

export interface CollateralRecord {
  readonly id: string
  readonly companyId: string
  readonly collateralId: string
  readonly revision: string
  readonly direction: CollateralDirection
  readonly source: CollateralSource
  readonly partyId: string
  readonly companyAccountId: string
  readonly kind: CollateralKind
  readonly collateralTypeId: string
  readonly collateralTypeVersion: string
  readonly currencyId: string
  readonly amount: string
  readonly validFrom: string
  readonly validUntil: string | null
  readonly documentReference: string | null
  readonly releaseReason: string | null
  readonly status: CollateralStatus
  readonly recordedAtUtc: string
  readonly supersededAtUtc: string | null
  readonly version: string
}

export interface CollateralAmount {
  readonly currencyId: string
  readonly amount: string
}

export type CollateralFigure =
  | { readonly state: 'Available'; readonly amounts: readonly CollateralAmount[]; readonly reasonCode: null }
  | { readonly state: 'Unavailable' | 'Withheld'; readonly amounts: readonly []; readonly reasonCode: string }

export interface CollateralAccount {
  readonly companyId: string
  readonly companyAccountId: string
  readonly held: readonly CollateralRecord[]
  readonly weightedCollateral: CollateralFigure
  readonly documentaryRisk: CollateralFigure
}

export interface CollateralHistoryPage {
  readonly companyId: string
  readonly companyAccountId: string
  readonly items: readonly CollateralRecord[]
}

export type CollateralCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: CollateralRecord
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

export interface CollateralTypeCandidate {
  readonly collateralTypeId: string
  readonly code: string
  readonly name: string
  readonly ratePercent: string
  readonly version: string
}

export interface CollateralTypeCandidates {
  readonly companyId: string
  readonly items: readonly CollateralTypeCandidate[]
}

export type CollateralCurrencyCandidates = CurrencyCandidates

// ---------------------------------------------------------------------------------
// Requests (camelCase members of the C# request records; the company is never in a body)
// ---------------------------------------------------------------------------------

export interface CollateralRecordBody {
  readonly partyId: string
  readonly companyAccountId: string
  readonly collateralTypeId: string
  readonly currencyId: string
  readonly amount: string
  readonly validFrom: string
  readonly validUntil: string | null
  readonly documentReference: string | null
}

export interface CollateralChangeBody {
  readonly expectedRevision: string
  readonly collateralTypeId: string
  readonly currencyId: string
  readonly amount: string
  readonly validFrom: string
  readonly validUntil: string | null
  readonly documentReference: string | null
}

export interface CollateralReleaseBody {
  readonly expectedRevision: string
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

const isDate = (value: unknown): value is string => typeof value === 'string' && isDateOnly(value)
const isNullableInstant = (value: unknown): value is string | null => value === null || isInstant(value)

/** Text the server keeps as given: not blank and not padded. */
const isCanonicalText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value === value.trim()

const MAX_PERCENT_INTEGER = 100

/** A Settings rate: exact decimal text within 0–100. Finance re-checks it; this only refuses a nonsense answer. */
export function isRatePercentText(value: unknown): value is string {
  if (!isNonNegativeDecimalText(value)) return false
  const [integer, fraction = ''] = value.split('.')
  const whole = Number(integer)
  if (!Number.isSafeInteger(whole) || whole > MAX_PERCENT_INTEGER) return false
  return whole < MAX_PERCENT_INTEGER || !/[1-9]/.test(fraction)
}

const RECORD_FIELDS = [
  'id', 'companyId', 'collateralId', 'revision', 'direction', 'source', 'partyId', 'companyAccountId', 'kind',
  'collateralTypeId', 'collateralTypeVersion', 'currencyId', 'amount', 'validFrom', 'validUntil', 'documentReference',
  'releaseReason', 'status', 'recordedAtUtc', 'supersededAtUtc', 'version',
] as const

const DIRECTIONS: readonly string[] = ['Received', 'Given']
const SOURCES: readonly string[] = ['FinanceManualEntry']
const KINDS: readonly string[] = ['Record', 'Change', 'Release']
const STATUSES: readonly string[] = ['Effective', 'Superseded']
const FIGURE_STATES: readonly string[] = ['Available', 'Unavailable', 'Withheld']
const COMMAND_OUTCOMES: readonly string[] = [
  'Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded',
]

function decodeRecord(value: unknown, companyId: string): CollateralRecord | null {
  if (!isRecord(value) || !hasExactKeys(value, RECORD_FIELDS)) return null
  const {
    id, collateralId, revision, direction, source, partyId, companyAccountId, kind, collateralTypeId,
    collateralTypeVersion, currencyId, amount, validFrom, validUntil, documentReference, releaseReason, status,
    recordedAtUtc, supersededAtUtc, version,
  } = value
  if (value.companyId !== companyId) return null
  if (!isBoundUuid(id) || !isBoundUuid(collateralId) || !isBoundUuid(partyId) || !isBoundUuid(companyAccountId)) return null
  if (!isBoundUuid(collateralTypeId) || !isBoundUuid(currencyId)) return null
  if (!isPositiveInt64Text(revision) || !isPositiveInt64Text(version)) return null
  if (typeof direction !== 'string' || !DIRECTIONS.includes(direction)) return null
  if (typeof source !== 'string' || !SOURCES.includes(source)) return null
  if (typeof kind !== 'string' || !KINDS.includes(kind)) return null
  if (typeof status !== 'string' || !STATUSES.includes(status)) return null
  if (!isCanonicalText(collateralTypeVersion) || !isPositiveDecimalText(amount)) return null
  if (!isDate(validFrom) || !(validUntil === null || isDate(validUntil))) return null
  // ISO dates compare as text: an end before the start is not a validity the server keeps.
  if (validUntil !== null && validUntil < validFrom) return null
  if (!(documentReference === null || isCanonicalText(documentReference))) return null
  if (!isInstant(recordedAtUtc) || !isNullableInstant(supersededAtUtc)) return null

  // A Record starts a collateral at revision 1; a Change or a Release is always a later revision.
  if ((kind === 'Record') !== (revision === '1')) return null
  // Only a Release carries a reason.
  if (kind === 'Release' ? !isCanonicalText(releaseReason) : releaseReason !== null) return null
  // The superseded stamp agrees with the status.
  if ((status === 'Superseded') !== (supersededAtUtc !== null)) return null

  return {
    id,
    companyId,
    collateralId,
    revision,
    direction: direction as CollateralDirection,
    source: source as CollateralSource,
    partyId,
    companyAccountId,
    kind: kind as CollateralKind,
    collateralTypeId,
    collateralTypeVersion,
    currencyId,
    amount,
    validFrom,
    validUntil,
    documentReference,
    releaseReason: releaseReason as string | null,
    status: status as CollateralStatus,
    recordedAtUtc,
    supersededAtUtc,
    version,
  }
}

function decodeFigure(value: unknown, allowWithheld: boolean): CollateralFigure | null {
  if (!isRecord(value) || !hasExactKeys(value, ['state', 'amounts', 'reasonCode'])) return null
  const { state, amounts, reasonCode } = value
  if (typeof state !== 'string' || !FIGURE_STATES.includes(state) || !Array.isArray(amounts)) return null
  if (state !== 'Available') {
    if (state === 'Withheld' && !allowWithheld) return null
    // A figure that cannot be shown carries its reason and no amount.
    if (amounts.length > 0 || typeof reasonCode !== 'string' || reasonCode.length === 0) return null
    return { state: state as 'Unavailable' | 'Withheld', amounts: [], reasonCode }
  }
  if (reasonCode !== null) return null
  const decoded: CollateralAmount[] = []
  const seen = new Set<string>()
  for (const raw of amounts) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['currencyId', 'amount'])) return null
    // One line per currency; a weighted amount may be zero (a 0% rate), never negative.
    if (!isBoundUuid(raw.currencyId) || seen.has(raw.currencyId) || !isNonNegativeDecimalText(raw.amount)) return null
    seen.add(raw.currencyId)
    decoded.push({ currencyId: raw.currencyId, amount: raw.amount })
  }
  return { state: 'Available', amounts: decoded, reasonCode: null }
}

/** One account, answering the account that was asked for. */
export function decodeCollateralAccount(value: unknown, companyId: string,
  companyAccountId: string): Decoded<CollateralAccount> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'companyAccountId', 'held', 'weightedCollateral', 'documentaryRisk'])) {
    return FAIL
  }
  if (value.companyId !== companyId || value.companyAccountId !== companyAccountId || !Array.isArray(value.held)) return FAIL
  const held: CollateralRecord[] = []
  const seen = new Set<string>()
  for (const raw of value.held) {
    const record = decodeRecord(raw, companyId)
    // A held collateral is the account's effective, received, not released revision — once per collateral.
    if (record === null || record.companyAccountId !== companyAccountId || seen.has(record.collateralId)) return FAIL
    if (record.status !== 'Effective' || record.kind === 'Release' || record.direction !== 'Received') return FAIL
    seen.add(record.collateralId)
    held.push(record)
  }
  // The weighted collateral is never withheld; the documentary risk follows the instrument amount authority.
  const weightedCollateral = decodeFigure(value.weightedCollateral, false)
  const documentaryRisk = decodeFigure(value.documentaryRisk, true)
  if (weightedCollateral === null || documentaryRisk === null) return FAIL
  return ok({ companyId, companyAccountId, held, weightedCollateral, documentaryRisk })
}

export function decodeCollateralHistoryPage(value: unknown, companyId: string,
  companyAccountId: string): Decoded<CollateralHistoryPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'companyAccountId', 'items'])) return FAIL
  if (value.companyId !== companyId || value.companyAccountId !== companyAccountId || !Array.isArray(value.items)) return FAIL
  const items: CollateralRecord[] = []
  const seen = new Set<string>()
  const revisions = new Set<string>()
  const effective = new Set<string>()
  for (const raw of value.items) {
    const record = decodeRecord(raw, companyId)
    if (record === null || record.companyAccountId !== companyAccountId || seen.has(record.id)) return FAIL
    if (record.direction !== 'Received') return FAIL
    const revisionKey = `${record.collateralId}:${record.revision}`
    // One row per collateral revision and at most one effective revision per collateral.
    if (revisions.has(revisionKey) || (record.status === 'Effective' && effective.has(record.collateralId))) return FAIL
    seen.add(record.id)
    revisions.add(revisionKey)
    if (record.status === 'Effective') effective.add(record.collateralId)
    items.push(record)
  }
  return ok({ companyId, companyAccountId, items })
}

export function decodeCollateralCommandResult(value: unknown, companyId: string): Decoded<CollateralCommandResult> {
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
    // A success carries its effective revision and no issue.
    return resource === null || resource.status !== 'Effective' || issues.length > 0
      ? FAIL
      : ok({ outcome: 'Succeeded', resource, ...common })
  }
  if (value.resource !== null) return FAIL
  return ok({ outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}

/** Settings' active collateral types as Finance passes them: as given, never filtered, reordered or defaulted. */
export function decodeCollateralTypeCandidates(value: unknown, companyId: string): Decoded<CollateralTypeCandidates> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  const items: CollateralTypeCandidate[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['collateralTypeId', 'code', 'name', 'ratePercent', 'version'])) return FAIL
    if (
      !isBoundUuid(raw.collateralTypeId) ||
      seen.has(raw.collateralTypeId) ||
      !isCanonicalText(raw.code) ||
      !isCanonicalText(raw.name) ||
      !isRatePercentText(raw.ratePercent) ||
      !isCanonicalText(raw.version)
    ) {
      return FAIL
    }
    seen.add(raw.collateralTypeId)
    items.push({
      collateralTypeId: raw.collateralTypeId,
      code: raw.code,
      name: raw.name,
      ratePercent: raw.ratePercent,
      version: raw.version,
    })
  }
  return ok({ companyId, items })
}

/** Settings' money currency candidates, as the bank-account master decodes them: as given, no default. */
export function decodeCollateralCurrencyCandidates(value: unknown, companyId: string): Decoded<CollateralCurrencyCandidates> {
  return decodeCurrencyCandidates(value, companyId)
}
