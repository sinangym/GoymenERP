/**
 * UI-64b: the Turkish text of the official e-invoice match form (API-35). Every amount the screen shows next to these texts is
 * the server's text; nothing here states a tolerance, a threshold or an amount.
 */

export const INVOICE_MATCH_TEXT = {
  formHeading: 'Resmî e-faturayla eşleştir',
  formNote:
    'Resmî fatura numarası ve ETTN, onaylı resmî e-fatura kaydıyla eşleştirilir. Numara, ETTN, müşteri ve para birimi birebir ' +
    'tutmalıdır; tutar farkını ve toleransı sunucu hesaplar, bu ekran hesaplamaz.',
  officialInvoiceNo: 'Resmî fatura no',
  officialInvoiceNoHelp: (max: number) => `Resmî e-fatura üzerindeki numara (en çok ${max} karakter).`,
  ettn: 'ETTN',
  ettnHelp: 'Resmî e-faturanın evrensel tekil numarası (ör. 3fa85f64-5717-4562-b3fc-2c963f66afa6).',
  approveDifference: 'Tolerans üstündeki tutar farkını onaylıyorum',
  differenceReason: 'Fark onayı gerekçesi',
  differenceReasonHelp: 'Onay gerekçesi denetim kaydına yazılır.',
  submit: 'Eşleştir',
  submitApproval: 'Farkı onaylayarak eşleştir',
  formUnavailable: 'Eşleştirme formu, bu işlemin ekran yetkisi yayımlanana kadar gösterilmez.',
  approvalNeeded:
    'Sunucu, resmî tutarla fatura tutarı arasındaki farkın tolerans üstünde olduğunu bildirdi. Eşleşme ancak ayrı onay ' +
    'yetkisiyle, gerekçe yazılarak kaydedilebilir.',
  approvalNotAvailable:
    'Fark onayı ayrı bir yetki ister ve faturayı hazırlayan kişi onaylayamaz; bu ekranda fark onayı sunulmuyor.',
  invalidOfficialNo: (max: number) => `Resmî fatura numarasını yazın (en çok ${max} karakter).`,
  invalidEttn: 'ETTN, 8-4-4-4-12 biçiminde onaltılık bir numara olmalıdır.',
  invalidReason: (max: number) => `Fark onayı için gerekçe yazın (en çok ${max} karakter).`,
  submitting: 'Eşleştirme gönderiliyor…',
  completed: 'Resmî e-fatura eşleşmesi kaydedildi.',
  unknownServiceUnavailable:
    'Eşleştirme şu anda değerlendirilemedi: Satış servisi ya da resmî e-fatura kaynağı yanıt vermiyor. Biraz sonra aynı ' +
    'eşleştirmeyi tekrar gönderin; ikinci bir kayıt oluşmaz.',
  replayed: 'Bu eşleşme daha önce kaydedilmişti; güncel durum gösteriliyor.',
  resultCaption: 'Sunucunun kaydettiği eşleşme sonucu',
  resultDifference: 'Fark',
  resultTolerance: 'Tolerans',
  resultApprovedBy: 'Farkı onaylayan',
  resultReason: 'Fark onayı gerekçesi',
  differenceApprovedBy: 'Farkı onaylayan',
  differenceReasonFact: 'Fark onayı gerekçesi',
  noDifferenceApproval: 'Fark onayı yok',
  mismatchFields: 'Tutmayan alanlar',
  conflict: 'Sunucu eşleştirmeyi yapmadı; faturanın güncel durumu yeniden okundu.',
  refused: 'Sunucu eşleştirmeyi yapmadı.',
} as const

/** The stable problem codes API-35 answers (OpenAPI `MatchSalesInvoiceV1`), with their Turkish text. */
export const INVOICE_MATCH_PROBLEM_CODE = {
  officialDocumentNotFound: 'official_document_not_found',
  officialMatchMismatch: 'official_match_mismatch',
  differenceApprovalRequired: 'difference_approval_required',
  officialDocumentAlreadyMatched: 'official_document_already_matched',
  selfApproval: 'sales_invoice_self_approval',
  differenceNotApplicable: 'sales_invoice_match_difference_not_applicable',
  differenceReasonRequired: 'sales_invoice_match_difference_reason_required',
  approvalReasonTooLong: 'sales_approval_reason_too_long',
  providerUnavailable: 'official_invoice_provider_unavailable',
} as const

export const INVOICE_MATCH_PROBLEM_TEXT: Readonly<Record<string, string>> = {
  [INVOICE_MATCH_PROBLEM_CODE.officialDocumentNotFound]:
    'Bu numara ve ETTN ile onaylı bir resmî e-fatura kaydı bulunamadı.',
  [INVOICE_MATCH_PROBLEM_CODE.officialMatchMismatch]:
    'Resmî e-fatura bu faturayla birebir tutmuyor (numara, ETTN, müşteri veya para birimi).',
  [INVOICE_MATCH_PROBLEM_CODE.differenceApprovalRequired]: 'Tutar farkı tolerans üstünde; ayrı fark onayı gerekiyor.',
  [INVOICE_MATCH_PROBLEM_CODE.officialDocumentAlreadyMatched]: 'Bu resmî e-fatura başka bir faturayla zaten eşleştirilmiş.',
  [INVOICE_MATCH_PROBLEM_CODE.selfApproval]: 'Faturayı hazırlayan kişi tutar farkını onaylayamaz.',
  [INVOICE_MATCH_PROBLEM_CODE.differenceNotApplicable]: 'Tutar farkı tolerans içinde; fark onayı gerekmiyor.',
  [INVOICE_MATCH_PROBLEM_CODE.differenceReasonRequired]: 'Fark onayı için gerekçe zorunlu.',
  [INVOICE_MATCH_PROBLEM_CODE.approvalReasonTooLong]: 'Fark onayı gerekçesi çok uzun.',
  version_conflict: 'Fatura siz gördükten sonra değişti; güncel sürüm yeniden okundu.',
  concurrent_change: 'Fatura aynı anda başka bir işlemle değişti; güncel sürüm yeniden okundu.',
  sales_state_conflict: 'Fatura eşleştirme için uygun durumda değil (yalnız onaylı ve eşleşmemiş fatura eşleştirilir).',
  same_key_payload_mismatch: 'Aynı işlem anahtarı farklı bilgilerle kullanıldı; işlem yapılmadı.',
  validation_failed: 'Gönderilen bilgiler geçerli değil.',
}

/** Field names a mismatch refusal names (`errors[].field`). */
export const INVOICE_MATCH_FIELD_TEXT: Readonly<Record<string, string>> = {
  customer: 'Müşteri',
  currency: 'Para birimi',
  officialInvoiceNo: 'Resmî fatura no',
  ettn: 'ETTN',
}
