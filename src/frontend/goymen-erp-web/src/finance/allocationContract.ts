import type { Decoded, ValidationIssue } from './contract'

export type FinanceAllocationStatus = 'Active' | 'Released'

export interface FinanceAllocation {
  readonly id: string
  readonly companyId: string
  readonly sourceId: string
  readonly openItemId: string
  readonly currencyId: string
  readonly amount: string
  readonly status: FinanceAllocationStatus
  readonly version: string
  readonly reason: string
  readonly createdBy: string
  readonly createdAtUtc: string
  readonly releasedBy: string | null
  readonly releasedAtUtc: string | null
  readonly releaseReason: string | null
}

export type FinanceAllocationCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: FinanceAllocation
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly []
    }
  | {
      readonly outcome: 'Unavailable' | 'Denied' | 'ValidationFailed' | 'VersionConflict' | 'IdempotencyConflict'
      readonly resource: null
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly ValidationIssue[]
    }

export interface CreateFinanceAllocationRequest {
  readonly allocationId: string
  readonly sourceId: string
  readonly expectedSourceVersion: string
  readonly openItemId: string
  readonly expectedOpenItemVersion: string
  readonly amount: string
  readonly reason: string
}

export interface ReleaseFinanceAllocationRequest {
  readonly allocationId: string
  readonly expectedAllocationVersion: string
  readonly expectedOpenItemVersion: string
  readonly reason: string
}

export interface FinanceAllocationPage {
  readonly companyId: string
  readonly items: readonly FinanceAllocation[]
  readonly windowFull: boolean
}

export interface FinanceAllocationDetail {
  readonly allocation: FinanceAllocation
  readonly openItemVersion: string
}

const FAIL = { ok: false } as const
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const VERSION = /^[1-9][0-9]*$/
const AMOUNT = /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/
const OUTCOMES = ['Succeeded', 'Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict'] as const

function ok<T>(value: T): Decoded<T> {
  return { ok: true, value }
}

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function exact(value: Record<string, unknown>, keys: readonly string[]): boolean {
  return Object.keys(value).sort().join('|') === [...keys].sort().join('|')
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.trim() === value
}

function guid(value: unknown): value is string {
  return text(value) && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
}

function instant(value: unknown): value is string {
  return text(value) && /^\d{4}-\d{2}-\d{2}T.*(?:Z|\+00:00)$/.test(value) && !Number.isNaN(Date.parse(value))
}

function issue(value: unknown): ValidationIssue | null {
  const source = record(value)
  return source !== null && exact(source, ['field', 'code']) && text(source.field) && text(source.code)
    ? { field: source.field, code: source.code }
    : null
}

export function decodeFinanceAllocation(value: unknown, companyId: string): Decoded<FinanceAllocation> {
  const source = record(value)
  if (source === null || !exact(source, [
    'id', 'companyId', 'sourceId', 'openItemId', 'currencyId', 'amount', 'status', 'version', 'reason',
    'createdBy', 'createdAtUtc', 'releasedBy', 'releasedAtUtc', 'releaseReason',
  ])) return FAIL
  if (!guid(source.id) || !guid(source.companyId) || source.companyId !== companyId || !guid(source.sourceId) ||
      !guid(source.openItemId) || !guid(source.currencyId) || !text(source.amount) || !AMOUNT.test(source.amount) ||
      !/[1-9]/.test(source.amount) || !text(source.version) || !VERSION.test(source.version) || !text(source.reason) ||
      !text(source.createdBy) || !instant(source.createdAtUtc) ||
      (source.status !== 'Active' && source.status !== 'Released')) return FAIL

  const releasedBy = source.releasedBy === null ? null : text(source.releasedBy) ? source.releasedBy : undefined
  const releasedAtUtc = source.releasedAtUtc === null ? null : instant(source.releasedAtUtc) ? source.releasedAtUtc : undefined
  const releaseReason = source.releaseReason === null ? null : text(source.releaseReason) ? source.releaseReason : undefined
  if (releasedBy === undefined || releasedAtUtc === undefined || releaseReason === undefined) return FAIL
  if ((source.status === 'Active' && (releasedBy !== null || releasedAtUtc !== null || releaseReason !== null)) ||
      (source.status === 'Released' && (releasedBy === null || releasedAtUtc === null || releaseReason === null))) return FAIL

  return ok({
    id: source.id, companyId: source.companyId, sourceId: source.sourceId, openItemId: source.openItemId,
    currencyId: source.currencyId, amount: source.amount, status: source.status, version: source.version,
    reason: source.reason, createdBy: source.createdBy, createdAtUtc: source.createdAtUtc,
    releasedBy, releasedAtUtc, releaseReason,
  })
}

export function decodeFinanceAllocationCommandResult(
  value: unknown,
  companyId: string,
): Decoded<FinanceAllocationCommandResult> {
  const source = record(value)
  if (source === null || !exact(source, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues']) ||
      !OUTCOMES.includes(source.outcome as (typeof OUTCOMES)[number]) || typeof source.isReplay !== 'boolean' ||
      typeof source.originalCorrelationId !== 'string' || !Array.isArray(source.issues) ||
      (source.isReplay && source.originalCorrelationId.length === 0)) return FAIL
  const issues = source.issues.map(issue)
  if (issues.some((item) => item === null)) return FAIL

  if (source.outcome === 'Succeeded') {
    const resource = decodeFinanceAllocation(source.resource, companyId)
    return resource.ok && issues.length === 0
      ? ok({ outcome: 'Succeeded', resource: resource.value, isReplay: source.isReplay,
        originalCorrelationId: source.originalCorrelationId, issues: [] })
      : FAIL
  }
  if (source.resource !== null) return FAIL
  return ok({
    outcome: source.outcome as Exclude<(typeof OUTCOMES)[number], 'Succeeded'>,
    resource: null,
    isReplay: source.isReplay,
    originalCorrelationId: source.originalCorrelationId,
    issues: issues as ValidationIssue[],
  })
}

export function decodeFinanceAllocationPage(value: unknown, companyId: string): Decoded<FinanceAllocationPage> {
  const source = record(value)
  if (source === null || !exact(source, ['companyId', 'items', 'windowFull']) || source.companyId !== companyId
      || !Array.isArray(source.items) || typeof source.windowFull !== 'boolean') return FAIL
  const items: FinanceAllocation[] = []
  const allocationIds = new Set<string>()
  for (const item of source.items) {
    const decoded = decodeFinanceAllocation(item, companyId)
    if (!decoded.ok) return FAIL
    if (allocationIds.has(decoded.value.id)) return FAIL
    allocationIds.add(decoded.value.id)
    items.push(decoded.value)
  }
  if (items.length > 50 || (source.windowFull && items.length !== 50)) return FAIL
  return ok({ companyId, items, windowFull: source.windowFull })
}

export function decodeFinanceAllocationDetail(value: unknown, companyId: string): Decoded<FinanceAllocationDetail> {
  const source = record(value)
  if (source === null || !exact(source, ['allocation', 'openItemVersion'])
      || !text(source.openItemVersion) || !VERSION.test(source.openItemVersion)) return FAIL
  const allocation = decodeFinanceAllocation(source.allocation, companyId)
  return allocation.ok ? ok({ allocation: allocation.value, openItemVersion: source.openItemVersion }) : FAIL
}
