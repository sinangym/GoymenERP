/**
 * URT-S13 monthly scrap price user text, in Turkish and in its own file (TEK-03): the screen holds no literal text.
 * No price, approval or correction rule is described here beyond what the server enforces: one price per month, in TL
 * per kg, never changed once recorded, and a month without a price leaves its scrap cost pending ("maliyet yok").
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
    'Girilmiş bir ayın fiyatı değiştirilmez.',
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
  subject: 'aylık hurda fiyatları',
  contextSubject: 'aylık hurda fiyatları için tek şirket bağlamı',
} as const

/** The server's decision on a refused entry, in the user's words; the server's code is the source, never a guess. */
export function scrapPriceFailureText(status: number, code: string | null, field: string | null): string {
  if (status === 409 && code === 'manufacturing_scrap_price_already_recorded') {
    return 'Bu ayın hurda fiyatı zaten girilmiş; girilmiş ay değiştirilmez. Liste yeniden okundu.'
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
