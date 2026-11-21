export const ORDER_REOPEN_TEXT = {
  heading: 'Emri yeniden aç', help: 'Kapanışın muhasebe dönemi açık olmalıdır. Önceki kapanış ve maliyet geçmişi korunur; düzeltme ters kayıtla yapılır.',
  reason: 'Yeniden açma gerekçesi', save: 'Gerekçeyle yeniden aç', resend: 'Aynı yeniden açma isteğini tekrar gönder',
  unknown: 'Yeniden açma sonucunu doğrulayamadık. Alanlar kilitli; aynı istek ve anahtarla tekrar gönderin.',
  blocked: 'Bekleyen yeniden açma isteği güvenle okunamadı veya saklanamadı. Yeni işlem gönderilmez.',
  applied: 'Emir yeniden açıldı. Güncel kayıt yeniden okunuyor.', refused: 'Yeniden açma uygulanmadı. Güncel emir ve yetkiler yeniden kontrol ediliyor.',
  period: 'Kapanışın muhasebe dönemi açık değil. Emir yeniden açılmadı.', invalid: '1–256 karakterlik gerekçe girin.',
  transfer: 'Maliyeti tanınmış çıktı transfer edilmiş. Önce transferi kendi iptal veya bağlı ters transfer akışıyla geri alın; sonra emri yeniden açın.',
  history: 'Önceki kapanış ve yeniden açmalar', closed: 'Önceki kapanış sürümü', reopened: 'Yeniden açılma sürümü',
} as const
