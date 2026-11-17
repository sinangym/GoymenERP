export const SOURCE_VOUCHER_TEXT = {
  title: 'Fatura fişleri', subject: 'kaynak fatura fişleri', intro: 'Faturanın kesinleşmiş tutarları ve hesap eşlemesi korunur. Onay, fatura tarihinin açık dönemine tek yevmiye kaydı oluşturur.',
  empty: 'Kaynak fatura fişi bulunmuyor.', refresh: 'Yeniden oku', select: 'Fişi aç', invoice: 'Resmî fatura', date: 'Fatura tarihi',
  amount: 'Ödenecek TL', status: 'Durum', source: 'Kaynak', sale: 'Satış', purchase: 'Alış', missingNumber: 'Kaynakta fatura numarası yok',
  account: 'Hesap', accountMissing: 'Hesap planı kaynağı eksik', side: 'Yön', debit: 'Borç', credit: 'Alacak', lineAmount: 'TL tutarı',
  approve: 'Onayla ve yevmiyeye geçir', own: 'Kesinleştirdiğiniz faturanın fişini başka bir muhasebe yetkilisi onaylamalı.',
  period: 'Fatura tarihine ait açık dönem bekleniyor. Başka bir dönem veya tarih kendiliğinden seçilmez.',
  reversal: 'Kaynak fatura iptali kaydedildi. Onaylı yevmiye korunuyor; ters kayıt için dönem ve tarih kararı bekleniyor.',
  statuses: { AwaitingApproval: 'Onay bekliyor', PeriodWaiting: 'Dönem bekliyor', Posted: 'Muhasebeleştirildi', Cancelled: 'Taslak iptal edildi', CancellationReversalPending: 'Ters kayıt kararı bekliyor' },
  unknown: 'İşlemin sonucu doğrulanamadı. Aynı komut yeniden gönderilmeden yeni onay başlatılamaz.', resend: 'Aynı komutu yeniden gönder',
  done: 'Fiş onaylandı.', refused: 'İşlem tamamlanamadı. Yetki, güncel sürüm, açık dönem ve hesap planını kontrol edin.',
} as const
