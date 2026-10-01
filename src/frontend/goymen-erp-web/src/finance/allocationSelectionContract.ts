import { isBoundUuid, isPositiveInt64Text } from './contract'
import { isNonNegativeDecimalText, isPositiveDecimalText, positiveDifference, sameDecimal } from './decimalText'
import { isDateOnly } from './dateOnly'

export interface ApprovedAllocationOpenItemSourceRow {
  readonly openItemId: string
  readonly sourceDocumentId: string
  readonly sourceInstallmentId: string
  readonly sourceVersion: string
  readonly documentKind: 'official-invoice-installment'
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly amount: string
  readonly dueOn: string | null
}

export interface ApprovedAllocationOpenItemSourceBatch {
  readonly batchId: string
  readonly companyId: string
  readonly batchVersion: string
  readonly batchChecksum: string
  readonly rows: readonly ApprovedAllocationOpenItemSourceRow[]
}

export interface ApprovedAllocationOpenItemSourcePage {
  readonly companyId: string
  readonly items: readonly ApprovedAllocationOpenItemSourceBatch[]
  readonly nextCursor: string | null
}

export interface AllocationOpenItem {
  readonly id: string
  readonly companyId: string
  readonly sourceKind: 'official.invoice-installment.v1'
  readonly sourceDocumentId: string
  readonly sourceInstallmentId: string
  readonly sourceVersion: string
  readonly sourceBatchId: string
  readonly sourceBatchVersion: string
  readonly sourceBatchChecksum: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly originalAmount: string
  readonly outstandingAmount: string
  readonly dueOn: string | null
  readonly status: 1 | 2
  readonly version: string
}

export interface AppliedAllocationOpenItemBatch {
  readonly batchId: string
  readonly companyId: string
  readonly batchVersion: string
  readonly batchChecksum: string
  readonly openItems: readonly AllocationOpenItem[]
}

export type AllocationSelectionDecoded<T> = { readonly ok: true; readonly value: T } | { readonly ok: false }

const FAIL = { ok: false } as const
const SHA256 = /^[0-9a-f]{64}$/

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function exact(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function token(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= 128
    && /^[A-Za-z0-9._:/-]+$/.test(value)
}

function cursor(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.length <= 8192 && value === value.trim()
}

function dueOn(value: unknown): value is string | null {
  return value === null || (typeof value === 'string' && isDateOnly(value))
}

function sourceRow(value: unknown): ApprovedAllocationOpenItemSourceRow | null {
  const source = record(value)
  if (source === null || !exact(source, [
    'openItemId', 'sourceDocumentId', 'sourceInstallmentId', 'sourceVersion', 'documentKind',
    'partyId', 'companyAccountId', 'currencyId', 'amount', 'dueOn',
  ]) || !isBoundUuid(source.openItemId) || !isBoundUuid(source.sourceDocumentId)
    || !isBoundUuid(source.sourceInstallmentId) || !token(source.sourceVersion)
    || source.documentKind !== 'official-invoice-installment' || !isBoundUuid(source.partyId)
    || !isBoundUuid(source.companyAccountId) || !isBoundUuid(source.currencyId)
    || !isPositiveDecimalText(source.amount) || !dueOn(source.dueOn)) return null
  return source as unknown as ApprovedAllocationOpenItemSourceRow
}

function approvedBatch(value: unknown, companyId: string): ApprovedAllocationOpenItemSourceBatch | null {
  const source = record(value)
  if (source === null || !exact(source, ['batchId', 'companyId', 'batchVersion', 'batchChecksum', 'rows'])
    || !isBoundUuid(source.batchId) || source.companyId !== companyId || !token(source.batchVersion)
    || typeof source.batchChecksum !== 'string' || !SHA256.test(source.batchChecksum)
    || !Array.isArray(source.rows) || source.rows.length === 0) return null
  const rows = source.rows.map(sourceRow)
  if (rows.some((row) => row === null)) return null
  const decoded = rows as ApprovedAllocationOpenItemSourceRow[]
  if (new Set(decoded.map((row) => row.openItemId.toLowerCase())).size !== decoded.length) return null
  if (new Set(decoded.map((row) => `${row.sourceDocumentId.toLowerCase()}:${row.sourceInstallmentId.toLowerCase()}`)).size
    !== decoded.length) return null
  return {
    batchId: source.batchId as string,
    companyId,
    batchVersion: source.batchVersion,
    batchChecksum: source.batchChecksum,
    rows: decoded,
  }
}

export function decodeApprovedAllocationOpenItemSourcePage(
  value: unknown,
  companyId: string,
): AllocationSelectionDecoded<ApprovedAllocationOpenItemSourcePage> {
  const source = record(value)
  if (source === null || !exact(source, ['companyId', 'items', 'nextCursor']) || source.companyId !== companyId
    || !Array.isArray(source.items) || source.items.length > 100
    || !(source.nextCursor === null || cursor(source.nextCursor))) return FAIL
  const items = source.items.map((item) => approvedBatch(item, companyId))
  if (items.some((item) => item === null)) return FAIL
  const decoded = items as ApprovedAllocationOpenItemSourceBatch[]
  if (new Set(decoded.map((item) => item.batchId.toLowerCase())).size !== decoded.length) return FAIL
  return { ok: true, value: { companyId, items: decoded, nextCursor: source.nextCursor as string | null } }
}

function openItem(
  value: unknown,
  companyId: string,
  expected: ApprovedAllocationOpenItemSourceBatch,
): AllocationOpenItem | null {
  const source = record(value)
  const expectedRow = source === null || !isBoundUuid(source.id)
    ? null
    : expected.rows.find((row) => row.openItemId === source.id) ?? null
  if (source === null || !exact(source, [
    'id', 'companyId', 'sourceKind', 'sourceDocumentId', 'sourceInstallmentId', 'sourceVersion',
    'sourceBatchId', 'sourceBatchVersion', 'sourceBatchChecksum', 'partyId', 'companyAccountId',
    'currencyId', 'originalAmount', 'outstandingAmount', 'dueOn', 'status', 'version',
  ]) || expectedRow === null || source.companyId !== companyId
    || source.sourceKind !== 'official.invoice-installment.v1'
    || source.sourceDocumentId !== expectedRow.sourceDocumentId
    || source.sourceInstallmentId !== expectedRow.sourceInstallmentId
    || source.sourceVersion !== expectedRow.sourceVersion
    || source.sourceBatchId !== expected.batchId || source.sourceBatchVersion !== expected.batchVersion
    || source.sourceBatchChecksum !== expected.batchChecksum || source.partyId !== expectedRow.partyId
    || source.companyAccountId !== expectedRow.companyAccountId || source.currencyId !== expectedRow.currencyId
    || !isPositiveDecimalText(source.originalAmount) || source.originalAmount !== expectedRow.amount
    || !isNonNegativeDecimalText(source.outstandingAmount)
    || !dueOn(source.dueOn) || (source.status !== 1 && source.status !== 2)
    || source.dueOn !== expectedRow.dueOn || !isPositiveInt64Text(source.version)
    || !(sameDecimal(source.originalAmount, source.outstandingAmount)
      || positiveDifference(source.originalAmount, source.outstandingAmount) !== null)
    || (source.status === 1 && !isPositiveDecimalText(source.outstandingAmount))
    || (source.status === 2 && !sameDecimal(source.outstandingAmount, '0'))) return null
  return source as unknown as AllocationOpenItem
}

export function decodeAppliedAllocationOpenItemBatch(
  value: unknown,
  companyId: string,
  expected: ApprovedAllocationOpenItemSourceBatch,
): AllocationSelectionDecoded<AppliedAllocationOpenItemBatch> {
  const source = record(value)
  if (source === null || !exact(source, ['batchId', 'companyId', 'batchVersion', 'batchChecksum', 'openItems'])
    || source.batchId !== expected.batchId || source.companyId !== companyId
    || source.batchVersion !== expected.batchVersion || source.batchChecksum !== expected.batchChecksum
    || !Array.isArray(source.openItems) || source.openItems.length !== expected.rows.length) return FAIL
  const items = source.openItems.map((item) => openItem(item, companyId, expected))
  if (items.some((item) => item === null)) return FAIL
  const decoded = items as AllocationOpenItem[]
  if (new Set(decoded.map((item) => item.id.toLowerCase())).size !== decoded.length) return FAIL
  return { ok: true, value: {
    batchId: expected.batchId,
    companyId,
    batchVersion: expected.batchVersion,
    batchChecksum: expected.batchChecksum,
    openItems: decoded,
  } }
}
