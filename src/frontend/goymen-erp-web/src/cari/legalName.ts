/**
 * F-027 / ALS-D95 — the Cari card's official legal name (resmi/hukuki unvan).
 *
 * Mirrors, never invents: the shape is `CounterpartyLegalNameView` of the generated contract (operations
 * `GetCounterpartyLegalNameV1`, `UpdateCounterpartyLegalNameV1`).
 *
 *  - The legal name is changed in ONE step, without an approval step, by the holder of the SEPARATE legal-name
 *    permission (19.22). The server measures that permission for this actor and company and says so in `viewer`.
 *  - It is fenced on the PARTY version, the same version the trade name and the tax identity move.
 *  - The party is shared by every company holding an account for it, so the change is seen by all of them; the server
 *    records the old and the new legal name in the audit.
 *
 * The decoder is strict: an unknown shape is `null`, never a partly trusted object. Every user-facing word lives in
 * `LEGAL_NAME_TEXT` below.
 */

import { LEGAL_NAME_MAX, PARTY_CODE_MAX } from './tradeName'
import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'

export { LEGAL_NAME_MAX }

/** The endpoint's own stable codes (`CounterpartyProblemCodesV1`). */
export const LEGAL_NAME_PROBLEM_CODE = {
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
} as const

export type LegalNameProblemCode = (typeof LEGAL_NAME_PROBLEM_CODE)[keyof typeof LEGAL_NAME_PROBLEM_CODE]

const KNOWN_CODES = new Set<string>(Object.values(LEGAL_NAME_PROBLEM_CODE))

export function legalNameProblemCodeOf(raw: unknown): LegalNameProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_CODES.has(code) ? (code as LegalNameProblemCode) : null
}

export function legalNamePath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/legal-name`
}

export interface LegalNameView {
  readonly partyId: string
  readonly companyId: string
  readonly code: string
  readonly version: string
  readonly legalName: string
  readonly viewer: { readonly canUpdateLegalName: boolean }
}

const VIEW_KEYS = ['code', 'companyId', 'legalName', 'partyId', 'version', 'viewer'] as const
const VIEWER_KEYS = ['canUpdateLegalName'] as const

const isObject = (raw: unknown): raw is Record<string, unknown> =>
  typeof raw === 'object' && raw !== null && !Array.isArray(raw)

function hasExactKeys(raw: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(raw).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}

/** The read must answer for the party and company the request named; another record is not this one. */
export function decodeLegalNameView(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): LegalNameView | null {
  if (!isObject(raw) || !hasExactKeys(raw, VIEW_KEYS)) return null
  if (!isObject(raw.viewer) || !hasExactKeys(raw.viewer, VIEWER_KEYS)) return null
  if (
    !isBoundUuid(raw.partyId) ||
    raw.partyId.toLowerCase() !== expected.partyId.toLowerCase() ||
    !isBoundUuid(raw.companyId) ||
    raw.companyId.toLowerCase() !== expected.companyId.toLowerCase() ||
    !isNonEmptyString(raw.code, PARTY_CODE_MAX) ||
    !isLosslessVersion(raw.version) ||
    !isNonEmptyString(raw.legalName, LEGAL_NAME_MAX) ||
    typeof raw.viewer.canUpdateLegalName !== 'boolean'
  ) {
    return null
  }
  return {
    partyId: raw.partyId,
    companyId: raw.companyId,
    code: raw.code,
    version: raw.version,
    legalName: raw.legalName,
    viewer: { canUpdateLegalName: raw.viewer.canUpdateLegalName },
  }
}

export type LegalNameCheck =
  | { readonly ok: true; readonly value: string }
  | { readonly ok: false; readonly message: string }

/** Mirrors the server's normalization (trim; required; at most 250); the server decides again. */
export function checkLegalName(text: string): LegalNameCheck {
  const value = text.trim()
  if (value.length === 0) return { ok: false, message: LEGAL_NAME_TEXT.required }
  if (value.length > LEGAL_NAME_MAX) return { ok: false, message: LEGAL_NAME_TEXT.tooLong }
  return { ok: true, value }
}

export const LEGAL_NAME_TEXT = {
  heading: 'Resmi unvan',
  loading: 'Resmi unvan yükleniyor…',
  loadError: 'Resmi unvan okunamadı.',
  forbidden: 'Resmi unvanı görme yetkiniz yok.',
  notFound: 'Bu cari seçili şirkette kayıtlı değil.',
  readAgain: 'Yeniden dene',
  legalNameLabel: 'Resmi unvan',
  legalNameHint: `Zorunlu, en fazla ${LEGAL_NAME_MAX} karakter. Değişiklik tek adımda uygulanır; eski ve yeni unvan denetim kaydına yazılır.`,
  sharedNote: 'Resmi unvan cari kartın kendisine aittir; bu cariyi kullanan bütün şirketlerde aynı görünür.',
  save: 'Resmi unvanı kaydet',
  saving: 'Kaydediliyor…',
  saved: 'Resmi unvan güncellendi.',
  readOnlyNote: 'Resmi unvanı değiştirme yetkiniz yok; yalnız görüntüleyebilirsiniz.',
  required: 'Resmi unvan boş bırakılamaz.',
  tooLong: `Resmi unvan en fazla ${LEGAL_NAME_MAX} karakter olabilir.`,
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome:
    'İşlemin sonucu belirsiz: bağlantı kesildi veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden: 'Bu işlem için yetkiniz yok.',
  refusal: {
    [LEGAL_NAME_PROBLEM_CODE.versionConflict]:
      'Kayıt siz düzenlerken başka biri tarafından değiştirildi. Güncel bilgiler yüklendi; değişikliğinizi yeniden yapın.',
    [LEGAL_NAME_PROBLEM_CODE.notFound]: 'Kayıt bulunamadı veya bu şirkette kullanılamıyor.',
    [LEGAL_NAME_PROBLEM_CODE.idempotencyConflict]:
      'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [LEGAL_NAME_PROBLEM_CODE.validationFailed]: 'Girilen resmi unvan geçersiz.',
  } as Readonly<Record<string, string>>,
} as const
