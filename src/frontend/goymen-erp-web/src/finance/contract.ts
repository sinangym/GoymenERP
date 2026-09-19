/**
 * Finance manual receipt wire contract v1 — runtime decoders and request encoders.
 *
 * SOURCE, pinned exactly: Finance Backend Draft #258 @fb482de
 *   `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Contracts/ManualReceiptV1.cs`
 *     (blob 4c8a153)
 *   `src/backend/Modules/Finance/examples/manual-receipt.normal.json` (blob 4d64909)
 *   `src/backend/Modules/Finance/examples/manual-receipt.negative.json` (blob 594f0b9)
 * serialized with `FinanceJson.CreateOptions()`: web camelCase, enums as STRINGS only,
 * `DateOnly` as `YYYY-MM-DD`, unmapped members DISALLOWED, nullable annotations respected.
 *
 * WHAT IS NOT PUBLISHED, and therefore not invented here: HTTP routes, methods, status
 * mappings, the idempotency/replay headers, a platform 403 code, grant names. Reads and
 * commands carry their outcome IN THE BODY (`ReadResult.outcome`, `CommandResult.outcome`);
 * this file decodes bodies and nothing else. The transport is `./port.ts`.
 *
 * Fail-closed rules on top of the C# shapes, each from the contract text:
 *
 *  - unknown members are rejected (`UnmappedMemberHandling.Disallow`), so a v1 client
 *    never renders a field whose meaning it does not know;
 *  - every id is a non-empty UUID (`Guid.Empty` is an identity error server-side);
 *  - source/bank `version` is an opaque non-blank string (`string Version`); only the
 *    profile version is a positive int64 in text (README "Profile versions are positive
 *    server-owned versions … lossless strings"); neither is ever parsed to a number;
 *  - `Visible` ⇔ money present; `Redacted`/`Unavailable` ⇔ money null. A redacted amount
 *    carrying digits is exactly the "hidden in the DOM" leak the spec forbids (§4);
 *  - allocation `Unavailable` ⇔ amount `Unavailable` with no money — never 0 (#258 test
 *    `UnavailableAllocationSerializesWithoutZero`);
 *  - money currency must be the source currency; every company id must be the context
 *    company (list/detail/successful resources "bind server company/source identity");
 *  - `resource` present ⇔ `Found` / `Succeeded`.
 */

import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText, isPositiveDecimalText } from './decimalText'

// ---------------------------------------------------------------------------------
// Wire guards (local on purpose — other lanes' guards belong to their single writers)
// ---------------------------------------------------------------------------------

const UUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_UUID = '00000000-0000-0000-0000-000000000000'

export function isBoundUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID_RE.test(value) && value !== NIL_UUID
}

/** Int64.MaxValue — C# `long` profile versions (Settings `ProfileVersionV1.Version`). */
const INT64_MAX = '9223372036854775807'

/**
 * A positive C# `long` as decimal text: no sign, no leading zero, at most Int64.MaxValue.
 * Used ONLY for `ProfileReference.version`, which the Backend converts from a `long`.
 * Compared as text, never parsed: 19-digit values are ordered lexicographically.
 */
export function isPositiveInt64Text(value: unknown): value is string {
  if (typeof value !== 'string' || !/^[1-9][0-9]*$/.test(value) || value.length > INT64_MAX.length) return false
  return value.length < INT64_MAX.length || value <= INT64_MAX
}

/**
 * A source / bank account concurrency token: #258 types it as `string Version` and the
 * validators only require it to be non-blank. It is OPAQUE — `etag-v1` is as valid as
 * `9007199254740993` — carried verbatim, never parsed, never compared numerically.
 */
export function isOpaqueVersion(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isInstant(value: unknown): value is string {
  if (typeof value !== 'string' || value.length > 64) return false
  if (!/^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$/.test(value)) return false
  return !Number.isNaN(Date.parse(value))
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** Exactly these members — no more, none missing (nullable members must be present). */
function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value)
  return actual.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

function isNullableText(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

// ---------------------------------------------------------------------------------
// Types (mirror ManualReceiptV1.cs)
// ---------------------------------------------------------------------------------

export interface MoneyText {
  readonly amount: string
  readonly currencyId: string
}

export interface ProfileReference {
  readonly profileId: string
  readonly version: string
}

export type SourceStage = 'Draft' | 'Recorded' | 'Reversed'
export type AmountDisclosure = 'Unavailable' | 'Redacted' | 'Visible'

export type AmountView =
  | { readonly disclosure: 'Visible'; readonly money: MoneyText }
  | { readonly disclosure: 'Redacted' | 'Unavailable'; readonly money: null }

export type UnallocatedView =
  | { readonly knowledge: 'Unavailable'; readonly amount: { readonly disclosure: 'Unavailable'; readonly money: null } }
  | { readonly knowledge: 'Known'; readonly amount: AmountView }

export interface BankAccountView {
  readonly id: string
  readonly companyId: string
  readonly currencyId: string
  readonly displayName: string
  readonly isActive: boolean
  readonly isUsableForReceipt: boolean
  readonly version: string
}

export interface BankAccountPage {
  readonly companyId: string
  readonly items: readonly BankAccountView[]
}

export interface SourceSummary {
  readonly id: string
  readonly companyId: string
  readonly sourceNumber: string
  readonly version: string
  readonly stage: SourceStage
  readonly partyId: string
  readonly companyAccountId: string
  readonly treasuryAccountId: string
  readonly currencyId: string
  readonly occurredOn: string
  readonly valueOn: string | null
  readonly amount: AmountView
}

export interface SourceDetail {
  readonly source: SourceSummary
  readonly profile: ProfileReference
  readonly bankReference: string | null
  readonly description: string | null
  readonly recordedAtUtc: string | null
  readonly reversalSourceId: string | null
  readonly reversesSourceId: string | null
  readonly unallocated: UnallocatedView
}

export interface SourcePage {
  readonly companyId: string
  readonly items: readonly SourceSummary[]
  readonly nextCursor: string | null
}

export type ReadOutcome = 'Unavailable' | 'Denied' | 'NotFound' | 'Found'

export type ReadResult<T> =
  | { readonly outcome: 'Found'; readonly resource: T }
  | { readonly outcome: 'Unavailable' | 'Denied' | 'NotFound'; readonly resource: null }

export interface ValidationIssue {
  readonly field: string
  readonly code: string
}

export type CommandOutcomeName =
  | 'Unavailable'
  | 'Denied'
  | 'ValidationFailed'
  | 'VersionConflict'
  | 'IdempotencyConflict'
  | 'Succeeded'

export type CommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: SourceDetail
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

export type Decoded<T> = { readonly ok: true; readonly value: T } | { readonly ok: false }

const FAIL = { ok: false } as const
function ok<T>(value: T): Decoded<T> {
  return { ok: true, value }
}

// ---------------------------------------------------------------------------------
// Decoders
// ---------------------------------------------------------------------------------

function decodeMoney(value: unknown, currencyId: string, allowZero: boolean): MoneyText | null {
  if (!isRecord(value) || !hasExactKeys(value, ['amount', 'currencyId'])) return null
  const amountOk = allowZero ? isNonNegativeDecimalText(value.amount) : isPositiveDecimalText(value.amount)
  if (!amountOk || value.currencyId !== currencyId) return null
  return { amount: value.amount as string, currencyId }
}

function decodeAmountView(value: unknown, currencyId: string, allowZero: boolean): AmountView | null {
  if (!isRecord(value) || !hasExactKeys(value, ['disclosure', 'money'])) return null
  if (value.disclosure === 'Visible') {
    const money = decodeMoney(value.money, currencyId, allowZero)
    return money === null ? null : { disclosure: 'Visible', money }
  }
  if ((value.disclosure === 'Redacted' || value.disclosure === 'Unavailable') && value.money === null) {
    return { disclosure: value.disclosure, money: null }
  }
  return null
}

function decodeUnallocated(value: unknown, currencyId: string): UnallocatedView | null {
  if (!isRecord(value) || !hasExactKeys(value, ['knowledge', 'amount'])) return null
  const amount = decodeAmountView(value.amount, currencyId, true)
  if (amount === null) return null
  if (value.knowledge === 'Unavailable') {
    return amount.disclosure === 'Unavailable'
      ? { knowledge: 'Unavailable', amount: { disclosure: 'Unavailable', money: null } }
      : null
  }
  if (value.knowledge === 'Known') {
    return amount.disclosure === 'Unavailable' ? null : { knowledge: 'Known', amount }
  }
  return null
}

function decodeProfile(value: unknown): ProfileReference | null {
  if (!isRecord(value) || !hasExactKeys(value, ['profileId', 'version'])) return null
  if (!isBoundUuid(value.profileId) || !isPositiveInt64Text(value.version)) return null
  return { profileId: value.profileId, version: value.version }
}

const STAGES: readonly string[] = ['Draft', 'Recorded', 'Reversed']

const SUMMARY_KEYS = [
  'id', 'companyId', 'sourceNumber', 'version', 'stage', 'partyId', 'companyAccountId',
  'treasuryAccountId', 'currencyId', 'occurredOn', 'valueOn', 'amount',
] as const

function decodeSummary(value: unknown, companyId: string): SourceSummary | null {
  if (!isRecord(value) || !hasExactKeys(value, SUMMARY_KEYS)) return null
  const v = value
  if (
    !isBoundUuid(v.id) ||
    v.companyId !== companyId ||
    typeof v.sourceNumber !== 'string' ||
    v.sourceNumber.trim().length === 0 ||
    !isOpaqueVersion(v.version) ||
    typeof v.stage !== 'string' ||
    !STAGES.includes(v.stage) ||
    !isBoundUuid(v.partyId) ||
    !isBoundUuid(v.companyAccountId) ||
    !isBoundUuid(v.treasuryAccountId) ||
    !isBoundUuid(v.currencyId) ||
    typeof v.occurredOn !== 'string' ||
    !isDateOnly(v.occurredOn) ||
    !(v.valueOn === null || (typeof v.valueOn === 'string' && isDateOnly(v.valueOn)))
  ) {
    return null
  }
  const amount = decodeAmountView(v.amount, v.currencyId, false)
  if (amount === null) return null
  return {
    id: v.id,
    companyId,
    sourceNumber: v.sourceNumber,
    version: v.version,
    stage: v.stage as SourceStage,
    partyId: v.partyId,
    companyAccountId: v.companyAccountId,
    treasuryAccountId: v.treasuryAccountId,
    currencyId: v.currencyId,
    occurredOn: v.occurredOn,
    valueOn: v.valueOn as string | null,
    amount,
  }
}

const DETAIL_KEYS = [
  'source', 'profile', 'bankReference', 'description', 'recordedAtUtc',
  'reversalSourceId', 'reversesSourceId', 'unallocated',
] as const

export function decodeSourceDetail(value: unknown, companyId: string): Decoded<SourceDetail> {
  if (!isRecord(value) || !hasExactKeys(value, DETAIL_KEYS)) return FAIL
  const source = decodeSummary(value.source, companyId)
  const profile = decodeProfile(value.profile)
  if (source === null || profile === null) return FAIL
  const unallocated = decodeUnallocated(value.unallocated, source.currencyId)
  if (
    unallocated === null ||
    !isNullableText(value.bankReference) ||
    !isNullableText(value.description) ||
    !(value.recordedAtUtc === null || isInstant(value.recordedAtUtc)) ||
    !(value.reversalSourceId === null || isBoundUuid(value.reversalSourceId)) ||
    !(value.reversesSourceId === null || isBoundUuid(value.reversesSourceId))
  ) {
    return FAIL
  }
  return ok({
    source,
    profile,
    bankReference: value.bankReference,
    description: value.description,
    recordedAtUtc: value.recordedAtUtc as string | null,
    reversalSourceId: value.reversalSourceId as string | null,
    reversesSourceId: value.reversesSourceId as string | null,
    unallocated,
  })
}

/**
 * A detail read answers ONE source. A well-formed body about another source of the same
 * company is not that answer and is refused like any other malformed response.
 */
export function decodeSourceDetailFor(sourceId: string): (value: unknown, companyId: string) => Decoded<SourceDetail> {
  return (value, companyId) => {
    const decoded = decodeSourceDetail(value, companyId)
    return decoded.ok && decoded.value.source.id === sourceId ? decoded : FAIL
  }
}

export function decodeSourcePage(value: unknown, companyId: string): Decoded<SourcePage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items', 'nextCursor'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  if (!(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) {
    return FAIL
  }
  const items: SourceSummary[] = []
  for (const raw of value.items) {
    const item = decodeSummary(raw, companyId)
    if (item === null) return FAIL
    items.push(item)
  }
  return ok({ companyId, items, nextCursor: value.nextCursor })
}

const BANK_KEYS = ['id', 'companyId', 'currencyId', 'displayName', 'isActive', 'isUsableForReceipt', 'version'] as const

export function decodeBankAccountPage(value: unknown, companyId: string): Decoded<BankAccountPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  const items: BankAccountView[] = []
  for (const raw of value.items) {
    if (!isRecord(raw) || !hasExactKeys(raw, BANK_KEYS)) return FAIL
    if (
      !isBoundUuid(raw.id) ||
      raw.companyId !== companyId ||
      !isBoundUuid(raw.currencyId) ||
      typeof raw.displayName !== 'string' ||
      raw.displayName.trim().length === 0 ||
      typeof raw.isActive !== 'boolean' ||
      typeof raw.isUsableForReceipt !== 'boolean' ||
      !isOpaqueVersion(raw.version)
    ) {
      return FAIL
    }
    items.push({
      id: raw.id,
      companyId,
      currencyId: raw.currencyId,
      displayName: raw.displayName,
      isActive: raw.isActive,
      isUsableForReceipt: raw.isUsableForReceipt,
      version: raw.version,
    })
  }
  return ok({ companyId, items })
}

const READ_OUTCOMES: readonly string[] = ['Unavailable', 'Denied', 'NotFound', 'Found']

export function decodeReadResult<T>(
  value: unknown,
  decodeResource: (resource: unknown) => Decoded<T>,
): Decoded<ReadResult<T>> {
  if (!isRecord(value) || !hasExactKeys(value, ['outcome', 'resource'])) return FAIL
  if (typeof value.outcome !== 'string' || !READ_OUTCOMES.includes(value.outcome)) return FAIL
  if (value.outcome !== 'Found') {
    return value.resource === null
      ? ok({ outcome: value.outcome as 'Unavailable' | 'Denied' | 'NotFound', resource: null })
      : FAIL
  }
  const resource = decodeResource(value.resource)
  return resource.ok ? ok({ outcome: 'Found', resource: resource.value }) : FAIL
}

const COMMAND_OUTCOMES: readonly string[] = [
  'Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded',
]

export function decodeCommandResult(value: unknown, companyId: string): Decoded<CommandResult> {
  if (!isRecord(value) || !hasExactKeys(value, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])) {
    return FAIL
  }
  if (
    typeof value.outcome !== 'string' ||
    !COMMAND_OUTCOMES.includes(value.outcome) ||
    typeof value.isReplay !== 'boolean' ||
    typeof value.originalCorrelationId !== 'string' ||
    // A replay names the first attempt's correlation. A status-only answer (403 Denied, 503
    // Unavailable, restated by the HTTP port) carries none, and is still a definite outcome.
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
    const resource = decodeSourceDetail(value.resource, companyId)
    return resource.ok ? ok({ outcome: 'Succeeded', resource: resource.value, ...common }) : FAIL
  }
  if (value.resource !== null) return FAIL
  return ok({ outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}

// ---------------------------------------------------------------------------------
// Request encoders — exact member sets of DraftSaveRequest / RecordRequest / ReverseRequest
// ---------------------------------------------------------------------------------

export interface ReceiptPreparation {
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

/** Create sends `draftId` and `expectedVersion` both null; update sends both (README). */
export type DraftIdentity =
  | { readonly draftId: null; readonly expectedVersion: null }
  | { readonly draftId: string; readonly expectedVersion: string }

export interface DraftSaveRequest {
  readonly draftId: string | null
  readonly expectedVersion: string | null
  readonly preparation: ReceiptPreparation
}

export interface RecordRequest {
  readonly draftId: string
  readonly expectedVersion: string
}

export interface ReverseRequest {
  readonly sourceId: string
  readonly expectedVersion: string
  readonly occurredOn: string
  readonly reason: string
}

export function draftSaveRequest(identity: DraftIdentity, preparation: ReceiptPreparation): DraftSaveRequest {
  return { draftId: identity.draftId, expectedVersion: identity.expectedVersion, preparation }
}
