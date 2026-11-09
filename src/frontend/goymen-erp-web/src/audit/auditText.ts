/**
 * Every user-facing word of "Denetim kaydı" (UI-K02), Turkish, in one place (TEK-03). Codes coming from the server
 * (event type, entity type, operation, permission code, actor type) are shown as the server stored them: this screen
 * does not translate or reinterpret an audit fact.
 */

import type { AuditFilterKey } from './capabilities'

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
  },
  versionArrow: '→',
  noValue: '—',

  details: 'Ayrıntı',
  before: 'Önceki değer',
  after: 'Sonraki değer',
  approvalContext: 'Onay bağlamı',
  noApprovalContext: 'Onay bağlamı yok.',
  unreadablePayload: 'Bu yük JSON olarak okunamadı; maskelenemediği için gösterilmiyor.',

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
