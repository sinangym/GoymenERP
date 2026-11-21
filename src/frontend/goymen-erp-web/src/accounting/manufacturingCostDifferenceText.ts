export const COST_DIFFERENCE_TEXT = {
  title: 'Üretim maliyet farkları', intro: 'Önceden çıkışı yapılan üretim miktarının TL maliyet farkı ayrı bir muhasebe fişine hazırlanır. Kaynak stok çıkışları korunur; USD değeri yalnız gösterim içindir.',
  subject: 'üretim maliyet farkları', empty: 'Bekleyen üretim maliyet farkı bulunmuyor.',
  select: 'Öneriyi aç', source: 'Üretim emri', amount: 'Tüketilen bölüm TL farkı', status: 'Durum',
  statuses: { AwaitingPreparation: 'Hazırlık bekliyor', Prepared: 'Onay bekliyor', Posted: 'Muhasebeleştirildi', NoDifference: 'Maliyet farkı yok' },
  debit: 'Borç hesabı', credit: 'Alacak hesabı', choose: 'Seçiniz', date: 'Muhasebe tarihi', transactionDate: 'İşlem tarihi', reason: 'Gerekçe',
  prepare: 'Onaya hazırla', approve: 'Onayla ve fişi oluştur', own: 'Hazırladığınız öneriyi başka bir muhasebe yetkilisi onaylamalı.',
  noPlan: 'Etkin onaylı hesap planı yok. Hesap planı tanımlanmadan öneri hazırlanamaz.',
  unknown: 'İşlemin sonucu doğrulanamadı. Aynı komut yeniden gönderilmeden yeni işlem başlatılamaz.', resend: 'Aynı komutu yeniden gönder',
  done: 'İşlem kaydedildi.', refused: 'İşlem tamamlanamadı. Yetkiyi, sürümü, açık dönemi ve hesap planını kontrol edin.',
  refresh: 'Yeniden oku', output: 'Çıktı', kg: 'Kaynak / kalan / tüketilen kg', original: 'Kaynak toplam TL',
  warning: 'Negatif TL farkı ters yönlü maliyet etkisidir. Borç ve alacak hesaplarını bu işarete göre seçip kontrol edin.',
  planSource: 'Hazırlıkta kullanılan onaylı hesap planı',
} as const
