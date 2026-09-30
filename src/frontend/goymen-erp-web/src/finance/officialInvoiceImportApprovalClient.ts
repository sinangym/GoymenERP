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
  const response = await fetcher(`${API_BASE}/finance/official-invoice-imports/preview`, {
    method: 'POST',
    headers: { ...headers(context.companyId), 'Content-Type': 'application/json' },
    body: JSON.stringify(await importBody(source, file)),
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
    response = await fetcher(`${API_BASE}/finance/official-invoice-imports/apply`, {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(await importBody(source, file)),
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
  if (response.status === 409) return isObject(body) ? { kind: 'conflicted' } : { kind: 'unknown' }
  if (response.status === 422) {
    const preview = isObject(body) ? decodeImportPreview(body.preview, source, context.companyId) : null
    return preview === null ? { kind: 'unknown' } : { kind: 'refused', issues: preview.issues }
  }
  if (!isObject(body) || body.isSucceeded !== true || !isObject(body.approvedSource)
    || body.approvedSource.outcome !== 3 || !isObject(body.approvedSource.resource)
    || body.approvedSource.resource.batchId !== source.batchId
    || body.approvedSource.resource.companyId !== context.companyId
    || body.approvedSource.resource.batchVersion !== source.batchVersion
    || !isObject(body.admission) || body.admission.outcome !== 'Succeeded'
    || !isObject(body.freshRead) || body.freshRead.outcome !== 3) return { kind: 'unknown' }
  const batch = decodeAppliedBatch(body.freshRead.resource, source.batchId, context.companyId)
  if (batch === null || batch.batchVersion !== source.batchVersion) return { kind: 'unknown' }
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
  if (!isObject(value) || typeof value.templateVersion !== 'string' || typeof value.sourceSha256 !== 'string'
    || !Array.isArray(value.issues)) return null
  const issues = value.issues.map(decodeIssue)
  if (issues.some((issue) => issue === null)) return null
  let rowCount = 0
  if (value.batch !== null) {
    if (!isObject(value.batch) || value.batch.batchId !== source.batchId || value.batch.companyId !== companyId
      || value.batch.batchVersion !== source.batchVersion || !Array.isArray(value.batch.rows)) return null
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

function decodeAppliedBatch(value: unknown, batchId: string, companyId: string): OfficialInvoiceAppliedBatch | null {
  if (!isObject(value) || value.batchId !== batchId || value.companyId !== companyId
    || typeof value.batchVersion !== 'string' || typeof value.batchChecksum !== 'string'
    || !Array.isArray(value.openItems)) return null
  const openItems = value.openItems.map(decodeOpenItem)
  if (openItems.some((item) => item === null)) return null
  return {
    batchId,
    batchVersion: value.batchVersion,
    batchChecksum: value.batchChecksum,
    openItems: openItems as OfficialInvoiceOpenItem[],
  }
}

function decodeOpenItem(value: unknown): OfficialInvoiceOpenItem | null {
  if (!isObject(value) || typeof value.id !== 'string' || typeof value.sourceDocumentId !== 'string'
    || typeof value.sourceInstallmentId !== 'string' || typeof value.partyId !== 'string'
    || typeof value.companyAccountId !== 'string' || typeof value.currencyId !== 'string'
    || typeof value.originalAmount !== 'string' || typeof value.outstandingAmount !== 'string'
    || (value.dueOn !== null && typeof value.dueOn !== 'string')
    || (typeof value.status !== 'number' && typeof value.status !== 'string')
    || typeof value.version !== 'string') return null
  const status = typeof value.status === 'number' ? value.status : Number(value.status)
  if (!Number.isInteger(status)) return null
  return {
    id: value.id,
    sourceDocumentId: value.sourceDocumentId,
    sourceInstallmentId: value.sourceInstallmentId,
    partyId: value.partyId,
    companyAccountId: value.companyAccountId,
    currencyId: value.currencyId,
    originalAmount: value.originalAmount,
    outstandingAmount: value.outstandingAmount,
    dueOn: value.dueOn as string | null,
    status,
    version: value.version,
  }
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
