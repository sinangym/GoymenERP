export const TWO_FACTOR_TEXT = {
  title: 'İki aşamalı doğrulama', intro: 'Bu özellik isteğe bağlıdır. Etkinleştirirseniz girişte parolanıza ek olarak doğrulayıcı uygulama veya tek kullanımlık kurtarma kodu gerekir.',
  subject: 'hesap güvenliği', loginCode: 'Doğrulama veya kurtarma kodu (2FA açıksa)', password: 'Mevcut parola', code: 'Doğrulayıcı veya kurtarma kodu',
  begin: 'Doğrulayıcı kurulumu hazırla', enable: 'Doğrulayıcıyı etkinleştir', disable: 'İki aşamalı doğrulamayı kapat', regenerate: 'Yeni kurtarma kodları oluştur',
  enabled: 'Etkin', disabled: 'Kapalı', count: 'Kalan kurtarma kodu', seed: 'Doğrulayıcı uygulamaya girilecek anahtar', uri: 'Doğrulayıcı kurulum adresi',
  codes: 'Tek kullanımlık kurtarma kodları', codesHelp: 'Kodları güvenli ve çevrimdışı bir yere kaydedin. Her kod yalnız bir kez kullanılabilir. İkinci bir yönetici bulunması önerilir.',
  setupHelp: 'Henüz etkinleştirilmedi. Anahtarı doğrulayıcı uygulamaya ekleyin; uygulamanın verdiği kodla etkinleştirin.',
  reason: 'Gerekçe', saved: 'Hesap güvenliği değişikliği kaydedildi. Diğer oturumlarınız sona erdi.',
  replay: 'Bu istek daha önce işlendi. Kurtarma kodları yeniden gösterilmez; gerekirse doğrulayıcı koduyla yeni bir kod grubu oluşturun.',
  refused: 'İşlem uygulanmadı. Parolanızı, kodunuzu ve güncel hesap durumunu kontrol edin.', pending: 'İşleniyor…', retry: 'Aynı isteği tekrar gönder',
  unknown: 'İşlemin sonucu doğrulanamadı. İçeriği değiştirmeden aynı isteği tekrar gönderin.', dismiss: 'Kodları kaydettim, gösterimi kapat',
  cancel: 'Vazgeç', returnAccounts: 'Kullanıcı yönetimi',
} as const
