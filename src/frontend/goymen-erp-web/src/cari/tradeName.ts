/**
 * F-027 / C-02 — the Cari card's names (the direct part of the card edit).
 *
 * Mirrors, never invents: the shape is `CounterpartyTradeNameView` of the generated contract (operations
 * `GetCounterpartyTradeNameV1`, `UpdateCounterpartyTradeNameV1`).
 *
 *  - The TRADE name (ticari ad) is a basic card field: it is changed directly, fenced on the PARTY version.
 *  - The official LEGAL name (resmi unvan) is shown read-only here and never sent from this screen: it is changed on its
 *    own panel by maker-checker under separate permissions (ALS-D111/ALS-D113, ./legalName.ts).
 *  - The party is shared by every company holding an account for it, so a trade name change is seen by all of them.
 *
 * The decoder is strict: an unknown shape is `null`, never a partly trusted object. Every user-facing word lives in
 * `TRADE_NAME_TEXT` below.
 */

import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'

/** The server's limits (`Party.TradeNameMaximumLength`, `Party.LegalNameMaximumLength`). */
export const TRADE_NAME_MAX = 200
export const LEGAL_NAME_MAX = 250
/** The same bound the Cari detail decoder applies to the party code. */
export const PARTY_CODE_MAX = 64

/** The endpoint's own stable codes (`CounterpartyProblemCodesV1`). */
export const TRADE_NAME_PROBLEM_CODE = {
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
} as const

export type TradeNameProblemCode = (typeof TRADE_NAME_PROBLEM_CODE)[keyof typeof TRADE_NAME_PROBLEM_CODE]

const KNOWN_CODES = new Set<string>(Object.values(TRADE_NAME_PROBLEM_CODE))

export function tradeNameProblemCodeOf(raw: unknown): TradeNameProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_CODES.has(code) ? (code as TradeNameProblemCode) : null
}

export function tradeNamePath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/trade-name`
}

export interface TradeNameView {
  readonly partyId: string
  readonly companyId: string
  readonly code: string
  readonly version: string
  readonly legalName: string
  readonly tradeName: string | null
  readonly viewer: { readonly canUpdateTradeName: boolean }
}

const VIEW_KEYS = ['code', 'companyId', 'legalName', 'partyId', 'tradeName', 'version', 'viewer'] as const
const VIEWER_KEYS = ['canUpdateTradeName'] as const

const isObject = (raw: unknown): raw is Record<string, unknown> =>
  typeof raw === 'object' && raw !== null && !Array.isArray(raw)

function hasExactKeys(raw: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(raw).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}

/** The read must answer for the party and company the request named; another record is not this one. */
export function decodeTradeNameView(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): TradeNameView | null {
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
    !(raw.tradeName === null || isNonEmptyString(raw.tradeName, TRADE_NAME_MAX)) ||
    typeof raw.viewer.canUpdateTradeName !== 'boolean'
  ) {
    return null
  }
  return {
    partyId: raw.partyId,
    companyId: raw.companyId,
    code: raw.code,
    version: raw.version,
    legalName: raw.legalName,
    tradeName: raw.tradeName,
    viewer: { canUpdateTradeName: raw.viewer.canUpdateTradeName },
  }
}

export type TradeNameCheck =
  | { readonly ok: true; readonly value: string | null }
  | { readonly ok: false; readonly message: string }

/** Mirrors the server's normalization (trim; blank clears; at most 200); the server decides again. */
export function checkTradeName(text: string): TradeNameCheck {
  const value = text.trim()
  if (value.length === 0) return { ok: true, value: null }
  if (value.length > TRADE_NAME_MAX) return { ok: false, message: TRADE_NAME_TEXT.tooLong }
  return { ok: true, value }
}

export const TRADE_NAME_TEXT = {
  heading: 'Cari adı',
  loading: 'Cari adı yükleniyor…',
  loadError: 'Cari adı okunamadı.',
  forbidden: 'Cari adını görme yetkiniz yok.',
  notFound: 'Bu cari seçili şirkette kayıtlı değil.',
  readAgain: 'Yeniden dene',
  legalNameLabel: 'Hukuki unvan',
  legalNameNote: 'Hukuki unvan ayrı yetkiyle, "Resmi unvan" bölümünden değiştirilir.',
  tradeNameLabel: 'Ticari unvan',
  tradeNameNone: 'Ticari unvan girilmemiş',
  tradeNameHint: `İsteğe bağlı, en fazla ${TRADE_NAME_MAX} karakter. Boş bırakılırsa ticari unvan kaldırılır.`,
  sharedNote: 'Ticari unvan cari kartın kendisine aittir; bu cariyi kullanan bütün şirketlerde aynı görünür.',
  save: 'Ticari unvanı kaydet',
  saving: 'Kaydediliyor…',
  saved: 'Ticari unvan güncellendi.',
  cleared: 'Ticari unvan kaldırıldı.',
  readOnlyNote: 'Ticari unvanı değiştirme yetkiniz yok; yalnız görüntüleyebilirsiniz.',
  tooLong: `Ticari unvan en fazla ${TRADE_NAME_MAX} karakter olabilir.`,
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome:
    'İşlemin sonucu belirsiz: bağlantı kesildi veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden: 'Bu işlem için yetkiniz yok.',
  refusal: {
    [TRADE_NAME_PROBLEM_CODE.versionConflict]:
      'Kayıt siz düzenlerken başka biri tarafından değiştirildi. Güncel bilgiler yüklendi; değişikliğinizi yeniden yapın.',
    [TRADE_NAME_PROBLEM_CODE.notFound]: 'Kayıt bulunamadı veya bu şirkette kullanılamıyor.',
    [TRADE_NAME_PROBLEM_CODE.idempotencyConflict]:
      'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [TRADE_NAME_PROBLEM_CODE.validationFailed]: 'Girilen ticari unvan geçersiz.',
  } as Readonly<Record<string, string>>,
} as const
