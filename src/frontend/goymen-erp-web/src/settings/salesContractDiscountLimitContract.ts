export const SALES_CONTRACT_DISCOUNT_LIMIT_PATH = '/settings/company-policies/sales-contract-discount-limit'
export interface ContractDiscountLimitVersion { readonly version: string; readonly limitPercent: string; readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string }
export interface ContractDiscountLimit { readonly companyId: string; readonly limitPercent: string | null; readonly version: string; readonly versions: readonly ContractDiscountLimitVersion[] }
export interface RecordContractDiscountLimit { readonly expectedVersion: string; readonly limitPercent: string; readonly reason: string }
const object = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null && !Array.isArray(raw)
const text = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const version = (raw: unknown): raw is string => typeof raw === 'string' && raw.length <= 19 && /^(0|[1-9]\d*)$/.test(raw) && BigInt(raw) <= 9223372036854775807n
// Published percentage transport shape only; no order/contract approval or money calculation runs in the screen.
const rate = (raw: unknown): raw is string => typeof raw === 'string' && /^(100|(?:[1-9]?\d)(?:\.\d{1,4})?)$/.test(raw)
export function decodeContractDiscountLimit(raw: unknown, company: string): ContractDiscountLimit | null {
  if (!object(raw) || raw.companyId !== company || !version(raw.version) || !Array.isArray(raw.versions)
    || raw.limitPercent !== null && !rate(raw.limitPercent)) return null
  const versions: ContractDiscountLimitVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !version(item.version) || item.version === '0' || !rate(item.limitPercent) || !text(item.recordedBy) || !text(item.reason)
      || typeof item.recordedAtUtc !== 'string' || !/^\d{4}-\d\d-\d\dT/.test(item.recordedAtUtc) || !Number.isFinite(Date.parse(item.recordedAtUtc))
      || BigInt(item.version) > BigInt(raw.version) || versions.some(entry => entry.version === item.version)) return null
    versions.push({ version: item.version, limitPercent: item.limitPercent, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  const current = versions.find(entry => entry.version === raw.version)
  if (raw.version === '0' ? raw.limitPercent !== null || versions.length !== 0 : current === undefined || current.limitPercent !== raw.limitPercent) return null
  return { companyId: company, limitPercent: raw.limitPercent, version: raw.version, versions }
}
