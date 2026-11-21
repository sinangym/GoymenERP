/**
 * URT-S13 monthly scrap price user text, in Turkish and in its own file (TEK-03): the screen holds no literal text.
 * No price, approval or correction rule is described here beyond what the server enforces: one effective price per
 * month, in TL per kg, greater than zero (URT-D74), for the current or a past month only (URT-D73), corrected only by a
 * new version with a reason that keeps the old one (URT-D72), and a month without a price leaves its scrap cost pending
 * ("maliyet yok").
 */

/** Calendar month names, January first; index = month - 1. */
export const SCRAP_PRICE_MONTH_LABEL: readonly string[] = [
  'Ocak',
  'Şubat',
  'Mart',
  'Nisan',
  'Mayıs',
  'Haziran',
  'Temmuz',
  'Ağustos',
  'Eylül',
  'Ekim',
  'Kasım',
  'Aralık',
]

export function scrapPriceMonthLabel(month: number): string {
  return SCRAP_PRICE_MONTH_LABEL[month - 1] ?? String(month)
}

export const SCRAP_PRICE_TEXT = {
  heading: 'Aylık hurda fiyatları',
  caption: 'Seçilen yılın girilmiş ayları, en son aydan geriye.',
  year: 'Yıl',
  yearInvalid: 'Yıl 1 ile 9999 arasında bir tam sayı olmalıdır.',
  month: 'Ay',
  price: 'Hurda fiyatı (TL/kg)',
  version: 'Sürüm',
  reason: 'Düzeltme gerekçesi',
  noReason: 'İlk giriş',
  recordedBy: 'Giren (teknik kimlik)',
  recordedAt: 'Giriş zamanı',
  orders: 'Üretim emirleri',
  costNote:
    'Her hurda tartımı, tartıldığı ayın tek hurda fiyatıyla değerlenir (malzemeye göre fiyat yoktur). ' +
    'Fiyatı girilmemiş ayın hurda tartımları "maliyet yok" olarak kalır ve kesinleşmez; sıfır sayılmaz.',
  missingMonths: 'Fiyatı girilmemiş aylar',
  noMissingMonths: 'Bu yılın her ayı için fiyat girilmiş.',
  formHeading: 'Ay fiyatı gir',
  formHelp:
    'Fiyat sıfırdan büyük olmalı, ondalık ayırıcı nokta olmalı ve en çok dört ondalık basamak taşımalıdır (ör. 7.25). ' +
    'Yalnız bu ay veya geçmiş bir ay için fiyat girilir. Girilmiş bir ayın fiyatı yalnız aşağıdaki gerekçeli düzeltmeyle, ' +
    'yeni sürüm olarak değişir.',
  priceInvalid: 'Fiyatı nokta ile ayrılmış bir sayı olarak girin (ör. 7.25); virgül, boşluk veya işaret kullanmayın.',
  submit: 'Fiyatı kaydet',
  submitting: 'Gönderiliyor…',
  resend: 'Yeniden gönder',
  applied: 'Ayın hurda fiyatı kaydedildi.',
  appliedReplay: 'Bu gönderim daha önce işlenmişti; sunucu ilk sonucu yineledi.',
  unknown:
    'Gönderimin sonucu bilinmiyor. Aynı gönderimi aynı anahtarla yeniden gönderebilirsiniz; ' +
    'sunucu daha önce işlediyse ilk sonucu yineler.',
  pendingHeld: 'Sonucu bilinmeyen gönderim',
  correctionHeading: 'Girilmiş ayın fiyatını düzelt',
  correctionHelp:
    'Düzeltme ayın yeni sürümüdür: eski sürüm silinmez, son sürüm geçerli olur ve her sürüm denetim izine yazılır. ' +
    'Kesinleşmemiş maliyet yeni fiyatı kullanır; kesinleşmiş maliyet değişmez.',
  correctionMonth: 'Düzeltilecek ay',
  correctionPrice: 'Yeni hurda fiyatı (TL/kg)',
  correctionReason: 'Gerekçe',
  correctionReasonInvalid: 'Gerekçe boş olamaz ve en çok 256 karakter olabilir.',
  correctionNothing: 'Bu yıl düzeltilecek girilmiş ay yok.',
  correctionSubmit: 'Düzeltmeyi kaydet',
  correctionApplied: 'Ayın hurda fiyatı yeni sürümle düzeltildi.',
  correctionPendingHeld: 'Sonucu bilinmeyen düzeltme',
  currentVersion: (version: number) => `şu anki sürüm ${version}`,
  subject: 'aylık hurda fiyatları',
  contextSubject: 'aylık hurda fiyatları için tek şirket bağlamı',
} as const

/** The server's decision on a refused entry, in the user's words; the server's code is the source, never a guess. */
export function scrapPriceFailureText(status: number, code: string | null, field: string | null): string {
  if (status === 409 && code === 'manufacturing_scrap_price_already_recorded') {
    return 'Bu ayın hurda fiyatı zaten girilmiş; değiştirmek için gerekçeli düzeltmeyi kullanın. Liste yeniden okundu.'
  }
  if (status === 422 && code === 'manufacturing_scrap_price_future_month') {
    return 'Gelecek bir ay için hurda fiyatı girilmez; yalnız bu ay veya geçmiş bir ay girilir.'
  }
  if (status === 422 && code === 'manufacturing_scrap_price_precision_exceeded') {
    return 'Fiyat en çok dört ondalık basamak taşıyabilir; beşinci basamak yuvarlanmaz, reddedilir.'
  }
  if (status === 400 && field === 'tlPerKg') {
    return 'Fiyat geçersiz: sıfırdan büyük, nokta ile ayrılmış ve izin verilen sınırın altında olmalıdır.'
  }
  if (status === 400 && (field === 'year' || field === 'month')) return 'Ay veya yıl geçersiz.'
  if (status === 401) return 'Oturum sona erdi; yeniden giriş yapın. Fiyat kaydedilmedi.'
  if (status === 403) return 'Bu işlem için yetkiniz yok. Fiyat kaydedilmedi.'
  if (status === 409) return 'İstek mevcut kayıtla çakıştı; fiyat kaydedilmedi. Liste yeniden okundu.'
  return 'Sunucu isteği kabul etmedi; fiyat kaydedilmedi.'
}

/** The server's decision on a refused correction (URT-D72), in the user's words; the server's code is the source. */
export function scrapPriceCorrectionFailureText(status: number, code: string | null, field: string | null): string {
  if (status === 409 && code === 'manufacturing_version_conflict') {
    return 'Ayın fiyatı siz düzeltirken başka bir sürümle değişti; düzeltme kaydedilmedi. Liste yeniden okundu.'
  }
  if (status === 404) return 'Bu ayın girilmiş bir fiyatı yok; önce ayın fiyatını girin. Liste yeniden okundu.'
  if (status === 422 && code === 'manufacturing_scrap_price_unchanged') {
    return 'Yeni fiyat ayın geçerli fiyatıyla aynı; yeni sürüm açılmadı.'
  }
  if (status === 422 && code === 'manufacturing_scrap_price_future_month') {
    return 'Gelecek bir ayın hurda fiyatı düzeltilmez; yalnız bu ay veya geçmiş bir ay düzeltilir.'
  }
  if (status === 422 && code === 'manufacturing_scrap_price_precision_exceeded') {
    return 'Fiyat en çok dört ondalık basamak taşıyabilir; beşinci basamak yuvarlanmaz, reddedilir.'
  }
  if (status === 400 && field === 'tlPerKg') {
    return 'Fiyat geçersiz: sıfırdan büyük, nokta ile ayrılmış ve izin verilen sınırın altında olmalıdır.'
  }
  if (status === 400 && field === 'reason') return 'Gerekçe boş olamaz ve en çok 256 karakter olabilir.'
  if (status === 400) return 'Ay, yıl veya sürüm geçersiz.'
  if (status === 401) return 'Oturum sona erdi; yeniden giriş yapın. Düzeltme kaydedilmedi.'
  if (status === 403) return 'Bu işlem için yetkiniz yok. Düzeltme kaydedilmedi.'
  if (status === 409) return 'İstek mevcut kayıtla çakıştı; düzeltme kaydedilmedi. Liste yeniden okundu.'
  return 'Sunucu isteği kabul etmedi; düzeltme kaydedilmedi.'
}
