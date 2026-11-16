/**
 * ALS-D111 + ALS-D113 — the Cari card's official legal name (resmi/hukuki unvan), changed by maker-checker.
 *
 * Mirrors, never invents: the shape is `CounterpartyLegalNameView` of the generated contract (operations
 * `GetCounterpartyLegalNameV1`, `ProposeCounterpartyLegalNameChangeV1`, `DecideCounterpartyLegalNameChangeV1`).
 *
 *  - The holder of the SEPARATE propose permission proposes a new legal name with a reason; the name does NOT change
 *    then. A DIFFERENT holder of the SEPARATE approve permission approves (one approval is enough) or rejects it. Only
 *    the approval writes the new name. The server measures both permissions for this actor and company, and whether
 *    this actor is the proposer of the pending request, and says so in `viewer`.
 *  - Every command is fenced on the PARTY version, the same version the trade name and the tax identity move.
 *  - The party is shared by every company holding an account for it, so an approved change is seen by all of them; the
 *    server records the old and the new legal name in the audit.
 *
 * The decoder is strict: an unknown shape is `null`, never a partly trusted object. Every user-facing word lives in
 * `LEGAL_NAME_TEXT` below.
 */

import { LEGAL_NAME_MAX, PARTY_CODE_MAX } from './tradeName'
import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'

export { LEGAL_NAME_MAX }

/** The server's limits for the proposal reason and the decision note (`PartyLegalNameChangeRequest`). */
export const LEGAL_NAME_REASON_MAX = 1000
export const LEGAL_NAME_NOTE_MAX = 1000
/** The server's limit for the recorded actor of a proposal or decision. */
export const LEGAL_NAME_ACTOR_MAX = 128

/** The endpoint's own stable codes (`CounterpartyProblemCodesV1`). */
export const LEGAL_NAME_PROBLEM_CODE = {
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
  unchanged: 'legal_name_unchanged',
  changePending: 'legal_name_change_pending',
  changeNotPending: 'legal_name_change_not_pending',
  changeStale: 'legal_name_change_stale',
  selfDecision: 'legal_name_self_decision',
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

export const LEGAL_NAME_CHANGE_STATUS = {
  pending: 'pending',
  approved: 'approved',
  rejected: 'rejected',
} as const

export type LegalNameChangeStatus = (typeof LEGAL_NAME_CHANGE_STATUS)[keyof typeof LEGAL_NAME_CHANGE_STATUS]

const STATUSES = new Set<string>(Object.values(LEGAL_NAME_CHANGE_STATUS))

export type LegalNameDecision = 'approve' | 'reject'

export interface LegalNameChange {
  readonly id: string
  readonly status: LegalNameChangeStatus
  readonly currentLegalName: string
  readonly proposedLegalName: string
  readonly reason: string
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionNote: string | null
}

export interface LegalNameViewer {
  readonly canPropose: boolean
  readonly canApprove: boolean
  readonly isRequesterOfPending: boolean
}

export interface LegalNameView {
  readonly partyId: string
  readonly companyId: string
  readonly code: string
  readonly version: string
  readonly legalName: string
  readonly pendingChange: LegalNameChange | null
  readonly lastDecidedChange: LegalNameChange | null
  readonly viewer: LegalNameViewer
}

const VIEW_KEYS = [
  'code',
  'companyId',
  'lastDecidedChange',
  'legalName',
  'partyId',
  'pendingChange',
  'version',
  'viewer',
] as const
const VIEWER_KEYS = ['canApprove', 'canPropose', 'isRequesterOfPending'] as const
const CHANGE_KEYS = [
  'currentLegalName',
  'decidedAtUtc',
  'decidedBy',
  'decisionNote',
  'id',
  'proposedLegalName',
  'reason',
  'requestedAtUtc',
  'requestedBy',
  'status',
] as const

const isObject = (raw: unknown): raw is Record<string, unknown> =>
  typeof raw === 'object' && raw !== null && !Array.isArray(raw)

function hasExactKeys(raw: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(raw).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}

/** An ISO-8601 instant the browser can read; the server always sends one with an offset. */
function isInstant(raw: unknown): raw is string {
  return typeof raw === 'string' && raw.length > 0 && raw.length <= 64 && !Number.isNaN(Date.parse(raw))
}

const isOptionalText = (raw: unknown, max: number): raw is string | null =>
  raw === null || isNonEmptyString(raw, max)

function decodeChange(raw: unknown): LegalNameChange | null {
  if (!isObject(raw) || !hasExactKeys(raw, CHANGE_KEYS)) return null
  if (
    !isBoundUuid(raw.id) ||
    typeof raw.status !== 'string' ||
    !STATUSES.has(raw.status) ||
    !isNonEmptyString(raw.currentLegalName, LEGAL_NAME_MAX) ||
    !isNonEmptyString(raw.proposedLegalName, LEGAL_NAME_MAX) ||
    raw.currentLegalName === raw.proposedLegalName ||
    !isNonEmptyString(raw.reason, LEGAL_NAME_REASON_MAX) ||
    !isNonEmptyString(raw.requestedBy, LEGAL_NAME_ACTOR_MAX) ||
    !isInstant(raw.requestedAtUtc) ||
    !isOptionalText(raw.decidedBy, LEGAL_NAME_ACTOR_MAX) ||
    !(raw.decidedAtUtc === null || isInstant(raw.decidedAtUtc)) ||
    !isOptionalText(raw.decisionNote, LEGAL_NAME_NOTE_MAX)
  ) {
    return null
  }
  const pending = raw.status === LEGAL_NAME_CHANGE_STATUS.pending
  if (
    (pending && (raw.decidedBy !== null || raw.decidedAtUtc !== null || raw.decisionNote !== null)) ||
    (!pending && (raw.decidedBy === null || raw.decidedAtUtc === null))
  ) {
    return null
  }
  return {
    id: raw.id,
    status: raw.status as LegalNameChangeStatus,
    currentLegalName: raw.currentLegalName,
    proposedLegalName: raw.proposedLegalName,
    reason: raw.reason,
    requestedBy: raw.requestedBy,
    requestedAtUtc: raw.requestedAtUtc,
    decidedBy: raw.decidedBy,
    decidedAtUtc: raw.decidedAtUtc,
    decisionNote: raw.decisionNote,
  }
}

/** The read must answer for the party and company the request named; another record is not this one. */
export function decodeLegalNameView(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): LegalNameView | null {
  if (!isObject(raw) || !hasExactKeys(raw, VIEW_KEYS)) return null
  if (!isObject(raw.viewer) || !hasExactKeys(raw.viewer, VIEWER_KEYS)) return null
  const pendingChange = raw.pendingChange === null ? null : decodeChange(raw.pendingChange)
  const lastDecidedChange = raw.lastDecidedChange === null ? null : decodeChange(raw.lastDecidedChange)
  const viewer = raw.viewer
  if (
    !isBoundUuid(raw.partyId) ||
    raw.partyId.toLowerCase() !== expected.partyId.toLowerCase() ||
    !isBoundUuid(raw.companyId) ||
    raw.companyId.toLowerCase() !== expected.companyId.toLowerCase() ||
    !isNonEmptyString(raw.code, PARTY_CODE_MAX) ||
    !isLosslessVersion(raw.version) ||
    !isNonEmptyString(raw.legalName, LEGAL_NAME_MAX) ||
    typeof viewer.canPropose !== 'boolean' ||
    typeof viewer.canApprove !== 'boolean' ||
    typeof viewer.isRequesterOfPending !== 'boolean' ||
    (raw.pendingChange !== null &&
      (pendingChange === null || pendingChange.status !== LEGAL_NAME_CHANGE_STATUS.pending)) ||
    (raw.lastDecidedChange !== null &&
      (lastDecidedChange === null || lastDecidedChange.status === LEGAL_NAME_CHANGE_STATUS.pending)) ||
    // "The proposer of the pending request" cannot be true when nothing is pending.
    (pendingChange === null && viewer.isRequesterOfPending)
  ) {
    return null
  }
  return {
    partyId: raw.partyId,
    companyId: raw.companyId,
    code: raw.code,
    version: raw.version,
    legalName: raw.legalName,
    pendingChange,
    lastDecidedChange,
    viewer: {
      canPropose: viewer.canPropose,
      canApprove: viewer.canApprove,
      isRequesterOfPending: viewer.isRequesterOfPending,
    },
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

/** The proposal reason: trimmed, required, at most 1000. */
export function checkLegalNameReason(text: string): LegalNameCheck {
  const value = text.trim()
  if (value.length === 0) return { ok: false, message: LEGAL_NAME_TEXT.reasonRequired }
  if (value.length > LEGAL_NAME_REASON_MAX) return { ok: false, message: LEGAL_NAME_TEXT.reasonTooLong }
  return { ok: true, value }
}

/** The decision note: optional; a blank note is "no note" (`null`), at most 1000. */
export function checkLegalNameNote(
  text: string,
): { readonly ok: true; readonly value: string | null } | { readonly ok: false; readonly message: string } {
  const value = text.trim()
  if (value.length === 0) return { ok: true, value: null }
  if (value.length > LEGAL_NAME_NOTE_MAX) return { ok: false, message: LEGAL_NAME_TEXT.noteTooLong }
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
  sharedNote: 'Resmi unvan cari kartın kendisine aittir; bu cariyi kullanan bütün şirketlerde aynı görünür.',
  makerCheckerNote:
    'Resmi unvan değişikliği iki adımlıdır: önerilen unvan, öneren dışında yetkili bir kişi onaylayana kadar uygulanmaz.',
  proposeHeading: 'Resmi unvan değişikliği öner',
  proposedLabel: 'Önerilen resmi unvan',
  proposedHint: `Zorunlu, en fazla ${LEGAL_NAME_MAX} karakter ve mevcut unvandan farklı olmalı.`,
  reasonLabel: 'Değişiklik gerekçesi',
  reasonHint: `Zorunlu, en fazla ${LEGAL_NAME_REASON_MAX} karakter.`,
  propose: 'Onaya gönder',
  proposing: 'Gönderiliyor…',
  proposed: 'Resmi unvan değişikliği onaya gönderildi. Unvan, onaylanana kadar değişmez.',
  pendingHeading: 'Onay bekleyen değişiklik',
  pendingCurrent: 'Mevcut unvan (öneri anında)',
  pendingProposed: 'Önerilen unvan',
  pendingReason: 'Gerekçe',
  pendingRequestedBy: 'Öneren',
  pendingRequestedAt: 'Öneri zamanı',
  pendingBlocksPropose: 'Onay bekleyen bir değişiklik var; o sonuçlanmadan yeni öneri yapılamaz.',
  ownPendingNote: 'Bu öneriyi siz yaptınız; kendi önerinizi onaylayamaz veya reddedemezsiniz.',
  waitingNote: 'Öneri, yetkili bir kişinin onayını bekliyor.',
  noteLabel: 'Karar notu (isteğe bağlı)',
  noteHint: `En fazla ${LEGAL_NAME_NOTE_MAX} karakter.`,
  approve: 'Onayla',
  reject: 'Reddet',
  deciding: 'Kaydediliyor…',
  approved: 'Resmi unvan değişikliği onaylandı ve uygulandı.',
  rejected: 'Resmi unvan değişikliği reddedildi; unvan değişmedi.',
  lastDecidedHeading: 'Son sonuçlanan değişiklik',
  statusLabel: {
    pending: 'Onay bekliyor',
    approved: 'Onaylandı',
    rejected: 'Reddedildi',
  } as Readonly<Record<LegalNameChangeStatus, string>>,
  decidedBy: 'Karar veren',
  decidedAt: 'Karar zamanı',
  decisionNote: 'Karar notu',
  readOnlyNote: 'Resmi unvan değişikliği önerme veya onaylama yetkiniz yok; yalnız görüntüleyebilirsiniz.',
  required: 'Önerilen resmi unvan boş bırakılamaz.',
  tooLong: `Resmi unvan en fazla ${LEGAL_NAME_MAX} karakter olabilir.`,
  sameAsCurrent: 'Önerilen unvan mevcut unvanla aynı.',
  reasonRequired: 'Değişiklik gerekçesi boş bırakılamaz.',
  reasonTooLong: `Gerekçe en fazla ${LEGAL_NAME_REASON_MAX} karakter olabilir.`,
  noteTooLong: `Karar notu en fazla ${LEGAL_NAME_NOTE_MAX} karakter olabilir.`,
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome:
    'İşlemin sonucu belirsiz: bağlantı kesildi veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden: 'Bu işlem için yetkiniz yok.',
  refusal: {
    [LEGAL_NAME_PROBLEM_CODE.versionConflict]:
      'Kayıt siz düzenlerken başka biri tarafından değiştirildi. Güncel bilgiler yüklendi; işleminizi yeniden yapın.',
    [LEGAL_NAME_PROBLEM_CODE.notFound]: 'Kayıt bulunamadı veya bu şirkette kullanılamıyor.',
    [LEGAL_NAME_PROBLEM_CODE.idempotencyConflict]:
      'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [LEGAL_NAME_PROBLEM_CODE.validationFailed]: 'Girilen bilgiler geçersiz.',
    [LEGAL_NAME_PROBLEM_CODE.unchanged]: 'Önerilen unvan mevcut unvanla aynı.',
    [LEGAL_NAME_PROBLEM_CODE.changePending]:
      'Bu cari için zaten onay bekleyen bir resmi unvan değişikliği var. Güncel bilgiler yüklendi.',
    [LEGAL_NAME_PROBLEM_CODE.changeNotPending]:
      'Bu değişiklik artık onay beklemiyor; başka biri sonuçlandırmış olabilir. Güncel bilgiler yüklendi.',
    [LEGAL_NAME_PROBLEM_CODE.changeStale]:
      'Öneri yapıldıktan sonra cari kart değişti; bu öneri artık onaylanamaz. Reddedip yeni bir öneri yapılabilir.',
    [LEGAL_NAME_PROBLEM_CODE.selfDecision]: 'Kendi önerinizi onaylayamaz veya reddedemezsiniz.',
  } as Readonly<Record<string, string>>,
} as const
