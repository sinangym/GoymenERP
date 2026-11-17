export const PURCHASE_INVOICE_VARIANCE_POLICY_TEXT = {
  title: 'Alış fatura fark toleransı', subject: 'fatura fark toleransı',
  intro: 'Fiyat ve kur farkı sınırları aynı şirket politika sürümünde tutulur. Fatura eşleştirmesi kullandığı sürümü saklar.',
  empty: 'Henüz yayımlanmış politika yok. Formdaki sıfır değerleri yayımlanana kadar geçerli politika sayılmaz.',
  amount: 'Fiyat farkı toleransı', rate: 'Kontrol kuru farkı toleransı', reason: 'Yayın gerekçesi',
  edit: 'Yeni sürüm yayımla', publish: 'Toleransı yayımla', pending: 'Yayımlanıyor…', cancel: 'Vazgeç',
  current: 'Geçerli sürüm', history: 'Yayın geçmişi', version: 'Sürüm', who: 'Yayımlayan', when: 'Yayın zamanı',
  saved: 'Tolerans sürümü yayımlandı.', replay: 'Önceki yayın sonucu getirildi; ikinci bir sürüm oluşmadı.',
  unknown: 'Yayın sonucu kesinleşmedi. Aynı içerik ve işlem anahtarıyla yeniden deneyin.', retry: 'Aynı yayını yeniden dene',
  conflict: 'Politika değişmiş. Güncel sürüm getiriliyor; değişikliği yeniden inceleyin.', refused: 'Yayın reddedildi. Değerleri ve yetkinizi kontrol edin.',
  effect: 'Bu yayın geçmiş faturaların sakladığı politika sürümünü değiştirmez. İki yöndeki farklar sunucuda değerlendirilir.',
} as const
