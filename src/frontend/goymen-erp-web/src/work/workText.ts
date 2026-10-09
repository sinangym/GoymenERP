/** D-05 "Onaylarım" — the user-facing words and paths of the Finance approval pilot. */

import { OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE } from '../finance/officialInvoiceImportRoutes'
import { CAPABILITY } from '../shell/capabilities'

/** The native capability the pilot's single connected kind (Finance official-invoice-import approval) belongs to. */
export const WORK_APPROVALS_CAPABILITY = CAPABILITY.financeOfficialInvoiceImportApprove

export const WORK_ROUTE = {
  approvals: '/onaylarim',
  approvalDetail: '/onaylarim/:kind/:approvalId',
} as const

export const approvalDetailRoute = (kind: string, approvalId: string) =>
  `${WORK_ROUTE.approvals}/${encodeURIComponent(kind)}/${encodeURIComponent(approvalId)}`

/** Finance's own approval screen, opened on this record (#640 reopens it from `?approvalId=`). */
export const nativeApprovalPath = (item: { readonly approvalId: string }) =>
  `${OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE}?${new URLSearchParams({ approvalId: item.approvalId }).toString()}`

export const APPROVALS_TEXT = {
  heading: 'Onaylarım',
  /** The pilot's honest scope: one module, one kind; nothing else is presented as connected. */
  pilotScope:
    'Pilot kapsamı: yalnız Finans — resmî fatura aktarım onayları. Diğer modüllerin onayları bu listeye henüz bağlanmadı.',
  /** The approval is never given here. */
  nativeOnly: 'Onay bu ekrandan verilmez; Finans onay ekranında, Finans\'ın kendi yetki ve kişi ayrılığı kurallarıyla verilir.',
  caption: 'Onayınızı bekleyen Finans kayıtları',
  kind: 'Onay türü',
  kindFinanceOfficialInvoiceImport: 'Finans · Resmî fatura aktarımı',
  submittedBy: 'Gönderen',
  submittedAt: 'Gönderim',
  itemCount: 'Kalem',
  record: 'Kayıt',
  openDetail: 'Ayrıntı',
  openNative: 'Finans onay ekranında aç',
  loadMore: 'Daha fazla göster',
  loadingMore: 'Yükleniyor…',
  end: 'Listenin sonu.',
  nextPageFailed: 'Sonraki sayfa alınamadı. Gösterilen satırlar geçerli.',
  subject: 'bekleyen Finans onayları',
  detailSubject: 'Finans onay kaydı',
  backToList: 'Onaylarım',
  detailHeading: 'Resmî fatura aktarım onayı',
  status: 'Durum',
  statusPending: 'Onay bekliyor',
  templateVersion: 'Şablon sürümü',
  batchVersion: 'Aktarım sürümü',
  /** Home entry point. */
  homeHeading: 'Onaylarım',
  homeText: 'Finans pilotu: onayınızı bekleyen resmî fatura aktarımları.',
  homeLink: 'Bekleyen Finans onaylarını aç',
} as const

export const APPROVAL_KIND_TEXT: Readonly<Record<string, string>> = {
  'finance.official-invoice-import.v1': APPROVALS_TEXT.kindFinanceOfficialInvoiceImport,
}

/** The same reading as the other modules' instants: Turkish medium date, short time. */
export function formatInstant(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}
