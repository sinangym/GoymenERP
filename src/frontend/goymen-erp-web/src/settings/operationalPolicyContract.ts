export type OperationalPolicyKind = 'scrap-cost' | 'notification-retention'
export interface OperationalPolicyVersion { readonly version: string; readonly value: boolean | number; readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string }
export interface OperationalPolicy { readonly companyId: string; readonly value: boolean | number; readonly version: string; readonly versions: readonly OperationalPolicyVersion[] }
export interface RecordOperationalPolicyBody { readonly expectedVersion: string; readonly includeScrapCost?: boolean; readonly activeDays?: number; readonly reason: string }
export const operationalPolicyPath = (kind: OperationalPolicyKind) => `/settings/company-policies/${kind === 'scrap-cost' ? 'manufacturing-scrap-cost' : 'notification-retention'}`
const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) => Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const nonempty = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const version = (raw: unknown): raw is string => typeof raw === 'string' && /^(0|[1-9]\d{0,18})$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const validValue = (raw: unknown, kind: OperationalPolicyKind): raw is boolean | number => kind === 'scrap-cost' ? typeof raw === 'boolean' : typeof raw === 'number' && Number.isInteger(raw) && raw >= 0 && raw <= 2147483647
export function decodeOperationalPolicy(raw: unknown, company: string, kind: OperationalPolicyKind): OperationalPolicy | null {
  const field = kind === 'scrap-cost' ? 'includeScrapCost' : 'activeDays'
  if (!object(raw) || !exact(raw, ['companyId', field, 'version', 'versions']) || raw.companyId !== company || !version(raw.version) || !validValue(raw[field], kind) || !Array.isArray(raw.versions)) return null
  const history: OperationalPolicyVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !exact(item, ['version', 'value', 'recordedBy', 'recordedAtUtc', 'reason']) || !version(item.version) || item.version === '0' || !validValue(item.value, kind)
      || !nonempty(item.recordedBy) || !nonempty(item.reason) || typeof item.recordedAtUtc !== 'string' || !/^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(item.recordedAtUtc) || !Number.isFinite(Date.parse(item.recordedAtUtc))
      || BigInt(item.version) > BigInt(raw.version) || (history.length > 0 && BigInt(history[history.length - 1].version) <= BigInt(item.version))) return null
    history.push({ version: item.version, value: item.value, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  if (raw.version === '0' ? history.length !== 0 || raw[field] !== (kind === 'scrap-cost' ? true : 90) : history[0]?.version !== raw.version || history[0]?.value !== raw[field]) return null
  return { companyId: company, value: raw[field], version: raw.version, versions: history }
}
