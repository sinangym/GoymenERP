import { isPurchasingGuid, isPurchasingInstant, isPurchasingVersion } from './contract'

/**
 * F-017 §3.5 F17-4 (ALS-D83/ALS-D85): GET …/orders/{id}/future-allocations — the active future allocations Inventory holds on
 * this order's general lines, in the purchase line unit. Read only; the quantities are the owner's text, never recomputed here.
 */
export interface PurchaseOrderFutureAllocationLinks {
  readonly orderId: string
  readonly orderVersion: string
  readonly links: readonly PurchaseOrderFutureAllocationLink[]
}

export interface PurchaseOrderFutureAllocationLink {
  readonly allocationId: string
  /** The allocation's own version; a release and a confirmed set send it back unchanged. */
  readonly version: string
  readonly lineId: string
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly unitId: string
  readonly allocatedQuantity: string
  readonly remainingQuantity: string
  readonly createdAtUtc: string
}

/** POST …/future-allocations/{allocationId}/release — the order version stays as it was (ALS-D83). */
export interface PurchaseFutureAllocationReleaseResponse {
  readonly orderId: string
  readonly orderVersion: string
  readonly allocationId: string
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

type JsonObject = Record<string, unknown>

const DECIMAL = /^(0|[1-9]\d*)(\.\d+)?$/
const LINK_FIELDS = [
  'allocationId', 'version', 'lineId', 'salesOrderId', 'salesOrderLineId', 'unitId',
  'allocatedQuantity', 'remainingQuantity', 'createdAtUtc',
] as const

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function quantity(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && DECIMAL.test(value)
}

function decodeLink(value: unknown): PurchaseOrderFutureAllocationLink | null {
  const source = record(value)
  if (source === null || !exact(source, LINK_FIELDS)) return null
  const { allocationId, version, lineId, salesOrderId, salesOrderLineId, unitId, allocatedQuantity, remainingQuantity, createdAtUtc } = source
  if (!isPurchasingGuid(allocationId) || !isPurchasingVersion(version) || !isPurchasingGuid(lineId)
    || !isPurchasingGuid(salesOrderId) || !isPurchasingGuid(salesOrderLineId) || !isPurchasingGuid(unitId)
    || !quantity(allocatedQuantity) || !quantity(remainingQuantity) || !isPurchasingInstant(createdAtUtc)) return null
  return { allocationId, version, lineId, salesOrderId, salesOrderLineId, unitId, allocatedQuantity, remainingQuantity, createdAtUtc }
}

/** Exact keys, every value typed; a duplicate allocation id or anything unknown refuses the whole body (fail closed). */
export function decodePurchaseOrderFutureAllocationLinks(value: unknown): PurchaseOrderFutureAllocationLinks | null {
  const source = record(value)
  if (source === null || !exact(source, ['orderId', 'orderVersion', 'links'])) return null
  const { orderId, orderVersion, links } = source
  if (!isPurchasingGuid(orderId) || !isPurchasingVersion(orderVersion) || !Array.isArray(links)) return null
  const decoded: PurchaseOrderFutureAllocationLink[] = []
  const seen = new Set<string>()
  for (const item of links) {
    const link = decodeLink(item)
    if (link === null || seen.has(link.allocationId.toLowerCase())) return null
    seen.add(link.allocationId.toLowerCase())
    decoded.push(link)
  }
  return { orderId, orderVersion, links: decoded }
}

export function decodePurchaseFutureAllocationReleaseResponse(value: unknown): PurchaseFutureAllocationReleaseResponse | null {
  const source = record(value)
  if (source === null || !exact(source, ['orderId', 'orderVersion', 'allocationId', 'isReplay', 'originalCorrelationId'])) return null
  const { orderId, orderVersion, allocationId, isReplay, originalCorrelationId } = source
  if (!isPurchasingGuid(orderId) || !isPurchasingVersion(orderVersion) || !isPurchasingGuid(allocationId)
    || typeof isReplay !== 'boolean' || typeof originalCorrelationId !== 'string' || originalCorrelationId.length === 0) return null
  return { orderId, orderVersion, allocationId, isReplay, originalCorrelationId }
}
