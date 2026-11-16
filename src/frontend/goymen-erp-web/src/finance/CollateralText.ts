/**
 * Turkish text for API-85b — Müşteri teminatları (F018 plan §L2; FNS-D57, FNS-D31, FNS-D58).
 *
 * Same rule as ./financeText.ts: chosen from a decoded state or a stable code, never parsed from server prose. A
 * collateral is a MASTER record: no text may suggest a money movement. A cheque or a note is never a collateral type;
 * the documentary risk is shown on its own and never added to a risk total.
 */

import type { CollateralFigureState, CollateralKind, CollateralStatus } from './CollateralContract'
import type { CollateralOperation } from './CollateralHttp'

export const COLLATERAL_TEXT = {
  title: 'Müşteri teminatları',
  accountSubject: 'cari hesap teminatları',
  historySubject: 'teminat geçmişi',
  typeSubject: 'teminat türü adayları',
  currencySubject: 'para birimi adayları',
  notice:
    'Teminat, müşteriden alınan güvencenin tanımıdır; para hareketi veya bakiye göstermez. Kayıt, değişiklik ve çözüm ' +
    'tek adımda hemen geçerli olur; her değişiklik eski ve yeni değerleriyle geçmişte kalır. Çek ve senet teminat türü ' +
    'değildir; evraklı risk ayrı gösterilir.',
  lookupHeading: 'Cari hesap seç',
  lookupHint: 'Teminatlarını görmek istediğiniz cari hesabın kimliğini girin.',
  companyAccountId: 'Cari hesap kimliği',
  partyId: 'Cari kimliği',
  identityInvalid: 'Geçerli bir kimlik girin.',
  show: 'Göster',
  close: 'Kapat',
  heldHeading: 'Tutulan teminatlar',
  heldCaption: 'Cari hesapta tutulan teminatlar',
  heldEmpty: 'Bu cari hesapta tutulan teminat yok.',
  figuresHeading: 'Teminat göstergeleri',
  weightedHeading: 'Ağırlıklı teminat',
  weightedHint: 'Bugün geçerli teminatların tutarı × Ayarlar\'daki tür oranı / 100; para birimi başına, çevrilmeden.',
  documentaryHeading: 'Evraklı risk',
  documentaryHint:
    'Tahsil edilmemiş ve müşteriye iade edilmemiş müşteri çek/senetlerinin toplamı. Ayrı gösterilir; brüt riske eklenmez.',
  figureZero: 'Yok (sıfır)',
  historyHeading: 'Teminat geçmişi',
  historyCaption: 'Teminat geçmişi',
  historyEmpty: 'Bu hesap için gösterilecek geçmiş kaydı yok.',
  column: {
    collateral: 'Teminat',
    revision: 'Sürüm',
    kind: 'İşlem türü',
    type: 'Teminat türü',
    currency: 'Para birimi',
    amount: 'Tutar',
    validFrom: 'Başlangıç',
    validUntil: 'Bitiş',
    documentReference: 'Belge referansı',
    releaseReason: 'Çözüm gerekçesi',
    status: 'Durum',
    recordedAt: 'Kayıt zamanı',
    action: 'İşlem',
  },
  openEnded: 'Süresiz',
  none: '—',
  recordHeading: 'Yeni teminat kaydet',
  recordSubmit: 'Teminatı kaydet',
  changeHeading: 'Teminatı değiştir',
  changeOpen: 'Değiştir',
  changeSubmit: 'Değişikliği kaydet',
  releaseHeading: 'Teminatı çöz',
  releaseOpen: 'Çöz',
  releaseHint: 'Çözülen teminat ters kayıtla kapanır; silinmez ve geçmişte kalır.',
  releaseReason: 'Çözüm gerekçesi',
  releaseReasonRequired: 'Çözüm gerekçesi yazın.',
  releaseSubmit: 'Teminatı çöz',
  type: 'Teminat türü',
  typePlaceholder: 'Teminat türü seçin',
  typeRequired: 'Teminat türü seçin.',
  typeOption: (code: string, name: string, rate: string) => `${code} — ${name} (%${rate})`,
  noTypes: 'Ayarlarda bu şirket için teminat türü yok veya okunamıyor; teminat kaydedilemez.',
  currency: 'Para birimi',
  currencyPlaceholder: 'Para birimi seçin',
  currencyRequired: 'Para birimi seçin.',
  noCurrencies: 'Ayarlarda bu şirket için para birimi adayı yok; teminat kaydedilemez.',
  amount: 'Teminat tutarı',
  amountHint: 'Ondalık ayırıcı virgüldür (ör. 150000,50). Tutar olduğu gibi saklanır, yuvarlanmaz.',
  amountError: {
    required: 'Tutar girin.',
    format: 'Tutar yalnız rakam ve en fazla bir virgül içerebilir.',
    'not-positive': 'Tutar sıfırdan büyük olmalı.',
  },
  validFrom: 'Geçerlilik başlangıcı',
  validFromRequired: 'Geçerlilik başlangıcını girin.',
  validUntil: 'Geçerlilik bitişi (boş: süresiz)',
  validityInvalid: 'Bitiş tarihi başlangıçtan önce olamaz.',
  documentReference: 'Belge referansı (isteğe bağlı)',
  documentReferenceInvalid: 'Belge referansının başında veya sonunda boşluk olamaz.',
  submitting: 'Gönderiliyor…',
  unknownTitle: 'Sonuç bilinmiyor',
  unknownBody: 'İsteğin sonucu alınamadı. Aynı isteği aynı anahtarla yeniden gönderin; sunucu ilk denemenin sonucunu verir.',
  unknownRecordWarning:
    'Bu denemeyi bırakmadan önce cari hesabın teminatlarını yeniden yükleyip kaydın oluşup oluşmadığını kontrol edin; ' +
    'aksi hâlde aynı teminat iki kez kaydedilebilir.',
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
    VersionConflict: 'Teminat siz bakarken değişti; güncel hâli yükleyip yeniden deneyin.',
    IdempotencyConflict: 'Aynı anahtarla farklı bir istek gönderilmiş; işlem uygulanmadı.',
    ValidationFailed: 'İstek kabul edilmedi.',
    Denied: 'Bu işlem için yetkiniz yok.',
    Unavailable: 'Hizmet şu anda yanıt veremiyor.',
  },
  reload: 'Güncel hâli yükle',
} as const

export const COLLATERAL_SUCCESS: Readonly<Record<CollateralOperation, string>> = {
  record: 'Teminat kaydedildi ve hemen geçerli oldu.',
  change: 'Teminat değiştirildi; önceki değerler geçmişte kaldı.',
  release: 'Teminat çözüldü; ters kaydı geçmişte kaldı.',
}

export const COLLATERAL_KIND: Readonly<Record<CollateralKind, string>> = {
  Record: 'Kayıt',
  Change: 'Değişiklik',
  Release: 'Çözüm',
}

export const COLLATERAL_STATUS: Readonly<Record<CollateralStatus, string>> = {
  Effective: 'Geçerli',
  Superseded: 'Yerini yenisi aldı',
}

export const COLLATERAL_FIGURE_STATE: Readonly<Record<CollateralFigureState, string>> = {
  Available: 'Hesaplandı',
  Unavailable: 'Hesaplanamıyor',
  Withheld: 'Gösterilmiyor',
}

/** Stable figure reason codes → text. An unknown code is named as such, never hidden. */
export const COLLATERAL_FIGURE_REASON: Readonly<Record<string, string>> = {
  finance_collateral_type_rate_unavailable:
    'Bir teminat türünün oranı Ayarlar\'dan tam okunamadı; kısmi toplam veya varsayılan oran gösterilmez.',
  finance_collateral_unreadable: 'Teminat kayıtlarından biri tam okunamadı; kısmi toplam gösterilmez.',
  finance_business_day_unavailable:
    'Şirket iş günü bu sunucuda belirlenemedi; teminat geçerliliği UTC tarihiyle tahmin edilmez.',
  finance_documentary_risk_status_unknown: 'Bir çek/senedin durumu tanınmıyor; evraklı risk hesaplanmaz.',
  finance_documentary_risk_unreadable: 'Bir çek/senedin tutarı tam okunamadı; kısmi toplam gösterilmez.',
  finance_documentary_risk_withheld: 'Çek/senet tutarlarını görme yetkiniz yok; evraklı risk gösterilmez.',
}

export const collateralFigureReasonText = (code: string): string =>
  COLLATERAL_FIGURE_REASON[code] ?? `Tanımsız gerekçe kodu: ${code}`

/** Stable issue codes of the collateral master → text. An unknown code falls back to the generic refusal. */
export const COLLATERAL_ISSUE: Readonly<Record<string, string>> = {
  collateral_amount_invalid: 'Tutar sıfırdan büyük, geçerli bir ondalık sayı olmalı.',
  collateral_unchanged: 'Değişiklik teminatın geçerli değerleriyle aynı; kaydedilecek bir fark yok.',
  collateral_not_found: 'Teminat bulunamadı.',
  collateral_released: 'Bu teminat çözülmüş; artık değiştirilemez veya yeniden çözülemez.',
  collateral_type_catalog_unavailable: 'Teminat türleri Ayarlar\'dan şu anda okunamıyor.',
  finance_business_day_unavailable: 'Şirket iş günü bu sunucuda belirlenemedi; işlem şu anda yapılamaz.',
  collateral_type_not_a_candidate: 'Seçilen teminat türü bu şirketin etkin teminat türleri arasında değil.',
  collateral_type_is_instrument: 'Çek veya senet teminat türü olarak kaydedilemez.',
  collateral_type_rate_unavailable: 'Seçilen teminat türünün oranı tam okunamıyor.',
  collateral_validity_invalid: 'Bitiş tarihi başlangıçtan önce olamaz.',
  collateral_document_reference_invalid: 'Belge referansı boş, başında/sonunda boşluklu veya çok uzun olamaz.',
  party_account_ineligible: 'Bu cari hesap teminat için uygun değil veya cariye ait değil.',
  party_account_unavailable: 'Cari hesap bilgisi şu anda doğrulanamıyor.',
  currency_not_a_candidate: 'Seçilen para birimi bu şirketin para birimi adayları arasında değil.',
  identity_required: 'Zorunlu bir kimlik eksik veya geçersiz.',
  reason_invalid: 'Gerekçe boş olamaz ve başında veya sonunda boşluk olamaz.',
  finance_value_does_not_fit: 'Değer izin verilen uzunluğu aşıyor.',
  version_conflict: 'Teminat siz bakarken değişti.',
  concurrent_change: 'Aynı anda başka bir değişiklik kaydedildi; güncel hâli yükleyip yeniden deneyin.',
  idempotency_key_invalid: 'İstek anahtarı geçersiz.',
  settings_decimal_scale_exceeded: 'Tutarın ondalık basamak sayısı izin verilenden fazla.',
  settings_access_denied: 'Ayarlar kaydını okuma yetkiniz yok.',
  finance_permission_mapping_unavailable: 'Yetki eşlemesi şu anda okunamıyor.',
}

export const collateralIssueText = (code: string): string => COLLATERAL_ISSUE[code] ?? COLLATERAL_TEXT.outcome.ValidationFailed

/** A short, non-identifying reference for an id the read carries without a name. */
export const shortCollateralReference = (id: string): string => id.slice(0, 8)
