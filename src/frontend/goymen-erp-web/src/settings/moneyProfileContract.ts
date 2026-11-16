import { isDateText, isUuid } from './discountLimitContract'
import { decodeMeta, type SalesPolicyVersionMeta } from './salesPolicyContract'

export const MONEY_PROFILES_PATH = '/settings/money-profiles'
export interface MoneyProfileVersion extends SalesPolicyVersionMeta { readonly scale: number; readonly unitPriceScale: number | null }
export interface MoneyProfileSeries { readonly currencyId: string; readonly currencyCode: string; readonly operation: string; readonly version: string; readonly versions: readonly MoneyProfileVersion[] }
export interface MoneyProfileView { readonly companyId: string; readonly today: string; readonly profiles: readonly MoneyProfileSeries[] }
export interface RecordMoneyProfileBody { readonly expectedVersion: string; readonly currencyId: string; readonly operation: string; readonly scale: number; readonly unitPriceScale: number | null; readonly isActive: boolean; readonly reason: string; readonly effectiveFrom: string | null }

const VERSION_FIELDS = ['profileId', 'version', 'scale', 'unitPriceScale', 'isActive', 'effectiveFrom', 'effectiveThrough', 'state', 'sourceKind', 'decisionReference', 'recordedBy', 'recordedAtUtc', 'reason']
const record = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, keys: readonly string[]) => Object.keys(raw).length === keys.length && keys.every((key) => Object.hasOwn(raw, key))
const version = (raw: unknown): raw is string => typeof raw === 'string' && raw.length <= 19 && /^[1-9]\d*$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const scale = (raw: unknown): raw is number => typeof raw === 'number' && Number.isInteger(raw) && raw >= 0 && raw <= 28

export function decodeMoneyProfiles(raw: unknown, company: string): MoneyProfileView | null {
  if (!record(raw) || !exact(raw, ['companyId', 'today', 'profiles']) || raw.companyId !== company || !isDateText(raw.today) || !Array.isArray(raw.profiles)) return null
  const profiles: MoneyProfileSeries[] = []
  const seen = new Set<string>()
  for (const item of raw.profiles) {
    if (!record(item) || !exact(item, ['currencyId', 'currencyCode', 'operation', 'version', 'versions']) || !isUuid(item.currencyId)
      || typeof item.currencyCode !== 'string' || !item.currencyCode.trim() || item.currencyCode.length > 32
      || typeof item.operation !== 'string' || !item.operation.trim() || item.operation.length > 128 || !version(item.version)
      || !Array.isArray(item.versions) || !item.versions.length) return null
    const key = `${item.currencyId}:${item.operation}`
    if (seen.has(key)) return null
    seen.add(key)
    const versions: MoneyProfileVersion[] = []
    const numbers = new Set<string>()
    for (const value of item.versions) {
      if (!record(value) || !exact(value, VERSION_FIELDS) || !version(value.version) || !scale(value.scale) || (value.unitPriceScale !== null && !scale(value.unitPriceScale))) return null
      const meta = decodeMeta(value, value.recordedBy !== null)
      if (meta === null || numbers.has(meta.version) || BigInt(meta.version) > BigInt(item.version)) return null
      numbers.add(meta.version)
      versions.push({ ...meta, scale: value.scale, unitPriceScale: value.unitPriceScale as number | null })
    }
    if (!numbers.has(item.version)) return null
    profiles.push({ currencyId: item.currencyId, currencyCode: item.currencyCode, operation: item.operation, version: item.version, versions })
  }
  return { companyId: company, today: raw.today, profiles }
}
