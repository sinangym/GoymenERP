export const MONEY_PROFILE_TEXT = {
  title: 'Para profilleri', intro: 'Para birimi ve işlem bazında tutar ile birim fiyat ondalık basamakları. Değişiklikler yeni işlemlere uygulanır.',
  subject: 'para profilleri', empty: 'Bu şirkette onaylı para profili yok.',
  version: 'Sürüm', scale: 'Tutar ondalık basamağı', priceScale: 'Birim fiyat ondalık basamağı', active: 'Kullanımda',
  undefined: 'Tanımlı değil', yes: 'Evet', no: 'Hayır', edit: 'Yeni sürüm', reason: 'Gerekçe',
  preview: 'Değişiklik etkisi', previewHistory: 'Geçmiş ve onay bekleyen belgeler yeniden hesaplanmaz.',
  previewIncomplete: '0–28 arası tam basamak sayısı girin; birim fiyat boşsa tanımlı değildir.',
  companyScope: 'Değişiklik seçili şirkette ve seçilen işlemde geçerlidir.',
  previous: 'Önceki değer', next: 'Yeni değer', save: 'Yeni sürümü yayımla', cancel: 'Vazgeç',
  saved: 'Yeni para profili sürümü kaydedildi.', replay: 'Önceki isteğin sonucu gösterildi.',
  unknown: 'Sonuç doğrulanamadı. Aynı isteği aynı anahtarla yeniden deneyin.', retry: 'Aynı isteği yeniden dene',
  refused: 'İstek kabul edilmedi. Güncel bilgilerle yeniden deneyin.', pending: 'Kaydediliyor…',
  conflict: 'Profil değişti. Güncel bilgiler yüklendi.',
} as const

export function moneyOperationLabel(operation: string): string {
  const labels: Record<string, string> = {
    'accounting.journal-amount.v1': 'Muhasebe fişi',
    'finance.manual-bank-payment.v1': 'Banka ödemesi',
    'finance.manual-bank-receipt.v1': 'Banka tahsilatı',
    'purchasing.order.v1': 'Alış siparişi',
    'sales.order-draft.v1': 'Satış siparişi',
  }
  return labels[operation] ?? operation
}
