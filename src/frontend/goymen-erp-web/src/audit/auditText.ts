/**
 * User-facing Turkish text of "Denetim kayıtları", in one file so specs and tests read the same strings the screen
 * renders. Technical names (event types, entity types, permission codes, ids) are shown as-is, never translated.
 */

export const AUDIT_TEXT = {
  heading: 'Denetim kayıtları',
  lead:
    'Seçili şirkette yapılan kayıtlı işlemler, en yenisi üstte. Yalnız görüntüleme: denetim kaydı değiştirilemez ' +
    've silinemez.',
  subject: 'denetim kayıtları',
  tableLabel: 'Denetim kayıtları',
  filtersLegend: 'Süz',
  correlationLabel: 'Korelasyon kimliği',
  correlationHelp: 'Aynı isteğe ait bütün kayıtlar.',
  entityTypeLabel: 'Kayıt tipi',
  entityTypeHelp: 'Teknik ad, örn. Organization.Site.',
  entityIdLabel: 'Kayıt kimliği',
  apply: 'Süz',
  clear: 'Süzmeyi kaldır',
  tooLong: (max: number) => `En çok ${max} karakter.`,
  columnTime: 'Zaman',
  columnEvent: 'Olay',
  columnEntity: 'Kayıt',
  columnOperation: 'İşlem',
  columnVersion: 'Sürüm',
  columnActor: 'Yapan',
  columnPermission: 'Yetki',
  columnChannel: 'Kanal',
  columnAi: 'Yapay zekâ',
  columnOutcome: 'Sonuç',
  columnCorrelation: 'Korelasyon',
  filterByEntity: 'Bu kaydın geçmişi',
  filterByCorrelation: 'Bu isteğin kayıtları',
  pagerLabel: 'Denetim kayıtları sayfaları',
  newer: 'En yeni kayıtlara dön',
  older: 'Daha eski kayıtlar',
  noOlder: 'Daha eski kayıt yok.',
  invalidQueryTitle: 'Sayfa bilgisi geçersiz',
  invalidQueryDetail: 'Denetim listesini ilk sayfadan yeniden açın.',
  firstPage: 'İlk sayfayı aç',
  hiddenFieldsNote:
    'Önceki/sonraki değerler, gerekçe, IP adresi ve tarayıcı bilgisi bu listede gösterilmez; gösterilmeleri ayrı ' +
    'bir saklama ve gizlilik kararı bekliyor.',
  none: '—',
} as const

const ACTOR_TYPE_LABEL: Readonly<Record<string, string>> = {
  User: 'Kullanıcı',
  System: 'Sistem',
  Integration: 'Entegrasyon',
  Ai: 'Yapay zekâ',
}

const AI_INVOLVEMENT_LABEL: Readonly<Record<string, string>> = {
  None: 'Yok',
  Assisted: 'Yardımcı oldu',
  Proposed: 'Önerdi',
  Executed: 'Yürüttü',
}

const OUTCOME_LABEL: Readonly<Record<string, string>> = {
  Succeeded: 'Başarılı',
}

/** A known server token gets its Turkish label; an unknown one is shown verbatim rather than guessed. */
function label(table: Readonly<Record<string, string>>, token: string): string {
  return Object.prototype.hasOwnProperty.call(table, token) ? table[token] : token
}

export const actorTypeLabel = (token: string) => label(ACTOR_TYPE_LABEL, token)
export const aiInvolvementLabel = (token: string) => label(AI_INVOLVEMENT_LABEL, token)
export const outcomeLabel = (token: string) => label(OUTCOME_LABEL, token)

/** `3 → 4`, `— → 1` (created), `4 → —` (no after version); both absent is a dash. */
export function versionLabel(before: string | null, after: string | null): string {
  if (before === null && after === null) return AUDIT_TEXT.none
  return `${before ?? AUDIT_TEXT.none} → ${after ?? AUDIT_TEXT.none}`
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

/** Business display in Türkiye time; the stored instant is UTC and stays in the `<time dateTime>` attribute. */
export function formatInstant(value: string): string {
  return DATE.format(new Date(value))
}
