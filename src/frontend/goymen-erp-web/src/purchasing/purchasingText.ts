/**
 * Purchasing user text, in Turkish and in its own file (TEK-06). The order detail's texts stay in
 * `./purchaseOrderDetailText.ts` and the goods receipt's in `./goodsReceiptText.ts`.
 * Rendered text is unchanged from the screens' former inline strings.
 */

import type { PurchaseOrderStatus, PurchaseRequestStatus } from './contract'

export const PURCHASE_ORDER_STATUS_LABEL: Record<PurchaseOrderStatus, string> = { Draft: 'Taslak', Approved: 'Onaylandı' }

export const PURCHASE_REQUEST_STATUS_LABEL: Record<PurchaseRequestStatus, string> = { Draft: 'Taslak', Submitted: 'Gönderildi' }

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
  sourceLegend: 'Talep kaynağı',
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
