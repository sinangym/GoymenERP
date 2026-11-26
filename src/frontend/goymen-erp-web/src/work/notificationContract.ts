export const NOTIFICATIONS_PATH = '/work/notifications'
export const NOTIFICATIONS_ROUTE = '/bildirimlerim'
export interface WorkNotification { readonly id: string; readonly companyId: string; readonly kind: 'allocation-released' | 'purchase-supply-revived' | 'accounting-cost-proposal-stale'; readonly costKind?: 1 | 2; readonly salesOrderId: string | null; readonly salesOrderLineId: string | null; readonly purchaseOrderId: string | null; readonly purchaseOrderLineId: string | null; readonly sourceEntityId: string; readonly sourceVersion: string; readonly reasonCode: string; readonly reasonText: string; readonly occurredAtUtc: string; readonly recordedAtUtc: string; readonly passivatedAtUtc: string | null }
export interface WorkNotifications { readonly companyId: string; readonly items: readonly WorkNotification[]; readonly hasMore: boolean; readonly nextCursor: string | null }
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, names: readonly string[]) => Object.keys(value).length === names.length && names.every((name) => Object.hasOwn(value, name))
const guid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const instant = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(value) && Number.isFinite(Date.parse(value))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= max
const fields = ['id', 'companyId', 'kind', 'salesOrderId', 'salesOrderLineId', 'purchaseOrderId', 'purchaseOrderLineId', 'sourceEntityId', 'sourceVersion', 'reasonCode', 'reasonText', 'occurredAtUtc', 'recordedAtUtc', 'passivatedAtUtc']
const validTarget = (item: Record<string, unknown>) => item.kind === 'accounting-cost-proposal-stale'
  ? item.salesOrderId === null && item.salesOrderLineId === null && item.purchaseOrderId === null && item.purchaseOrderLineId === null
  : (guid(item.salesOrderId) && guid(item.salesOrderLineId) && item.purchaseOrderId === null && item.purchaseOrderLineId === null)
  || (item.kind === 'purchase-supply-revived' && item.salesOrderId === null && item.salesOrderLineId === null && guid(item.purchaseOrderId) && guid(item.purchaseOrderLineId))
/** Shared price-free evidence of YOL-D40; both legacy page and unified digest keep exact wire shapes around it. */
export const validAccountingCostStaleEvent = (item: Record<string, unknown>): boolean => guid(item.sourceEntityId)
  && (item.costKind === 1 || item.costKind === 2) && item.reasonCode === 'inventory_cost_preparation_stale' && item.reasonText === 'Bayatladı'
  && typeof item.sourceVersion === 'string' && /^[1-9]\d{0,18}$/.test(item.sourceVersion)
  && BigInt(item.sourceVersion) >= 3n && BigInt(item.sourceVersion) <= 9223372036854775807n
  && item.salesOrderId === null && item.salesOrderLineId === null && item.purchaseOrderId === null && item.purchaseOrderLineId === null
export function decodeWorkNotifications(raw: unknown, companyId: string): WorkNotifications | null {
  if (!object(raw) || !exact(raw, ['companyId', 'items', 'hasMore', 'nextCursor']) || !guid(companyId) || raw.companyId !== companyId || typeof raw.hasMore !== 'boolean' || !Array.isArray(raw.items) || raw.items.length > 100 || (raw.hasMore ? raw.items.length !== 100 || !guid(raw.nextCursor) : raw.nextCursor !== null)) return null
  const items: WorkNotification[] = []
  for (const item of raw.items) {
    if (!object(item) || !exact(item, item.kind === 'accounting-cost-proposal-stale' ? [...fields, 'costKind'] : fields) || !guid(item.id) || item.companyId !== companyId || (item.kind !== 'allocation-released' && item.kind !== 'purchase-supply-revived' && item.kind !== 'accounting-cost-proposal-stale')
      || !validTarget(item) || !guid(item.sourceEntityId) || typeof item.sourceVersion !== 'string'
      || !/^[1-9]\d{0,18}$/.test(item.sourceVersion) || BigInt(item.sourceVersion) > 9223372036854775807n
      || !text(item.reasonCode, 64) || !text(item.reasonText, 4000)
      || (item.kind === 'accounting-cost-proposal-stale'
        ? !validAccountingCostStaleEvent(item)
        : !/^[a-z]+(?:-[a-z]+)*$/.test(item.reasonCode))
      || !instant(item.occurredAtUtc) || !instant(item.recordedAtUtc) || (item.passivatedAtUtc !== null && !instant(item.passivatedAtUtc))
      || items.some((previous) => previous.id === item.id) || (items.length > 0 && Date.parse(items[items.length - 1].recordedAtUtc) < Date.parse(item.recordedAtUtc))) return null
    items.push(item as unknown as WorkNotification)
  }
  if (raw.hasMore && raw.nextCursor !== items.at(-1)?.id) return null
  return { companyId, items, hasMore: raw.hasMore, nextCursor: raw.nextCursor as string | null }
}
