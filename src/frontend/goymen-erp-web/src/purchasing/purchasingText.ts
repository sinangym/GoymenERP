/**
 * Purchasing user text, in Turkish and in its own file (TEK-06). The order detail's texts stay in
 * `./purchaseOrderDetailText.ts` and the goods receipt's in `./goodsReceiptText.ts`.
 * Rendered text is unchanged from the screens' former inline strings.
 */

import type { PurchaseOrderStatus, PurchaseRequestStatus, PurchaseRequestSummarySourceKind } from './contract'

/** A1-3 (No.161 §2): Cancelled is an approved order whose unaccepted remainder was cancelled. */
export const PURCHASE_ORDER_STATUS_LABEL: Record<PurchaseOrderStatus, string> = { Draft: 'Taslak', Approved: 'Onaylandı', Cancelled: 'Kalanı iptal edildi' }

/** A1-1: Approved and Rejected are the purchase manager's decisions (ALS-D2, No.161 §1); A1-1b: Withdrawn (ALS-D46). */
export const PURCHASE_REQUEST_STATUS_LABEL: Record<PurchaseRequestStatus, string> = {
  Draft: 'Taslak', Submitted: 'Gönderildi', Approved: 'Onaylandı', Rejected: 'Reddedildi', Withdrawn: 'Geri çekildi',
}

/** A1-2 (plan API-02): where a request line's need came from; Manual is a line without a source. */
export const PURCHASE_REQUEST_SOURCE_LABEL: Record<PurchaseRequestSummarySourceKind, string> = {
  Manual: 'Manuel', Stock: 'Stok', Sales: 'Satış', Production: 'Üretim',
}

/** Shared by the request and order create forms. */
export const CREATE_FORM_TEXT = {
  previousUnknown: 'Önceki gönderimin sonucu bilinmiyor.',
  unknownRetry: 'Sonuç bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin.',
  stillUnknown: 'Önceki gönderimin sonucu hâlâ bilinmiyor.',
  retrySameKey: 'Aynı anahtarla yeniden gönder',
  closeForm: 'Formu kapat',
  replayed: 'Önceki kayıtlı sonuç yeniden gösterildi.',
  openSame: 'Aynı kaydı aç',
  backToList: 'Listeye dön ve formu kapat',
  version: (id: string, version: number | string) => `${id} · sürüm ${version}`,
  businessDate: 'İş tarihi',
  productSearch: 'Ürün ara',
  search: 'Ara',
  choose: 'Seçiniz',
  quantity: 'Miktar',
  saving: 'Kaydediliyor…',
  productsLoading: 'Ürünler yükleniyor…',
  dateBeforeProductSearch: 'Ürün aramadan önce geçerli bir iş tarihi seçin.',
  productSourceHttp: (status: number) => `Ürün kaynağı yanıt vermedi (${status}).`,
  productSourceFailed: 'Ürün kaynağına ulaşılamadı veya yanıt doğrulanamadı.',
  productOption: (code: string, name: string, unitCode: string) => `${code} — ${name} · ${unitCode}`,
} as const

export const REQUEST_LIST_TEXT = {
  eyebrow: 'P-01',
  heading: 'Satın alma talepleri',
  create: 'Yeni talep',
  filter: 'Durum',
  filterAll: 'Tümü',
  subject: 'satın alma talepleri',
  scope: 'İhtiyaç havuzu yalnız talep miktarını gösterir; fiyat, tedarikçi veya stok hareketi üretmez.',
  caption: 'Şirketin satın alma talepleri',
  request: 'Talep',
  status: 'Durum',
  businessDate: 'İş tarihi',
  lineCount: 'Satır',
  source: 'Kaynak',
  /** A body without the A1-2 field: the source is not known, never shown as manual. */
  sourceUnknown: '—',
  updatedAt: 'Güncellendi',
  loadingMore: 'Sonraki sayfa yükleniyor…',
  loadMore: 'Daha fazla göster',
  nextPageSubject: 'sonraki talep sayfası',
  retrySamePage: 'Aynı sayfayı yeniden dene',
} as const

export const REQUEST_CREATE_TEXT = {
  eyebrow: 'P-01',
  heading: 'Yeni satın alma talebi',
  back: '← Satın alma talepleri',
  noContext: 'Yetkili tek şirket bağlamı seçilmeden talep oluşturulamaz.',
  saved: 'Satın alma talebi kaydedildi',
  notSaved: (code: string | number) => `Talep kaydedilmedi (${code}).`,
  linesRequired: 'Her satırda Product kaynağından ürün/alış birimi ve sıfırdan büyük miktar zorunludur.',
  detailsLegend: 'Talep bilgileri',
  manualSource: 'Kaynak: Manuel. Satış, Üretim veya Stok ihtiyacından açılan talep kaynağını o modülün eyleminden alır.',
  linesLegend: 'Talep satırları',
  product: (position: number) => `Ürün ${position}`,
  removeLine: 'Satırı kaldır',
  addLine: 'Satır ekle',
  submit: 'Taslak talebi kaydet',
} as const

export const REQUEST_DETAIL_TEXT = {
  eyebrow: 'P-01-DETAIL',
  heading: 'Satın alma talebi',
  subject: 'satın alma talebi',
  replayed: 'Önceki gönderim yanıtı yeniden gösterildi.',
  submitted: 'Talep gönderildi ve yeniden okundu.',
  notSubmitted: (code: string | number) => `Talep gönderilmedi (${code}). Kayıt yeniden okundu.`,
  returnNav: 'Siparişe geri dönüş',
  returnLink: 'Kaynak siparişe dön',
  request: 'Talep',
  status: 'Durum',
  version: 'Sürüm',
  businessDate: 'İş tarihi',
  linesHeading: 'İhtiyaç satırları',
  lineNo: 'No',
  product: 'Ürün',
  unit: 'Birim',
  quantity: 'Miktar',
  source: 'Kaynak',
  sourceUnknown: '—',
  sourceReference: (documentId: string, documentLineId: string) => `belge …${documentId.slice(-8)} · satır …${documentLineId.slice(-8)}`,
  submitHeading: 'Talebi ihtiyaç havuzuna gönder',
  submitNote: 'Gönderim yalnız talep durumunu değiştirir; sipariş, fiyat veya stok hareketi üretmez.',
  unknown: 'Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar ve sürümle yeniden gönderin.',
  sending: 'Gönderiliyor…',
  retry: 'Aynı gönderimi yeniden dene',
  submit: 'Gönder',
} as const

export const ORDER_LIST_TEXT = {
  heading: 'Satın alma siparişleri',
  subject: 'satın alma siparişleri',
  tableLabel: 'Şirketin satın alma siparişleri',
  create: 'Yeni',
  filter: 'Durum',
  filterAll: 'Tümü',
  scope: 'Bu görünüm fiyat veya stok göstermez; yalnız satın alma taahhüdünün sunucuda kayıtlı durumunu gösterir.',
  order: 'Sipariş',
  status: 'Durum',
  businessDate: 'İş tarihi',
  lineCount: 'Satır',
  updatedAt: 'Güncellendi',
  loadMore: 'Daha fazla göster',
  loadingMore: 'Sonraki sayfa yükleniyor…',
  retrySamePage: 'Aynı sayfayı yeniden dene',
  nextPageSubject: 'sonraki sipariş sayfası',
  filterBySupplier: 'Filtrele',
  supplierFilter: 'Tedarikçi filtresi',
  clearSupplierFilter: 'Tedarikçi filtresini kaldır',
  supplierFilterUnnamed: 'Tedarikçi adı bu listeden okunamadı',
} as const

export const ORDER_CREATE_TEXT = {
  eyebrow: 'P-04',
  heading: 'Yeni satın alma siparişi',
  back: '← Satın alma siparişleri',
  noContext: 'Yetkili tek şirket bağlamı seçilmeden sipariş oluşturulamaz.',
  saved: 'Satın alma siparişi kaydedildi',
  supplierReadDenied: 'Bu şirket için tedarikçi okuma yetkiniz yok.',
  supplierSourceHttp: (status: number) => `Tedarikçi kaynağı yanıt vermedi (${status}).`,
  offline: 'Çevrimdışısınız.',
  fenceStale: 'Şirket veya oturum değişti; sonuç kullanılmadı.',
  supplierMalformed: 'Tedarikçi kaynağı beklenen sözleşmeyle yanıt vermedi.',
  supplierUnreachable: 'Tedarikçi kaynağına ulaşılamadı.',
  createDenied: 'Sipariş oluşturma yetkiniz artık geçerli değil.',
  stateChanged: (code: string) => `Seçilen kayıtların güncel durumu değişti (${code}). Kaynakları yenileyin.`,
  lineRejected: (code: string) => `Ürün, alış birimi veya miktar kabul edilmedi (${code}).`,
  notSaved: (code: string | number) => `Sipariş kaydedilmedi (${code}).`,
  fieldsRequired: 'Tedarikçi hesabı, iş tarihi, ürün/alış birimi ve sıfırdan büyük miktar zorunludur.',
  supplierLegend: 'Tedarikçi',
  supplierSearch: 'Tedarikçi ara',
  suppliersLoading: 'Tedarikçiler yükleniyor…',
  supplierAccount: 'Tedarikçi cari hesabı',
  moreSuppliers: 'Daha fazla tedarikçi',
  lineLegend: 'Sipariş satırı',
  product: 'Ürün ve alış birimi',
  searchWindowFull: 'Arama penceresi doldu; sonucu daraltmak için ürün kodu veya adı yazın.',
  quantityPlaceholder: 'Örn. 1250 veya 1,5',
  submit: 'Taslak siparişi kaydet',
} as const

/**
 * A2a-1 (TEK-01, API-20; ALS-D5 "elle, iskontolu", ALS-D18, ALS-D51/D58/D60): the order form's price fieldset. The net and the
 * line amount are never computed on the screen; the server answers them after the save.
 */
export const ORDER_PRICE_TEXT = {
  legend: 'Fiyat (isteğe bağlı)',
  note: 'Açıkça 0 girilen fiyat bir fiyattır (satır tutarı 0). Fiyat boş bırakılırsa sipariş "fiyat bekliyor" olarak kaydedilir; yine onaylanır ve mal kabulü yapılır, fiyat girilene kadar faturayla eşleşmez. Net fiyat ve satır tutarı kayıttan sonra sunucudan gösterilir.',
  currency: 'Para birimi',
  currencyLoading: 'Para birimleri yükleniyor…',
  currencyUnavailable: 'Para birimi seçenekleri şu an alınamıyor (hizmet kullanılamıyor). Fiyat girilemez; sipariş fiyatsız açılabilir.',
  currencyDenied: 'Para birimi seçeneklerini okuma yetkiniz yok. Fiyat girilemez; sipariş fiyatsız açılabilir.',
  currencyFailed: 'Para birimi seçenekleri okunamadı. Fiyat girilemez; sipariş fiyatsız açılabilir.',
  currencyNone: 'Alış siparişi için tanımlı para birimi yok. Fiyat girilemez; sipariş fiyatsız açılabilir.',
  unitPrice: 'Birim fiyat',
  unitPricePlaceholder: 'Örn. 41,25',
  priceUnit: (unitCode: string) => `Fiyat birimi: ${unitCode} (satırın giriş birimi; birim çevrimi henüz yok)`,
  priceUnitPending: 'Fiyat birimi: ürün seçilince satırın giriş birimi',
  discountKind: 'İskonto',
  discountNone: 'İskonto yok',
  discountPercent: 'Yüzde (%)',
  discountAmount: 'Tutar',
  discountValue: 'İskonto değeri',
  discountPlaceholder: 'Örn. 2,5',
  currencyRequired: 'Birim fiyat girildiyse para birimi seçilmelidir.',
  priceInvalid: 'Birim fiyat 0 veya daha büyük bir sayı olmalıdır (örn. 41,25; açıkça 0 girilebilir).',
  ambiguousSeparator: '"1.250" gibi bir değer bin ayırıcısı mı ondalık mı belirsiz; ondalık için virgül kullanın (örn. 1,25) veya binlik ayırıcı yazmayın (örn. 1250).',
  discountInvalid: 'İskonto değeri geçerli bir sayı olmalıdır; yüzde 0 ile 100 arasındadır.',
  discountWithoutPrice: 'İskonto yalnız birim fiyatla birlikte verilebilir.',
  priceRejected: (code: string) => `Fiyat kabul edilmedi (${ORDER_PRICE_PROBLEM_TEXT[code] ?? code}).`,
} as const

/** A2a-1: the stable price refusals (`lines[i].price`) in Turkish; an unknown code is shown as received. */
export const ORDER_PRICE_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  purchasing_price_currency_required: 'fiyatlı satır para birimi ister',
  purchasing_price_negative: 'birim fiyat negatif olamaz',
  purchasing_price_scale_exceeded: 'para profilinin izin verdiğinden fazla ondalık; yuvarlanmaz',
  purchasing_price_unit_conversion_unavailable: 'fiyat birimi satır biriminden farklı; birim çevrimi yok, tutar hesaplanamıyor',
  purchasing_discount_kind_conflict: 'iskonto ya yüzde ya tutar olur',
  purchasing_discount_percent_out_of_range: 'iskonto yüzdesi 0–100 arasında olmalı',
  purchasing_discount_exceeds_price: 'iskonto tutarı birim fiyatı aşamaz',
  purchasing_price_out_of_range: 'değer izin verilen aralığın dışında',
  unsupported_currency: 'para birimi bu iş tarihinde alış için tanımlı değil',
}

/** A1-1 (plan API-01; ALS-D2, No.161 §1): the request decision control's user texts. */
export const REQUEST_DECISION_TEXT = {
  heading: 'Talep kararı',
  note: 'Onay veya ret yalnız talep durumunu değiştirir; sipariş, fiyat veya stok hareketi üretmez. Talebi hazırlayan veya gönderen karar veremez.',
  approve: 'Talebi onayla',
  approving: 'Onaylanıyor…',
  reject: 'Talebi reddet',
  rejecting: 'Reddediliyor…',
  reasonLabel: 'Ret gerekçesi',
  reasonHint: 'Ret için gerekçe zorunludur (en fazla 4000 karakter).',
  reasonRequired: 'Ret gerekçesi boş olamaz.',
  reasonTooLong: 'Ret gerekçesi 4000 karakteri aşamaz; kısaltın.',
  unknown: 'Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar, sürüm ve gerekçeyle yeniden gönderin.',
  retryApprove: 'Aynı onayı yeniden gönder',
  retryReject: 'Aynı reddi yeniden gönder',
  approved: 'Talep onaylandı ve yeniden okundu.',
  rejected: 'Talep reddedildi ve yeniden okundu.',
  replayed: 'Önceki karar yanıtı yeniden gösterildi.',
  failed: 'Karar kaydedilmedi',
  reread: 'Kayıt yeniden okundu.',
  approvalFacts: 'Onay',
  rejectionFacts: 'Ret',
  reasonFacts: 'Ret gerekçesi',
} as const

/** Known refusal codes in user words; an unknown code is shown as-is beside the generic text. */
export const REQUEST_DECISION_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  version_conflict: 'Talep bu sırada değişti; güncel sürüm yeniden okundu.',
  purchasing_request_decision_state: 'Talep bu karar için uygun durumda değil.',
  purchasing_request_same_actor: 'Talebi hazırlayan veya gönderen aynı talep için karar veremez.',
  purchasing_request_maker_unknown: 'Talebi hazırlayan veya gönderen bilinmiyor; karar verilemez.',
  purchasing_request_has_approved_order: 'Talebe bağlı onaylı sipariş var; önce bağlı sipariş iptal edilmelidir.',
  purchasing_request_rejection_reason_required: 'Ret gerekçesi zorunludur.',
  purchasing_request_rejection_reason_too_long: 'Ret gerekçesi 4000 karakteri aşamaz.',
  concurrent_change: 'Talep aynı anda başka bir işlemle değişti; kayıt yeniden okundu.',
  same_key_payload_mismatch: 'Aynı işlem anahtarı başka bir içerikle kullanıldı.',
}

/** A1-1b (plan API-01b; ALS-D46, ALS-D47): the preparer's or submitter's withdrawal control's user texts. */
export const REQUEST_WITHDRAWAL_TEXT = {
  heading: 'Talebi geri çek',
  note: 'Oluşturduğunuz veya onaya gönderdiğiniz talebi, henüz karar verilmemişken gerekçeyle geri çekebilirsiniz. Geri çekilen talep kapanır, siparişe dönüşmez ve ret sayılmaz; sipariş, fiyat veya stok hareketi üretmez.',
  withdraw: 'Geri çek',
  withdrawing: 'Geri çekiliyor…',
  reasonLabel: 'Geri çekme gerekçesi',
  reasonHint: 'Geri çekme için gerekçe zorunludur (en fazla 4000 karakter).',
  reasonRequired: 'Geri çekme gerekçesi boş olamaz.',
  reasonTooLong: 'Geri çekme gerekçesi 4000 karakteri aşamaz; kısaltın.',
  unknown: 'Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar, sürüm ve gerekçeyle yeniden gönderin.',
  retry: 'Aynı geri çekmeyi yeniden gönder',
  withdrawn: 'Talep geri çekildi ve yeniden okundu.',
  replayed: 'Önceki geri çekme yanıtı yeniden gösterildi.',
  failed: 'Talep geri çekilmedi',
  reread: 'Kayıt yeniden okundu.',
  withdrawalFacts: 'Geri çekme',
  reasonFacts: 'Geri çekme gerekçesi',
} as const

/** A1-3 (plan API-10/UI-05; No.161 §2): the order remainder cancellation control's user texts. */
export const ORDER_CANCELLATION_TEXT = {
  heading: 'Kabul edilmemiş kalanı iptal et',
  note: 'Onaylı siparişin henüz kabul edilmemiş kalanının tamamı iptal edilir; kısmi kabul yapılmışsa kabul edilen miktar olduğu gibi kalır. Satır ya da miktar seçilmez. İptal yalnız beklenen arzı ve sipariş kalanını düşürür; stok, mal kabul veya para hareketi üretmez. Sipariş kaynağı olan talep bağı korunur.',
  impactHeading: 'İptal etkisi',
  impactCaption: 'İptal edilecek kalan, satır bazında',
  impactLoading: 'İptal etkisi okunuyor…',
  impactUnavailable: 'İptal etkisi okunamadı. Yeniden deneyin.',
  impactRetry: 'Etkiyi yeniden oku',
  impactStale: 'Sipariş, iptal etkisi okunduktan sonra değişti. Kaydı ve etkiyi yeniden okuyun.',
  partialAcceptance: 'Bu siparişte kısmi kabul var; kabul edilen miktar iptal edilmez.',
  lineNo: 'Satır',
  committed: 'Taahhüt',
  accepted: 'Kabul',
  toCancel: 'İptal edilecek',
  blocked: 'Bu sipariş iptal edilemez',
  reasonLabel: 'İptal gerekçesi',
  reasonHint: 'İptal için gerekçe zorunludur (en fazla 4000 karakter).',
  reasonRequired: 'İptal gerekçesi boş olamaz.',
  reasonTooLong: 'İptal gerekçesi 4000 karakteri aşamaz; kısaltın.',
  cancel: 'Kalanı iptal et',
  cancelling: 'İptal ediliyor…',
  unknown: 'Sonuç bilinmiyor. Yeni işlem başlatmadan aynı anahtar, sürüm ve gerekçeyle yeniden gönderin.',
  retry: 'Aynı iptali yeniden gönder',
  cancelled: 'Siparişin kabul edilmemiş kalanı iptal edildi ve kayıt yeniden okundu.',
  replayed: 'Önceki iptal yanıtı yeniden gösterildi.',
  failed: 'İptal kaydedilmedi',
  reread: 'Kayıt yeniden okundu.',
  cancellationHeading: 'Kalan iptali',
  cancelledBy: 'İptal eden',
  cancelledAt: 'İptal zamanı (UTC)',
  cancellationReason: 'İptal gerekçesi',
} as const

/** A1-3: known cancellation refusal and impact blocker codes in user words; an unknown code is shown as-is beside the generic text. */
export const ORDER_CANCELLATION_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  version_conflict: 'Sipariş bu sırada değişti; güncel sürüm yeniden okundu.',
  purchasing_order_state: 'Yalnız onaylı siparişin kalanı iptal edilir.',
  purchasing_order_not_cancellable: 'Yalnız onaylı siparişin kalanı iptal edilir.',
  purchasing_order_nothing_to_cancel: 'Siparişin tamamı kabul edilmiş; iptal edilecek kalan yok.',
  purchasing_order_cancellation_reason_required: 'İptal gerekçesi zorunludur.',
  purchasing_order_cancellation_reason_too_long: 'İptal gerekçesi 4000 karakteri aşamaz.',
  concurrent_change: 'Sipariş aynı anda başka bir işlemle değişti; kayıt yeniden okundu.',
  same_key_payload_mismatch: 'Aynı işlem anahtarı başka bir içerikle kullanıldı.',
}

/** Known withdrawal refusal codes in user words; an unknown code is shown as-is beside the generic text. */
export const REQUEST_WITHDRAWAL_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  version_conflict: 'Talep bu sırada değişti; güncel sürüm yeniden okundu.',
  purchasing_request_decision_state: 'Yalnız gönderilmiş ve henüz karar verilmemiş talep geri çekilir.',
  purchasing_request_not_preparer: 'Talebi yalnız oluşturan veya gönderen kişi geri çekebilir.',
  purchasing_request_maker_unknown: 'Talebi oluşturan ve gönderen bilinmiyor; talep geri çekilemez.',
  purchasing_request_has_approved_order: 'Talebe bağlı onaylı sipariş var; önce bağlı sipariş iptal edilmelidir.',
  purchasing_request_withdrawal_reason_required: 'Geri çekme gerekçesi zorunludur.',
  purchasing_request_withdrawal_reason_too_long: 'Geri çekme gerekçesi 4000 karakteri aşamaz.',
  concurrent_change: 'Talep aynı anda başka bir işlemle değişti; kayıt yeniden okundu.',
  same_key_payload_mismatch: 'Aynı işlem anahtarı başka bir içerikle kullanıldı.',
}
