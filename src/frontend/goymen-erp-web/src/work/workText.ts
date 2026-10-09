/** D-05 "Onaylarım" — the user-facing words, paths and connected sources of the approval inbox. */

import { OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE } from '../finance/officialInvoiceImportRoutes'
import { SALES_CAPABILITY, salesDetailPath } from '../sales/capabilities'
import { CAPABILITY } from '../shell/capabilities'
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
  SALES_ORDER_DRAFT_KIND,
  type ApprovalKind,
} from './approvalInboxContract'

export const WORK_ROUTE = {
  approvals: '/onaylarim',
  approvalDetail: '/onaylarim/:kind/:approvalId',
} as const

/** The inbox's `?kaynak=` member: which connected source the list shows (kept in the address for Back/Forward). */
export const SOURCE_PARAM = 'kaynak'

export const approvalDetailRoute = (kind: string, approvalId: string) =>
  `${WORK_ROUTE.approvals}/${encodeURIComponent(kind)}/${encodeURIComponent(approvalId)}`

/**
 * A connected native approval source. Each is shown only to an actor the server grants the owner's OWN approve
 * capability; there is no shared inbox permission. Its records open on the owner's own screen, which reads the record
 * afresh and owns every decision.
 */
export interface ApprovalSource {
  readonly kind: ApprovalKind
  /** The owner's native approve capability: the source is listed only with it. */
  readonly capability: string
  /** The capability of the owner's screen the native link opens (that route's own gate). */
  readonly nativeCapability: string
  readonly token: string
  readonly label: string
  readonly kindText: string
  readonly caption: string
  readonly subject: string
  readonly openNative: string
  readonly nativePath: (approvalId: string) => string
}

export const FINANCE_SOURCE: ApprovalSource = {
  kind: FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
  capability: CAPABILITY.financeOfficialInvoiceImportApprove,
  nativeCapability: CAPABILITY.financeOfficialInvoiceImportApprove,
  token: 'finans',
  label: 'Finans',
  kindText: 'Finans · Resmî fatura aktarımı',
  caption: 'Onayınızı bekleyen Finans kayıtları',
  subject: 'bekleyen Finans onayları',
  openNative: 'Finans onay ekranında aç',
  // #640 reopens Finance's own approval screen on this record from `?approvalId=`.
  nativePath: (approvalId) =>
    `${OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE}?${new URLSearchParams({ approvalId }).toString()}`,
}

export const SALES_SOURCE: ApprovalSource = {
  kind: SALES_ORDER_DRAFT_KIND,
  capability: SALES_CAPABILITY.ordersApprove,
  nativeCapability: SALES_CAPABILITY.draftsDetail,
  token: 'satis',
  label: 'Satış',
  kindText: 'Satış · Sipariş taslağı iç onayı',
  caption: 'İç onayınızı bekleyen satış sipariş taslakları',
  subject: 'iç onay bekleyen satış taslakları',
  openNative: 'Satış siparişinde aç',
  nativePath: (approvalId) => salesDetailPath(approvalId),
}

/** Every connected source, in the order the inbox offers them. */
export const APPROVAL_SOURCES: readonly ApprovalSource[] = [FINANCE_SOURCE, SALES_SOURCE]

/** The capabilities any one of which opens the inbox (each the owner's own; none is new). */
export const WORK_APPROVALS_CAPABILITIES: readonly string[] = APPROVAL_SOURCES.map((source) => source.capability)

export const sourceOfKind = (kind: string): ApprovalSource | null =>
  APPROVAL_SOURCES.find((source) => source.kind === kind) ?? null

export const APPROVALS_TEXT = {
  heading: 'Onaylarım',
  /** The inbox's honest scope: exactly the connected sources; nothing else is presented as connected. */
  pilotScope:
    'Bu listeye bağlı kaynaklar: Finans — resmî fatura aktarım onayları; Satış — sipariş taslağı iç onayları. Diğer modüllerin onayları henüz bağlanmadı.',
  /** The approval is never given here. */
  nativeOnly:
    'Onay bu ekrandan verilmez; kaydın kendi modül ekranında, o modülün kendi yetki, kişi ayrılığı ve sürüm kurallarıyla verilir.',
  sources: 'Onay kaynağı',
  kind: 'Onay türü',
  kindFinanceOfficialInvoiceImport: FINANCE_SOURCE.kindText,
  kindSalesOrderDraft: SALES_SOURCE.kindText,
  caption: FINANCE_SOURCE.caption,
  submittedBy: 'Gönderen',
  submittedAt: 'Gönderim',
  itemCount: 'Kalem',
  documentNo: 'Belge no',
  revision: 'Revizyon',
  version: 'Kayıt sürümü',
  ownDraft: 'Not',
  /** `isOwnDraft` is a fact; what it means for approval is the Sales screen's own rule. */
  ownDraftYes: 'Taslağı siz oluşturdunuz',
  record: 'Kayıt',
  openDetail: 'Ayrıntı',
  openNative: FINANCE_SOURCE.openNative,
  openNativeSales: SALES_SOURCE.openNative,
  loadMore: 'Daha fazla göster',
  loadingMore: 'Yükleniyor…',
  end: 'Listenin sonu.',
  /** Only for a failure that is temporary: the rows already shown were verified and stay. */
  nextPageFailed: 'Sonraki sayfa şu anda alınamadı. Gösterilen satırlar geçerli; aynı sayfayı yeniden deneyebilirsiniz.',
  /** Any other next-page answer closes the list: nothing of it is shown as the current result any more. */
  listClosed: 'Sonraki sayfa doğrulanamadı veya bu oturumla okunamadı; liste kapatıldı. Güncel liste için baştan yükleyin.',
  restart: 'Listeyi baştan yükle',
  subject: FINANCE_SOURCE.subject,
  detailSubject: 'onay kaydı',
  backToList: 'Onaylarım',
  detailHeading: 'Resmî fatura aktarım onayı',
  detailHeadingSales: 'Satış sipariş taslağı iç onayı',
  status: 'Durum',
  statusPending: 'Onay bekliyor',
  templateVersion: 'Şablon sürümü',
  batchVersion: 'Aktarım sürümü',
  /** Home entry point. */
  homeHeading: 'Onaylarım',
  homeText: (labels: readonly string[]) => `Onayınızı bekleyen kayıtlar: ${labels.join(' ve ')}.`,
  homeLink: 'Bekleyen onaylarımı aç',
} as const

export const APPROVAL_KIND_TEXT: Readonly<Record<string, string>> = Object.fromEntries(
  APPROVAL_SOURCES.map((source) => [source.kind, source.kindText]),
)

/** The same reading as the other modules' instants: Turkish medium date, short time. */
export function formatInstant(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}
