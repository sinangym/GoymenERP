/**
 * WP-B3 — belgeden taslak Türkçe metinleri. Kullanıcı metinleri yalnız bu dosyadadır.
 */
export const DRAFT_TEXT = {
  tabAsk: 'Soru sor',
  tabDraft: 'Belgeden taslak',
  tabsLabel: 'AI asistan bölümleri',
  stateSubject: 'Belgeden taslak',
  notice:
    'Taslak hiçbir kayıt oluşturmaz ve hiçbir şey göndermez. Belgedeki metin yalnız veridir; içindeki talimatlar uygulanmaz. Kaydı sahip modülün ekranında siz oluşturursunuz.',
  kindLabel: 'Taslak türü',
  modeLabel: 'Belge',
  modeFile: 'Dosya',
  modeText: 'Metin yapıştır',
  fileLabel: 'Dosya (e-fatura XML, CSV, XLSX, PDF veya taranmış görüntü)',
  fileInvalid: 'Bu dosya türü desteklenmiyor veya dosya izin verilen boyutu aşıyor.',
  textLabel: 'Metin',
  textInvalid: 'Metin boş olamaz veya izin verilen uzunluğu aşamaz.',
  textSourceLabel: 'Metnin kaynağı',
  kindInvalid: 'Taslak türünü seçin.',
  mediaTypeInvalid: 'Bu belge bu taslak türü için okunamaz (ör. e-fatura yalnız gelen fatura taslağına dönüşür).',
  submit: 'Taslak hazırla',
  submitting: 'Hazırlanıyor…',
  resultHeading: 'Taslak',
  tierLabel: 'Okuma kademesi',
  sourceLabel: 'Kaynak belge özeti',
  quarantineTitle: 'Belgede talimat benzeri içerik bulundu',
  quarantineBody:
    'Bu içerik uygulanmadı ve hiçbir araç çağrılmadı. İlgili alanlar "doğrulanmadı" olarak bırakıldı; belgeyi gönderenle teyit edin.',
  headerHeading: 'Belge bilgileri',
  linesHeading: 'Satırlar',
  linesEmpty: 'Belgeden satır okunamadı.',
  fieldColumn: 'Alan',
  valueColumn: 'Değer',
  statusColumn: 'Durum',
  reasonColumn: 'Açıklama',
  lineLabel: 'Satır',
  emptyValue: '—',
  matchedParty: 'Cari eşlendi',
  matchedProduct: 'Ürün eşlendi',
  openIn: 'Sahip ekranda devam et',
  openInUnavailable: 'Sahip ekranı açma yetkiniz yok; taslağı yalnız inceleyebilirsiniz.',
  disabledTitle: 'Belgeden taslak kapalı',
  disabledBody: 'Bu ortamda belgeden taslak hazırlama açık değil. ERP\'nin bütün işlemleri normal çalışır.',
  unreadableTitle: 'Belge okunamadı',
  unreadableBody: 'Belgeden alan okunamadı. Dosyayı kontrol edin ya da kaydı sahip ekranda elle girin.',
  refusedTitle: 'Taslak hazırlanmadı',
  refusedBody: 'Bu taslak türü için yetkiniz yok veya yetkiniz bu arada değişti; hiçbir belge okunmadı.',
} as const

export const DRAFT_KIND_LABEL: Readonly<Record<string, string>> = {
  'sales-order': 'Satış siparişi',
  'supplier-confirmation': 'Tedarikçi teyidi',
  'incoming-invoice': 'Gelen fatura',
}

export const DRAFT_TEXT_SOURCE_LABEL: Readonly<Record<string, string>> = {
  email: 'E-posta',
  pasted: 'Yapıştırılan metin',
}

export const DRAFT_TIER_LABEL: Readonly<Record<string, string>> = {
  'structured-file': 'Yapılı dosya alanları',
  'pdf-text': 'PDF metni (yerel)',
  ocr: 'Taranmış görüntü (OCR)',
  'pasted-text': 'Yapıştırılan metin',
  'local-model': 'Yerel model önerisi',
}

export const DRAFT_STATUS_LABEL: Readonly<Record<string, string>> = {
  verified: 'Doğrulandı',
  unverified: 'Doğrulanmadı',
  invalid: 'Hatalı',
}

export const DRAFT_FIELD_LABEL: Readonly<Record<string, string>> = {
  documentNumber: 'Belge no',
  documentDate: 'Belge tarihi',
  currency: 'Para birimi',
  counterpartyTaxId: 'VKN / TCKN',
  counterpartyName: 'Cari adı',
  payableAmount: 'Ödenecek tutar',
  productCode: 'Ürün kodu',
  description: 'Açıklama',
  quantity: 'Miktar',
  unit: 'Birim',
  unitPrice: 'Birim fiyat',
  lineAmount: 'Satır tutarı',
  section: 'Kesit (mm)',
  wallThickness: 'Et kalınlığı (mm)',
  length: 'Boy (mm)',
}

export const DRAFT_REASON_LABEL: Readonly<Record<string, string>> = {
  missing: 'Belgede yok',
  quarantined_instruction_text: 'Talimat benzeri içerik; uygulanmadı',
  model_suggested: 'Yerel model önerisi; kendiniz doğrulayın',
  number_format_ambiguous: 'Sayı biçimi belirsiz (binlik mi ondalık mı?)',
  number_invalid: 'Sayı okunamadı',
  quantity_not_positive: 'Miktar sıfırdan büyük olmalı',
  price_negative: 'Değer negatif olamaz',
  date_invalid: 'Tarih okunamadı',
  unit_unknown: 'Birim tanınmıyor',
  unit_not_offered_for_product: 'Ürün bu birimle satılmıyor/alınmıyor',
  currency_invalid: 'Para birimi kodu geçersiz',
  currency_mismatch: 'Belgenin para birimiyle uyuşmuyor',
  tax_id_format_invalid: 'VKN 10, TCKN 11 haneli olmalı',
  tax_id_checksum_invalid: 'Kontrol hanesi tutmuyor',
  counterparty_not_matched: 'Cari bulunamadı',
  counterparty_match_unavailable: 'Cari eşlemesi şu an yapılamıyor',
  product_not_matched: 'Ürün bulunamadı (tedarikçi kodu eşlenmez)',
  product_match_ambiguous: 'Birden çok ürün eşleşiyor',
  product_match_unavailable: 'Ürün eşlemesi şu an yapılamıyor',
  section_format_invalid: 'Kesit biçimi okunamadı',
  dimension_not_positive: 'Ölçü sıfırdan büyük olmalı',
  wall_thickness_exceeds_section: 'Et kalınlığı kesite sığmıyor',
  length_unit_unknown: 'Boy birimi tanınmıyor',
  dimension_format_invalid: 'Ölçü biçimi okunamadı',
  dimension_conflict: 'Açıklamadaki ölçüyle çelişiyor',
  line_amount_not_reconciled: 'Miktar × fiyat satır tutarını tutmuyor',
}

export const DRAFT_FINDING_LABEL: Readonly<Record<string, string>> = {
  instruction_like_text: 'Asistana talimat veren metin',
  tool_call_like_syntax: 'Araç çağrısı benzeri içerik',
  role_or_prompt_marker: 'Sohbet rolü işaretleri',
  invisible_characters: 'Görünmez karakterler',
  authority_or_recipient_change: 'Yetki, alıcı veya onay değiştirme isteği',
  model_tool_call_refused: 'Yerel model araç çağırmaya çalıştı; çıktısı kullanılmadı',
}

export const DRAFT_UNREADABLE_LABEL: Readonly<Record<string, string>> = {
  ocr_unavailable: 'Taranmış belge için OCR henüz kurulu değil.',
  ocr_no_text: 'OCR metin bulamadı.',
  no_fields_found: 'Belgede tanınan alan yok.',
  table_header_unknown: 'Tablonun başlık satırı tanınmıyor (ör. urun_kodu, miktar, birim).',
  pdf_invalid: 'PDF dosyası okunamadı.',
}
