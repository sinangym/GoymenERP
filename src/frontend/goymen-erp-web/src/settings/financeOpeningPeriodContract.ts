export const FINANCE_OPENING_PERIOD_PATH = '/settings/company-policies/finance-opening-period'
export interface FinanceOpeningVersion { readonly version: string; readonly open: boolean; readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string }
export interface FinanceOpeningPeriod { readonly companyId: string; readonly state: 0 | 1 | 2; readonly version: string; readonly versions: readonly FinanceOpeningVersion[] }
export interface RecordFinanceOpeningPeriodBody { readonly expectedVersion: string; readonly open: boolean; readonly reason: string }
const record = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null && !Array.isArray(raw)
const text = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
/** Exact key sets of FinanceOpeningPeriodViewV1 / FinanceOpeningPeriodVersionViewV1: an unknown or missing key fails closed. */
const VIEW_FIELDS = ['companyId', 'state', 'version', 'versions'] as const
const VERSION_FIELDS = ['version', 'open', 'recordedBy', 'recordedAtUtc', 'reason'] as const
const exact = (raw: Record<string, unknown>, keys: readonly string[]) => Object.keys(raw).length === keys.length && keys.every((key) => Object.hasOwn(raw, key))
const version = (raw: unknown): raw is string => typeof raw === 'string' && raw.length <= 19 && /^(0|[1-9]\d*)$/.test(raw) && BigInt(raw) <= 9223372036854775807n
export function decodeFinanceOpeningPeriod(raw: unknown, company: string): FinanceOpeningPeriod | null {
  if (!record(raw) || !exact(raw, VIEW_FIELDS) || raw.companyId !== company || (raw.state !== 0 && raw.state !== 1 && raw.state !== 2) || !version(raw.version) || !Array.isArray(raw.versions)) return null
  const versions: FinanceOpeningVersion[] = []
  for (const item of raw.versions) {
    if (!record(item) || !exact(item, VERSION_FIELDS) || !version(item.version) || item.version === '0' || typeof item.open !== 'boolean' || !text(item.recordedBy) || !text(item.reason)
      || typeof item.recordedAtUtc !== 'string' || !/^\d{4}-\d\d-\d\dT/.test(item.recordedAtUtc) || !Number.isFinite(Date.parse(item.recordedAtUtc))
      || BigInt(item.version) > BigInt(raw.version) || versions.some((entry) => entry.version === item.version)) return null
    versions.push({ version: item.version, open: item.open, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  const current = versions.find((entry) => entry.version === raw.version)
  if (raw.version === '0' ? raw.state !== 0 || versions.length !== 0 : current === undefined || raw.state !== (current.open ? 1 : 2)) return null
  return { companyId: company, state: raw.state, version: raw.version, versions }
}
