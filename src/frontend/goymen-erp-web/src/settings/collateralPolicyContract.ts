import { financePolicyShape as S, type FinancePolicyHeader } from './financePolicyContract'
import { decodeCollateralTypes, type CollateralTypes } from './collateralTypesContract'
export interface BankInstitution { readonly bankInstitutionId: string; readonly name: string; readonly eftCode: string; readonly active: boolean; readonly sourceVersion: string }
export interface BankInstitutionPolicy extends FinancePolicyHeader { readonly entries: readonly BankInstitution[] }
export interface BankInstitutionsWorkspace { readonly companyId: string; readonly businessDate: string; readonly versions: readonly BankInstitutionPolicy[] }
export interface BankInstitutionCatalog { readonly outcome: 'Available' | 'Unavailable'; readonly companyId: string; readonly entries: readonly BankInstitution[]; readonly sourceVersion: string | null; readonly reason: string | null }
export type CollateralRateScope = 'Company' | 'TypeBank' | 'TypeBankRemainingDays'
export interface CollateralRatePolicy extends FinancePolicyHeader { readonly scope: CollateralRateScope; readonly collateralTypeId: string | null; readonly collateralTypeVersion: string | null; readonly bankInstitutionId: string | null; readonly bankSourceVersion: string | null; readonly maximumRemainingDays: number | null; readonly ratePercent: string }
export interface CollateralRatesWorkspace { readonly companyId: string; readonly businessDate: string; readonly types: CollateralTypes; readonly banks: BankInstitutionCatalog; readonly versions: readonly CollateralRatePolicy[] }
function bank(raw: unknown): raw is BankInstitution {
  return S.object(raw) && S.exact(raw, ['bankInstitutionId', 'name', 'eftCode', 'active', 'sourceVersion']) && S.guid(raw.bankInstitutionId)
    && S.text(raw.name) && S.text(raw.eftCode) && typeof raw.active === 'boolean' && S.text(raw.sourceVersion)
}
export function decodeBankCatalog(raw: unknown, company: string): BankInstitutionCatalog | null {
  if (!S.object(raw) || !S.exact(raw, ['outcome', 'companyId', 'entries', 'sourceVersion', 'reason']) || raw.companyId !== company || !Array.isArray(raw.entries) || !raw.entries.every(bank)
    || !S.unique(raw.entries, x => (x as BankInstitution).bankInstitutionId) || !S.unique(raw.entries, x => (x as BankInstitution).eftCode)) return null
  if (raw.outcome === 'Available' ? !S.text(raw.sourceVersion) || raw.reason !== null || raw.entries.some(x => x.sourceVersion !== raw.sourceVersion)
    : raw.outcome !== 'Unavailable' || raw.entries.length !== 0 || raw.sourceVersion !== null || !S.text(raw.reason)) return null
  return raw as unknown as BankInstitutionCatalog
}
export function decodeBankInstitutions(raw: unknown, company: string): BankInstitutionsWorkspace | null {
  if (!S.object(raw) || !S.exact(raw, ['companyId', 'businessDate', 'versions']) || raw.companyId !== company || !S.date(raw.businessDate) || !Array.isArray(raw.versions)) return null
  if (!raw.versions.every(x => S.object(x) && S.exact(x, [...S.headerKeys, 'entries']) && S.header(x, company) && Array.isArray(x.entries) && x.entries.every(bank)
    && S.unique(x.entries, y => (y as BankInstitution).bankInstitutionId) && S.unique(x.entries, y => (y as BankInstitution).eftCode))
    || !S.unique(raw.versions, x => (x as BankInstitutionPolicy).policyId) || !S.unique(raw.versions, x => (x as BankInstitutionPolicy).version)) return null
  return raw as unknown as BankInstitutionsWorkspace
}
const rateFields = ['scope', 'collateralTypeId', 'collateralTypeVersion', 'bankInstitutionId', 'bankSourceVersion', 'maximumRemainingDays', 'ratePercent']
function scope(raw: Record<string, unknown>) {
  return raw.scope === 'Company' ? raw.collateralTypeId === null && raw.collateralTypeVersion === null && raw.bankInstitutionId === null && raw.bankSourceVersion === null && raw.maximumRemainingDays === null
    : (raw.scope === 'TypeBank' || raw.scope === 'TypeBankRemainingDays') && S.guid(raw.collateralTypeId) && S.version(raw.collateralTypeVersion) && S.guid(raw.bankInstitutionId) && S.text(raw.bankSourceVersion)
      && (raw.scope === 'TypeBank' ? raw.maximumRemainingDays === null : typeof raw.maximumRemainingDays === 'number' && Number.isSafeInteger(raw.maximumRemainingDays) && raw.maximumRemainingDays >= 0)
}
export function decodeCollateralRates(raw: unknown, company: string): CollateralRatesWorkspace | null {
  if (!S.object(raw) || !S.exact(raw, ['companyId', 'businessDate', 'types', 'banks', 'versions']) || raw.companyId !== company || !S.date(raw.businessDate)
    || decodeCollateralTypes(raw.types, company) === null || decodeBankCatalog(raw.banks, company) === null || !Array.isArray(raw.versions)
    || !raw.versions.every(x => S.object(x) && S.exact(x, [...S.headerKeys, ...rateFields]) && S.header(x, company) && scope(x) && S.amount(x.ratePercent))
    || !S.unique(raw.versions, x => (x as CollateralRatePolicy).policyId)) return null
  return raw as unknown as CollateralRatesWorkspace
}
export interface CollateralRatePreview { readonly outcome: 'Available' | 'Unavailable'; readonly ratePercent: string | null; readonly sourceVersion: string | null; readonly scope: string | null; readonly reason: string | null; readonly collateralTypeVersion?: string }
export function decodeCollateralRatePreview(raw: unknown): CollateralRatePreview | null {
  if (!S.object(raw) || !S.exact(raw, ['outcome', 'ratePercent', 'sourceVersion', 'scope', 'reason', ...(Object.hasOwn(raw, 'collateralTypeVersion') ? ['collateralTypeVersion'] : [])])
    || Object.hasOwn(raw, 'collateralTypeVersion') && (!S.version(raw.collateralTypeVersion) || raw.outcome !== 'Available')) return null
  if (raw.outcome === 'Available' ? !S.amount(raw.ratePercent) || !S.text(raw.sourceVersion) || !['Company', 'Type', 'TypeBank', 'TypeBankRemainingDays'].includes(String(raw.scope)) || raw.reason !== null
    : raw.outcome !== 'Unavailable' || raw.ratePercent !== null || raw.sourceVersion !== null || raw.scope !== null || !S.text(raw.reason)) return null
  return raw as unknown as CollateralRatePreview
}
