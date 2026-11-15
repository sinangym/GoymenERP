/**
 * Turkish texts of the Ayarlar > Birim çevrim politikası screen (F-004, KIM-D37). Every user-facing string lives here.
 *
 * KIM-D37: no value is seeded and the form pre-fills nothing. The decision's suggestions are shown as reminder text only
 * (density 7,85; coefficient 3 decimals; kg → whole unit rounded down; document total 2 decimals; tolerance %1 with ≤;
 * theoretical kg). The coefficient's rounding direction has NO suggestion: it is open (URN-D8, NEEDS-SINAN) and the
 * administrator chooses it.
 */

import {
  UNIT_CONVERSION_BASE,
  UNIT_CONVERSION_BOUNDARY,
  UNIT_CONVERSION_POLICY_INPUT,
  UNIT_CONVERSION_POLICY_PROBLEM,
  UNIT_CONVERSION_ROUNDING,
} from './unitConversionPolicyContract'

export const UNIT_CONVERSION_TEXT = {
  title: 'Birim çevrim politikası',
  intro:
    'Şirketin kg ↔ adet / metre çevriminde kullandığı yoğunluk, yuvarlama ve kantar toleransı değerleri. Başlangıç ' +
    'değeri yoktur: değerler girilene kadar bu değerlere bağlı hesaplar reddedilir.',
  subject: 'birim çevrim politikası',
  tableLabel: 'Birim çevrim politikası sürümleri',
  empty:
    'Bu şirket için henüz politika kaydı yok. Çevrim ve kantar kontrolü gibi bağlı hesaplar, ilk sürüm kaydedilene ' +
    'kadar reddedilir.',
  inForce: 'Yürürlükte',
  noneInForce: 'Bugün yürürlükte politika yok; bağlı hesaplar reddedilir.',
  columnVersion: 'Sürüm',
  columnDensity: 'Yoğunluk (g/cm³)',
  columnCoefficient: 'Katsayı',
  columnDocumentTotal: 'Belge toplamı',
  columnWholeUnit: 'kg → tam birim',
  columnTolerance: 'Kantar toleransı',
  columnFrom: 'Başlangıç',
  columnThrough: 'Bitiş',
  columnState: 'Durum',
  columnRecorded: 'Kaydeden / zaman',
  columnReason: 'Gerekçe',
  openEnded: 'Süresiz',
  newVersion: 'Yeni sürüm kaydet',
  hintsHeading: 'Karar KIM-D37 önerileri (yalnız hatırlatma; form kendiliğinden doldurmaz)',
  hintDensity: 'Yoğunluk: 7,85 g/cm³ (girişte 7.85).',
  hintCoefficientDecimals: 'Katsayı: 3 ondalık.',
  hintCoefficientRounding:
    'Katsayının yuvarlama yönü için öneri yoktur; karar açıktır ve yöneticinin seçimine bırakılmıştır.',
  hintWholeUnit: 'kg → tam birim: aşağı yuvarlama (pozitif miktarda «Sıfıra doğru» ile «Eksi sonsuza doğru» aynıdır).',
  hintDocumentTotal: 'Belge toplamı: 2 ondalık.',
  hintTolerance: 'Kantar toleransı: %1, sapma toleransa eşitse geçer (≤), teorik kg üzerinden.',
  densityLabel: 'Yoğunluk (g/cm³)',
  densityHelp:
    `Sıfırdan büyük; ondalık ayırıcı nokta, en çok ${UNIT_CONVERSION_POLICY_INPUT.densityMaximumScale} ondalık ` +
    `(örn. 7.85). Daha uzun değer yuvarlanmaz, reddedilir.`,
  densityError: 'Sıfırdan büyük, nokta ile ayrılmış bir sayı girin.',
  coefficientDecimalsLabel: 'Katsayı ondalık sayısı',
  coefficientRoundingLegend: 'Katsayı yuvarlama yönü',
  documentTotalDecimalsLabel: 'Belge toplamı ondalık sayısı',
  documentTotalRoundingLegend: 'Belge toplamı yuvarlama yönü',
  wholeUnitRoundingLegend: 'kg → tam birim yuvarlama yönü',
  decimalsHelp: `Tam sayı; ${UNIT_CONVERSION_POLICY_INPUT.minimumRoundingDecimals} ile ${UNIT_CONVERSION_POLICY_INPUT.maximumRoundingDecimals} arasında.`,
  decimalsError: `${UNIT_CONVERSION_POLICY_INPUT.minimumRoundingDecimals} ile ${UNIT_CONVERSION_POLICY_INPUT.maximumRoundingDecimals} arasında bir tam sayı girin.`,
  toleranceLabel: 'Kantar toleransı (%)',
  toleranceHelp:
    `${UNIT_CONVERSION_POLICY_INPUT.minimumPercent} ile ${UNIT_CONVERSION_POLICY_INPUT.maximumPercent} arasında; ` +
    `ondalık ayırıcı nokta, en çok ${UNIT_CONVERSION_POLICY_INPUT.percentMaximumScale} ondalık.`,
  toleranceError: `${UNIT_CONVERSION_POLICY_INPUT.minimumPercent} ile ${UNIT_CONVERSION_POLICY_INPUT.maximumPercent} arasında, nokta ile ayrılmış bir sayı girin.`,
  boundaryLegend: 'Sapma toleransa eşitse',
  baseLegend: 'Sapma yüzdesinin tabanı',
  save: 'Kaydet',
  cancel: 'Vazgeç',
  saving: 'Kaydediliyor…',
  retrySame: 'Aynı işlemi yeniden dene',
  saved: 'Yeni sürüm kaydedildi.',
  savedReplay: 'Bu işlem daha önce kaydedilmişti; sonuç yeniden gösterildi.',
  unknownOutcome:
    'Sonuç alınamadı; işlem uygulanmış olabilir. "Aynı işlemi yeniden dene" aynı işlem anahtarıyla gönderir ve iki kez uygulanmaz.',
  staleReloaded: 'Kayıt siz açtıktan sonra değişti. Güncel hâli yüklendi; değişikliği yeniden yapın.',
  reasonLabel: 'Gerekçe',
  reasonHelp: `Zorunlu; en çok ${UNIT_CONVERSION_POLICY_INPUT.reasonMaximumLength} karakter. Değişiklik gerekçesiyle birlikte denetim kaydına geçer.`,
} as const

const ROUNDING_LABEL: Record<string, string> = {
  [UNIT_CONVERSION_ROUNDING.toEven]: 'Yarıda çift basamağa (bankacı yuvarlaması)',
  [UNIT_CONVERSION_ROUNDING.awayFromZero]: 'Yarıda sıfırdan uzağa',
  [UNIT_CONVERSION_ROUNDING.toZero]: 'Sıfıra doğru (kesme)',
  [UNIT_CONVERSION_ROUNDING.toNegativeInfinity]: 'Eksi sonsuza doğru (taban)',
  [UNIT_CONVERSION_ROUNDING.toPositiveInfinity]: 'Artı sonsuza doğru (tavan)',
}

const BOUNDARY_LABEL: Record<string, string> = {
  [UNIT_CONVERSION_BOUNDARY.inclusive]: 'Geçer (sapma ≤ tolerans)',
  [UNIT_CONVERSION_BOUNDARY.exclusive]: 'Geçmez (sapma < tolerans)',
}

const BASE_LABEL: Record<string, string> = {
  [UNIT_CONVERSION_BASE.theoreticalKg]: 'Teorik (hesaplanan) kg',
  [UNIT_CONVERSION_BASE.weighedKg]: 'Tartılan kg',
}

export function roundingLabel(mode: string): string {
  return ROUNDING_LABEL[mode] ?? mode
}

export function boundaryLabel(boundary: string): string {
  return BOUNDARY_LABEL[boundary] ?? boundary
}

export function baseLabel(base: string): string {
  return BASE_LABEL[base] ?? base
}

/** "3 ondalık · Sıfıra doğru (kesme)". */
export function roundingRuleLabel(decimals: number, mode: string): string {
  return `${decimals} ondalık · ${roundingLabel(mode)}`
}

/** A refusal's text by its stable code (or status when the body is code-less). */
export function unitConversionRefusalText(status: number, code: string | null): string {
  switch (code) {
    case UNIT_CONVERSION_POLICY_PROBLEM.reasonRequired:
      return 'Gerekçe zorunludur.'
    case UNIT_CONVERSION_POLICY_PROBLEM.reasonTooLong:
      return `Gerekçe en fazla ${UNIT_CONVERSION_POLICY_INPUT.reasonMaximumLength} karakter olabilir.`
    case UNIT_CONVERSION_POLICY_PROBLEM.requestInvalid:
      return 'Girilen değerler geçersiz; alanları kontrol edin.'
    case UNIT_CONVERSION_POLICY_PROBLEM.commandMetadataInvalid:
      return 'İstek bilgileri eksik ya da geçersiz; sayfayı yenileyip tekrar deneyin.'
    case UNIT_CONVERSION_POLICY_PROBLEM.idempotencyConflict:
      return 'Bu işlem anahtarı başka bir istekle kullanılmış; formu yeniden açıp tekrar deneyin.'
    case UNIT_CONVERSION_POLICY_PROBLEM.versionConflict:
      return UNIT_CONVERSION_TEXT.staleReloaded
    case UNIT_CONVERSION_POLICY_PROBLEM.effectiveFromPast:
      return 'Başlangıç tarihi bugünden önce olamaz.'
    case UNIT_CONVERSION_POLICY_PROBLEM.providerUnavailable:
      return 'Ayarlar hizmeti şu anda yanıt vermiyor; biraz sonra tekrar deneyin.'
    default:
      if (status === 403) return 'Bu işlem için yetkiniz yok.'
      if (status === 415) return 'İstek biçimi kabul edilmedi.'
      return 'İşlem kabul edilmedi.'
  }
}
