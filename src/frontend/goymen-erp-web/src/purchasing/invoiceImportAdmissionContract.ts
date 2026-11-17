import { isPurchasingGuid as guid, isPurchasingVersion as version } from './contract'
import { decodeInvoiceCommand, type InvoiceCommandResponse } from './incomingInvoiceContract'
import type { ImportPreviewRequest, SourceInvoice } from './invoiceImportContract'

export interface ImportReference { readonly id: string; readonly version: string; readonly code: string; readonly name: string }
export interface ImportUnit { readonly unitId: string; readonly code: string; readonly profileId: string; readonly profileVersion: string; readonly scale: number }
export interface ImportCurrency { readonly currencyId: string; readonly code: string; readonly profileId: string; readonly profileVersion: string; readonly scale: number }
export interface ImportMappingReferences { readonly companyId: string; readonly importId: string; readonly documentId: string; readonly documentVersion: number
  readonly documentSha256: string; readonly invoiceDate: string; readonly units: readonly ImportUnit[]; readonly currencies: readonly ImportCurrency[]
  readonly payable: string; readonly functionalPayable: string | null; readonly serviceTypes: readonly ImportReference[] | null; readonly expenseTypes: readonly ImportReference[] | null }
export interface ImportSupplier { readonly companyId: string; readonly importId: string; readonly supplierPartyId: string; readonly companyAccountId: string
  readonly partyVersion: string; readonly accountVersion: string }
export interface ImportProduct { readonly productId: string; readonly productVersion: string; readonly code: string; readonly name: string
  readonly unitId: string; readonly unitCode: string; readonly profileId: string; readonly profileVersion: string
  readonly itemType: 'Steel' | 'Scrap' | 'Service'; readonly itemTypeVersion: string }
export interface ImportProducts { readonly companyId: string; readonly importId: string; readonly items: readonly ImportProduct[]; readonly searchWindowFull: boolean }
export interface ImportLineMapping { readonly sourceLineId: string; readonly lineId: string; readonly productId: string; readonly expectedProductVersion: string
  readonly unitId: string; readonly itemType: 'Steel' | 'Scrap' | 'Service' | 'Expense'; readonly linePurpose: 'production-input' | 'resale' | 'service' | 'expense'
  readonly vatTaxCode: string; readonly serviceTypeId?: string; readonly expectedServiceTypeVersion?: string
  readonly expenseTypeId?: string; readonly expectedExpenseTypeVersion?: string; readonly expectedItemTypeVersion?: string }
export interface ImportInstallment { readonly id: string; readonly openItemId: string; readonly sequence: number; readonly dueOn: string
  readonly amount: string; readonly functionalAmount: string | null }
export interface ImportAdmission { readonly source: ImportPreviewRequest; readonly expectedInvoiceVersion: '0'; readonly supplierPartyId: string
  readonly companyAccountId: string; readonly expectedPartyVersion: string; readonly expectedAccountVersion: string; readonly currencyId: string
  readonly lines: readonly ImportLineMapping[]; readonly installments: readonly ImportInstallment[] }
export interface ImportRecordSnapshot { readonly request: ImportAdmission; readonly facts: SourceInvoice }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k))
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() !== '' && v.length <= 4000 && !/[\u0000-\u001f\u007f]/.test(v)
const scale = (v: unknown) => typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 6
export const importAmount = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,27})(?:\.\d{1,6})?$/.test(v) && !/^0(?:\.0+)?$/.test(v)
const unique = <T>(values: readonly T[], key: (v: T) => unknown) => new Set(values.map(key)).size === values.length
function references(v: unknown): v is readonly ImportReference[] | null {
  return v === null || Array.isArray(v) && v.every(x => object(x) && exact(x, ['id', 'version', 'code', 'name'])
    && guid(x.id) && version(x.version) && text(x.code) && text(x.name)) && unique(v, x => (x as ImportReference).id)
}
export function decodeImportMappingReferences(v: unknown, company: string, id: string, source: ImportPreviewRequest, date: string): ImportMappingReferences | null {
  if (!object(v) || !exact(v, ['companyId', 'importId', 'documentId', 'documentVersion', 'documentSha256', 'invoiceDate', 'units', 'currencies', 'payable', 'functionalPayable', 'serviceTypes', 'expenseTypes'])
    || v.companyId !== company || v.importId !== id || v.documentId !== source.documentId || v.documentVersion !== source.documentVersion
    || typeof v.documentSha256 !== 'string' || v.documentSha256.toLowerCase() !== source.documentSha256.toLowerCase() || v.invoiceDate !== date
    || !Array.isArray(v.units) || !Array.isArray(v.currencies) || !importAmount(v.payable) || !(v.functionalPayable === null || importAmount(v.functionalPayable))
    || !references(v.serviceTypes) || !references(v.expenseTypes)) return null
  if (v.units.some(x => !object(x) || !exact(x, ['unitId', 'code', 'profileId', 'profileVersion', 'scale']) || !guid(x.unitId)
    || !text(x.code) || !guid(x.profileId) || !version(x.profileVersion) || !scale(x.scale))
    || v.currencies.some(x => !object(x) || !exact(x, ['currencyId', 'code', 'profileId', 'profileVersion', 'scale']) || !guid(x.currencyId)
      || !text(x.code) || !guid(x.profileId) || !version(x.profileVersion) || !scale(x.scale))
    || !unique(v.units, x => (x as ImportUnit).unitId) || !unique(v.currencies, x => (x as ImportCurrency).currencyId)) return null
  return v as unknown as ImportMappingReferences
}
export function decodeImportSupplier(v: unknown, company: string, id: string, party: string, account: string): ImportSupplier | null {
  return object(v) && exact(v, ['companyId', 'importId', 'supplierPartyId', 'companyAccountId', 'partyVersion', 'accountVersion'])
    && v.companyId === company && v.importId === id && v.supplierPartyId === party && v.companyAccountId === account
    && version(v.partyVersion) && version(v.accountVersion) ? v as unknown as ImportSupplier : null
}
export function decodeImportProducts(v: unknown, company: string, id: string): ImportProducts | null {
  if (!object(v) || !exact(v, ['companyId', 'importId', 'items', 'searchWindowFull']) || v.companyId !== company || v.importId !== id
    || typeof v.searchWindowFull !== 'boolean' || !Array.isArray(v.items) || v.items.length > 25
    || v.items.some(x => !object(x) || !exact(x, ['productId', 'productVersion', 'code', 'name', 'unitId', 'unitCode', 'profileId', 'profileVersion', 'itemType', 'itemTypeVersion'])
      || !guid(x.productId) || !text(x.productVersion) || !text(x.code) || !text(x.name) || !guid(x.unitId) || !text(x.unitCode)
      || !guid(x.profileId) || !version(x.profileVersion) || !['Steel', 'Scrap', 'Service'].includes(x.itemType as string) || !version(x.itemTypeVersion))
    || !unique(v.items, x => `${(x as ImportProduct).productId}:${(x as ImportProduct).unitId}`)) return null
  return v as unknown as ImportProducts
}
export function decodeImportRecord(v: unknown, company: string, id: string, snapshot: ImportRecordSnapshot, replay: boolean): InvoiceCommandResponse | null {
  const answer = decodeInvoiceCommand(v, company, id, replay), request = snapshot.request, source = snapshot.facts
  if (answer === null) return null
  const invoice = answer.invoice
  if (invoice.status !== 'Recorded' || invoice.version !== '1' || answer.financeOutcome !== null || invoice.supplierPartyId !== request.supplierPartyId
    || invoice.companyAccountId !== request.companyAccountId || invoice.currencyId !== request.currencyId || invoice.documentId !== request.source.documentId
    || invoice.documentVersion !== String(request.source.documentVersion) || invoice.officialNumber !== source.number || invoice.ettn !== source.ettn
    || invoice.invoiceDate !== source.invoiceDate || invoice.lines.length !== request.lines.length || request.lines.some(line => !invoice.lines.some(actual => actual.id === line.lineId
      && actual.productId === line.productId && actual.unitId === line.unitId && actual.itemType === line.itemType && actual.linePurpose === line.linePurpose
      && actual.serviceTypeId === line.serviceTypeId && actual.expenseTypeId === line.expenseTypeId))) return null
  return answer
}
