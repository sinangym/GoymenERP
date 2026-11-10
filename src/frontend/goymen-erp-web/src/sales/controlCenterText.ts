/**
 * UI-65 "Kontrol Merkezi" user texts (Turkish, single source). The screen is read-only: it lists what the server says needs a
 * person and decides nothing itself — no amount, tolerance, threshold or age is computed here.
 */

import type { ControlCenterCheckState, ControlCenterKind, ControlCenterSeverity } from './controlCenterContract'

export const CONTROL_CENTER_KIND_TEXT: Readonly<Record<ControlCenterKind, string>> = {
  UninvoicedDispatchOverdue: 'Faturalanmamış sevk (gecikmiş)',
  InvoiceNeedsReview: 'Fatura incelemesi gerekli',
  InvoiceAwaitingOfficialMatch: 'Resmî e-fatura eşleşmesi bekleniyor',
  OfficialDocumentDeviation: 'Resmî belge farkı',
  InvoiceFinalizationBlocked: 'Kesinleştirme engelli',
  InvoiceFinanceAdmissionWaiting: 'Finans kabulü bekliyor',
}

export const CONTROL_CENTER_KIND_HINT: Readonly<Record<ControlCenterKind, (graceDays: number) => string>> = {
  UninvoicedDispatchOverdue: (graceDays) =>
    `Sevk gününden bu yana ${graceDays} günden uzun süredir hiçbir etkin faturaya bağlanmamış onaylı sevkler.`,
  InvoiceNeedsReview: () => 'Grup farkının TL tutarını işaret değiştirdiği ya da aştığı satırı olan etkin faturalar.',
  InvoiceAwaitingOfficialMatch: () => 'Onaylanmış, ancak resmî e-fatura eşleşmesi henüz kaydedilmemiş faturalar.',
  OfficialDocumentDeviation: () => 'Resmî belgeyle kaydedilen TL farkı sıfır olmayan eşleşmiş faturalar (tolerans sınıfı sunucunundur).',
  InvoiceFinalizationBlocked: () => 'Eşleşme farkı karara bağlanmadığı için kesinleştirilemeyen onaylı faturalar.',
  InvoiceFinanceAdmissionWaiting: () => "Kesinleşmiş, ancak Finans'ın henüz kabul etmediği faturalar; kayıtlı bir Finans reddi kritiktir.",
}

export const CONTROL_CENTER_SEVERITY_TEXT: Readonly<Record<ControlCenterSeverity, string>> = {
  Critical: 'Kritik',
  ApprovalRequired: 'Onay gerekli',
  WithinTolerance: 'Tolerans içinde',
  Info: 'Bilgi',
}

export const CONTROL_CENTER_STATE_TEXT: Readonly<Record<ControlCenterCheckState, string>> = {
  Measured: 'Ölçüldü',
  NoSource: 'Kaynak yok — listelenenler bu kontrolün görebildiği kayıtlardır',
  Unavailable: 'Kullanılamıyor — bu kontrol için sonuç yok',
}

export const CONTROL_CENTER_TEXT = {
  heading: 'Kontrol Merkezi',
  subject: 'kontrol merkezi',
  intro:
    'Satış faturalarında ve faturasını bekleyen sevklerde bir kişinin bakması gereken kayıtlar (en kritik, sonra en eski önce). ' +
    'Liste, önem derecesi ve gün sayıları sunucunundur; bu ekran hesap yapmaz ve kayıt değiştirmez.',
  noAmounts: 'Tutar bu ekranda gösterilmez.',
  invoiceDetailHint: 'Bir faturanın ayrıntısı ve kesinleştirme durumu, bağlı olduğu siparişin Faturalar sekmesinde açılır.',
  facts: 'Ölçüm bilgileri',
  businessDate: 'İş günü',
  graceDays: 'Faturalanmamış sevk süresi',
  graceDaysValue: (days: number) => `${days} gün`,
  checksHeading: 'Kontroller',
  checksCaption: 'Kontrollerin durumu ve kayıt sayısı',
  check: 'Kontrol',
  checkState: 'Durum',
  checkItems: 'Kayıt',
  checkTruncated: 'Okuma sınırı',
  truncated: 'Sınıra ulaşıldı — liste eksik olabilir',
  notTruncated: '—',
  showOnly: 'Yalnız bunları göster',
  showOnlyLabel: (kind: string) => `Yalnız “${kind}” kayıtlarını göster`,
  truncatedNotice: 'Bazı kontroller okuma sınırına ulaştı; o kontrollerin listesi eksik olabilir.',
  unavailableNotice: 'Bazı kontroller şu an ölçülemedi; o kontroller için “kayıt yok” denemez.',
  itemsHeading: 'Bakılması gereken kayıtlar',
  itemsCaption: 'Kontrol merkezi kayıtları',
  filteredTo: (kind: string) => `Yalnız “${kind}” kayıtları gösteriliyor.`,
  allKinds: 'Tüm kontrolleri göster',
  severity: 'Önem',
  kind: 'Kontrol',
  reference: 'Belge no',
  referenceDate: 'Belge / sevk günü',
  ageDays: 'Gün',
  count: 'Satır',
  customer: 'Müşteri',
  links: 'Bağlantılar',
  openOrder: 'Siparişi aç',
  /** SAT-D246: an entry over several orders gives each order its own, distinct link label. */
  openOrderNth: (n: number, total: number) => `Siparişi aç (${n}/${total})`,
  openCandidates: 'Faturaya hazır listesinde göster',
  openCari: 'Cari kartı',
  accountIdentifier: 'Cari hesap kimliği',
} as const
