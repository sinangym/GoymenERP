/**
 * Turkish text for API-85a — Cari kredi limitleri (F018 plan §L1; FNS-D27/D27a).
 *
 * Same rule as ./financeText.ts: chosen from a decoded state or a stable code, never parsed from server prose. A limit
 * is a MASTER record: no text may suggest a balance, a risk figure or a money movement. A removed limit is "limit
 * yok", never zero and never unlimited.
 */

import type { CreditLimitKind, CreditLimitStatus } from './CreditLimitContract'
import type { CreditLimitOperation } from './CreditLimitHttp'

export const CREDIT_LIMIT_TEXT = {
  title: 'Cari kredi limitleri',
  subject: 'kredi limitleri',
  accountSubject: 'cari hesap kredi limiti',
  historySubject: 'kredi limiti geçmişi',
  currencySubject: 'para birimi adayları',
  notice:
    'Kredi limiti cari hesap başına tek para biriminde tutulan bir tanımdır; para hareketi veya bakiye göstermez. ' +
    'Onay yetkilisi limiti tek adımda tanımlar, değiştirir veya kaldırır. Öneri yetkilisinin önerisi, önerenden farklı ' +
    'bir onay yetkilisi onaylayana kadar geçerli limiti değiştirmez.',
  empty: 'Bu şirkette tanımlı kredi limiti veya bekleyen öneri yok.',
  tableCaption: 'Cari hesap kredi limitleri',
  historyCaption: 'Kredi limiti geçmişi',
  historyEmpty: 'Bu hesap için gösterilecek geçmiş kaydı yok.',
  column: {
    account: 'Cari hesap',
    party: 'Cari',
    limit: 'Geçerli limit',
    pending: 'Bekleyen öneri',
    action: 'İşlem',
    kind: 'İşlem türü',
    status: 'Durum',
    direct: 'Kayıt şekli',
    proposedAt: 'Öneri zamanı',
    decidedAt: 'Karar zamanı',
    reason: 'Ret gerekçesi',
  },
  open: 'Aç',
  close: 'Kapat',
  noLimit: 'Limit yok',
  noLimitRemoved: 'Limit yok — kaldırıldı',
  noLimitNever: 'Limit tanımlanmamış',
  noPending: 'Yok',
  directRecord: 'Onay yetkilisi tek adımda',
  proposalRecord: 'Öneri ve onay',
  ownPending: 'Sizin öneriniz — önerenden farklı bir onay yetkilisi karar vermeli.',
  pendingHidden: 'Bekleyen öneriler yalnız öneri veya onay yetkisi olana gösterilir.',
  detailHeading: 'Hesap ayrıntısı',
  effectiveHeading: 'Geçerli limit',
  pendingHeading: 'Bekleyen öneri',
  newHeading: 'Yeni hesap için limit',
  newHint: 'Kredi limiti olmayan bir cari hesap için cari ve cari hesap kimliğini girin.',
  partyId: 'Cari kimliği',
  companyAccountId: 'Cari hesap kimliği',
  identityInvalid: 'Geçerli bir kimlik girin.',
  currency: 'Para birimi',
  currencyPlaceholder: 'Para birimi seçin',
  noCandidates: 'Ayarlarda bu şirket için para birimi adayı yok; limit tanımlanamaz.',
  amount: 'Limit tutarı',
  amountHint: 'Ondalık ayırıcı virgüldür (ör. 150000,50). Tutar olduğu gibi saklanır, yuvarlanmaz.',
  amountError: {
    required: 'Tutar girin.',
    format: 'Tutar yalnız rakam ve en fazla bir virgül içerebilir.',
    'not-positive': 'Tutar sıfırdan büyük olmalı.',
  },
  currencyRequired: 'Para birimi seçin.',
  setHeading: 'Limiti tanımla veya değiştir',
  setSubmit: 'Limiti kaydet',
  removeHeading: 'Limiti kaldır',
  removeHint: 'Kaldırılan limit "limit yok" demektir; sıfır veya sınırsız anlamına gelmez.',
  removeSubmit: 'Limiti kaldır',
  proposeHeading: 'Limit öner',
  proposeKind: 'Öneri türü',
  proposeKindSet: 'Limit tanımla veya değiştir',
  proposeKindRemove: 'Limiti kaldır',
  proposeSubmit: 'Öneriyi gönder',
  approve: 'Öneriyi onayla',
  reject: 'Öneriyi reddet',
  rejectReason: 'Ret gerekçesi',
  rejectReasonRequired: 'Ret gerekçesi yazın.',
  submitting: 'Gönderiliyor…',
  unknownTitle: 'Sonuç bilinmiyor',
  unknownBody: 'İsteğin sonucu alınamadı. Aynı isteği aynı anahtarla yeniden gönderin; sunucu ilk denemenin sonucunu verir.',
  resend: 'Aynı isteği yeniden gönder',
  abandon: 'Bu denemeyi bırak',
  blocked: 'Sonucu bilinmeyen bir deneme var; önce onu yeniden gönderin veya bırakın.',
  dismiss: 'Tamam',
  replayNote: 'Bu istek daha önce işlenmişti; ilk denemenin sonucu gösteriliyor.',
  refusedStatus: {
    400: 'İstek biçimi sunucu tarafından reddedildi.',
    401: 'Oturumunuz sona erdi; yeniden giriş yapın.',
    403: 'Bu işlem için yetkiniz yok veya bu öneriye siz karar veremezsiniz.',
    404: 'Kayıt bulunamadı.',
    415: 'İstek biçimi desteklenmiyor.',
  } as Readonly<Record<number, string>>,
  refusedOther: 'İstek reddedildi.',
  outcome: {
    VersionConflict: 'Hesabın limiti siz bakarken değişti; güncel hâli yükleyip yeniden deneyin.',
    IdempotencyConflict: 'Aynı anahtarla farklı bir istek gönderilmiş; işlem uygulanmadı.',
    ValidationFailed: 'İstek kabul edilmedi.',
    Denied: 'Bu işlem için yetkiniz yok.',
    Unavailable: 'Hizmet şu anda yanıt veremiyor.',
  },
  reload: 'Güncel hâli yükle',
} as const

export const CREDIT_LIMIT_SUCCESS: Readonly<Record<CreditLimitOperation, string>> = {
  set: 'Limit kaydedildi ve hemen geçerli oldu.',
  remove: 'Limit kaldırıldı; hesap için limit yok.',
  propose: 'Öneri gönderildi; onaylanana kadar geçerli limit değişmez.',
  approve: 'Öneri onaylandı ve geçerli oldu.',
  reject: 'Öneri gerekçesiyle reddedildi.',
}

export const CREDIT_LIMIT_KIND: Readonly<Record<CreditLimitKind, string>> = {
  Set: 'Limit tanımı',
  Remove: 'Limit kaldırma',
}

export const CREDIT_LIMIT_STATUS: Readonly<Record<CreditLimitStatus, string>> = {
  Proposed: 'Onay bekliyor',
  Approved: 'Geçerli',
  Rejected: 'Reddedildi',
  Superseded: 'Yerini yenisi aldı',
}

/** Stable issue codes of the credit limit master → text. An unknown code falls back to the generic refusal. */
export const CREDIT_LIMIT_ISSUE: Readonly<Record<string, string>> = {
  credit_limit_amount_invalid: 'Tutar sıfırdan büyük, geçerli bir ondalık sayı olmalı.',
  credit_limit_remove_carries_no_amount: 'Kaldırma önerisi para birimi veya tutar taşımaz.',
  credit_limit_kind_invalid: 'Öneri türü geçersiz.',
  credit_limit_not_set: 'Bu hesap için kaldırılacak bir limit yok.',
  credit_limit_proposal_pending: 'Bu hesap için bekleyen bir öneri var; önce ona karar verilmeli.',
  credit_limit_proposal_not_found: 'Öneri bulunamadı.',
  credit_limit_not_proposed: 'Bu öneriye artık karar verilemez.',
  credit_limit_revision_stale: 'Öneriden sonra hesabın limiti değişti; öneri onaylanamaz.',
  party_account_ineligible: 'Bu cari hesap kredi limiti için uygun değil veya cariye ait değil.',
  party_account_unavailable: 'Cari hesap bilgisi şu anda doğrulanamıyor.',
  currency_not_a_candidate: 'Seçilen para birimi bu şirketin para birimi adayları arasında değil.',
  identity_required: 'Zorunlu bir kimlik eksik veya geçersiz.',
  reason_invalid: 'Gerekçe boş olamaz ve başında veya sonunda boşluk olamaz.',
  finance_value_does_not_fit: 'Değer izin verilen uzunluğu aşıyor.',
  finance_approver_is_proposer: 'Kendi önerinize karar veremezsiniz.',
  version_conflict: 'Kayıt siz bakarken değişti.',
  idempotency_key_invalid: 'İstek anahtarı geçersiz.',
  settings_decimal_scale_exceeded: 'Tutarın ondalık basamak sayısı izin verilenden fazla.',
}

export const creditLimitIssueText = (code: string): string => CREDIT_LIMIT_ISSUE[code] ?? CREDIT_LIMIT_TEXT.outcome.ValidationFailed

/** A short, non-identifying reference for an id the read carries without a name. */
export const shortCreditLimitReference = (id: string): string => id.slice(0, 8)

export const creditLimitAccountName = (reference: string): string => `Cari hesap ${reference} ayrıntısını aç`

export const creditLimitCurrencyReference = (reference: string): string => `Para birimi ${reference}`
