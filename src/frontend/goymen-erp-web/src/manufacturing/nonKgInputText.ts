export const NON_KG_INPUT_TEXT = {
  heading: 'Gerçek kg dışı sarf', help: 'Yalnız gerçekten tüketilen kalemi kaydedin. Bu kayıt stok hareketi veya kg çevrimi oluşturmaz. Boya ve diğer kg dışı girdilerin bedeli ilk sürüm ürün maliyetine eklenmez.',
  subject: 'Gerçek sarf kayıtları', empty: 'Gerçek kg dışı sarf kaydı yok.', item: 'Sarf kalemi', kind: 'Girdi türü', paint: 'Boya', other: 'Diğer kg dışı girdi',
  unit: 'Birim', litre: 'Litre', metre: 'Metre', piece: 'Adet', quantity: 'Gerçek tüketilen miktar', occurred: 'Gerçekleşme zamanı (UTC)', reason: 'Kayıt / düzeltme gerekçesi',
  correction: 'Düzeltilecek kayıt', newEntry: 'Yeni gerçek sarf', correct: 'Bu kaydı düzelt', history: 'Önceki kayıtlar korunur; düzeltme eski kaydın yerine yeni ölçümü geçirir. Yanlış kaydı sıfır miktarla düzeltmek mümkündür.',
  submit: 'Gerçek sarfı kaydet', resend: 'Aynı kaydı yeniden gönder', unknown: 'Sonuç bilinmiyor. Form kilitli; yalnız aynı kayıt ve anahtarla tekrar gönderin.',
  refused: 'Kayıt uygulanmadı. Güncel kayıtları okuyup tekrar deneyin.', applied: 'Gerçek sarf kaydedildi.', blocked: 'Bekleyen kayıt güvenle okunamıyor. Yeni kayıt gönderilemez.',
  revision: 'Kayıt sürümü', replacement: 'Düzelttiği kayıt', invalid: 'Kalem, geçerli pozitif miktar, UTC zamanı ve gerekçe girin. Yeni kayıt sıfır olamaz.',
} as const
