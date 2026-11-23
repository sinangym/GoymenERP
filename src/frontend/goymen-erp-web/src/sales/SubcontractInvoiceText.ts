/**
 * F2: the Turkish texts of the subcontract service ("fason hizmet") invoice draft on the order detail. The billed kilograms are
 * the delivery weighing's net kg (LOJ-D9; scrap is not billed) and every amount is the server's text; nothing here describes a
 * calculation the screen makes, and no text names a role.
 */
import { SUBCONTRACT_INVOICE_REASON as R } from './SubcontractInvoiceContract'
import { invoiceNotReadyReasonText } from './invoiceDraftText'

export const SUBCONTRACT_INVOICE_TEXT = {
  heading: 'Fason hizmet faturası',
  intro:
    'Fason hizmet bedeli, teslim tartısının net kilogramı üzerinden faturalanır; fire faturalanmaz. Yükleme talimatını ve ' +
    'teslim tartısını girin; net kilogramı, birim fiyatı, vergiyi ve tutarları sunucu okur ve hesaplar. Resmî fatura dış ' +
    'programda kesilir.',
  loadingInstructionLabel: 'Yükleme talimatı kimliği',
  weighingLabel: 'Teslim tartısı kimliği',
  idHint: 'Lojistik ekranındaki kayıt kimliğini (UUID) yapıştırın.',
  required: 'Yükleme talimatı ve teslim tartısı kimliklerinin ikisi de gerekli.',
  invalidId: 'Kimlik geçerli bir kayıt kimliği (UUID) değil.',
  create: 'Fason hizmet faturası taslağı hazırla',
  submitting: 'Fason hizmet faturası taslağı hazırlanıyor…',
  completed: (documentNo: string) => `${documentNo} fason hizmet faturası taslağı hazırlandı.`,
  replayed: (documentNo: string) => `${documentNo} fason hizmet faturası taslağı daha önce hazırlanmıştı; kayıtlı hali gösteriliyor.`,
  editAfterUnknown:
    'Sonucu bilinmeyen bir hazırlama var. Kimlikleri değiştirmeden önce aynı işlemi tekrar gönderin ya da bırakın; yoksa aynı ' +
    'tartı için ikinci bir deneme başlatılmaz.',
  notReady: 'Fason hizmet faturası şu anda hazırlanamaz. Sunucunun gerekçeleri:',
  notReadyItem: (field: string, reason: string) => `${field}: ${reason}`,
  createConflict: 'Fason hizmet faturası taslağı hazırlanmadı; aynı tartı başka bir işlemle çakıştı. Tekrar denerseniz güncel durum okunur.',
  newDraftHeading: 'Hazırlanan fason hizmet faturası taslağı',
  documentNo: 'Belge no',
  state: 'Durum',
  draftState: 'Taslak',
  businessDate: 'İş tarihi',
  netKg: 'Teslim tartısı net kg',
  weighingRevision: 'Tartı revizyonu',
  quantity: 'Faturalanan kg',
  netUnitPrice: 'Net birim fiyat (kg)',
  vatPercent: 'KDV %',
  netAmount: 'Net tutar',
  vatAmount: 'KDV',
  withheldVatAmount: 'Tevkif edilen KDV',
  grossAmount: 'Brüt tutar',
  payableAmount: 'Ödenecek tutar',
  linesCaption: 'Fason hizmet satırları',
  lineNo: 'Satır',
  unavailable: 'Fason hizmet faturası hazırlama yetkiniz bu oturumda yayımlanmadı.',
  /** 404 on the create: the order is not in this company (another company's order is never named). */
  notFound: 'Sipariş bu şirkette bulunamadı; işlem yapılmadı.',
} as const

/** The field a 409 `invoice_not_ready` reason names, as the screen calls it. */
export const SUBCONTRACT_INVOICE_FIELD_TEXT: Readonly<Record<string, string>> = {
  salesOrderId: 'Sipariş',
  loadingInstructionId: 'Yükleme talimatı',
  weighingId: 'Teslim tartısı',
  'lines[0]': 'Fason hizmet satırı',
}

export const subcontractInvoiceFieldText = (field: string): string =>
  Object.hasOwn(SUBCONTRACT_INVOICE_FIELD_TEXT, field) ? SUBCONTRACT_INVOICE_FIELD_TEXT[field] : field

/**
 * The F2 reason codes; the order and tax reasons are API-32's texts, and an unknown code is shown as the code itself, never
 * dropped. The texts describe the server's rule; the screen evaluates none of them.
 */
export const SUBCONTRACT_INVOICE_REASON_TEXT: Readonly<Record<string, string>> = {
  [R.notSubcontractOrder]: 'Sipariş bir fason hizmet siparişi değil; faturası sevk payları üzerinden hazırlanır.',
  [R.weighingMissing]: 'Bu yükleme talimatı için teslim tartısı bulunamadı; tartı olmadan fason hizmet faturası hazırlanmaz.',
  [R.weighingOrderMismatch]: 'Tartılan yükleme talimatı bu siparişin bir satırına hizmet etmiyor.',
  [R.sharePolicyPending]:
    'Tartılan yükleme talimatı birden fazla siparişe ya da sipariş satırına hizmet ediyor; net kilogramın paylaştırma kuralı ' +
    'henüz karara bağlanmadı.',
  [R.theoreticalKgUnavailable]:
    'Ortak teslim tartısındaki bir satırın teorik kilogramı okunamadı; net kilogram paylaştırılmadı ve fatura hazırlanmadı.',
  [R.shareNotPositive]:
    'Ortak teslim tartısının tam kilograma yuvarlanmış payı bu satır için sıfır çıkıyor; sıfır kilogramlık fatura hazırlanmaz.',
  [R.currencyPending]: 'Döviz cinsinden fason hizmet siparişinin kur günü henüz karara bağlanmadı.',
  [R.lineNotKilogram]: 'Sipariş satırının birimi kilogram değil; teslim tartısının net kilogramı yalnız kg satırına faturalanır.',
  [R.weighingAlreadyInvoiced]: 'Bu teslim tartısı bu sipariş için zaten faturalandı; düzeltme fark faturasıyla yapılır.',
}

export const subcontractInvoiceReasonText = (code: string): string =>
  Object.hasOwn(SUBCONTRACT_INVOICE_REASON_TEXT, code) ? SUBCONTRACT_INVOICE_REASON_TEXT[code] : invoiceNotReadyReasonText(code)
