/**
 * AK-7 (URT-D112): Turkish texts of the output piece count — the piece field of the output declaration and of the
 * output correction, and the real/theoretical kg per piece deviation table of the order screen. The deviation figures
 * are the server's (`OutputPieceDeviationCalculatorV1`); the screen never recomputes them. No threshold is applied:
 * the deviation threshold is open (URT-D51 (5)).
 */
export const OUTPUT_PIECE_TEXT = {
  pieceCount: 'Adet',
  pieceCountHelp:
    'Adetli üründe zorunludur: sayılan adet ve tartılan kg birlikte kaydedilir. Adetle izlenmeyen üründe boş bırakın.',
  pieceCountFormat: 'Adet, sıfırdan büyük bir tam sayı olmalıdır.',
  correctionPieceCount: 'Geri alınan adet',
  correctionPieceCountHelp: 'Adetli çıktıda zorunludur; yalnız kg düzeltiliyorsa 0 girin.',
  correctionPieceCountFormat: 'Geri alınan adet, sıfır veya pozitif bir tam sayı olmalıdır.',
  correctionPieceCountRequired: 'Bu çıktı adetli; geri alınan adedi girin (yalnız kg düzeltiliyorsa 0).',
  heading: 'Adetli çıktılar: gerçek ve teorik kg/adet',
  caption: 'Adetli çıktıların net adet, net kg ve teorik kg/adete göre sapması',
  thresholdNote: 'Sapma yalnız gösterilir; sapma eşiği henüz karara bağlanmadı (URT-D51), eşik uygulanmaz.',
  output: 'Çıktı',
  outputClass: 'Sınıf',
  declaredPieces: 'Beyan adedi',
  netPieces: 'Net adet',
  netKg: 'Net kg',
  theoreticalKgPerPiece: 'Teorik kg/adet',
  realKgPerPiece: 'Gerçek kg/adet',
  deviationKgPerPiece: 'Sapma kg/adet',
  deviationPercent: 'Sapma %',
  noNetPieces: 'Net adet kalmadı',
} as const

/** The domain and command refusals of the piece side, told by their stable code. */
export const OUTPUT_PIECE_RULE_TEXT: Record<string, string> = {
  manufacturing_output_pieces_required:
    'Bu ürün adetle izleniyor; çıktı adet ve tartılan kg ile beyan edilmelidir. Adedi girip yeniden gönderin.',
  manufacturing_output_pieces_not_applicable:
    'Bu çıktı adetle izlenmiyor (ürün kartında adet birimi yok, proses kaybı ya da adetsiz beyan); adet alanını boş bırakın.',
  manufacturing_output_piece_kg_source_missing:
    'Ürün kartı adet birimini taşıyor ama teorik kg/adet hesaplanamıyor (ölçü, ağırlık tablosu veya politika eksik ya da ' +
    'çelişkili). Kart düzeltilmeden adetli çıktı kaydedilmez.',
  manufacturing_output_piece_reversal_exceeds_declared:
    'Düzeltmelerde geri alınan adet, beyan edilen adedi aşamaz.',
  manufacturing_output_piece_reversal_inconsistent:
    'Düzeltme sonrası net kg ve net adet birlikte sıfıra inmelidir; kg kalırken adet sıfırlanamaz, adet kalırken kg ' +
    'sıfırlanamaz.',
}

/** A signed percent as the server sent it, with a plus for a heavier piece. */
export function signedText(value: string): string {
  return value.startsWith('-') || value === '0' ? value : `+${value}`
}
