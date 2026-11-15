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
  decodeDocumentLinkCandidates,
  decodeDocumentLinkWrite,
  decodeDocumentList,
  decodeDocumentUpload,
  decodeDocumentVersionPage,
  decodeDocumentVersionWrite,
  linkCandidatesPath,
  linkPath,
  listPath,
  problemCode,
  versionContentPath,
  versionHistoryPath,
  versionsPath,
  VERSION_PAGE_SIZE,
  type DocumentBinding,
  type DocumentLinkCandidates,
  type DocumentLinkWrite,
  type DocumentList,
  type DocumentUpload,
  type DocumentVersionPage,
  type DocumentVersionWrite,
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

/**
 * The cache key of one owner record's document list. A screen that reads the same owner's list beside its
 * `DocumentsPanel` (S-11's document timeline) uses this key, so both share ONE request and an upload in the panel
 * refreshes both.
 */
export function documentsQueryKey(namespace: string, entryGeneration: number, ownerKind: OwnerKind, ownerId: string) {
  return [namespace, entryGeneration, 'documents', ownerKind, ownerId] as const
}

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

/* ── Immutable versions of one document (#677) ────────────────────────────── */

export type VersionHistoryOutcome =
  | { readonly type: 'ok'; readonly page: DocumentVersionPage }
  /** 403/404: the history does not exist for this actor in this company. */
  | { readonly type: 'gone'; readonly status: number }
  /** No answer, a server failure, or an answer that is not this document's page: the same request may be repeated. */
  | { readonly type: 'failed' }
  | { readonly type: 'aborted' }

/** One history page, newest first. `cursor` is null for the first page, else the previous page's `nextCursor`. */
export async function fetchDocumentVersions(
  binding: DocumentBinding,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<VersionHistoryOutcome> {
  const response = await send(
    versionHistoryPath(binding.documentId, VERSION_PAGE_SIZE, cursor),
    { headers: headers(binding.companyId) },
    signal,
  )
  if (response === 'aborted') return { type: 'aborted' }
  if (typeof response === 'string') return { type: 'failed' }
  const body = await jsonBody(response)
  if (signal?.aborted) return { type: 'aborted' }
  if (response.status === 403 || response.status === 404) return { type: 'gone', status: response.status }
  if (response.status !== 200) return { type: 'failed' }
  const page = decodeDocumentVersionPage(body, { ...binding, pageSize: VERSION_PAGE_SIZE, cursor })
  return page === null ? { type: 'failed' } : { type: 'ok', page }
}

/** One append attempt, frozen when the file was chosen: a retry sends exactly this, never the screen's later state. */
export interface VersionAttempt extends DocumentBinding {
  readonly idempotencyKey: string
  readonly expectedCurrentVersion: number
  readonly file: File
}

export type VersionAppendOutcome =
  | { readonly type: 'stored'; readonly write: DocumentVersionWrite; readonly replayed: boolean }
  /** A definite refusal: no version was created under this key. */
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  /** No answer, a 5xx, or a 201 that is not this attempt's: the version may exist; the same attempt is kept for a retry. */
  | { readonly type: 'unknown' }

export async function appendDocumentVersion(attempt: VersionAttempt): Promise<VersionAppendOutcome> {
  const form = new FormData()
  form.append('expectedCurrentVersion', String(attempt.expectedCurrentVersion))
  form.append('file', attempt.file, attempt.file.name)
  const response = await send(
    versionsPath(attempt.documentId),
    {
      method: 'POST',
      // No Content-Type: the browser writes the multipart boundary itself.
      headers: { ...headers(attempt.companyId), 'Idempotency-Key': attempt.idempotencyKey },
      body: form,
    },
    undefined,
  )
  if (typeof response === 'string') return { type: 'unknown' }
  const body = await jsonBody(response)
  if (response.status === CREATED_STATUS) {
    const write = decodeDocumentVersionWrite(body, {
      companyId: attempt.companyId,
      ownerKind: attempt.ownerKind,
      ownerId: attempt.ownerId,
      documentId: attempt.documentId,
      expectedCurrentVersion: attempt.expectedCurrentVersion,
      sizeBytes: attempt.file.size,
    })
    const replayed = response.headers.get(REPLAY_HEADER) === 'true'
    if (write === null || write.isReplay !== replayed) return { type: 'unknown' }
    return { type: 'stored', write, replayed }
  }
  if (response.status >= 500) return { type: 'unknown' }
  return { type: 'refused', status: response.status, code: problemCode(body) }
}

/** The chosen version's own bytes. An answer whose length is not that version's recorded size is not saved. */
export async function downloadDocumentVersion(
  binding: DocumentBinding,
  versionNo: number,
  sizeBytes: number,
  signal?: AbortSignal,
): Promise<DownloadOutcome> {
  const response = await send(versionContentPath(binding.documentId, versionNo), { headers: headers(binding.companyId) }, signal)
  if (typeof response === 'string') return { type: 'failed' }
  if (response.status !== 200) {
    const body = await jsonBody(response)
    return { type: 'refused', status: response.status, code: problemCode(body) }
  }
  try {
    const blob = await response.blob()
    return blob.size === sizeBytes ? { type: 'ok', blob } : { type: 'failed' }
  } catch {
    return { type: 'failed' }
  }
}

/* ── Existing documents linked to another record (KLN-D49/D50) ────────────── */

/** The chosen file's SHA-256 as the server writes it; null when the browser cannot compute it (no probe then). */
export async function sha256Hex(file: Blob): Promise<string | null> {
  try {
    if (typeof crypto === 'undefined' || crypto.subtle === undefined) return null
    const digest = await crypto.subtle.digest('SHA-256', new Uint8Array(await file.arrayBuffer()))
    return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('')
  } catch {
    return null
  }
}

export type LinkCandidatesOutcome =
  | { readonly type: 'ok'; readonly candidates: DocumentLinkCandidates }
  /** A definite answer that there is nothing to offer (403/404/422): the upload stays the only way. */
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  /** No answer, a server failure, or an answer that is not this probe's. */
  | { readonly type: 'failed' }
  | { readonly type: 'aborted' }

/** The target record's earlier documents with the chosen file's content (reads only; the server re-checks on link). */
export async function fetchLinkCandidates(
  asked: { readonly companyId: string; readonly ownerKind: OwnerKind; readonly ownerId: string; readonly sha256: string; readonly sizeBytes: number },
  signal?: AbortSignal,
): Promise<LinkCandidatesOutcome> {
  const response = await send(linkCandidatesPath(asked.ownerKind, asked.ownerId, asked.sha256), { headers: headers(asked.companyId) }, signal)
  if (response === 'aborted') return { type: 'aborted' }
  if (typeof response === 'string') return { type: 'failed' }
  const body = await jsonBody(response)
  if (signal?.aborted) return { type: 'aborted' }
  if (response.status === 200) {
    const candidates = decodeDocumentLinkCandidates(body, asked)
    return candidates === null ? { type: 'failed' } : { type: 'ok', candidates }
  }
  if (response.status >= 500) return { type: 'failed' }
  return { type: 'refused', status: response.status, code: problemCode(body) }
}

/** One link attempt, frozen when the user chose the candidate: a retry sends exactly this. */
export interface LinkAttempt extends DocumentBinding {
  readonly idempotencyKey: string
  readonly expectedCurrentVersion: number
}

export type LinkOutcome =
  | { readonly type: 'linked'; readonly write: DocumentLinkWrite; readonly replayed: boolean }
  /** A definite refusal: no link was created under this key. */
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  /** No answer, a 5xx, or a 201 that is not this attempt's: the link may exist; the same attempt is kept for a retry. */
  | { readonly type: 'unknown' }

export async function linkDocument(attempt: LinkAttempt): Promise<LinkOutcome> {
  const response = await send(
    linkPath(attempt.documentId),
    {
      method: 'POST',
      headers: { ...headers(attempt.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': attempt.idempotencyKey },
      body: JSON.stringify({
        ownerKind: attempt.ownerKind,
        ownerId: attempt.ownerId,
        expectedCurrentVersion: attempt.expectedCurrentVersion,
      }),
    },
    undefined,
  )
  if (typeof response === 'string') return { type: 'unknown' }
  const body = await jsonBody(response)
  if (response.status === CREATED_STATUS) {
    const write = decodeDocumentLinkWrite(body, attempt)
    const replayed = response.headers.get(REPLAY_HEADER) === 'true'
    if (write === null || write.isReplay !== replayed) return { type: 'unknown' }
    return { type: 'linked', write, replayed }
  }
  if (response.status >= 500) return { type: 'unknown' }
  return { type: 'refused', status: response.status, code: problemCode(body) }
}
