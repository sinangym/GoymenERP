import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'
import { LEGAL_NAME_MAX, PARTY_CODE_MAX } from './tradeName'

export interface MergeImpactSelection { readonly partyId: string; readonly companyId: string; readonly mergedPartyId: string; readonly expectedVersion: string; readonly mergedPartyExpectedVersion: string }
export interface MergeImpactParty { readonly partyId: string; readonly code: string; readonly legalName: string; readonly version: string; readonly isActive: boolean; readonly mergedIntoPartyId: null }
export interface MergeHoldingAccount { readonly companyId: string; readonly accountId: string; readonly partyId: string; readonly version: string; readonly isActive: boolean }
export interface MergeImpact { readonly companyId: string; readonly survivor: MergeImpactParty; readonly merged: MergeImpactParty; readonly holdingAccounts: readonly MergeHoldingAccount[]; readonly changes: { readonly partyId: string; readonly mergedIntoPartyId: string; readonly isActive: false } }
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const keys = (v: Record<string, unknown>, names: readonly string[]) => Object.keys(v).length === names.length && names.every(name => Object.hasOwn(v, name))
// Local exact guards also refuse ECMAScript regex final-line-terminator matches; native guards remain unchanged.
const id = (v: unknown): v is string => typeof v === 'string' && v.length === 36 && isBoundUuid(v)
const version = (v: unknown): v is string => typeof v === 'string' && v === v.trim() && isLosslessVersion(v)
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
function party(v: unknown, expectedId: string, expectedVersion: string): MergeImpactParty | null {
  if (!object(v) || !keys(v, ['partyId','code','legalName','version','isActive','mergedIntoPartyId'])
    || !id(v.partyId) || !same(v.partyId, expectedId) || !version(v.version) || v.version !== expectedVersion
    || !isNonEmptyString(v.code, PARTY_CODE_MAX) || !isNonEmptyString(v.legalName, LEGAL_NAME_MAX)
    || typeof v.isActive !== 'boolean' || v.mergedIntoPartyId !== null) return null
  return { partyId: v.partyId, code: v.code, legalName: v.legalName, version: v.version, isActive: v.isActive, mergedIntoPartyId: null }
}
export function decodeMergeImpact(raw: unknown, expected: MergeImpactSelection): MergeImpact | null {
  if (!id(expected.partyId) || !id(expected.mergedPartyId) || same(expected.partyId, expected.mergedPartyId)
    || !id(expected.companyId) || !version(expected.expectedVersion) || !version(expected.mergedPartyExpectedVersion)
    || !object(raw) || !keys(raw, ['companyId','survivor','merged','holdingAccounts','changes'])
    || !id(raw.companyId) || !same(raw.companyId, expected.companyId) || !Array.isArray(raw.holdingAccounts)
    || !object(raw.changes) || !keys(raw.changes, ['partyId','mergedIntoPartyId','isActive'])
    || !id(raw.changes.partyId) || !same(raw.changes.partyId, expected.mergedPartyId)
    || !id(raw.changes.mergedIntoPartyId) || !same(raw.changes.mergedIntoPartyId, expected.partyId) || raw.changes.isActive !== false) return null
  const survivor = party(raw.survivor, expected.partyId, expected.expectedVersion)
  const merged = party(raw.merged, expected.mergedPartyId, expected.mergedPartyExpectedVersion)
  if (survivor === null || merged === null) return null
  const accounts: MergeHoldingAccount[] = []; const seen = new Set<string>()
  for (const row of raw.holdingAccounts) {
    if (!object(row) || !keys(row, ['companyId','accountId','partyId','version','isActive'])
      || !id(row.companyId) || !id(row.accountId) || seen.has(row.accountId.toLowerCase()) || !id(row.partyId)
      || !same(row.partyId, expected.partyId) && !same(row.partyId, expected.mergedPartyId)
      || !version(row.version) || typeof row.isActive !== 'boolean') return null
    seen.add(row.accountId.toLowerCase())
    accounts.push({ companyId: row.companyId, accountId: row.accountId, partyId: row.partyId, version: row.version, isActive: row.isActive })
  }
  if (!accounts.some(row => same(row.companyId, expected.companyId) && same(row.partyId, expected.partyId))
    || !accounts.some(row => same(row.partyId, expected.mergedPartyId))) return null
  return { companyId: raw.companyId, survivor, merged, holdingAccounts: accounts,
    changes: { partyId: raw.changes.partyId, mergedIntoPartyId: raw.changes.mergedIntoPartyId, isActive: false } }
}
export const MERGE_IMPACT_TEXT = {
  heading: 'Birleştirme etkisi', loading: 'Seçilen carilerin etkisi doğrulanıyor.', unavailable: 'Etki önizlemesi doğrulanamadı. Onay vermeden önce yeniden okuyun.',
  select: 'Etki önizlemesi için birleştirilecek cariyi seçin.', retry: 'Etki önizlemesini yeniden oku',
  survivor: 'Kullanılmaya devam edilecek cari', merged: 'Pasifleştirilecek cari', company: 'Şirket kimliği', account: 'Şirket cari hesabı', party: 'Cari', state: 'Hesap durumu',
  active: 'Aktif', passive: 'Pasif', accounts: 'Etkilenen carilerin mevcut şirket hesapları',
  effect: 'Mükerrer cari pasifleştirilip bu cariye yönlendirilir. Şirket cari hesapları ayrı kalır; bu işlem hesapları birleştirmez.',
  history: 'Geçmiş belge ve hareketlerin cari kimlikleri korunur. Açık kalem veya bakiye aktarımı yapılmaz.',
  retained: 'Önceki birleştirme isteğinizin sonucu bekleniyor. Yalnız aynı isteği yeniden doğrulayabilirsiniz.',
} as const

/** Existing native approval permission, published as a control of the existing Cari detail. */
export const CARI_MERGE_CAPABILITY = 'cari.parties.merge'

/** Purpose-only wrapper; the five native human fields remain decoded by the native decoder above. */
export interface MergePurposeImpact { readonly impact: MergeImpact; readonly seenImpactDigest: string }
export function decodeMergePurposeImpact(raw: unknown, expected: MergeImpactSelection): MergePurposeImpact | null {
  if (!object(raw) || !keys(raw, ['impact', 'seenImpactDigest']) || typeof raw.seenImpactDigest !== 'string'
    || raw.seenImpactDigest.length !== 64 || !/^[A-F0-9]+$/.test(raw.seenImpactDigest)) return null
  const impact = decodeMergeImpact(raw.impact, expected)
  return impact === null ? null : { impact, seenImpactDigest: raw.seenImpactDigest }
}
