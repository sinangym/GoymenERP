import { useMemo } from 'react'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useFinanceBinding } from './financePort'
import { decodeUploadReply, type OfficialInvoicePendingBatch, type OfficialInvoiceUploadReply } from './officialInvoiceImportApprovalContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export interface OfficialInvoiceUploadAttemptContext {
  readonly sessionEpoch: number
  readonly actorId: string
  readonly companyId: string
  readonly committedContextRevision: number
  readonly requestFenceGeneration: number
  readonly capabilityGeneration: string
  readonly securityNamespace: string
}

export interface OfficialInvoiceUploadClient {
  upload(
    batchId: string,
    batchVersion: string,
    file: File,
    idempotencyKey: string,
    signal: AbortSignal,
  ): Promise<OfficialInvoiceUploadReply>
}

export interface OfficialInvoiceUploadTransport {
  readonly context: OfficialInvoiceUploadAttemptContext
  readonly client: OfficialInvoiceUploadClient
}

export function useOfficialInvoiceUploadTransport(): OfficialInvoiceUploadTransport | null {
  const bound = useFinanceBinding()
  const boundary = useAuthBoundary()
  const snapshot = boundary.capture()
  const actorId = bound?.binding.actorId ?? null
  const sessionEpoch = bound?.binding.sessionEpoch ?? null
  const companyId = bound?.binding.companyId ?? null
  const boundNamespace = bound?.namespace ?? null
  const context = useMemo<OfficialInvoiceUploadAttemptContext | null>(() => {
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
    client: createOfficialInvoiceUploadClient(context, boundary),
  }, [context, boundary])
}

export function createOfficialInvoiceUploadClient(
  context: OfficialInvoiceUploadAttemptContext,
  boundary: AuthBoundary,
  fetcher: typeof fetch = fetch,
): OfficialInvoiceUploadClient {
  return {
    async upload(batchId, batchVersion, file, idempotencyKey, signal) {
      let snapshot: ReturnType<AuthBoundary['capture']>
      try {
        snapshot = capture(context, boundary)
      } catch {
        return { kind: 'unknown' }
      }
      let response: Response
      try {
        const body = JSON.stringify({ batchId, batchVersion, utf8Csv: await fileBase64(file) })
        if (signal.aborted || !sameContext(context, boundary)) return { kind: 'unknown' }
        response = await fetcher(`${API_BASE}/finance/official-invoice-import-batches`, {
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
        body = await response.json()
      } catch {
        return { kind: 'unknown' }
      }
      if (!sameContext(context, boundary)) return { kind: 'unknown' }
      const decoded = decodeUploadReply(body)
      if (!decoded.ok) return { kind: 'unknown' }
      const reply = decoded.value
      if (response.status === 200 && reply.kind === 'succeeded') {
        const replayed = response.headers.get('Idempotency-Replayed') === 'true'
        return reply.replayed === replayed && samePending(reply.batch, batchId, batchVersion, context.companyId)
          ? reply
          : { kind: 'unknown' }
      }
      if (response.status === 409 && reply.kind === 'conflicted') return reply
      if (response.status === 422 && reply.kind === 'refused') return reply
      return { kind: 'unknown' }
    },
  }
}

function samePending(batch: OfficialInvoicePendingBatch, batchId: string, batchVersion: string, companyId: string) {
  return batch.batchId === batchId && batch.batchVersion === batchVersion && batch.companyId === companyId
}

async function fileBase64(file: File): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer())
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
  }
  return btoa(binary)
}

function capture(context: OfficialInvoiceUploadAttemptContext, boundary: AuthBoundary) {
  const snapshot = boundary.capture()
  if (snapshot.sessionEpoch !== context.sessionEpoch || snapshot.companyId !== context.companyId
    || snapshot.actorId !== context.actorId || snapshot.committedContextRevision !== context.committedContextRevision
    || snapshot.requestFenceGeneration !== context.requestFenceGeneration
    || snapshot.capabilityGeneration !== context.capabilityGeneration
    || snapshot.namespace !== context.securityNamespace) throw new Error('stale-finance-context')
  return snapshot
}

function sameContext(context: OfficialInvoiceUploadAttemptContext, boundary: AuthBoundary): boolean {
  try { capture(context, boundary); return true } catch { return false }
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
