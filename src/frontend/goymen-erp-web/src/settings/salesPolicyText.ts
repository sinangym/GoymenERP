/**
 * Turkish texts of the F-029 Settings sales policy screens (SAT-D5 delivery tolerance, SAT-D6 approval amount thresholds;
 * URN-D2/D3/D4). Every user-facing string lives here; the screens compose nothing from a server's localized title or
 * detail, and they never state a tolerance or threshold the server did not send.
 */

import { SALES_POLICY_INPUT, SALES_POLICY_PROBLEM, SALES_POLICY_SOURCE, SALES_POLICY_STATE } from './salesPolicyContract'

export const POLICY_TEXT = {
  // Shared
  columnVersion: 'Sürüm',
  columnFrom: 'Başlangıç',
  columnThrough: 'Bitiş',
  columnState: 'Durum',
  columnSource: 'Kaynak',
  columnRecorded: 'Kaydeden / zaman',
  columnReason: 'Gerekçe',
  openEnded: 'Süresiz',
  notRecorded: '—',
  baselineHeading: 'Başlangıç değerlerini devral',
  baselinePending:
    'Bu şirkette değerler henüz Ayarlar kaydına devredilmedi; aşağıdaki tablo derlenmiş başlangıç kataloğunu gösterir. ' +
    'Değişiklik yapabilmek için önce başlangıç değerlerini bir kez devralın. Devir, değerleri değiştirmez; yalnız kayda alır.',
  baselineSubmit: 'Başlangıç değerlerini devral',
  baselineDone: 'Başlangıç değerleri devralındı.',
  newVersion: 'Yeni sürüm kaydet',
  reasonLabel: 'Gerekçe',
  reasonHelp: `Zorunlu; en çok ${SALES_POLICY_INPUT.reasonMaximumLength} karakter. Değişiklik gerekçesiyle birlikte denetim kaydına geçer.`,
  effectiveFromLabel: 'Geçerlilik başlangıcı',
  effectiveFromHelp:
    'Bugün veya ileri bir tarih; geçmiş tarihli kayıt yapılamaz. Bugünden geçerli kayıt hemen yürürlüğe girer; ileri tarihli kayıt planlı olarak bekler.',
  effectiveFromError: 'Tarih bugünden önce olamaz.',
  oneManagerNote:
    'Bu değişiklik ikinci bir onaya gitmez; yetkili yönetici kaydettiği anda yayımlanır ve her değişiklik denetim kaydına geçer.',
  save: 'Kaydet',
  cancel: 'Vazgeç',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  saved: 'Değişiklik kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; sonuç yeniden gösterildi.',
  unknownOutcome:
    'Sonuç alınamadı; işlem uygulanmış olabilir. "Aynı işlemi yeniden dene" aynı işlem anahtarıyla gönderir ve iki kez uygulanmaz.',
  staleReloaded: 'Kayıt siz açtıktan sonra değişti. Güncel hâli yüklendi; değişikliği yeniden yapın.',

  // Delivery tolerance (SAT-D5)
  toleranceTitle: 'Teslim toleransı',
  toleranceIntro:
    'Sipariş miktarını aşan teslimde uyarı, onay ve engel eşikleri (yüzde). Eşikler sevkiyat sırasında sunucuda değerlendirilir; ' +
    'bu ekran yalnız kayıtlı sürümleri gösterir ve yeni sürüm kaydeder.',
  toleranceSubject: 'teslim toleransı',
  toleranceTableLabel: 'Teslim toleransı sürümleri',
  toleranceEmpty: 'Bu şirket için kayıtlı teslim toleransı sürümü yok.',
  toleranceInForce: 'Bugün geçerli',
  toleranceNoneInForce: 'Bugün geçerli bir teslim toleransı sürümü yok.',
  columnWarn: 'Uyarı %',
  columnApproval: 'Onay %',
  columnBlock: 'Engel %',
  warnLabel: 'Uyarı eşiği (%)',
  approvalLabel: 'Onay eşiği (%)',
  blockLabel: 'Engel eşiği (%)',
  percentHelp: `0 ile 100 arasında, en çok ${SALES_POLICY_INPUT.percentMaximumScale} ondalık. Uyarı ≤ onay ≤ engel olmalıdır; sunucu kontrol eder.`,
  percentError: `0 ile 100 arasında, en çok ${SALES_POLICY_INPUT.percentMaximumScale} ondalıklı bir yüzde girin.`,

  // Approval amount thresholds (SAT-D6)
  thresholdTitle: 'Onay tutar eşikleri',
  thresholdIntro:
    'Satış belgesinin tutar nedeniyle onaya gitmesi için para birimi başına eşik. Eşik kapalıysa tutar nedeniyle onay istenmez. ' +
    'Değerlendirme sunucuda yapılır; bu ekran yalnız kayıtlı sürümleri gösterir ve yeni sürüm kaydeder.',
  thresholdSubject: 'onay tutar eşikleri',
  thresholdEmpty: 'Bu şirket için tanımlı para birimi eşiği yok.',
  currencyHeading: 'Para birimi',
  currencyTableLabel: 'Onay tutar eşiği sürümleri — para birimi',
  currencyNoVersions: 'Bu para birimi için kayıtlı sürüm yok.',
  columnEnabled: 'Eşik',
  columnAmount: 'Tutar',
  enabled: 'Açık',
  disabled: 'Kapalı',
  noAmount: '—',
  enabledLegend: 'Tutar eşiği',
  enabledOn: 'Açık — bu tutar ve üzeri onaya gider',
  enabledOff: 'Kapalı — tutar nedeniyle onay istenmez',
  amountLabel: 'Eşik tutarı',
  amountHelp: `Sıfırdan büyük; en çok ${SALES_POLICY_INPUT.amountMaximumIntegerDigits} tam basamak ve ${SALES_POLICY_INPUT.amountMaximumScale} ondalık. Ondalık ayırıcı nokta (örn. 250000.50).`,
  amountError: 'Sıfırdan büyük, nokta ayırıcılı bir tutar girin (örn. 250000.50).',
} as const

/** A refusal's text by its stable code (or status when the body is code-less). */
export function refusalText(status: number, code: string | null): string {
  switch (code) {
    case SALES_POLICY_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case SALES_POLICY_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${SALES_POLICY_INPUT.reasonMaximumLength} karakter olabilir.`
    case SALES_POLICY_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz (örn. uyarı ≤ onay ≤ engel sırası); alanları kontrol edin.'
    case SALES_POLICY_PROBLEM.commandMetadataInvalid:
      return 'İstek bilgileri eksik ya da geçersiz; sayfayı yenileyip tekrar deneyin.'
    case SALES_POLICY_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açıp tekrar deneyin.'
    case SALES_POLICY_PROBLEM.versionConflict:
      return POLICY_TEXT.staleReloaded
    case SALES_POLICY_PROBLEM.effectiveFromPast:
      return 'Geçerlilik başlangıcı bugünden önce olamaz.'
    case SALES_POLICY_PROBLEM.baselineRequired:
      return 'Önce başlangıç değerleri devralınmalı. Güncel hâli yüklendi.'
    case SALES_POLICY_PROBLEM.baselineAlreadyTransferred:
      return 'Başlangıç değerleri bu şirkette zaten devralınmış. Güncel hâli yüklendi.'
    case SALES_POLICY_PROBLEM.currencyNotServed:
      return 'Bu para birimi için eşik kaydedilemez.'
    case SALES_POLICY_PROBLEM.amountScaleExceeded:
      return 'Tutar, izin verilen basamak sayısını aşıyor.'
    case SALES_POLICY_PROBLEM.providerUnavailable:
      return 'Ayarlar hizmeti şu anda yanıt vermiyor; biraz sonra tekrar deneyin.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 415) return 'İstek biçimi kabul edilmedi.'
      return 'İşlem kabul edilmedi.'
  }
}

export function stateLabel(state: string): string {
  switch (state) {
    case SALES_POLICY_STATE.inForce:
      return 'Yürürlükte'
    case SALES_POLICY_STATE.scheduled:
      return 'Planlı'
    case SALES_POLICY_STATE.ended:
      return 'Sona erdi'
    case SALES_POLICY_STATE.superseded:
      return 'Yerine yenisi geçti'
    default:
      return state
  }
}

export function sourceLabel(source: string): string {
  switch (source) {
    case SALES_POLICY_SOURCE.compiledBaselineV0:
      return 'Başlangıç kataloğu'
    case SALES_POLICY_SOURCE.userMaintained:
      return 'Ayarlar kaydı'
    default:
      return source
  }
}

export function percentLabel(percent: string): string {
  return `%${percent}`
}
