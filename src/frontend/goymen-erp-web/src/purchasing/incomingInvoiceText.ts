export const INCOMING_INVOICE_TEXT = {
  heading: 'Alış faturaları', detail: 'Alış faturası', number: 'Resmî fatura numarası', search: 'Ara', open: 'Aç',
  date: 'Fatura tarihi', status: 'Durum', previous: 'Önceki', next: 'Sonraki', back: 'Faturalara dön', refresh: 'Yeniden oku',
  line: 'Fatura satırı', quantity: 'Miktar', net: 'Vergi hariç tutar', vat: 'KDV', withholding: 'Tevkifat', payable: 'Ödenecek tutar',
  receipts: 'Mal kabulleri', chooseReceipts: 'Eşleştirilecek kabulleri seç', acceptedOn: 'Kabul zamanı', available: 'Faturalanabilir miktar',
  order: 'Alış siparişi', orderUnavailable: 'Sipariş numarası okunamadı', orderLine: 'Sipariş satırı', select: 'Seç', share: 'Bu faturaya ayrılan miktar',
  match: 'Fatura paylarını eşleştir', reason: 'Fark onayı gerekçesi', approve: 'Farkı onayla', finalize: 'Kesinleştir', retryFinance: 'Finans aktarımını dene',
  variances: 'Onay gerektiren farklar', expected: 'Beklenen', actual: 'Belgedeki', price: 'Fiyat', exchangeRate: 'Kur',
  invalidSelection: 'Her satır için gerçek kabul miktarı veya masraf net tutar payı seçin. Pozitif değerlerde en fazla altı ondalık basamak kullanılabilir.',
  invalidReason: 'Fark onayı için gerekçe girin.', approvalDone: 'Fark onayı kaydedildi.', sending: 'İşlem sürüyor…',
  unknown: 'İşlemin sonucu kesinleşmedi. Yeni bir işlem başlatmadan aynı anahtarla sonucu doğrulayın.', retry: 'Aynı işlemi doğrula',
  saved: 'İşlem kaydedildi.', conflict: 'Kayıt veya kaynak değişti. Güncel bilgiyi okuyup yeniden değerlendirin.',
  rejected: 'İşlem kabul edilmedi. Belge durumunu, seçilen payları ve yetkinizi kontrol edin.',
  matchingHint: 'Kabul fiyatı, kur ve tolerans sunucuda okunur. Burada toplam veya fark hesaplanmaz.',
  ettn: 'ETTN', currencyUnavailable: 'Para birimi adı okunamadı',
  chooseExpenses: 'Eşleştirilecek ek masrafları seç', expenseAvailable: 'Sunucuda kalan net masraf tutarı', expenseShare: 'Bu faturaya bağlanacak net tutar',
  expenseHint: 'Masraf aynı carinin faturalarına parça parça bağlanır. Toplam sınırını sunucu denetler; stok hareketi oluşmaz.',
  expenseTotal: 'Kaydedilmiş net masraf', expenseDescription: 'Masraf açıklaması',
  // AK-3b (SAT-D436, SAT-D439): kg price indicator in the invoice's own currency — information only, never converted or recomputed.
  kgPriceHeading: 'Fatura kg fiyatı (bilgi; maliyet değildir)', lineKgPriceHeading: 'Satır kg fiyatı (bilgi)',
  theoreticalKgPrice: 'Teorik kg fiyatı', realKgPrice: 'Gerçek kg fiyatı',
  kgPricePerKg: (amount: string, currency: string) => `${amount} ${currency}/kg`, kgPriceNone: '—',
} as const
/** AK-3b: invoice command refusals named in user words; any other code keeps the generic conflict / refusal text. */
export const INCOMING_INVOICE_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  purchasing_invoice_piece_quantity_invalid: 'Adetli sipariş satırı yalnız tam adetle eşleştirilebilir. Paylaştırılan miktarı tam adet olarak girin.',
}
export const INCOMING_INVOICE_STATUS = { Recorded: 'Kaydedildi', Matched: 'Eşleştirildi', FinancePending: 'Finans aktarımı bekliyor', FinanceAccepted: 'Finansa aktarıldı' } as const
