/**
 * DEC-FIN-016 first release — the customer cheque / promissory note portfolio wire contract.
 *
 * SOURCE: `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Contracts/CustomerInstrumentV1.cs`, serialized with
 * `FinanceJson.CreateOptions()` (web camelCase, enums as names, `DateOnly` as `YYYY-MM-DD`, unknown members refused).
 * The fail-closed rules are the manual receipt contract's (./contract.ts): exact member sets, bound UUIDs, the
 * company bound to the verified shell company, `Visible` ⇔ money present and `Redacted`/`Unavailable` ⇔ no money,
 * and the face value in the instrument's own currency. A resource that breaks any of these is not rendered.
 */

import { isBoundUuid, isInstant, isOpaqueVersion, type AmountView, type Decoded, type MoneyText, type ValidationIssue } from './contract'
import { isDateOnly } from './dateOnly'
import { isPositiveDecimalText } from './decimalText'

export type CustomerInstrumentKind = 'Cheque' | 'PromissoryNote'
export type CustomerInstrumentStatus =
  | 'InPortfolio'
  | 'AtBankForCollection'
  | 'Collected'
  | 'Bounced'
  | 'Protested'
  | 'ReturnedToCustomer'
export type CustomerInstrumentTransition = 'DepositForCollection' | 'WithdrawFromBank' | 'Dishonour' | 'ReturnToCustomer'
export type CustomerInstrumentAction = CustomerInstrumentTransition | 'Collect'

const KINDS: readonly string[] = ['Cheque', 'PromissoryNote']
const STATUSES: readonly string[] = [
  'InPortfolio', 'AtBankForCollection', 'Collected', 'Bounced', 'Protested', 'ReturnedToCustomer',
]
const ACTIONS: readonly string[] = ['DepositForCollection', 'WithdrawFromBank', 'Dishonour', 'ReturnToCustomer', 'Collect']

export interface CustomerInstrumentView {
  readonly id: string
  readonly companyId: string
  readonly version: string
  readonly kind: CustomerInstrumentKind
  readonly status: CustomerInstrumentStatus
  readonly partyId: string
  readonly companyAccountId: string
  readonly serialNumber: string
  readonly drawer: string
  readonly bankName: string | null
  readonly branchName: string | null
  readonly dueOn: string
  readonly receivedOn: string
  readonly currencyId: string
  readonly amount: AmountView
  readonly description: string | null
  readonly treasuryAccountId: string | null
  readonly statusChangedOn: string
  readonly collectionSourceId: string | null
  readonly receivedBy: string
  readonly receivedAtUtc: string
}

export interface CustomerInstrumentMovementView {
  readonly version: string
  readonly fromStatus: CustomerInstrumentStatus | null
  readonly toStatus: CustomerInstrumentStatus
  readonly occurredOn: string
  readonly treasuryAccountId: string | null
  readonly collectionSourceId: string | null
  readonly note: string | null
  readonly recordedBy: string
  readonly recordedAtUtc: string
}

export interface CustomerInstrumentDetail {
  readonly instrument: CustomerInstrumentView
  readonly movements: readonly CustomerInstrumentMovementView[]
  readonly availableActions: readonly CustomerInstrumentAction[]
}

export interface CustomerInstrumentPage {
  readonly companyId: string
  readonly items: readonly CustomerInstrumentView[]
  readonly nextCursor: string | null
}

export type CustomerInstrumentCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: CustomerInstrumentView
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly ValidationIssue[]
    }
  | {
      readonly outcome: 'Unavailable' | 'Denied' | 'ValidationFailed' | 'VersionConflict' | 'IdempotencyConflict'
      readonly resource: null
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly ValidationIssue[]
    }

export interface CustomerInstrumentReceiveRequest {
  readonly kind: CustomerInstrumentKind
  readonly partyId: string
  readonly companyAccountId: string
  readonly serialNumber: string
  readonly drawer: string
  readonly bankName: string | null
  readonly branchName: string | null
  readonly dueOn: string
  readonly receivedOn: string
  readonly money: MoneyText
  readonly description: string | null
}

export interface CustomerInstrumentStatusChangeRequest {
  readonly instrumentId: string
  readonly expectedVersion: string
  readonly transition: CustomerInstrumentTransition
  readonly occurredOn: string
  readonly treasuryAccountId: string | null
  readonly note: string | null
}

export interface CustomerInstrumentCollectRequest {
  readonly instrumentId: string
  readonly expectedVersion: string
  readonly treasuryAccountId: string
  readonly collectedOn: string
  readonly valueOn: string | null
}

const FAIL = { ok: false } as const
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

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

function isNullableText(value: unknown): value is string | null {
  return value === null || isText(value)
}

function isNullableUuid(value: unknown): value is string | null {
  return value === null || isBoundUuid(value)
}

function isDate(value: unknown): value is string {
  return typeof value === 'string' && isDateOnly(value)
}

function decodeAmount(value: unknown, currencyId: string): AmountView | null {
  if (!isRecord(value) || !hasExactKeys(value, ['disclosure', 'money'])) return null
  if (value.disclosure === 'Visible') {
    const money = value.money
    if (!isRecord(money) || !hasExactKeys(money, ['amount', 'currencyId'])) return null
    if (!isPositiveDecimalText(money.amount) || money.currencyId !== currencyId) return null
    return { disclosure: 'Visible', money: { amount: money.amount, currencyId } }
  }
  if ((value.disclosure === 'Redacted' || value.disclosure === 'Unavailable') && value.money === null) {
    return { disclosure: value.disclosure, money: null }
  }
  return null
}

const VIEW_KEYS = [
  'id', 'companyId', 'version', 'kind', 'status', 'partyId', 'companyAccountId', 'serialNumber', 'drawer', 'bankName',
  'branchName', 'dueOn', 'receivedOn', 'currencyId', 'amount', 'description', 'treasuryAccountId', 'statusChangedOn',
  'collectionSourceId', 'receivedBy', 'receivedAtUtc',
] as const

export function decodeCustomerInstrument(value: unknown, companyId: string): Decoded<CustomerInstrumentView> {
  if (!isRecord(value) || !hasExactKeys(value, VIEW_KEYS)) return FAIL
  const v = value
  if (!isBoundUuid(v.id) || v.companyId !== companyId || !isOpaqueVersion(v.version)) return FAIL
  if (typeof v.kind !== 'string' || !KINDS.includes(v.kind) || typeof v.status !== 'string' || !STATUSES.includes(v.status)) {
    return FAIL
  }
  if (!isBoundUuid(v.partyId) || !isBoundUuid(v.companyAccountId) || !isBoundUuid(v.currencyId)) return FAIL
  if (!isText(v.serialNumber) || !isText(v.drawer) || !isNullableText(v.bankName) || !isNullableText(v.branchName)) {
    return FAIL
  }
  if (v.kind === 'Cheque' && v.bankName === null) return FAIL
  if (!isDate(v.dueOn) || !isDate(v.receivedOn) || !isDate(v.statusChangedOn)) return FAIL
  if (!isNullableText(v.description) || !isNullableUuid(v.treasuryAccountId) || !isNullableUuid(v.collectionSourceId)) {
    return FAIL
  }
  // The status and its bank / collection facts agree (the database's CK_CustomerInstruments_State, read back).
  const collected = v.status === 'Collected'
  if (collected !== (v.collectionSourceId !== null)) return FAIL
  if ((v.status === 'AtBankForCollection' || collected) !== (v.treasuryAccountId !== null)) return FAIL
  if (!isText(v.receivedBy) || !isInstant(v.receivedAtUtc)) return FAIL
  const amount = decodeAmount(v.amount, v.currencyId)
  if (amount === null) return FAIL
  return ok({
    id: v.id,
    companyId,
    version: v.version,
    kind: v.kind as CustomerInstrumentKind,
    status: v.status as CustomerInstrumentStatus,
    partyId: v.partyId,
    companyAccountId: v.companyAccountId,
    serialNumber: v.serialNumber,
    drawer: v.drawer,
    bankName: v.bankName,
    branchName: v.branchName,
    dueOn: v.dueOn,
    receivedOn: v.receivedOn,
    currencyId: v.currencyId,
    amount,
    description: v.description,
    treasuryAccountId: v.treasuryAccountId,
    statusChangedOn: v.statusChangedOn,
    collectionSourceId: v.collectionSourceId,
    receivedBy: v.receivedBy,
    receivedAtUtc: v.receivedAtUtc,
  })
}

function decodeMovement(value: unknown): CustomerInstrumentMovementView | null {
  const keys = [
    'version', 'fromStatus', 'toStatus', 'occurredOn', 'treasuryAccountId', 'collectionSourceId', 'note', 'recordedBy',
    'recordedAtUtc',
  ]
  if (!isRecord(value) || !hasExactKeys(value, keys)) return null
  if (!isOpaqueVersion(value.version) || typeof value.toStatus !== 'string' || !STATUSES.includes(value.toStatus)) return null
  if (value.fromStatus !== null && (typeof value.fromStatus !== 'string' || !STATUSES.includes(value.fromStatus))) return null
  if (!isDate(value.occurredOn) || !isNullableUuid(value.treasuryAccountId) || !isNullableUuid(value.collectionSourceId)) {
    return null
  }
  if (!isNullableText(value.note) || !isText(value.recordedBy) || !isInstant(value.recordedAtUtc)) return null
  return {
    version: value.version,
    fromStatus: value.fromStatus as CustomerInstrumentStatus | null,
    toStatus: value.toStatus as CustomerInstrumentStatus,
    occurredOn: value.occurredOn,
    treasuryAccountId: value.treasuryAccountId,
    collectionSourceId: value.collectionSourceId,
    note: value.note,
    recordedBy: value.recordedBy,
    recordedAtUtc: value.recordedAtUtc,
  }
}

export function decodeCustomerInstrumentDetail(value: unknown, companyId: string): Decoded<CustomerInstrumentDetail> {
  if (!isRecord(value) || !hasExactKeys(value, ['instrument', 'movements', 'availableActions'])) return FAIL
  const instrument = decodeCustomerInstrument(value.instrument, companyId)
  if (!instrument.ok || !Array.isArray(value.movements) || !Array.isArray(value.availableActions)) return FAIL
  const movements: CustomerInstrumentMovementView[] = []
  for (const raw of value.movements) {
    const movement = decodeMovement(raw)
    if (movement === null) return FAIL
    movements.push(movement)
  }
  const actions: CustomerInstrumentAction[] = []
  for (const raw of value.availableActions) {
    if (typeof raw !== 'string' || !ACTIONS.includes(raw)) return FAIL
    actions.push(raw as CustomerInstrumentAction)
  }
  return ok({ instrument: instrument.value, movements, availableActions: actions })
}

export function decodeCustomerInstrumentPage(value: unknown, companyId: string): Decoded<CustomerInstrumentPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items', 'nextCursor'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items) || !isNullableText(value.nextCursor)) return FAIL
  const items: CustomerInstrumentView[] = []
  for (const raw of value.items) {
    const item = decodeCustomerInstrument(raw, companyId)
    if (!item.ok) return FAIL
    items.push(item.value)
  }
  return ok({ companyId, items, nextCursor: value.nextCursor })
}

const COMMAND_OUTCOMES: readonly string[] = [
  'Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded',
]

export function decodeCustomerInstrumentCommandResult(
  value: unknown,
  companyId: string,
): Decoded<CustomerInstrumentCommandResult> {
  if (!isRecord(value) || !hasExactKeys(value, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])) {
    return FAIL
  }
  if (typeof value.outcome !== 'string' || !COMMAND_OUTCOMES.includes(value.outcome) || typeof value.isReplay !== 'boolean'
    || typeof value.originalCorrelationId !== 'string' || (value.isReplay && value.originalCorrelationId.length === 0)
    || !Array.isArray(value.issues)) {
    return FAIL
  }
  const issues: ValidationIssue[] = []
  for (const raw of value.issues) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['field', 'code']) || typeof raw.field !== 'string' || !isText(raw.code)) {
      return FAIL
    }
    issues.push({ field: raw.field, code: raw.code })
  }
  const common = { isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId, issues }
  if (value.outcome === 'Succeeded') {
    const resource = decodeCustomerInstrument(value.resource, companyId)
    return resource.ok ? ok({ outcome: 'Succeeded', resource: resource.value, ...common }) : FAIL
  }
  if (value.resource !== null) return FAIL
  return ok({
    outcome: value.outcome as Exclude<CustomerInstrumentCommandResult['outcome'], 'Succeeded'>,
    resource: null,
    ...common,
  })
}
