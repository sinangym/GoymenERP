/**
 * Turkish texts of the Ayarlar > Masraf ve hizmet tipleri and Ayarlar > Boya rengi ve hurda türü screens (API-98, B-14;
 * KIM-D49, KIM-D51). Every user-facing string lives here; the screens compose nothing from a server's localized title or
 * detail and suggest no list entry: every list starts empty.
 */

import { REFERENCE_LIST_INPUT, REFERENCE_LIST_PROBLEM, type ReferenceListId } from './referenceListContract'

export const REFERENCE_LIST_GROUP_TEXT = {
  purchasing: {
    title: 'Masraf ve hizmet tipleri',
    intro:
      'Şirketin masraf tipi ve hizmet tipi listeleri. Listeler boş başlar; hiçbir değer hazır gelmez. Satış fasonu bir ' +
      'işlem türüdür, hizmet tipi listesine girmez.',
    subject: 'masraf ve hizmet tipleri',
  },
  production: {
    title: 'Boya rengi ve hurda türü',
    intro: 'Şirketin boya rengi ve hurda türü listeleri. Listeler boş başlar; hiçbir değer hazır gelmez.',
    subject: 'boya rengi ve hurda türü listeleri',
  },
} as const

export const REFERENCE_LIST_LABEL: Record<ReferenceListId, string> = {
  expenseType: 'Masraf tipleri',
  serviceType: 'Hizmet tipleri',
  paintColor: 'Boya renkleri',
  scrapKind: 'Hurda türleri',
}

export const REFERENCE_LIST_TEXT = {
  empty: 'Bu listede henüz kayıt yok. "Yeni kayıt ekle" ile ilk kaydı oluşturabilirsiniz.',
  columnCode: 'Kod',
  columnName: 'Ad',
  columnStatus: 'Durum',
  columnVersion: 'Sürüm',
  columnRecorded: 'Kaydeden / zaman',
  columnActions: 'İşlem',
  active: 'Aktif',
  inactive: 'Pasif',
  add: 'Yeni kayıt ekle',
  addHeading: (list: string) => `${list} — yeni kayıt`,
  edit: 'Düzenle',
  editHeading: (code: string) => `${code} — yeni sürüm`,
  singleDeciderNote:
    'Değişiklik ikinci bir onaya gitmez; yetkili kişi kaydettiği anda yürürlüğe girer ve gerekçesiyle birlikte denetim ' +
    'kaydına geçer. Kayıt silinmez; kullanılmayacaksa pasif yapılır.',
  codeLabel: 'Kod',
  codeHelp:
    `Zorunlu; büyük harf (A–Z), rakam ve - _ . işaretleri, harf ya da rakamla başlar, en çok ` +
    `${REFERENCE_LIST_INPUT.codeMaximumLength} karakter. Kod sonradan değiştirilmez.`,
  codeError: 'Kod yalnız büyük harf, rakam ve - _ . içerebilir ve harf ya da rakamla başlamalıdır.',
  nameLabel: 'Ad',
  nameHelp: `Zorunlu; en çok ${REFERENCE_LIST_INPUT.nameMaximumLength} karakter.`,
  nameError: 'Ad boş olamaz ve satır sonu ya da sekme içeremez.',
  activeLabel: 'Aktif (yeni kayıtlarda seçilebilir)',
  reasonLabel: 'Gerekçe',
  reasonHelp: `Zorunlu; en çok ${REFERENCE_LIST_INPUT.reasonMaximumLength} karakter.`,
  save: 'Kaydet',
  cancel: 'Vazgeç',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  saved: 'Değişiklik kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; sonuç yeniden gösterildi.',
  unknownOutcome:
    'Sonuç alınamadı; işlem uygulanmış olabilir. "Aynı işlemi yeniden dene" aynı işlem anahtarıyla gönderir ve iki kez uygulanmaz.',
  staleReloaded: 'Kayıt siz açtıktan sonra değişti. Güncel hâli yüklendi; değişikliği yeniden yapın.',
  entryGone: 'Kayıt bu listede bulunamadı. Liste yeniden yüklendi.',
} as const

/** A refusal's text by its stable code (or status when the body is code-less). */
export function referenceListRefusalText(status: number, code: string | null): string {
  switch (code) {
    case REFERENCE_LIST_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case REFERENCE_LIST_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${REFERENCE_LIST_INPUT.reasonMaximumLength} karakter olabilir.`
    case REFERENCE_LIST_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz; alanları kontrol edin.'
    case REFERENCE_LIST_PROBLEM.commandMetadataInvalid:
      return 'İstek bilgileri eksik ya da geçersiz; sayfayı yenileyip tekrar deneyin.'
    case REFERENCE_LIST_PROBLEM.codeConflict:
      return 'Bu kod bu listede zaten kullanılıyor; başka bir kod girin.'
    case REFERENCE_LIST_PROBLEM.entryNotFound:
      return REFERENCE_LIST_TEXT.entryGone
    case REFERENCE_LIST_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açıp tekrar deneyin.'
    case REFERENCE_LIST_PROBLEM.versionConflict:
      return REFERENCE_LIST_TEXT.staleReloaded
    case REFERENCE_LIST_PROBLEM.providerUnavailable:
      return 'Ayarlar hizmeti şu anda yanıt vermiyor; biraz sonra tekrar deneyin.'
    case REFERENCE_LIST_PROBLEM.profileInvalid:
      return 'Kayıtlı değer okunamadı; sistem yöneticisine bildirin.'
    case REFERENCE_LIST_PROBLEM.accessDenied:
      return 'Bu şirkette bu işlem için yetkiniz yok.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 415) return 'İstek biçimi kabul edilmedi.'
      return 'İşlem kabul edilmedi.'
  }
}
