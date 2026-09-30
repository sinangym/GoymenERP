/**
 * Document vault transport (AT-065). It invents no field and carries no rule: the server decides authority, the owner
 * record's read, the allowed kinds, the scan and the duplicate warning. `X-Company-Id` is the committed shell company —
 * request context, never a form member. The upload's `Idempotency-Key` is the caller's, kept for the same file until the
 * outcome is known, so a retry after a lost answer can never store the document twice.
 */

import {
  CREATED_STATUS,
  DOCUMENTS_PATH,
  REPLAY_HEADER,
  contentPath,
  decodeDocumentList,
  decodeDocumentUpload,
  listPath,
  problemCode,
  type DocumentList,
  type DocumentUpload,
  type OwnerKind,
} from './contract'
import type { ViewOutcome } from '../shell/uiState'

const API_BASE_PATH = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

type Failure = 'aborted' | 'offline' | 'unreachable'

async function send(path: string, init: RequestInit, signal: AbortSignal | undefined): Promise<Response | Failure> {
  try {
    return await fetch(`${API_BASE_PATH}${path}`, { ...init, signal })
  } catch {
    if (signal?.aborted) return 'aborted'
    if (!navigator.onLine) return 'offline'
    return 'unreachable'
  }
}

async function jsonBody(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    return null
  }
}

/* ── The owner record's documents ─────────────────────────────────────────── */

export async function fetchDocuments(
  ownerKind: OwnerKind,
  ownerId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DocumentList>> {
  const response = await send(listPath(ownerKind, ownerId), { headers: headers(companyId) }, signal)
  if (response === 'aborted') return { type: 'fence-stale' }
  if (response === 'offline') return { type: 'offline' }
  if (response === 'unreachable') return { type: 'network-error' }
  const body = await jsonBody(response)
  if (signal?.aborted) return { type: 'fence-stale' }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const list = decodeDocumentList(body, companyId, ownerKind, ownerId)
  return list === null ? { type: 'malformed' } : { type: 'ok-single', data: list }
}

/* ── Upload ───────────────────────────────────────────────────────────────── */

export interface UploadAttempt {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
  readonly ownerKind: OwnerKind
  readonly ownerId: string
  readonly file: File
}

export type UploadOutcome =
  | { readonly type: 'stored'; readonly upload: DocumentUpload; readonly replayed: boolean }
  /** A definite refusal: nothing was stored under this key. */
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  /** No answer, or an answer that is not this attempt's: the document may exist; the same key is kept for a retry. */
  | { readonly type: 'unknown' }
  | { readonly type: 'aborted' }

export async function uploadDocument(attempt: UploadAttempt, signal?: AbortSignal): Promise<UploadOutcome> {
  const form = new FormData()
  form.append('ownerKind', attempt.ownerKind)
  form.append('ownerId', attempt.ownerId)
  form.append('file', attempt.file, attempt.file.name)
  const response = await send(
    DOCUMENTS_PATH,
    {
      method: 'POST',
      // No Content-Type: the browser writes the multipart boundary itself.
      headers: { ...headers(attempt.companyId), 'Idempotency-Key': attempt.idempotencyKey },
      body: form,
    },
    signal,
  )
  if (response === 'aborted') return { type: 'aborted' }
  if (typeof response === 'string') return { type: 'unknown' }
  const body = await jsonBody(response)
  if (response.status === CREATED_STATUS) {
    const upload = decodeDocumentUpload(body, {
      companyId: attempt.companyId,
      ownerKind: attempt.ownerKind,
      ownerId: attempt.ownerId,
      sizeBytes: attempt.file.size,
    })
    const replayed = response.headers.get(REPLAY_HEADER) === 'true'
    if (upload === null || upload.isReplay !== replayed) return { type: 'unknown' }
    return { type: 'stored', upload, replayed }
  }
  // 5xx: the server may or may not have stored it. Everything else is a definite answer about this attempt.
  if (response.status >= 500) return { type: 'unknown' }
  return {
    type: 'refused',
    status: response.status,
    code: problemCode(body),
  }
}

/* ── Download ─────────────────────────────────────────────────────────────── */

export type DownloadOutcome =
  | { readonly type: 'ok'; readonly blob: Blob }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  | { readonly type: 'failed' }

export async function downloadDocument(documentId: string, companyId: string, signal?: AbortSignal): Promise<DownloadOutcome> {
  const response = await send(contentPath(documentId), { headers: headers(companyId) }, signal)
  if (typeof response === 'string') return { type: 'failed' }
  if (response.status !== 200) {
    const body = await jsonBody(response)
    return {
      type: 'refused',
      status: response.status,
      code: problemCode(body),
    }
  }
  try {
    return { type: 'ok', blob: await response.blob() }
  } catch {
    return { type: 'failed' }
  }
}
