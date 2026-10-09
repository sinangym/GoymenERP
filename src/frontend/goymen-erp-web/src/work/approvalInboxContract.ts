/**
 * D-05 "Onaylarım" — the Work read of native approvals waiting for the signed-in actor (#639,
 * `GET /api/v1/work/approvals` and `GET /api/v1/work/approvals/{kind}/{approvalId}`).
 *
 * The pilot carries ONE native kind: Finance's official-invoice-import batches awaiting approval. The Work read is a
 * thin projection of Finance's own pending-approval read — Finance decides who may see a batch (its approve permission,
 * the company, and that the uploader is never shown their own batch) — so this decoder applies Finance's own rules to
 * every item by mapping it back onto the Finance batch shape. An item of another kind, another company or another
 * state is not part of this contract, and the whole answer is refused rather than partly shown.
 */

import {
  decodePendingBatch,
  decodePendingDetail,
  type OfficialInvoicePendingDetail,
} from '../finance/officialInvoiceImportApprovalContract'

export const FINANCE_OFFICIAL_INVOICE_IMPORT_KIND = 'finance.official-invoice-import.v1'

export const WORK_APPROVALS_PATH = '/work/approvals'
export const workApprovalPath = (kind: string, approvalId: string) =>
  `${WORK_APPROVALS_PATH}/${encodeURIComponent(kind)}/${encodeURIComponent(approvalId)}`

export interface ApprovalItem {
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

export interface ApprovalPage {
  readonly companyId: string
  readonly items: readonly ApprovalItem[]
  /** Opaque; null is the end of the queue. */
  readonly nextCursor: string | null
}

export interface ApprovalDetail {
  readonly item: ApprovalItem
  readonly preview: OfficialInvoicePendingDetail['preview']
}

const ITEM_KEYS = [
  'kind', 'approvalId', 'companyId', 'status', 'version', 'batchVersion', 'checksum', 'templateVersion', 'itemCount',
  'submittedBy', 'submittedAtUtc',
] as const

type Obj = Record<string, unknown>

function isObject(value: unknown): value is Obj {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function exact(value: Obj, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

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

export function decodeApprovalItem(value: unknown): ApprovalItem | null {
  if (!isObject(value) || !exact(value, ITEM_KEYS) || value.kind !== FINANCE_OFFICIAL_INVOICE_IMPORT_KIND) return null
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

export function decodeApprovalPage(value: unknown): ApprovalPage | null {
  if (!isObject(value) || !exact(value, ['companyId', 'items', 'nextCursor']) || !Array.isArray(value.items)) return null
  if (typeof value.companyId !== 'string' || value.companyId.length === 0) return null
  if (!(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) return null
  const items: ApprovalItem[] = []
  const ids = new Set<string>()
  for (const entry of value.items) {
    const item = decodeApprovalItem(entry)
    const id = item?.approvalId.toLowerCase()
    if (item === null || id === undefined || ids.has(id) || item.companyId.toLowerCase() !== value.companyId.toLowerCase()) return null
    ids.add(id)
    items.push(item)
  }
  return { companyId: value.companyId, items, nextCursor: value.nextCursor }
}

/** `expectedId`: the approval that was asked for — a detail of another one is not an answer. */
export function decodeApprovalDetail(value: unknown, expectedId: string): ApprovalDetail | null {
  if (!isObject(value) || !exact(value, ['item', 'preview']) || !isObject(value.item)) return null
  const item = decodeApprovalItem(value.item)
  if (item === null || item.approvalId.toLowerCase() !== expectedId.toLowerCase()) return null
  const finance = decodePendingDetail({ batch: asFinanceBatch(value.item), preview: value.preview })
  return finance.ok ? { item, preview: finance.value.preview } : null
}
