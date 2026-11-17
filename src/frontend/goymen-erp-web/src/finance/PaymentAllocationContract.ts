import { decodeAllocationPositions, isBoundUuid, isPositiveInt64Text, type Decoded, type FinanceAllocationPosition } from './contract'
import { decodeFinanceAllocation, decodeFinanceAllocationCommandResult, type FinanceAllocation, type FinanceAllocationCommandResult } from './allocationContract'
import { isDateOnly } from './dateOnly'
export interface PaymentAllocationView extends Omit<FinanceAllocation, 'sourceId'> { readonly paymentId: string }
export interface PaymentAllocationItem {
  readonly openItemId: string; readonly version: string; readonly sourceKind: string; readonly sourceId: string
  readonly dueOn: string | null; readonly originalPartyId: string; readonly originalAccountId: string
  readonly positions: readonly FinanceAllocationPosition[]
}
export interface PaymentAllocationCandidates {
  readonly companyId: string; readonly paymentId: string; readonly paymentVersion: string; readonly currencyId: string
  readonly originalPartyId: string; readonly originalAccountId: string; readonly positions: readonly FinanceAllocationPosition[]
  readonly openItems: readonly PaymentAllocationItem[]; readonly pageIndex: number; readonly hasMore: boolean
  readonly allocations: readonly PaymentAllocationView[]; readonly hasMoreAllocations: boolean
}
const FAIL = { ok: false } as const
const rec = (v: unknown): Record<string, unknown> | null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Record<string, unknown> : null
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every((k) => Object.hasOwn(v, k))
/** The shared allocation contract has identical rules; only its source identity is named paymentId on this wire. */
function receiptShape(value: unknown): unknown {
  const v = rec(value)
  if (v === null || !isBoundUuid(v.paymentId) || Object.hasOwn(v, 'sourceId')) return null
  const { paymentId, ...rest } = v
  return { ...rest, sourceId: paymentId }
}
export function decodePaymentAllocation(value: unknown, companyId: string): Decoded<PaymentAllocationView> {
  const decoded = decodeFinanceAllocation(receiptShape(value), companyId)
  if (!decoded.ok) return FAIL
  const { sourceId, ...rest } = decoded.value
  return { ok: true, value: { ...rest, paymentId: sourceId } }
}
export function decodePaymentAllocationCommand(value: unknown, companyId: string): Decoded<FinanceAllocationCommandResult> {
  const v = rec(value)
  if (v === null) return FAIL
  return decodeFinanceAllocationCommandResult({ ...v, resource: v.resource === null ? null : receiptShape(v.resource) }, companyId)
}
export function decodePaymentAllocationCandidates(value: unknown, companyId: string, paymentId: string, page: number): Decoded<PaymentAllocationCandidates> {
  const v = rec(value)
  if (v === null || !exact(v, ['companyId', 'paymentId', 'paymentVersion', 'currencyId', 'originalPartyId', 'originalAccountId', 'positions', 'openItems', 'pageIndex', 'hasMore', 'allocations', 'hasMoreAllocations'])
    || v.companyId !== companyId || v.paymentId !== paymentId || !isPositiveInt64Text(v.paymentVersion) || !isBoundUuid(v.currencyId)
    || !isBoundUuid(v.originalPartyId) || !isBoundUuid(v.originalAccountId) || v.pageIndex !== page || typeof v.hasMore !== 'boolean'
    || typeof v.hasMoreAllocations !== 'boolean' || !Array.isArray(v.openItems) || v.openItems.length > 50 || !Array.isArray(v.allocations) || v.allocations.length > 50
    || decodeAllocationPositions(v.positions, v.originalPartyId, v.originalAccountId) === null) return FAIL
  const seen = new Set<string>()
  for (const raw of v.openItems) {
    const item = rec(raw)
    if (item === null || !exact(item, ['openItemId', 'version', 'sourceKind', 'sourceId', 'dueOn', 'originalPartyId', 'originalAccountId', 'positions'])
      || !isBoundUuid(item.openItemId) || seen.has(item.openItemId) || !isPositiveInt64Text(item.version) || !isBoundUuid(item.sourceId)
      || typeof item.sourceKind !== 'string' || item.sourceKind.length === 0 || !isBoundUuid(item.originalPartyId) || !isBoundUuid(item.originalAccountId)
      || !(item.dueOn === null || (typeof item.dueOn === 'string' && isDateOnly(item.dueOn)))
      || decodeAllocationPositions(item.positions, item.originalPartyId, item.originalAccountId, false) === null) return FAIL
    seen.add(item.openItemId)
  }
  const allocated = new Set<string>()
  for (const raw of v.allocations) {
    const decoded = decodePaymentAllocation(raw, companyId)
    if (!decoded.ok || decoded.value.paymentId !== paymentId || decoded.value.currencyId !== v.currencyId || allocated.has(decoded.value.id)) return FAIL
    allocated.add(decoded.value.id)
  }
  return { ok: true, value: v as unknown as PaymentAllocationCandidates }
}
