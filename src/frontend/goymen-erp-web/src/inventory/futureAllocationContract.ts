export interface FutureAllocation {
  readonly allocationId: string; readonly companyId: string; readonly version: string; readonly status: 'Active' | 'Consumed' | 'Released'
  readonly salesOrderId: string; readonly salesOrderLineId: string; readonly purchaseOrderId: string; readonly purchaseOrderLineId: string
  readonly productId: string; readonly productVersion: string; readonly unitId: string; readonly allocatedQuantity: string
  readonly remainingQuantity: string; readonly releasedQuantity: string; readonly consumedQuantity: string; readonly manualSequence: number | null
  readonly reason: string; readonly releaseReasonCode: string | null; readonly releaseOrigin: string | null; readonly releaseReason: string | null
  readonly createdAtUtc: string; readonly updatedAtUtc: string; readonly releasedAtUtc: string | null
}
export interface FutureAllocationList {
  readonly companyId: string; readonly salesOrderId: string; readonly salesOrderLineId: string
  readonly items: readonly FutureAllocation[]; readonly windowFull: boolean; readonly unitCodes: Readonly<Record<string, string>>
}
export interface FutureSupplyOption {
  readonly purchaseOrderId: string; readonly purchaseOrderLineId: string; readonly lineNo: number; readonly supplierCode: string
  readonly supplierName: string; readonly unitId: string; readonly unitCode: string; readonly allocatableQuantity: string
  readonly reservedQuantity: string; readonly availableQuantity: string
}
export interface FutureSupplyOptions {
  readonly companyId: string; readonly salesOrderId: string; readonly salesOrderLineId: string; readonly productId: string
  readonly productVersion: string; readonly demandUnitId: string; readonly demandUnitCode: string; readonly openDemandQuantity: string
  readonly items: readonly FutureSupplyOption[]; readonly nextCursor: string | null
}
export interface CreateFutureAllocation {
  readonly allocationId: string; readonly salesOrderId: string; readonly salesOrderLineId: string; readonly purchaseOrderId: string
  readonly purchaseOrderLineId: string; readonly unitId: string; readonly quantityText: string; readonly reason: string
}
export interface ReleaseFutureAllocation {
  readonly expectedAllocationVersion: string; readonly quantityText: null; readonly reasonCode: 'manual-release'; readonly reasonText: string
}

export const futureGuid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(v)
  && v !== '00000000-0000-0000-0000-000000000000'
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0
const nullableText = (v: unknown) => v === null || text(v)
const version = (v: unknown): v is string => typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n
const instant = (v: unknown) => typeof v === 'string' && v.length <= 64 && /(?:Z|[+-]\d{2}:\d{2})$/.test(v) && !Number.isNaN(Date.parse(v))
const quantity = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,14})(?:\.\d{1,3})?$/.test(v)
const obj = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, fields: readonly string[]) => Object.keys(v).length === fields.length && fields.every(f => Object.hasOwn(v, f))
const allocationFields = ['allocationId', 'companyId', 'version', 'status', 'salesOrderId', 'salesOrderLineId', 'purchaseOrderId', 'purchaseOrderLineId',
  'productId', 'productVersion', 'unitId', 'allocatedQuantity', 'remainingQuantity', 'releasedQuantity', 'consumedQuantity', 'manualSequence', 'reason',
  'releaseReasonCode', 'releaseOrigin', 'releaseReason', 'createdAtUtc', 'updatedAtUtc', 'releasedAtUtc']

export function decodeFutureAllocation(value: unknown): FutureAllocation | null {
  if (!obj(value) || !exact(value, allocationFields)
    || !['allocationId', 'companyId', 'salesOrderId', 'salesOrderLineId', 'purchaseOrderId', 'purchaseOrderLineId', 'productId', 'unitId'].every(key => futureGuid(value[key]))
    || !version(value.version) || !version(value.productVersion) || !['Active', 'Consumed', 'Released'].includes(String(value.status))
    || !['allocatedQuantity', 'remainingQuantity', 'releasedQuantity', 'consumedQuantity'].every(key => quantity(value[key]))
    || !(value.manualSequence === null || typeof value.manualSequence === 'number' && Number.isSafeInteger(value.manualSequence) && value.manualSequence >= 0)
    || !text(value.reason) || !nullableText(value.releaseReasonCode) || !(value.releaseOrigin === null || ['inventory', 'sales', 'purchasing'].includes(String(value.releaseOrigin)))
    || !nullableText(value.releaseReason) || !instant(value.createdAtUtc) || !instant(value.updatedAtUtc)
    || !(value.releasedAtUtc === null || instant(value.releasedAtUtc))) return null
  return value as unknown as FutureAllocation
}

export function decodeFutureAllocationList(value: unknown): FutureAllocationList | null {
  if (!obj(value) || !exact(value, ['companyId', 'salesOrderId', 'salesOrderLineId', 'items', 'windowFull', 'unitCodes'])
    || !futureGuid(value.companyId) || !futureGuid(value.salesOrderId) || !futureGuid(value.salesOrderLineId) || !Array.isArray(value.items)
    || value.items.length > 100 || typeof value.windowFull !== 'boolean' || !obj(value.unitCodes)
    || Object.entries(value.unitCodes).some(([key, name]) => !futureGuid(key) || !text(name))) return null
  const ids = new Set<string>()
  for (const raw of value.items) {
    const item = decodeFutureAllocation(raw)
    if (item === null || item.companyId !== value.companyId || item.salesOrderId !== value.salesOrderId || item.salesOrderLineId !== value.salesOrderLineId
      || ids.has(item.allocationId) || !Object.hasOwn(value.unitCodes, item.unitId)) return null
    ids.add(item.allocationId)
  }
  return value as unknown as FutureAllocationList
}

export function decodeFutureSupplyOptions(value: unknown): FutureSupplyOptions | null {
  if (!obj(value) || !exact(value, ['companyId', 'salesOrderId', 'salesOrderLineId', 'productId', 'productVersion', 'demandUnitId', 'demandUnitCode', 'openDemandQuantity', 'items', 'nextCursor'])
    || !['companyId', 'salesOrderId', 'salesOrderLineId', 'productId', 'demandUnitId'].every(key => futureGuid(value[key]))
    || !version(value.productVersion) || !text(value.demandUnitCode) || !quantity(value.openDemandQuantity) || !Array.isArray(value.items) || value.items.length > 25
    || !(value.nextCursor === null || text(value.nextCursor) && value.nextCursor.length <= 4096)) return null
  const ids = new Set<string>()
  for (const item of value.items) {
    if (!obj(item) || !exact(item, ['purchaseOrderId', 'purchaseOrderLineId', 'lineNo', 'supplierCode', 'supplierName', 'unitId', 'unitCode',
      'allocatableQuantity', 'reservedQuantity', 'availableQuantity']) || !futureGuid(item.purchaseOrderId) || !futureGuid(item.purchaseOrderLineId)
      || !futureGuid(item.unitId) || typeof item.lineNo !== 'number' || !Number.isSafeInteger(item.lineNo) || item.lineNo < 1
      || !text(item.supplierCode) || !text(item.supplierName) || !text(item.unitCode)
      || !quantity(item.allocatableQuantity) || !quantity(item.reservedQuantity) || !quantity(item.availableQuantity) || ids.has(item.purchaseOrderLineId)) return null
    ids.add(item.purchaseOrderLineId)
  }
  return value as unknown as FutureSupplyOptions
}
