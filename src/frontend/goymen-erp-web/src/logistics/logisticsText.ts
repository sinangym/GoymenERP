/**
 * User-facing Logistics texts. Turkish, in one place, so they stay localizable. No price, currency
 * or finance word appears on these screens (Business 5743412166, the Sevkiyatçı rule).
 */

import {
  INSTRUCTION_STATUS,
  LOGISTICS_PROBLEM_CODE,
  MEASUREMENT,
  SHARE_KIND,
  type InstructionStatus,
  type LogisticsProblemCode,
  type Measurement,
  type ShareKind,
} from './contract'

export const INSTRUCTION_STATUS_TEXT: Record<InstructionStatus, string> = {
  [INSTRUCTION_STATUS.draft]: 'Taslak',
  [INSTRUCTION_STATUS.issued]: 'Yayımlandı',
}

export const MEASUREMENT_TEXT: Record<Measurement, string> = {
  [MEASUREMENT.measured]: 'Tartıldı',
  [MEASUREMENT.allocatedFromVehicleWeight]: 'Araç tartısından pay edildi',
}

export const SHARE_KIND_TEXT: Record<ShareKind, string> = {
  [SHARE_KIND.loaded]: 'Yüklendi',
  [SHARE_KIND.shipped]: 'Sevk edildi',
  [SHARE_KIND.delivered]: 'Teslim edildi',
}

export const INSTRUCTION_LIST_TEXT = {
  heading: 'Yükleme talimatları',
  newInstruction: 'Yeni yükleme talimatı',
  caption: 'Seçili şirketteki yükleme talimatları, en yeni önce',
  filter: 'Durum',
  filterAll: 'Tümü',
  instruction: 'Talimat',
  status: 'Durum',
  effectiveOn: 'İş tarihi',
  customer: 'Müşteri kimliği',
  lineCount: 'Satır',
  createdAt: 'Oluşturulma',
  loadMore: 'Daha fazla göster',
  loadingMore: 'Yükleniyor…',
  end: 'Listenin sonu.',
  nextPageFailed: 'Sonraki sayfa alınamadı. Gösterilen satırlar geçerli.',
} as const

export const INSTRUCTION_CREATE_TEXT = {
  heading: 'Yeni yükleme talimatı',
  backToList: 'Yükleme talimatları',
  singleCompanyOnly: 'Yükleme talimatı yalnız tek bir şirket seçiliyken oluşturulabilir.',
  field: {
    effectiveOn: 'İş tarihi',
    customer: 'Müşteri (onaylı siparişlerden)',
    lines: 'Onaylı sipariş satırları',
    instructed: 'Talimat miktarı',
  },
  choose: 'Seçin',
  sourcesLoading: 'Onaylı sipariş satırları okunuyor…',
  moreLines: 'Daha fazla satır göster',
  moreLinesFailed: 'Sonraki satırlar alınamadı. Gösterilen satırlar geçerli.',
  noApprovedDrafts: 'Bu şirkette onaylı sipariş satırı yok. Talimat yalnız onaylı bir satıra açılır.',
  noLinesForCustomer: 'Seçilen müşterinin onaylı siparişinde satır bulunamadı.',
  sourcesFailed: 'Onaylı sipariş satırları okunamadı. Seçenek olmadan talimat oluşturulamaz.',
  sourcesForbidden: 'Onaylı sipariş satırlarını okuma yetkiniz yok; talimat için satır seçilemez.',
  picker: {
    pick: 'Seç',
    document: 'Sipariş',
    lineNo: 'Satır',
    product: 'Ürün',
    ordered: 'Sipariş miktarı',
    unit: 'Birim',
    requestedDeliveryDate: 'Termin',
    instructed: 'Talimat miktarı',
    note: 'Satır kaynağı Sipariş modülünün onaylı satırıdır; ürün ve müşteri sunucuda Sipariş sahibinden okunur. Bu okuma yalnız kimlik, ad, birim ve miktar taşır.',
  },
  save: 'Talimatı oluştur',
  errorSummary: 'Talimat gönderilmedi. Şu alanları düzeltin:',
  errorSummaryUnresolved:
    'Şu alanları düzeltin. Önceki gönderimin sonucu hâlâ bilinmiyor; talimat oluşturulmuş olabilir.',
  validation: {
    effectiveOnRequired: 'İş tarihini girin.',
    dateInvalid: 'Takvimde var olan bir tarih girin.',
    customerRequired: 'Müşteriyi seçin.',
    customerNotOffered: 'Seçilen müşteri artık seçilebilir değil. Listeden yeniden seçin.',
    linesRequired: 'En az bir onaylı sipariş satırı seçin.',
    tooManyLines: 'Bir talimatta en fazla 500 satır olabilir.',
    quantityRequired: 'Talimat miktarını girin.',
    quantityInvalid: 'Miktar yalnız rakam ve tek ondalık ayırıcı içerebilir.',
    quantityAmbiguous: 'Nokta binlik ayırıcı mı ondalık mı anlaşılmıyor. 1000 ya da 1,5 biçiminde yazın.',
    quantityNotPositive: 'Miktar sıfırdan büyük olmalı.',
    quantityScale: 'Kilogram en fazla 3 ondalık taşıyabilir.',
    quantityTooWide: 'Miktar en fazla 15 tam sayı basamağı taşıyabilir.',
  },
  attempt: {
    submitting: 'Yükleme talimatı oluşturuluyor…',
    unknown:
      'Kaydın sonucu alınamadı; talimat oluşturulmuş olabilir. Aynı işlemi tekrar göndermek ikinci talimat oluşturmaz.',
    unknownChangedBlocked:
      'Önceki gönderimin sonucu hâlâ bilinmiyor. Değişikliklerinizi göndermeden önce önceki işlemi tekrar deneyin ya da bırakın.',
    abandoned:
      'Önceki gönderim bırakıldı ama sonucu bilinmiyor; oluşturulmuş olabilir. Yükleme talimatları listesinden kontrol edin.',
    refusedAfterUnknown: 'Tekrar deneme reddedildi. Bu, önceki gönderimin kaydedilmediğini göstermez.',
    validation: 'Talimat oluşturulmadı. İşaretli alanları düzeltin.',
    unauthenticated: 'Oturumunuz sona erdi; talimat oluşturulmadı.',
    forbidden: 'Bu şirkette yükleme talimatı oluşturma yetkiniz yok; talimat oluşturulmadı.',
    notFound: 'Seçilen kayıtlardan biri bulunamadı; talimat oluşturulmadı.',
    conflict: 'İşlem mevcut kayıtla çakıştı; talimat oluşturulmadı.',
    unexpected: 'Talimat oluşturulmadı. Beklenmeyen bir yanıt alındı.',
    completed: 'Yükleme talimatı oluşturuldu.',
    replayed: 'Bu işlem daha önce kaydedilmişti; aynı talimat gösteriliyor.',
    retry: 'Aynı işlemi tekrar dene',
    abandon: 'Bu işlemi bırak',
    openInstruction: 'Talimatı aç',
  },
  summary: {
    heading: 'Oluşturmadan önce',
    company: 'Kaydedilecek şirket',
    draft: 'Taslak olarak açılır; yayımlama ayrı bir işlemdir.',
    noEffects: 'Talimat stok, tahsis, sevk, irsaliye veya fatura oluşturmaz; yükleme payları fiziksel etki yazmaz.',
  },
} as const

export const INSTRUCTION_DETAIL_TEXT = {
  backToList: 'Yükleme talimatları',
  heading: 'Yükleme talimatı',
  status: 'Durum',
  version: 'Kayıt sürümü',
  effectiveOn: 'İş tarihi',
  createdAt: 'Oluşturulma',
  updatedAt: 'Güncelleme',
  party: 'Müşteri kimliği',
  account: 'Şirket hesabı kimliği',
  referenceNote:
    'Müşteri, ürün ve birim adları bu sözleşmede yok; kimlikler gösteriliyor. Talimat stok, tahsis, sevk veya finans etkisi oluşturmaz.',
  linesHeading: 'Satırlar',
  lineNo: '#',
  sourceOrder: 'Kaynak sipariş satırı',
  revision: 'Revizyon',
  product: 'Ürün kimliği',
  unit: 'Birim kimliği',
  instructed: 'Talimat',
  loaded: 'Yüklenen',
  shipped: 'Sevk edilen',
  delivered: 'Teslim edilen',
  remainingToLoad: 'Yüklenecek kalan',
  blocked: 'Bloklu',
  sharesHeading: 'Paylar',
  noShares: 'Henüz pay kaydedilmedi.',
  shareKind: 'Tür',
  shareLine: 'Satır',
  shareQuantity: 'Miktar',
  shareMeasurement: 'Ölçüm',
  shareRecordedAt: 'Kayıt zamanı',
  issue: {
    heading: 'Yayımlama',
    note: 'Taslak talimat yayımlanınca yükleme payı kaydedilebilir. Yayımlama fiziksel etki yazmaz.',
    issue: 'Yayımla',
  },
  load: {
    heading: 'Yükleme payı kaydet',
    note: 'Yükleme payı talimat kalanını aşamaz; bloklu satıra pay kaydedilmez. Sunucu yetkiyi her gönderimde yeniden ölçer.',
    line: 'Satır',
    quantity: 'Yüklenen miktar',
    measurement: 'Ölçüm',
    record: 'Payı kaydet',
    draftOnly: 'Yükleme payı yalnız yayımlanmış talimata kaydedilir.',
    blockedLine: 'Bu satır bloklu; yeni pay kaydedilmez.',
  },
  command: {
    submitting: 'İşlem gönderiliyor…',
    unknown:
      'İşlemin sonucu alınamadı; kaydedilmiş olabilir. Aynı işlemi tekrar göndermek ikinci bir pay veya geçiş oluşturmaz.',
    refusedAfterUnknown: 'Tekrar deneme reddedildi. Bu, önceki gönderimin uygulanmadığını göstermez.',
    unauthenticated: 'Oturumunuz sona erdi; işlem uygulanmadı.',
    forbidden: 'Bu şirkette bu işlem için yetkiniz yok; işlem uygulanmadı.',
    notFound: 'Talimat veya satır bulunamadı; işlem uygulanmadı.',
    conflict: 'İşlem talimatın güncel durumuyla çakıştı; talimat yeniden okundu.',
    validation: 'İşlem uygulanmadı; gönderilen bilgi geçerli değil.',
    unexpected: 'İşlem uygulanmadı. Beklenmeyen bir yanıt alındı.',
    completed: 'İşlem kaydedildi.',
    replayed: 'Bu işlem daha önce kaydedilmişti; güncel durum gösteriliyor.',
    retry: 'Aynı işlemi tekrar dene',
    abandon: 'Bu işlemi bırak',
  },
  unknown: 'Bilinmiyor',
} as const

/** Messages for the stable codes the routes answer (`LogisticsProblemCodesV1`). Never parsed from `title`. */
export const PROBLEM_CODE_TEXT: Record<LogisticsProblemCode, string> = {
  [LOGISTICS_PROBLEM_CODE.validationFailed]: 'Gönderilen bilgi geçerli değil.',
  [LOGISTICS_PROBLEM_CODE.commandMetadataInvalid]: 'İşlem anahtarı eksik ya da geçersiz; gönderim yapılmadı.',
  [LOGISTICS_PROBLEM_CODE.invalidCursor]: 'Liste sayfası imleci geçersiz; listeyi baştan açın.',
  [LOGISTICS_PROBLEM_CODE.accountState]: 'Seçilen müşteri hesabı bu carinin değil, bu şirkette değil ya da pasif.',
  [LOGISTICS_PROBLEM_CODE.orderLineState]:
    'Seçilen sipariş satırı onaylı değil, bu müşterinin değil ya da bu şirkette bulunamadı.',
  [LOGISTICS_PROBLEM_CODE.versionConflict]: 'Talimat siz okuduktan sonra değişti; güncel durum gösteriliyor.',
  [LOGISTICS_PROBLEM_CODE.instructionState]:
    'Bu işlem talimatın şu anki durumunda yapılamaz (taslak pay almaz; yayımlanmış talimat yeniden yayımlanmaz).',
  [LOGISTICS_PROBLEM_CODE.loadExceedsInstructed]: 'Yükleme payı talimat kalanını aşıyor; pay kaydedilmedi.',
  [LOGISTICS_PROBLEM_CODE.lineBlocked]: 'Satır bloklu; yeni yükleme payı kaydedilmez.',
  [LOGISTICS_PROBLEM_CODE.duplicateShare]: 'Bu yükleme kaydı daha önce işlendi; ikinci kez kaydedilmedi.',
  [LOGISTICS_PROBLEM_CODE.sameKeyPayloadMismatch]:
    'Aynı işlem anahtarı farklı bir içerikle kullanılmış; bu gönderim uygulanmadı.',
  [LOGISTICS_PROBLEM_CODE.concurrentChange]: 'Aynı anda başka bir kayıt öne geçti; aynı gönderimi tekrar deneyin.',
  [LOGISTICS_PROBLEM_CODE.unsupportedUnit]:
    'Bu birim bu şirkette iş tarihinde onaylı bir kilogram profili değil ya da miktar ondalığı profili aşıyor.',
  [LOGISTICS_PROBLEM_CODE.serviceUnavailable]: 'Sunucu şu anda yanıt veremiyor; işlem uygulanmış olabilir.',
}

export function problemCodeText(code: string | null): string | null {
  if (code === null) return null
  return Object.hasOwn(PROBLEM_CODE_TEXT, code) ? PROBLEM_CODE_TEXT[code as LogisticsProblemCode] : null
}
