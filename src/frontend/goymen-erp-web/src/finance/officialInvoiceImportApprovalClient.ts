import { useMemo } from 'react'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useFinanceBinding } from './financePort'
import type {
  OfficialInvoiceApprovalAttemptContext,
  OfficialInvoiceApprovalClient,
} from './OfficialInvoiceImportApprovalsPage'
import type {
  OfficialInvoiceAppliedBatch,
  OfficialInvoiceApplyReply,
  OfficialInvoiceImportClient,
  OfficialInvoiceImportIssue,
  OfficialInvoiceImportPreview,
  OfficialInvoiceImportSourceSelection,
  OfficialInvoiceOpenItem,
} from './OfficialInvoiceImportPanel'
import {
  decodeApprovalReply,
  decodePendingDetail,
  decodePendingPage,
  type OfficialInvoiceApprovalReply,
  type OfficialInvoicePendingDetail,
  type OfficialInvoicePendingPage,
} from './officialInvoiceImportApprovalContract'
import { isNonNegativeDecimalText, isPositiveDecimalText } from './decimalText'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export const OFFICIAL_INVOICE_PENDING_PATH = '/finance/official-invoice-import-batches/pending'
export const officialInvoiceBatchPath = (batchId: string) =>
  `/finance/official-invoice-import-batches/${encodeURIComponent(batchId)}`
export const officialInvoiceApprovalPath = (batchId: string) => `${officialInvoiceBatchPath(batchId)}/approval`

export interface OfficialInvoiceApprovalTransport {
  readonly context: OfficialInvoiceApprovalAttemptContext
  readonly client: OfficialInvoiceApprovalClient
  readonly applicationClient: OfficialInvoiceImportClient
}

export function useOfficialInvoiceApprovalTransport(): OfficialInvoiceApprovalTransport | null {
  const bound = useFinanceBinding()
  const boundary = useAuthBoundary()
  const snapshot = boundary.capture()
  const actorId = bound?.binding.actorId ?? null
  const sessionEpoch = bound?.binding.sessionEpoch ?? null
  const companyId = bound?.binding.companyId ?? null
  const boundNamespace = bound?.namespace ?? null
  const context = useMemo<OfficialInvoiceApprovalAttemptContext | null>(() => {
    if (actorId === null || sessionEpoch === null || companyId === null
      || snapshot.actorId !== actorId || snapshot.companyId !== companyId || snapshot.sessionEpoch !== sessionEpoch
      || snapshot.namespace !== boundNamespace) return null
    return {
      actorId,
      sessionEpoch,
      companyId,
      committedContextRevision: snapshot.committedContextRevision,
      requestFenceGeneration: snapshot.requestFenceGeneration,
      capabilityGeneration: snapshot.capabilityGeneration,
      securityNamespace: snapshot.namespace,
    }
  }, [actorId, sessionEpoch, companyId, boundNamespace, snapshot.actorId, snapshot.sessionEpoch, snapshot.companyId,
    snapshot.committedContextRevision, snapshot.requestFenceGeneration, snapshot.capabilityGeneration, snapshot.namespace])
  return useMemo(() => context === null ? null : {
    context,
    client: createOfficialInvoiceApprovalClient(context, boundary),
    applicationClient: createOfficialInvoiceImportClient(context, boundary),
  }, [context, boundary])
}

export function useOfficialInvoiceApprovalClient(): OfficialInvoiceApprovalClient | null {
  return useOfficialInvoiceApprovalTransport()?.client ?? null
}

export function createOfficialInvoiceApprovalClient(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch = fetch,
): OfficialInvoiceApprovalClient {
  return {
    listPending: (cursor, signal) => readPage(context, boundary, fetcher, cursor, signal),
    getPending: (batchId, signal) => readDetail(context, boundary, fetcher, batchId, signal),
    approve: (batchId, expectedVersion, idempotencyKey, signal) =>
      approve(context, boundary, fetcher, batchId, expectedVersion, idempotencyKey, signal),
  }
}

export function createOfficialInvoiceImportClient(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch = fetch,
): OfficialInvoiceImportClient {
  return {
    preview: (source, file, signal) => previewImport(context, boundary, fetcher, source, file, signal),
    apply: (source, file, idempotencyKey, signal) =>
      applyImport(context, boundary, fetcher, source, file, idempotencyKey, signal),
    reopen: (batchId, signal) => reopenImport(context, boundary, fetcher, batchId, signal),
  }
}

async function previewImport(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch,
  source: OfficialInvoiceImportSourceSelection,
  file: File,
  signal: AbortSignal,
): Promise<OfficialInvoiceImportPreview> {
  const snapshot = capture(context, boundary)
  const body = JSON.stringify(await importBody(source, file))
  if (signal.aborted || !sameContext(context, boundary)) throw new Error('stale-finance-context')
  const response = await fetcher(`${API_BASE}/finance/official-invoice-imports/preview`, {
    method: 'POST',
    headers: { ...headers(context.companyId), 'Content-Type': 'application/json' },
    body,
    signal,
  })
  reportBoundary(response.status, snapshot, boundary)
  if (response.status !== 200) throw new Error(`official-invoice-preview-status-${response.status}`)
  const decoded = decodeImportPreview(await json(response), source, context.companyId)
  if (decoded === null || !sameContext(context, boundary)) throw new Error('official-invoice-preview-malformed')
  return decoded
}

async function applyImport(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch,
  source: OfficialInvoiceImportSourceSelection,
  file: File,
  idempotencyKey: string,
  signal: AbortSignal,
): Promise<OfficialInvoiceApplyReply> {
  let snapshot: ReturnType<AuthBoundary['capture']>
  try {
    snapshot = capture(context, boundary)
  } catch {
    return { kind: 'unknown' }
  }

  let response: Response
  try {
    const body = JSON.stringify(await importBody(source, file))
    if (signal.aborted || !sameContext(context, boundary)) return { kind: 'unknown' }
    response = await fetcher(`${API_BASE}/finance/official-invoice-imports/apply`, {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body,
      signal,
    })
  } catch {
    return { kind: 'unknown' }
  }
  reportBoundary(response.status, snapshot, boundary)
  if (response.status === 401 || response.status === 403 || response.status === 503
    || !sameContext(context, boundary)) return { kind: 'unknown' }
  if (response.status !== 200 && response.status !== 409 && response.status !== 422) return { kind: 'unknown' }

  let body: unknown
  try {
    body = await json(response)
  } catch {
    return { kind: 'unknown' }
  }
  if (!sameContext(context, boundary)) return { kind: 'unknown' }
  if (response.status === 409) return decodeApplyConflict(body, source, context.companyId)
    ? { kind: 'conflicted' }
    : { kind: 'unknown' }
  if (response.status === 422) {
    const preview = isObject(body) ? decodeImportPreview(body.preview, source, context.companyId) : null
    return preview === null ? { kind: 'unknown' } : { kind: 'refused', issues: preview.issues }
  }
  if (!isObject(body) || body.isSucceeded !== true || !isObject(body.approvedSource)
    || body.approvedSource.outcome !== 3
    || !isObject(body.admission) || body.admission.outcome !== 'Succeeded'
    || !isObject(body.freshRead) || body.freshRead.outcome !== 3) return { kind: 'unknown' }
  const preview = decodeImportPreview(body.preview, source, context.companyId)
  if (preview === null || preview.issues.length !== 0) return { kind: 'unknown' }
  const previewRows = isObject(body.preview) && body.preview.isValid === true
    ? decodeApprovedSource(body.preview.batch, source, context.companyId, preview.sourceSha256)
    : null
  const approvedRows = decodeApprovedSource(body.approvedSource.resource, source, context.companyId, preview.sourceSha256)
  if (previewRows === null || approvedRows === null || !sameSourceRows(previewRows, approvedRows)) return { kind: 'unknown' }
  const batch = decodeAppliedBatch(body.freshRead.resource, source.batchId, context.companyId, {
    batchVersion: source.batchVersion,
    batchChecksum: preview.sourceSha256,
    rows: approvedRows,
    requireOpen: true,
  })
  if (batch === null || !sameAdmissionIds(body.admission, batch.openItems.map((item) => item.id))) {
    return { kind: 'unknown' }
  }
  const replayed = response.headers.get('Idempotency-Replayed') === 'true'
  if (body.admission.isReplay !== replayed) return { kind: 'unknown' }
  return { kind: 'succeeded', replayed, batch }
}

async function reopenImport(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch,
  batchId: string,
  signal: AbortSignal,
): Promise<OfficialInvoiceAppliedBatch> {
  const snapshot = capture(context, boundary)
  const response = await fetcher(`${API_BASE}/finance/official-invoice-imports/${encodeURIComponent(batchId)}`, {
    method: 'GET', headers: headers(context.companyId), signal,
  })
  reportBoundary(response.status, snapshot, boundary)
  if (response.status !== 200) throw new Error(`official-invoice-reopen-status-${response.status}`)
  const decoded = decodeAppliedBatch(await json(response), batchId, context.companyId)
  if (decoded === null || !sameContext(context, boundary)) throw new Error('official-invoice-reopen-malformed')
  return decoded
}

async function importBody(source: OfficialInvoiceImportSourceSelection, file: File) {
  return {
    batchId: source.batchId,
    batchVersion: source.batchVersion,
    utf8Csv: bytesToBase64(new Uint8Array(await file.arrayBuffer())),
  }
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
  }
  return btoa(binary)
}

function decodeImportPreview(
  value: unknown,
  source: OfficialInvoiceImportSourceSelection,
  companyId: string,
): OfficialInvoiceImportPreview | null {
  if (!isObject(value) || typeof value.templateVersion !== 'string' || value.templateVersion.length === 0
    || typeof value.sourceSha256 !== 'string' || !SHA256.test(value.sourceSha256)
    || !Array.isArray(value.issues)) return null
  const issues = value.issues.map(decodeIssue)
  if (issues.some((issue) => issue === null)) return null
  let rowCount = 0
  if (value.batch !== null) {
    if (!isObject(value.batch) || value.batch.batchId !== source.batchId || value.batch.companyId !== companyId
      || value.batch.batchVersion !== source.batchVersion || value.batch.batchChecksum !== value.sourceSha256
      || !Array.isArray(value.batch.rows)) return null
    rowCount = value.batch.rows.length
  }
  return {
    templateVersion: value.templateVersion,
    sourceSha256: value.sourceSha256,
    batchId: source.batchId,
    batchVersion: source.batchVersion,
    rowCount,
    issues: issues as OfficialInvoiceImportIssue[],
  }
}

function decodeIssue(value: unknown): OfficialInvoiceImportIssue | null {
  return isObject(value) && typeof value.rowNumber === 'number' && Number.isInteger(value.rowNumber)
    && typeof value.field === 'string'
    && typeof value.code === 'string'
    ? { rowNumber: value.rowNumber as number, field: value.field, code: value.code }
    : null
}

interface AppliedExpectation {
  readonly batchVersion: string
  readonly batchChecksum: string
  readonly rows: readonly Record<string, unknown>[]
  readonly requireOpen: boolean
}

function decodeAppliedBatch(
  value: unknown,
  batchId: string,
  companyId: string,
  expected?: AppliedExpectation,
): OfficialInvoiceAppliedBatch | null {
  if (!isObject(value) || value.batchId !== batchId || value.companyId !== companyId
    || typeof value.batchVersion !== 'string' || value.batchVersion.length === 0
    || typeof value.batchChecksum !== 'string' || !SHA256.test(value.batchChecksum)
    || (expected !== undefined && (value.batchVersion !== expected.batchVersion
      || value.batchChecksum !== expected.batchChecksum))
    || !Array.isArray(value.openItems)) return null
  const openItems = value.openItems.map((item) => decodeOpenItem(
    item,
    companyId,
    batchId,
    value.batchVersion as string,
    value.batchChecksum as string,
    expected?.requireOpen ?? false,
  ))
  if (openItems.some((item) => item === null)) return null
  const decoded = openItems as OfficialInvoiceOpenItem[]
  if (new Set(decoded.map((item) => item.id.toLowerCase())).size !== decoded.length) return null
  if (expected !== undefined && !sameRows(expected.rows, decoded)) return null
  return {
    batchId,
    batchVersion: value.batchVersion,
    batchChecksum: value.batchChecksum,
    openItems: decoded,
  }
}

function decodeOpenItem(
  value: unknown,
  companyId: string,
  batchId: string,
  batchVersion: string,
  batchChecksum: string,
  requireOpen: boolean,
): OfficialInvoiceOpenItem | null {
  const guidFields = ['id', 'companyId', 'sourceDocumentId', 'sourceInstallmentId', 'partyId',
    'companyAccountId', 'currencyId'] as const
  if (!isObject(value) || !guidFields.every((field) => canonicalGuid(value[field]))
    || value.companyId !== companyId || value.sourceKind !== 'official.invoice-installment.v1'
    || typeof value.sourceVersion !== 'string' || value.sourceVersion.length === 0
    || value.sourceBatchId !== batchId || value.sourceBatchVersion !== batchVersion
    || value.sourceBatchChecksum !== batchChecksum
    || !isPositiveDecimalText(value.originalAmount) || !isNonNegativeDecimalText(value.outstandingAmount)
    || !(value.dueOn === null || (typeof value.dueOn === 'string' && ISO_DATE.test(value.dueOn)))
    || (value.status !== 1 && value.status !== 2) || (requireOpen && value.status !== 1)
    || typeof value.version !== 'string' || !POSITIVE_INTEGER.test(value.version)) return null
  return {
    id: value.id as string,
    companyId: value.companyId as string,
    sourceKind: value.sourceKind,
    sourceDocumentId: value.sourceDocumentId as string,
    sourceInstallmentId: value.sourceInstallmentId as string,
    sourceVersion: value.sourceVersion,
    sourceBatchId: value.sourceBatchId,
    sourceBatchVersion: value.sourceBatchVersion,
    sourceBatchChecksum: value.sourceBatchChecksum,
    partyId: value.partyId as string,
    companyAccountId: value.companyAccountId as string,
    currencyId: value.currencyId as string,
    originalAmount: value.originalAmount,
    outstandingAmount: value.outstandingAmount,
    dueOn: value.dueOn as string | null,
    status: value.status,
    version: value.version,
  }
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'
const SHA256 = /^[0-9a-f]{64}$/
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/
const POSITIVE_INTEGER = /^[1-9]\d*$/

function canonicalGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== EMPTY_GUID
}

function decodeApprovedSource(
  value: unknown,
  source: OfficialInvoiceImportSourceSelection,
  companyId: string,
  checksum: string,
): readonly Record<string, unknown>[] | null {
  if (!isObject(value) || value.batchId !== source.batchId || value.companyId !== companyId
    || value.batchVersion !== source.batchVersion || value.batchChecksum !== checksum
    || !Array.isArray(value.rows) || value.rows.length === 0) return null
  const rows = value.rows.filter((row): row is Record<string, unknown> => isObject(row))
  if (rows.length !== value.rows.length || rows.some((row) => !canonicalGuid(row.openItemId)
    || !canonicalGuid(row.sourceDocumentId) || !canonicalGuid(row.sourceInstallmentId)
    || typeof row.sourceVersion !== 'string' || row.sourceVersion.length === 0
    || row.documentKind !== 'official-invoice-installment'
    || !canonicalGuid(row.partyId) || !canonicalGuid(row.companyAccountId) || !canonicalGuid(row.currencyId)
    || !isPositiveDecimalText(row.amount)
    || !(row.dueOn === null || (typeof row.dueOn === 'string' && ISO_DATE.test(row.dueOn))))) return null
  return new Set(rows.map((row) => (row.openItemId as string).toLowerCase())).size === rows.length ? rows : null
}

function sameRows(rows: readonly Record<string, unknown>[], items: readonly OfficialInvoiceOpenItem[]): boolean {
  if (rows.length !== items.length) return false
  const byId = new Map(items.map((item) => [item.id.toLowerCase(), item]))
  return rows.every((row) => {
    const item = byId.get((row.openItemId as string).toLowerCase())
    return item !== undefined && item.sourceDocumentId === row.sourceDocumentId
      && item.sourceInstallmentId === row.sourceInstallmentId && item.sourceVersion === row.sourceVersion
      && item.partyId === row.partyId && item.companyAccountId === row.companyAccountId
      && item.currencyId === row.currencyId && item.originalAmount === row.amount
      && item.outstandingAmount === row.amount && item.dueOn === row.dueOn
  })
}

function sameSourceRows(left: readonly Record<string, unknown>[], right: readonly Record<string, unknown>[]): boolean {
  if (left.length !== right.length) return false
  const fields = ['sourceDocumentId', 'sourceInstallmentId', 'sourceVersion', 'documentKind', 'partyId',
    'companyAccountId', 'currencyId', 'amount', 'dueOn'] as const
  const byId = new Map(right.map((row) => [(row.openItemId as string).toLowerCase(), row]))
  return left.every((row) => {
    const other = byId.get((row.openItemId as string).toLowerCase())
    return other !== undefined && fields.every((field) => row[field] === other[field])
  })
}

function sameAdmissionIds(value: Record<string, unknown>, ids: readonly string[]): boolean {
  if (!Array.isArray(value.openItemIds) || typeof value.isReplay !== 'boolean'
    || typeof value.originalCorrelationId !== 'string' || value.originalCorrelationId.length === 0
    || !Array.isArray(value.issues) || value.issues.length !== 0
    || value.openItemIds.some((id) => !canonicalGuid(id))) return false
  const admitted = (value.openItemIds as string[]).map((id) => id.toLowerCase())
  return new Set(admitted).size === admitted.length
    && admitted.length === ids.length
    && admitted.every((id) => ids.some((fresh) => fresh.toLowerCase() === id))
}

function decodeApplyConflict(
  value: unknown,
  source: OfficialInvoiceImportSourceSelection,
  companyId: string,
): boolean {
  if (!isObject(value) || value.isSucceeded !== false || value.freshRead !== null
    || !isObject(value.admission)
    || (value.admission.outcome !== 'VersionConflict' && value.admission.outcome !== 'IdempotencyConflict')
    || !Array.isArray(value.admission.openItemIds) || value.admission.openItemIds.length !== 0
    || value.admission.isReplay !== false || typeof value.admission.originalCorrelationId !== 'string'
    || !Array.isArray(value.admission.issues) || !isObject(value.approvedSource)
    || value.approvedSource.outcome !== 3 || !isObject(value.preview)) return false
  const preview = decodeImportPreview(value.preview, source, companyId)
  if (preview === null || preview.issues.length !== 0 || value.preview.isValid !== true) return false
  const previewRows = decodeApprovedSource(value.preview.batch, source, companyId, preview.sourceSha256)
  const approvedRows = decodeApprovedSource(value.approvedSource.resource, source, companyId, preview.sourceSha256)
  return previewRows !== null && approvedRows !== null && sameSourceRows(previewRows, approvedRows)
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

async function readPage(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch,
  cursor: string | null,
  signal: AbortSignal,
): Promise<OfficialInvoicePendingPage> {
  const snapshot = capture(context, boundary)
  const path = cursor === null
    ? OFFICIAL_INVOICE_PENDING_PATH
    : `${OFFICIAL_INVOICE_PENDING_PATH}?cursor=${encodeURIComponent(cursor)}`
  const response = await fetcher(`${API_BASE}${path}`, {
    method: 'GET', headers: headers(context.companyId), signal,
  })
  reportBoundary(response.status, snapshot, boundary)
  if (response.status !== 200) throw new Error(`pending-list-status-${response.status}`)
  const decoded = decodePendingPage(await json(response))
  if (!decoded.ok || decoded.value.companyId !== context.companyId || !sameContext(context, boundary)) {
    throw new Error('pending-list-malformed')
  }
  return decoded.value
}

async function readDetail(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch,
  batchId: string,
  signal: AbortSignal,
): Promise<OfficialInvoicePendingDetail> {
  const snapshot = capture(context, boundary)
  const response = await fetcher(`${API_BASE}${officialInvoiceBatchPath(batchId)}`, {
    method: 'GET', headers: headers(context.companyId), signal,
  })
  reportBoundary(response.status, snapshot, boundary)
  if (response.status !== 200) throw new Error(`pending-detail-status-${response.status}`)
  const decoded = decodePendingDetail(await json(response))
  if (!decoded.ok || decoded.value.batch.batchId !== batchId
    || decoded.value.batch.companyId !== context.companyId || !sameContext(context, boundary)) {
    throw new Error('pending-detail-malformed')
  }
  return decoded.value
}

async function approve(
  context: OfficialInvoiceApprovalAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch,
  batchId: string,
  expectedVersion: string,
  idempotencyKey: string,
  signal: AbortSignal,
): Promise<OfficialInvoiceApprovalReply> {
  let snapshot: ReturnType<AuthBoundary['capture']>
  try {
    snapshot = capture(context, boundary)
  } catch {
    return { kind: 'unknown' }
  }

  let response: Response
  try {
    response = await fetcher(`${API_BASE}${officialInvoiceApprovalPath(batchId)}`, {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify({ batchId, expectedVersion }),
      signal,
    })
  } catch {
    return { kind: 'unknown' }
  }
  reportBoundary(response.status, snapshot, boundary)
  if (response.status === 401 || response.status === 403) return { kind: 'unknown' }
  if (response.status === 503 || !sameContext(context, boundary)) return { kind: 'unknown' }
  if (response.status !== 200 && response.status !== 409 && response.status !== 422) return { kind: 'unknown' }

  let body: unknown
  try {
    body = await json(response)
  } catch {
    return { kind: 'unknown' }
  }
  if (!sameContext(context, boundary)) return { kind: 'unknown' }
  const decoded = decodeApprovalReply(body)
  if (!decoded.ok) return { kind: 'unknown' }
  const reply = decoded.value
  if (response.status === 200 && reply.kind === 'succeeded') {
    const replayed = response.headers.get('Idempotency-Replayed') === 'true'
    return reply.replayed === replayed ? reply : { kind: 'unknown' }
  }
  if (response.status === 409 && reply.kind === 'conflicted') return reply
  if (response.status === 422 && reply.kind === 'refused') return reply
  return { kind: 'unknown' }
}

function capture(context: OfficialInvoiceApprovalAttemptContext, boundary: AuthBoundary) {
  const snapshot = boundary.capture()
  if (snapshot.sessionEpoch !== context.sessionEpoch || snapshot.companyId !== context.companyId
    || snapshot.actorId !== context.actorId
    || snapshot.committedContextRevision !== context.committedContextRevision
    || snapshot.requestFenceGeneration !== context.requestFenceGeneration
    || snapshot.capabilityGeneration !== context.capabilityGeneration
    || snapshot.namespace !== context.securityNamespace) throw new Error('stale-finance-context')
  return snapshot
}

function sameContext(context: OfficialInvoiceApprovalAttemptContext, boundary: AuthBoundary): boolean {
  try {
    capture(context, boundary)
    return true
  } catch {
    return false
  }
}

function reportBoundary(status: number, snapshot: ReturnType<AuthBoundary['capture']>, boundary: AuthBoundary) {
  if (status === 401 || status === 403) boundary.report(status, snapshot)
}

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

async function json(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new Error('malformed-json')
  }
}
