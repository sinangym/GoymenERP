/**
 * The document vault's wire contract (ERP-000G core, AT-065 first owner: the Cari party), pinned to the backend routes:
 *
 *   GET  /api/v1/documents?ownerKind&ownerId   → DocumentList (newest first, canUpload, limits)
 *   POST /api/v1/documents  (multipart: ownerKind, ownerId, file; Idempotency-Key)
 *                                              → 201 DocumentUpload (isReplay=false)
 *                                              → 201 same body, isReplay=true + `Idempotency-Replayed`
 *   GET  /api/v1/documents/{id}/content        → the verified bytes, as an attachment
 *
 * The decoders are strict: a missing or mistyped member makes the whole answer `null` (malformed), never a partial
 * row. There is no delete, rename or overwrite anywhere in the contract: retention/deletion is undecided (OPEN-018/021)
 * and a document is never overwritten (DEC-DOC-003).
 */

export const DOCUMENTS_PATH = '/documents'
export const REPLAY_HEADER = 'Idempotency-Replayed'
export const CREATED_STATUS = 201

/** The owner-declared link kinds the client knows. Only the Cari party today. */
export const OWNER_KIND = {
  counterpartyParty: 'counterparty.party',
} as const
export type OwnerKind = (typeof OWNER_KIND)[keyof typeof OWNER_KIND]

/** The stable problem codes the screen explains; any other code is shown as a generic refusal. */
export const DOCUMENT_PROBLEM = {
  empty: 'document_empty',
  tooLarge: 'document_too_large',
  typeNotAllowed: 'document_type_not_allowed',
  contentMismatch: 'document_content_mismatch',
  contentRejected: 'document_content_rejected',
  notScanned: 'document_not_scanned',
  sameKeyPayloadMismatch: 'same_key_payload_mismatch',
  concurrentChange: 'concurrent_change',
  validationFailed: 'validation_failed',
} as const

export type ScanState = 'Clean' | 'NotScanned'

export interface DocumentRow {
  readonly id: string
  readonly companyId: string
  readonly ownerKind: string
  readonly ownerId: string
  readonly versionNo: number
  readonly fileName: string
  readonly contentType: string
  readonly sizeBytes: number
  readonly sha256: string
  readonly scanState: ScanState
  readonly downloadable: boolean
  readonly uploadedBy: string
  readonly uploadedAtUtc: string
}

export interface DocumentLimits {
  readonly maximumBytes: number
  readonly extensions: readonly string[]
  readonly contentTypes: readonly string[]
}

export interface DocumentList {
  readonly items: readonly DocumentRow[]
  readonly canUpload: boolean
  readonly limits: DocumentLimits
}

export interface DocumentDuplicate {
  readonly documentId: string
  readonly fileName: string
  readonly ownerKind: string
  readonly ownerId: string
  readonly uploadedAtUtc: string
}

export interface DocumentUpload {
  readonly document: DocumentRow
  readonly duplicates: readonly DocumentDuplicate[]
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}

export function listPath(ownerKind: OwnerKind, ownerId: string): string {
  const params = new URLSearchParams({ ownerKind, ownerId })
  return `${DOCUMENTS_PATH}?${params.toString()}`
}

export function contentPath(documentId: string): string {
  return `${DOCUMENTS_PATH}/${encodeURIComponent(documentId)}/content`
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SHA256 = /^[0-9a-f]{64}$/

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value)
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

function isInstant(value: unknown): value is string {
  return typeof value === 'string' && !Number.isNaN(Date.parse(value))
}

function isCount(value: unknown, minimum: number): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum
}

export function decodeDocumentRow(value: unknown): DocumentRow | null {
  if (!isRecord(value)) return null
  const scanState = value.scanState
  if (
    !isGuid(value.id) ||
    !isGuid(value.companyId) ||
    !isText(value.ownerKind) ||
    !isGuid(value.ownerId) ||
    !isCount(value.versionNo, 1) ||
    !isText(value.fileName) ||
    !isText(value.contentType) ||
    !isCount(value.sizeBytes, 1) ||
    typeof value.sha256 !== 'string' ||
    !SHA256.test(value.sha256) ||
    (scanState !== 'Clean' && scanState !== 'NotScanned') ||
    typeof value.downloadable !== 'boolean' ||
    // Only a clean version is ever served; an answer claiming otherwise is not this contract.
    (value.downloadable && scanState !== 'Clean') ||
    !isText(value.uploadedBy) ||
    !isInstant(value.uploadedAtUtc)
  ) {
    return null
  }
  return {
    id: value.id,
    companyId: value.companyId,
    ownerKind: value.ownerKind,
    ownerId: value.ownerId,
    versionNo: value.versionNo,
    fileName: value.fileName,
    contentType: value.contentType,
    sizeBytes: value.sizeBytes,
    sha256: value.sha256,
    scanState,
    downloadable: value.downloadable,
    uploadedBy: value.uploadedBy,
    uploadedAtUtc: value.uploadedAtUtc,
  }
}

function decodeLimits(value: unknown): DocumentLimits | null {
  if (!isRecord(value)) return null
  const { maximumBytes, extensions, contentTypes } = value
  if (
    !isCount(maximumBytes, 1) ||
    !Array.isArray(extensions) ||
    !extensions.every((e) => typeof e === 'string' && e.startsWith('.')) ||
    !Array.isArray(contentTypes) ||
    !contentTypes.every(isText)
  ) {
    return null
  }
  return { maximumBytes, extensions: extensions as string[], contentTypes: contentTypes as string[] }
}

/** A list is data only when every row is the asked owner's, under the company it was read in. */
export function decodeDocumentList(value: unknown, companyId: string, ownerKind: OwnerKind, ownerId: string): DocumentList | null {
  if (!isRecord(value) || !Array.isArray(value.items) || typeof value.canUpload !== 'boolean') return null
  const limits = decodeLimits(value.limits)
  if (limits === null) return null
  const items: DocumentRow[] = []
  for (const raw of value.items) {
    const row = decodeDocumentRow(raw)
    if (row === null || row.companyId !== companyId || row.ownerKind !== ownerKind || row.ownerId !== ownerId) return null
    items.push(row)
  }
  return { items, canUpload: value.canUpload, limits }
}

function decodeDuplicate(value: unknown): DocumentDuplicate | null {
  if (
    !isRecord(value) ||
    !isGuid(value.documentId) ||
    !isText(value.fileName) ||
    !isText(value.ownerKind) ||
    !isGuid(value.ownerId) ||
    !isInstant(value.uploadedAtUtc)
  ) {
    return null
  }
  return {
    documentId: value.documentId,
    fileName: value.fileName,
    ownerKind: value.ownerKind,
    ownerId: value.ownerId,
    uploadedAtUtc: value.uploadedAtUtc,
  }
}

/** An upload answer is this attempt's only when it names the company, the owner and the exact file size that was sent. */
export function decodeDocumentUpload(
  value: unknown,
  sent: { readonly companyId: string; readonly ownerKind: OwnerKind; readonly ownerId: string; readonly sizeBytes: number },
): DocumentUpload | null {
  if (!isRecord(value) || typeof value.isReplay !== 'boolean' || !isText(value.originalCorrelationId) || !Array.isArray(value.duplicates)) {
    return null
  }
  const document = decodeDocumentRow(value.document)
  if (
    document === null ||
    document.companyId !== sent.companyId ||
    document.ownerKind !== sent.ownerKind ||
    document.ownerId !== sent.ownerId ||
    document.sizeBytes !== sent.sizeBytes
  ) {
    return null
  }
  const duplicates: DocumentDuplicate[] = []
  for (const raw of value.duplicates) {
    const duplicate = decodeDuplicate(raw)
    if (duplicate === null || duplicate.documentId === document.id) return null
    duplicates.push(duplicate)
  }
  return { document, duplicates, isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId }
}

/** The stable `code` of a ProblemDetails body, when it has one. */
export function problemCode(body: unknown): string | null {
  return isRecord(body) && typeof body.code === 'string' ? body.code : null
}
