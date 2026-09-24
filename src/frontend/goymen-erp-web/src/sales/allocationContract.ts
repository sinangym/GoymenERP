export const PHYSICAL_ALLOCATIONS_PATH = '/inventory/physical-allocations' as const

export type AllocationStatus = 'Active' | 'Consumed' | 'Released'

export interface PhysicalAllocation {
  readonly allocationId: string
  readonly companyId: string
  readonly version: string
  readonly status: AllocationStatus
  readonly labelId: string
  readonly productId: string
  readonly demandKind: 'sales.order-line'
  readonly demandId: string
  readonly demandLineId: string
  readonly demandProductVersion: string
  readonly allocatedKg: string
  readonly remainingKg: string
  readonly reason: string
  readonly reservedUntilUtc: string | null
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
}

export interface PhysicalAllocationItem {
  readonly allocation: PhysicalAllocation
  readonly labelVersion: string
}

export interface PhysicalAllocationPage {
  readonly items: readonly PhysicalAllocationItem[]
  readonly searchWindowFull: boolean
}

export interface AllocationScope {
  readonly companyId: string
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly productId: string
  readonly productVersion: string
}

export interface CreatePhysicalAllocationBody {
  readonly allocationId: string
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly labelId: string
  readonly expectedLabelVersion: string
  readonly quantityKg: string
  readonly reason: string
  readonly reservedUntilUtc: string | null
}

export interface ReleasePhysicalAllocationBody {
  readonly expectedAllocationVersion: string
  readonly reason: string
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const VERSION = /^[1-9][0-9]*$/
const DECIMAL = /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/
const STATUS = new Set<AllocationStatus>(['Active', 'Consumed', 'Released'])

const record = (value: unknown): Record<string, unknown> | null =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null

const text = (value: unknown): value is string => typeof value === 'string' && value.trim() === value && value.length > 0
const guid = (value: unknown): value is string => text(value) && GUID.test(value)
const version = (value: unknown): value is string => text(value) && VERSION.test(value)
const decimal = (value: unknown): value is string => text(value) && DECIMAL.test(value)
const instant = (value: unknown): value is string => text(value) && !Number.isNaN(Date.parse(value))

export function decodePhysicalAllocation(value: unknown, expected: AllocationScope): PhysicalAllocation | null {
  const source = record(value)
  if (source === null || !guid(source.allocationId) || !guid(source.companyId) || !version(source.version)
    || typeof source.status !== 'string' || !STATUS.has(source.status as AllocationStatus)
    || !guid(source.labelId) || !guid(source.productId) || source.demandKind !== 'sales.order-line'
    || !guid(source.demandId) || !guid(source.demandLineId) || !text(source.demandProductVersion)
    || !decimal(source.allocatedKg) || !decimal(source.remainingKg) || !text(source.reason)
    || !(source.reservedUntilUtc === null || instant(source.reservedUntilUtc))
    || !instant(source.createdAtUtc) || !instant(source.updatedAtUtc)) return null

  if (source.companyId !== expected.companyId || source.demandId !== expected.salesOrderId
    || source.demandLineId !== expected.salesOrderLineId || source.productId !== expected.productId
    || source.demandProductVersion !== expected.productVersion) return null
  if (source.status === 'Active' ? source.remainingKg === '0' : source.remainingKg !== '0') return null

  return source as unknown as PhysicalAllocation
}

export function decodePhysicalAllocationPage(value: unknown, expected: AllocationScope): PhysicalAllocationPage | null {
  const source = record(value)
  if (source === null || !Array.isArray(source.items) || typeof source.searchWindowFull !== 'boolean') return null
  const items: PhysicalAllocationItem[] = []
  const ids = new Set<string>()
  for (const raw of source.items) {
    const item = record(raw)
    const allocation = item === null ? null : decodePhysicalAllocation(item.allocation, expected)
    if (item === null || allocation === null || !version(item.labelVersion) || ids.has(allocation.allocationId)) return null
    ids.add(allocation.allocationId)
    items.push({ allocation, labelVersion: item.labelVersion })
  }
  return { items, searchWindowFull: source.searchWindowFull }
}

export function problemCode(value: unknown): string | null {
  const source = record(value)
  return source !== null && text(source.code) ? source.code : null
}

export function isGuid(value: string): boolean {
  return GUID.test(value)
}

export function isPositiveVersion(value: string): boolean {
  return VERSION.test(value)
}
