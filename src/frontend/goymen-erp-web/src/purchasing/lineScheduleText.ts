/**
 * F-027 (ALS-D94, ALS-D81): Turkish texts of the read-only line delivery schedule section. The screen shows the server's
 * schedule and its recorded revisions as received; it never edits or recomputes a termin (the edit control is a separate slice).
 */
export const LINE_SCHEDULE_TEXT = {
  heading: 'Teslim terminleri',
  note: 'Onaylı siparişte termin değişikliği yeniden onay istemez; her değişiklik kaydedeni ve zamanıyla aşağıdaki geçmişte görünür.',
  lineHeading: (lineNo: number) => `Satır ${lineNo}`,
  currentCaption: (lineNo: number) => `Satır ${lineNo} güncel teslim termini`,
  currentEmpty: 'Bu satır için kaydedilmiş teslim termini yok.',
  currentUnavailable: 'Bu sipariş yanıtı teslim terminini içermiyor.',
  sliceNo: 'Dilim',
  quantity: 'Miktar',
  deliveryDate: 'Teslim tarihi',
  showHistory: (lineNo: number) => `Satır ${lineNo} termin geçmişini göster`,
  hideHistory: (lineNo: number) => `Satır ${lineNo} termin geçmişini gizle`,
  historySubject: 'termin geçmişi',
  historyEmpty: 'Bu satır için kaydedilmiş termin revizyonu yok.',
  historyReadAt: (version: string) => `Geçmiş, siparişin ${version} sürümüyle birlikte okundu.`,
  historyNewer: 'Sipariş bu sayfa açıldıktan sonra değişti; güncel kaydı görmek için siparişi yeniden okuyun.',
  revisionHeading: (version: string) => `Sipariş sürümü ${version}`,
  revisionCaption: (lineNo: number, version: string) => `Satır ${lineNo}, sürüm ${version} teslim termini`,
  recordedBy: 'Kaydeden',
  recordedAtUtc: 'Kayıt zamanı (UTC)',
  currentMarker: 'Güncel',
} as const
