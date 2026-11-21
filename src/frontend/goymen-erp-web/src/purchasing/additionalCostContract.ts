import { isDateOnly } from '../sales/wirePrimitives'
import { serviceGuid } from './serviceOrderContract'
export const ADDITIONAL_COST_CAPABILITY = { read: 'purchasing.additional-costs.read', record: 'purchasing.additional-costs.record' } as const
export type AdditionalCostKind = 'GoodsOrder' | 'ServiceOrder'
export const additionalCostPath = (kind: AdditionalCostKind, id: string) => `/satinalma/ek-masraflar/${kind === 'GoodsOrder' ? 'goods' : 'service'}/${encodeURIComponent(id)}`
export interface AdditionalCostSource { readonly companyId: string; readonly orderId: string; readonly orderVersion: string; readonly orderNumber: string | null
  readonly sourceKind: AdditionalCostKind; readonly currencyId: string; readonly currencyCode: string
  readonly acceptances: readonly { readonly acceptanceId: string; readonly lineId: string; readonly quantity: string; readonly recordedAtUtc: string }[] }
export interface AdditionalCostReferences { readonly companyId: string; readonly typeRequired: boolean
  readonly expenseTypes: readonly { readonly id: string; readonly version: string; readonly code: string; readonly name: string }[]
  readonly businessDate: string; readonly vatListVersion: string; readonly vatRates: readonly { readonly code: string; readonly name: string; readonly percent: string }[] }
export interface AdditionalCost { readonly companyId: string; readonly costId: string; readonly version: '1'; readonly orderId: string
  readonly sourceOrderVersion: string; readonly sourceOrderNumber: string | null; readonly sourceKind: AdditionalCostKind; readonly acceptanceId: string | null
  readonly supplierPartyId: string; readonly companyAccountId: string; readonly businessDate: string; readonly currencyId: string; readonly currencyCode: string
  readonly description: string; readonly expenseTypeId: string | null; readonly expenseTypeVersion: string | null; readonly expenseTypeName: string | null
  readonly netAmount: string; readonly vatPercent: string; readonly vatAmount: string; readonly grossAmount: string; readonly recordedBy: string
  readonly reason: string; readonly recordedAtUtc: string }
export interface RecordAdditionalCost { readonly costId: string; readonly expectedVersion: '0'; readonly orderId: string; readonly sourceKind: 1 | 2
  readonly expectedOrderVersion: string; readonly acceptanceId: string | null; readonly supplierPartyId: string; readonly companyAccountId: string
  readonly businessDate: string; readonly expenseTypeId: string | null; readonly expectedExpenseTypeVersion: string | null; readonly description: string
  readonly netAmount: string; readonly vatRateCode: string; readonly expectedVatListVersion: string; readonly reason: string }
export interface AdditionalCostReceipt { readonly cost: AdditionalCost; readonly isReplay: boolean; readonly originalCorrelationId: string }
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, names: readonly string[]) => Object.keys(v).length === names.length && names.every(x => Object.hasOwn(v, x))
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 1000
const version = (v: unknown): v is string => typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n
const amount = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,28})(?:\.\d{0,5}[1-9])?$/.test(v)
const unique = <T>(v: readonly T[], key: (x: T) => unknown) => new Set(v.map(key)).size === v.length
const utc = (v: unknown): v is string => typeof v === 'string' && /(?:Z|\+00:00)$/.test(v) && !Number.isNaN(Date.parse(v))
export function decodeAdditionalCostSource(v: unknown, company: string, kind: AdditionalCostKind, id: string): AdditionalCostSource | null {
  if (!record(v) || !exact(v, ['companyId', 'orderId', 'orderVersion', 'orderNumber', 'sourceKind', 'currencyId', 'currencyCode', 'acceptances'])
    || v.companyId !== company || v.orderId !== id || v.sourceKind !== kind || !serviceGuid(v.orderId) || !version(v.orderVersion)
    || v.orderNumber !== null && !text(v.orderNumber) || !serviceGuid(v.currencyId) || !text(v.currencyCode) || !Array.isArray(v.acceptances)) return null
  for (const x of v.acceptances) if (!record(x) || !exact(x, ['acceptanceId', 'lineId', 'quantity', 'recordedAtUtc'])
    || !serviceGuid(x.acceptanceId) || !serviceGuid(x.lineId) || !amount(x.quantity) || x.quantity === '0' || !utc(x.recordedAtUtc)) return null
  return unique(v.acceptances, x => x.acceptanceId) ? v as unknown as AdditionalCostSource : null
}
export function decodeAdditionalCostReferences(v: unknown, company: string, on: string): AdditionalCostReferences | null {
  if (!record(v) || !exact(v, ['companyId', 'typeRequired', 'expenseTypes', 'businessDate', 'vatListVersion', 'vatRates'])
    || v.companyId !== company || v.businessDate !== on || !isDateOnly(on) || typeof v.typeRequired !== 'boolean'
    || !version(v.vatListVersion) || !Array.isArray(v.expenseTypes) || !Array.isArray(v.vatRates) || v.typeRequired !== (v.expenseTypes.length > 0)) return null
  for (const x of v.expenseTypes) if (!record(x) || !exact(x, ['id', 'version', 'code', 'name']) || !serviceGuid(x.id) || !version(x.version) || !text(x.code) || !text(x.name)) return null
  for (const x of v.vatRates) if (!record(x) || !exact(x, ['code', 'name', 'percent']) || !text(x.code) || !text(x.name) || !amount(x.percent)) return null
  return unique(v.expenseTypes, x => x.id) && unique(v.vatRates, x => x.code) ? v as unknown as AdditionalCostReferences : null
}
const costFields = ['companyId', 'costId', 'version', 'orderId', 'sourceOrderVersion', 'sourceOrderNumber', 'sourceKind', 'acceptanceId',
  'supplierPartyId', 'companyAccountId', 'businessDate', 'currencyId', 'currencyCode', 'description', 'expenseTypeId', 'expenseTypeVersion',
  'expenseTypeName', 'netAmount', 'vatPercent', 'vatAmount', 'grossAmount', 'recordedBy', 'reason', 'recordedAtUtc'] as const
export function decodeAdditionalCost(v: unknown, company: string, kind: AdditionalCostKind, order: string): AdditionalCost | null {
  if (!record(v) || !exact(v, costFields) || v.companyId !== company || v.orderId !== order || v.sourceKind !== kind || !serviceGuid(v.costId)
    || v.version !== '1' || !version(v.sourceOrderVersion) || v.sourceOrderNumber !== null && !text(v.sourceOrderNumber)
    || v.acceptanceId !== null && !serviceGuid(v.acceptanceId) || !serviceGuid(v.supplierPartyId) || !serviceGuid(v.companyAccountId)
    || typeof v.businessDate !== 'string' || !isDateOnly(v.businessDate) || !serviceGuid(v.currencyId) || !text(v.currencyCode) || !text(v.description)
    || !amount(v.netAmount) || !amount(v.vatPercent) || !amount(v.vatAmount) || !amount(v.grossAmount) || !text(v.recordedBy) || !text(v.reason) || !utc(v.recordedAtUtc)
    || (v.expenseTypeId === null ? v.expenseTypeVersion !== null || v.expenseTypeName !== null
      : !serviceGuid(v.expenseTypeId) || !version(v.expenseTypeVersion) || !text(v.expenseTypeName))) return null
  return v as unknown as AdditionalCost
}
export function decodeAdditionalCosts(v: unknown, company: string, kind: AdditionalCostKind, order: string): readonly AdditionalCost[] | null {
  if (!Array.isArray(v) || v.length > 100) return null
  const rows = v.map(x => decodeAdditionalCost(x, company, kind, order))
  return rows.some(x => x === null) || !unique(rows, x => x?.costId) ? null : rows as AdditionalCost[]
}
export function decodeAdditionalCostReceipt(v: unknown, company: string, kind: AdditionalCostKind, order: string, costId: string, replay: boolean): AdditionalCostReceipt | null {
  if (!record(v) || !exact(v, ['cost', 'isReplay', 'originalCorrelationId']) || v.isReplay !== replay || !text(v.originalCorrelationId)) return null
  const cost = decodeAdditionalCost(v.cost, company, kind, order)
  return cost === null || cost.costId !== costId ? null : { cost, isReplay: replay, originalCorrelationId: v.originalCorrelationId }
}
