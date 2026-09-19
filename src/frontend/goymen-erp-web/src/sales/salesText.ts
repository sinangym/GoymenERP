/**
 * User-facing Sales texts. Turkish, in one place, so they stay localizable.
 */

import { APPROVAL_STATE, SALES_PROBLEM_CODE, type ApprovalState, type SalesProblemCode } from './contract'

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
  /** Which reference source is missing and why, from the real answers — no source is guessed. */
  referencesMissing: (missing: readonly { readonly source: string; readonly reason: string }[]) =>
    'Sunulmayan kaynaklar: ' +
    missing
      .map((m) => {
        const name =
          m.source === 'accounts' ? 'cari hesapları' : m.source === 'currencies' ? 'para birimleri' : 'satışa uygun ürünler ve birimler'
        const why = m.reason === 'no-list-route' ? 'liste kaynağı yayımlanmadı' : `gerçek yanıt ${m.reason}`
        return `${name} (${why})`
      })
      .join('; ') +
    '.',
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
  [SALES_PROBLEM_CODE.unsupportedCurrency]: 'Bu para birimi için iş tarihinde onaylı bir para profili yok.',
  [SALES_PROBLEM_CODE.accountState]: 'Seçilen cari hesabı bu carinin değil, bu şirkette değil ya da pasif.',
  [SALES_PROBLEM_CODE.versionConflict]: 'Ürün bilgisi bu seçimden sonra değişti; ürünü yeniden seçin.',
  [SALES_PROBLEM_CODE.concurrentChange]: 'Aynı anda başka bir kayıt öne geçti; aynı gönderimi tekrar deneyin.',
  [SALES_PROBLEM_CODE.commercialNotAvailable]: 'Fiyat ve iskonto bu sürümde kaydedilmez; alanları boş bırakın.',
  [SALES_PROBLEM_CODE.ownerReferenceUnavailable]: 'Adres, kişi ve benzeri referanslar bu sürümde kaydedilmez; alanları boş bırakın.',
  [SALES_PROBLEM_CODE.commandMetadataInvalid]: 'İşlem anahtarı eksik ya da geçersiz; gönderim yapılmadı.',
  [SALES_PROBLEM_CODE.invalidCursor]: 'Liste sayfası imleci geçersiz; listeyi baştan açın.',
  [SALES_PROBLEM_CODE.stateConflict]: 'Bu işlem taslağın şu anki onay durumunda yapılamaz; taslağı yeniden açın.',
  [SALES_PROBLEM_CODE.approvalReasonTooLong]: 'Ret nedeni en fazla 4000 karakter olabilir.',
}

export function problemCodeText(code: string | null): string | null {
  if (code === null) return null
  return Object.hasOwn(PROBLEM_CODE_TEXT, code) ? PROBLEM_CODE_TEXT[code as SalesProblemCode] : null
}

/** SALES-A1: the internal approval axis, one label per `InternalApprovalState`. */
export const APPROVAL_STATE_TEXT: Record<ApprovalState, string> = {
  [APPROVAL_STATE.preparing]: 'Hazırlanıyor',
  [APPROVAL_STATE.pendingApproval]: 'Onay bekliyor',
  [APPROVAL_STATE.approved]: 'Onaylı',
  [APPROVAL_STATE.rejected]: 'Reddedildi',
}

export const ORDER_LIST_TEXT = {
  heading: 'Satış siparişi taslakları',
  newDraft: 'Yeni taslak',
  caption: 'Seçili şirketteki sipariş taslakları, en yeni önce',
  documentNo: 'Belge no',
  businessDate: 'Sipariş tarihi',
  state: 'İç onay',
  filter: 'İç onay durumu',
  filterAll: 'Tümü',
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
  /** SALES-A1: the approval facts and the commands on the detail. */
  approval: {
    heading: 'İç onay',
    state: 'İç onay durumu',
    submittedBy: 'Onaya gönderen',
    submittedAt: 'Gönderim zamanı',
    decidedBy: 'Karar veren',
    decidedAt: 'Karar zamanı',
    rejectionReason: 'Ret nedeni',
    notYet: 'Henüz yok',
    noReason: 'Neden belirtilmedi',
    // D4 (DEC-SAL-005): the linked effects are shown honestly — no consumer exists in this slice.
    linkedEffects: 'Bağlı etki: tahsis 0 / yükleme talimatı 0 (bu dilimde tüketici yok).',
    approvedNote: 'Onaylı taslağın satırları değişmez; değişiklik yeni bir revizyonla gelir (SALES-A2).',
    rejectedNote: 'Reddedilen taslak aynı revizyonla yeniden onaya gönderilebilir; düzeltme SALES-A2 ile gelir.',
    submit: 'Onaya gönder',
    approve: 'Onayla',
    reject: 'Reddet',
    reasonLabel: 'Ret nedeni (isteğe bağlı)',
    submitting: 'İşlem gönderiliyor…',
    unknown:
      'İşlemin sonucu alınamadı; kaydedilmiş olabilir. Aynı işlemi tekrar göndermek ikinci bir geçiş oluşturmaz.',
    refusedAfterUnknown: 'Tekrar deneme reddedildi. Bu, önceki gönderimin uygulanmadığını göstermez.',
    unauthenticated: 'Oturumunuz sona erdi; işlem uygulanmadı.',
    forbidden: 'Bu şirkette bu işlem için yetkiniz yok; işlem uygulanmadı.',
    notFound: 'Taslak bulunamadı; işlem uygulanmadı.',
    conflict: 'İşlem taslağın güncel durumuyla çakıştı; taslak yeniden okundu.',
    /** SALES-A1 409 `version_conflict` on a command: the DRAFT moved after this screen read it (not the S-03 product-version sentence). */
    versionConflict: 'Taslak siz okuduktan sonra değişti; güncel durum gösteriliyor.',
    validation: 'İşlem uygulanmadı; gönderilen bilgi geçerli değil.',
    unexpected: 'İşlem uygulanmadı. Beklenmeyen bir yanıt alındı.',
    completed: 'İşlem kaydedildi.',
    replayed: 'Bu işlem daha önce kaydedilmişti; güncel durum gösteriliyor.',
    retry: 'Aynı işlemi tekrar dene',
    abandon: 'Bu işlemi bırak',
  },
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
