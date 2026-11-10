/**
 * UI-63 S-09 "Satış faturaları" user texts (Turkish, single source). The list is read-only: amounts are the server's text, the
 * order is the server's (newest first), and a row opens the existing invoice detail with its finalization state.
 */

export const INVOICE_LIST_TEXT = {
  heading: 'Satış faturaları',
  intro: 'Şirketin satış faturaları, en yeni önce. Sıralama ve tutarlar sunucunundur; bu ekran hesap yapmaz.',
  subject: 'satış faturaları',
  caption: 'Satış faturaları listesi',
  documentNo: 'Fatura no',
  state: 'Durum',
  businessDate: 'Fatura tarihi',
  customer: 'Müşteri',
  payable: 'Ödenecek tutar (belge para birimi)',
  functionalPayable: 'Ödenecek tutar (şirket para birimi)',
  lineCount: 'Satır',
  createdAt: 'Oluşturma',
  none: '—',
  /** SAT-D238: an amount the server withheld from this user (never shown as 0). */
  withheld: 'Gizli',
  withheldLabel: 'Tutar gizli: fiyat görme yetkiniz yok',
  openLabel: (documentNo: string) => `${documentNo} faturasının ayrıntısını göster`,
  detailHeading: 'Seçili fatura',
  close: 'Ayrıntıyı kapat',
} as const
