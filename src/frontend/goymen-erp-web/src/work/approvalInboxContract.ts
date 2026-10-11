/**
 * D-05 "Onaylarım" — the Work read of native approvals waiting for the signed-in actor (#639/#644/#645,
 * `GET /api/v1/work/approvals[?kind=&cursor=]` and `GET /api/v1/work/approvals/{kind}/{approvalId}`).
 *
 * Each connected native kind is read on its own: one kind per list request (an omitted kind stays the Finance default
 * of the first pilot; Sales is always asked for explicitly), and every answer is bound to the kind, company and record
 * that were asked for. The Work read is a thin projection of each owner's own pending-approval read — the owner decides
 * who may see a record and owns every decision — so:
 *  - a Finance item is judged by Finance's own pending-batch rules, by mapping it back onto the Finance batch shape;
 *  - a Sales item is the Sales draft awaiting its internal approval (`PendingApproval`), with its document number,
 *    commercial revision and version, and `isOwnDraft` as a FACT the Sales screen's own policy interprets;
 *  - an Inventory item (#667) is a stock-count difference or a stock scrap still awaiting its decision, judged by
 *    Inventory's own count/scrap rules, by mapping it back onto the Inventory view the label screen itself reads.
 * An item of another kind, company or state, a duplicate, or an unknown member is not this contract: the whole answer
 * is refused rather than partly shown.
 */

import {
  decodePendingBatch,
  decodePendingDetail,
  type OfficialInvoicePendingDetail,
} from '../finance/officialInvoiceImportApprovalContract'
import { decodeStockCountView } from '../inventory/stockCountContract'
import { decodeStockScrapView } from '../inventory/stockScrapContract'
import { STOCK_PARTY_KINDS, type StockPartyKind } from '../inventory/stockDisplayContract'

export const FINANCE_OFFICIAL_INVOICE_IMPORT_KIND = 'finance.official-invoice-import.v1'
export const SALES_ORDER_DRAFT_KIND = 'sales.order-draft.v1'
export const INVENTORY_STOCK_COUNT_KIND = 'inventory.stock-count.v1'
export const INVENTORY_STOCK_SCRAP_KIND = 'inventory.stock-scrap.v1'
export type ApprovalKind =
  | typeof FINANCE_OFFICIAL_INVOICE_IMPORT_KIND
  | typeof SALES_ORDER_DRAFT_KIND
  | typeof INVENTORY_STOCK_COUNT_KIND
  | typeof INVENTORY_STOCK_SCRAP_KIND
export const APPROVAL_KINDS: readonly ApprovalKind[] = [
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND, SALES_ORDER_DRAFT_KIND, INVENTORY_STOCK_COUNT_KIND, INVENTORY_STOCK_SCRAP_KIND,
]
export const isApprovalKind = (value: unknown): value is ApprovalKind =>
  typeof value === 'string' && (APPROVAL_KINDS as readonly string[]).includes(value)

export const WORK_APPROVALS_PATH = '/work/approvals'
export const workApprovalPath = (kind: string, approvalId: string) =>
  `${WORK_APPROVALS_PATH}/${encodeURIComponent(kind)}/${encodeURIComponent(approvalId)}`

export interface FinanceApprovalItem {
  readonly kind: typeof FINANCE_OFFICIAL_INVOICE_IMPORT_KIND
  readonly approvalId: string
  readonly companyId: string
  readonly status: 'Pending'
  readonly version: string
  readonly batchVersion: string
  readonly checksum: string
  readonly templateVersion: string
  readonly itemCount: number
  readonly submittedBy: string
  readonly submittedAtUtc: string
}

export interface SalesApprovalItem {
  readonly kind: typeof SALES_ORDER_DRAFT_KIND
  /** The Sales owner's draft id: the native record opened at `/satis/siparisler/{draftId}`. */
  readonly approvalId: string
  readonly companyId: string
  readonly status: 'PendingApproval'
  readonly version: string
  readonly revision: string
  readonly documentNo: string
  /** A fact about the signed-in caller (they created this draft) — never a decision; the Sales screen applies its policy. */
  readonly isOwnDraft: boolean
  readonly submittedBy: string
  readonly submittedAtUtc: string
}

/** What a stock count and a stock scrap awaiting their decision have in common: the label and its position. */
interface InventoryApprovalFacts {
  /** The Inventory owner's count/scrap id. The record is decided on the label's own screen, `/stok/etiket/{labelId}`. */
  readonly approvalId: string
  readonly companyId: string
  readonly status: 'PendingApproval'
  /** The label's version, as the producer publishes it for both `version` and `labelVersion`. */
  readonly version: string
  readonly labelId: string
  readonly productId: string
  readonly locationId: string
  readonly labelVersion: string
  readonly ownerKind: StockPartyKind
  readonly ownerId: string
  readonly custodianKind: StockPartyKind
  readonly custodianId: string
  readonly block: 'Free' | 'Blocked'
  /** Canonical kilograms as text; never parsed into a number here. */
  readonly bookKg: string
  readonly reason: string
  readonly submittedBy: string
  readonly submittedAtUtc: string
}

export interface InventoryStockCountApprovalItem extends InventoryApprovalFacts {
  readonly kind: typeof INVENTORY_STOCK_COUNT_KIND
  /** counted − book, signed and never zero: a count without a difference awaits nobody. */
  readonly differenceKg: string
  readonly countedKg: string
}

export interface InventoryStockScrapApprovalItem extends InventoryApprovalFacts {
  readonly kind: typeof INVENTORY_STOCK_SCRAP_KIND
  readonly scrapKg: string
  /** book − scrap. */
  readonly remainingKg: string
}

export type InventoryApprovalItem = InventoryStockCountApprovalItem | InventoryStockScrapApprovalItem

export type ApprovalItem = FinanceApprovalItem | SalesApprovalItem | InventoryApprovalItem
export type ApprovalItemOf<K extends ApprovalKind> = Extract<ApprovalItem, { readonly kind: K }>

export interface ApprovalPage<I extends ApprovalItem = ApprovalItem> {
  readonly companyId: string
  readonly items: readonly I[]
  /** Opaque; null is the end of the queue. */
  readonly nextCursor: string | null
}

export interface FinanceApprovalDetail {
  readonly item: FinanceApprovalItem
  readonly preview: OfficialInvoicePendingDetail['preview']
}

export interface SalesApprovalDetail {
  readonly item: SalesApprovalItem
}

/** Inventory's detail is the item alone: the label screen reads the record, its history and its position afresh. */
export interface InventoryApprovalDetail {
  readonly item: InventoryApprovalItem
}

export type ApprovalDetail = FinanceApprovalDetail | SalesApprovalDetail | InventoryApprovalDetail

const FINANCE_ITEM_KEYS = [
  'kind', 'approvalId', 'companyId', 'status', 'version', 'batchVersion', 'checksum', 'templateVersion', 'itemCount',
  'submittedBy', 'submittedAtUtc',
] as const

const SALES_ITEM_KEYS = [
  'kind', 'approvalId', 'companyId', 'status', 'version', 'revision', 'documentNo', 'isOwnDraft', 'submittedBy',
  'submittedAtUtc',
] as const

/** The producer's exact members (#667 `InventoryStockCountNativeApprovalReadItemV1`). */
const INVENTORY_COUNT_ITEM_KEYS = [
  'kind', 'approvalId', 'companyId', 'status', 'version', 'labelId', 'productId', 'locationId', 'differenceKg',
  'submittedBy', 'submittedAtUtc', 'labelVersion', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId', 'block',
  'bookKg', 'countedKg', 'reason',
] as const

/** The producer's exact members (#667 `InventoryStockScrapNativeApprovalReadItemV1`). */
const INVENTORY_SCRAP_ITEM_KEYS = [
  'kind', 'approvalId', 'companyId', 'status', 'version', 'labelId', 'productId', 'locationId', 'scrapKg',
  'submittedBy', 'submittedAtUtc', 'labelVersion', 'ownerKind', 'ownerId', 'custodianKind', 'custodianId', 'block',
  'bookKg', 'remainingKg', 'reason',
] as const

type Obj = Record<string, unknown>

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
const REVISION = /^Rev\.(?:0|[1-9][0-9]*)$/
const VERSION = /^[1-9][0-9]*$/

function isObject(value: unknown): value is Obj {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function exact(value: Obj, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

const isId = (value: unknown): value is string => typeof value === 'string' && GUID.test(value) && value !== EMPTY_GUID
const isFilled = (value: unknown, max: number): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= max
const isInstant = (value: unknown): value is string =>
  typeof value === 'string' && INSTANT.test(value) && !Number.isNaN(Date.parse(value))

/** The Finance batch this Work item projects; Finance's decoder then judges every member by its own rules. */
function asFinanceBatch(item: Obj): Obj {
  return {
    batchId: item.approvalId,
    companyId: item.companyId,
    batchVersion: item.batchVersion,
    batchChecksum: item.checksum,
    templateVersion: item.templateVersion,
    status: item.status,
    version: item.version,
    rowCount: item.itemCount,
    uploadedBy: item.submittedBy,
    uploadedAtUtc: item.submittedAtUtc,
    approvedBy: null,
    approvedAtUtc: null,
  }
}

function decodeFinanceItem(value: Obj): FinanceApprovalItem | null {
  if (!exact(value, FINANCE_ITEM_KEYS) || value.kind !== FINANCE_OFFICIAL_INVOICE_IMPORT_KIND) return null
  if (!decodePendingBatch(asFinanceBatch(value)).ok) return null
  return {
    kind: FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
    approvalId: value.approvalId as string,
    companyId: value.companyId as string,
    status: 'Pending',
    version: value.version as string,
    batchVersion: value.batchVersion as string,
    checksum: value.checksum as string,
    templateVersion: value.templateVersion as string,
    itemCount: value.itemCount as number,
    submittedBy: value.submittedBy as string,
    submittedAtUtc: value.submittedAtUtc as string,
  }
}

function decodeSalesItem(value: Obj): SalesApprovalItem | null {
  if (
    !exact(value, SALES_ITEM_KEYS) ||
    value.kind !== SALES_ORDER_DRAFT_KIND ||
    !isId(value.approvalId) ||
    !isId(value.companyId) ||
    value.status !== 'PendingApproval' ||
    typeof value.version !== 'string' || !VERSION.test(value.version) || value.version.length > 64 ||
    typeof value.revision !== 'string' || !REVISION.test(value.revision) ||
    !isFilled(value.documentNo, 200) ||
    typeof value.isOwnDraft !== 'boolean' ||
    !isFilled(value.submittedBy, 128) ||
    !isInstant(value.submittedAtUtc)
  ) {
    return null
  }
  return {
    kind: SALES_ORDER_DRAFT_KIND,
    approvalId: value.approvalId,
    companyId: value.companyId,
    status: 'PendingApproval',
    version: value.version,
    revision: value.revision,
    documentNo: value.documentNo,
    isOwnDraft: value.isOwnDraft,
    submittedBy: value.submittedBy,
    submittedAtUtc: value.submittedAtUtc,
  }
}

const isPartyKind = (value: unknown): value is StockPartyKind =>
  typeof value === 'string' && (STOCK_PARTY_KINDS as readonly string[]).includes(value)

/**
 * What a count and a scrap item share, checked here because the Work projection adds it: the kind's own state, the
 * producer's `version` (the label version, published twice), who submitted it and when, and the two closed party
 * kinds. Everything about the label, its position and its kilograms is then judged by the Inventory owner's decoder.
 */
function inventoryFactsHold(value: Obj): boolean {
  return value.status === 'PendingApproval' &&
    isId(value.approvalId) && isId(value.companyId) && isId(value.labelId) && isId(value.productId) &&
    isId(value.locationId) && isId(value.ownerId) && isId(value.custodianId) &&
    typeof value.version === 'string' && VERSION.test(value.version) && value.version === value.labelVersion &&
    isPartyKind(value.ownerKind) && isPartyKind(value.custodianKind) &&
    isFilled(value.submittedBy, 128) && isInstant(value.submittedAtUtc)
}

/**
 * The Inventory count this Work item projects. The item carries one instant (when it was submitted); the owner's view
 * has two, and both are checked as that instant — the owner's rule also requires it to be UTC.
 */
function decodeInventoryCountItem(value: Obj): InventoryStockCountApprovalItem | null {
  if (!exact(value, INVENTORY_COUNT_ITEM_KEYS) || value.kind !== INVENTORY_STOCK_COUNT_KIND || !inventoryFactsHold(value)) return null
  const view = decodeStockCountView({
    countId: value.approvalId, companyId: value.companyId, labelId: value.labelId, labelVersion: value.labelVersion,
    ownerKind: value.ownerKind, ownerId: value.ownerId, custodianKind: value.custodianKind, custodianId: value.custodianId,
    locationId: value.locationId, productId: value.productId, block: value.block, bookKg: value.bookKg,
    countedKg: value.countedKg, differenceKg: value.differenceKg, countedAtUtc: value.submittedAtUtc,
    reason: value.reason, recordedAtUtc: value.submittedAtUtc, status: value.status, decision: null,
  })
  // Inventory's own rule for `PendingApproval`: difference = counted − book, and it is not zero.
  if (view === null || view.status !== 'PendingApproval') return null
  return {
    kind: INVENTORY_STOCK_COUNT_KIND,
    approvalId: view.countId, companyId: view.companyId, status: 'PendingApproval', version: value.version as string,
    labelId: view.labelId, productId: view.productId, locationId: view.locationId, labelVersion: view.labelVersion,
    ownerKind: value.ownerKind as StockPartyKind, ownerId: view.ownerId,
    custodianKind: value.custodianKind as StockPartyKind, custodianId: view.custodianId,
    block: view.block as 'Free' | 'Blocked', bookKg: view.bookKg, countedKg: view.countedKg,
    differenceKg: view.differenceKg, reason: view.reason,
    submittedBy: value.submittedBy as string, submittedAtUtc: value.submittedAtUtc as string,
  }
}

/** The Inventory scrap this Work item projects, under the same reading of its one instant. */
function decodeInventoryScrapItem(value: Obj): InventoryStockScrapApprovalItem | null {
  if (!exact(value, INVENTORY_SCRAP_ITEM_KEYS) || value.kind !== INVENTORY_STOCK_SCRAP_KIND || !inventoryFactsHold(value)) return null
  const view = decodeStockScrapView({
    scrapId: value.approvalId, companyId: value.companyId, labelId: value.labelId, labelVersion: value.labelVersion,
    ownerKind: value.ownerKind, ownerId: value.ownerId, custodianKind: value.custodianKind, custodianId: value.custodianId,
    locationId: value.locationId, productId: value.productId, block: value.block, bookKg: value.bookKg,
    scrapKg: value.scrapKg, remainingKg: value.remainingKg, scrappedAtUtc: value.submittedAtUtc,
    reason: value.reason, recordedAtUtc: value.submittedAtUtc, status: value.status, decision: null,
  })
  // Inventory's own rule: a positive scrap no larger than the book, and remaining = book − scrap.
  if (view === null || view.status !== 'PendingApproval') return null
  return {
    kind: INVENTORY_STOCK_SCRAP_KIND,
    approvalId: view.scrapId, companyId: view.companyId, status: 'PendingApproval', version: value.version as string,
    labelId: view.labelId, productId: view.productId, locationId: view.locationId, labelVersion: view.labelVersion,
    ownerKind: value.ownerKind as StockPartyKind, ownerId: view.ownerId,
    custodianKind: value.custodianKind as StockPartyKind, custodianId: view.custodianId,
    block: view.block as 'Free' | 'Blocked', bookKg: view.bookKg, scrapKg: view.scrapKg,
    remainingKg: view.remainingKg, reason: view.reason,
    submittedBy: value.submittedBy as string, submittedAtUtc: value.submittedAtUtc as string,
  }
}

const ITEM_DECODERS: { readonly [K in ApprovalKind]: (value: Obj) => ApprovalItemOf<K> | null } = {
  [FINANCE_OFFICIAL_INVOICE_IMPORT_KIND]: decodeFinanceItem,
  [SALES_ORDER_DRAFT_KIND]: decodeSalesItem,
  [INVENTORY_STOCK_COUNT_KIND]: decodeInventoryCountItem,
  [INVENTORY_STOCK_SCRAP_KIND]: decodeInventoryScrapItem,
}

/** One item of the kind that was asked for; any other kind is not an answer to that request. */
export function decodeApprovalItem<K extends ApprovalKind>(value: unknown, kind: K): ApprovalItemOf<K> | null {
  // A kind this client does not know has no decoder: it stays closed rather than being read as another kind.
  if (!isObject(value) || !Object.hasOwn(ITEM_DECODERS, kind)) return null
  const item = ITEM_DECODERS[kind](value)
  return item !== null && item.kind === kind ? item : null
}

/** A page of the requested kind, of one company, with no repeated record. */
export function decodeApprovalPage<K extends ApprovalKind>(value: unknown, kind: K): ApprovalPage<ApprovalItemOf<K>> | null {
  // A page of a kind this client does not know is not read at all — not even an empty one.
  if (!isApprovalKind(kind)) return null
  if (!isObject(value) || !exact(value, ['companyId', 'items', 'nextCursor']) || !Array.isArray(value.items)) return null
  if (!isId(value.companyId)) return null
  if (!(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) return null
  const items: ApprovalItemOf<K>[] = []
  const ids = new Set<string>()
  for (const entry of value.items) {
    const item = decodeApprovalItem(entry, kind)
    const id = item?.approvalId.toLowerCase()
    if (item === null || id === undefined || ids.has(id) || item.companyId.toLowerCase() !== value.companyId.toLowerCase()) return null
    ids.add(id)
    items.push(item)
  }
  return { companyId: value.companyId, items, nextCursor: value.nextCursor }
}

/**
 * `kind` and `expectedId`: the record that was asked for — a detail of another kind or record is not an answer.
 * Finance's detail carries its immutable preview (judged by Finance's own detail rules); Sales' and Inventory's carry
 * only the item — the owner's own screen reads the record itself afresh.
 */
export function decodeApprovalDetail(value: unknown, kind: ApprovalKind, expectedId: string): ApprovalDetail | null {
  if (!isObject(value)) return null
  if (kind === SALES_ORDER_DRAFT_KIND) {
    if (!exact(value, ['item'])) return null
    const item = decodeApprovalItem(value.item, SALES_ORDER_DRAFT_KIND)
    return item !== null && item.approvalId.toLowerCase() === expectedId.toLowerCase() ? { item } : null
  }
  if (kind === INVENTORY_STOCK_COUNT_KIND || kind === INVENTORY_STOCK_SCRAP_KIND) {
    if (!exact(value, ['item'])) return null
    const item: InventoryApprovalItem | null = decodeApprovalItem(value.item, kind)
    return item !== null && item.approvalId.toLowerCase() === expectedId.toLowerCase() ? { item } : null
  }
  if (kind !== FINANCE_OFFICIAL_INVOICE_IMPORT_KIND) return null
  if (!exact(value, ['item', 'preview']) || !isObject(value.item)) return null
  const item = decodeApprovalItem(value.item, FINANCE_OFFICIAL_INVOICE_IMPORT_KIND)
  if (item === null || item.approvalId.toLowerCase() !== expectedId.toLowerCase()) return null
  const finance = decodePendingDetail({ batch: asFinanceBatch(value.item), preview: value.preview })
  return finance.ok ? { item, preview: finance.value.preview } : null
}
