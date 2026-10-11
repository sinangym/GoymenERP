/** D-05 "Onaylarım" — the user-facing words, paths and connected sources of the approval inbox. */

import { OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE } from '../finance/officialInvoiceImportRoutes'
import {
  INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
  INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY,
} from '../inventory/capabilities'
import { inventoryLabelDetailPath } from '../inventory/labelDetailRoute'
import { formatKg } from '../inventory/stockPositionText'
import { SALES_CAPABILITY, salesDetailPath } from '../sales/capabilities'
import { CAPABILITY } from '../shell/capabilities'
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
  INVENTORY_STOCK_COUNT_KIND,
  INVENTORY_STOCK_SCRAP_KIND,
  SALES_ORDER_DRAFT_KIND,
  type ApprovalItem,
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
  readonly detailHeading: string
  readonly openNative: string
  /** The owner's own screen for this exact record: built only from the verified item the read returned. */
  readonly nativePath: (item: ApprovalItem) => string
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
  detailHeading: 'Resmî fatura aktarım onayı',
  openNative: 'Finans onay ekranında aç',
  // #640 reopens Finance's own approval screen on this record from `?approvalId=`.
  nativePath: (item) =>
    `${OFFICIAL_INVOICE_IMPORT_APPROVALS_ROUTE}?${new URLSearchParams({ approvalId: item.approvalId }).toString()}`,
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
  detailHeading: 'Satış sipariş taslağı iç onayı',
  openNative: 'Satış siparişinde aç',
  nativePath: (item) => salesDetailPath(item.approvalId),
}

/**
 * Inventory (#667): a count or a scrap is decided on the label's own screen, so the native link is the label —
 * `/stok/etiket/{labelId}`, the route the stock-positions capability already gates — never a Work command. An item of
 * another kind has no label; its link then stays on the inbox itself.
 */
const inventoryLabelPath = (item: ApprovalItem): string =>
  item.kind === INVENTORY_STOCK_COUNT_KIND || item.kind === INVENTORY_STOCK_SCRAP_KIND
    ? inventoryLabelDetailPath(item.labelId)
    : WORK_ROUTE.approvals

export const INVENTORY_STOCK_COUNT_SOURCE: ApprovalSource = {
  kind: INVENTORY_STOCK_COUNT_KIND,
  capability: INVENTORY_STOCK_COUNT_APPROVE_CAPABILITY,
  nativeCapability: INVENTORY_STOCK_POSITIONS_CAPABILITY,
  token: 'stok-sayim',
  label: 'Stok sayım farkı',
  kindText: 'Stok · Sayım farkı onayı',
  caption: 'Onayınızı bekleyen stok sayım farkları',
  subject: 'onay bekleyen stok sayım farkları',
  detailHeading: 'Stok sayım farkı onayı',
  openNative: 'Etiket ekranında aç',
  nativePath: inventoryLabelPath,
}

export const INVENTORY_STOCK_SCRAP_SOURCE: ApprovalSource = {
  kind: INVENTORY_STOCK_SCRAP_KIND,
  capability: INVENTORY_STOCK_SCRAP_APPROVE_CAPABILITY,
  nativeCapability: INVENTORY_STOCK_POSITIONS_CAPABILITY,
  token: 'stok-hurda',
  label: 'Stok hurda çıkışı',
  kindText: 'Stok · Hurda çıkışı onayı',
  caption: 'Onayınızı bekleyen stok hurda çıkışları',
  subject: 'onay bekleyen stok hurda çıkışları',
  detailHeading: 'Stok hurda çıkışı onayı',
  openNative: 'Etiket ekranında aç',
  nativePath: inventoryLabelPath,
}

/** Every connected source, in the order the inbox offers them. */
export const APPROVAL_SOURCES: readonly ApprovalSource[] = [
  FINANCE_SOURCE, SALES_SOURCE, INVENTORY_STOCK_COUNT_SOURCE, INVENTORY_STOCK_SCRAP_SOURCE,
]

/** "A", "A ve B", "A, B ve C": the Turkish list the home entry and the scope sentence read. */
export function joinLabels(labels: readonly string[]): string {
  return labels.length <= 1 ? labels.join('') : `${labels.slice(0, -1).join(', ')} ve ${labels[labels.length - 1]}`
}

/** The capabilities any one of which opens the inbox (each the owner's own; none is new). */
export const WORK_APPROVALS_CAPABILITIES: readonly string[] = APPROVAL_SOURCES.map((source) => source.capability)

export const sourceOfKind = (kind: string): ApprovalSource | null =>
  APPROVAL_SOURCES.find((source) => source.kind === kind) ?? null

export const APPROVALS_TEXT = {
  heading: 'Onaylarım',
  /** The inbox's honest scope: exactly the connected sources; nothing else is presented as connected. */
  pilotScope:
    'Bu listeye bağlı kaynaklar: Finans — resmî fatura aktarım onayları; Satış — sipariş taslağı iç onayları; Stok — sayım farkı ve hurda çıkışı onayları. Diğer modüllerin onayları henüz bağlanmadı.',
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
  detailHeading: FINANCE_SOURCE.detailHeading,
  detailHeadingSales: SALES_SOURCE.detailHeading,
  /** Inventory facts, in the label screen's own words. */
  labelId: 'Etiket kimliği',
  labelVersion: 'Etiket sürümü',
  bookKg: 'Defter (kg)',
  countedKg: 'Sayılan (kg)',
  differenceKg: 'Fark (kg)',
  scrapKg: 'Hurda (kg)',
  remainingKg: 'Kalan (kg)',
  reason: 'Gerekçe',
  block: 'Blokaj',
  blockFree: 'Serbest',
  blockBlocked: 'Bloklu',
  owner: 'Mal sahibi',
  custodian: 'Malı tutan',
  status: 'Durum',
  statusPending: 'Onay bekliyor',
  templateVersion: 'Şablon sürümü',
  batchVersion: 'Aktarım sürümü',
  /** Home entry point. */
  homeHeading: 'Onaylarım',
  homeText: (labels: readonly string[]) => `Onayınızı bekleyen kayıtlar: ${joinLabels(labels)}.`,
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

/**
 * A stock-count difference as the label screen says it: the producer's exact signed kilograms in Turkish digits, with
 * the sign kept visible both ways (a shortage "-3", a surplus "+3"). Nothing is rounded or recomputed.
 */
export function formatSignedKg(value: string): string {
  return value.startsWith('-') ? `-${formatKg(value.slice(1))}` : `+${formatKg(value)}`
}
