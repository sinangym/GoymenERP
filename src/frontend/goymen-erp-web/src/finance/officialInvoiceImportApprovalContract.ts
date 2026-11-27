import { isNonNegativeDecimalText, isPositiveDecimalText } from './decimalText'
import { decodeOfficialInvoiceTaxWarning, type OfficialInvoiceTaxWarning } from './officialSalesInvoiceApi'

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
    /** FNS-D64: non-blocking warnings; the key is absent (or null) when the server found none. */
    readonly taxWarnings?: readonly OfficialInvoiceTaxWarning[] | null
  }
}

export interface OfficialInvoiceApprovedBatch extends Omit<OfficialInvoicePendingBatch,
  'status' | 'version' | 'approvedBy' | 'approvedAtUtc'> {
  readonly status: 'Approved'
  readonly version: '2'
  readonly approvedBy: string
  readonly approvedAtUtc: string
}

export interface OfficialInvoiceDecisionDetail extends Omit<OfficialInvoicePendingDetail, 'batch'> {
  readonly batch: OfficialInvoicePendingBatch | OfficialInvoiceApprovedBatch
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

function decodeRow(value: unknown, decision = false): DecodeResult<OfficialInvoicePendingRow> {
  const keys = ['openItemId', 'sourceDocumentId', 'sourceInstallmentId', 'sourceVersion', 'documentKind', 'partyId',
    'companyAccountId', 'currencyId', 'amount', 'dueOn'] as const
  if (!object(value) || !exact(value, keys)
    || !['openItemId', 'sourceDocumentId', 'sourceInstallmentId', 'partyId', 'companyAccountId', 'currencyId']
      .every((key) => canonicalGuid(value[key]))
    || typeof value.sourceVersion !== 'string' || value.sourceVersion.length === 0
    || value.documentKind !== 'official-invoice-installment'
    || typeof value.amount !== 'string' || (decision ? value.amount !== value.amount.trim()
      || !nativePositiveAmount(value.amount) : !decimal.test(value.amount))
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

function decodeDetail(value: unknown, decision: boolean): DecodeResult<OfficialInvoiceDecisionDetail> {
  if (!object(value) || !exact(value, ['batch', 'preview'])) return { ok: false }
  const summary = decision ? decodeDecisionBatch(value.batch) : decodePendingBatch(value.batch)
  if (!summary.ok || !object(value.preview)
    || !exact(value.preview, ['templateVersion', 'sourceSha256', 'batch', 'issues', 'isValid',
      ...('taxWarnings' in value.preview ? ['taxWarnings'] : [])])
    || value.preview.taxWarnings != null && (!Array.isArray(value.preview.taxWarnings)
      || value.preview.taxWarnings.some((warning) => decodeOfficialInvoiceTaxWarning(warning) === null))
    || value.preview.templateVersion !== summary.value.templateVersion
    || value.preview.sourceSha256 !== summary.value.batchChecksum || value.preview.isValid !== true
    || !Array.isArray(value.preview.issues)
    || value.preview.issues.length !== 0 || !object(value.preview.batch)
    || !exact(value.preview.batch, ['batchId', 'companyId', 'batchVersion', 'batchChecksum', 'rows'])
    || value.preview.batch.batchId !== summary.value.batchId || value.preview.batch.companyId !== summary.value.companyId
    || value.preview.batch.batchVersion !== summary.value.batchVersion
    || value.preview.batch.batchChecksum !== summary.value.batchChecksum || !Array.isArray(value.preview.batch.rows)) return { ok: false }
  const rows = value.preview.batch.rows.map(row => decodeRow(row, decision))
  if (rows.length !== summary.value.rowCount || rows.some((row) => !row.ok)) return { ok: false }
  if (decision) {
    const itemIds = new Set<string>(), identities = new Set<string>()
    for (const decoded of rows) {
      if (!decoded.ok) return { ok: false }
      const row = decoded.value
      const ids = [row.openItemId,row.sourceDocumentId,row.sourceInstallmentId,row.partyId,row.companyAccountId,row.currencyId]
      const identity = `${row.sourceDocumentId.toLowerCase()}:${row.sourceInstallmentId.toLowerCase()}`
      if (ids.some(id => id !== id.trim()) || !nativeToken(row.sourceVersion)
        || itemIds.has(row.openItemId.toLowerCase()) || identities.has(identity)
        || row.dueOn !== null && !realDate(row.dueOn)) return { ok: false }
      itemIds.add(row.openItemId.toLowerCase()); identities.add(identity)
    }
    if (value.preview.taxWarnings != null && (summary.value.templateVersion === 'finance-official-invoice-installment-csv.v1'
      || (value.preview.taxWarnings as readonly OfficialInvoiceTaxWarning[]).some(warning => warning.sourceDocumentId === null
        || !rows.some(row => row.ok && row.value.sourceDocumentId === warning.sourceDocumentId)
        || warning.code !== 'finance_official_invoice_tax_inconsistent'
        || !nativeNonNegativeAmount(warning.expectedPayableFunctionalAmount.startsWith('-') ? warning.expectedPayableFunctionalAmount.slice(1) : warning.expectedPayableFunctionalAmount)
        || !nativeNonNegativeAmount(warning.payableFunctionalAmount) || !nativePositiveAmount(warning.differenceFunctionalAmount)))) return { ok: false }
  }
  return { ok: true, value: value as unknown as OfficialInvoiceDecisionDetail }
}

// Preserve the existing pending-only API/legacy screen contract; the new purpose alone accepts terminal detail.
export function decodePendingDetail(value: unknown): DecodeResult<OfficialInvoicePendingDetail> {
  return decodeDetail(value, false) as DecodeResult<OfficialInvoicePendingDetail>
}
export function decodeDecisionDetail(value: unknown): DecodeResult<OfficialInvoiceDecisionDetail> {
  return decodeDetail(value, true)
}

function decodeDecisionBatch(value: unknown): DecodeResult<OfficialInvoicePendingBatch | OfficialInvoiceApprovedBatch> {
  if (!object(value)) return { ok: false }
  const terminal = value.status === 'Approved'
  const base = decodePendingBatch(terminal ? { ...value, status: 'Pending', version: '1', approvedBy: null, approvedAtUtc: null } : value)
  if (!base.ok || !nativeToken(base.value.batchVersion) || base.value.batchId !== base.value.batchId.trim()
    || base.value.companyId !== base.value.companyId.trim() || base.value.batchChecksum.length !== 64 || base.value.rowCount > 10_000
    || base.value.uploadedBy !== base.value.uploadedBy.trim() || base.value.uploadedBy.length > 128 || !nativeUtc(base.value.uploadedAtUtc)
    || !['finance-official-invoice-installment-csv.v1','finance-official-invoice-installment-csv.v2','finance-official-invoice-installment-csv.v3'].includes(base.value.templateVersion)
    || terminal && (value.version !== '2' || typeof value.approvedBy !== 'string' || value.approvedBy.trim() !== value.approvedBy
      || value.approvedBy.length === 0 || value.approvedBy.length > 128 || value.approvedBy === value.uploadedBy || !nativeUtc(value.approvedAtUtc)
      || nativeUtcKey(value.approvedAtUtc) < nativeUtcKey(base.value.uploadedAtUtc))) return { ok: false }
  return { ok: true, value: value as unknown as OfficialInvoicePendingBatch | OfficialInvoiceApprovedBatch }
}
// FinanceExactDecimalText uses exact digit text/BigInteger, not CLR decimal or a UI amount calculation.
function nativeNonNegativeAmount(value: string): boolean {
  return value === value.trim() && isNonNegativeDecimalText(value)
}
function nativePositiveAmount(value: string): boolean {
  return value === value.trim() && isPositiveDecimalText(value)
}
function nativeToken(value: string): boolean {
  return value.length > 0 && value.length <= 128 && value === value.trim() && /^[a-zA-Z0-9._:/-]+$/.test(value)
}
function nativeUtc(value: unknown): value is string {
  if (typeof value !== 'string' || value !== value.trim()) return false
  const parts = /^(\d{4}-\d{2}-\d{2})T([0-2]\d):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(value)
  if (parts === null || parts[0] !== value || !realDate(parts[1]!) || parts[2]! > '23') return false
  // DateTimeOffset default is not a native recorded instant. Preserve fractional ticks verbatim.
  return !(parts[1] === '0001-01-01' && parts[2] === '00' && parts[3] === '00' && parts[4] === '00'
    && /^0*$/.test(parts[5] ?? ''))
}
function nativeUtcKey(value: string): string {
  // Both inputs already passed nativeUtc; comparing seven fractional digits preserves .NET tick ordering.
  const fraction = value.slice(19).replace(/(?:Z|\+00:00)$/, '').replace(/^\./, '')
  return value.slice(0, 19) + '.' + fraction.padEnd(7, '0')
}
function realDate(value: string): boolean {
  const parsed = new Date(`${value}T00:00:00Z`)
  return !value.startsWith('0000-') && Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value
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
