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
 *    commercial revision and version, and `isOwnDraft` as a FACT the Sales screen's own policy interprets.
 * An item of another kind, company or state, a duplicate, or an unknown member is not this contract: the whole answer
 * is refused rather than partly shown.
 */

import {
  decodePendingBatch,
  decodePendingDetail,
  type OfficialInvoicePendingDetail,
} from '../finance/officialInvoiceImportApprovalContract'

export const FINANCE_OFFICIAL_INVOICE_IMPORT_KIND = 'finance.official-invoice-import.v1'
export const SALES_ORDER_DRAFT_KIND = 'sales.order-draft.v1'
export type ApprovalKind = typeof FINANCE_OFFICIAL_INVOICE_IMPORT_KIND | typeof SALES_ORDER_DRAFT_KIND
export const APPROVAL_KINDS: readonly ApprovalKind[] = [FINANCE_OFFICIAL_INVOICE_IMPORT_KIND, SALES_ORDER_DRAFT_KIND]
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

export type ApprovalItem = FinanceApprovalItem | SalesApprovalItem
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

export type ApprovalDetail = FinanceApprovalDetail | SalesApprovalDetail

const FINANCE_ITEM_KEYS = [
  'kind', 'approvalId', 'companyId', 'status', 'version', 'batchVersion', 'checksum', 'templateVersion', 'itemCount',
  'submittedBy', 'submittedAtUtc',
] as const

const SALES_ITEM_KEYS = [
  'kind', 'approvalId', 'companyId', 'status', 'version', 'revision', 'documentNo', 'isOwnDraft', 'submittedBy',
  'submittedAtUtc',
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

/** One item of the kind that was asked for; any other kind is not an answer to that request. */
export function decodeApprovalItem<K extends ApprovalKind>(value: unknown, kind: K): ApprovalItemOf<K> | null {
  if (!isObject(value)) return null
  const item = kind === FINANCE_OFFICIAL_INVOICE_IMPORT_KIND ? decodeFinanceItem(value) : decodeSalesItem(value)
  return item !== null && item.kind === kind ? item as ApprovalItemOf<K> : null
}

/** A page of the requested kind, of one company, with no repeated record. */
export function decodeApprovalPage<K extends ApprovalKind>(value: unknown, kind: K): ApprovalPage<ApprovalItemOf<K>> | null {
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
 * Finance's detail carries its immutable preview (judged by Finance's own detail rules); Sales' carries only the item —
 * the Sales screen reads the record itself afresh.
 */
export function decodeApprovalDetail(value: unknown, kind: ApprovalKind, expectedId: string): ApprovalDetail | null {
  if (!isObject(value)) return null
  if (kind === SALES_ORDER_DRAFT_KIND) {
    if (!exact(value, ['item'])) return null
    const item = decodeApprovalItem(value.item, SALES_ORDER_DRAFT_KIND)
    return item !== null && item.approvalId.toLowerCase() === expectedId.toLowerCase() ? { item } : null
  }
  if (!exact(value, ['item', 'preview']) || !isObject(value.item)) return null
  const item = decodeApprovalItem(value.item, FINANCE_OFFICIAL_INVOICE_IMPORT_KIND)
  if (item === null || item.approvalId.toLowerCase() !== expectedId.toLowerCase()) return null
  const finance = decodePendingDetail({ batch: asFinanceBatch(value.item), preview: value.preview })
  return finance.ok ? { item, preview: finance.value.preview } : null
}
