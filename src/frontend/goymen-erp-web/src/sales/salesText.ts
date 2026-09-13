/**
 * User-facing Sales texts. Turkish, in one place, so they stay localizable.
 */

export const ORDER_DRAFT_TEXT = {
  field: {
    customer: 'Cari ve şirket hesabı',
    businessDate: 'Sipariş tarihi',
    currency: 'Para birimi',
    lines: 'Sipariş satırları',
    product: 'Ürün',
    quantity: 'Miktar',
    orderUnit: 'Sipariş birimi',
    requestedDeliveryDate: 'Termin',
  },
  validation: {
    customerRequired: 'Cari ve bu şirketteki hesabını seçin.',
    customerNotOffered: 'Seçilen cari hesabı artık seçilebilir değil. Listeden yeniden seçin.',
    businessDateRequired: 'Sipariş tarihini girin.',
    dateInvalid: 'Takvimde var olan bir tarih girin.',
    currencyRequired: 'Para birimini seçin.',
    currencyNotOffered: 'Seçilen para birimi artık seçilebilir değil.',
    linesRequired: 'En az bir sipariş satırı ekleyin.',
    productRequired: 'Ürünü seçin.',
    productNotOffered: 'Seçilen ürün satışa kullanılabilir değil. Listeden yeniden seçin.',
    quantityRequired: 'Miktarı girin.',
    quantityInvalid: 'Miktar yalnız rakam ve tek ondalık ayırıcı içerebilir.',
    quantityAmbiguous:
      'Nokta binlik ayırıcı mı ondalık mı anlaşılmıyor. 1000 ya da 1,5 biçiminde yazın.',
    quantityNotPositive: 'Miktar sıfırdan büyük olmalı.',
    orderUnitRequired: 'Sipariş birimini seçin.',
    orderUnitNotOffered: 'Seçilen birim bu ürün için kullanılamıyor.',
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
    retry: 'Aynı işlemi tekrar dene',
    abandon: 'Bu işlemi bırak',
  },
  /** §3: before saving, the open company and the un-priced / un-approved state are shown. */
  summary: {
    company: 'Kaydedilecek şirket',
    notPriced: 'Henüz fiyatlandırılmadı',
    notApproved: 'Onaysız taslak',
    noEffects:
      'Taslak kaydı onay, tahsis, stok, irsaliye, fatura veya tahsilat oluşturmaz.',
  },
} as const
