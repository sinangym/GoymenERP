/**
 * F-027 / EK-18 #76-#77 — duplicate Cari merge (mükerrer cari birleştirme), seen from the SURVIVING card.
 *
 * Mirrors, never invents: the shape is `CounterpartyPartyMergeView` of the generated contract (operations
 * `GetCounterpartyPartyMergeV1`, `ApproveCounterpartyPartyMergeV1`).
 *
 *  - The card the user is on is the survivor. The server lists the duplicates the selected company holds with the SAME
 *    VKN/TCKN (each with its own party version as text); another VKN is never offered and the server refuses it.
 *  - The merge is the group manager's approval (`counterparty.parties.merge.approve`); the server measures it for this
 *    actor and company and says so in `viewer`, and requires it again in every company holding a card of either party.
 *  - Both party versions are fenced. The duplicate becomes passive for good and points at the survivor; every company
 *    holding either card is notified. There is NO unmerge (#77): a wrong merge is corrected forward with a new card.
 *  - A card that was merged away shows its target instead (with the target's code only when this company holds it).
 *
 * The decoder is strict: an unknown shape is `null`, never a partly trusted object. Every user-facing word lives in
 * `MERGE_TEXT` below.
 */

import { LEGAL_NAME_MAX, PARTY_CODE_MAX } from './tradeName'
import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'

/** The server's reason limit (`PartyMerge.ReasonMaximumLength`). */
export const MERGE_REASON_MAX = 1000

/** The server returns at most this many candidates (`DuplicateLimit`). */
export const MERGE_CANDIDATE_LIMIT = 20

/** The endpoint's own stable codes (`CounterpartyProblemCodesV1`). */
export const MERGE_PROBLEM_CODE = {
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  alreadyMerged: 'party_already_merged',
  targetMerged: 'party_merge_target_merged',
  taxIdentityMismatch: 'party_merge_tax_identity_mismatch',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
} as const

export type MergeProblemCode = (typeof MERGE_PROBLEM_CODE)[keyof typeof MERGE_PROBLEM_CODE]

const KNOWN_CODES = new Set<string>(Object.values(MERGE_PROBLEM_CODE))

export function mergeProblemCodeOf(raw: unknown): MergeProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_CODES.has(code) ? (code as MergeProblemCode) : null
}

export function mergePath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/merge`
}

export interface MergeCandidate {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly version: string
}

export interface MergeTarget {
  readonly partyId: string
  /** `null` when the selected company holds no card of the survivor. */
  readonly code: string | null
}

export interface MergeView {
  readonly partyId: string
  readonly companyId: string
  readonly code: string
  readonly version: string
  readonly legalName: string
  readonly mergedInto: MergeTarget | null
  readonly candidates: readonly MergeCandidate[]
  readonly viewer: { readonly canApproveMerge: boolean }
}

const VIEW_KEYS = ['candidates', 'code', 'companyId', 'legalName', 'mergedInto', 'partyId', 'version', 'viewer'] as const
const VIEWER_KEYS = ['canApproveMerge'] as const
const TARGET_KEYS = ['code', 'partyId'] as const
const CANDIDATE_KEYS = ['code', 'legalName', 'partyId', 'version'] as const

const isObject = (raw: unknown): raw is Record<string, unknown> =>
  typeof raw === 'object' && raw !== null && !Array.isArray(raw)

function hasExactKeys(raw: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(raw).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}

function decodeCandidate(raw: unknown, survivorId: string): MergeCandidate | null {
  if (!isObject(raw) || !hasExactKeys(raw, CANDIDATE_KEYS)) return null
  if (
    !isBoundUuid(raw.partyId) ||
    raw.partyId.toLowerCase() === survivorId.toLowerCase() ||
    !isNonEmptyString(raw.code, PARTY_CODE_MAX) ||
    !isNonEmptyString(raw.legalName, LEGAL_NAME_MAX) ||
    !isLosslessVersion(raw.version)
  ) {
    return null
  }
  return { partyId: raw.partyId, code: raw.code, legalName: raw.legalName, version: raw.version }
}

function decodeTarget(raw: unknown, partyId: string): MergeTarget | null | undefined {
  if (raw === null) return null
  if (!isObject(raw) || !hasExactKeys(raw, TARGET_KEYS)) return undefined
  if (!isBoundUuid(raw.partyId) || raw.partyId.toLowerCase() === partyId.toLowerCase()) return undefined
  if (raw.code !== null && !isNonEmptyString(raw.code, PARTY_CODE_MAX)) return undefined
  return { partyId: raw.partyId, code: raw.code }
}

/** The read must answer for the party and company the request named; another record is not this one. */
export function decodeMergeView(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): MergeView | null {
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
    typeof raw.viewer.canApproveMerge !== 'boolean' ||
    !Array.isArray(raw.candidates) ||
    raw.candidates.length > MERGE_CANDIDATE_LIMIT
  ) {
    return null
  }
  const partyId = raw.partyId
  const mergedInto = decodeTarget(raw.mergedInto, partyId)
  if (mergedInto === undefined) return null
  const candidates: MergeCandidate[] = []
  const seen = new Set<string>()
  for (const item of raw.candidates as unknown[]) {
    const candidate = decodeCandidate(item, partyId)
    if (candidate === null || seen.has(candidate.partyId.toLowerCase())) return null
    seen.add(candidate.partyId.toLowerCase())
    candidates.push(candidate)
  }
  // A merged card has no candidates and cannot be approved as a survivor; a body claiming otherwise is not trusted.
  if (mergedInto !== null && (candidates.length > 0 || raw.viewer.canApproveMerge)) return null
  return {
    partyId,
    companyId: raw.companyId,
    code: raw.code,
    version: raw.version,
    legalName: raw.legalName,
    mergedInto,
    candidates,
    viewer: { canApproveMerge: raw.viewer.canApproveMerge },
  }
}

export type MergeReasonCheck =
  | { readonly ok: true; readonly value: string }
  | { readonly ok: false; readonly message: string }

/** Mirrors the server's normalization (trim; required; at most 1000); the server decides again. */
export function checkMergeReason(text: string): MergeReasonCheck {
  const value = text.trim()
  if (value.length === 0) return { ok: false, message: MERGE_TEXT.reasonRequired }
  if (value.length > MERGE_REASON_MAX) return { ok: false, message: MERGE_TEXT.reasonTooLong }
  return { ok: true, value }
}

export const MERGE_TEXT = {
  heading: 'Mükerrer cari birleştirme',
  loading: 'Birleştirme bilgisi yükleniyor…',
  loadError: 'Birleştirme bilgisi okunamadı.',
  forbidden: 'Birleştirme bilgisini görme yetkiniz yok.',
  notFound: 'Bu cari seçili şirkette kayıtlı değil.',
  readAgain: 'Yeniden dene',
  mergedInto: 'Bu cari başka bir cariyle birleştirilmiştir ve pasiftir.',
  mergedIntoCode: 'Birleştirildiği cari:',
  mergedIntoHidden: 'Birleştirildiği cari seçili şirkette kayıtlı değil.',
  noCandidates: 'Seçili şirkette bu cariyle aynı VKN/TCKN’ye sahip başka cari yok.',
  candidatesIntro:
    'Seçili şirkette aynı VKN/TCKN ile kayıtlı mükerrer cariler aşağıdadır. Seçtiğiniz cari bu kartla birleştirilir.',
  candidateLabel: 'Birleştirilecek mükerrer cari',
  candidatePlaceholder: 'Cari seçin',
  reasonLabel: 'Birleştirme gerekçesi',
  reasonHint: `Zorunlu, en fazla ${MERGE_REASON_MAX} karakter. Gerekçe denetim kaydına yazılır.`,
  warning:
    'Birleştirme geri alınamaz: mükerrer cari kalıcı olarak pasifleşir ve bu karta yönlendirilir. Hatalı birleştirme yeni cari açılarak ileriye doğru düzeltilir. Bu cariyi kullanan bütün şirketlere bildirim gider.',
  confirmLabel: 'Birleştirmenin geri alınamayacağını anladım.',
  confirmRequired: 'Devam etmek için birleştirmenin geri alınamayacağını onaylayın.',
  candidateRequired: 'Birleştirilecek cariyi seçin.',
  approve: 'Birleştirmeyi onayla',
  approving: 'Onaylanıyor…',
  approved: 'Cari birleştirildi.',
  readOnlyNote: 'Cari birleştirme onayı yetkiniz yok; yalnız görüntüleyebilirsiniz.',
  reasonRequired: 'Birleştirme gerekçesi boş bırakılamaz.',
  reasonTooLong: `Birleştirme gerekçesi en fazla ${MERGE_REASON_MAX} karakter olabilir.`,
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome:
    'İşlemin sonucu belirsiz: bağlantı kesildi veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden:
    'Bu işlem için yetkiniz yok. Birleştirme onayı, iki cariyi kullanan her şirkette yetki gerektirir.',
  refusal: {
    [MERGE_PROBLEM_CODE.versionConflict]:
      'Carilerden biri siz incelerken değiştirildi. Güncel bilgiler yüklendi; seçiminizi yeniden yapın.',
    [MERGE_PROBLEM_CODE.notFound]: 'Cari bulunamadı veya bu şirkette kullanılamıyor.',
    [MERGE_PROBLEM_CODE.idempotencyConflict]: 'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [MERGE_PROBLEM_CODE.alreadyMerged]: 'Seçilen cari daha önce başka bir cariyle birleştirilmiş.',
    [MERGE_PROBLEM_CODE.targetMerged]: 'Bu cari başka bir cariyle birleştirilmiş; birleştirme hedefi olamaz.',
    [MERGE_PROBLEM_CODE.taxIdentityMismatch]: 'VKN/TCKN’si farklı cariler birleştirilemez.',
    [MERGE_PROBLEM_CODE.validationFailed]: 'Birleştirme isteği geçersiz.',
  } as Readonly<Record<string, string>>,
} as const
