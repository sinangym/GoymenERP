/**
 * URT-S10 (F-010b) / URT-SC33 termin suggestion text, in Turkish and in its own file. The suggestion is the server's:
 * computed from the work center's shift calendar, the input capacity (kg/h) and the safety buffer in the work center
 * capacity record (KIM-SC33-b), after the remaining work of the plans ordered ahead on the work center (URT-D86..D89,
 * URT-D96, URT-D97, URT-D99). It is never applied by itself; the user takes it into the termin field and saves it like a hand-entered termin, or types another day.
 */

export const DUE_DATE_SUGGESTION_TEXT = {
  subject: 'Termin önerisi',
  request: 'Termin önerisi al',
  refresh: 'Öneriyi yenile',
  heading: 'Termin önerisi',
  help: 'Öneri iş merkezinin vardiya takviminden, iş merkezi kapasite kaydındaki giriş kapasitesinden (kg/saat) ve güvenlik payından hesaplanır; iş merkezinde sırada önde olan onaylı planların (termin, öncelik puanı, oluşturulma sırasıyla) kalan işi önce sayılır. Gün payı iş günü olarak sayılır. Vardiya başladığı güne aittir. Kaydedilmez; kullanmak için alana alın ve termini kaydedin.',
  suggested: 'Önerilen termin',
  completesAt: 'Tahmini bitiş (takvim saati)',
  requiredKg: 'Planlanan giriş',
  requiredTime: 'Gereken çalışma süresi',
  minutes: 'dk',
  queue: 'Sıradaki kalan iş',
  queuePlans: 'plan',
  queueTime: 'Sıradaki işin süresi',
  safetyMargin: 'Güvenlik payı',
  safetyMarginHoursUnit: 'saat',
  safetyMarginDaysUnit: 'iş günü',
  totalTime: 'Toplam çalışma süresi (sıra + plan + pay)',
  capacity: 'Giriş kapasitesi',
  capacityUnit: 'kg/saat',
  profile: 'kayıt sürümü',
  calendar: 'Takvim',
  calendarRevision: 'takvim revizyonu',
  businessDate: 'İş günü',
  use: 'Öneriyi kullan',
  used: 'Önerilen gün termin alanına alındı; kaydetmek için “Termini kaydet”e basın.',
  unavailable: 'Termin önerisi verilemiyor',
  stalePlan: 'Öneri planın daha eski bir sürümü için hesaplandı; öneriyi yenileyin.',
} as const

const SOURCE_TEXT: Record<string, string> = {
  Settings: 'Ayarlar',
  WorkCenterCapacity: 'iş merkezi kapasite kaydı',
  Catalog: 'katalog varsayılanı',
  WorkCenter: 'iş merkezi takvimi',
  Default: 'varsayılan takvim',
}

/** A source name in the user's terms; an unknown one is shown as the server wrote it. */
export function dueDateSuggestionSourceText(source: string | null): string {
  if (source === null) return '—'
  return SOURCE_TEXT[source] ?? source
}

const REASON_TEXT: Record<string, string> = {
  plan_cancelled: 'Plan iptal edilmiş; termin önerilmez.',
  capacity_not_configured:
    'Termin hesaplanamadı: iş merkezinin giriş kapasitesi (kg/saat) Ayarlar’da tanımlı değil.',
  capacity_invalid: 'İş merkezi kapasite kaydındaki giriş kapasitesi geçerli değil (kg/saat cinsinden pozitif bir sayı olmalı).',
  safety_margin_invalid:
    'İş merkezi kapasite kaydındaki güvenlik payı geçerli değil (saat payı sıfır veya pozitif, gün payı sıfır veya pozitif tam iş günü olmalı).',
  queue_unavailable: 'İş merkezinde sırada bekleyen iş şu anda okunamadı; daha sonra yeniden deneyin.',
  due_date_input_invalid: 'Termin hesabının girdileri geçerli değil.',
  capacity_source_unavailable: 'İş merkezi kapasite kaydı şu anda okunamadı; daha sonra yeniden deneyin.',
  calendar_unavailable: 'İş merkezinin vardiya takvimi şu anda okunamadı; daha sonra yeniden deneyin.',
  calendar_answer_invalid: 'İş merkezinin vardiya takvimi beklenen biçimde değil.',
  work_center_not_found: 'Planın iş merkezi bu şirket bağlamında bulunamadı.',
  no_working_time: 'Takvimde bu işi bitirecek kadar çalışma zamanı yok (arama sınırı içinde).',
  calendar_invalid: 'Vardiya takviminde geçersiz bir vardiya var.',
}

/** Why no termin is suggested, in the user's terms; an unknown reason names its code. */
export function dueDateSuggestionReasonText(reasonCode: string | null): string {
  if (reasonCode === null) return DUE_DATE_SUGGESTION_TEXT.unavailable
  return REASON_TEXT[reasonCode] ?? `${DUE_DATE_SUGGESTION_TEXT.unavailable} (${reasonCode}).`
}
