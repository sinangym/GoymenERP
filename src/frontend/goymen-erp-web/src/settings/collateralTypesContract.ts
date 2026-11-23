export const COLLATERAL_TYPES_PATH = '/settings/collateral-types'
export const COLLATERAL_KINDS = ['BankGuaranteedPayment', 'Dbs', 'GuaranteeLetter', 'CreditCard', 'Vinov', 'Mortgage'] as const
export type CollateralKind = typeof COLLATERAL_KINDS[number]
export interface CollateralType { readonly id: string; readonly code: string; readonly name: string; readonly kind: CollateralKind; readonly active: boolean; readonly ratePercent: string; readonly version: string; readonly recordedBy: string; readonly recordedAtUtc: string }
export interface CollateralTypes { readonly companyId: string; readonly entries: readonly CollateralType[] }
export interface CreateCollateralType { readonly code: string; readonly name: string; readonly kind: CollateralKind; readonly reason: string }
export interface RecordCollateralType { readonly expectedVersion: string; readonly name: string; readonly active: boolean; readonly ratePercent: string; readonly reason: string }
const object = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, keys: readonly string[]) => Object.keys(raw).length === keys.length && keys.every(key => Object.hasOwn(raw, key))
const text = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const guid = (raw: unknown): raw is string => typeof raw === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(raw) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(raw)
// Transport syntax only. The backend owns rate limits and all calculations.
const rate = (raw: unknown): raw is string => typeof raw === 'string' && raw.length <= 64 && /^(0|[1-9]\d*)(\.\d*[1-9])?$/.test(raw)
export function decodeCollateralTypes(raw: unknown, company: string): CollateralTypes | null {
  if (!object(raw) || !exact(raw, ['companyId', 'entries']) || raw.companyId !== company || !Array.isArray(raw.entries)) return null
  const entries: CollateralType[] = []
  for (const item of raw.entries) {
    if (!object(item) || !exact(item, ['id', 'code', 'name', 'kind', 'active', 'ratePercent', 'version', 'recordedBy', 'recordedAtUtc']) || !guid(item.id) || entries.some(entry => entry.id === item.id || entry.code === item.code)
      || !text(item.code) || item.code.length > 32 || !/^[A-Z0-9][A-Z0-9._-]*$/.test(item.code)
      || !text(item.name) || item.name.length > 200 || item.name !== item.name.trim() || /[\u0000-\u001f\u007f]/.test(item.name)
      || typeof item.kind !== 'string' || !COLLATERAL_KINDS.includes(item.kind as CollateralKind)
      || typeof item.active !== 'boolean' || !rate(item.ratePercent) || !text(item.version) || !/^[1-9]\d*$/.test(item.version)
      || item.version.length > 19 || BigInt(item.version) > 9223372036854775807n || !text(item.recordedBy)
      || typeof item.recordedAtUtc !== 'string' || !/^\d{4}-\d\d-\d\dT/.test(item.recordedAtUtc) || !Number.isFinite(Date.parse(item.recordedAtUtc))) return null
    entries.push({ id: item.id, code: item.code, name: item.name, kind: item.kind as CollateralKind, active: item.active,
      ratePercent: item.ratePercent, version: item.version, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc })
  }
  return { companyId: company, entries }
}
