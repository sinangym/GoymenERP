/**
 * UI-70b S-10 "Müşteri iade faturaları" — every user-facing sentence of the customer return screens (Turkish, one place).
 * The screens say what the server recorded; none of these texts states a business rule the server did not decide.
 */
import type { CustomerReturnState } from './customerReturnContract'
import { poolProblemText } from './poolText'
import type { StatusTone } from '../ui/StatusIndicator'

export const CUSTOMER_RETURN_STATE_TEXT: Readonly<Record<CustomerReturnState, string>> = {
  Draft: 'Taslak',
  PendingApproval: 'Onay bekliyor',
  Approved: 'Onaylandı',
  Finalized: 'Kesinleşti',
  Rejected: 'Reddedildi',
  Cancelled: 'İptal edildi',
}

export const CUSTOMER_RETURN_TONE: Readonly<Record<CustomerReturnState, StatusTone>> = {
  Draft: 'neutral',
  PendingApproval: 'warning',
  Approved: 'info',
  Finalized: 'success',
  Rejected: 'danger',
  Cancelled: 'neutral',
}

/** The query member (Turkish, user-visible in the address) that narrows the list to one original invoice. */
export const CUSTOMER_RETURN_INVOICE_PARAM = 'fatura'

export const CUSTOMER_RETURN_SUBJECT = {
  list: 'müşteri iade faturaları',
  detail: 'müşteri iade faturası',
  invoices: 'satış faturaları',
  invoice: 'satış faturası',
} as const

export const CUSTOMER_RETURN_TEXT = {
  common: {
    originalInvoice: 'Asıl satış faturası',
    customerDocumentNo: 'Müşteri belge no',
    customerDocumentDate: 'Müşteri belge tarihi',
    party: 'Cari',
    state: 'Durum',
    currency: 'Para birimi',
    lineTotal: 'İade toplamı',
    lineCount: 'Satır',
    createdAt: 'Oluşturulma',
    shipment: 'Sevkiyat',
    shipmentLinked: 'Sevkiyata bağlı',
    shipmentNone: 'Sevkiyat belirtilmedi',
    needsReview: 'Kur farkı incelemesi gerekli',
    yes: 'Evet',
    no: 'Hayır',
    unknown: 'Belirtilmedi',
    singleCompanyOnly: 'Müşteri iade faturası yalnız tek bir şirket seçiliyken görüntülenir ve kaydedilir.',
    retry: 'Aynı işlemi tekrar dene',
    abandon: 'Bu işlemi bırak',
  },
  list: {
    heading: 'Müşteri iade faturaları',
    caption: 'Seçili şirkette kaydedilen müşteri iade faturaları',
    intro: 'Müşterinin düzenleyip gönderdiği iade faturaları burada kaydedilir; sistem iade faturası düzenlemez.',
    create: 'Yeni müşteri iade faturası',
    filteredBy: (invoiceId: string) => `Yalnız ${invoiceId} faturasına ait iadeler gösteriliyor.`,
    clearFilter: 'Tüm iadeleri göster',
    shipmentColumn: 'Sevkiyat bağı',
  },
  create: {
    heading: 'Yeni müşteri iade faturası',
    intro:
      'Müşterinin gönderdiği iade faturasını kesinleşmiş asıl satış faturasına bağlayarak kaydedin. Birim fiyat ve tutarlar asıl faturadan sunucu tarafından hesaplanır; ekranda tutar hesaplanmaz.',
    invoice: 'Asıl satış faturası',
    invoicePlaceholder: 'Fatura seçin',
    invoiceOption: (documentNo: string, state: string) => `${documentNo} (${state})`,
    invoiceNotFinalized: 'Yalnız kesinleşmiş satış faturasına iade kaydedilir.',
    invoiceRequired: 'Asıl satış faturasını seçin.',
    documentNoRequired: 'Müşterinin iade faturası belge numarasını girin.',
    documentNoTooLong: (max: number) => `Belge numarası en fazla ${max} karakter olabilir.`,
    documentDateRequired: 'Müşteri belge tarihini girin.',
    shipment: 'Sevkiyat (isteğe bağlı)',
    shipmentNone: 'Sevkiyat belirtme',
    shipmentOption: (dispatchNoteNumber: string) => `İrsaliye ${dispatchNoteNumber}`,
    shipmentHint:
      'Sevkiyat seçilirse yalnız o sevkiyatın satırları iade edilebilir. Sevkiyat seçilmezse iade, faturalanmış ama sevk edilmemiş bakiyeyi aşamaz.',
    linesHeading: 'İade satırları',
    linesCaption: 'Asıl faturanın satırları ve iade miktarları',
    lineNo: 'No',
    product: 'Ürün',
    invoicedQuantity: 'Faturalanan miktar',
    unitPrice: 'Birim fiyat',
    returnQuantity: 'İade miktarı',
    returnQuantityLabel: (lineNo: number) => `${lineNo}. satırın iade miktarı`,
    quantityInvalid: 'Miktar sıfırdan büyük bir sayı olmalıdır.',
    linesRequired: 'En az bir satıra iade miktarı girin.',
    tooManyLines: (max: number) => `Bir iade en fazla ${max} satır içerebilir.`,
    errorSummary: 'Form gönderilmedi. Lütfen şu alanları düzeltin:',
    submit: 'İade faturasını kaydet',
    submitting: 'Kaydediliyor…',
    noun: 'iade faturası',
    completed: (documentNo: string) => `${documentNo} müşteri iade faturası taslak olarak kaydedildi.`,
    back: 'Müşteri iade faturalarına dön',
  },
  detail: {
    heading: (documentNo: string) => `Müşteri iade faturası ${documentNo}`,
    back: 'Müşteri iade faturalarına dön',
    facts: 'İade bilgileri',
    version: 'Sürüm',
    rate: 'Kur',
    companyAccount: 'Cari hesap',
    decisions: 'İşlem geçmişi',
    createdBy: 'Hazırlayan',
    submittedBy: 'Onaya gönderen',
    approvedBy: 'Onaylayan',
    finalizedBy: 'Kesinleştiren',
    rejectedBy: 'Reddeden',
    rejectionReason: 'Ret gerekçesi',
    cancelledBy: 'İptal eden',
    cancellationReason: 'İptal gerekçesi',
    reasonNone: 'Gerekçe belirtilmedi',
    at: (who: string, when: string) => `${who} — ${when}`,
    totals: 'Tutarlar',
    functionalTotals: 'Yerel para tutarları',
    net: 'Net tutar',
    vat: 'KDV',
    withheld: 'Tevkif edilen KDV',
    lineTotal: 'Satır toplamı',
    derivedPayable: 'Hesaplanan ödenecek',
    drift: 'Yuvarlama farkı',
    lines: 'İade satırları',
    linesCaption: 'İade edilen fatura satırları',
    lineNo: 'No',
    originalLineNo: 'Fatura satırı',
    product: 'Ürün',
    unit: 'Birim',
    quantity: 'Miktar',
    netUnitPrice: 'Birim fiyat',
    vatPercent: 'KDV %',
    flags: 'Not',
    lastReturn: 'Son iade: kalan tutar kapatıldı',
    commands: 'İade işlemleri',
    submit: 'Onaya gönder',
    approve: 'Onayla',
    finalize: 'Kesinleştir',
    reject: 'Reddet',
    cancel: 'İptal et',
    rejectConfirm: 'İadeyi reddet',
    cancelConfirm: 'İadeyi iptal et',
    reasonBack: 'Vazgeç',
    reasonLabel: 'Gerekçe (isteğe bağlı)',
    reasonHint: (limit: number) => `En fazla ${limit} karakter. Boş bırakılırsa gerekçesiz kaydedilir.`,
    reasonTooLong: (limit: number) => `Gerekçe en fazla ${limit} karakter olabilir.`,
    reasonTooLongServer: 'Gerekçe sunucunun kabul ettiği uzunluğu aşıyor; işlem yapılmadı.',
    rejectNote: 'Reddedilen iade kapanır ve iade edilebilir miktarı kullanmaz; düzeltme için yeni iade kaydedilir.',
    cancelNote: 'İptal yalnız onaydan önce yapılır; iptal edilen iade kapanır ve iade edilebilir miktarı kullanmaz.',
    noun: 'iade işlemi',
    completed: {
      submission: 'İade faturası onaya gönderildi.',
      approval: 'İade faturası onaylandı.',
      finalization: 'İade faturası kesinleşti ve finansa iletildi.',
      rejection: 'İade faturası reddedildi.',
      cancellation: 'İade faturası iptal edildi.',
    },
    approveHint: 'Hazırlayan ya da onaya gönderen kişi aynı iadeyi onaylayamaz ya da reddedemez; sunucu da reddeder.',
    financePortUnavailable:
      'Finans kaydı şu anda alınamıyor; iade kesinleşmedi. Finans bağlantısı açıldığında aynı işlemi tekrar deneyin.',
  },
} as const

/** The S-10 codes (`SalesCustomerReturnProblemCodesV1` / `SalesRuleCodes.CustomerReturn*`). */
export const CUSTOMER_RETURN_PROBLEM_CODE = {
  amountOutOfRange: 'sales_customer_return_amount_out_of_range',
  amountsChanged: 'sales_customer_return_amounts_changed',
  documentDuplicate: 'sales_customer_return_document_duplicate',
  documentInvalid: 'sales_customer_return_document_invalid',
  financePortUnavailable: 'sales_customer_return_finance_port_unavailable',
  financeRefused: 'sales_customer_return_finance_refused',
  invoiceNotFinalized: 'sales_customer_return_invoice_not_finalized',
  invoiceNotFound: 'sales_customer_return_invoice_not_found',
  lineNotOnInvoice: 'sales_customer_return_line_not_on_invoice',
  linesInvalid: 'sales_customer_return_lines_invalid',
  quantityExceeded: 'sales_customer_return_quantity_exceeded',
  quantityInvalid: 'sales_customer_return_quantity_invalid',
  selfApproval: 'sales_customer_return_self_approval',
  shipmentNotOnInvoice: 'sales_customer_return_shipment_not_on_invoice',
  unshippedBalanceExceeded: 'sales_customer_return_unshipped_balance_exceeded',
} as const

type CustomerReturnProblemCode = (typeof CUSTOMER_RETURN_PROBLEM_CODE)[keyof typeof CUSTOMER_RETURN_PROBLEM_CODE]

const PROBLEM_TEXT: Readonly<Record<CustomerReturnProblemCode, string>> = {
  [CUSTOMER_RETURN_PROBLEM_CODE.amountOutOfRange]: 'İade tutarı izin verilen aralığın dışında kaldı.',
  [CUSTOMER_RETURN_PROBLEM_CODE.amountsChanged]:
    'Bu faturaya bu arada başka bir iade kesinleşti; tutarlar değişti. İadeyi yeniden okuyup kontrol edin.',
  [CUSTOMER_RETURN_PROBLEM_CODE.documentDuplicate]: 'Bu carinin aynı belge numarasıyla kayıtlı bir iade faturası zaten var.',
  [CUSTOMER_RETURN_PROBLEM_CODE.documentInvalid]: 'Müşteri belge numarası ya da tarihi geçersiz.',
  [CUSTOMER_RETURN_PROBLEM_CODE.financePortUnavailable]: CUSTOMER_RETURN_TEXT.detail.financePortUnavailable,
  [CUSTOMER_RETURN_PROBLEM_CODE.financeRefused]: 'Finans iade kaydını kabul etmedi; iade kesinleşmedi.',
  [CUSTOMER_RETURN_PROBLEM_CODE.invoiceNotFinalized]: 'Asıl satış faturası kesinleşmemiş; iade kaydedilemez.',
  [CUSTOMER_RETURN_PROBLEM_CODE.invoiceNotFound]: 'Asıl satış faturası bu şirkette bulunamadı.',
  [CUSTOMER_RETURN_PROBLEM_CODE.lineNotOnInvoice]: 'Seçilen satırlardan biri asıl faturada yok.',
  [CUSTOMER_RETURN_PROBLEM_CODE.linesInvalid]: 'İade satırları tekrarsız ve sınır içinde olmalıdır.',
  [CUSTOMER_RETURN_PROBLEM_CODE.quantityExceeded]: 'İade miktarı satırın kalan iade edilebilir miktarını aşıyor.',
  [CUSTOMER_RETURN_PROBLEM_CODE.quantityInvalid]: 'İade miktarı geçersiz.',
  [CUSTOMER_RETURN_PROBLEM_CODE.selfApproval]: 'İadeyi hazırlayan ya da onaya gönderen kişi onaylayamaz ya da reddedemez.',
  [CUSTOMER_RETURN_PROBLEM_CODE.shipmentNotOnInvoice]: 'Seçilen sevkiyat bu faturaya ait değil ya da satırları bu sevkiyattan değil.',
  [CUSTOMER_RETURN_PROBLEM_CODE.unshippedBalanceExceeded]:
    'Sevkiyata bağlanmayan iade, faturalanmış ama sevk edilmemiş bakiyeyi aşıyor. Sevkiyatı seçerek kaydedin.',
}

/** The reason text of an S-10 code only; null for any other code. */
export function customerReturnOwnProblemText(code: string | null): string | null {
  return code !== null && Object.hasOwn(PROBLEM_TEXT, code) ? PROBLEM_TEXT[code as CustomerReturnProblemCode] : null
}

/** The reason text of a stable code; the shared Sales codes (version, state, key reuse) keep their texts. */
export function customerReturnProblemText(code: string | null): string | null {
  return customerReturnOwnProblemText(code) ?? poolProblemText(code, true)
}
