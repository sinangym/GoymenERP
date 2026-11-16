/**
 * Turkish text for FIN-S9 — Açılış bakiyeleri (FNS-D23).
 *
 * Same rule as ./financeText.ts: chosen from a decoded state or a stable code, never parsed from server prose. An
 * approved opening is recorded and audited; it moves no money yet, so no text may suggest a posted balance.
 */

import type {
  OpeningBalanceDirection,
  OpeningBalanceStatus,
  OpeningBalanceTargetKind,
  OpeningPeriodState,
} from './OpeningBalanceContract'
import type { OpeningBalanceOperation } from './OpeningBalanceHttp'

export const OPENING_BALANCE_TEXT = {
  title: 'Açılış bakiyeleri',
  periodSubject: 'açılış dönemi',
  listSubject: 'açılış bakiyeleri',
  currencySubject: 'para birimi adayları',
  notice:
    'Kasa, banka ve cari açılış bakiyeleri yalnız Ayarlar\'daki açılış dönemi açıkken girilir. Uzman önerir, müdür ' +
    'onaylar veya reddeder; öneren kendi önerisini onaylayamaz. Onaylanan açılış kaydedilir ve denetim izine yazılır; ' +
    'şimdilik bakiye veya cari hareket oluşturmaz.',
  listCaption: 'Açılış bakiyeleri',
  listEmpty: 'Henüz açılış bakiyesi önerilmedi.',
  column: {
    reference: 'Kayıt',
    target: 'Hedef',
    targetId: 'Hedef kimliği',
    party: 'Cari',
    direction: 'Yön',
    amount: 'Tutar',
    asOfDate: 'Bakiye tarihi',
    status: 'Durum',
    proposedAt: 'Öneri zamanı',
    decidedAt: 'Karar zamanı',
    rejectionReason: 'Red gerekçesi',
    action: 'İşlem',
  },
  none: '—',
  ownProposal: 'Sizin öneriniz; başka bir yetkili karar verir.',
  proposeHeading: 'Açılış bakiyesi öner',
  proposeSubmit: 'Öneriyi gönder',
  targetKind: 'Hedef türü',
  targetKindPlaceholder: 'Hedef türü seçin',
  targetKindRequired: 'Hedef türü seçin.',
  targetId: 'Hedef kimliği (kasa, banka hesabı veya cari hesap)',
  partyId: 'Cari kimliği',
  identityInvalid: 'Geçerli bir kimlik girin.',
  direction: 'Yön',
  directionPlaceholder: 'Yön seçin',
  directionRequired: 'Cari açılışı için yön seçin.',
  currency: 'Para birimi',
  currencyPlaceholder: 'Para birimi seçin',
  currencyRequired: 'Para birimi seçin.',
  noCurrencies: 'Ayarlarda bu şirket için para birimi adayı yok; açılış bakiyesi önerilemez.',
  amount: 'Tutar',
  amountHint: 'Ondalık ayırıcı virgüldür (ör. 150000,50). Tutar olduğu gibi saklanır, yuvarlanmaz.',
  amountError: {
    required: 'Tutar girin.',
    format: 'Tutar yalnız rakam ve en fazla bir virgül içerebilir.',
    'not-positive': 'Tutar sıfırdan büyük olmalı.',
  },
  asOfDate: 'Bakiye tarihi',
  asOfDateRequired: 'Bakiye tarihini girin.',
  approve: 'Onayla',
  rejectOpen: 'Reddet',
  rejectHeading: 'Açılış bakiyesini reddet',
  rejectReason: 'Red gerekçesi',
  rejectReasonRequired: 'Red gerekçesi yazın.',
  rejectSubmit: 'Reddet',
  close: 'Kapat',
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
    403: 'Bu işlem için yetkiniz yok.',
    404: 'Kayıt bulunamadı.',
    415: 'İstek biçimi desteklenmiyor.',
  } as Readonly<Record<number, string>>,
  refusedOther: 'İstek reddedildi.',
  outcome: {
    VersionConflict: 'Açılış bakiyesi siz bakarken değişti; güncel hâli yükleyip yeniden deneyin.',
    IdempotencyConflict: 'Aynı anahtarla farklı bir istek gönderilmiş; işlem uygulanmadı.',
    ValidationFailed: 'İstek kabul edilmedi.',
    Denied: 'Bu işlem için yetkiniz yok.',
    Unavailable: 'Hizmet şu anda yanıt veremiyor.',
  },
  reload: 'Güncel hâli yükle',
} as const

/** The period notice when entry is not possible: the list and the forms are not shown then. */
export const OPENING_PERIOD_NOTICE: Readonly<Record<Exclude<OpeningPeriodState, 'Open'>, string>> = {
  Closed: 'Açılış dönemi kapalı; açılış bakiyesi girilemez ve önceki açılışlar bu ekranda gösterilmez.',
  Unavailable: 'Açılış dönemi ayarı Ayarlar\'dan şu anda okunamıyor; açılış bakiyesi girilemez.',
}

export const OPENING_BALANCE_SUCCESS: Readonly<Record<OpeningBalanceOperation, string>> = {
  propose: 'Açılış bakiyesi önerildi; başka bir yetkilinin onayını bekliyor.',
  approve: 'Açılış bakiyesi onaylandı.',
  reject: 'Açılış bakiyesi reddedildi; aynı hedef için yeni öneri girilebilir.',
}

export const OPENING_BALANCE_TARGET: Readonly<Record<OpeningBalanceTargetKind, string>> = {
  CashAccount: 'Kasa',
  BankAccount: 'Banka hesabı',
  PartyAccount: 'Cari hesap',
}

export const OPENING_BALANCE_DIRECTION: Readonly<Record<OpeningBalanceDirection, string>> = {
  Debit: 'Borç (cari şirkete borçlu)',
  Credit: 'Alacak (şirket cariye borçlu)',
}

export const OPENING_BALANCE_STATUS: Readonly<Record<OpeningBalanceStatus, string>> = {
  Proposed: 'Onay bekliyor',
  Approved: 'Onaylandı',
  Rejected: 'Reddedildi',
}

/** Stable issue codes → text. An unknown code falls back to the generic refusal. */
export const OPENING_BALANCE_ISSUE: Readonly<Record<string, string>> = {
  opening_amount_invalid: 'Tutar sıfırdan büyük, geçerli bir ondalık sayı olmalı.',
  opening_balance_exists: 'Bu hedef ve para birimi için bekleyen veya onaylanmış bir açılış bakiyesi zaten var.',
  opening_balance_not_found: 'Açılış bakiyesi bulunamadı.',
  opening_balance_not_proposed: 'Bu açılış bakiyesine zaten karar verilmiş.',
  opening_currency_mismatch: 'Seçilen para birimi kasanın veya banka hesabının para birimi değil.',
  opening_direction_required: 'Cari açılışı için cari ve yön gerekir; kasa ve banka açılışında yön verilmez.',
  opening_period_closed: 'Açılış dönemi kapalı; işlem yapılamaz.',
  opening_period_unavailable: 'Açılış dönemi ayarı şu anda okunamıyor; işlem yapılamaz.',
  opening_target_kind_invalid: 'Hedef türü tanınmıyor.',
  opening_target_not_eligible: 'Hedef bu şirkette bulunamadı veya açılış bakiyesi için uygun değil.',
  finance_approver_is_proposer: 'Kendi önerinizi onaylayamaz veya reddedemezsiniz.',
  party_account_ineligible: 'Bu cari hesap uygun değil veya cariye ait değil.',
  party_account_unavailable: 'Cari hesap bilgisi şu anda doğrulanamıyor.',
  currency_not_a_candidate: 'Seçilen para birimi bu şirketin para birimi adayları arasında değil.',
  identity_required: 'Zorunlu bir kimlik eksik veya geçersiz.',
  reason_invalid: 'Gerekçe boş olamaz ve başında veya sonunda boşluk olamaz.',
  finance_value_does_not_fit: 'Değer izin verilen uzunluğu aşıyor.',
  version_conflict: 'Açılış bakiyesi siz bakarken değişti.',
  concurrent_change: 'Aynı anda başka bir değişiklik kaydedildi; güncel hâli yükleyip yeniden deneyin.',
  idempotency_key_invalid: 'İstek anahtarı geçersiz.',
  settings_access_denied: 'Ayarlar kaydını okuma yetkiniz yok.',
  finance_permission_mapping_unavailable: 'Yetki eşlemesi şu anda okunamıyor.',
}

export const openingBalanceIssueText = (code: string): string =>
  OPENING_BALANCE_ISSUE[code] ?? OPENING_BALANCE_TEXT.outcome.ValidationFailed

/** A short, non-identifying reference for an id the read carries without a name. */
export const shortOpeningReference = (id: string): string => id.slice(0, 8)
