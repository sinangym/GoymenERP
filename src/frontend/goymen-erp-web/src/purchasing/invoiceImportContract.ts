import { isPurchasingGuid as guid } from './contract'
import { isDateOnly } from '../finance/dateOnly'

export interface ImportReservation { readonly id: string; readonly companyId: string; readonly version: '1'; readonly reservedBy: string; readonly reservedAtUtc: string }
export interface ImportReservationResult { readonly reservation: ImportReservation; readonly isReplay: boolean; readonly originalCorrelationId: string }
export interface ImportReservationPage { readonly companyId: string; readonly pageIndex: number; readonly hasNext: boolean; readonly items: readonly ImportReservation[] }
export interface SourceAmount { readonly currency: string; readonly value: string }
export interface SourceTax { readonly code: string | null; readonly percent: string | null; readonly amount: SourceAmount; readonly taxableAmount: SourceAmount | null; readonly isWithholding: boolean }
export interface SourceAdjustment { readonly isCharge: boolean; readonly amount: SourceAmount; readonly reason: string | null }
export interface SourceFunctionalAmounts { readonly net: string; readonly vat: string; readonly withholding: string }
export interface SourceLine { readonly id: string; readonly sellerCode: string | null; readonly buyerCode: string | null; readonly description: string;
  readonly unitCode: string; readonly quantity: string; readonly net: SourceAmount; readonly taxes: readonly SourceTax[]; readonly adjustments: readonly SourceAdjustment[]
  readonly functionalAmounts?: SourceFunctionalAmounts | null }
export interface SourceInvoice {
  readonly number: string; readonly ettn: string; readonly invoiceDate: string; readonly invoiceType: string; readonly currency: string
  readonly supplierIdentities: readonly { readonly scheme: string; readonly value: string }[]
  readonly buyerIdentities: readonly { readonly scheme: string; readonly value: string }[]
  readonly totals: Readonly<Record<string, SourceAmount>>; readonly taxes: readonly SourceTax[]; readonly adjustments: readonly SourceAdjustment[]
  readonly rates: readonly { readonly purpose: string; readonly sourceCurrency: string; readonly targetCurrency: string; readonly rate: string;
    readonly sourceBaseRate: string | null; readonly targetBaseRate: string | null; readonly operator: string | null; readonly date: string | null }[]
  readonly payments: readonly { readonly dueOn: string | null; readonly amount: SourceAmount | null; readonly installmentDueOn: string | null; readonly source: string }[]
  readonly lines: readonly SourceLine[]
  readonly functionalTotals?: SourceFunctionalAmounts | null
}
export interface ImportOptions { readonly format: 1 | 2 | 3; readonly columns?: Readonly<Record<string, string>>; readonly worksheet?: string;
  readonly delimiter: ';' | ',' | '\t'; readonly decimalSeparator: '.' | ','; readonly dateFormat: 'yyyy-MM-dd' | 'dd.MM.yyyy' }
export interface ImportPreviewRequest { readonly expectedVersion: '1'; readonly documentId: string; readonly documentVersion: number;
  readonly documentSha256: string; readonly options: ImportOptions }
export interface ImportPreview { readonly importId: string; readonly companyId: string; readonly version: '1'; readonly documentId: string;
  readonly documentVersion: number; readonly documentSha256: string; readonly invoice: SourceInvoice | null;
  readonly issues: readonly { readonly field: string; readonly code: string }[] }

type Check = (value: unknown) => boolean
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const object = (value: unknown, fields: Readonly<Record<string, Check>>, optional: Readonly<Record<string, Check>> = {}) => record(value)
  && Object.keys(value).every(key => Object.hasOwn(fields, key) || Object.hasOwn(optional, key))
  && Object.entries(fields).every(([key, check]) => Object.hasOwn(value, key) && check(value[key]))
  && Object.entries(optional).every(([key, check]) => !Object.hasOwn(value, key) || check(value[key]))
const text: Check = value => typeof value === 'string' && value.length > 0 && value.length <= 4000 && value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value)
const nullable = (check: Check): Check => value => value === null || check(value)
const array = (check: Check, maximum = 2000): Check => value => Array.isArray(value) && value.length <= maximum && value.every(check)
const decimal: Check = value => typeof value === 'string' && value.length <= 40 && /^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/.test(value)
const currency: Check = value => typeof value === 'string' && /^[A-Z]{3}$/.test(value)
const date: Check = value => typeof value === 'string' && isDateOnly(value)
const boolean: Check = value => typeof value === 'boolean'
const amount: Check = value => object(value, { currency, value: decimal })
const identity: Check = value => object(value, { scheme: text, value: text })
const tax: Check = value => object(value, { code: nullable(text), percent: nullable(decimal), amount, taxableAmount: nullable(amount), isWithholding: boolean })
const adjustment: Check = value => object(value, { isCharge: boolean, amount, reason: nullable(text) })
const rate: Check = value => object(value, { purpose: text, sourceCurrency: currency, targetCurrency: currency, rate: decimal,
  sourceBaseRate: nullable(decimal), targetBaseRate: nullable(decimal), operator: nullable(text), date: nullable(date) })
const payment: Check = value => object(value, { dueOn: nullable(date), amount: nullable(amount), installmentDueOn: nullable(date), source: text })
const functionalAmounts: Check = value => object(value, { net: decimal, vat: decimal, withholding: decimal })
const line: Check = value => object(value, { id: text, sellerCode: nullable(text), buyerCode: nullable(text), description: text,
  unitCode: text, quantity: decimal, net: amount, taxes: array(tax), adjustments: array(adjustment) }, { functionalAmounts: nullable(functionalAmounts) })
const totalNames = ['LineExtensionAmount', 'TaxExclusiveAmount', 'TaxInclusiveAmount', 'AllowanceTotalAmount', 'ChargeTotalAmount', 'PrepaidAmount', 'PayableRoundingAmount', 'PayableAmount']
const totals: Check = value => record(value) && Object.keys(value).every(name => totalNames.includes(name)) && Object.values(value).every(amount)
const invoice: Check = value => object(value, { number: text, ettn: guid, invoiceDate: date, invoiceType: text, currency,
  supplierIdentities: array(identity), buyerIdentities: array(identity), totals, taxes: array(tax), adjustments: array(adjustment),
  rates: array(rate, 4), payments: array(payment), lines: array(line) }, { functionalTotals: nullable(functionalAmounts) })
  && record(value) && Array.isArray(value.lines) && value.lines.length > 0
  && new Set(value.lines.map(item => (item as SourceLine).id)).size === value.lines.length
const instant: Check = value => typeof value === 'string' && value.length <= 64 && /^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value))
const reservation: Check = value => object(value, { id: guid, companyId: guid, version: value => value === '1', reservedBy: text, reservedAtUtc: instant })
export function decodeImportReservations(value: unknown, company: string, pageIndex: number): ImportReservationPage | null {
  if (!object(value, { companyId: guid, pageIndex: value => value === pageIndex, hasNext: boolean, items: array(reservation, 25) })) return null
  const result = value as ImportReservationPage
  return result.companyId === company && result.items.every(item => item.companyId === company)
    && (!result.hasNext || result.items.length === 25)
    && new Set(result.items.map(item => item.id)).size === result.items.length ? result : null
}
export function decodeImportReservation(value: unknown, company: string, replay: boolean, original: string | null): ImportReservationResult | null {
  if (!object(value, { reservation, isReplay: boolean, originalCorrelationId: text })) return null
  const result = value as ImportReservationResult
  return result.reservation.companyId === company && result.isReplay === replay && (!replay || original === result.originalCorrelationId) ? result : null
}
export function decodeImportPreview(value: unknown, company: string, id: string, request: ImportPreviewRequest): ImportPreview | null {
  if (!object(value, { importId: guid, companyId: guid, version: value => value === '1', documentId: guid,
    documentVersion: value => typeof value === 'number' && Number.isSafeInteger(value) && value > 0,
    documentSha256: value => typeof value === 'string' && /^[a-f0-9]{64}$/i.test(value), invoice: nullable(invoice),
    issues: array(value => object(value, { field: text, code: text })) })) return null
  const result = value as ImportPreview
  return result.companyId === company && result.importId === id && result.version === request.expectedVersion
    && result.documentId === request.documentId && result.documentVersion === request.documentVersion
    && result.documentSha256.toLowerCase() === request.documentSha256.toLowerCase()
    && (result.invoice === null ? result.issues.length > 0 : result.issues.length === 0) ? result : null
}
