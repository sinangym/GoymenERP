import { decodeFinanceDocumentNumber, type FinanceDocumentNumber } from './documentNumber'
/**
 * FNS-D24 / API-32/34 — the treasury transfer wire contract (mirrors CashTransferV1.cs). A transfer between two
 * treasury accounts of the company, or a currency exchange between them at one rate, is ONE record carrying both
 * legs. Every answer is `unknown` until decoded; a decoder answers the typed value or `{ ok: false }` / `null`, never
 * a partial object or a default. The company, the leg rules and the confirmation are checked after the shape.
 */

import { isDateOnly } from './dateOnly'
import { isPositiveDecimalText } from './decimalText'
import {
  isBoundUuid,
  isInstant,
  isOpaqueVersion,
  isPositiveInt64Text,
  type CommandOutcomeName,
  type Decoded,
  type MoneyText,
  type ProfileReference,
  type ValidationIssue,
} from './contract'

export type CashTransferAccountKind = 'Bank' | 'Cash'
export type CashTransferKind = 'SameCurrency' | 'CurrencyExchange'

export const CASH_TRANSFER_ACCOUNT_KINDS: readonly CashTransferAccountKind[] = ['Bank', 'Cash']
export const CASH_TRANSFER_KINDS: readonly CashTransferKind[] = ['SameCurrency', 'CurrencyExchange']

/** Same limits as CashTransferValidator; the server stays the authority, these only spare a round trip. */
export const CASH_TRANSFER_DESCRIPTION_MAX_LENGTH = 1000
export const CASH_TRANSFER_REFERENCE_MAX_LENGTH = 128
/** The rate's storage bounds (CashTransferValidator.RateMaximumScale / RateMaximumIntegerDigits). */
export const CASH_TRANSFER_RATE_MAX_SCALE = 10
export const CASH_TRANSFER_RATE_MAX_INTEGER_DIGITS = 18

export interface CashTransferPreparation {
  readonly kind: CashTransferKind
  readonly sourceKind: CashTransferAccountKind
  readonly sourceAccountId: string
  readonly sourceMoney: MoneyText
  readonly sourceProfile: ProfileReference
  readonly targetKind: CashTransferAccountKind
  readonly targetAccountId: string
  readonly targetMoney: MoneyText
  readonly targetProfile: ProfileReference
  /** Target units per ONE source unit; only on a currency exchange. */
  readonly rate: string | null
  readonly occurredOn: string
  readonly valueOn: string | null
  readonly reference: string | null
  readonly description: string | null
}

export interface CommitCashTransferRequest {
  readonly preparation: CashTransferPreparation
  readonly transferConfirmed: boolean
}

export interface CashTransferView {
  readonly documentNumber?: FinanceDocumentNumber
  readonly id: string
  readonly companyId: string
  readonly preparation: CashTransferPreparation
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly transferConfirmed: true
  readonly version: string
  readonly exchange?: CashTransferExchangeSnapshot
}

export interface CashTransferComparisonSource {
  readonly currencyId: string
  readonly sellingRate: string
  readonly rateDay: string
  readonly source: 'Tcmb' | 'FunctionalCurrency'
  readonly recordId: string | null
  readonly recordVersion: string | null
  readonly bulletinDate: string | null
  readonly provenance: string | null
}

export interface CashTransferExchangeSnapshot {
  readonly companyId: string
  readonly occurredOn: string
  readonly sourceMoney: MoneyText
  readonly targetMoney: MoneyText
  readonly sourceProfile: ProfileReference
  readonly targetProfile: ProfileReference
  readonly transactionRate: string
  readonly exactTransactionRate: { readonly numerator: string; readonly denominator: string }
  readonly comparisonRate: string
  readonly exactComparisonRate: { readonly numerator: string; readonly denominator: string }
  readonly rateDeviation: string
  readonly sourceComparison: CashTransferComparisonSource
  readonly targetComparison: CashTransferComparisonSource
}

export interface CashTransferPage {
  readonly companyId: string
  readonly items: readonly CashTransferView[]
  readonly nextCursor: string | null
}

export type CashTransferCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: CashTransferView
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

export type CashTransferPreviewResult =
  | { readonly outcome: 'Succeeded'; readonly preparation: CashTransferPreparation; readonly issues: readonly ValidationIssue[]; readonly exchange?: CashTransferExchangeSnapshot }
  | { readonly outcome: 'ValidationFailed'; readonly preparation: null; readonly issues: readonly ValidationIssue[] }

export interface CashTransferAccountContext {
  readonly companyId: string
  readonly accountKind: CashTransferAccountKind
  readonly accountId: string
  readonly currencyId: string
  readonly profile: ProfileReference
  readonly occurredOn: string
}

export interface AccountContextRequest {
  readonly accountKind: CashTransferAccountKind
  readonly accountId: string
  readonly occurredOn: string
}

const FAIL = { ok: false } as const
const COMMAND_OUTCOMES: readonly CommandOutcomeName[] = [
  'Unavailable',
  'Denied',
  'ValidationFailed',
  'VersionConflict',
  'IdempotencyConflict',
  'Succeeded',
]

function ok<T>(value: T): Decoded<T> {
  return { ok: true, value }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value)
  return actual.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

function isActor(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.trim() === value
}

export function isCashTransferAccountKind(value: unknown): value is CashTransferAccountKind {
  return typeof value === 'string' && (CASH_TRANSFER_ACCOUNT_KINDS as readonly string[]).includes(value)
}

function isTransferKind(value: unknown): value is CashTransferKind {
  return typeof value === 'string' && (CASH_TRANSFER_KINDS as readonly string[]).includes(value)
}

function isWireDate(value: unknown): value is string {
  return typeof value === 'string' && isDateOnly(value)
}

/** The server's rate grammar: an exact positive decimal within the storage bounds; never rounded or re-written. */
export function isRateText(value: unknown): value is string {
  if (!isPositiveDecimalText(value)) return false
  const dot = value.indexOf('.')
  const integerDigits = dot === -1 ? value.length : dot
  const scale = dot === -1 ? 0 : value.length - dot - 1
  return integerDigits <= CASH_TRANSFER_RATE_MAX_INTEGER_DIGITS && scale <= CASH_TRANSFER_RATE_MAX_SCALE
}

function isOptionalText(value: unknown, max: number): value is string | null {
  return value === null || (typeof value === 'string' && value.trim().length > 0 && value.length <= max)
}

function decodeProfile(value: unknown): ProfileReference | null {
  if (!isRecord(value) || !hasExactKeys(value, ['profileId', 'version'])) return null
  if (!isBoundUuid(value.profileId) || !isPositiveInt64Text(value.version)) return null
  return { profileId: value.profileId, version: value.version }
}

function decodeMoney(value: unknown): MoneyText | null {
  if (!isRecord(value) || !hasExactKeys(value, ['amount', 'currencyId'])) return null
  if (!isPositiveDecimalText(value.amount) || !isBoundUuid(value.currencyId)) return null
  return { amount: value.amount, currencyId: value.currencyId }
}

const PREPARATION_KEYS = [
  'kind',
  'sourceKind',
  'sourceAccountId',
  'sourceMoney',
  'sourceProfile',
  'targetKind',
  'targetAccountId',
  'targetMoney',
  'targetProfile',
  'rate',
  'occurredOn',
  'valueOn',
  'reference',
  'description',
] as const

/**
 * A stored or echoed preparation. The leg rules the server enforces are re-checked so a drifted answer is refused:
 * the two legs never name the same account; a same-currency transfer carries the same amount text in the same
 * currency and no rate; a currency exchange names two currencies and a rate.
 */
export function decodeCashTransferPreparation(value: unknown, computedRate = false): CashTransferPreparation | null {
  if (!isRecord(value) || !hasExactKeys(value, PREPARATION_KEYS)) return null
  const sourceMoney = decodeMoney(value.sourceMoney)
  const targetMoney = decodeMoney(value.targetMoney)
  const sourceProfile = decodeProfile(value.sourceProfile)
  const targetProfile = decodeProfile(value.targetProfile)
  if (
    !isTransferKind(value.kind)
    || !isCashTransferAccountKind(value.sourceKind)
    || !isBoundUuid(value.sourceAccountId)
    || sourceMoney === null
    || sourceProfile === null
    || !isCashTransferAccountKind(value.targetKind)
    || !isBoundUuid(value.targetAccountId)
    || targetMoney === null
    || targetProfile === null
    || !(value.rate === null || isRateText(value.rate) || computedRate && value.rate === '0.00')
    || !isWireDate(value.occurredOn)
    || !(value.valueOn === null || (isWireDate(value.valueOn) && value.valueOn >= value.occurredOn))
    || !isOptionalText(value.reference, CASH_TRANSFER_REFERENCE_MAX_LENGTH)
    || !isOptionalText(value.description, CASH_TRANSFER_DESCRIPTION_MAX_LENGTH)
  ) return null

  if (value.sourceKind === value.targetKind && value.sourceAccountId === value.targetAccountId) return null
  const sameCurrency = sourceMoney.currencyId === targetMoney.currencyId
  if (value.kind === 'SameCurrency'
    && (!sameCurrency || sourceMoney.amount !== targetMoney.amount || value.rate !== null)) return null
  if (value.kind === 'CurrencyExchange' && sameCurrency) return null

  return {
    kind: value.kind,
    sourceKind: value.sourceKind,
    sourceAccountId: value.sourceAccountId,
    sourceMoney,
    sourceProfile,
    targetKind: value.targetKind,
    targetAccountId: value.targetAccountId,
    targetMoney,
    targetProfile,
    rate: value.rate,
    occurredOn: value.occurredOn,
    valueOn: value.valueOn,
    reference: value.reference,
    description: value.description,
  }
}

const VIEW_KEYS = ['id', 'companyId', 'preparation', 'recordedBy', 'recordedAtUtc', 'transferConfirmed', 'version'] as const

function decodeExchange(value: unknown, preparation: CashTransferPreparation): CashTransferExchangeSnapshot | null {
  const keys = ['companyId', 'occurredOn', 'sourceMoney', 'targetMoney', 'sourceProfile', 'targetProfile',
    'transactionRate', 'exactTransactionRate', 'comparisonRate', 'exactComparisonRate', 'rateDeviation', 'sourceComparison', 'targetComparison']
  if (!isRecord(value) || !hasExactKeys(value, keys) || !isBoundUuid(value.companyId)
    || value.occurredOn !== preparation.occurredOn || preparation.kind !== 'CurrencyExchange') return null
  const sourceMoney = decodeMoney(value.sourceMoney); const targetMoney = decodeMoney(value.targetMoney)
  const sourceProfile = decodeProfile(value.sourceProfile); const targetProfile = decodeProfile(value.targetProfile)
  if (sourceMoney === null || targetMoney === null || sourceProfile === null || targetProfile === null
    || sourceMoney.currencyId !== preparation.sourceMoney.currencyId || sourceMoney.amount !== preparation.sourceMoney.amount
    || targetMoney.currencyId !== preparation.targetMoney.currencyId || targetMoney.amount !== preparation.targetMoney.amount
    || sourceProfile.profileId !== preparation.sourceProfile.profileId || sourceProfile.version !== preparation.sourceProfile.version
    || targetProfile.profileId !== preparation.targetProfile.profileId || targetProfile.version !== preparation.targetProfile.version
    || typeof value.transactionRate !== 'string' || !/^(0|[1-9]\d*)\.\d{2}$/.test(value.transactionRate)
    || typeof value.comparisonRate !== 'string' || !/^(0|[1-9]\d*)\.\d{2}$/.test(value.comparisonRate)
    || typeof value.rateDeviation !== 'string' || !/^-?(0|[1-9]\d*)\.\d{2}$/.test(value.rateDeviation)
    || value.rateDeviation === '-0.00') return null
  const ratio = (raw: unknown): { numerator: string; denominator: string } | null =>
    isRecord(raw) && hasExactKeys(raw, ['numerator', 'denominator'])
      && typeof raw.numerator === 'string' && /^[1-9]\d*$/.test(raw.numerator)
      && typeof raw.denominator === 'string' && /^[1-9]\d*$/.test(raw.denominator)
      ? { numerator: raw.numerator, denominator: raw.denominator } : null
  const comparison = (raw: unknown, currency: string): CashTransferComparisonSource | null => {
    if (!isRecord(raw) || !hasExactKeys(raw, ['currencyId', 'sellingRate', 'rateDay', 'source', 'recordId',
      'recordVersion', 'bulletinDate', 'provenance']) || raw.currencyId !== currency || raw.rateDay !== preparation.occurredOn
      || !isRateText(raw.sellingRate) || !isOptionalText(raw.provenance, 4000)) return null
    if (raw.source === 'FunctionalCurrency') {
      if (raw.sellingRate !== '1' || raw.recordId !== null || raw.recordVersion !== null || raw.bulletinDate !== null) return null
    } else if (raw.source === 'Tcmb') {
      if (!isBoundUuid(raw.recordId) || !isOpaqueVersion(raw.recordVersion) || !isWireDate(raw.bulletinDate)
        || raw.bulletinDate >= preparation.occurredOn) return null
    } else return null
    return raw as unknown as CashTransferComparisonSource
  }
  const exactTransactionRate = ratio(value.exactTransactionRate); const exactComparisonRate = ratio(value.exactComparisonRate)
  const sourceComparison = comparison(value.sourceComparison, sourceMoney.currencyId)
  const targetComparison = comparison(value.targetComparison, targetMoney.currencyId)
  if (exactTransactionRate === null || exactComparisonRate === null || sourceComparison === null || targetComparison === null) return null
  return { companyId: value.companyId, occurredOn: preparation.occurredOn, sourceMoney, targetMoney, sourceProfile, targetProfile,
    transactionRate: value.transactionRate, exactTransactionRate, comparisonRate: value.comparisonRate, exactComparisonRate,
    rateDeviation: value.rateDeviation, sourceComparison, targetComparison }
}

export function decodeCashTransferView(value: unknown, companyId: string): Decoded<CashTransferView> {
  if (!isRecord(value)) return FAIL
  const keys = Object.hasOwn(value, 'exchange') ? [...VIEW_KEYS, 'exchange'] : [...VIEW_KEYS]
  if (!hasExactKeys(value, Object.hasOwn(value, 'documentNumber') ? [...keys, 'documentNumber'] : keys)) return FAIL
  const documentNumber = decodeFinanceDocumentNumber(value.documentNumber)
  if (documentNumber === null) return FAIL
  const preparation = decodeCashTransferPreparation(value.preparation, Object.hasOwn(value, 'exchange'))
  if (
    !isBoundUuid(value.id)
    || value.companyId !== companyId
    || preparation === null
    || !isActor(value.recordedBy)
    || !isInstant(value.recordedAtUtc)
    // Only a confirmed commit is ever stored.
    || value.transferConfirmed !== true
    || !isOpaqueVersion(value.version)
  ) return FAIL
  if (preparation.kind === 'CurrencyExchange' && preparation.rate === null) return FAIL
  const exchange = Object.hasOwn(value, 'exchange') ? decodeExchange(value.exchange, preparation) : undefined
  if (exchange === null || exchange !== undefined && (exchange.companyId !== companyId || exchange.transactionRate !== preparation.rate)) return FAIL
  return ok({
    id: value.id,
    companyId,
    preparation,
    recordedBy: value.recordedBy,
    recordedAtUtc: value.recordedAtUtc,
    transferConfirmed: true,
    version: value.version,
    ...(documentNumber === undefined ? {} : { documentNumber }),
    ...(exchange === undefined ? {} : { exchange }),
  })
}

/** The detail read: the transfer asked for, for the active company, or nothing. */
export function decodeCashTransferFor(value: unknown, companyId: string, transferId: string): CashTransferView | null {
  const decoded = decodeCashTransferView(value, companyId)
  return decoded.ok && decoded.value.id === transferId ? decoded.value : null
}

export function decodeCashTransferPage(value: unknown, companyId: string): CashTransferPage | null {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items', 'nextCursor'])) return null
  if (value.companyId !== companyId || !Array.isArray(value.items)) return null
  if (!(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) return null
  const items: CashTransferView[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    const decoded = decodeCashTransferView(raw, companyId)
    if (!decoded.ok || seen.has(decoded.value.id)) return null
    seen.add(decoded.value.id)
    items.push(decoded.value)
  }
  return { companyId, items, nextCursor: value.nextCursor }
}

function decodeIssues(value: unknown): ValidationIssue[] | null {
  if (!Array.isArray(value)) return null
  const issues: ValidationIssue[] = []
  for (const raw of value) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['field', 'code'])
      || typeof raw.field !== 'string' || typeof raw.code !== 'string' || raw.code.length === 0) return null
    issues.push({ field: raw.field, code: raw.code })
  }
  return issues
}

export function decodeCashTransferCommandResult(value: unknown, companyId: string): Decoded<CashTransferCommandResult> {
  if (!isRecord(value) || !hasExactKeys(value,
    ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])) return FAIL
  const issues = decodeIssues(value.issues)
  if (
    typeof value.outcome !== 'string'
    || !COMMAND_OUTCOMES.includes(value.outcome as CommandOutcomeName)
    || typeof value.isReplay !== 'boolean'
    || typeof value.originalCorrelationId !== 'string'
    || (value.isReplay && value.originalCorrelationId.length === 0)
    || issues === null
  ) return FAIL

  const common = { isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId, issues }
  if (value.outcome === 'Succeeded') {
    const resource = decodeCashTransferView(value.resource, companyId)
    return resource.ok ? ok({ outcome: 'Succeeded', resource: resource.value, ...common }) : FAIL
  }
  if (value.resource !== null) return FAIL
  return ok({ outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}

/**
 * The preview answer. A success echoes the preparation the commit would record and carries no issue; a validation
 * refusal carries no preparation and at least one issue. Any other outcome is not a preview body (the server answers
 * 403/503 code-less).
 */
export function decodeCashTransferPreviewResult(value: unknown): Decoded<CashTransferPreviewResult> {
  if (!isRecord(value) || !hasExactKeys(value, Object.hasOwn(value, 'exchange')
    ? ['outcome', 'preparation', 'issues', 'exchange'] : ['outcome', 'preparation', 'issues'])) return FAIL
  const issues = decodeIssues(value.issues)
  if (issues === null) return FAIL
  if (value.outcome === 'Succeeded') {
    const preparation = decodeCashTransferPreparation(value.preparation)
    if (preparation === null || issues.length > 0) return FAIL
    const exchange = Object.hasOwn(value, 'exchange') ? decodeExchange(value.exchange, preparation) : undefined
    if (exchange === null || preparation.kind === 'CurrencyExchange' && exchange === undefined) return FAIL
    return ok({ outcome: 'Succeeded', preparation, issues, ...(exchange === undefined ? {} : { exchange }) })
  }
  if (value.outcome === 'ValidationFailed') {
    return value.preparation !== null || issues.length === 0 || Object.hasOwn(value, 'exchange')
      ? FAIL
      : ok({ outcome: 'ValidationFailed', preparation: null, issues })
  }
  return FAIL
}

/** The context answers exactly what was asked: same account kind, account and date, for the active company. */
export function decodeCashTransferAccountContext(
  value: unknown,
  companyId: string,
  request: AccountContextRequest,
): CashTransferAccountContext | null {
  if (!isRecord(value) || !hasExactKeys(value,
    ['companyId', 'accountKind', 'accountId', 'currencyId', 'profile', 'occurredOn'])) return null
  const profile = decodeProfile(value.profile)
  if (
    value.companyId !== companyId
    || value.accountKind !== request.accountKind
    || value.accountId !== request.accountId
    || !isBoundUuid(value.currencyId)
    || profile === null
    || value.occurredOn !== request.occurredOn
  ) return null
  return {
    companyId,
    accountKind: request.accountKind,
    accountId: request.accountId,
    currencyId: value.currencyId,
    profile,
    occurredOn: request.occurredOn,
  }
}
