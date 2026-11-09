/**
 * UI-62 / UI-64 (API-32 / API-33): the Turkish texts of the invoice draft create on S-08 and the draft's submit / approve /
 * reject / cancel controls. Every amount on these screens is the server's text; nothing here describes a calculation the screen
 * makes, and no text names a role — the server decides who may approve (SAT-D88 / SAT-D204).
 */

export const INVOICE_DRAFT_CREATE_TEXT = {
  heading: 'Fatura taslağı',
  intro:
    'Seçili sevk payları tek fatura taslağına alınır; grubu bölmek için payların bir kısmını seçebilirsiniz. Kısmi miktar bu ' +
    'adımda seçilmez. Tutarları, kuru ve vadeyi sunucu hesaplar; resmî fatura dış programda kesilir.',
  selectShare: (dispatch: string) => `${dispatch} sevk payını faturaya al`,
  selectAll: 'Tümünü seç',
  selectNone: 'Seçimi temizle',
  selectedCount: (selected: number, total: number) => `${total} sevk payından ${selected} tanesi seçili.`,
  noneSelected: 'Fatura taslağı için en az bir sevk payı seçin.',
  tooMany: (limit: number) => `Bir fatura taslağına en fazla ${limit} sevk payı alınabilir.`,
  create: 'Fatura taslağı hazırla',
  submitting: 'Fatura taslağı hazırlanıyor…',
  completed: (documentNo: string) => `${documentNo} fatura taslağı hazırlandı. Onaya göndermek için aşağıdaki taslağı kullanın.`,
  replayed: (documentNo: string) => `${documentNo} fatura taslağı daha önce hazırlanmıştı; güncel durumu gösteriliyor.`,
  unavailable: 'Fatura taslağı hazırlama yetkiniz bu oturumda yayımlanmadı; yalnız liste gösteriliyor.',
  notInvoiceableHint: 'Bu grup faturalanabilir olmadan fatura taslağı hazırlanamaz.',
  editAfterUnknown:
    'Sonucu bilinmeyen bir hazırlama var. Seçimi değiştirmeden önce aynı işlemi tekrar gönderin ya da bırakın; yoksa aynı ' +
    'paylar için ikinci bir deneme başlatılmaz.',
  /** API-32 409 `invoice_not_ready`: the field errors name every share and reason; they are listed verbatim. */
  notReady: 'Seçili paylar şu anda birlikte faturalanamaz. Sunucunun gerekçeleri:',
  notReadyItem: (share: string, reason: string) => `${share}: ${reason}`,
  unknownShare: 'Sevk payı',
  /** A 409 other than `invoice_not_ready` (the same key with other shares, or a concurrent save): nothing was prepared. */
  createConflict: 'Fatura taslağı hazırlanmadı; seçili paylar başka bir işlemle çakıştı. Liste yeniden okundu.',
  newDraftHeading: 'Hazırlanan fatura taslağı',
} as const

/**
 * The API-32 reason codes (`SalesInvoiceReasonCodesV1`) of a 409 `invoice_not_ready`; an unknown code is shown as the code
 * itself, never dropped. The texts describe the server's rule; the screen evaluates none of them.
 */
export const INVOICE_NOT_READY_REASON_TEXT: Readonly<Record<string, string>> = {
  share_not_invoiceable: 'Sevk payı faturaya hazır değil ya da bulunamadı.',
  share_already_invoiced: 'Sevk payı başka bir faturada.',
  customer_mismatch: 'Sevk payı seçilen cari hesaba ait değil.',
  order_not_approved: 'Siparişin güncel revizyonu onaylı değil.',
  order_awaiting_reapproval: 'Sipariş yeniden onay bekliyor.',
  order_awaiting_customer_acceptance: 'Sipariş müşteri onayı bekliyor.',
  order_line_not_in_basis: 'Sipariş satırı onaylı revizyonda yok.',
  currency_mismatch: 'Para birimi diğer paylarla aynı değil.',
  money_profile_mismatch: 'Para biriminin ondalık ayarı diğer paylarla aynı değil.',
  rate_unresolved: 'Kur kaydı henüz yok.',
  rate_mismatch: 'Kur diğer paylarla aynı değil.',
  due_terms_missing: 'Siparişte vade bilgisi yok.',
  due_date_pending: 'Vade tarihi henüz belirlenemedi.',
  due_date_mismatch: 'Vade tarihi diğer paylarla aynı değil.',
  line_unpriced: 'Sipariş satırının fiyatı yok.',
  tax_unavailable: 'Vergi bilgisi okunamadı.',
  exemption_document_after_shipment: 'Alıcının istisna belgesi sevkten sonra tarihli.',
  amount_out_of_range: 'Tutar izin verilen aralığın dışında.',
  due_amount_not_splittable: 'Ödenecek tutar vade planının taksitlerine bölünemiyor.',
}

export const invoiceNotReadyReasonText = (code: string): string =>
  Object.hasOwn(INVOICE_NOT_READY_REASON_TEXT, code) ? INVOICE_NOT_READY_REASON_TEXT[code] : code

export const INVOICE_DRAFT_ACTIONS_TEXT = {
  heading: 'Taslak işlemleri',
  submit: 'Onaya gönder',
  approve: 'Onayla',
  reject: 'Reddet',
  cancel: 'Taslağı iptal et',
  submitNote: 'Taslak onaya gönderilir; onaylayan kişi taslağı hazırlayan ya da gönderen kişi olamaz.',
  approveNote: 'Onay bekleyen taslağı onaylayabilir ya da gerekçeyle reddedebilirsiniz. Reddedilen taslağın sevk payları yeniden faturaya hazır olur.',
  cancelNote: 'İptal edilen taslağın sevk payları yeniden faturaya hazır olur. Onaylanmış faturanın iptali bu adımda yapılmaz.',
  reasonLabel: 'Gerekçe',
  reasonHint: (limit: number) => `En fazla ${limit} karakter.`,
  reasonTooLong: (limit: number) => `Gerekçe en fazla ${limit} karakter olabilir.`,
  rejectConfirm: 'Reddi gönder',
  cancelConfirm: 'İptali gönder',
  back: 'Vazgeç',
  submitting: {
    submit: 'Onaya gönderiliyor…',
    approve: 'Onaylanıyor…',
    reject: 'Reddediliyor…',
    cancel: 'İptal ediliyor…',
  },
  completed: {
    submit: 'Taslak onaya gönderildi.',
    approve: 'Fatura onaylandı.',
    reject: 'Taslak reddedildi; sevk payları yeniden faturaya hazır.',
    cancel: 'Taslak iptal edildi; sevk payları yeniden faturaya hazır.',
  },
  replayed: 'Bu işlem daha önce kaydedilmişti; güncel durum gösteriliyor.',
  writeUnavailable: 'Taslağı onaya gönderme ve iptal etme yetkiniz bu oturumda yayımlanmadı.',
  approveUnavailable: 'Taslağı onaylama ya da reddetme yetkiniz bu oturumda yayımlanmadı.',
  decisionHeading: 'Onay geçmişi',
  submittedBy: 'Onaya gönderen',
  submittedAt: 'Gönderilme',
  decidedBy: 'Karar veren',
  decidedAt: 'Karar zamanı',
  decisionReason: 'Karar gerekçesi',
  cancelledBy: 'İptal eden',
  cancelledAt: 'İptal zamanı',
  cancellationReason: 'İptal gerekçesi',
  /** API-33 409 `sales_invoice_self_approval` (recorded under the key). */
  selfApproval: 'Taslağı hazırlayan ya da onaya gönderen kişi onaylayamaz veya reddedemez.',
  /** 409 `concurrent_change`: another save won the race; nothing was recorded. */
  concurrentChange: 'Fatura aynı anda başka bir işlemle değişti; güncel durum yeniden okundu.',
  /** 409 `same_key_payload_mismatch`. */
  sameKeyPayloadMismatch: 'Bu deneme farklı bilgilerle daha önce gönderilmiş; işlem yapılmadı.',
  /** 422 `sales_approval_reason_too_long`. */
  reasonTooLongServer: 'Gerekçe sunucunun kabul ettiği uzunluğu aşıyor; işlem yapılmadı.',
} as const
