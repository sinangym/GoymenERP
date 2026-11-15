/**
 * URT-S10 (F-010b) termin suggestion text, in Turkish and in its own file. The suggestion is the server's: computed from
 * the work center's shift calendar and the hourly capacity in the plan's parameter profile. It is never applied by
 * itself; the user takes it into the termin field and saves it like a hand-entered termin, or types another day. The
 * line is assumed free from now on (no queue of other work is counted); these words say so.
 */

export const DUE_DATE_SUGGESTION_TEXT = {
  subject: 'Termin önerisi',
  request: 'Termin önerisi al',
  refresh: 'Öneriyi yenile',
  heading: 'Termin önerisi',
  help: 'Öneri iş merkezinin vardiya takviminden ve parametre profilindeki saatlik kapasiteden, işe şimdi başlandığı ve hattın boş olduğu varsayımıyla hesaplanır. Kaydedilmez; kullanmak için alana alın ve termini kaydedin.',
  suggested: 'Önerilen termin',
  completesAt: 'Tahmini bitiş (takvim saati)',
  requiredKg: 'Planlanan giriş',
  requiredTime: 'Gereken çalışma süresi',
  minutes: 'dk',
  capacity: 'Saatlik kapasite',
  capacityUnit: 'kg/saat',
  profile: 'parametre profili sürümü',
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
    'Planın parametre profilinde saatlik kapasite (kg/saat) tanımlı değil; öneri için Ayarlar’da tanımlanmalıdır.',
  capacity_invalid: 'Parametre profilindeki saatlik kapasite geçerli değil (birim kg/saat ve pozitif bir sayı olmalı).',
  capacity_source_unavailable: 'Parametre profili şu anda okunamadı; daha sonra yeniden deneyin.',
  calendar_unavailable: 'İş merkezinin vardiya takvimi şu anda okunamadı; daha sonra yeniden deneyin.',
  calendar_answer_invalid: 'İş merkezinin vardiya takvimi beklenen biçimde değil.',
  work_center_not_found: 'Planın iş merkezi bu şirket bağlamında bulunamadı.',
  no_working_time: 'Takvimde bu işi bitirecek kadar çalışma zamanı yok (arama sınırı içinde).',
  calendar_invalid: 'Vardiya takviminde geçersiz bir vardiya var.',
  overnight_holiday_spill_undecided:
    'İş, tatile taşan bir gece vardiyası saatine ihtiyaç duyuyor; bu durumun politikası henüz karara bağlanmadı. Termini elle girin.',
}

/** Why no termin is suggested, in the user's terms; an unknown reason names its code. */
export function dueDateSuggestionReasonText(reasonCode: string | null): string {
  if (reasonCode === null) return DUE_DATE_SUGGESTION_TEXT.unavailable
  return REASON_TEXT[reasonCode] ?? `${DUE_DATE_SUGGESTION_TEXT.unavailable} (${reasonCode}).`
}
