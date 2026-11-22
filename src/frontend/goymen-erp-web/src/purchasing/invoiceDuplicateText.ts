import type { InvoiceDuplicateCode } from './invoiceDuplicateContract'

/** WP15 (1.16) — the words of the incoming invoice duplicate warning. */
export const INVOICE_DUPLICATE_TEXT = {
  heading: 'Mükerrer fatura kontrolü',
  subject: 'Mükerrer fatura kontrolü',
  none: 'Kural tabanlı kontrolde benzer kayıtlı fatura bulunmadı.',
  warning: 'Bu faturaya benzeyen kayıtlı faturalar var. Kayıt engellenmez; lütfen kontrol edin.',
  truncated: 'Gösterilenden fazla benzer fatura var.',
  date: (date: string) => { const [y, m, d] = date.split('-'); return `${d}.${m}.${y}` },
  caption: 'Benzer faturalar',
  number: 'Fatura no',
  invoiceDate: 'Fatura tarihi',
  reason: 'Neden',
} as const

export const INVOICE_DUPLICATE_REASON: Record<InvoiceDuplicateCode, string> = {
  'purchasing.invoice-duplicate.same-ettn': 'Aynı ETTN',
  'purchasing.invoice-duplicate.same-supplier-number': 'Aynı tedarikçi ve fatura no (yazım farkıyla)',
  'purchasing.invoice-duplicate.same-supplier-date-amount': 'Aynı tedarikçi, tarih, para birimi ve tutar',
}
