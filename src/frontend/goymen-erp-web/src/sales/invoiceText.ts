/**
 * API-09 / API-36 (PR #986): the Turkish texts of the order's "Faturalar" tab and the invoice finalization panel. Every amount,
 * date and instalment on these screens is the server's text; nothing here describes a calculation the screen makes.
 */
import type { FinanceStage, InvoiceState } from './invoiceFinalizationContract'

export const ORDER_INVOICES_TEXT = {
  heading: 'Bu siparişin faturaları',
  subject: 'faturalar',
  caption: 'Siparişe bağlı satış faturaları',
  documentNo: 'Fatura no',
  state: 'Durum',
  businessDate: 'Fatura tarihi',
  lineCount: 'Fatura satırı / sipariş satırı',
  lineCountValue: (invoiceLines: number, orderLines: number) => `${invoiceLines} / ${orderLines}`,
  createdAt: 'Oluşturma',
  action: 'İşlem',
  empty: 'Bu sipariş için henüz satış faturası yok.',
  hasMore: 'Listede yalnız ilk faturalar gösteriliyor; sunucu daha fazlasının olduğunu bildiriyor.',
  open: 'Kesinleştirmeyi göster',
  openLabel: (documentNo: string) => `${documentNo} faturasının kesinleştirme durumunu göster`,
  close: 'Kapat',
  noDetailRead: 'Fatura ayrıntısını görme yetkiniz bu oturumda yayımlanmadı; yalnız liste gösteriliyor.',
} as const

export const INVOICE_STATE_TEXT: Record<InvoiceState, string> = {
  Draft: 'Taslak',
  PendingApproval: 'Onay bekliyor',
  Approved: 'Onaylandı',
  Rejected: 'Reddedildi',
  Cancelled: 'İptal edildi',
  Finalized: 'Kesinleşti',
}

export const FINANCE_STAGE_TEXT: Record<FinanceStage, string> = {
  pending: "Finans'a gönderilmeyi bekliyor",
  'sent-waiting': "Finans'a gönderildi, yanıt bekleniyor",
  accepted: 'Finans kabul etti',
  refused: 'Finans reddetti',
}

export const INVOICE_FINALIZATION_TEXT = {
  heading: (documentNo: string) => `${documentNo} — kesinleştirme`,
  subject: 'fatura',
  description:
    'Fatura, resmî e-fatura eşleşmesi kaydedildikten sonra kesinleştirilir. Kesinleşen faturanın vade planı Finans\'a açık kalem ' +
    'olarak gönderilir. Tutarlar, vade payları ve kur karşılıkları sunucunun hesabıdır; bu ekran hesap yapmaz.',
  facts: 'Fatura bilgileri',
  state: 'Fatura durumu',
  version: 'Sürüm',
  businessDate: 'Fatura tarihi',
  payable: 'Ödenecek tutar',
  createdBy: 'Oluşturan',
  paymentAllocationWarning: 'Ödeme dağıtım uyarısı',
  matchHeading: 'Resmî e-fatura eşleşmesi',
  matchMissing: 'Resmî e-fatura eşleşmesi henüz kaydedilmedi; fatura eşleşme olmadan kesinleştirilemez.',
  officialInvoiceNo: 'Resmî fatura no',
  ettn: 'ETTN',
  invoiceAmount: 'ERP tutarı',
  officialAmount: 'Resmî tutar',
  difference: 'Fark (sunucunun hesabı)',
  tolerance: 'Tolerans (sunucunun değeri)',
  matchedBy: 'Eşleştiren',
  matchedAt: 'Eşleşme zamanı',
  finalizationHeading: 'Kesinleştirme ve Finans durumu',
  notFinalized: 'Fatura henüz kesinleştirilmedi.',
  financeStage: 'Finans durumu',
  finalizedBy: 'Kesinleştiren',
  finalizedAt: 'Kesinleşme zamanı',
  finalizedVersion: 'Kesinleşen sürüm',
  admittedAt: 'Finans kabul zamanı',
  attemptCount: 'Finans gönderim denemesi',
  lastAttemptAt: 'Son deneme',
  lastOutcome: 'Son yanıt (sunucunun kaydı)',
  noAttempt: 'Henüz denenmedi',
  accounting: 'Muhasebe fişi',
  accountingNoSource: 'Kaynak yok — muhasebe fişi bu aşamada oluşturulmaz.',
  instalmentsPlannedCaption: 'Planlanan vade taksitleri (sunucunun sırasıyla)',
  instalmentsFinalCaption: 'Kesinleşen vade taksitleri (sunucunun sırasıyla)',
  instalmentsEmpty: 'Sunucu bu fatura için vade taksiti bildirmedi.',
  sequenceNo: 'Taksit',
  dueDate: 'Vade',
  sharePercent: 'Pay (%)',
  amount: 'Tutar',
  functionalAmount: 'TL karşılığı',
  functionalMissing: '—',
  openItemId: 'Finans açık kalem',
  deniedNote:
    'Finans bu faturayı yetki gerekçesiyle reddetti. Bu durumdan çıkış yolu (iptal ya da düzeltme) henüz kararlaştırılmadı; ' +
    'ekran bir çözüm önermez.',
  redriveNote:
    'Finans yanıtı beklenirken otomatik yeniden gönderim politikası (sıklık ve üst sınır) karar bekliyor; bu ekran arka planda ' +
    'yeniden göndermez.',
  controlsUnavailable:
    'Kesinleştirme ve yeniden gönderme düğmeleri, bu işlemlerin ekran yetkisi yayımlanana kadar gösterilmez.',
  finalize: 'Faturayı kesinleştir',
  finalizeNote:
    'Kesinleştirme faturayı kilitler ve vade planını Finans\'a gönderir; geri alınamaz. Tutar farkı açıksa sunucu kesinleştirmez.',
  resend: "Finans'a yeniden gönder",
  resendNote: "Finans yanıtı beklenen kesinleşmiş fatura aynı vade planıyla yeniden gönderilir; ikinci bir açık kalem oluşmaz.",
  submittingFinalize: 'Kesinleştirme gönderiliyor…',
  submittingResend: 'Yeniden gönderim yapılıyor…',
  completedFinalize: 'Fatura kesinleştirildi. Finans durumu aşağıda gösteriliyor.',
  completedResend: "Fatura Finans'a yeniden gönderildi. Finans durumu aşağıda gösteriliyor.",
  replayed: 'Bu işlem daha önce kaydedilmişti; güncel durum gösteriliyor.',
  unknown: 'İşlemin sonucu alınamadı; kaydedilmiş olabilir. Aynı işlemi tekrar göndermek ikinci bir kayıt oluşturmaz.',
  unknownOffline:
    'Bağlantı yok; işlemin sonucu alınamadı, kaydedilmiş olabilir. Bağlantı gelince aynı işlemi tekrar gönderin; ikinci bir ' +
    'kayıt oluşmaz.',
  unknownNetwork:
    'Sunucuya ulaşılamadı; işlemin sonucu alınamadı, kaydedilmiş olabilir. Aynı işlemi tekrar göndermek ikinci bir kayıt ' +
    'oluşturmaz.',
  unknownServiceUnavailable:
    'İşlem şu anda değerlendirilemedi: Satış servisi ya da Finans yanıt vermiyor. Biraz sonra aynı işlemi tekrar gönderin; ' +
    'ikinci bir kayıt oluşmaz.',
  unknownMalformed:
    'Sunucunun yanıtı beklenen biçimde değil; işlem kaydedilmiş olabilir. Aynı işlemi tekrar göndermek ikinci bir kayıt ' +
    'oluşturmaz.',
  abandoned: 'Sonucu bilinmeyen işlem bırakıldı. Aynı işlemi yeniden başlatmak aynı kaydı kullanır.',
  retry: 'Aynı işlemi tekrar gönder',
  abandon: 'Bırak',
  unauthenticated: 'Oturumunuz sona erdi; işlem yapılmadı.',
  forbidden: 'Bu işlem için yetkiniz yok; işlem yapılmadı.',
  notFound: 'Fatura bulunamadı; işlem yapılmadı.',
  conflict: 'Sunucu işlemi yapmadı; faturanın güncel durumu yeniden okundu.',
  validation: 'Sunucu işlemi kabul etmedi; işlem yapılmadı.',
  unexpected: 'Beklenmeyen bir yanıt alındı; işlem yapılmadı.',
  versionConflict: 'Fatura siz gördükten sonra değişti; güncel sürüm yeniden okundu.',
  stateConflict: 'Fatura bu işlem için uygun durumda değil.',
} as const
