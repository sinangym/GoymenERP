/**
 * Turkish texts of the Ayarlar > İş merkezi kapasitesi screen (URT-D89). Every user-facing string lives here; the screen
 * composes nothing from a server's localized title or detail and never states a capacity or buffer the server did not send.
 */

import {
  WORK_CENTER_CAPACITY_INPUT,
  WORK_CENTER_CAPACITY_PROBLEM,
  WORK_CENTER_SAFETY_BUFFER_UNIT,
} from './workCenterCapacityContract'

export const WORK_CENTER_CAPACITY_TEXT = {
  title: 'İş merkezi kapasitesi',
  intro:
    'Her iş merkezinin saatte işleyebildiği reçete girdisi (kg/saat) ve isteğe bağlı termin güvenlik payı. Üretim ' +
    'termin önerisi bu değerleri kullanır; kapasitesi kayıtlı olmayan iş merkezinde termin "hesaplanamadı" olur.',
  subject: 'iş merkezi kapasitesi',
  workCentersSubject: 'iş merkezleri',
  tableLabel: 'İş merkezi kapasiteleri',
  empty: 'Bu şirkette tanımlı iş merkezi yok. Önce Organizasyon > İş merkezleri ekranından iş merkezi tanımlanmalıdır.',
  columnCode: 'Kod',
  columnName: 'İş merkezi',
  columnCapacity: 'Kapasite (kg/saat)',
  columnBuffer: 'Güvenlik payı',
  columnVersion: 'Sürüm',
  columnActions: 'İşlem',
  columnCurrent: 'Durum',
  columnRecorded: 'Kaydeden / zaman',
  columnReason: 'Gerekçe',
  inactive: 'pasif',
  noCapacity: 'Kayıtlı değil (termin hesaplanamaz)',
  noBuffer: 'Yok (0)',
  noVersion: '—',
  unlisted: 'Listede olmayan iş merkezi',
  isCurrent: 'Yürürlükte',
  isPrevious: 'Önceki sürüm',
  open: 'Geçmiş ve yeni sürüm',
  close: 'Kapat',
  historyHeading: (name: string) => `${name} — kapasite sürümleri`,
  historyEmpty: 'Bu iş merkezi için henüz kayıtlı kapasite yok.',
  newVersion: 'Yeni sürüm kaydet',
  singleDeciderNote:
    'Değişiklik ikinci bir onaya gitmez; yetkili kişi kaydettiği anda yürürlüğe girer ve gerekçesiyle birlikte denetim ' +
    'kaydına geçer.',
  capacityLabel: 'Kapasite (girdi kg/saat)',
  capacityHelp:
    `Zorunlu; sıfırdan büyük, en çok ${WORK_CENTER_CAPACITY_INPUT.capacityMaximumIntegerDigits} tam ve ` +
    `${WORK_CENTER_CAPACITY_INPUT.capacityMaximumScale} ondalık basamak (örn. 1250 veya 1250,5).`,
  capacityError: 'Sıfırdan büyük, geçerli bir sayı girin.',
  bufferLegend: 'Güvenlik payı',
  bufferNone: 'Güvenlik payı yok',
  bufferValueLabel: 'Güvenlik payı değeri',
  bufferHelp:
    `Sıfırdan büyük, en çok ${WORK_CENTER_CAPACITY_INPUT.safetyBufferMaximumIntegerDigits} tam ve ` +
    `${WORK_CENTER_CAPACITY_INPUT.safetyBufferMaximumScale} ondalık basamak. Pay yoksa termine 0 eklenir.`,
  bufferError: 'Sıfırdan büyük, geçerli bir sayı girin.',
  bufferDaysNote:
    'Gün olarak girilen payın nasıl sayılacağı (iş günü ya da takvim günü) termin önerisi tarafında belirlenir; burada ' +
    'değer girildiği birimle saklanır.',
  reasonLabel: 'Gerekçe',
  reasonHelp: `Zorunlu; en çok ${WORK_CENTER_CAPACITY_INPUT.reasonMaximumLength} karakter.`,
  save: 'Kaydet',
  cancel: 'Vazgeç',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  saved: 'Değişiklik kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; sonuç yeniden gösterildi.',
  unknownOutcome:
    'Sonuç alınamadı; işlem uygulanmış olabilir. "Aynı işlemi yeniden dene" aynı işlem anahtarıyla gönderir ve iki kez uygulanmaz.',
  staleReloaded: 'Kayıt siz açtıktan sonra değişti. Güncel hâli yüklendi; değişikliği yeniden yapın.',
  workCenterGone: 'İş merkezi bu şirkette bulunamadı. Liste yeniden yüklendi.',
} as const

export const WORK_CENTER_SAFETY_BUFFER_UNIT_LABEL: Record<string, string> = {
  [WORK_CENTER_SAFETY_BUFFER_UNIT.hours]: 'saat',
  [WORK_CENTER_SAFETY_BUFFER_UNIT.days]: 'gün',
}

export function safetyBufferUnitLabel(unit: string): string {
  return WORK_CENTER_SAFETY_BUFFER_UNIT_LABEL[unit] ?? unit
}

/** A decimal text as a Turkish reader writes it ("1250.5" → "1250,5"); the digits are the server's, never rounded. */
export function decimalLabel(text: string): string {
  return text.replace('.', ',')
}

/** A recorded buffer ("12 saat") or the no-buffer label. */
export function safetyBufferLabel(value: string | null, unit: string | null): string {
  if (value === null || unit === null) return WORK_CENTER_CAPACITY_TEXT.noBuffer
  return `${decimalLabel(value)} ${safetyBufferUnitLabel(unit)}`
}

/** A refusal's text by its stable code (or status when the body is code-less). */
export function workCenterCapacityRefusalText(status: number, code: string | null): string {
  switch (code) {
    case WORK_CENTER_CAPACITY_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case WORK_CENTER_CAPACITY_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${WORK_CENTER_CAPACITY_INPUT.reasonMaximumLength} karakter olabilir.`
    case WORK_CENTER_CAPACITY_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz; alanları kontrol edin.'
    case WORK_CENTER_CAPACITY_PROBLEM.commandMetadataInvalid:
      return 'İstek bilgileri eksik ya da geçersiz; sayfayı yenileyip tekrar deneyin.'
    case WORK_CENTER_CAPACITY_PROBLEM.workCenterNotFound:
      return WORK_CENTER_CAPACITY_TEXT.workCenterGone
    case WORK_CENTER_CAPACITY_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açıp tekrar deneyin.'
    case WORK_CENTER_CAPACITY_PROBLEM.versionConflict:
      return WORK_CENTER_CAPACITY_TEXT.staleReloaded
    case WORK_CENTER_CAPACITY_PROBLEM.providerUnavailable:
      return 'Ayarlar ya da organizasyon hizmeti şu anda yanıt vermiyor; biraz sonra tekrar deneyin.'
    case WORK_CENTER_CAPACITY_PROBLEM.profileInvalid:
      return 'Kayıtlı değer okunamadı; sistem yöneticisine bildirin.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 415) return 'İstek biçimi kabul edilmedi.'
      return 'İşlem kabul edilmedi.'
  }
}
