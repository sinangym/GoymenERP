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
    /** SALES-A2a: chosen from the Cari owner's contact information of the chosen account; optional. */
    deliveryAddress: 'Teslimat adresi',
    deliveryContact: 'Teslimat kişisi',
  },
  choose: 'Seçin',
  /** SALES-A2a: the empty choice of an optional reference — nothing is sent, never a default. */
  notChosen: '— seçilmedi —',
  /** SALES-A2a: the header selects' own read states (each real answer, never a guessed option). */
  references: {
    /** #361: the customer select is one page of the Cari owner's list; the search and the continuation are the server's own. */
    accountSearch: 'Cari ara',
    accountSearchHint: 'Kod ya da ada göre; eşleştirmeyi sunucu yapar.',
    accountSearchSubmit: 'Ara',
    accountsMore: 'Daha fazla cari',
    accountsMoreLoading: 'Yükleniyor…',
    accountsMoreFailed: (reason: string) => `Sonraki cariler okunamadı (${reason}); listelenenler yerinde kaldı.`,
    accountsEnd: 'Aramaya uyan tüm cariler listelendi.',
    currencyIdle: 'Para birimleri sipariş tarihine göre listelenir; önce sipariş tarihini girin.',
    currencyLoading: 'Para birimleri okunuyor…',
    currencyEmpty: 'Bu sipariş tarihinde onaylı para birimi yok.',
    currencyUnavailable: (reason: string) => `Para birimleri okunamadı (${reason}).`,
    productSearch: 'Ürün ara',
    productSearchHint: 'Ürün kodu ya da adına göre; eşleştirmeyi ürün servisi yapar.',
    productSearchSubmit: 'Ürünleri getir',
    productIdle: 'Satışa uygun ürünler sipariş tarihine göre listelenir; önce sipariş tarihini girin.',
    productLoading: 'Satışa uygun ürünler okunuyor…',
    productEmpty: 'Bu aramada uygun ürün adayı dönmedi; bu sonuç tüm katalogda uygun ürün olmadığı anlamına gelmez.',
    productUnavailable: (reason: string) => `Satışa uygun ürünler okunamadı (${reason}).`,
    productWindowFull: 'İlk 25 aday gösteriliyor; aradığınız ürün yoksa kod veya adla aramayı daraltın.',
    deliveryIdle: 'Teslimat adresi ve kişisi cari hesabı seçildikten sonra listelenir.',
    deliveryLoading: 'Yükleniyor…',
    noAddress: 'Kayıtlı adres yok',
    noContact: 'Kayıtlı kişi yok',
    deliveryUnavailable: (reason: string) => `Cari iletişim bilgileri okunamadı (${reason}).`,
    retry: 'Tekrar dene',
    /** 409 `version_conflict` on `deliveryAddress` / `contact`: the Cari owner's item moved after the list was read (not the product sentence). */
    versionConflict: 'Cari adres/kişi kaydı siz seçtikten sonra değişti; listeyi yenileyip yeniden seçin.',
    /** Product/date read is not ready; the header stays usable while lines and submit remain withheld. */
    linesWithheld: 'Satışa uygun ürün adayları hazır olana kadar satırlar ve gönderim kapalı.',
  },
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
    deliveryAddressNotOffered: 'Seçilen teslimat adresi bu cari hesabında artık sunulmuyor. Listeden yeniden seçin.',
    contactNotOffered: 'Seçilen teslimat kişisi bu cari hesabında artık sunulmuyor. Listeden yeniden seçin.',
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
  [SALES_PROBLEM_CODE.ownerReferenceUnavailable]: 'Sorumlu, vade, kaynak ve paket referansları bu sürümde kaydedilmez; alanları boş bırakın.',
  [SALES_PROBLEM_CODE.ownerReferenceInvalid]:
    'Seçilen teslimat adresi ya da kişisi bu cari hesabında kayıtlı değil ya da pasif; listeden yeniden seçin.',
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
  [APPROVAL_STATE.withdrawn]: 'Geri çekildi',
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
    // D2 (Sinan 5749046401): the creator's own withdrawal — its own command and trail, nothing deleted.
    withdraw: 'Geri çek',
    withdrawReasonLabel: 'Geri çekme nedeni (isteğe bağlı)',
    withdrawnBy: 'Geri çeken',
    withdrawnAt: 'Geri çekme zamanı',
    withdrawalReason: 'Geri çekme nedeni',
    withdrawnNote: 'Geri çekilen taslak kapalıdır; kayıt ve satırlar silinmez. Yeni sipariş yeni bir taslakla açılır.',
    // D2: the order one PREPARED is decided by someone else; the server refuses regardless of this screen.
    ownDecisionNote: 'Kendi hazırladığınız taslağı onaylayamaz veya reddedemezsiniz; karar başka bir yetkiliye aittir.',
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
  priceRevision: {
    heading: 'Fiyat ve iskonto revizyonu',
    description: 'Her satırın fiyatını ve iskonto yüzdesini birlikte kaydedin. Boş fiyat ücretsiz anlamına gelmez.',
    approvedWarning: 'Onaylı fiyat değişikliği önceki onayı silmez; eski revizyon arşivlenir ve sipariş yeniden onaya girer.',
    withheld: 'Fiyatları görme yetkisi olmadan mevcut ticari değerler güvenle değiştirilemez.',
    stateUnavailable: 'Onay bekleyen veya geri çekilmiş siparişte fiyat revizyonu yapılamaz.',
    unitPrice: 'Birim fiyat',
    priceUnit: 'Fiyat birimi',
    discount: 'İskonto %',
    orderUnit: 'Sipariş birimi',
    currentPriceUnit: 'Mevcut fiyat birimi',
    submit: 'Fiyat ve iskontoyu kaydet',
    submitting: 'Fiyat revizyonu gönderiliyor…',
    unknown: 'İşlemin sonucu alınamadı; kaydedilmiş olabilir. Aynı işlemi aynı anahtarla tekrar deneyin.',
    completed: 'Fiyat revizyonu kaydedildi.',
    replayed: 'Bu fiyat revizyonu daha önce kaydedilmişti; güncel sipariş gösteriliyor.',
    retry: 'Aynı revizyonu tekrar dene',
    abandon: 'Bu denemeyi bırak',
    unauthenticated: 'Oturumunuz sona erdi; fiyat revizyonu uygulanmadı.',
    forbidden: 'Bu şirkette fiyat yazma yetkiniz yok; işlem uygulanmadı.',
    notFound: 'Sipariş taslağı bulunamadı; işlem uygulanmadı.',
    conflict: 'Sipariş siz çalışırken değişti; güncel kayıt yeniden okunuyor.',
    versionConflict: 'Kayıt sürümü değişti; güncel değerleri yeniden inceleyin.',
    validation: 'Fiyat revizyonu uygulanmadı; satır değerlerini kontrol edin.',
    unexpected: 'Fiyat revizyonu uygulanmadı. Beklenmeyen bir yanıt alındı.',
    localError: (lineNo: number, reason: 'discount-without-price' | 'price-unit-required') =>
      reason === 'discount-without-price'
        ? `${lineNo}. satırda iskonto için birim fiyat gerekir.`
        : `${lineNo}. satırda birim fiyat için fiyat birimi gerekir.`,
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
  /** K3 P2: the server withheld the price from this caller — not "no price", not "unknown". */
  priceWithheld: 'Fiyat bu yetkiyle görüntülenmez',
  referenceNote:
    'Cari ve birim adları bu sözleşmede yok; kimlikler gösteriliyor. Taslak onay, stok veya finans etkisi oluşturmaz.',
  /** SALES-A2a: the delivery block — the owner's texts for the stored references, read afresh; never a guessed name. */
  delivery: {
    heading: 'Teslimat',
    currency: 'Para birimi',
    address: 'Teslimat adresi',
    contact: 'Teslimat kişisi',
    notChosen: 'Teslimat adresi/kişisi seçilmedi',
    addressNotChosen: 'Teslimat adresi seçilmedi',
    contactNotChosen: 'Teslimat kişisi seçilmedi',
    reading: 'Okunuyor…',
    notListed: 'Kayıt bulunamadı',
    unreadable: (reason: string) => `Ad okunamadı (${reason})`,
    noGrant: 'Ad okunamıyor (referans okuma yetkisi yok)',
    currencyNotListed: 'Kod okunamadı',
    versionChanged: 'Kayıt sürümü taslaktan sonra değişti.',
  },
  ownerReference: {
    deliveryAddress: 'Teslim adresi',
    contact: 'Muhatap',
    responsible: 'Sorumlu',
    paymentTerms: 'Vade',
    sourceStrategy: 'Kaynak stratejisi',
    package: 'Paket',
  },
} as const

/** S-04: immutable approved snapshots, separate from the editable current revision. */
export const REVISION_HISTORY_TEXT = {
  heading: 'Onaylı ticari revizyon geçmişi',
  subject: 'ticari revizyon geçmişi',
  empty: 'Henüz arşivlenmiş onaylı ticari revizyon yok.',
  note: 'Önceki onaylı revizyonlar, arşivlendikleri bilgilerle ve en eskisi önce gösterilir. Güncel sipariş değiştirilmez.',
  unknown: 'Kayıtlı bilgi yok',
  submittedBy: 'Onaya gönderen',
  submittedAt: 'Onaya gönderilme zamanı',
  approvedBy: 'Onaylayan',
  approvedAt: 'Onay zamanı',
  archivedBy: 'Arşivleyen',
  archivedAt: 'Arşiv zamanı',
  moneyProfile: 'Para profili',
  moneyVersion: 'Para profili sürümü',
  moneyScale: 'Para profili ondalık basamağı',
  caption: (revision: string) => `${revision} — arşivlenmiş sipariş satırları`,
  lineNo: 'Satır',
  lineId: 'Satır kimliği',
  product: 'Ürün kimliği',
  productVersion: 'Ürün sürümü',
  orderUnit: 'Sipariş birimi',
  quantity: 'Miktar',
  unitPrice: 'Birim fiyat',
  priceUnit: 'Fiyat birimi',
  discount: 'İskonto %',
  withheld: 'Yetki nedeniyle gizli',
  notPriced: 'Fiyat girilmemiş',
  notSpecified: 'Belirtilmemiş',
} as const
