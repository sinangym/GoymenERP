export type DecodeResult<T> = { readonly ok: true; readonly value: T } | { readonly ok: false }

export interface OfficialInvoicePendingBatch {
  readonly batchId: string
  readonly companyId: string
  readonly batchVersion: string
  readonly batchChecksum: string
  readonly templateVersion: string
  readonly status: 'Pending'
  readonly version: '1'
  readonly rowCount: number
  readonly uploadedBy: string
  readonly uploadedAtUtc: string
  readonly approvedBy: null
  readonly approvedAtUtc: null
}

export interface OfficialInvoicePendingRow {
  readonly openItemId: string
  readonly sourceDocumentId: string
  readonly sourceInstallmentId: string
  readonly sourceVersion: string
  readonly documentKind: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly amount: string
  readonly dueOn: string | null
}

export interface OfficialInvoicePendingPage {
  readonly companyId: string
  readonly items: readonly OfficialInvoicePendingBatch[]
  readonly nextCursor: string | null
}

export interface OfficialInvoicePendingDetail {
  readonly batch: OfficialInvoicePendingBatch
  readonly preview: {
    readonly templateVersion: string
    readonly sourceSha256: string
    readonly isValid: true
    readonly batch: {
      readonly batchId: string
      readonly companyId: string
      readonly batchVersion: string
      readonly batchChecksum: string
      readonly rows: readonly OfficialInvoicePendingRow[]
    }
    readonly issues: readonly never[]
  }
}

export interface OfficialInvoiceApprovedBatch extends Omit<OfficialInvoicePendingBatch,
  'status' | 'version' | 'approvedBy' | 'approvedAtUtc'> {
  readonly status: 'Approved'
  readonly version: '2'
  readonly approvedBy: string
  readonly approvedAtUtc: string
}

export type OfficialInvoiceApprovalReply =
  | { readonly kind: 'succeeded'; readonly replayed: boolean; readonly batch: OfficialInvoiceApprovedBatch }
  | { readonly kind: 'refused'; readonly issues: readonly { readonly field: string; readonly code: string }[] }
  | { readonly kind: 'conflicted' }
  | { readonly kind: 'unknown' }

export type OfficialInvoiceUploadReply =
  | { readonly kind: 'succeeded'; readonly replayed: boolean; readonly batch: OfficialInvoicePendingBatch }
  | { readonly kind: 'refused'; readonly issues: readonly { readonly field: string; readonly code: string }[] }
  | { readonly kind: 'conflicted' }
  | { readonly kind: 'unknown' }

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const emptyGuid = '00000000-0000-0000-0000-000000000000'
const sha256 = /^[0-9a-f]{64}$/
const date = /^\d{4}-\d{2}-\d{2}$/
const decimal = /^(0|[1-9]\d*)(\.\d+)?$/

function object(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function exact(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  return actual.length === keys.length && keys.slice().sort().every((key, index) => key === actual[index])
}

function timestamp(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && Number.isFinite(Date.parse(value))
}

function canonicalGuid(value: unknown): value is string {
  return typeof value === 'string' && guid.test(value) && value.toLowerCase() !== emptyGuid
}

export function decodePendingBatch(value: unknown): DecodeResult<OfficialInvoicePendingBatch> {
  const keys = ['batchId', 'companyId', 'batchVersion', 'batchChecksum', 'templateVersion', 'status', 'version',
    'rowCount', 'uploadedBy', 'uploadedAtUtc', 'approvedBy', 'approvedAtUtc'] as const
  if (!object(value) || !exact(value, keys) || !canonicalGuid(value.batchId)
    || !canonicalGuid(value.companyId)
    || typeof value.batchVersion !== 'string' || value.batchVersion.length === 0
    || typeof value.batchChecksum !== 'string' || !sha256.test(value.batchChecksum)
    || typeof value.templateVersion !== 'string' || value.templateVersion.length === 0
    || value.status !== 'Pending' || value.version !== '1'
    || !Number.isSafeInteger(value.rowCount) || (value.rowCount as number) < 1
    || typeof value.uploadedBy !== 'string' || value.uploadedBy.length === 0
    || !timestamp(value.uploadedAtUtc) || value.approvedBy !== null || value.approvedAtUtc !== null) return { ok: false }
  return { ok: true, value: value as unknown as OfficialInvoicePendingBatch }
}

function decodeRow(value: unknown): DecodeResult<OfficialInvoicePendingRow> {
  const keys = ['openItemId', 'sourceDocumentId', 'sourceInstallmentId', 'sourceVersion', 'documentKind', 'partyId',
    'companyAccountId', 'currencyId', 'amount', 'dueOn'] as const
  if (!object(value) || !exact(value, keys)
    || !['openItemId', 'sourceDocumentId', 'sourceInstallmentId', 'partyId', 'companyAccountId', 'currencyId']
      .every((key) => canonicalGuid(value[key]))
    || typeof value.sourceVersion !== 'string' || value.sourceVersion.length === 0
    || value.documentKind !== 'official-invoice-installment'
    || typeof value.amount !== 'string' || !decimal.test(value.amount)
    || !(value.dueOn === null || (typeof value.dueOn === 'string' && date.test(value.dueOn)))) return { ok: false }
  return { ok: true, value: value as unknown as OfficialInvoicePendingRow }
}

export function decodePendingPage(value: unknown): DecodeResult<OfficialInvoicePendingPage> {
  if (!object(value) || !exact(value, ['companyId', 'items', 'nextCursor'])
    || !canonicalGuid(value.companyId) || !Array.isArray(value.items)
    || !(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) return { ok: false }
  const items = value.items.map(decodePendingBatch)
  if (items.some((item) => !item.ok) || items.some((item) => item.ok && item.value.companyId !== value.companyId)) return { ok: false }
  return { ok: true, value: { companyId: value.companyId, items: items.map((item) => item.ok ? item.value : never()), nextCursor: value.nextCursor } }
}

export function decodePendingDetail(value: unknown): DecodeResult<OfficialInvoicePendingDetail> {
  if (!object(value) || !exact(value, ['batch', 'preview'])) return { ok: false }
  const summary = decodePendingBatch(value.batch)
  if (!summary.ok || !object(value.preview)
    || !exact(value.preview, ['templateVersion', 'sourceSha256', 'batch', 'issues', 'isValid'])
    || value.preview.templateVersion !== summary.value.templateVersion
    || value.preview.sourceSha256 !== summary.value.batchChecksum || value.preview.isValid !== true
    || !Array.isArray(value.preview.issues)
    || value.preview.issues.length !== 0 || !object(value.preview.batch)
    || !exact(value.preview.batch, ['batchId', 'companyId', 'batchVersion', 'batchChecksum', 'rows'])
    || value.preview.batch.batchId !== summary.value.batchId || value.preview.batch.companyId !== summary.value.companyId
    || value.preview.batch.batchVersion !== summary.value.batchVersion
    || value.preview.batch.batchChecksum !== summary.value.batchChecksum || !Array.isArray(value.preview.batch.rows)) return { ok: false }
  const rows = value.preview.batch.rows.map(decodeRow)
  if (rows.length !== summary.value.rowCount || rows.some((row) => !row.ok)) return { ok: false }
  return { ok: true, value: value as unknown as OfficialInvoicePendingDetail }
}

export function decodeApprovalReply(value: unknown): DecodeResult<OfficialInvoiceApprovalReply> {
  if (!object(value) || !exact(value, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])
    || typeof value.isReplay !== 'boolean' || typeof value.originalCorrelationId !== 'string' || !Array.isArray(value.issues)) return { ok: false }
  if (value.outcome === 'Succeeded') {
    if (!object(value.resource)) return { ok: false }
    const pendingShape = { ...value.resource, status: 'Pending', version: '1', approvedBy: null, approvedAtUtc: null }
    const base = decodePendingBatch(pendingShape)
    if (!base.ok || value.resource.status !== 'Approved' || value.resource.version !== '2'
      || typeof value.resource.approvedBy !== 'string' || value.resource.approvedBy.length === 0
      || !timestamp(value.resource.approvedAtUtc) || value.issues.length !== 0) return { ok: false }
    return { ok: true, value: { kind: 'succeeded', replayed: value.isReplay, batch: value.resource as unknown as OfficialInvoiceApprovedBatch } }
  }
  const issues = value.issues.filter((issue): issue is { field: string; code: string } =>
    object(issue) && exact(issue, ['field', 'code']) && typeof issue.field === 'string' && typeof issue.code === 'string')
  if (issues.length !== value.issues.length || value.resource !== null) return { ok: false }
  if (value.outcome === 'VersionConflict' || value.outcome === 'IdempotencyConflict') return { ok: true, value: { kind: 'conflicted' } }
  if (value.outcome === 'ValidationFailed') return { ok: true, value: { kind: 'refused', issues } }
  return { ok: false }
}

export function decodeUploadReply(value: unknown): DecodeResult<OfficialInvoiceUploadReply> {
  if (!object(value) || !exact(value, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])
    || typeof value.isReplay !== 'boolean' || typeof value.originalCorrelationId !== 'string'
    || value.originalCorrelationId.length === 0 || !Array.isArray(value.issues)) return { ok: false }
  if (value.outcome === 'Succeeded') {
    const pending = decodePendingBatch(value.resource)
    if (!pending.ok || value.issues.length !== 0) return { ok: false }
    return { ok: true, value: { kind: 'succeeded', replayed: value.isReplay, batch: pending.value } }
  }
  const issues = value.issues.filter((issue): issue is { field: string; code: string } =>
    object(issue) && exact(issue, ['field', 'code']) && typeof issue.field === 'string' && typeof issue.code === 'string')
  if (issues.length !== value.issues.length || value.resource !== null) return { ok: false }
  if (value.outcome === 'VersionConflict' || value.outcome === 'IdempotencyConflict') {
    return { ok: true, value: { kind: 'conflicted' } }
  }
  if (value.outcome === 'ValidationFailed') return { ok: true, value: { kind: 'refused', issues } }
  return { ok: false }
}

function never(): never { throw new Error('unreachable decoder branch') }
