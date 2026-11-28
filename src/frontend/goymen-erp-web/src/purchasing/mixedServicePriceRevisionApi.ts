import { PURCHASING_ORDERS_BASE, purchasingHeaders } from './api'
import { decodePurchaseOrderCommandDetail, decodePurchaseOrderCommandResponse, type PurchaseOrderCommandResponse, type PurchaseOrderDetail } from './contract'
import { decodeServiceOrder, serviceGuid, type ServiceOrder } from './serviceOrderContract'
import type { ViewOutcome } from '../shell/uiState'
import type { AdjustmentOutcome } from './receiptAdjustmentApi'

export interface MixedServiceSelection { readonly serviceOrderId: string; readonly expectedVersion: string; readonly lineIds: readonly string[] }
export interface MixedPriceService { readonly service: ServiceOrder; readonly initialPreparedVersion: string; readonly initialLineIds: readonly string[]
  readonly preparedBy: string; readonly createdAtUtc: string; readonly updatedAtUtc: string; readonly priceRevision: number
  readonly partyVersion: string; readonly accountVersion: string; readonly selection: MixedServiceSelection }
export interface MixedServicePricePreview { readonly companyId: string; readonly parent: PurchaseOrderDetail; readonly services: readonly MixedPriceService[] }
export interface MixedServicePriceRequest { readonly expectedVersion: string; readonly services: readonly MixedServiceSelection[]
  readonly changes: readonly { readonly serviceOrderId: string; readonly lines: readonly { readonly lineId: string; readonly unitPrice: string }[] }[]; readonly reason: string }
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, fields: readonly string[]) => Object.keys(v).length === fields.length && fields.every(x => Object.hasOwn(v, x))
const guid = (v: unknown): v is string => serviceGuid(v) && v.length === 36 && v.trim() === v
export const mixedPriceVersion = (v: unknown, increment = false): v is string => typeof v === 'string' && v.trim() === v
  && /^[1-9][0-9]{0,18}$/.test(v) && BigInt(v) <= (increment ? 9223372036854775806n : 9223372036854775807n)
const utc = (v: unknown): v is string => typeof v === 'string' && v.trim() === v && /(?:Z|\+00:00)$/.test(v) && Number.isFinite(Date.parse(v))
const actor = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 128
const unique = (v: readonly string[]) => new Set(v.map(x => x.toLowerCase())).size === v.length
const serviceFields = ['service', 'initialPreparedVersion', 'initialLineIds', 'preparedBy', 'createdAtUtc', 'updatedAtUtc', 'priceRevision', 'partyVersion', 'accountVersion', 'selection'] as const
export function decodeMixedServicePricePreview(v: unknown, company: string, order: string, version: string): MixedServicePricePreview | null {
  if (!object(v) || !exact(v, ['companyId', 'parent', 'services']) || v.companyId !== company || !Array.isArray(v.services) || v.services.length === 0) return null
  const parent = decodePurchaseOrderCommandDetail(v.parent)
  if (parent === null || parent.order.companyId !== company || parent.order.id !== order || parent.order.status !== 'Approved'
    || parent.order.version !== version || !mixedPriceVersion(parent.order.version) || parent.currency == null
    || parent.priceDisclosure !== 'Visible' && parent.priceDisclosure !== 'Withheld') return null
  const services: MixedPriceService[] = []
  for (const item of v.services) {
    if (!object(item) || !exact(item, serviceFields) || !object(item.service) || !mixedPriceVersion(item.service.version)
      || !mixedPriceVersion(item.initialPreparedVersion) || BigInt(item.initialPreparedVersion) > BigInt(item.service.version)
      || !Array.isArray(item.initialLineIds) || item.initialLineIds.length === 0 || !item.initialLineIds.every(guid) || !unique(item.initialLineIds)
      || !actor(item.preparedBy) || !utc(item.createdAtUtc) || !utc(item.updatedAtUtc) || !Number.isInteger(item.priceRevision)
      || typeof item.priceRevision !== 'number' || item.priceRevision < 0 || item.priceRevision > 2147483647
      || typeof item.partyVersion !== 'string' || !item.partyVersion.trim() || typeof item.accountVersion !== 'string' || !item.accountVersion.trim()
      || !object(item.selection) || !exact(item.selection, ['serviceOrderId', 'expectedVersion', 'lineIds'])
      || !guid(item.selection.serviceOrderId) || !mixedPriceVersion(item.selection.expectedVersion) || !Array.isArray(item.selection.lineIds)
      || !item.selection.lineIds.every(guid) || !unique(item.selection.lineIds)) return null
    const selection: MixedServiceSelection = { serviceOrderId: item.selection.serviceOrderId, expectedVersion: item.selection.expectedVersion, lineIds: item.selection.lineIds }
    const lineIds = selection.lineIds
    const service = decodeServiceOrder(item.service, company, selection.serviceOrderId)
    if (service === null || service.status !== 'Approved' || service.supplierPartyId !== parent.order.supplierPartyId
      || service.companyAccountId !== parent.order.companyAccountId || service.currencyId !== parent.currency.currencyId
      || service.currencyCode !== parent.currency.currencyCode || service.priceDisclosure !== parent.priceDisclosure
      || service.version !== selection.expectedVersion || lineIds.length !== service.lines.length
      || service.lines.some(line => !lineIds.includes(line.lineId))) return null
    services.push({ service, initialPreparedVersion: item.initialPreparedVersion, initialLineIds: item.initialLineIds, preparedBy: item.preparedBy,
      createdAtUtc: item.createdAtUtc, updatedAtUtc: item.updatedAtUtc, priceRevision: item.priceRevision, partyVersion: item.partyVersion,
      accountVersion: item.accountVersion, selection: { serviceOrderId: selection.serviceOrderId, expectedVersion: selection.expectedVersion, lineIds } })
  }
  return unique(services.map(x => x.service.orderId)) ? { companyId: company, parent, services } : null
}
export async function fetchMixedServicePricePreview(company: string, order: string, version: string, signal: AbortSignal): Promise<ViewOutcome<MixedServicePricePreview>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(order)}/service-price-revision-preview?expectedVersion=${encodeURIComponent(version)}`,
      { headers: purchasingHeaders(company), cache: 'no-store', signal })
    if (response.status !== 200) return response.status >= 200 && response.status < 300 ? { type: 'malformed' } : { type: 'http', status: response.status }
    const value = decodeMixedServicePricePreview(await response.json(), company, order, version)
    return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export async function sendMixedServicePriceRevision(company: string, order: string, request: MixedServicePriceRequest, key: string): Promise<AdjustmentOutcome<PurchaseOrderCommandResponse>> {
  try {
    const response = await fetch(`${PURCHASING_ORDERS_BASE}/${encodeURIComponent(order)}/service-price-revisions`, { method: 'POST',
      headers: { ...purchasingHeaders(company), 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify(request) })
    let body: unknown; try { body = await response.json() } catch { return { type: 'unknown', reason: 'malformed', status: response.status } }
    // The native final release fence can fail AFTER commit. Even a first 401/403 must retain this exact original attempt.
    if (response.status === 401 || response.status === 403 || response.status >= 500) return { type: 'unknown', reason: 'service-unavailable', status: response.status }
    if ([400, 404, 409, 415, 422].includes(response.status)) return { type: 'failed', status: response.status,
      isReplay: response.headers.get('Idempotency-Replayed') === 'true', code: object(body) && typeof body.code === 'string' ? body.code : null }
    if (response.status !== 200) return { type: 'unknown', reason: 'malformed' }
    const value = decodePurchaseOrderCommandResponse(body)
    if (value === null || value.detail.order.id !== order || value.detail.order.companyId !== company || value.detail.order.status !== 'Draft'
      || !mixedPriceVersion(request.expectedVersion, true) || !mixedPriceVersion(value.detail.order.version)
      || BigInt(value.detail.order.version) !== BigInt(request.expectedVersion) + 1n) return { type: 'unknown', reason: 'malformed' }
    const replay = response.headers.get('Idempotency-Replayed'), original = response.headers.get('Idempotency-Original-Correlation-Id')
    if (value.isReplay ? replay !== 'true' || original !== value.originalCorrelationId : replay !== null || original !== null)
      return { type: 'unknown', reason: 'malformed' }
    return { type: 'applied', response: value }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
