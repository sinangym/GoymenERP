import { isBoundUuid } from '../finance/contract'
import { hasExactKeys } from '../finance/wireShape'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'

// Staged core only; publication belongs to the paired shell capability/route change.
export const STAGED_APPROVED_SOURCE_CATALOG_PURPOSE = {
  kind: 'accounting.approved-source-catalog.v1', capabilityId: 'accounting.approved-source-catalog.work-decision',
  routePath: '/onaylarim/accounting.approved-source-catalog.v1/:approvalId',
} as const
export const APPROVED_SOURCE_CATALOG_KIND = STAGED_APPROVED_SOURCE_CATALOG_PURPOSE.kind
export interface WorkCatalogAccount { readonly accountId: string; readonly code: string; readonly name: string }
export interface WorkCatalogMapping { readonly mappingKey: string; readonly side: 'Debit' | 'Credit'; readonly accountId: string }
export interface WorkCatalogBinding {
  readonly sourceId: string; readonly sourceKind: string; readonly sourceVersion: string; readonly ownerProfileId: string
  readonly ownerProfileVersion: string; readonly currencyId: string; readonly postingPurpose: string
  readonly postingRuleVersion: string; readonly mappings: readonly WorkCatalogMapping[]
}
export interface WorkApprovedSourceCatalogView {
  readonly packageId: string; readonly companyId: string; readonly status: 'Prepared' | 'Active' | 'Superseded'; readonly version: string
  readonly catalogVersion: string | null; readonly templateVersion: string; readonly accountPlanSourceVersion: string
  readonly accountsSourceSha256: string; readonly postingMappingsSourceSha256: string; readonly preparedByUserId: string
  readonly preparedAtUtc: string; readonly activatedByUserId: string | null; readonly activatedAtUtc: string | null
  readonly accountCount: number; readonly bindingCount: number
}
export interface WorkApprovedSourceCatalogPreview {
  readonly catalog: WorkApprovedSourceCatalogView
  readonly package: { readonly accounts: readonly WorkCatalogAccount[]; readonly bindings: readonly WorkCatalogBinding[] }
  readonly expectedPackageVersion: string; readonly expectedActiveCatalogVersion: string; readonly sourceFactsDigest: string; readonly previewDigest: string
}
const object = (v: unknown): v is Record<string,unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const same = (a: unknown,b: string) => typeof a === 'string' && a.toLowerCase() === b.toLowerCase()
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v === v.trim()
const sha = (v: unknown): v is string => typeof v === 'string' && v.length === 64 && /^[0-9a-fA-F]{64}$/.test(v)
const digest = (v: unknown) => sha(v) && v === v.toUpperCase()
const count = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v > 0 && v <= 2147483647
export const catalogVersion = (v: unknown,positive = true): v is string => typeof v === 'string' && v === v.trim()
  && (positive ? /^[1-9][0-9]{0,18}$/.test(v) : /^(?:0|[1-9][0-9]{0,18})$/.test(v)) && BigInt(v) <= 9223372036854775807n
export const catalogIncrementable = (v: unknown,positive = true) => catalogVersion(v,positive) && v !== '9223372036854775807'
const utc = (v: unknown): v is string => transferDispatchUtc(v) && v === v.trim()
  && !/^0001-01-01T00:00:00(?:\.0{1,7})?(?:Z|\+00:00)$/.test(v)
  && Number(v.slice(11,13)) < 24 && Number(v.slice(14,16)) < 60 && Number(v.slice(17,19)) < 60
const sourceKinds: readonly string[] = ['finance.manual-receipt.recorded.v1','finance.expense-payment.recorded.v1',
  'finance.cash-transfer.recorded.v1','finance.cash-exchange.book-value-approved.v1','finance.credit-movement.recorded.v1']

export function validApprovedSourceCatalogPreview(value: Record<string,unknown>,company: string,id: string): boolean {
  const h = value.approvedSourceCatalog
  if (!object(h) || !hasExactKeys(h,['catalog','package','expectedPackageVersion','expectedActiveCatalogVersion','sourceFactsDigest','previewDigest'])
    || !object(h.catalog) || !object(h.package) || !hasExactKeys(h.package,['accounts','bindings'])
    || !digest(h.sourceFactsDigest) || !digest(h.previewDigest) || !catalogVersion(h.expectedPackageVersion)
    || !catalogVersion(h.expectedActiveCatalogVersion,false)) return false
  const c = h.catalog
  if (!hasExactKeys(c,['packageId','companyId','status','version','catalogVersion','templateVersion','accountPlanSourceVersion','accountsSourceSha256',
    'postingMappingsSourceSha256','preparedByUserId','preparedAtUtc','activatedByUserId','activatedAtUtc','accountCount','bindingCount'])
    || !guid(c.companyId) || !same(c.companyId,company) || !guid(c.packageId) || !same(c.packageId,id) || !catalogVersion(c.version)
    || c.version !== h.expectedPackageVersion || c.version !== value.sourceVersion || !guid(c.preparedByUserId) || !utc(c.preparedAtUtc)
    || c.templateVersion !== 'accounting-approved-source-package-csv.v1' || !text(c.accountPlanSourceVersion)
    || !sha(c.accountsSourceSha256) || !sha(c.postingMappingsSourceSha256) || !count(c.accountCount) || !count(c.bindingCount)
    || c.status !== 'Prepared' && c.status !== 'Active' && c.status !== 'Superseded'
    || value.pending !== (c.status === 'Prepared') || value.approvalReasonRequired !== false || value.rejectionSupported !== false) return false
  if (c.status === 'Prepared' ? c.catalogVersion !== null || c.activatedByUserId !== null || c.activatedAtUtc !== null
    || !catalogIncrementable(c.version) || !catalogIncrementable(h.expectedActiveCatalogVersion,false)
    : !catalogVersion(c.catalogVersion) || BigInt(c.version) <= 1n || !guid(c.activatedByUserId) || !utc(c.activatedAtUtc)) return false
  const {accounts,bindings} = h.package
  if (!Array.isArray(accounts) || accounts.length !== c.accountCount || !Array.isArray(bindings) || bindings.length !== c.bindingCount) return false
  const accountIds = new Set<string>(),codes = new Set<string>(),bindingKeys = new Set<string>()
  for (const a of accounts) {
    if (!object(a) || !hasExactKeys(a,['accountId','code','name']) || !guid(a.accountId) || !text(a.code)
      || typeof a.name !== 'string' || a.name.trim().length === 0 || accountIds.has(a.accountId.toLowerCase()) || codes.has(a.code)) return false
    accountIds.add(a.accountId.toLowerCase());codes.add(a.code)
  }
  for (const b of bindings) {
    if (!object(b) || !hasExactKeys(b,['sourceId','sourceKind','sourceVersion','ownerProfileId','ownerProfileVersion','currencyId','postingPurpose','postingRuleVersion','mappings'])
      || !guid(b.sourceId) || typeof b.sourceKind !== 'string' || !sourceKinds.includes(b.sourceKind) || !text(b.sourceVersion)
      || !text(b.ownerProfileId) || !text(b.ownerProfileVersion) || !guid(b.currencyId) || !text(b.postingPurpose) || !text(b.postingRuleVersion)
      || !Array.isArray(b.mappings) || b.mappings.length === 0) return false
    const key = JSON.stringify([b.sourceId.toLowerCase(),b.sourceKind,b.sourceVersion,b.ownerProfileId,b.ownerProfileVersion,b.currencyId.toLowerCase()])
    if (bindingKeys.has(key)) return false
    bindingKeys.add(key);const mappings = new Set<string>()
    for (const m of b.mappings) {
      if (!object(m) || !hasExactKeys(m,['mappingKey','side','accountId']) || !text(m.mappingKey) || m.side !== 'Debit' && m.side !== 'Credit'
        || !guid(m.accountId) || !accountIds.has(m.accountId.toLowerCase())) return false
      const identity = JSON.stringify([m.mappingKey,m.side]);if (mappings.has(identity)) return false
      mappings.add(identity)
    }
  }
  if (value.documentLabel !== id.slice(0,8).toLowerCase() || !Array.isArray(value.rules) || value.rules.length !== 1
    || !object(value.rules[0]) || value.rules[0].ruleCode !== 'accounting.approved-source-catalog-activation-required'
    || [value.rules[0].threshold,value.rules[0].actual,value.rules[0].deviation,value.rules[0].unit].some(v=>v!==null)
    || !Array.isArray(value.references) || value.references.length !== 1) return false
  const ref = value.references[0]
  return object(ref) && ref.role === 'approved-source-catalog-package' && same(ref.recordId,id) && ref.label === value.documentLabel && ref.link === null
}
