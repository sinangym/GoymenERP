import { isDateOnly } from '../sales/wirePrimitives'
export const SERVICE_ORDER_CAPABILITY = { list: 'purchasing.service-orders.list', create: 'purchasing.service-orders.create',
  detail: 'purchasing.service-orders.detail', approve: 'purchasing.service-orders.approve', accept: 'purchasing.service-orders.accept',
  revisePrices: 'purchasing.service-orders.prices-revise' } as const
export const SERVICE_ORDER_PATH = '/satinalma/hizmet-siparisleri'
export const serviceOrderPath = (id: string) => `${SERVICE_ORDER_PATH}/${encodeURIComponent(id)}`
export interface ServiceLine { readonly lineId: string; readonly serviceTypeId: string; readonly serviceTypeVersion: string; readonly serviceName: string
  readonly unitId: string; readonly unitCode: string; readonly quantity: string; readonly acceptedQuantity: string; readonly unitPrice: string | null; readonly amount: string | null
  readonly tax?: { readonly vatApplies: boolean; readonly vatPercent: string | null; readonly exemptionCode: string | null; readonly withholdingApplies: boolean
    readonly withholdingPercent: string | null; readonly withholdingCode: string | null; readonly settingsVersion: string
    readonly change?: { readonly sourceVersion: string; readonly vatRateCode: string | null; readonly withholdingRatioCode: string | null; readonly changedBy: string; readonly reason: string; readonly changedAtUtc: string } } }
export interface ServiceMaterial { readonly labelId: string; readonly labelVersion: string; readonly productId: string; readonly productVersion: string; readonly quantityKg: string }
export interface ServiceOutput { readonly productId: string; readonly productVersion: string; readonly expectedQuantityKg: string }
export interface ServiceAcceptance { readonly acceptanceId: string; readonly lineId: string; readonly quantity: string; readonly acceptedOn: string
  readonly recordedBy: string; readonly reason: string; readonly recordedAtUtc: string }
export interface ServiceOrder { readonly companyId: string; readonly orderId: string; readonly documentNumber: string; readonly version: string
  readonly kind: 'ServicePurchase' | 'OwnMaterialSubcontract'; readonly status: 'Draft' | 'Approved' | 'Cancelled'
  readonly supplierPartyId: string; readonly companyAccountId: string; readonly businessDate: string; readonly currencyId: string; readonly currencyCode: string
  readonly isOpen: boolean; readonly priceDisclosure: 'Visible' | 'Withheld'; readonly lines: readonly ServiceLine[]
  readonly materials: readonly ServiceMaterial[]; readonly expectedOutputs: readonly ServiceOutput[]; readonly acceptances: readonly ServiceAcceptance[] }
export interface ServiceReferences { readonly companyId: string; readonly serviceTypes: readonly { readonly id: string; readonly version: string; readonly code: string; readonly name: string }[]
  readonly units: readonly { readonly unitId: string; readonly code: string; readonly isKilogram: boolean }[] }
export interface ServiceOrderReceipt { readonly order: ServiceOrder; readonly isReplay: boolean; readonly originalCorrelationId: string }
export interface ServiceCreate { readonly orderId: string; readonly expectedVersion: '0'; readonly kind: 1 | 2; readonly supplierPartyId: string; readonly companyAccountId: string
  readonly businessDate: string; readonly currencyId: string; readonly reason: string; readonly materials: readonly ServiceMaterial[]; readonly expectedOutputs: readonly ServiceOutput[]
  readonly lines: readonly { readonly lineId: string; readonly serviceTypeId: string; readonly expectedServiceTypeVersion: string; readonly unitId: string; readonly quantity: string; readonly unitPrice: string | null }[] }
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, fields: readonly string[]) => Object.keys(v).length === fields.length && fields.every(x => Object.hasOwn(v, x))
export const serviceGuid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 1000
const version = (v: unknown): v is string => typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n
const quantity = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,17})(?:\.\d{0,5}[1-9])?$/.test(v)
const amount = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,28})(?:\.\d{0,5}[1-9])?$/.test(v)
const unique = <T>(values: readonly T[], id: (v: T) => unknown) => new Set(values.map(id)).size === values.length
const lineFields = ['lineId', 'serviceTypeId', 'serviceTypeVersion', 'serviceName', 'unitId', 'unitCode', 'quantity', 'acceptedQuantity', 'unitPrice', 'amount'] as const
const materialFields = ['labelId', 'labelVersion', 'productId', 'productVersion', 'quantityKg'] as const
const outputFields = ['productId', 'productVersion', 'expectedQuantityKg'] as const
const acceptanceFields = ['acceptanceId', 'lineId', 'quantity', 'acceptedOn', 'recordedBy', 'reason', 'recordedAtUtc'] as const
const orderFields = ['companyId', 'orderId', 'documentNumber', 'version', 'kind', 'status', 'supplierPartyId', 'companyAccountId', 'businessDate', 'currencyId', 'currencyCode',
  'isOpen', 'priceDisclosure', 'lines', 'materials', 'expectedOutputs', 'acceptances'] as const
export function decodeServiceOrder(v: unknown, company: string, id?: string): ServiceOrder | null {
  if (!record(v) || !exact(v, orderFields) || v.companyId !== company || !serviceGuid(v.orderId) || id !== undefined && v.orderId !== id
    || !text(v.documentNumber) || !version(v.version) || v.kind !== 'ServicePurchase' && v.kind !== 'OwnMaterialSubcontract'
    || !['Draft', 'Approved', 'Cancelled'].includes(String(v.status)) || !serviceGuid(v.supplierPartyId) || !serviceGuid(v.companyAccountId)
    || !serviceGuid(v.currencyId) || !text(v.currencyCode) || typeof v.businessDate !== 'string' || !isDateOnly(v.businessDate)
    || typeof v.isOpen !== 'boolean' || v.priceDisclosure !== 'Visible' && v.priceDisclosure !== 'Withheld'
    || !Array.isArray(v.lines) || v.lines.length === 0 || v.lines.length > 100 || !Array.isArray(v.materials)
    || !Array.isArray(v.expectedOutputs) || !Array.isArray(v.acceptances)) return null
  for (const x of v.lines) if (!record(x) || !exact(x, [...lineFields, ...(Object.hasOwn(x, 'tax') ? ['tax'] : [])]) || !serviceGuid(x.lineId) || !serviceGuid(x.serviceTypeId)
    || !version(x.serviceTypeVersion) || !text(x.serviceName) || !serviceGuid(x.unitId) || !text(x.unitCode) || !quantity(x.quantity) || x.quantity === '0'
    || !quantity(x.acceptedQuantity) || x.unitPrice !== null && !quantity(x.unitPrice) || x.amount !== null && !amount(x.amount)
    || v.priceDisclosure === 'Withheld' && (x.unitPrice !== null || x.amount !== null)
    || Object.hasOwn(x, 'tax') && !tax(x.tax)) return null
  for (const x of v.materials) if (!record(x) || !exact(x, materialFields) || !serviceGuid(x.labelId) || !version(x.labelVersion)
    || !serviceGuid(x.productId) || !text(x.productVersion) || !quantity(x.quantityKg) || x.quantityKg === '0') return null
  for (const x of v.expectedOutputs) if (!record(x) || !exact(x, outputFields) || !serviceGuid(x.productId) || !text(x.productVersion)
    || !quantity(x.expectedQuantityKg) || x.expectedQuantityKg === '0') return null
  for (const x of v.acceptances) if (!record(x) || !exact(x, acceptanceFields) || !serviceGuid(x.acceptanceId) || !serviceGuid(x.lineId)
    || !v.lines.some(line => line.lineId === x.lineId) || !quantity(x.quantity) || x.quantity === '0' || !text(x.recordedBy) || !text(x.reason)
    || typeof x.acceptedOn !== 'string' || !isDateOnly(x.acceptedOn) || typeof x.recordedAtUtc !== 'string'
    || !/(?:Z|\+00:00)$/.test(x.recordedAtUtc) || Number.isNaN(Date.parse(x.recordedAtUtc))) return null
  if (!unique(v.lines, x => x.lineId) || !unique(v.materials, x => x.labelId) || !unique(v.acceptances, x => x.acceptanceId)
    || v.kind === 'ServicePurchase' && (v.materials.length !== 0 || v.expectedOutputs.length !== 0)
    || v.kind === 'OwnMaterialSubcontract' && (v.materials.length === 0 || v.expectedOutputs.length === 0)) return null
  return v as unknown as ServiceOrder
}
export function decodeServiceOrders(v: unknown, company: string): readonly ServiceOrder[] | null {
  if (!Array.isArray(v) || v.length > 100) return null
  const rows = v.map(x => decodeServiceOrder(x, company))
  return rows.some(x => x === null) || !unique(rows, x => x?.orderId) ? null : rows as ServiceOrder[]
}
export function decodeServiceReferences(v: unknown, company: string): ServiceReferences | null {
  if (!record(v) || !exact(v, ['companyId', 'serviceTypes', 'units']) || v.companyId !== company || !Array.isArray(v.serviceTypes) || !Array.isArray(v.units)) return null
  for (const x of v.serviceTypes) if (!record(x) || !exact(x, ['id', 'version', 'code', 'name']) || !serviceGuid(x.id) || !version(x.version) || !text(x.code) || !text(x.name)) return null
  for (const x of v.units) if (!record(x) || !exact(x, ['unitId', 'code', 'isKilogram']) || !serviceGuid(x.unitId) || !text(x.code) || typeof x.isKilogram !== 'boolean') return null
  return unique(v.serviceTypes, x => x.id) && unique(v.units, x => x.unitId) ? v as unknown as ServiceReferences : null
}
function tax(v: unknown): boolean {
  return record(v) && exact(v, ['vatApplies', 'vatPercent', 'exemptionCode', 'withholdingApplies', 'withholdingPercent', 'withholdingCode', 'settingsVersion', ...(Object.hasOwn(v, 'change') ? ['change'] : [])])
    && typeof v.vatApplies === 'boolean' && (v.vatPercent === null || quantity(v.vatPercent))
    && (v.exemptionCode === null || text(v.exemptionCode)) && typeof v.withholdingApplies === 'boolean'
    && (v.withholdingPercent === null || quantity(v.withholdingPercent)) && (v.withholdingCode === null || text(v.withholdingCode))
    && (!Object.hasOwn(v, 'change') || record(v.change) && exact(v.change, ['sourceVersion', 'vatRateCode', 'withholdingRatioCode', 'changedBy', 'reason', 'changedAtUtc'])
      && text(v.change.sourceVersion) && (v.change.vatRateCode === null || text(v.change.vatRateCode)) && (v.change.withholdingRatioCode === null || text(v.change.withholdingRatioCode))
      && text(v.change.changedBy) && text(v.change.reason) && typeof v.change.changedAtUtc === 'string' && /(?:Z|\+00:00)$/.test(v.change.changedAtUtc) && Number.isFinite(Date.parse(v.change.changedAtUtc)))
    && version(v.settingsVersion) && (v.vatApplies ? v.vatPercent !== null : v.vatPercent === null && v.exemptionCode !== null)
    && (v.withholdingApplies ? v.vatApplies && v.withholdingPercent !== null && v.withholdingCode !== null
      : v.withholdingPercent === null && v.withholdingCode === null)
}
export function decodeServiceReceipt(v: unknown, company: string, id: string, replay: boolean): ServiceOrderReceipt | null {
  if (!record(v) || !exact(v, ['order', 'isReplay', 'originalCorrelationId']) || v.isReplay !== replay || !text(v.originalCorrelationId)) return null
  const order = decodeServiceOrder(v.order, company, id)
  return order === null ? null : { order, isReplay: replay, originalCorrelationId: v.originalCorrelationId }
}
