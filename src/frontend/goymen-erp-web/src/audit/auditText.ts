/**
 * Every user-facing word of "Denetim kaydı" (UI-K02), Turkish, in one place (TEK-03). Codes coming from the server
 * (event type, entity type, operation, permission code, actor type) are shown as the server stored them: this screen
 * does not translate or reinterpret an audit fact.
 */

import type { AuditFilterKey } from './capabilities'
import type { AuditOutcome, RefusalClass } from './auditQueryOutcome'

export const AUDIT_TEXT = {
  heading: 'Denetim kaydı',
  lead: 'Şirketin denetim olayları, en yeni önce. Yalnız okunur: bir kayıt bu ekrandan değiştirilemez veya silinemez.',
  maskNote:
    'Gizlilik: IP adresi ve tarayıcı bilgisi gösterilmez. Ayrıntı yüklerinde IBAN, VKN/TCKN (son 4 hane hariç), telefon, e-posta ve adres maskelenmiş gösterilir.',
  subject: 'Denetim kaydı',
  contextSubject: 'Denetim kaydı',

  filtersHeading: 'Filtre',
  filtersHelp: 'Değerler tam eşleşmeyle aranır; boş bırakılan alan filtrelenmez.',
  filterApply: 'Filtrele',
  filterClear: 'Filtreyi temizle',
  filterTooLong: (maxLength: number) => `En fazla ${maxLength} karakter girilebilir.`,
  filterOutcome: 'Sonuç',
  filterOutcomeHelp: 'Reddedilen: iş kuralı, doğrulama, eşzamanlılık veya işlem anahtarı nedeniyle reddedilen denemeler.',
  filterOutcomeAll: 'Tümü',
  invalidFilterTitle: 'Filtre geçersiz',
  invalidFilterDetail: 'Sunucu bu filtreyi kabul etmedi. Değerleri kontrol edip yeniden deneyin veya filtreyi temizleyin.',

  tableCaption: 'Denetim olayları',
  columns: {
    time: 'Zaman',
    eventType: 'Olay',
    entity: 'Kayıt',
    operation: 'İşlem',
    version: 'Sürüm',
    actor: 'İşlemi yapan',
    permission: 'Yetki',
    reason: 'Gerekçe',
    correlation: 'Korelasyon',
    outcome: 'Sonuç',
  },
  versionArrow: '→',
  noValue: '—',
  systemWithoutPermission: 'Sistem kurtarma komutu',

  details: 'Ayrıntı',
  before: 'Önceki değer',
  after: 'Sonraki değer',
  approvalContext: 'Onay bağlamı',
  noApprovalContext: 'Onay bağlamı yok.',
  unreadablePayload: 'Bu yük JSON olarak okunamadı; maskelenemediği için gösterilmiyor.',

  refusalDetail: 'Ret ayrıntısı',
  refusalClass: 'Ret sınıfı',
  problemCode: 'Sorun kodu',
  httpStatusLabel: 'HTTP durumu',
  httpStatus: (status: number) => `HTTP ${status}`,
  fieldErrors: 'Alan hataları',
  noFieldErrors: 'Alan hatası kaydedilmedi.',
  refusalSummary: (count: number, windowStart: string, windowEnd: string) =>
    `${count} kez reddedildi (pencere: ${windowStart} – ${windowEnd})`,

  shownCount: (count: number) => `${count} olay gösteriliyor.`,
  end: 'Listenin sonu.',
  loadMore: 'Daha fazla',
  loadingMore: 'Yükleniyor…',
  continuationUnavailable: 'Sonraki sayfa getirilemedi. Gösterilen olaylar korunuyor.',
  continuationMalformed: 'Sonraki sayfanın yanıtı beklenen biçimde değil. Gösterilen olaylar korunuyor.',
  retry: 'Tekrar dene',
  cursorReset: 'Sayfa bilgisi artık geçerli değil; liste ilk sayfadan yeniden açıldı.',
} as const

export const FILTER_LABEL: Record<AuditFilterKey, string> = {
  correlationId: 'Korelasyon kimliği',
  entityType: 'Kayıt türü',
  entityId: 'Kayıt kimliği',
}

/** The "Sonuç" filter options; the value is the server's outcome, the label what the user reads. */
export const OUTCOME_FILTER_LABEL: Record<AuditOutcome, string> = {
  Succeeded: 'Başarılı',
  Refused: 'Reddedilen',
}

/** The outcome of one row; a text label, never colour alone. */
export const OUTCOME_LABEL: Record<AuditOutcome, string> = {
  Succeeded: 'Başarılı',
  Refused: 'Reddedildi',
}

export const REFUSAL_CLASS_LABEL: Record<RefusalClass, string> = {
  Validation: 'Doğrulama',
  BusinessRule: 'İş kuralı',
  Concurrency: 'Eşzamanlılık',
  Idempotency: 'İşlem anahtarı',
  RequestShape: 'İstek biçimi',
}

/**
 * Turkish labels of the problem codes a refused business attempt commonly carries. A code not listed here is shown
 * as the server stored it: this screen does not guess the meaning of a code it does not know.
 */
const PROBLEM_LABEL: Readonly<Record<string, string>> = {
  validation_failed: 'Doğrulama hatası',
  request_invalid: 'İstek geçersiz',
  version_conflict: 'Sürüm çakışması',
  expected_version_invalid: 'Beklenen sürüm geçersiz',
  idempotency_key_invalid: 'İşlem anahtarı geçersiz',
  idempotency_conflict: 'İşlem anahtarı çakışması',
  same_key_payload_mismatch: 'Aynı işlem anahtarıyla farklı istek',
}

/** The Turkish label of a problem code, or `null` when this screen has none (the raw code is then shown alone). */
export function problemLabel(code: string): string | null {
  return Object.prototype.hasOwnProperty.call(PROBLEM_LABEL, code) ? PROBLEM_LABEL[code] : null
}

const DATE = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  timeZone: 'Europe/Istanbul',
})

/** Business display in Türkiye time; the stored instant is UTC and is never re-derived here. */
export function formatInstant(value: string): string {
  return DATE.format(new Date(value))
}
