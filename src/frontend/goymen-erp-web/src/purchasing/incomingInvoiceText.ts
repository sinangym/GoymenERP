export const INCOMING_INVOICE_TEXT = {
  heading: 'Alış faturaları', detail: 'Alış faturası', number: 'Resmî fatura numarası', search: 'Ara', open: 'Aç',
  date: 'Fatura tarihi', status: 'Durum', previous: 'Önceki', next: 'Sonraki', back: 'Faturalara dön', refresh: 'Yeniden oku',
  line: 'Fatura satırı', quantity: 'Miktar', net: 'Vergi hariç tutar', vat: 'KDV', withholding: 'Tevkifat', payable: 'Ödenecek tutar',
  receipts: 'Mal kabulleri', chooseReceipts: 'Eşleştirilecek kabulleri seç', acceptedOn: 'Kabul zamanı', available: 'Faturalanabilir miktar',
  order: 'Alış siparişi', orderUnavailable: 'Sipariş numarası okunamadı', orderLine: 'Sipariş satırı', select: 'Seç', share: 'Bu faturaya ayrılan miktar',
  match: 'Kabul paylarını eşleştir', reason: 'Fark onayı gerekçesi', approve: 'Farkı onayla', finalize: 'Kesinleştir', retryFinance: 'Finans aktarımını dene',
  variances: 'Onay gerektiren farklar', expected: 'Beklenen', actual: 'Belgedeki', price: 'Fiyat', exchangeRate: 'Kur',
  invalidSelection: 'Her fatura satırı için kabul seçin ve pozitif miktar girin. Miktarlarda en fazla altı ondalık basamak kullanılabilir.',
  invalidReason: 'Fark onayı için gerekçe girin.', approvalDone: 'Fark onayı kaydedildi.', sending: 'İşlem sürüyor…',
  unknown: 'İşlemin sonucu kesinleşmedi. Yeni bir işlem başlatmadan aynı anahtarla sonucu doğrulayın.', retry: 'Aynı işlemi doğrula',
  saved: 'İşlem kaydedildi.', conflict: 'Kayıt veya kaynak değişti. Güncel bilgiyi okuyup yeniden değerlendirin.',
  rejected: 'İşlem kabul edilmedi. Belge durumunu, seçilen payları ve yetkinizi kontrol edin.',
  matchingHint: 'Kabul fiyatı, kur ve tolerans sunucuda okunur. Burada toplam veya fark hesaplanmaz.',
  ettn: 'ETTN', currencyUnavailable: 'Para birimi adı okunamadı',
} as const
export const INCOMING_INVOICE_STATUS = { Recorded: 'Kaydedildi', Matched: 'Eşleştirildi', FinancePending: 'Finans aktarımı bekliyor', FinanceAccepted: 'Finansa aktarıldı' } as const
