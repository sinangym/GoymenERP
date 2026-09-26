import { instructionDetailPath, type InstructionLine, type LoadingShare } from './contract'

export const REALIZATION_CAPABILITY = {
  shipment: 'logistics.shipments.record',
  delivery: 'logistics.deliveries.record',
  allocation: 'inventory.physical-allocation.write',
} as const

export const shipmentPath = (id: string, line: string) => `${instructionDetailPath(id)}/lines/${encodeURIComponent(line)}/shipments`
export const deliveryPath = (id: string, line: string) => `${instructionDetailPath(id)}/lines/${encodeURIComponent(line)}/deliveries`
export const SHIPMENT_ALLOCATIONS_PATH = '/inventory/physical-allocations'

export interface RecordShipmentRequest {
  readonly expectedVersion: string
  readonly shipmentId: string
  readonly allocationId: string
  readonly expectedAllocationVersion: string
  readonly expectedLabelVersion: string
  readonly movementId: string
  readonly quantity: string
  readonly occurredAtUtc: string
}

export interface RecordDeliveryRequest {
  readonly expectedVersion: string
  readonly deliveryId: string
  readonly sourceShipmentId: string
  readonly quantity: string
  readonly occurredAtUtc: string
}

/** Price-free projection of InventoryPhysicalAllocationReadItemV1, not a second allocation engine. */
export interface ShipmentAllocation {
  readonly allocationId: string
  readonly labelId: string
  readonly allocationVersion: string
  readonly labelVersion: string
  readonly remainingKg: string
  readonly status: 'Active' | 'Consumed' | 'Released'
}

export interface ShipmentAllocationPage {
  readonly items: readonly ShipmentAllocation[]
  readonly searchWindowFull: boolean
}

const object = (value: unknown): Record<string, unknown> | null =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : null
const guid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000'
const version = (value: unknown): value is string => typeof value === 'string' && /^[1-9][0-9]{0,18}$/.test(value)
const decimal = (value: unknown): value is string => typeof value === 'string' && /^(?:0|[1-9][0-9]*)(?:\.[0-9]{1,3})?$/.test(value)

/**
 * An instruction does not publish ProductVersion. Do not invent it or import a Sales-private decoder.
 * Bind the public Inventory read to the actual company/order/line/product and retain exact owner versions;
 * the command service checks the current approved Sales source before the participant stages stock.
 */
export function decodeShipmentAllocations(value: unknown, companyId: string, line: InstructionLine): ShipmentAllocationPage | null {
  const source = object(value)
  if (source === null || !Array.isArray(source.items) || source.items.length > 100 || typeof source.searchWindowFull !== 'boolean') return null
  const items: ShipmentAllocation[] = []
  const seen = new Set<string>()
  for (const raw of source.items) {
    const item = object(raw)
    const allocation = object(item?.allocation)
    if (item === null || allocation === null || !guid(allocation.allocationId) || seen.has(allocation.allocationId)
      || allocation.companyId !== companyId || allocation.demandId !== line.sourceOrderId
      || allocation.demandLineId !== line.sourceOrderLineId || allocation.productId !== line.productId
      || allocation.demandKind !== 'sales.order-line' || !guid(allocation.labelId)
      || !version(allocation.version) || !version(item.labelVersion)
      || typeof allocation.demandProductVersion !== 'string' || allocation.demandProductVersion.trim().length === 0
      || !decimal(allocation.remainingKg) || !decimal(allocation.allocatedKg)
      || (allocation.status !== 'Active' && allocation.status !== 'Consumed' && allocation.status !== 'Released')) return null
    const zero = /^0(?:\.0+)?$/.test(allocation.remainingKg)
    if (allocation.status === 'Active' ? zero : !zero) return null
    seen.add(allocation.allocationId)
    items.push({ allocationId: allocation.allocationId, labelId: allocation.labelId, allocationVersion: allocation.version,
      labelVersion: item.labelVersion, remainingKg: allocation.remainingKg, status: allocation.status })
  }
  return { items, searchWindowFull: source.searchWindowFull }
}

/** Display-only exact remainder of one source shipment. The backend, never this value, enforces the cap. */
export function shipmentRemainder(shipment: LoadingShare, shares: readonly LoadingShare[]): string {
  const scaled = (text: string) => {
    const [whole, fraction = ''] = text.split('.')
    return BigInt(whole) * 1000n + BigInt(fraction.padEnd(3, '0'))
  }
  const remaining = scaled(shipment.quantity) - shares
    .filter((share) => share.kind === 'Delivered' && share.lineId === shipment.lineId && share.sourceShipmentId === shipment.shareId)
    .reduce((sum, share) => sum + scaled(share.quantity), 0n)
  if (remaining <= 0n) return '0'
  const fraction = (remaining % 1000n).toString().padStart(3, '0').replace(/0+$/, '')
  return `${remaining / 1000n}${fraction === '' ? '' : `.${fraction}`}`
}
