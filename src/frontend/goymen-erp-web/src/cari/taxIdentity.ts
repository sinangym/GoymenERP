/**
 * AT-009 — VKN/TCKN on the Cari card (user decision 2026-09-30).
 *
 *  - VALIDATION mirrors the server's `TaxIdentityNumber` exactly: 10 digits = VKN (Gelir İdaresi check digit),
 *    11 digits = TCKN (first digit not 0, NVİ check digits 10 and 11). Only surrounding whitespace is trimmed;
 *    anything else is refused, never cleaned. The client check only saves a round trip — the server refuses an
 *    invalid number on its own (`tax_identity_invalid`), and that answer is what counts.
 *  - DUPLICATE: the same number on another card of the SAME company is a warning; the save still goes through.
 *  - CHANGE: an existing number is never overwritten; a change is a request another person decides.
 *
 * The decoders are strict: an unknown shape is `null`, never a partly trusted object.
 */

import { isBoundUuid, isLosslessVersion } from './wireGuards'

export const TAX_IDENTITY_KIND = { vkn: 'vkn', tckn: 'tckn' } as const
export type TaxIdentityKind = (typeof TAX_IDENTITY_KIND)[keyof typeof TAX_IDENTITY_KIND]

export const TAX_CHANGE_STATUS = { pending: 'pending', approved: 'approved', rejected: 'rejected' } as const
export type TaxChangeStatus = (typeof TAX_CHANGE_STATUS)[keyof typeof TAX_CHANGE_STATUS]

/** The endpoint's own stable codes (`CounterpartyProblemCodesV1`). */
export const TAX_PROBLEM_CODE = {
  invalid: 'tax_identity_invalid',
  requiresApproval: 'tax_identity_change_requires_approval',
  notSet: 'tax_identity_not_set',
  unchanged: 'tax_identity_unchanged',
  changePending: 'tax_identity_change_pending',
  changeNotPending: 'tax_identity_change_not_pending',
  changeStale: 'tax_identity_change_stale',
  selfDecision: 'tax_identity_self_decision',
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
} as const

export type TaxIdentityCheck =
  | { readonly ok: true; readonly value: string; readonly kind: TaxIdentityKind }
  | { readonly ok: false; readonly message: string }

export function isValidVkn(digits: string): boolean {
  if (!/^[0-9]{10}$/.test(digits)) return false
  let sum = 0
  for (let i = 1; i <= 9; i++) {
    const t = (Number(digits[i - 1]) + 10 - i) % 10
    sum += t === 9 ? 9 : (t * 2 ** (10 - i)) % 9
  }
  return (10 - (sum % 10)) % 10 === Number(digits[9])
}

export function isValidTckn(digits: string): boolean {
  if (!/^[1-9][0-9]{10}$/.test(digits)) return false
  const d = [...digits].map(Number)
  const odd = d[0] + d[2] + d[4] + d[6] + d[8]
  const even = d[1] + d[3] + d[5] + d[7]
  const tenth = (((odd * 7 - even) % 10) + 10) % 10
  if (tenth !== d[9]) return false
  const firstTen = d.slice(0, 10).reduce((a, b) => a + b, 0)
  return firstTen % 10 === d[10]
}

export function checkTaxIdentity(text: string): TaxIdentityCheck {
  const value = text.trim()
  if (value.length === 0) return { ok: false, message: TAX_IDENTITY_TEXT.invalidEmpty }
  if (!/^[0-9]+$/.test(value)) return { ok: false, message: TAX_IDENTITY_TEXT.invalidCharacters }
  if (value.length === 10) {
    return isValidVkn(value)
      ? { ok: true, value, kind: TAX_IDENTITY_KIND.vkn }
      : { ok: false, message: TAX_IDENTITY_TEXT.invalidVkn }
  }
  if (value.length === 11) {
    return isValidTckn(value)
      ? { ok: true, value, kind: TAX_IDENTITY_KIND.tckn }
      : { ok: false, message: TAX_IDENTITY_TEXT.invalidTckn }
  }
  return { ok: false, message: TAX_IDENTITY_TEXT.invalidLength }
}

export interface TaxDuplicate {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly accountIsActive: boolean
}

export interface TaxChange {
  readonly id: string
  readonly status: TaxChangeStatus
  readonly currentTaxIdentityNumber: string
  readonly proposedTaxIdentityNumber: string
  readonly proposedKind: TaxIdentityKind
  readonly reason: string
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionNote: string | null
  readonly proposedDuplicates: readonly TaxDuplicate[]
}

export interface TaxIdentityView {
  readonly partyId: string
  readonly companyId: string
  readonly version: string
  readonly taxIdentityNumber: string | null
  readonly kind: TaxIdentityKind | null
  readonly duplicates: readonly TaxDuplicate[]
  readonly pendingChange: TaxChange | null
  readonly lastDecidedChange: TaxChange | null
  readonly viewer: {
    readonly canUpdate: boolean
    readonly canApprove: boolean
    readonly isRequesterOfPending: boolean
  }
}

export interface TaxDuplicateCheck {
  readonly taxIdentityNumber: string
  readonly kind: TaxIdentityKind
  readonly duplicates: readonly TaxDuplicate[]
}

const isObject = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null
const isString = (raw: unknown): raw is string => typeof raw === 'string'
const isNullableString = (raw: unknown): raw is string | null => raw === null || typeof raw === 'string'
const isKind = (raw: unknown): raw is TaxIdentityKind => raw === TAX_IDENTITY_KIND.vkn || raw === TAX_IDENTITY_KIND.tckn
const isStatus = (raw: unknown): raw is TaxChangeStatus =>
  raw === TAX_CHANGE_STATUS.pending || raw === TAX_CHANGE_STATUS.approved || raw === TAX_CHANGE_STATUS.rejected
const isStoredNumber = (raw: unknown): raw is string => isString(raw) && checkTaxIdentity(raw).ok

function decodeDuplicates(raw: unknown): TaxDuplicate[] | null {
  if (!Array.isArray(raw)) return null
  const out: TaxDuplicate[] = []
  for (const item of raw) {
    if (
      !isObject(item) ||
      !isBoundUuid(item.partyId) ||
      !isString(item.code) ||
      !isString(item.legalName) ||
      typeof item.accountIsActive !== 'boolean'
    ) {
      return null
    }
    out.push({
      partyId: item.partyId,
      code: item.code,
      legalName: item.legalName,
      accountIsActive: item.accountIsActive,
    })
  }
  return out
}

function decodeChange(raw: unknown): TaxChange | null {
  if (!isObject(raw)) return null
  const proposedDuplicates = decodeDuplicates(raw.proposedDuplicates)
  if (
    !isBoundUuid(raw.id) ||
    !isStatus(raw.status) ||
    !isStoredNumber(raw.currentTaxIdentityNumber) ||
    !isStoredNumber(raw.proposedTaxIdentityNumber) ||
    !isKind(raw.proposedKind) ||
    !isString(raw.reason) ||
    !isString(raw.requestedBy) ||
    !isString(raw.requestedAtUtc) ||
    !isNullableString(raw.decidedBy) ||
    !isNullableString(raw.decidedAtUtc) ||
    !isNullableString(raw.decisionNote) ||
    proposedDuplicates === null
  ) {
    return null
  }
  return {
    id: raw.id,
    status: raw.status,
    currentTaxIdentityNumber: raw.currentTaxIdentityNumber,
    proposedTaxIdentityNumber: raw.proposedTaxIdentityNumber,
    proposedKind: raw.proposedKind,
    reason: raw.reason,
    requestedBy: raw.requestedBy,
    requestedAtUtc: raw.requestedAtUtc,
    decidedBy: raw.decidedBy,
    decidedAtUtc: raw.decidedAtUtc,
    decisionNote: raw.decisionNote,
    proposedDuplicates,
  }
}

/** The GET body, bound to the party and company it was asked for; anything else is `null`. */
export function decodeTaxIdentityView(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): TaxIdentityView | null {
  if (!isObject(raw) || !isObject(raw.viewer)) return null
  const duplicates = decodeDuplicates(raw.duplicates)
  const pendingChange = raw.pendingChange === null ? null : decodeChange(raw.pendingChange)
  const lastDecidedChange = raw.lastDecidedChange === null ? null : decodeChange(raw.lastDecidedChange)
  const viewer = raw.viewer
  if (
    raw.partyId !== expected.partyId ||
    raw.companyId !== expected.companyId ||
    !isLosslessVersion(raw.version) ||
    !(raw.taxIdentityNumber === null || isStoredNumber(raw.taxIdentityNumber)) ||
    !(raw.kind === null || isKind(raw.kind)) ||
    (raw.taxIdentityNumber === null) !== (raw.kind === null) ||
    duplicates === null ||
    (raw.pendingChange !== null && (pendingChange === null || pendingChange.status !== TAX_CHANGE_STATUS.pending)) ||
    (raw.lastDecidedChange !== null &&
      (lastDecidedChange === null || lastDecidedChange.status === TAX_CHANGE_STATUS.pending)) ||
    typeof viewer.canUpdate !== 'boolean' ||
    typeof viewer.canApprove !== 'boolean' ||
    typeof viewer.isRequesterOfPending !== 'boolean'
  ) {
    return null
  }
  return {
    partyId: raw.partyId,
    companyId: raw.companyId,
    version: raw.version,
    taxIdentityNumber: raw.taxIdentityNumber as string | null,
    kind: raw.kind as TaxIdentityKind | null,
    duplicates,
    pendingChange,
    lastDecidedChange,
    viewer: {
      canUpdate: viewer.canUpdate,
      canApprove: viewer.canApprove,
      isRequesterOfPending: viewer.isRequesterOfPending,
    },
  }
}

export function decodeTaxDuplicateCheck(raw: unknown, expectedNumber: string): TaxDuplicateCheck | null {
  if (!isObject(raw)) return null
  const duplicates = decodeDuplicates(raw.duplicates)
  if (raw.taxIdentityNumber !== expectedNumber || !isKind(raw.kind) || duplicates === null) return null
  return { taxIdentityNumber: raw.taxIdentityNumber, kind: raw.kind, duplicates }
}

/** The stable code of a problem body, whatever it is (the shared decoder only knows the older codes). */
export function taxProblemCodeOf(raw: unknown): string | null {
  return isObject(raw) && isString(raw.code) ? raw.code : null
}

export const taxIdentityPath = (partyId: string) => `/counterparties/${encodeURIComponent(partyId)}/tax-identity`

export const KIND_LABEL: Record<TaxIdentityKind, string> = {
  [TAX_IDENTITY_KIND.vkn]: 'VKN',
  [TAX_IDENTITY_KIND.tckn]: 'TCKN',
}

export const TAX_IDENTITY_TEXT = {
  heading: 'Vergi / kimlik numarası',
  factLabel: 'VKN / TCKN',
  none: 'Girilmemiş',
  inputLabel: 'VKN (10 hane) veya TCKN (11 hane)',
  proposedLabel: 'Yeni VKN / TCKN',
  reasonLabel: 'Değişiklik gerekçesi',
  noteLabel: 'Karar notu (isteğe bağlı)',
  save: 'Kaydet',
  saveAnyway: 'Uyarıya rağmen kaydet',
  cancel: 'Vazgeç',
  requestChange: 'Numara değişikliği talep et',
  sendRequest: 'Onaya gönder',
  sendAnyway: 'Uyarıya rağmen onaya gönder',
  approve: 'Onayla',
  reject: 'Reddet',
  checking: 'Kontrol ediliyor…',
  sending: 'Gönderiliyor…',
  loading: 'Vergi/kimlik bilgisi yükleniyor…',
  loadFailed: 'Vergi/kimlik bilgisi okunamadı.',
  retry: 'Yeniden dene',
  saved: 'Numara kaydedildi.',
  requested: 'Değişiklik talebi onaya gönderildi. Başka bir yetkili onaylayana kadar kayıtlı numara geçerlidir.',
  approved: 'Değişiklik onaylandı; yeni numara kaydedildi.',
  rejected: 'Değişiklik talebi reddedildi; kayıtlı numara aynı kaldı.',
  duplicateTitle: 'Aynı numara bu şirketin başka cari kartında da kayıtlı',
  duplicateDetail: 'Şube veya aynı firma olabilir; kayıt engellenmez. Doğru cari olduğundan emin olun.',
  proposedDuplicateTitle: 'Önerilen numara bu şirketin başka cari kartında da kayıtlı',
  inactiveAccount: '(bu şirkette pasif)',
  pendingTitle: 'Onay bekleyen değişiklik',
  pendingCurrent: 'Kayıtlı numara',
  pendingProposed: 'Önerilen numara',
  pendingReason: 'Gerekçe',
  pendingRequestedAt: 'Talep zamanı',
  pendingBySelf: 'Bu talebi siz açtınız. Başka bir yetkili onaylamalı veya reddetmelidir.',
  pendingByOther: 'Talebi başka bir kullanıcı açtı.',
  pendingNoApprover: 'Bu değişikliği onaylama yetkiniz yok; yetkili bir kişi karara bağlayacak.',
  lastDecisionApproved: 'Son değişiklik talebi onaylandı',
  lastDecisionRejected: 'Son değişiklik talebi reddedildi',
  changeNote: 'Kayıtlı numara doğrudan değiştirilemez; değişiklik ikinci bir yetkilinin onayıyla uygulanır.',
  invalidEmpty: 'Numara boş olamaz.',
  invalidCharacters: 'Yalnız rakam girin (boşluk, tire veya harf olmadan).',
  invalidLength: 'VKN 10, TCKN 11 haneli olmalıdır.',
  invalidVkn: 'VKN geçersiz: kontrol hanesi tutmuyor.',
  invalidTckn: 'TCKN geçersiz: ilk hane 0 olamaz ve kontrol haneleri tutmalıdır.',
  reasonRequired: 'Değişiklik için gerekçe yazın (en çok 1000 karakter).',
  contractNotReady: 'Kart yeniden okunmadan işlem gönderilemez. Sayfayı yenileyin.',
  unknownOutcome:
    'Sunucudan kesin yanıt alınamadı. İşlem uygulanmış olabilir; aynı işlem anahtarıyla yeniden deneyin.',
  retrySame: 'Aynı işlemi yeniden gönder',
  forbidden: 'Bu işlem için yetkiniz yok.',
  refusal: {
    tax_identity_invalid: 'Numara geçersiz; sunucu kaydetmedi.',
    tax_identity_change_requires_approval:
      'Bu carinin kayıtlı bir numarası var; değiştirmek için değişiklik talebi açın.',
    tax_identity_not_set: 'Bu carinin kayıtlı numarası yok; numarayı doğrudan girin.',
    tax_identity_unchanged: 'Önerilen numara kayıtlı numarayla aynı.',
    tax_identity_change_pending: 'Bu cari için onay bekleyen bir değişiklik talebi zaten var.',
    tax_identity_change_not_pending: 'Bu talep artık beklemede değil.',
    tax_identity_change_stale:
      'Kayıtlı numara talepten sonra değişmiş; talep onaylanamaz. Reddedip yeni talep açılmalı.',
    tax_identity_self_decision: 'Kendi açtığınız talebi onaylayamaz veya reddedemezsiniz.',
    version_conflict: 'Cari kart başka bir işlemle değişmiş. Güncel bilgi yüklendi; tekrar deneyin.',
    not_found: 'Cari bu şirkette bulunamadı.',
    idempotency_conflict: 'İşlem anahtarı başka bir istek için kullanılmış; işlemi yeniden başlatın.',
    validation_failed: 'İstek geçersiz.',
    client_contract_required: 'Bu ekran sürümü sunucuyla uyumlu değil. Sayfayı yenileyin.',
    client_contract_unsupported: 'Bu ekran sürümü sunucuyla uyumlu değil. Sayfayı yenileyin.',
  } as Record<string, string>,
  genericRefusal: 'İşlem tamamlanamadı.',
} as const
