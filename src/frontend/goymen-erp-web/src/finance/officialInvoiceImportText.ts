/**
 * FIN-S5 (BS-4, FNS-D28, FNS-D19) — user-visible texts of the controlled official invoice file screens (TEK-03).
 * The server owns every check; this file only names the stable issue codes it returns. An unknown code is shown
 * as the raw code so a new server rule is never hidden.
 */

export const OFFICIAL_INVOICE_IMPORT_TEXT = {
  templateHint:
    'Resmî satış faturası için v3 şablonu kullanılır: v1 sütunlarının ardından officialNo, ettn, '
    + 'taxBaseFunctionalAmount, vatFunctionalAmount, withholdingFunctionalAmount, invoiceDate ve payableFunctionalAmount gelir. '
    + 'Belge sütunları aynı belgenin her taksit satırında aynı olmalıdır. Eski v1/v2 dosyaları korunur; '
    + 'kaynakta bulunmayan tarih veya TL karşılığı tahmin edilmez.',
  invoiceDate: 'Resmî fatura tarihi',
  importedTl: 'İthal belgedeki ödenecek TL',
  effectiveTl: 'Esas alınan ödenecek TL',
  unknown: 'Kaynakta yok',
  notCalculated: 'Hesaplanamadı; kaynak TL karşılığı yok',
  pendingManual: 'Onay bekleyen elle kayıt var; ithal kaynağı engellemez.',
  manualWins: 'Onaylı elle kayıt esas alınır; ithal belge korunur.',
  differences: 'Kaynaklar arasındaki farklı alanlar',
  // FNS-D64: the server's check; the screen only shows the amounts it returned.
  taxWarnings: 'Uyarı: aşağıdaki belgelerde matrah + KDV − tevkifat, ödenecek tutar (TL) ile uyuşmuyor. Dosya engellenmez; uyarı denetime yazılır.',
  taxWarningDocument: 'Belge',
  taxWarningExpected: 'Beklenen ödenecek tutar (TL)',
  taxWarningPayable: 'Dosyadaki ödenecek tutar (TL)',
  taxWarningDifference: 'Fark (TL)',
  taxWarningCode: 'Uyarı kodu',
  taxWarningNoDocument: 'Belge kimliği yok',
  identityHint:
    'Aynı resmî numara veya ETTN ile iptal edilmemiş bir alacak varken dosya kabul edilmez; kapanmış alacak da '
    + 'engeller. Belge numarası mükerrerliği kabul anında yeniden denetlenir.',
} as const

const ISSUE_LABELS: Readonly<Record<string, string>> = {
  official_no_required: 'Resmî fatura numarası boş olamaz.',
  official_no_invalid: 'Resmî fatura numarası geçersiz veya çok uzun.',
  ettn_required: 'ETTN boş olamaz.',
  ettn_invalid: 'ETTN geçerli bir GUID değil.',
  functional_amount_required: 'TL matrah, KDV ve tevkifat tutarları boş olamaz.',
  functional_amount_invalid: 'TL tutarı negatif olmayan, noktalı ondalık sayı olmalıdır.',
  official_document_inconsistent: 'Aynı belgenin taksit satırlarında belge sütunları farklı.',
  official_no_duplicate_in_file: 'Aynı resmî numara dosyada birden fazla belgede var.',
  ettn_duplicate_in_file: 'Aynı ETTN dosyada birden fazla belgede var.',
  finance_official_no_already_open: 'Bu resmî numarayla iptal edilmemiş bir alacak zaten kayıtlı; dosya uygulanmadı.',
  finance_ettn_already_open: 'Bu ETTN ile iptal edilmemiş bir alacak zaten kayıtlı; dosya uygulanmadı.',
  finance_official_documents_unavailable: 'Onaylı dosyanın belge bilgileri okunamadı; dosya uygulanmadı.',
}

/** The Turkish label of a server issue code, or null when the code has no label yet. */
export function officialInvoiceIssueLabel(code: string): string | null {
  return Object.prototype.hasOwnProperty.call(ISSUE_LABELS, code) ? ISSUE_LABELS[code] : null
}

/** One issue line: the field and the stable code, followed by the Turkish label when one is known. */
export function officialInvoiceIssueLine(field: string, code: string): string {
  const label = officialInvoiceIssueLabel(code)
  return label === null ? `${field}: ${code}` : `${field}: ${code} — ${label}`
}
