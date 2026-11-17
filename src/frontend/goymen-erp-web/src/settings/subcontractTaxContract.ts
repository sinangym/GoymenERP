import { isDateOnly } from '../sales/wirePrimitives'
export type SubcontractTaxSide = 'sales' | 'purchasing'
export const SUBCONTRACT_TAX_CAPABILITY = { sales: 'settings.sales-subcontract-tax-default.manage', purchasing: 'settings.purchasing-subcontract-tax-default.manage' } as const
export const subcontractTaxPath = (side: SubcontractTaxSide) => `/settings/company-policies/${side}-subcontract-tax-default`
export interface SubcontractTaxValue { readonly vatApplies: boolean; readonly vatPercent: string | null; readonly exemptionCode: string | null
  readonly withholdingApplies: boolean; readonly withholdingPercent: string | null; readonly withholdingCode: string | null }
export interface SubcontractTaxPolicy { readonly companyId: string; readonly current: SubcontractTaxValue | null; readonly version: string
  readonly versions: readonly { readonly version: string; readonly value: SubcontractTaxValue; readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string }[] }
export interface SubcontractTaxOptions { readonly companyId: string; readonly side: 1 | 2; readonly on: string; readonly defaultVersion: string; readonly default: SubcontractTaxValue | null
  readonly vatRates: readonly SubcontractTaxRate[]; readonly withholdingRatios: readonly SubcontractTaxRate[]
  readonly exemptionCodes: readonly { readonly code: string; readonly name: string }[]; readonly withholdingCodes: readonly { readonly code: string; readonly name: string }[]; readonly sourceVersion: string }
export interface SubcontractTaxRate { readonly code: string; readonly name: string; readonly percent: string; readonly taxCode: string | null }
export interface SubcontractTaxChoice { readonly useDefault: boolean; readonly vatRateCode: string; readonly exemptionCode: string; readonly withholdingRatioCode: string }
export interface RecordSubcontractTaxBody extends SubcontractTaxValue { readonly expectedVersion: string; readonly reason: string }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const exact = (v: Record<string, unknown>, names: readonly string[]) => Object.keys(v).length === names.length && names.every(x => Object.hasOwn(v, x))
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 4000
const code = (v: unknown): v is string => typeof v === 'string' && /^[A-Z0-9]{1,16}$/.test(v)
const version = (v: unknown): v is string => typeof v === 'string' && /^(0|[1-9]\d{0,18})$/.test(v) && BigInt(v) <= 9223372036854775807n
const percent = (v: unknown): v is string => typeof v === 'string' && /^(?:(?:0|[1-9]\d?)(?:\.\d{0,3}[1-9])?|100)$/.test(v)
export function isSubcontractTaxValue(v: unknown): v is SubcontractTaxValue {
  return object(v) && exact(v, ['vatApplies', 'vatPercent', 'exemptionCode', 'withholdingApplies', 'withholdingPercent', 'withholdingCode'])
    && typeof v.vatApplies === 'boolean' && typeof v.withholdingApplies === 'boolean'
    && (v.vatApplies ? percent(v.vatPercent) && v.vatPercent !== '0' && v.exemptionCode === null : v.vatPercent === null && (v.exemptionCode === null || code(v.exemptionCode)) && !v.withholdingApplies)
    && (v.withholdingApplies ? percent(v.withholdingPercent) && v.withholdingPercent !== '0' && (v.withholdingCode === null || code(v.withholdingCode)) : v.withholdingPercent === null && v.withholdingCode === null)
}
export function decodeSubcontractTaxPolicy(v: unknown, company: string): SubcontractTaxPolicy | null {
  if (!object(v) || !exact(v, ['companyId', 'current', 'version', 'versions']) || v.companyId !== company || !version(v.version)
    || v.current !== null && !isSubcontractTaxValue(v.current) || !Array.isArray(v.versions)) return null
  let last = BigInt(v.version) + 1n
  for (const row of v.versions) {
    if (!object(row) || !exact(row, ['version', 'value', 'recordedBy', 'recordedAtUtc', 'reason']) || !version(row.version) || row.version === '0'
      || BigInt(row.version) >= last || !isSubcontractTaxValue(row.value) || !text(row.recordedBy) || !text(row.reason)
      || typeof row.recordedAtUtc !== 'string' || !/(?:Z|\+00:00)$/.test(row.recordedAtUtc) || !Number.isFinite(Date.parse(row.recordedAtUtc))) return null
    last = BigInt(row.version)
  }
  if (v.version === '0' ? v.current !== null || v.versions.length !== 0 : v.versions[0]?.version !== v.version
    || !sameValue(v.current, v.versions[0]?.value)) return null
  return v as unknown as SubcontractTaxPolicy
}
const sameValue = (a: unknown, b: unknown) => isSubcontractTaxValue(a) && isSubcontractTaxValue(b)
  && a.vatApplies === b.vatApplies && a.vatPercent === b.vatPercent && a.exemptionCode === b.exemptionCode
  && a.withholdingApplies === b.withholdingApplies && a.withholdingPercent === b.withholdingPercent && a.withholdingCode === b.withholdingCode
export function decodeSubcontractTaxOptions(v: unknown, company: string, side: SubcontractTaxSide, on?: string): SubcontractTaxOptions | null {
  if (!object(v) || !exact(v, ['companyId', 'side', 'on', 'defaultVersion', 'default', 'vatRates', 'withholdingRatios', 'exemptionCodes', 'withholdingCodes', 'sourceVersion'])
    || v.companyId !== company || v.side !== (side === 'sales' ? 1 : 2) || typeof v.on !== 'string' || !isDateOnly(v.on) || on !== undefined && v.on !== on
    || !version(v.defaultVersion) || (v.defaultVersion === '0' ? v.default !== null : !isSubcontractTaxValue(v.default)) || !text(v.sourceVersion) || v.sourceVersion.length > 200) return null
  for (const field of ['vatRates', 'withholdingRatios'] as const) {
    const entries = v[field]
    if (!Array.isArray(entries) || entries.some(x => !object(x) || !exact(x, ['code', 'name', 'percent', 'taxCode']) || !text(x.code) || x.code.length > 64 || !text(x.name)
      || !percent(x.percent) || field === 'withholdingRatios' && x.percent === '0' || (x.taxCode !== null && !code(x.taxCode)) || field === 'vatRates' && x.taxCode !== null)
      || new Set(entries.map(x => x.code)).size !== entries.length) return null
  }
  for (const field of ['exemptionCodes', 'withholdingCodes'] as const) {
    const entries = v[field]
    if (!Array.isArray(entries) || entries.some(x => !object(x) || !exact(x, ['code', 'name']) || !code(x.code) || !text(x.name))
      || new Set(entries.map(x => x.code)).size !== entries.length) return null
  }
  return v as unknown as SubcontractTaxOptions
}
export function selectedSubcontractTax(options: SubcontractTaxOptions, choice: SubcontractTaxChoice): SubcontractTaxValue | null {
  if (choice.useDefault) return options.default
  const vat = options.vatRates.find(x => x.code === choice.vatRateCode)
  const ratio = choice.withholdingRatioCode === '' ? null : options.withholdingRatios.find(x => x.code === choice.withholdingRatioCode)
  if (!vat || ratio === undefined || vat.percent === '0' && (ratio !== null || !options.exemptionCodes.some(x => x.code === choice.exemptionCode))
    || vat.percent !== '0' && choice.exemptionCode !== '' || ratio !== null && (ratio.taxCode === null || !options.withholdingCodes.some(x => x.code === ratio.taxCode))) return null
  return { vatApplies: vat.percent !== '0', vatPercent: vat.percent === '0' ? null : vat.percent, exemptionCode: vat.percent === '0' ? choice.exemptionCode : null,
    withholdingApplies: ratio !== null, withholdingPercent: ratio?.percent ?? null, withholdingCode: ratio?.taxCode ?? null }
}
