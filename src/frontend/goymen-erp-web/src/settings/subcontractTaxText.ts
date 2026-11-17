export const SUBCONTRACT_TAX_TEXT = {
  sales: 'Satış fason vergi varsayılanı', purchasing: 'Alış fason vergi varsayılanı',
  intro: 'Satış ve alış ayrı kayıtlardır. Değişiklik sonraki seçimlere kaynak olur; geçmiş siparişler değiştirilmez.',
  empty: 'Henüz varsayılan kaydedilmedi.', current: 'Geçerli kayıt', history: 'Sürüm geçmişi', version: 'Sürüm', who: 'Kaydeden', when: 'Tarih', reason: 'Gerekçe',
  vat: 'KDV oranı', exemption: 'İstisna kodu', withholding: 'Tevkifat oranı', none: 'Tevkifat uygulanmaz', choose: 'Seçiniz', default: 'Ayarlar varsayılanını kullan',
  change: 'Satır için değiştir', save: 'Kaydet', saved: 'Vergi kaydı kaydedildi.', replay: 'Önceki işlemin sonucu alındı.', pending: 'Kaydediliyor…',
  invalid: 'Gerçek listelerden geçerli vergi seçimi ve gerekçe girin.', unknown: 'Sonuç doğrulanamadı. Aynı anahtarla yeniden deneyin.', retry: 'Aynı isteği yeniden dene',
  refused: 'İşlem kaydedilmedi.', conflict: 'Kaynak sürümü değişti. Güncel kaydı okuyup yeniden seçim yapın.', noSource: 'Vergi listesi veya varsayılan kaynak henüz yok.',
  taxRevision: 'Fason satır vergisini revize et', revisionHelp: 'Onaylı siparişte değişiklik yeni revizyon açar ve normal sipariş onayı ister. Önceki kabul ve vergi geçmişi korunur.',
} as const
