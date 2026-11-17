import { isDateText } from './discountLimitContract'

export const PURCHASE_INVOICE_VARIANCE_POLICY_PERMISSION = 'settings.purchase-invoice-variance-policies.manage'
export const PURCHASE_INVOICE_VARIANCE_POLICY_PATH = '/settings/purchase-invoice-variance-policy'
export interface PurchaseInvoiceVariancePolicyVersion {
  readonly policyId: string; readonly version: string; readonly amountTolerance: string; readonly rateTolerance: string
  readonly publishedBy: string; readonly publishedAtUtc: string; readonly reason: string
}
export interface PurchaseInvoiceVariancePolicy {
  readonly companyId: string; readonly version: string; readonly current: PurchaseInvoiceVariancePolicyVersion | null
  readonly versions: readonly PurchaseInvoiceVariancePolicyVersion[]
}
export interface PublishPurchaseInvoiceVariancePolicyBody {
  readonly expectedVersion: string; readonly amountTolerance: string; readonly rateTolerance: string; readonly reason: string
}
const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) => Object.keys(raw).length === names.length && names.every((key) => Object.hasOwn(raw, key))
const text = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const uuid = (raw: unknown): raw is string => typeof raw === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(raw) && !/^0{8}-(?:0{4}-){3}0{12}$/.test(raw)
const version = (raw: unknown): raw is string => typeof raw === 'string' && /^(0|[1-9]\d{0,18})$/.test(raw) && BigInt(raw) <= 9223372036854775807n
/** Shape validation only. No arithmetic, conversion or approval rule is duplicated in the browser. */
export const invoiceToleranceInput = (raw: string) => /^(0|[1-9]\d{0,17})(?:\.\d{1,6})?$/.test(raw)
const canonicalTolerance = (raw: unknown): raw is string => typeof raw === 'string' && invoiceToleranceInput(raw) && (!raw.includes('.') || !raw.endsWith('0'))
function item(raw: unknown): PurchaseInvoiceVariancePolicyVersion | null {
  if (!object(raw) || !exact(raw, ['policyId', 'version', 'amountTolerance', 'rateTolerance', 'publishedBy', 'publishedAtUtc', 'reason'])
    || !uuid(raw.policyId) || !version(raw.version) || raw.version === '0' || !canonicalTolerance(raw.amountTolerance) || !canonicalTolerance(raw.rateTolerance)
    || !text(raw.publishedBy) || raw.publishedBy.length > 128 || !text(raw.reason) || raw.reason.length > 4000
    || typeof raw.publishedAtUtc !== 'string' || !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(raw.publishedAtUtc)
    || !isDateText(raw.publishedAtUtc.slice(0, 10)) || !Number.isFinite(Date.parse(raw.publishedAtUtc))) return null
  return { policyId: raw.policyId, version: raw.version, amountTolerance: raw.amountTolerance, rateTolerance: raw.rateTolerance,
    publishedBy: raw.publishedBy, publishedAtUtc: raw.publishedAtUtc, reason: raw.reason }
}
export function decodePurchaseInvoiceVariancePolicy(raw: unknown, company: string): PurchaseInvoiceVariancePolicy | null {
  if (!object(raw) || !exact(raw, ['companyId', 'version', 'current', 'versions']) || raw.companyId !== company || !uuid(raw.companyId) || !version(raw.version) || !Array.isArray(raw.versions)) return null
  const versions: PurchaseInvoiceVariancePolicyVersion[] = []; const ids = new Set<string>()
  for (const candidate of raw.versions) {
    const decoded = item(candidate)
    if (decoded === null || ids.has(decoded.policyId.toLowerCase()) || BigInt(decoded.version) !== BigInt(raw.version) - BigInt(versions.length)) return null
    ids.add(decoded.policyId.toLowerCase()); versions.push(decoded)
  }
  if (BigInt(versions.length) !== BigInt(raw.version)) return null
  const current = raw.current === null ? null : item(raw.current)
  if (raw.version === '0' ? raw.current !== null || versions.length !== 0 : current === null || JSON.stringify(current) !== JSON.stringify(versions[0])) return null
  return { companyId: company, version: raw.version, current, versions }
}
