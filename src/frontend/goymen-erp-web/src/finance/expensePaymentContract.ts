/**
 * FNS-D25 / API-25 — the expense payment wire contract (mirrors ExpensePaymentV1.cs). Every answer is `unknown`
 * until decoded; a decoder answers the typed value or `{ ok: false }`, never a partial object or a default. The
 * company, the requested payment and the status facts are checked after the shape.
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

export type ExpensePaymentSourceKind = 'Bank' | 'Cash'
export type ExpensePaymentStatus = 'Draft' | 'Approved' | 'Recorded'

export const EXPENSE_SOURCE_KINDS: readonly ExpensePaymentSourceKind[] = ['Bank', 'Cash']

/** Same limits as ExpensePaymentValidator; the server stays the authority, these only spare a round trip. */
export const EXPENSE_DESCRIPTION_MAX_LENGTH = 1000
export const EXPENSE_REFERENCE_MAX_LENGTH = 128

export interface ExpensePaymentPreparation {
  readonly sourceKind: ExpensePaymentSourceKind
  readonly treasuryAccountId: string
  readonly expenseItemId: string
  readonly money: MoneyText
  readonly profile: ProfileReference
  readonly occurredOn: string
  readonly valueOn: string | null
  readonly reference: string | null
  readonly description: string
}

export interface ExpensePaymentDraftSaveRequest {
  readonly paymentId: string | null
  readonly expectedVersion: string | null
  readonly preparation: ExpensePaymentPreparation
}

export interface ApproveExpensePaymentRequest {
  readonly paymentId: string
  readonly expectedVersion: string
}

export interface RecordExpensePaymentRequest extends ApproveExpensePaymentRequest {
  readonly paymentConfirmed: boolean
}

export interface ExpensePaymentView {
  readonly id: string
  readonly companyId: string
  readonly status: ExpensePaymentStatus
  readonly preparation: ExpensePaymentPreparation
  readonly preparedBy: string
  readonly preparedAtUtc: string
  readonly approvedBy: string | null
  readonly approvedAtUtc: string | null
  readonly recordedBy: string | null
  readonly recordedAtUtc: string | null
  readonly paymentConfirmed: boolean
  readonly version: string
}

export interface ExpensePaymentPage {
  readonly companyId: string
  readonly items: readonly ExpensePaymentView[]
  readonly nextCursor: string | null
}

export type ExpensePaymentCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: ExpensePaymentView
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

export interface ExpenseItem {
  readonly id: string
  readonly code: string
}

export interface ExpenseItemCatalog {
  readonly companyId: string
  readonly items: readonly ExpenseItem[]
}

export interface ExpensePaymentDraftContext {
  readonly companyId: string
  readonly sourceKind: ExpensePaymentSourceKind
  readonly treasuryAccountId: string
  readonly currencyId: string
  readonly profile: ProfileReference
  readonly occurredOn: string
}

export type CashAccountStatus = 'Proposed' | 'Approved' | 'Rejected' | 'Passive'

export interface CashAccountListItem {
  readonly id: string
  readonly currencyId: string
  readonly displayName: string
  readonly status: CashAccountStatus
  readonly isActive: boolean
}

export interface CashAccountList {
  readonly companyId: string
  readonly items: readonly CashAccountListItem[]
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
const STATUSES: readonly ExpensePaymentStatus[] = ['Draft', 'Approved', 'Recorded']
const CASH_STATUSES: readonly CashAccountStatus[] = ['Proposed', 'Approved', 'Rejected', 'Passive']
const CODE_PATTERN = /^[a-z][a-z0-9-]{0,63}$/

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

function isSourceKind(value: unknown): value is ExpensePaymentSourceKind {
  return typeof value === 'string' && (EXPENSE_SOURCE_KINDS as readonly string[]).includes(value)
}

function isWireDate(value: unknown): value is string {
  return typeof value === 'string' && isDateOnly(value)
}

function decodeProfile(value: unknown): ProfileReference | null {
  if (!isRecord(value) || !hasExactKeys(value, ['profileId', 'version'])) return null
  if (!isBoundUuid(value.profileId) || !isPositiveInt64Text(value.version)) return null
  return { profileId: value.profileId, version: value.version }
}

const PREPARATION_KEYS = [
  'sourceKind',
  'treasuryAccountId',
  'expenseItemId',
  'money',
  'profile',
  'occurredOn',
  'valueOn',
  'reference',
  'description',
] as const

function decodePreparation(value: unknown): ExpensePaymentPreparation | null {
  if (!isRecord(value) || !hasExactKeys(value, PREPARATION_KEYS)) return null
  const profile = decodeProfile(value.profile)
  if (
    !isSourceKind(value.sourceKind)
    || !isBoundUuid(value.treasuryAccountId)
    || !isBoundUuid(value.expenseItemId)
    || !isRecord(value.money)
    || !hasExactKeys(value.money, ['amount', 'currencyId'])
    || !isPositiveDecimalText(value.money.amount)
    || !isBoundUuid(value.money.currencyId)
    || profile === null
    || !isWireDate(value.occurredOn)
    || !(value.valueOn === null || isWireDate(value.valueOn))
    || !(value.reference === null || (typeof value.reference === 'string' && value.reference.trim().length > 0
      && value.reference.length <= EXPENSE_REFERENCE_MAX_LENGTH))
    // FNS-D25: the description is mandatory on every stored payment.
    || typeof value.description !== 'string'
    || value.description.trim().length === 0
    || value.description.length > EXPENSE_DESCRIPTION_MAX_LENGTH
  ) return null

  return {
    sourceKind: value.sourceKind,
    treasuryAccountId: value.treasuryAccountId,
    expenseItemId: value.expenseItemId,
    money: { amount: value.money.amount, currencyId: value.money.currencyId },
    profile,
    occurredOn: value.occurredOn,
    valueOn: value.valueOn,
    reference: value.reference,
    description: value.description,
  }
}

const VIEW_KEYS = [
  'id',
  'companyId',
  'status',
  'preparation',
  'preparedBy',
  'preparedAtUtc',
  'approvedBy',
  'approvedAtUtc',
  'recordedBy',
  'recordedAtUtc',
  'paymentConfirmed',
  'version',
] as const

export function decodeExpensePaymentView(value: unknown, companyId: string): Decoded<ExpensePaymentView> {
  if (!isRecord(value) || !hasExactKeys(value, VIEW_KEYS)) return FAIL
  const preparation = decodePreparation(value.preparation)
  if (
    !isBoundUuid(value.id)
    || value.companyId !== companyId
    || typeof value.status !== 'string'
    || !(STATUSES as readonly string[]).includes(value.status)
    || preparation === null
    || !isActor(value.preparedBy)
    || !isInstant(value.preparedAtUtc)
    || !(value.approvedBy === null || isActor(value.approvedBy))
    || !(value.approvedAtUtc === null || isInstant(value.approvedAtUtc))
    || !(value.recordedBy === null || isActor(value.recordedBy))
    || !(value.recordedAtUtc === null || isInstant(value.recordedAtUtc))
    || typeof value.paymentConfirmed !== 'boolean'
    || !isOpaqueVersion(value.version)
  ) return FAIL

  const status = value.status as ExpensePaymentStatus
  const notRecorded = value.recordedBy === null && value.recordedAtUtc === null && value.paymentConfirmed === false
  const approvedByOther = isActor(value.approvedBy) && isInstant(value.approvedAtUtc)
    && value.preparedBy !== value.approvedBy
  const draft = value.approvedBy === null && value.approvedAtUtc === null && notRecorded
  const approved = approvedByOther && notRecorded
  const recorded = approvedByOther
    && isActor(value.recordedBy) && isInstant(value.recordedAtUtc) && value.paymentConfirmed === true
  if ((status === 'Draft' && !draft) || (status === 'Approved' && !approved) || (status === 'Recorded' && !recorded)) {
    return FAIL
  }

  return ok({
    id: value.id,
    companyId,
    status,
    preparation,
    preparedBy: value.preparedBy,
    preparedAtUtc: value.preparedAtUtc,
    approvedBy: value.approvedBy,
    approvedAtUtc: value.approvedAtUtc,
    recordedBy: value.recordedBy,
    recordedAtUtc: value.recordedAtUtc,
    paymentConfirmed: value.paymentConfirmed,
    version: value.version,
  })
}

/** The detail read: the payment asked for, for the active company, or nothing. */
export function decodeExpensePaymentFor(
  value: unknown,
  companyId: string,
  paymentId: string,
): ExpensePaymentView | null {
  const decoded = decodeExpensePaymentView(value, companyId)
  return decoded.ok && decoded.value.id === paymentId ? decoded.value : null
}

export function decodeExpensePaymentPage(value: unknown, companyId: string): ExpensePaymentPage | null {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items', 'nextCursor'])) return null
  if (value.companyId !== companyId || !Array.isArray(value.items)) return null
  if (!(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) return null
  const items: ExpensePaymentView[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    const decoded = decodeExpensePaymentView(raw, companyId)
    if (!decoded.ok || seen.has(decoded.value.id)) return null
    seen.add(decoded.value.id)
    items.push(decoded.value)
  }
  return { companyId, items, nextCursor: value.nextCursor }
}

export function decodeExpensePaymentCommandResult(
  value: unknown,
  companyId: string,
): Decoded<ExpensePaymentCommandResult> {
  if (!isRecord(value) || !hasExactKeys(value,
    ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])) return FAIL
  if (
    typeof value.outcome !== 'string'
    || !COMMAND_OUTCOMES.includes(value.outcome as CommandOutcomeName)
    || typeof value.isReplay !== 'boolean'
    || typeof value.originalCorrelationId !== 'string'
    || (value.isReplay && value.originalCorrelationId.length === 0)
    || !Array.isArray(value.issues)
  ) return FAIL

  const issues: ValidationIssue[] = []
  for (const raw of value.issues) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['field', 'code'])
      || typeof raw.field !== 'string' || typeof raw.code !== 'string' || raw.code.length === 0) return FAIL
    issues.push({ field: raw.field, code: raw.code })
  }
  const common = { isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId, issues }
  if (value.outcome === 'Succeeded') {
    const resource = decodeExpensePaymentView(value.resource, companyId)
    return resource.ok ? ok({ outcome: 'Succeeded', resource: resource.value, ...common }) : FAIL
  }
  if (value.resource !== null) return FAIL
  return ok({ outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}

export function decodeExpenseItemCatalog(value: unknown, companyId: string): ExpenseItemCatalog | null {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return null
  if (value.companyId !== companyId || !Array.isArray(value.items)) return null
  const items: ExpenseItem[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['id', 'code'])) return null
    if (!isBoundUuid(raw.id) || typeof raw.code !== 'string' || !CODE_PATTERN.test(raw.code)) return null
    if (seen.has(raw.id)) return null
    seen.add(raw.id)
    items.push({ id: raw.id, code: raw.code })
  }
  return { companyId, items }
}

export interface DraftContextRequest {
  readonly sourceKind: ExpensePaymentSourceKind
  readonly treasuryAccountId: string
  readonly occurredOn: string
}

/** The context answers exactly what was asked: same source, account and date, for the active company. */
export function decodeExpenseDraftContext(
  value: unknown,
  companyId: string,
  request: DraftContextRequest,
): ExpensePaymentDraftContext | null {
  if (!isRecord(value) || !hasExactKeys(value,
    ['companyId', 'sourceKind', 'treasuryAccountId', 'currencyId', 'profile', 'occurredOn'])) return null
  const profile = decodeProfile(value.profile)
  if (
    value.companyId !== companyId
    || value.sourceKind !== request.sourceKind
    || value.treasuryAccountId !== request.treasuryAccountId
    || !isBoundUuid(value.currencyId)
    || profile === null
    || value.occurredOn !== request.occurredOn
  ) return null
  return {
    companyId,
    sourceKind: request.sourceKind,
    treasuryAccountId: request.treasuryAccountId,
    currencyId: value.currencyId,
    profile,
    occurredOn: request.occurredOn,
  }
}

const CASH_ACCOUNT_KEYS = [
  'id', 'companyId', 'currencyId', 'displayName', 'status', 'isActive', 'rejectionReason', 'pendingChange', 'version',
] as const

/**
 * The cash-account list, reduced to what this screen needs to offer an account. The full shape is still checked,
 * so an answer that drifted from the contract is refused rather than half read.
 */
export function decodeCashAccountList(value: unknown, companyId: string): CashAccountList | null {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return null
  if (value.companyId !== companyId || !Array.isArray(value.items)) return null
  const items: CashAccountListItem[] = []
  for (const raw of value.items) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['account', 'isOwnProposal', 'isOwnPendingChange'])) return null
    if (typeof raw.isOwnProposal !== 'boolean' || typeof raw.isOwnPendingChange !== 'boolean') return null
    const account = raw.account
    if (!isRecord(account) || !hasExactKeys(account, CASH_ACCOUNT_KEYS)) return null
    if (
      !isBoundUuid(account.id)
      || account.companyId !== companyId
      || !isBoundUuid(account.currencyId)
      || typeof account.displayName !== 'string'
      || account.displayName.trim().length === 0
      || typeof account.status !== 'string'
      || !(CASH_STATUSES as readonly string[]).includes(account.status)
      || typeof account.isActive !== 'boolean'
      || !(account.rejectionReason === null || typeof account.rejectionReason === 'string')
      || !(account.pendingChange === null || account.pendingChange === 'Passivation'
        || account.pendingChange === 'Reactivation')
      || !isOpaqueVersion(account.version)
    ) return null
    items.push({
      id: account.id,
      currencyId: account.currencyId,
      displayName: account.displayName,
      status: account.status as CashAccountStatus,
      isActive: account.isActive,
    })
  }
  return { companyId, items }
}
