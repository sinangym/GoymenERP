export const COST_GENERATION_TEXT = {
  heading: 'Kapanış maliyet geçmişi', subject: 'kapanış maliyet geçmişi', empty: 'Henüz kaydedilmiş bir maliyet sonucu yok.',
  help: 'Her kapanışın ilk sonucu ve sonraki tanıma veya ters kaydı korunur. Geçmiş stok çıkışları değiştirilmez.',
  close: 'Kapanış sürümü', revision: 'Sonuç sürümü', recorded: 'Kayıt zamanı', reason: 'Gerekçe',
  pending: 'Bekliyor', unreadable: 'Hesaplanamadı', recognized: 'Maliyete alındı', reversed: 'Ters kayıt',
  outputs: 'Çıktıların kaydedilmiş maliyet etkisi', label: 'Etiket', net: 'Net kg', remaining: 'Stokta kalan kg', consumed: 'Çıkmış kg',
  originalTl: 'Kesin TL toplamı', remainingTl: 'Stokta kalan TL', consumedTl: 'Ayrı SMM farkı (TL)', usd: 'USD/kg (yalnız gösterim)',
  missing: 'Kullanılamıyor', recognize: 'Maliyeti yeniden oku ve tanı', applied: 'Maliyet sonucu kaydedildi.',
  unknown: 'İşlem sonucu doğrulanamadı. Aynı istek ve anahtarla sonucu sorgulamak için tekrar gönderin.',
  refused: 'İşlem reddedildi. Güncel maliyet geçmişini yeniden okuyun.', resend: 'Aynı isteği tekrar gönder',
  accounting: 'Çıkmış payın TL farkı ayrı muhasebe önerisidir. Açık dönem ve tarih seçimi ile farklı kişi onayı muhasebe akışında tamamlanır.',
} as const
