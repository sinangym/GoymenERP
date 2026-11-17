export const INVOICE_IMPORT_TEXT = {
  heading: 'Alış faturası dosyasını içe aktar', start: 'Dosya eklemeye başla', documents: 'Kaynak fatura belgeleri',
  previousImports: 'Önceki dosya işlemleri', open: 'Belgeleri aç', previous: 'Önceki sayfa', next: 'Sonraki sayfa', close: 'Belge listesini kapat',
  reservationLabel: (date: string) => `Dosya işlemi — ${new Date(date).toLocaleString('tr-TR')}`,
  hint: 'UBL-TR/XML, XLSX veya CSV belgesini yükleyin. Tablo dosyalarında dışarı aktarılan sütunları seçin.',
  reserved: 'Dosya yüklemek için kayıt hazır.', source: 'Okunacak dosya ve sürüm', choose: 'Dosya seçin',
  cleanRequired: 'Önizleme için güvenlik taraması tamamlanmış bir dosya seçin.',
  version: 'Sürüm', format: 'Dosya biçimi', xml: 'UBL-TR / XML', csv: 'CSV', xlsx: 'XLSX',
  worksheet: 'Çalışma sayfası adı', worksheetHint: 'Birden çok sayfa varsa adını dosyadaki gibi yazın.',
  delimiter: 'Sütun ayırıcı', semicolon: 'Noktalı virgül (;)', comma: 'Virgül (,)', tab: 'Sekme',
  decimal: 'Ondalık ayırıcı', dot: 'Nokta (.)', dateFormat: 'Tarih biçimi', isoDate: 'Yıl-ay-gün (2026-11-17)', localDate: 'Gün.ay.yıl (17.11.2026)',
  mapping: 'Dosyadaki sütun başlıkları', mappingHint: 'Her alan için dosyanın ilk satırındaki sütun başlığını yazın. Başlıklar birbirinden farklı olmalı.',
  required: 'Zorunlu sütunlar', optional: 'Diğer sütunlar', preview: 'Dosyayı oku', facts: 'Kaynak belgeden okunan bilgiler',
  missing: 'Belgede belirtilmemiş', number: 'Fatura numarası', ettn: 'ETTN', date: 'Fatura tarihi', type: 'Fatura türü', currency: 'Para birimi',
  supplier: 'Satıcı kimlikleri', buyer: 'Alıcı kimlikleri', totals: 'Belgedeki toplamlar', taxes: 'Vergiler', taxCode: 'Vergi kodu',
  rate: 'Oran', taxBase: 'Matrah', tax: 'Vergi tutarı', withholding: 'Tevkifat', adjustments: 'İskonto ve masraflar',
  charge: 'Masraf', allowance: 'İskonto', reason: 'Açıklama', rates: 'Belgedeki kurlar', purpose: 'Kur amacı',
  sourceCurrency: 'Kaynak para birimi', targetCurrency: 'Hedef para birimi', sourceBase: 'Kaynak baz oranı', targetBase: 'Hedef baz oranı', operator: 'İşlem',
  payments: 'Belgedeki ödeme bilgileri', paymentSource: 'Ödeme kaynağı', due: 'Ödeme tarihi', installmentDue: 'Taksit tarihi', amount: 'Tutar',
  line: 'Satır', description: 'Açıklama', sellerCode: 'Satıcı ürün kodu', buyerCode: 'Alıcı ürün kodu', unit: 'Belgedeki birim kodu', quantity: 'Miktar', net: 'Net tutar',
  issueHeading: 'Dosya okunamadı', issue: 'Bu alanı ve kaynak dosyayı kontrol edin.', invalidMapping: 'Zorunlu sütunları doldurun; aynı başlığı iki alana vermeyin.',
  changed: 'Seçilen belge değişti. Güncel sürümü seçip yeniden okuyun.', sending: 'Kayıt hazırlanıyor…', unknown: 'İşlemin sonucu alınamadı. Aynı işlemi tekrar sorgulayın.',
  retry: 'Aynı işlemi tekrar dene', refresh: 'Belgeleri yenile', rejected: 'İşlem kabul edilmedi.',
} as const

// Names are the file-reader contract's explicit mapping keys, not inferred vendor column names.
export const REQUIRED_IMPORT_COLUMNS: Readonly<Record<string, string>> = {
  Number: 'Fatura numarası', Ettn: 'ETTN', InvoiceDate: 'Fatura tarihi', InvoiceType: 'Fatura türü', Currency: 'Para birimi',
  SupplierTaxId: 'Satıcı vergi kimliği', SupplierTaxScheme: 'Satıcı kimlik türü', BuyerTaxId: 'Alıcı vergi kimliği', BuyerTaxScheme: 'Alıcı kimlik türü',
  LineId: 'Satır numarası', Description: 'Satır açıklaması', UnitCode: 'Birim kodu', Quantity: 'Miktar', LineNet: 'Satır net tutarı',
  LineExtensionAmount: 'Net satır toplamı', TaxExclusiveAmount: 'Vergi hariç toplam', TaxInclusiveAmount: 'Vergi dahil toplam', PayableAmount: 'Ödenecek toplam',
}
export const OPTIONAL_IMPORT_COLUMNS: Readonly<Record<string, string>> = {
  SellerCode: 'Satıcı ürün kodu', BuyerCode: 'Alıcı ürün kodu', AllowanceTotalAmount: 'İskonto toplamı', ChargeTotalAmount: 'Masraf toplamı',
  PrepaidAmount: 'Önceden ödenen tutar', PayableRoundingAmount: 'Ödenecek tutar yuvarlaması', LineTaxCode: 'Satır vergi kodu', LineTaxPercent: 'Satır vergi oranı',
  LineTaxAmount: 'Satır vergi tutarı', LineTaxableAmount: 'Satır vergi matrahı', LineWithholdingCode: 'Satır tevkifat kodu', LineWithholdingPercent: 'Satır tevkifat oranı',
  LineWithholdingAmount: 'Satır tevkifat tutarı', LineWithholdingTaxableAmount: 'Satır tevkifat matrahı', TaxCode: 'Fatura vergi kodu', TaxPercent: 'Fatura vergi oranı',
  TaxAmount: 'Fatura vergi tutarı', TaxableAmount: 'Fatura vergi matrahı', WithholdingCode: 'Fatura tevkifat kodu', WithholdingPercent: 'Fatura tevkifat oranı',
  WithholdingAmount: 'Fatura tevkifat tutarı', WithholdingTaxableAmount: 'Fatura tevkifat matrahı', OfficialRate: 'Belgedeki resmî kur',
  RateSourceCurrency: 'Kurun kaynak para birimi', RateTargetCurrency: 'Kurun hedef para birimi', RateDate: 'Kur tarihi', PaymentDueDate: 'Ödeme tarihi', PaymentAmount: 'Ödeme tutarı',
}
export const IMPORT_TOTAL_LABELS: Readonly<Record<string, string>> = { ...REQUIRED_IMPORT_COLUMNS, ...OPTIONAL_IMPORT_COLUMNS }
export const IMPORT_PURPOSE_LABELS: Readonly<Record<string, string>> = {
  TaxExchangeRate: 'Vergi kuru', PricingExchangeRate: 'Fiyatlandırma kuru', PaymentExchangeRate: 'Ödeme kuru',
  PaymentAlternativeExchangeRate: 'Alternatif ödeme kuru', OfficialInvoiceRate: 'Resmî fatura kuru',
  PaymentTerms: 'Ödeme koşulları', PaymentMeans: 'Ödeme aracı', TabularExport: 'Tablo dosyası',
}
