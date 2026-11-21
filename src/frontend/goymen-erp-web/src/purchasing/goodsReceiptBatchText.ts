/**
 * F-027 (ALC-S16; KLN-D41, KLN-D43, KLN-D45): Turkish texts of the bulk goods receipt section. The screen shows the server's
 * totals, difference, variance outcome and tolerance snapshot as received; the tolerance itself comes from Ayarlar and the
 * screen never recomputes it.
 */
export const GOODS_RECEIPT_BATCH_TEXT = {
  heading: 'Toplu mal kabuller',
  note: 'Bir mal kabul birden çok etiket içerir; toplam kg siparişle karşılaştırılır. Tolerans Ayarlar\'dan gelir: tolerans içindeki fark uyarıyla kabul edilir, dışındaki fark veya tolerans tanımlı değilse her fark onaycı bekler.',
  show: 'Toplu mal kabulleri göster',
  hide: 'Toplu mal kabulleri gizle',
  subject: 'toplu mal kabuller',
  empty: 'Bu sipariş için toplu mal kabul yok.',
  windowFull: 'Liste üst sınıra ulaştı; daha eski kayıtlar gösterilmiyor.',
  batchHeading: (index: number) => `Toplu mal kabul ${index}`,
  status: 'Durum',
  version: 'Sürüm',
  expectedKg: 'Beklenen (kg)',
  receivedKg: 'Teslim alınan (kg)',
  differenceKg: 'Fark (kg)',
  variance: 'Fark değerlendirmesi',
  tolerance: 'Tolerans (%)',
  toleranceNotSet: 'Tanımlı değil',
  acknowledged: 'Uyarı onaylandı',
  yes: 'Evet',
  no: 'Hayır',
  occurredAtUtc: 'Kabul zamanı (UTC)',
  preparedBy: 'Hazırlayan',
  decidedBy: 'Karar veren',
  decidedAtUtc: 'Karar zamanı (UTC)',
  decisionReason: 'Red gerekçesi',
  labelsCaption: (index: number) => `Toplu mal kabul ${index} etiketleri`,
  labelNo: 'Sıra',
  labelCode: 'Etiket kodu',
  labelCodeSource: 'Kod kaynağı',
  supplierLabelNumber: 'Tedarikçi etiket no',
  measuredKg: 'Ölçülen (kg)',
  heatNumber: 'Döküm no',
  coilNumber: 'Bobin no',
  none: '—',
} as const

export const GOODS_RECEIPT_BATCH_STATUS_TEXT: Readonly<Record<string, string>> = {
  PendingApproval: 'Onay bekliyor',
  Posted: 'Stoğa alındı',
  Rejected: 'Reddedildi',
}

export const GOODS_RECEIPT_VARIANCE_TEXT: Readonly<Record<string, string>> = {
  Exact: 'Fark yok',
  WithinTolerance: 'Tolerans içinde (uyarı)',
  OutsideTolerance: 'Tolerans dışında (onay gerekir)',
  ToleranceNotSet: 'Tolerans tanımlı değil (onay gerekir)',
}

export const GOODS_RECEIPT_LABEL_CODE_SOURCE_TEXT: Readonly<Record<string, string>> = {
  SupplierLabelNumber: 'Tedarikçi etiket no',
  Entered: 'Girilen',
  Generated: 'Sistem üretti',
}
