/**
 * Turkish texts of the Ayarlar > Birim profilleri screen (F-029 S1/S2; URN-D2…D5). Every user-facing string lives here; the
 * screen composes nothing from a server's localized title or detail and never states a scale the server did not send.
 */

import { UNIT_PROFILE_INPUT, UNIT_PROFILE_PROBLEM } from './unitProfileContract'

export const UNIT_PROFILE_TEXT = {
  title: 'Birim profilleri',
  intro:
    'Şirketin hangi birimi hangi tarihten itibaren ve kaç ondalık miktar hassasiyetiyle kullanabileceği. Miktar ' +
    'yuvarlaması sunucuda bu kayda göre yapılır; bu ekran yalnız kayıtlı sürümleri gösterir ve yeni sürüm kaydeder.',
  subject: 'birim profilleri',
  empty: 'Bu şirket için tanımlı birim profili yok.',
  unitHeading: 'Birim',
  unitTableLabel: 'Birim profili sürümleri',
  noneInForce: 'Bugün geçerli bir sürüm yok.',
  columnVersion: 'Sürüm',
  columnScale: 'Ondalık',
  columnActive: 'Kullanım',
  active: 'Kullanılabilir',
  inactive: 'Kullanılamaz',
  kilogramNote: `kg için en çok ${UNIT_PROFILE_INPUT.kilogramMaximumScale} ondalık`,
  baselineHeading: 'Başlangıç değerlerini devral',
  baselinePending:
    'Bu şirkette birim profilleri henüz Ayarlar kaydına devredilmedi; aşağıdaki tablolar onaylı başlangıç kataloğunu ' +
    'gösterir ve bütün okumalar bu katalogdan yanıtlanır. Değişiklik yapabilmek için önce başlangıç değerlerini bir kez ' +
    'devralın.',
  baselinePreview:
    'Etki önizlemesi: değerler değişmeden aktarılır; birimler, ondalık sayıları, kullanım durumu ve tarihler aynen ' +
    'kalır. Devirden sonra okumalar yalnız Ayarlar kaydından yanıtlanır.',
  baselineSubmit: 'Başlangıç değerlerini devral',
  baselineDone: 'Başlangıç değerleri devralındı.',
  newVersion: 'Yeni sürüm kaydet',
  unitLabel: 'Birim',
  unitHelp: 'Profil kaydedilebilecek birimler Ayarlar birim kimliklerinden gelir.',
  unitPlaceholder: 'Birim seçin',
  scaleLabel: 'Ondalık sayısı',
  scaleHelpPrefix: 'Miktarın kaç ondalık hassasiyetle tutulacağı. En az 0, en çok',
  scaleError: 'Bu birim için izin verilen aralıkta bir tam sayı girin.',
  activeLegend: 'Kullanım',
  activeOn: 'Kullanılabilir — şirket bu birimi kullanabilir',
  activeOff: 'Kullanılamaz — şirket bu birimi bu tarihten itibaren kullanamaz',
  previewHeading: 'Etki önizlemesi',
  previewCurrentNone: 'Bu birim için bugün geçerli bir sürüm yok.',
  previewCurrentPrefix: 'Bugün geçerli:',
  previewNextPrefix: 'Yeni sürüm:',
  previewFromPrefix: 'Geçerlilik başlangıcı:',
  previewHistory:
    'Daha önce kaydedilmiş belge ve hareketler değişmez; yeni değer yalnız başlangıç tarihinden itibaren kullanılır.',
  previewIncomplete: 'Önizleme için birim, ondalık sayısı ve kullanım durumunu seçin.',
  saved: 'Değişiklik kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; sonuç yeniden gösterildi.',
  unknownOutcome:
    'Sonuç alınamadı; işlem uygulanmış olabilir. "Aynı işlemi yeniden dene" aynı işlem anahtarıyla gönderir ve iki kez uygulanmaz.',
  staleReloaded: 'Kayıt siz açtıktan sonra değişti. Güncel hâli yüklendi; değişikliği yeniden yapın.',
  save: 'Kaydet',
  cancel: 'Vazgeç',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
} as const

export function activeLabel(isActive: boolean): string {
  return isActive ? UNIT_PROFILE_TEXT.active : UNIT_PROFILE_TEXT.inactive
}

/** "3 ondalık · Kullanılabilir". */
export function profileValueLabel(scale: number, isActive: boolean): string {
  return `${scale} ondalık · ${activeLabel(isActive)}`
}

/** "kg (Kütle)". */
export function unitLabel(unitCode: string, dimensionCode: string): string {
  return `${unitCode} (${dimensionCode})`
}

export function scaleHelp(maximumScale: number): string {
  return `${UNIT_PROFILE_TEXT.scaleHelpPrefix} ${maximumScale}.`
}

/** A refusal's text by its stable code (or status when the body is code-less). */
export function unitProfileRefusalText(status: number, code: string | null): string {
  switch (code) {
    case UNIT_PROFILE_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case UNIT_PROFILE_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${UNIT_PROFILE_INPUT.reasonMaximumLength} karakter olabilir.`
    case UNIT_PROFILE_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz; alanları kontrol edin.'
    case UNIT_PROFILE_PROBLEM.commandMetadataInvalid:
      return 'İstek bilgileri eksik ya da geçersiz; sayfayı yenileyip tekrar deneyin.'
    case UNIT_PROFILE_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açıp tekrar deneyin.'
    case UNIT_PROFILE_PROBLEM.versionConflict:
      return UNIT_PROFILE_TEXT.staleReloaded
    case UNIT_PROFILE_PROBLEM.baselineRequired:
      return 'Önce başlangıç değerleri devralınmalı. Güncel hâli yüklendi.'
    case UNIT_PROFILE_PROBLEM.alreadyTransferred:
      return 'Başlangıç değerleri zaten devralınmış. Güncel hâli yüklendi.'
    case UNIT_PROFILE_PROBLEM.effectiveFromPast:
      return 'Başlangıç tarihi bugünden önce olamaz.'
    case UNIT_PROFILE_PROBLEM.unitUnknown:
      return 'Seçilen birim Ayarlar birim kimliklerinde yok.'
    case UNIT_PROFILE_PROBLEM.providerUnavailable:
      return 'Ayarlar hizmeti şu anda yanıt vermiyor; biraz sonra tekrar deneyin.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 415) return 'İstek biçimi kabul edilmedi.'
      return 'İşlem kabul edilmedi.'
  }
}
