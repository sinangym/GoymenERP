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

export type ManualBankPaymentStatus = 'Draft' | 'Approved' | 'Recorded'

export interface ManualBankPaymentPreparation {
  readonly partyId: string
  readonly companyAccountId: string
  readonly treasuryAccountId: string
  readonly money: MoneyText
  readonly profile: ProfileReference
  readonly occurredOn: string
  readonly valueOn: string | null
  readonly bankReference: string | null
  readonly description: string | null
}

export interface ManualBankPaymentDraftSaveRequest {
  readonly paymentId: string | null
  readonly expectedVersion: string | null
  readonly preparation: ManualBankPaymentPreparation
  readonly linkedDocument: ManualBankPaymentLinkedDocumentRequest | null
}

export interface ManualBankPaymentLinkedDocumentRequest {
  readonly documentId: string
  readonly expectedVersion: string
}

export interface ManualBankPaymentLinkedDocument {
  readonly documentId: string
  readonly version: string
}

export interface ApproveManualBankPaymentRequest {
  readonly paymentId: string
  readonly expectedVersion: string
}

export interface RecordManualBankPaymentRequest extends ApproveManualBankPaymentRequest {
  readonly bankResultConfirmed: boolean
}

export interface ManualBankPaymentView {
  readonly id: string
  readonly companyId: string
  readonly status: ManualBankPaymentStatus
  readonly preparation: ManualBankPaymentPreparation
  readonly linkedDocument: ManualBankPaymentLinkedDocument | null
  readonly preparedBy: string
  readonly preparedAtUtc: string
  readonly approvedBy: string | null
  readonly approvedAtUtc: string | null
  readonly recordedBy: string | null
  readonly recordedAtUtc: string | null
  readonly bankResultConfirmed: boolean
  readonly version: string
}

export interface ManualBankPaymentPage {
  readonly companyId: string
  readonly items: readonly ManualBankPaymentView[]
  readonly nextCursor: string | null
}

export type ManualBankPaymentCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: ManualBankPaymentView
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

function isNullableText(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isActor(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.trim() === value
}

const PREPARATION_KEYS = [
  'partyId',
  'companyAccountId',
  'treasuryAccountId',
  'money',
  'profile',
  'occurredOn',
  'valueOn',
  'bankReference',
  'description',
] as const

function decodePreparation(value: unknown): ManualBankPaymentPreparation | null {
  if (!isRecord(value) || !hasExactKeys(value, PREPARATION_KEYS)) return null
  if (
    !isBoundUuid(value.partyId)
    || !isBoundUuid(value.companyAccountId)
    || !isBoundUuid(value.treasuryAccountId)
    || !isRecord(value.money)
    || !hasExactKeys(value.money, ['amount', 'currencyId'])
    || !isPositiveDecimalText(value.money.amount)
    || !isBoundUuid(value.money.currencyId)
    || !isRecord(value.profile)
    || !hasExactKeys(value.profile, ['profileId', 'version'])
    || !isBoundUuid(value.profile.profileId)
    || !isPositiveInt64Text(value.profile.version)
    || typeof value.occurredOn !== 'string'
    || !isDateOnly(value.occurredOn)
    || !(value.valueOn === null || (typeof value.valueOn === 'string' && isDateOnly(value.valueOn)))
    || !isNullableText(value.bankReference)
    || !isNullableText(value.description)
  ) return null

  return {
    partyId: value.partyId,
    companyAccountId: value.companyAccountId,
    treasuryAccountId: value.treasuryAccountId,
    money: { amount: value.money.amount, currencyId: value.money.currencyId },
    profile: { profileId: value.profile.profileId, version: value.profile.version },
    occurredOn: value.occurredOn,
    valueOn: value.valueOn,
    bankReference: value.bankReference,
    description: value.description,
  }
}

function decodeLinkedDocument(value: unknown): ManualBankPaymentLinkedDocument | null | undefined {
  if (value === null) return null
  if (!isRecord(value) || !hasExactKeys(value, ['documentId', 'version'])) return undefined
  if (!isBoundUuid(value.documentId) || !isOpaqueVersion(value.version)) return undefined
  return { documentId: value.documentId, version: value.version }
}

const VIEW_KEYS = [
  'id',
  'companyId',
  'status',
  'preparation',
  'linkedDocument',
  'preparedBy',
  'preparedAtUtc',
  'approvedBy',
  'approvedAtUtc',
  'recordedBy',
  'recordedAtUtc',
  'bankResultConfirmed',
  'version',
] as const

export function decodeManualBankPaymentView(value: unknown, companyId: string): Decoded<ManualBankPaymentView> {
  if (!isRecord(value) || !hasExactKeys(value, VIEW_KEYS)) return FAIL
  const preparation = decodePreparation(value.preparation)
  const linkedDocument = decodeLinkedDocument(value.linkedDocument)
  if (
    !isBoundUuid(value.id)
    || value.companyId !== companyId
    || !['Draft', 'Approved', 'Recorded'].includes(value.status as string)
    || preparation === null
    || linkedDocument === undefined
    || !isActor(value.preparedBy)
    || !isInstant(value.preparedAtUtc)
    || !(value.approvedBy === null || isActor(value.approvedBy))
    || !(value.approvedAtUtc === null || isInstant(value.approvedAtUtc))
    || !(value.recordedBy === null || isActor(value.recordedBy))
    || !(value.recordedAtUtc === null || isInstant(value.recordedAtUtc))
    || typeof value.bankResultConfirmed !== 'boolean'
    || !isOpaqueVersion(value.version)
  ) return FAIL

  const status = value.status as ManualBankPaymentStatus
  const draft = value.approvedBy === null && value.approvedAtUtc === null
    && value.recordedBy === null && value.recordedAtUtc === null && value.bankResultConfirmed === false
  const approved = isActor(value.approvedBy) && isInstant(value.approvedAtUtc)
    && value.preparedBy !== value.approvedBy
    && value.recordedBy === null && value.recordedAtUtc === null && value.bankResultConfirmed === false
  const recorded = isActor(value.approvedBy) && isInstant(value.approvedAtUtc)
    && value.preparedBy !== value.approvedBy
    && isActor(value.recordedBy) && isInstant(value.recordedAtUtc) && value.bankResultConfirmed === true
  if ((status === 'Draft' && !draft) || (status === 'Approved' && !approved) || (status === 'Recorded' && !recorded)) {
    return FAIL
  }

  return ok({
    id: value.id,
    companyId,
    status,
    preparation,
    linkedDocument,
    preparedBy: value.preparedBy,
    preparedAtUtc: value.preparedAtUtc,
    approvedBy: value.approvedBy,
    approvedAtUtc: value.approvedAtUtc,
    recordedBy: value.recordedBy,
    recordedAtUtc: value.recordedAtUtc,
    bankResultConfirmed: value.bankResultConfirmed,
    version: value.version,
  })
}

export function decodeManualBankPaymentPage(value: unknown, companyId: string): Decoded<ManualBankPaymentPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items', 'nextCursor'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  if (!(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) return FAIL
  const items: ManualBankPaymentView[] = []
  for (const raw of value.items) {
    const decoded = decodeManualBankPaymentView(raw, companyId)
    if (!decoded.ok) return FAIL
    items.push(decoded.value)
  }
  return ok({ companyId, items, nextCursor: value.nextCursor })
}

export function decodeManualBankPaymentCommandResult(
  value: unknown,
  companyId: string,
): Decoded<ManualBankPaymentCommandResult> {
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
    const resource = decodeManualBankPaymentView(value.resource, companyId)
    return resource.ok ? ok({ outcome: 'Succeeded', resource: resource.value, ...common }) : FAIL
  }
  if (value.resource !== null) return FAIL
  return ok({
    outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>,
    resource: null,
    ...common,
  })
}
