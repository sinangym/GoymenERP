/**
 * YOL-D31 (WP14) — the Cari card's link to the Göymen group company the party is.
 *
 * Mirrors, never invents: the shape is `CounterpartyGroupCompanyLinkView` (operations `GetCounterpartyGroupCompanyLinkV1`,
 * `UpdateCounterpartyGroupCompanyLinkV1`). A party is a group company ONLY through this link (never by a tax-number match);
 * the intercompany report reads it. The link is a field of the shared party card, so every company sees the same value.
 * The decoder is strict; every user-facing word lives in `GROUP_COMPANY_LINK_TEXT`.
 */

import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'
import { LEGAL_NAME_MAX, PARTY_CODE_MAX } from './tradeName'

export const GROUP_COMPANY_LINK_PROBLEM_CODE = {
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
} as const

const KNOWN_CODES = new Set<string>(Object.values(GROUP_COMPANY_LINK_PROBLEM_CODE))

export function groupCompanyLinkProblemCodeOf(raw: unknown): string | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_CODES.has(code) ? code : null
}

export function groupCompanyLinkPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/group-company-link`
}

export interface GroupCompanyLinkView {
  readonly partyId: string
  readonly companyId: string
  readonly code: string
  readonly version: string
  readonly legalName: string
  readonly groupCompanyId: string | null
  readonly viewer: { readonly canUpdateGroupCompanyLink: boolean }
}

const VIEW_KEYS = ['code', 'companyId', 'groupCompanyId', 'legalName', 'partyId', 'version', 'viewer'] as const
const VIEWER_KEYS = ['canUpdateGroupCompanyLink'] as const

const isObject = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null && !Array.isArray(raw)

function hasExactKeys(raw: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(raw).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}

export function decodeGroupCompanyLinkView(raw: unknown, expected: { readonly partyId: string; readonly companyId: string }): GroupCompanyLinkView | null {
  if (!isObject(raw) || !hasExactKeys(raw, VIEW_KEYS)) return null
  if (!isObject(raw.viewer) || !hasExactKeys(raw.viewer, VIEWER_KEYS)) return null
  if (!isBoundUuid(raw.partyId) || raw.partyId.toLowerCase() !== expected.partyId.toLowerCase()
    || !isBoundUuid(raw.companyId) || raw.companyId.toLowerCase() !== expected.companyId.toLowerCase()
    || !isNonEmptyString(raw.code, PARTY_CODE_MAX) || !isLosslessVersion(raw.version) || !isNonEmptyString(raw.legalName, LEGAL_NAME_MAX)
    || !(raw.groupCompanyId === null || isBoundUuid(raw.groupCompanyId)) || typeof raw.viewer.canUpdateGroupCompanyLink !== 'boolean') return null
  return {
    partyId: raw.partyId, companyId: raw.companyId, code: raw.code, version: raw.version, legalName: raw.legalName,
    groupCompanyId: raw.groupCompanyId, viewer: { canUpdateGroupCompanyLink: raw.viewer.canUpdateGroupCompanyLink },
  }
}

export const GROUP_COMPANY_LINK_TEXT = {
  heading: 'Grup şirketi bağlantısı',
  loading: 'Grup şirketi bağlantısı yükleniyor…',
  loadError: 'Grup şirketi bağlantısı okunamadı.',
  forbidden: 'Bu bilgiyi görme yetkiniz yok.',
  notFound: 'Bu cari seçili şirkette kayıtlı değil.',
  readAgain: 'Yeniden dene',
  label: 'Bu cari hangi grup şirketidir?',
  none: 'Grup şirketi değil',
  unknownCompany: 'Erişiminiz dışındaki bir grup şirketi',
  note: 'Grup içi işlem dökümü yalnız bu bağlantıyı kullanır; vergi numarası eşleşmesi grup şirketi saymaz. Bağlantı cari kartına aittir, bütün şirketlerde aynı görünür.',
  save: 'Bağlantıyı kaydet',
  saving: 'Kaydediliyor…',
  saved: 'Grup şirketi bağlantısı kaydedildi.',
  cleared: 'Grup şirketi bağlantısı kaldırıldı.',
  readOnlyNote: 'Bağlantıyı değiştirme yetkiniz yok; yalnız görüntüleyebilirsiniz.',
  directoryError: 'Şirket listesi okunamadı; bağlantı değiştirilemez.',
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome: 'İşlemin sonucu belirsiz: bağlantı kesildi veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden: 'Bu işlem için yetkiniz yok.',
  refusal: {
    [GROUP_COMPANY_LINK_PROBLEM_CODE.versionConflict]: 'Kayıt siz düzenlerken başka biri tarafından değiştirildi. Güncel bilgiler yüklendi; yeniden seçin.',
    [GROUP_COMPANY_LINK_PROBLEM_CODE.notFound]: 'Kayıt bulunamadı veya bu şirkette kullanılamıyor.',
    [GROUP_COMPANY_LINK_PROBLEM_CODE.idempotencyConflict]: 'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [GROUP_COMPANY_LINK_PROBLEM_CODE.validationFailed]: 'Seçilen şirket grupta etkin bir şirket değil.',
  } as Readonly<Record<string, string>>,
} as const
