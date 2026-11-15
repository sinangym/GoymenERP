/**
 * F-027 / KLN-D38 — the Cari card's dated identity history and the succession of a new legal person.
 *
 * Mirrors, never invents: the shape is `CounterpartyIdentityHistoryView` of the generated contract (operations
 * `GetCounterpartyIdentityHistoryV1`, `RecordCounterpartyPartySuccessionV1`).
 *
 *  - VKN/TCKN, the official legal name and the e-belge status are kept DATED. Until a party's first dated change the
 *    history is empty and the current values apply "since always"; the screen says so instead of inventing rows.
 *  - A wrong VKN/TCKN stays on the SAME cari and is fixed by the approved change request (the tax identity panel); this
 *    panel only SHOWS the resulting correction with its reason.
 *  - A NEW legal person (sole proprietorship → company, merger, death) gets a NEW cari. Recording the succession makes
 *    THIS cari's account in the selected company passive and links it to the successor. It moves NO balance: the balance
 *    moves only by an approved transfer voucher (devir fişi) in Finance, which keeps the legal basis and evidence.
 *  - The record is fenced on THIS cari's company-account version; the server measures whether the reader may record it.
 *
 * The decoder is strict: an unknown shape is `null`, never a partly trusted object. Every user-facing word lives in
 * `IDENTITY_HISTORY_TEXT` below.
 */

import { PARTY_KIND, type PartyKind } from './contract'
import { E_DOCUMENT_STATUSES, type EDocumentStatus } from './eDocument'
import { LEGAL_NAME_MAX, PARTY_CODE_MAX } from './tradeName'
import { isBoundUuid, isLosslessVersion, isNonEmptyString } from './wireGuards'

/** The server's trimmed limit for the legal basis (`PartySuccession.LegalBasisMaximumLength`). */
export const LEGAL_BASIS_MAX = 1000
/** The longest stored identity value: a legal name; a VKN/TCKN is 10 or 11 digits. */
const IDENTITY_VALUE_MAX = LEGAL_NAME_MAX
/** The longest reason a tax-identity change request may carry. */
const REASON_MAX = 1000

/** The endpoint's own stable codes (`CounterpartyProblemCodesV1`). */
export const IDENTITY_HISTORY_PROBLEM_CODE = {
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
  successionSameParty: 'succession_same_party',
  successionSameTaxIdentity: 'succession_same_tax_identity',
  successionSuccessorInactive: 'succession_successor_inactive',
  successionCycle: 'succession_cycle',
  successionAlreadyRecorded: 'succession_already_recorded',
} as const

export type IdentityHistoryProblemCode =
  (typeof IDENTITY_HISTORY_PROBLEM_CODE)[keyof typeof IDENTITY_HISTORY_PROBLEM_CODE]

const KNOWN_CODES = new Set<string>(Object.values(IDENTITY_HISTORY_PROBLEM_CODE))

export function identityHistoryProblemCodeOf(raw: unknown): IdentityHistoryProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_CODES.has(code) ? (code as IdentityHistoryProblemCode) : null
}

export function identityHistoryPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/identity-history`
}

export function successionPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/succession`
}

export const IDENTITY_ATTRIBUTES = ['TaxIdentityNumber', 'LegalName'] as const
export type IdentityAttribute = (typeof IDENTITY_ATTRIBUTES)[number]

export const IDENTITY_REVISION_KINDS = ['Baseline', 'InitialEntry', 'Correction', 'DatedChange'] as const
export type IdentityRevisionKind = (typeof IDENTITY_REVISION_KINDS)[number]

export const SUCCESSION_KINDS = ['Conversion', 'Merger', 'Death'] as const
export type SuccessionKind = (typeof SUCCESSION_KINDS)[number]

export interface IdentityRevision {
  readonly attribute: IdentityAttribute
  readonly kind: IdentityRevisionKind
  readonly sequence: string
  readonly value: string
  /** `null` = "since always". */
  readonly effectiveFrom: string | null
  /** Shown only to the company that recorded the revision; otherwise `null`. */
  readonly reason: string | null
  readonly recordedInActiveCompany: boolean
  readonly recordedAtUtc: string
}

export interface EDocumentStatusRevision {
  readonly sequence: string
  readonly status: EDocumentStatus
  readonly effectiveFrom: string
  readonly recordedInActiveCompany: boolean
  readonly recordedAtUtc: string
}

export interface SuccessionLink {
  readonly predecessorPartyId: string
  readonly successorPartyId: string
  readonly otherPartyId: string
  readonly otherPartyCode: string
  readonly otherPartyLegalName: string
  readonly kind: SuccessionKind
  readonly legalBasis: string
  readonly effectiveOn: string
  readonly recordedAtUtc: string
}

export interface IdentityHistoryView {
  readonly partyId: string
  readonly companyId: string
  readonly code: string
  readonly kind: PartyKind
  readonly accountVersion: string
  readonly accountIsActive: boolean
  readonly taxIdentityNumber: string | null
  readonly legalName: string
  readonly revisions: readonly IdentityRevision[]
  readonly eDocumentStatusRevisions: readonly EDocumentStatusRevision[]
  readonly successor: SuccessionLink | null
  readonly predecessors: readonly SuccessionLink[]
  readonly viewer: { readonly canRecordSuccession: boolean }
}

const VIEW_KEYS = [
  'accountIsActive',
  'accountVersion',
  'code',
  'companyId',
  'eDocumentStatusRevisions',
  'kind',
  'legalName',
  'partyId',
  'predecessors',
  'revisions',
  'successor',
  'taxIdentityNumber',
  'viewer',
] as const
const VIEWER_KEYS = ['canRecordSuccession'] as const
const REVISION_KEYS = [
  'attribute',
  'effectiveFrom',
  'kind',
  'reason',
  'recordedAtUtc',
  'recordedInActiveCompany',
  'sequence',
  'value',
] as const
const STATUS_REVISION_KEYS = ['effectiveFrom', 'recordedAtUtc', 'recordedInActiveCompany', 'sequence', 'status'] as const
const LINK_KEYS = [
  'effectiveOn',
  'kind',
  'legalBasis',
  'otherPartyCode',
  'otherPartyId',
  'otherPartyLegalName',
  'predecessorPartyId',
  'recordedAtUtc',
  'successorPartyId',
] as const

const BUSINESS_DATE_RE = /^\d{4}-\d{2}-\d{2}$/
const TAX_IDENTITY_RE = /^\d{10,11}$/

const isObject = (raw: unknown): raw is Record<string, unknown> =>
  typeof raw === 'object' && raw !== null && !Array.isArray(raw)

function hasExactKeys(raw: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(raw).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}

const oneOf = <T extends string>(values: readonly T[], raw: unknown): raw is T =>
  typeof raw === 'string' && (values as readonly string[]).includes(raw)

/** A calendar date `yyyy-MM-dd` that exists (2026-02-30 does not). */
export function isBusinessDate(raw: unknown): raw is string {
  if (typeof raw !== 'string' || !BUSINESS_DATE_RE.test(raw)) return false
  const [year, month, day] = raw.split('-').map(Number)
  const date = new Date(Date.UTC(year, month - 1, day))
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
}

const isInstant = (raw: unknown): raw is string => typeof raw === 'string' && !Number.isNaN(Date.parse(raw))

function decodeRevision(raw: unknown): IdentityRevision | null {
  if (!isObject(raw) || !hasExactKeys(raw, REVISION_KEYS)) return null
  if (
    !oneOf(IDENTITY_ATTRIBUTES, raw.attribute) ||
    !oneOf(IDENTITY_REVISION_KINDS, raw.kind) ||
    !isLosslessVersion(raw.sequence) ||
    !isNonEmptyString(raw.value, IDENTITY_VALUE_MAX) ||
    (raw.attribute === 'TaxIdentityNumber' && !TAX_IDENTITY_RE.test(raw.value)) ||
    !(raw.effectiveFrom === null || isBusinessDate(raw.effectiveFrom)) ||
    !(raw.reason === null || isNonEmptyString(raw.reason, REASON_MAX)) ||
    typeof raw.recordedInActiveCompany !== 'boolean' ||
    !isInstant(raw.recordedAtUtc)
  ) {
    return null
  }
  // "Since always" is exactly the baseline and the first entry; a correction or a dated change carries its day.
  const undated = raw.kind === 'Baseline' || raw.kind === 'InitialEntry'
  if (undated !== (raw.effectiveFrom === null)) return null
  // The reason belongs to the deciding company only; a revision recorded elsewhere never shows one.
  if (!raw.recordedInActiveCompany && raw.reason !== null) return null
  return {
    attribute: raw.attribute,
    kind: raw.kind,
    sequence: raw.sequence,
    value: raw.value,
    effectiveFrom: raw.effectiveFrom,
    reason: raw.reason,
    recordedInActiveCompany: raw.recordedInActiveCompany,
    recordedAtUtc: raw.recordedAtUtc,
  }
}

function decodeStatusRevision(raw: unknown): EDocumentStatusRevision | null {
  if (!isObject(raw) || !hasExactKeys(raw, STATUS_REVISION_KEYS)) return null
  if (
    !isLosslessVersion(raw.sequence) ||
    !oneOf(E_DOCUMENT_STATUSES, raw.status) ||
    !isBusinessDate(raw.effectiveFrom) ||
    typeof raw.recordedInActiveCompany !== 'boolean' ||
    !isInstant(raw.recordedAtUtc)
  ) {
    return null
  }
  return {
    sequence: raw.sequence,
    status: raw.status,
    effectiveFrom: raw.effectiveFrom,
    recordedInActiveCompany: raw.recordedInActiveCompany,
    recordedAtUtc: raw.recordedAtUtc,
  }
}

function decodeLink(raw: unknown, partyId: string, side: 'successor' | 'predecessor'): SuccessionLink | null {
  if (!isObject(raw) || !hasExactKeys(raw, LINK_KEYS)) return null
  if (
    !isBoundUuid(raw.predecessorPartyId) ||
    !isBoundUuid(raw.successorPartyId) ||
    !isBoundUuid(raw.otherPartyId) ||
    !isNonEmptyString(raw.otherPartyCode, PARTY_CODE_MAX) ||
    !isNonEmptyString(raw.otherPartyLegalName, LEGAL_NAME_MAX) ||
    !oneOf(SUCCESSION_KINDS, raw.kind) ||
    !isNonEmptyString(raw.legalBasis, LEGAL_BASIS_MAX) ||
    !isBusinessDate(raw.effectiveOn) ||
    !isInstant(raw.recordedAtUtc)
  ) {
    return null
  }
  const self = partyId.toLowerCase()
  const predecessor = raw.predecessorPartyId.toLowerCase()
  const successor = raw.successorPartyId.toLowerCase()
  const other = raw.otherPartyId.toLowerCase()
  // THIS cari is on the named side of the link and the "other" cari is the opposite end; never a self-link.
  if (predecessor === successor) return null
  if (side === 'successor' && (predecessor !== self || other !== successor)) return null
  if (side === 'predecessor' && (successor !== self || other !== predecessor)) return null
  return {
    predecessorPartyId: raw.predecessorPartyId,
    successorPartyId: raw.successorPartyId,
    otherPartyId: raw.otherPartyId,
    otherPartyCode: raw.otherPartyCode,
    otherPartyLegalName: raw.otherPartyLegalName,
    kind: raw.kind,
    legalBasis: raw.legalBasis,
    effectiveOn: raw.effectiveOn,
    recordedAtUtc: raw.recordedAtUtc,
  }
}

function decodeAll<T>(raw: unknown, decode: (item: unknown) => T | null): T[] | null {
  if (!Array.isArray(raw)) return null
  const out: T[] = []
  for (const item of raw) {
    const decoded = decode(item)
    if (decoded === null) return null
    out.push(decoded)
  }
  return out
}

/** The read must answer for the party and company the request named; another record is not this one. */
export function decodeIdentityHistoryView(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): IdentityHistoryView | null {
  if (!isObject(raw) || !hasExactKeys(raw, VIEW_KEYS)) return null
  if (!isObject(raw.viewer) || !hasExactKeys(raw.viewer, VIEWER_KEYS)) return null
  if (
    !isBoundUuid(raw.partyId) ||
    raw.partyId.toLowerCase() !== expected.partyId.toLowerCase() ||
    !isBoundUuid(raw.companyId) ||
    raw.companyId.toLowerCase() !== expected.companyId.toLowerCase() ||
    !isNonEmptyString(raw.code, PARTY_CODE_MAX) ||
    (raw.kind !== PARTY_KIND.LegalEntity && raw.kind !== PARTY_KIND.NaturalPerson) ||
    !isLosslessVersion(raw.accountVersion) ||
    typeof raw.accountIsActive !== 'boolean' ||
    !(raw.taxIdentityNumber === null || (typeof raw.taxIdentityNumber === 'string' && TAX_IDENTITY_RE.test(raw.taxIdentityNumber))) ||
    !isNonEmptyString(raw.legalName, LEGAL_NAME_MAX) ||
    typeof raw.viewer.canRecordSuccession !== 'boolean'
  ) {
    return null
  }
  const partyId = raw.partyId
  const revisions = decodeAll(raw.revisions, decodeRevision)
  const statusRevisions = decodeAll(raw.eDocumentStatusRevisions, decodeStatusRevision)
  const predecessors = decodeAll(raw.predecessors, (item) => decodeLink(item, partyId, 'predecessor'))
  const successor = raw.successor === null ? null : decodeLink(raw.successor, partyId, 'successor')
  if (revisions === null || statusRevisions === null || predecessors === null) return null
  if (raw.successor !== null && successor === null) return null
  return {
    partyId,
    companyId: raw.companyId,
    code: raw.code,
    kind: raw.kind,
    accountVersion: raw.accountVersion,
    accountIsActive: raw.accountIsActive,
    taxIdentityNumber: raw.taxIdentityNumber,
    legalName: raw.legalName,
    revisions,
    eDocumentStatusRevisions: statusRevisions,
    successor,
    predecessors,
    viewer: { canRecordSuccession: raw.viewer.canRecordSuccession },
  }
}

export interface SuccessionDraft {
  readonly successorPartyId: string
  readonly kind: SuccessionKind | ''
  readonly legalBasis: string
  readonly effectiveOn: string
}

export interface SuccessionPayload {
  readonly successorPartyId: string
  readonly kind: SuccessionKind
  readonly legalBasis: string
  readonly effectiveOn: string
}

export type SuccessionCheck =
  | { readonly ok: true; readonly value: SuccessionPayload }
  | { readonly ok: false; readonly field: keyof SuccessionDraft; readonly message: string }

/**
 * Mirrors the server's checks that need no data (a successor id that is a cari id and not this one, a known reason,
 * a trimmed legal basis of at most 1000 characters, a real date); the server decides again, including "not later
 * than today's business day".
 */
export function checkSuccession(draft: SuccessionDraft, partyId: string): SuccessionCheck {
  const successorPartyId = draft.successorPartyId.trim()
  if (!isBoundUuid(successorPartyId)) {
    return { ok: false, field: 'successorPartyId', message: IDENTITY_HISTORY_TEXT.successorRequired }
  }
  if (successorPartyId.toLowerCase() === partyId.toLowerCase()) {
    return { ok: false, field: 'successorPartyId', message: IDENTITY_HISTORY_TEXT.successorSelf }
  }
  if (draft.kind === '') return { ok: false, field: 'kind', message: IDENTITY_HISTORY_TEXT.kindRequired }
  const legalBasis = draft.legalBasis.trim()
  if (legalBasis.length === 0) {
    return { ok: false, field: 'legalBasis', message: IDENTITY_HISTORY_TEXT.legalBasisRequired }
  }
  if (legalBasis.length > LEGAL_BASIS_MAX) {
    return { ok: false, field: 'legalBasis', message: IDENTITY_HISTORY_TEXT.legalBasisTooLong }
  }
  if (!isBusinessDate(draft.effectiveOn)) {
    return { ok: false, field: 'effectiveOn', message: IDENTITY_HISTORY_TEXT.effectiveOnRequired }
  }
  return { ok: true, value: { successorPartyId, kind: draft.kind, legalBasis, effectiveOn: draft.effectiveOn } }
}

export const IDENTITY_HISTORY_TEXT = {
  heading: 'Kimlik geçmişi ve halef',
  loading: 'Kimlik geçmişi yükleniyor…',
  loadError: 'Kimlik geçmişi okunamadı.',
  forbidden: 'Kimlik geçmişini görme yetkiniz yok.',
  notFound: 'Bu cari seçili şirkette kayıtlı değil.',
  readAgain: 'Yeniden dene',
  identityHeading: 'Tarihli kimlik',
  noRevisions:
    'Henüz tarihli bir kimlik değişikliği yok; kayıttaki VKN/TCKN ve resmi unvan cari açıldığından beri geçerlidir.',
  statusHeading: 'Tarihli e-belge durumu',
  noStatusRevisions: 'Bu VKN için henüz tarihli bir e-belge durumu kaydı yok.',
  sinceAlways: 'Başlangıçtan beri',
  recordedElsewhere: 'Başka bir şirkette kaydedildi',
  reasonLabel: 'Gerekçe',
  attribute: {
    TaxIdentityNumber: 'VKN/TCKN',
    LegalName: 'Resmi unvan',
  } as Readonly<Record<IdentityAttribute, string>>,
  revisionKind: {
    Baseline: 'İlk kayıtlı değer',
    InitialEntry: 'İlk giriş',
    Correction: 'Onaylı düzeltme',
    DatedChange: 'Tarihli değişiklik',
  } as Readonly<Record<IdentityRevisionKind, string>>,
  status: {
    Unknown: 'Kontrol edilmedi',
    EInvoice: 'e-Fatura mükellefi',
    EArchive: 'e-Arşiv',
  } as Readonly<Record<EDocumentStatus, string>>,
  successionKind: {
    Conversion: 'Şahıs işletmesinden şirkete dönüşüm',
    Merger: 'Birleşme',
    Death: 'Ölüm',
  } as Readonly<Record<SuccessionKind, string>>,
  successorHeading: 'Halef cari',
  successorNote:
    'Bu carinin yerine yeni bir tüzel/gerçek kişi geçti; bu şirketteki hesabı pasiftir. Bakiye bu kayıtla taşınmaz; yalnız onaylı devir fişiyle Finans tarafında taşınır.',
  predecessorsHeading: 'Önceki cariler',
  effectiveOnLabel: 'Geçerlilik tarihi',
  legalBasisLabel: 'Hukuki dayanak',
  successionFormHeading: 'Yeni tüzel/gerçek kişiye halef kaydı',
  successionFormNote:
    'Yalnız yeni bir tüzel/gerçek kişi (şahıstan şirkete geçiş, birleşme, ölüm) için kullanın. Yanlış girilmiş VKN/TCKN aynı caride onaylı düzeltmeyle değiştirilir; aynı VKN ile tür değişikliği aynı caride kalır.',
  balanceNote:
    'Bu kayıt bakiye taşımaz. Bakiye yalnız Finans’ta onaylı devir fişiyle, hukuki dayanak ve kanıtla birlikte taşınır.',
  successorIdLabel: 'Halef cari kimliği',
  successorIdHint: 'Halef carinin kartındaki kimlik. Halef cari bu şirkette aktif olmalı ve VKN/TCKN’si farklı olmalıdır.',
  kindLabel: 'Halef nedeni',
  kindPlaceholder: 'Seçin',
  legalBasisHint: `Zorunlu, en fazla ${LEGAL_BASIS_MAX} karakter (ör. ticaret sicili ilanı, veraset ilamı).`,
  effectiveOnHint: 'Yeni kişinin devraldığı iş günü; bugünden sonra olamaz.',
  submit: 'Halef kaydını yap ve bu cariyi pasife al',
  submitting: 'Kaydediliyor…',
  saved: 'Halef kaydı yapıldı; bu carinin hesabı pasife alındı.',
  readOnlyNote: 'Halef kaydı yapma yetkiniz yok; yalnız görüntüleyebilirsiniz.',
  successorRequired: 'Halef carinin kimliğini girin.',
  successorSelf: 'Bir cari kendisinin halefi olamaz.',
  kindRequired: 'Halef nedenini seçin.',
  legalBasisRequired: 'Hukuki dayanak boş bırakılamaz.',
  legalBasisTooLong: `Hukuki dayanak en fazla ${LEGAL_BASIS_MAX} karakter olabilir.`,
  effectiveOnRequired: 'Geçerli bir geçerlilik tarihi girin.',
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome:
    'İşlemin sonucu belirsiz: bağlantı kesildi veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden: 'Bu işlem için yetkiniz yok.',
  refusal: {
    [IDENTITY_HISTORY_PROBLEM_CODE.versionConflict]:
      'Cari hesabı siz düzenlerken değiştirildi. Güncel bilgiler yüklendi; kaydı yeniden yapın.',
    [IDENTITY_HISTORY_PROBLEM_CODE.notFound]: 'Bu cari veya halef cari seçili şirkette bulunamadı.',
    [IDENTITY_HISTORY_PROBLEM_CODE.idempotencyConflict]:
      'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [IDENTITY_HISTORY_PROBLEM_CODE.validationFailed]: 'Girilen halef bilgileri geçersiz.',
    [IDENTITY_HISTORY_PROBLEM_CODE.successionSameParty]: 'Bir cari kendisinin halefi olamaz.',
    [IDENTITY_HISTORY_PROBLEM_CODE.successionSameTaxIdentity]:
      'Halef cari aynı VKN/TCKN’ye sahip: bu aynı tüzel/gerçek kişidir. Tür değişikliği aynı caride kalır; halef kaydı yapılmaz.',
    [IDENTITY_HISTORY_PROBLEM_CODE.successionSuccessorInactive]: 'Halef carinin bu şirketteki hesabı pasif.',
    [IDENTITY_HISTORY_PROBLEM_CODE.successionCycle]: 'Bu kayıt halef zincirinde döngü oluşturur.',
    [IDENTITY_HISTORY_PROBLEM_CODE.successionAlreadyRecorded]: 'Bu cari için bu şirkette zaten bir halef kaydı var.',
  } as Readonly<Record<string, string>>,
} as const
