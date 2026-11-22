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
  // AK-3b (KLN-D119): a bulk receipt of a piece-unit line — pieces per label, theoretical kg instead of the line remainder.
  pieceCount: 'Adet',
  theoreticalKg: 'Teorik (kg)',
  pieceBatchNote: 'Adetli satır: fark yalnız uyarıdır; bu mal kabul onay beklemeden stoğa alınmıştır.',
  submitFailed: (code: string | number) => `Toplu mal kabul kaydedilmedi (${code}).`,
  labelProblem: (labelNo: number, text: string) => `Etiket ${labelNo}: ${text}`,
} as const

/** AK-3b: the variance outcome of a piece batch in user words — a warning only, never "onay gerekir". */
export const GOODS_RECEIPT_PIECE_VARIANCE_TEXT: Readonly<Record<string, string>> = {
  Exact: 'Fark yok',
  WithinTolerance: 'Tolerans içinde',
  OutsideTolerance: 'Tolerans dışında (uyarı)',
  ToleranceNotSet: 'Tolerans tanımlı değil (bilgi)',
}

/** AK-3b: the bulk receipt submit refusals this slice names, in user words; any other code keeps the generic text with the code. */
export const GOODS_RECEIPT_BATCH_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  purchasing_receipt_piece_count_invalid: 'Adetli satırda her etiket için tam ve pozitif bir adet girilmelidir.',
  purchasing_acceptance_measured_kg_mismatch: 'Bu satır kilogramla alınır; etiketlere adet gönderilemez.',
  purchasing_receipt_theoretical_kilograms_missing: 'Adetli satırda kayıtlı teorik kg/adet yok; toplu mal kabul yapılamaz. Sipariş satırının kg/adet kaynağı tamamlanmalıdır.',
  purchasing_goods_receipt_batch_exceeds_remaining: 'Etiketlerin toplamı satırın kalan miktarını aşıyor. Güncel kalanı okuyup yeniden deneyin.',
}

/**
 * The Turkish messages of a refused bulk receipt submit: one per field problem the server named on a label
 * (`labels[i].acceptedQuantity` → "Etiket i+1: …"), otherwise the problem code's own text, otherwise the generic text with the
 * code or the status. Nothing is inferred beyond the server's code and field.
 */
export function goodsReceiptBatchProblemMessages(status: number, code: string | null,
  fields: readonly { readonly field: string; readonly code: string }[]): readonly string[] {
  const messages: string[] = []
  for (const problem of fields) {
    const match = /^labels\[(0|[1-9][0-9]{0,2})\]\.acceptedQuantity$/.exec(problem.field)
    if (match !== null && Object.hasOwn(GOODS_RECEIPT_BATCH_PROBLEM_TEXT, problem.code)) {
      messages.push(GOODS_RECEIPT_BATCH_TEXT.labelProblem(Number(match[1]) + 1, GOODS_RECEIPT_BATCH_PROBLEM_TEXT[problem.code]))
    }
  }
  if (messages.length > 0) return messages
  return [code !== null && Object.hasOwn(GOODS_RECEIPT_BATCH_PROBLEM_TEXT, code)
    ? GOODS_RECEIPT_BATCH_PROBLEM_TEXT[code] : GOODS_RECEIPT_BATCH_TEXT.submitFailed(code ?? status)]
}

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
