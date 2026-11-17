export type TemplateModule = 1 | 2 | 3
export const BRANDING_MANAGE = 'documents.company-branding.manage'
export const TEMPLATE_CATALOG = {
  1: { code: 'sales.order-pdf', slug: 'sales', prepare: 'documents.templates.sales.prepare', publish: 'documents.templates.sales.publish' },
  2: { code: 'inventory.material-label.technical', slug: 'inventory', prepare: 'documents.templates.inventory.prepare', publish: 'documents.templates.inventory.publish' },
  3: { code: 'logistics.dispatch-note', slug: 'logistics', prepare: 'documents.templates.logistics.prepare', publish: 'documents.templates.logistics.publish' },
} as const
export interface BrandingAsset {
  readonly companyId: string; readonly documentId: string; readonly versionNo: number; readonly contentType: string
  readonly sizeBytes: number; readonly sha256: string; readonly scannerId: string; readonly uploadedBy: string; readonly uploadedAtUtc: string
}
export interface CompanyBranding {
  readonly companyId: string; readonly version: string; readonly logo: BrandingAsset | null; readonly letterhead: BrandingAsset | null
  readonly preparedBy: string; readonly preparedAtUtc: string; readonly reason: string
}
export interface TemplateDraft extends Omit<CompanyBranding, 'version'> {
  readonly module: TemplateModule; readonly templateCode: string; readonly version: string; readonly brandingVersion: string
}
export interface TemplatePublication {
  readonly publicationId: string; readonly companyId: string; readonly module: TemplateModule; readonly templateCode: string
  readonly version: string; readonly templateVersion: string; readonly draftVersion: string; readonly brandingVersion: string
  readonly logo: BrandingAsset | null; readonly letterhead: BrandingAsset | null; readonly preparedBy: string
  readonly publishedBy: string; readonly publishedAtUtc: string; readonly reason: string
}
export interface BrandingCurrent {
  readonly companyId: string; readonly branding: CompanyBranding | null
  readonly uploadPolicy: { readonly allowedContentTypes: readonly string[]; readonly maximumUploadBytes: number; readonly canUpload: boolean }
}
export interface TemplateCurrent {
  readonly companyId: string; readonly module: TemplateModule; readonly templateCode: string
  readonly draft: TemplateDraft | null; readonly publication: TemplatePublication | null; readonly currentBranding: CompanyBranding | null
}
export interface AssetReference { readonly documentId: string; readonly versionNo: number }
export interface SetBranding { readonly expectedVersion: string; readonly logo: AssetReference | null; readonly letterhead: AssetReference | null; readonly reason: string }
export interface PrepareTemplate { readonly module: TemplateModule; readonly templateCode: string; readonly expectedDraftVersion: string; readonly expectedBrandingVersion: string; readonly reason: string }
export interface PublishTemplate { readonly module: TemplateModule; readonly templateCode: string; readonly expectedDraftVersion: string; readonly expectedPublicationVersion: string; readonly reason: string }
export interface BrandingMutation<T> { readonly value: T; readonly isReplay: boolean; readonly originalCorrelationId: string }

export const brandingGuid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v)
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every((k) => Object.hasOwn(v, k))
const text = (v: unknown, max: number): v is string => typeof v === 'string' && v.length <= max && v.trim() !== '' && !/[\u0000-\u001f\u007f-\u009f]/.test(v)
const sameCompany = (v: unknown, company: string) => brandingGuid(v) && v.toLowerCase() === company.toLowerCase()
export const brandingVersion = (v: unknown, zero = false): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,18})$/.test(v) && (zero || v !== '0') && BigInt(v) <= 9223372036854775807n
function utc(v: unknown): v is string {
  if (typeof v !== 'string' || !/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v) || !Number.isFinite(Date.parse(v))) return false
  return new Date(`${v.slice(0, 10)}T00:00:00Z`).toISOString().slice(0, 10) === v.slice(0, 10)
}
const mime = (v: unknown) => v === 'application/pdf' || v === 'image/png' || v === 'image/jpeg'
const assetKeys = ['companyId', 'documentId', 'versionNo', 'contentType', 'sizeBytes', 'sha256', 'scannerId', 'uploadedBy', 'uploadedAtUtc']
export function decodeBrandingAsset(v: unknown, company: string): BrandingAsset | null {
  return object(v) && exact(v, assetKeys) && sameCompany(v.companyId, company) && brandingGuid(v.documentId)
    && typeof v.versionNo === 'number' && Number.isSafeInteger(v.versionNo) && v.versionNo > 0 && v.versionNo <= 2147483647
    && mime(v.contentType) && typeof v.sizeBytes === 'number' && Number.isSafeInteger(v.sizeBytes) && v.sizeBytes > 0
    && typeof v.sha256 === 'string' && /^[0-9a-f]{64}$/i.test(v.sha256) && text(v.scannerId, 64) && text(v.uploadedBy, 128) && utc(v.uploadedAtUtc)
    ? v as unknown as BrandingAsset : null
}
function slots(v: Record<string, unknown>, company: string) {
  return (v.logo === null || decodeBrandingAsset(v.logo, company) !== null) && (v.letterhead === null || decodeBrandingAsset(v.letterhead, company) !== null)
}
const brandingKeys = ['companyId', 'version', 'logo', 'letterhead', 'preparedBy', 'preparedAtUtc', 'reason']
export function decodeCompanyBranding(v: unknown, company: string): CompanyBranding | null {
  return object(v) && exact(v, brandingKeys) && sameCompany(v.companyId, company) && brandingVersion(v.version)
    && slots(v, company) && text(v.preparedBy, 128) && utc(v.preparedAtUtc) && text(v.reason, 500) ? v as unknown as CompanyBranding : null
}
export function decodeBrandingCurrent(v: unknown, company: string): BrandingCurrent | null {
  if (!object(v) || !exact(v, ['companyId', 'branding', 'uploadPolicy']) || !sameCompany(v.companyId, company)
    || !(v.branding === null || decodeCompanyBranding(v.branding, company) !== null) || !object(v.uploadPolicy)) return null
  const p = v.uploadPolicy
  return exact(p, ['allowedContentTypes', 'maximumUploadBytes', 'canUpload']) && Array.isArray(p.allowedContentTypes)
    && p.allowedContentTypes.length > 0 && p.allowedContentTypes.length <= 3 && p.allowedContentTypes.every(mime)
    && new Set(p.allowedContentTypes).size === p.allowedContentTypes.length && typeof p.maximumUploadBytes === 'number'
    && Number.isSafeInteger(p.maximumUploadBytes) && p.maximumUploadBytes > 0 && typeof p.canUpload === 'boolean' ? v as unknown as BrandingCurrent : null
}
function frame(v: Record<string, unknown>, company: string, module: TemplateModule) {
  return sameCompany(v.companyId, company) && v.module === module && v.templateCode === TEMPLATE_CATALOG[module].code
}
export function decodeTemplateDraft(v: unknown, company: string, module: TemplateModule): TemplateDraft | null {
  return object(v) && exact(v, [...brandingKeys, 'module', 'templateCode', 'brandingVersion']) && frame(v, company, module)
    && brandingVersion(v.version) && brandingVersion(v.brandingVersion) && slots(v, company)
    && text(v.preparedBy, 128) && utc(v.preparedAtUtc) && text(v.reason, 500) ? v as unknown as TemplateDraft : null
}
export function decodeTemplatePublication(v: unknown, company: string, module: TemplateModule): TemplatePublication | null {
  return object(v) && exact(v, ['publicationId', 'companyId', 'module', 'templateCode', 'version', 'templateVersion', 'draftVersion', 'brandingVersion',
    'logo', 'letterhead', 'preparedBy', 'publishedBy', 'publishedAtUtc', 'reason']) && frame(v, company, module) && brandingGuid(v.publicationId)
    && brandingVersion(v.version) && v.templateVersion === `pub.${v.version}` && brandingVersion(v.draftVersion) && brandingVersion(v.brandingVersion)
    && (v.logo !== null || v.letterhead !== null) && slots(v, company) && text(v.preparedBy, 128) && text(v.publishedBy, 128)
    && v.preparedBy !== v.publishedBy && utc(v.publishedAtUtc) && text(v.reason, 500) ? v as unknown as TemplatePublication : null
}
export function decodeTemplateCurrent(v: unknown, company: string, module: TemplateModule): TemplateCurrent | null {
  return object(v) && exact(v, ['companyId', 'module', 'templateCode', 'draft', 'publication', 'currentBranding']) && frame(v, company, module)
    && (v.draft === null || decodeTemplateDraft(v.draft, company, module) !== null)
    && (v.publication === null || decodeTemplatePublication(v.publication, company, module) !== null)
    && (v.currentBranding === null || decodeCompanyBranding(v.currentBranding, company) !== null) ? v as unknown as TemplateCurrent : null
}
export function decodeBrandingMutation<T>(raw: unknown, field: string, decoder: (v: unknown) => T | null): BrandingMutation<T> | null {
  if (!object(raw) || !exact(raw, [field, 'isReplay', 'originalCorrelationId']) || typeof raw.isReplay !== 'boolean' || !text(raw.originalCorrelationId, 128)) return null
  const value = decoder(raw[field]); return value === null ? null : { value, isReplay: raw.isReplay, originalCorrelationId: raw.originalCorrelationId }
}
