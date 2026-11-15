import { describe, expect, it } from 'vitest'
import { OFFICIAL_INVOICE_IMPORT_TEXT, officialInvoiceIssueLabel, officialInvoiceIssueLine } from './officialInvoiceImportText'

describe('official invoice import text', () => {
  it('names every FIN-S5 issue code in Turkish', () => {
    for (const code of [
      'official_no_required', 'official_no_invalid', 'ettn_required', 'ettn_invalid', 'functional_amount_required',
      'functional_amount_invalid', 'official_document_inconsistent', 'official_no_duplicate_in_file',
      'ettn_duplicate_in_file', 'finance_official_no_already_open', 'finance_ettn_already_open',
      'finance_official_documents_unavailable',
    ]) {
      expect(officialInvoiceIssueLabel(code)).toMatch(/\S/)
    }
  })

  it('keeps an unknown or inherited code raw so a new server rule is never hidden', () => {
    expect(officialInvoiceIssueLabel('decimal_invalid')).toBeNull()
    expect(officialInvoiceIssueLabel('toString')).toBeNull()
    expect(officialInvoiceIssueLine('amount', 'decimal_invalid')).toBe('amount: decimal_invalid')
  })

  it('shows the field and the stable code before the label', () => {
    expect(officialInvoiceIssueLine('rows[0].officialNo', 'finance_official_no_already_open'))
      .toBe('rows[0].officialNo: finance_official_no_already_open — '
        + 'Bu resmî numarayla iptal edilmemiş bir alacak zaten kayıtlı; dosya uygulanmadı.')
  })

  it('lists every v2 document column in the template hint', () => {
    for (const column of ['officialNo', 'ettn', 'taxBaseFunctionalAmount', 'vatFunctionalAmount',
      'withholdingFunctionalAmount']) {
      expect(OFFICIAL_INVOICE_IMPORT_TEXT.templateHint).toContain(column)
    }
  })
})
