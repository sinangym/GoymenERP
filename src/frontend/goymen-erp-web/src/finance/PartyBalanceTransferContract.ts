import { isBoundUuid, isInstant, isPositiveInt64Text, type Decoded } from './contract'
import { isNonNegativeDecimalText } from './decimalText'
import { isDateOnly } from './dateOnly'

export interface BalanceTransferSelection {
  readonly predecessorPartyId: string
  readonly predecessorAccountId: string
  readonly successorPartyId: string
  readonly successorAccountId: string
}
export interface BalanceTransferOwner extends BalanceTransferSelection {
  readonly companyId: string
  readonly predecessorPartyVersion: string
  readonly predecessorAccountVersion: string
  readonly predecessorCode: string
  readonly predecessorName: string
  readonly successorPartyVersion: string
  readonly successorAccountVersion: string
  readonly successorCode: string
  readonly successorName: string
  readonly linkKind: 'Succession' | 'DuplicateMerge'
  readonly effectiveOn: string | null
  readonly legalBasis: string | null
  readonly linkRecordedAtUtc: string | null
}
export interface BalanceTransferCurrency {
  readonly currencyId: string
  readonly side: 'Zero' | 'Debit' | 'Credit'
  readonly amount: string
  readonly openReceivable: string
  readonly openPayable: string
}
export interface BalanceTransferPreview {
  readonly companyId: string
  readonly ownerProof: BalanceTransferOwner
  readonly snapshotVersion: string
  readonly currencies: readonly BalanceTransferCurrency[]
  readonly openItemCount: number
  readonly receiptCount: number
  readonly paymentCount: number
  readonly canRecord: boolean
}
export interface BalanceTransferEvidence { readonly documentId: string; readonly versionNo: number; readonly sha256: string }
export interface BalanceTransferRecord {
  readonly transferId: string
  readonly selection: BalanceTransferSelection
  readonly expectedSnapshotVersion: string
  readonly occurredOn: string
  readonly reason: string
  readonly evidence: BalanceTransferEvidence
}
export interface BalanceTransferView {
  readonly id: string
  readonly companyId: string
  readonly version: string
  readonly ownerProof: BalanceTransferOwner
  readonly currencies: readonly BalanceTransferCurrency[]
  readonly occurredOn: string
  readonly reason: string
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly evidence: BalanceTransferEvidence
}
export interface BalanceTransferResult {
  readonly outcome: 'Succeeded' | 'Unavailable' | 'Denied' | 'VersionConflict' | 'IdempotencyConflict' | 'ValidationFailed'
  readonly resource: BalanceTransferView | null
  readonly code: string | null
  readonly replayed: boolean
}
const FAIL = { ok: false } as const
const hash = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{64}$/i.test(v)
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() === v && v.length > 0
const rec = (v: unknown): Record<string, unknown> | null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Record<string, unknown> : null
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every((k) => Object.hasOwn(v, k))
const ownerKeys = ['companyId', 'predecessorPartyId', 'predecessorAccountId', 'predecessorPartyVersion', 'predecessorAccountVersion', 'predecessorCode', 'predecessorName', 'successorPartyId', 'successorAccountId', 'successorPartyVersion', 'successorAccountVersion', 'successorCode', 'successorName', 'linkKind', 'effectiveOn', 'legalBasis', 'linkRecordedAtUtc']
function owner(value: unknown, companyId: string): BalanceTransferOwner | null {
  const v = rec(value)
  if (v === null || !exact(v, ownerKeys) || v.companyId !== companyId
    || !['predecessorPartyId', 'predecessorAccountId', 'successorPartyId', 'successorAccountId'].every((k) => isBoundUuid(v[k]))
    || v.predecessorPartyId === v.successorPartyId || v.predecessorAccountId === v.successorAccountId
    || !['predecessorPartyVersion', 'predecessorAccountVersion', 'successorPartyVersion', 'successorAccountVersion'].every((k) => isPositiveInt64Text(v[k]))
    || !['predecessorCode', 'predecessorName', 'successorCode', 'successorName'].every((k) => text(v[k]))
    || (v.linkKind !== 'Succession' && v.linkKind !== 'DuplicateMerge')
    || !(v.effectiveOn === null || (typeof v.effectiveOn === 'string' && isDateOnly(v.effectiveOn)))
    || !(v.legalBasis === null || text(v.legalBasis)) || !(v.linkRecordedAtUtc === null || isInstant(v.linkRecordedAtUtc))) return null
  return v as unknown as BalanceTransferOwner
}
function currencies(value: unknown): readonly BalanceTransferCurrency[] | null {
  if (!Array.isArray(value)) return null
  const seen = new Set<string>()
  for (const item of value) {
    const v = rec(item)
    if (v === null || !exact(v, ['currencyId', 'side', 'amount', 'openReceivable', 'openPayable']) || !isBoundUuid(v.currencyId)
      || seen.has(v.currencyId) || !['Zero', 'Debit', 'Credit'].includes(v.side as string)
      || !isNonNegativeDecimalText(v.amount) || !isNonNegativeDecimalText(v.openReceivable) || !isNonNegativeDecimalText(v.openPayable)
      || ((v.side === 'Zero') !== !/[1-9]/.test(v.amount))) return null
    seen.add(v.currencyId)
  }
  return value as BalanceTransferCurrency[]
}
function evidence(value: unknown): BalanceTransferEvidence | null {
  const v = rec(value)
  return v !== null && exact(v, ['documentId', 'versionNo', 'sha256']) && isBoundUuid(v.documentId)
    && typeof v.versionNo === 'number' && Number.isSafeInteger(v.versionNo) && v.versionNo > 0 && hash(v.sha256)
    ? v as unknown as BalanceTransferEvidence : null
}
export function decodeBalanceTransferPreview(value: unknown, companyId: string,
  expected: BalanceTransferSelection): Decoded<BalanceTransferPreview> {
  const envelope = rec(value)
  if (envelope === null || !exact(envelope, ['outcome', 'preview', 'transfer']) || envelope.outcome !== 'Found' || envelope.transfer !== null) return FAIL
  const v = rec(envelope.preview)
  if (v === null || !exact(v, ['companyId', 'ownerProof', 'snapshotVersion', 'currencies', 'openItemCount', 'receiptCount', 'paymentCount', 'canRecord'])
    || v.companyId !== companyId || !hash(v.snapshotVersion) || typeof v.canRecord !== 'boolean'
    || !['openItemCount', 'receiptCount', 'paymentCount'].every((k) => typeof v[k] === 'number' && Number.isSafeInteger(v[k]) && (v[k] as number) >= 0)) return FAIL
  const proof = owner(v.ownerProof, companyId)
  const money = currencies(v.currencies)
  if (proof === null || money === null || !Object.entries(expected).every(([k, item]) => proof[k as keyof BalanceTransferSelection] === item)) return FAIL
  return { ok: true, value: v as unknown as BalanceTransferPreview }
}
export function decodeBalanceTransferView(value: unknown, companyId: string): Decoded<BalanceTransferView> {
  const v = rec(value)
  if (v === null || !exact(v, ['id', 'companyId', 'version', 'ownerProof', 'currencies', 'occurredOn', 'reason', 'recordedBy', 'recordedAtUtc', 'evidence'])
    || !isBoundUuid(v.id) || v.companyId !== companyId || v.version !== '1' || owner(v.ownerProof, companyId) === null
    || currencies(v.currencies) === null || typeof v.occurredOn !== 'string' || !isDateOnly(v.occurredOn)
    || !text(v.reason) || !text(v.recordedBy) || !isInstant(v.recordedAtUtc) || evidence(v.evidence) === null) return FAIL
  return { ok: true, value: v as unknown as BalanceTransferView }
}
export function decodeBalanceTransferResult(value: unknown, companyId: string, request: BalanceTransferRecord): Decoded<BalanceTransferResult> {
  const v = rec(value)
  if (v === null || !exact(v, ['outcome', 'resource', 'code', 'replayed']) || typeof v.replayed !== 'boolean'
    || !(v.code === null || text(v.code)) || !['Succeeded', 'Unavailable', 'Denied', 'VersionConflict', 'IdempotencyConflict', 'ValidationFailed'].includes(v.outcome as string)) return FAIL
  if (v.outcome === 'Succeeded') {
    const decoded = decodeBalanceTransferView(v.resource, companyId)
    if (!decoded.ok || decoded.value.id !== request.transferId || decoded.value.occurredOn !== request.occurredOn
      || decoded.value.reason !== request.reason || decoded.value.evidence.documentId !== request.evidence.documentId
      || decoded.value.evidence.versionNo !== request.evidence.versionNo || decoded.value.evidence.sha256 !== request.evidence.sha256
      || !Object.entries(request.selection).every(([k, item]) => decoded.value.ownerProof[k as keyof BalanceTransferSelection] === item)) return FAIL
  } else if (v.resource !== null) return FAIL
  return { ok: true, value: v as unknown as BalanceTransferResult }
}
