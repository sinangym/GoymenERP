/**
 * User-facing Sales texts. Turkish, in one place, so they stay localizable.
 */

import { SALES_PROBLEM_CODE, type SalesProblemCode } from './contract'

export const ORDER_DRAFT_TEXT = {
  heading: 'Yeni satış siparişi taslağı',
  field: {
    customer: 'Cari ve şirket hesabı',
    businessDate: 'Sipariş tarihi',
    currency: 'Para birimi',
    requestedDeliveryDate: 'Üst termin',
    note: 'İç açıklama',
    lines: 'Sipariş satırları',
    product: 'Ürün',
    quantity: 'Miktar',
    orderUnit: 'Sipariş birimi',
    lineDeliveryDate: 'Satır termini',
    lineNote: 'Satır açıklaması',
    slices: 'Termin dilimleri',
    sliceQuantity: 'Dilim miktarı',
    sliceDate: 'Dilim termini',
  },
  choose: 'Seçin',
  addLine: 'Satır ekle',
  removeLine: 'Satırı kaldır',
  addSlice: 'Termin dilimi ekle',
  removeSlice: 'Dilimi kaldır',
  unplanned: (quantity: string) => `Planlanmamış miktar: ${quantity}`,
  unplannedNote: 'Planlanmamış miktar teslim edilmiş miktar değildir.',
  save: 'Taslak olarak kaydet',
  errorSummary: 'Taslak gönderilmedi. Şu alanları düzeltin:',
  /**
   * The same summary while an earlier send is still unresolved. "Gönderilmedi" would be
   * true of this click and false about the draft: the earlier send may already be saved.
   */
  errorSummaryUnresolved:
    'Şu alanları düzeltin. Önceki gönderimin sonucu hâlâ bilinmiyor; taslak kaydedilmiş olabilir.',
  referencesUnavailable:
    'Cari hesabı, satışa uygun ürün, birim ve para birimi seçenekleri henüz yayımlanmadı. Seçenekler olmadan taslak oluşturulamaz.',
  validation: {
    customerRequired: 'Cari ve bu şirketteki hesabını seçin.',
    customerNotOffered: 'Seçilen cari hesabı artık seçilebilir değil. Listeden yeniden seçin.',
    businessDateRequired: 'Sipariş tarihini girin.',
    dateInvalid: 'Takvimde var olan bir tarih girin.',
    currencyRequired: 'Para birimini seçin.',
    currencyNotOffered: 'Seçilen para birimi artık seçilebilir değil.',
    noteTooLong: 'Açıklama en fazla 4000 karakter olabilir.',
    linesRequired: 'En az bir sipariş satırı ekleyin.',
    tooManyLines: 'Bir taslakta en fazla 500 satır olabilir.',
    productRequired: 'Ürünü seçin.',
    productNotOffered: 'Seçilen ürün satışa kullanılabilir değil. Listeden yeniden seçin.',
    quantityRequired: 'Miktarı girin.',
    quantityInvalid: 'Miktar yalnız rakam ve tek ondalık ayırıcı içerebilir.',
    quantityAmbiguous:
      'Nokta binlik ayırıcı mı ondalık mı anlaşılmıyor. 1000 ya da 1,5 biçiminde yazın.',
    quantityNotPositive: 'Miktar sıfırdan büyük olmalı.',
    orderUnitRequired: 'Sipariş birimini seçin.',
    orderUnitNotOffered: 'Seçilen birim bu ürün için kullanılamıyor.',
    sliceDateRequired: 'Dilimin terminini girin.',
    tooManySlices: 'Bir satırda en fazla 500 termin dilimi olabilir.',
    slicesExceedOrder: 'Termin dilimlerinin toplamı satır miktarını aşıyor.',
  },
  attempt: {
    submitting: 'Sipariş taslağı kaydediliyor…',
    unknown:
      'Kaydın sonucu alınamadı; taslak kaydedilmiş olabilir. Aynı işlemi tekrar göndermek ikinci sipariş oluşturmaz.',
    unknownChangedBlocked:
      'Önceki gönderimin sonucu hâlâ bilinmiyor. Değişikliklerinizi göndermeden önce önceki işlemi tekrar deneyin ya da bırakın.',
    abandoned:
      'Önceki gönderim bırakıldı ama sonucu bilinmiyor; kaydedilmiş olabilir. Siparişler listesinden kontrol edin.',
    refusedAfterUnknown:
      'Tekrar deneme reddedildi. Bu, önceki gönderimin kaydedilmediğini göstermez.',
    validation: 'Taslak kaydedilmedi. İşaretli alanları düzeltin.',
    unauthenticated: 'Oturumunuz sona erdi; taslak kaydedilmedi.',
    forbidden: 'Bu şirkette sipariş taslağı oluşturma yetkiniz yok; taslak kaydedilmedi.',
    notFound: 'Seçilen kayıtlardan biri bulunamadı; taslak kaydedilmedi.',
    conflict: 'İşlem mevcut kayıtla çakıştı; taslak kaydedilmedi.',
    unexpected: 'Taslak kaydedilmedi. Beklenmeyen bir yanıt alındı.',
    completed: 'Sipariş taslağı kaydedildi.',
    replayed: 'Bu işlem daha önce kaydedilmişti; aynı taslak gösteriliyor.',
    retry: 'Aynı işlemi tekrar dene',
    abandon: 'Bu işlemi bırak',
  },
  summary: {
    heading: 'Kaydetmeden önce',
    company: 'Kaydedilecek şirket',
    notPriced: 'Henüz fiyatlandırılmadı',
    notApproved: 'Onaysız taslak',
    noEffects: 'Taslak kaydı onay, tahsis, stok, irsaliye, fatura veya tahsilat oluşturmaz.',
  },
} as const

/** Messages for the stable codes in `SalesDraftFieldCodesV1`. Never parsed from `title`. */
export const PROBLEM_CODE_TEXT: Record<SalesProblemCode, string> = {
  [SALES_PROBLEM_CODE.validationFailed]: 'Gönderilen bilgi geçerli değil.',
  [SALES_PROBLEM_CODE.companyMismatch]: 'Seçilen cari hesabı bu şirkete ait değil.',
  [SALES_PROBLEM_CODE.passiveCompanyAccount]: 'Seçilen cari hesabı pasif; yeni işlemde kullanılamaz.',
  [SALES_PROBLEM_CODE.unsupportedProduct]: 'Bu ürün satışa kullanılabilir değil.',
  [SALES_PROBLEM_CODE.unsupportedUnit]: 'Bu birim bu ürün için kullanılamıyor.',
  [SALES_PROBLEM_CODE.invalidQuantity]: 'Miktar geçerli değil.',
  [SALES_PROBLEM_CODE.invalidDecimalText]: 'Sayı biçimi geçerli değil.',
  [SALES_PROBLEM_CODE.sameKeyPayloadMismatch]:
    'Aynı işlem anahtarı farklı bir içerikle kullanılmış; bu gönderim uygulanmadı.',
  [SALES_PROBLEM_CODE.settingsProviderUnavailable]:
    'Birim veya para birimi ayarlarına şu anda ulaşılamıyor.',
  [SALES_PROBLEM_CODE.settingsAccessDenied]: 'Birim veya para birimi ayarlarını okuma yetkiniz yok.',
}

export function problemCodeText(code: string | null): string | null {
  if (code === null) return null
  return Object.hasOwn(PROBLEM_CODE_TEXT, code) ? PROBLEM_CODE_TEXT[code as SalesProblemCode] : null
}

export const ORDER_LIST_TEXT = {
  heading: 'Satış siparişi taslakları',
  newDraft: 'Yeni taslak',
  caption: 'Seçili şirketteki sipariş taslakları, en yeni önce',
  documentNo: 'Belge no',
  businessDate: 'Sipariş tarihi',
  state: 'Durum',
  revision: 'Revizyon',
  lineCount: 'Satır',
  createdAt: 'Oluşturulma',
  loadMore: 'Daha fazla göster',
  loadingMore: 'Yükleniyor…',
  end: 'Listenin sonu.',
  nextPageFailed: 'Sonraki sayfa alınamadı. Gösterilen satırlar geçerli.',
} as const

export const ORDER_DETAIL_TEXT = {
  backToList: 'Sipariş taslakları',
  state: 'Durum',
  stateDraft: 'Taslak',
  revision: 'Ticari revizyon',
  version: 'Kayıt sürümü',
  businessDate: 'Sipariş tarihi',
  createdAt: 'Oluşturulma',
  party: 'Cari kimliği',
  account: 'Şirket hesabı kimliği',
  currency: 'Para birimi kimliği',
  requestedDeliveryDate: 'Üst termin',
  note: 'İç açıklama',
  commercialHeading: 'Ticari bilgiler',
  linesHeading: 'Sipariş satırları',
  lineNo: '#',
  product: 'Ürün',
  productVersion: 'Ürün sürümü',
  quantity: 'Miktar',
  orderUnit: 'Sipariş birimi kimliği',
  lineDeliveryDate: 'Satır termini',
  price: 'Birim fiyat',
  discount: 'İskonto %',
  slicesHeading: 'Termin dilimleri',
  sliceDate: 'Termin',
  sliceQuantity: 'Miktar',
  unplanned: 'Planlanmamış',
  planUnknown: 'Termin planı girilmedi',
  planEmpty: 'Planlanmış dilim yok',
  unknown: 'Bilinmiyor',
  notPriced: 'Fiyatlandırılmadı',
  referenceNote:
    'Cari, birim ve para birimi adları bu sözleşmede yok; kimlikler gösteriliyor. Taslak onay, stok veya finans etkisi oluşturmaz.',
  ownerReference: {
    deliveryAddress: 'Teslim adresi',
    contact: 'Muhatap',
    responsible: 'Sorumlu',
    paymentTerms: 'Vade',
    sourceStrategy: 'Kaynak stratejisi',
    package: 'Paket',
  },
} as const
