/**
 * Ayarlar > Ana Veriler > Kurlar — every user-facing text of the exchange rate screen (F-025 / B-11; KUR_GUNU, SAT-D55,
 * SAT-D169…D172, SAT-D188). Kept here, never inline, so the screen stays localizable.
 */

import {
  EXCHANGE_RATE_INPUT,
  EXCHANGE_RATE_PROBLEM,
  EXCHANGE_RATE_UNAVAILABLE,
  type BulletinState,
  type HalfDayState,
  type ImportOutcome,
  type Provenance,
  type RecordKind,
} from './exchangeRateContract'

export const EXCHANGE_RATE_TEXT = {
  title: 'Kurlar',
  intro:
    'TCMB döviz satış kurları gün gün. Kur yalnız sunucunun tek kur hesaplayıcısından gelir; bir gün için kur yoksa işlem durur, eski veya varsayılan bir kur kullanılmaz.',
  contextSubject: 'kurlar',
  listSubject: 'kur listesi',
  rangeHeading: 'Tarih aralığı',
  fromLabel: 'Başlangıç',
  toLabel: 'Bitiş',
  rangeHelp: `En fazla ${EXCHANGE_RATE_INPUT.maximumListDays} gün.`,
  show: 'Göster',
  rangeInvalid: `Başlangıç bitişten sonra olamaz ve aralık en fazla ${EXCHANGE_RATE_INPUT.maximumListDays} gün olabilir.`,
  bulletinSourceDisabled: 'TCMB bülten kaynağı kapalı: yeni kur alınamaz, yeniden doğrulama yapılamaz.',
  halfDaySourceDisabled: 'Yarım gün kaynağı kapalı: yarım gün bilgisi elle işaretlenmeden belirsiz kalır.',
  tableCaption: (from: string, to: string) => `${formatDate(from)} – ${formatDate(to)} kurları`,
  columnDay: 'Gün',
  columnBulletin: 'TCMB bülteni',
  columnHalfDay: 'Yarım gün',
  columnRates: 'Satış kurları',
  columnActions: 'İşlemler',
  noRates: '—',
  manualBadge: 'elle düzeltildi',
  provenanceOf: (provenance: string) => `(${provenance})`,
  rateOf: (currency: string, rate: string) => `${currency} ${rate}`,
  historySummary: (count: number) => `Kayıt geçmişi (${count})`,
  historyKindBulletin: 'Bülten',
  historyKindHalfDay: 'Yarım gün',
  historyEntry: (kind: string, state: string, provenance: string, by: string, at: string) =>
    `${kind}: ${state} — ${provenance}, ${by}, ${at}`,
  historyReason: (reason: string) => `Gerekçe: ${reason}`,
  historySource: (reference: string) => `Kaynak: ${reference}`,
  correct: 'Kuru düzelt',
  markHalfDay: 'Yarım gün işaretle',
  reverify: 'Yeniden doğrula',
  correctHeading: (date: string) => `${formatDate(date)} kur düzeltmesi`,
  markHeading: (date: string) => `${formatDate(date)} yarım gün işareti`,
  reverifyHeading: (date: string) => `${formatDate(date)} yeniden doğrulama`,
  reverifyIntro: 'Gün TCMB kaynağından yeniden alınmak üzere açılır; mevcut kayıt silinmez, geçmişte kalır.',
  currencyLabel: 'Para birimi',
  currencyHelp: 'O günün yayımlanmış kurlarından biri.',
  rateLabel: 'Satış kuru',
  rateHelp: `0'dan büyük, en çok ${EXCHANGE_RATE_INPUT.rateMaximumIntegerDigits} tam ve ${EXCHANGE_RATE_INPUT.rateMaximumScale} ondalık basamak, ondalık ayırıcı nokta (örn. 34.1234).`,
  rateInvalid: 'Kur geçersiz.',
  markStateLegend: 'Gün türü',
  reasonLabel: 'Gerekçe',
  reasonHelp: `Zorunlu, en fazla ${EXCHANGE_RATE_INPUT.reasonMaximumLength} karakter; kayıt geçmişinde saklanır.`,
  save: 'Kaydet',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  cancel: 'Vazgeç',
  saved: 'Kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; aynı sonuç gösteriliyor.',
  conflict: 'Gün siz açtıktan sonra değişti; güncel hâli yüklendi, yeniden deneyin.',
  unknownOutcome:
    'İşlemin sonucu bilinmiyor (bağlantı, sunucu veya kapalı kaynak). Aynı işlemi yeniden deneyin; iki kez uygulanmaz.',
  importHeading: 'TCMB’den kur al',
  importIntro: `Seçilen günler için bülten ve yarım gün bilgisi açık kaynaklardan alınır; en fazla ${EXCHANGE_RATE_INPUT.maximumImportDays} gün.`,
  importOpen: 'Kur al',
  importSubmit: 'Al',
  importing: 'Alınıyor…',
  importRangeInvalid: `Başlangıç bitişten sonra olamaz ve aralık en fazla ${EXCHANGE_RATE_INPUT.maximumImportDays} gün olabilir.`,
  importSummaryCaption: 'Kur alma sonucu',
  importSummaryEmpty: 'Bu aralıkta alınacak gün yok.',
  importColumnBulletin: 'Bülten',
  importColumnHalfDay: 'Yarım gün',
  resolutionHeading: 'Kur sorgula',
  resolutionIntro:
    'Bir işlem gününde kullanılacak kuru sunucunun kur hesaplayıcısı verir (yarım gün kuralı dahil). Kur yoksa işlem durur.',
  rateDayLabel: 'İşlem günü',
  resolutionCurrencyLabel: 'Para birimi',
  resolutionCurrencyHelp: 'Üç harfli ISO 4217 kodu (örn. USD); TRY olamaz.',
  resolutionInputInvalid: 'Geçerli bir gün ve TRY dışında üç harfli bir para birimi girin.',
  resolve: 'Sorgula',
  resolving: 'Sorgulanıyor…',
  resolutionSubject: 'kur sorgusu',
  resolutionRejected: 'Sorgu reddedildi; gün ve para birimini kontrol edin.',
  resolvedRate: (currency: string, rate: string) => `${currency} satış kuru: ${rate}`,
  resolvedBulletin: (date: string) => `Kullanılan bülten: ${formatDate(date)}`,
  resolvedProvenance: (provenance: string) => `Kaynak: ${provenance}`,
  halfDayRuleApplied: (previous: string | null) =>
    previous === null
      ? 'Yarım gün kuralı uygulandı.'
      : `Yarım gün kuralı uygulandı: önceki tam iş günü ${formatDate(previous)}.`,
  unavailable: 'Kur yok — işlem durur.',
  unavailableStoppedAt: (date: string) => `Durduğu gün: ${formatDate(date)}`,
} as const

export const BULLETIN_STATE_LABEL: Record<BulletinState, string> = {
  Published: 'Yayımlandı',
  NoBulletin: 'Bülten yok (tatil)',
  Unverified: 'Doğrulanmadı',
  NotFound: 'Bulunamadı',
  NotRecorded: 'Kayıt yok',
}

export const HALF_DAY_STATE_LABEL: Record<HalfDayState, string> = {
  FullDay: 'Tam gün',
  HalfDay: 'Yarım gün',
  Undetermined: 'Belirsiz',
}

export const PROVENANCE_LABEL: Record<Provenance, string> = {
  Official: 'resmî kaynak',
  Manual: 'elle',
}

export const RECORD_KIND_LABEL: Record<RecordKind, string> = {
  Bulletin: EXCHANGE_RATE_TEXT.historyKindBulletin,
  HalfDay: EXCHANGE_RATE_TEXT.historyKindHalfDay,
}

export const IMPORT_OUTCOME_LABEL: Record<ImportOutcome, string> = {
  Recorded: 'Kaydedildi',
  Unchanged: 'Değişmedi',
  SourceDisabled: 'Kaynak kapalı',
}

/** A record's state label; an off-list state is shown as sent (the decoder already bounded it). */
export function recordStateLabel(kind: RecordKind, state: string): string {
  if (kind === 'Bulletin') return (BULLETIN_STATE_LABEL as Record<string, string>)[state] ?? state
  return (HALF_DAY_STATE_LABEL as Record<string, string>)[state] ?? state
}

/** dd.MM.yyyy from the wire's yyyy-MM-dd day (no time zone arithmetic). */
export function formatDate(date: string): string {
  const [year, month, day] = date.split('-')
  return `${day}.${month}.${year}`
}

const INSTANT_FORMAT = new Intl.DateTimeFormat('tr-TR', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Istanbul',
})

/** A recorded instant in Türkiye time. */
export function formatInstant(instant: string): string {
  return INSTANT_FORMAT.format(new Date(instant))
}

export function exchangeRateRefusalText(status: number, code: string | null): string {
  switch (code) {
    case EXCHANGE_RATE_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case EXCHANGE_RATE_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${EXCHANGE_RATE_INPUT.reasonMaximumLength} karakter olabilir.`
    case EXCHANGE_RATE_PROBLEM.versionConflict:
      return EXCHANGE_RATE_TEXT.conflict
    case EXCHANGE_RATE_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açın.'
    case EXCHANGE_RATE_PROBLEM.dayNotPublished:
      return 'Bu gün için yayımlanmış resmî bülten yok; kur yalnız yayımlanmış günde düzeltilebilir.'
    case EXCHANGE_RATE_PROBLEM.halfDayDetermined:
      return 'Günün yarım gün durumu resmî kaynakça belirlenmiş; elle değiştirilemez.'
    case EXCHANGE_RATE_PROBLEM.markFutureDay:
      return 'İleri bir gün işaretlenemez.'
    case EXCHANGE_RATE_PROBLEM.dayNotFinal:
      return 'Gün henüz kesinleşmemiş; yeniden doğrulamaya gerek yok.'
    case EXCHANGE_RATE_PROBLEM.sourceDisabled:
      return 'Kur kaynağı kapalı.'
    case EXCHANGE_RATE_PROBLEM.requestInvalid:
    case EXCHANGE_RATE_PROBLEM.commandMetadataInvalid:
      return 'Girilen değerler geçersiz.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 404) return 'Gün bu şirkette bulunamadı.'
      return 'İşlem reddedildi.'
  }
}

/** Why the calculator gave no rate; an unknown code still means "no rate" with a generic reason. */
export function unavailableReasonText(code: string): string {
  switch (code) {
    case EXCHANGE_RATE_UNAVAILABLE.halfDayUndetermined:
      return 'Günün yarım gün olup olmadığı bilinmiyor; yarım gün bilgisi alınmalı veya elle işaretlenmeli.'
    case EXCHANGE_RATE_UNAVAILABLE.dayUnconfirmed:
      return 'Gerekli günün bülteni alınmamış veya doğrulanmamış.'
    case EXCHANGE_RATE_UNAVAILABLE.currencyMissing:
      return 'En yakın yayımlanmış bültende bu para biriminin kuru yok.'
    case EXCHANGE_RATE_UNAVAILABLE.manualConflict:
      return 'Günün elle girilmiş kayıtları çelişiyor; kayıt geçmişini kontrol edin.'
    default:
      return 'Kur hesaplanamadı.'
  }
}
