import { useMemo } from 'react'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useFinanceBinding } from './financePort'
import type {
  OfficialInvoiceApprovalAttemptContext,
  OfficialInvoiceApprovalClient,
} from './OfficialInvoiceImportApprovalsPage'
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
