/** STK-S13 / BS-10: direct ProductPaintMembershipContractsV1 JSON. Enums are numeric on this endpoint. */
import { isBoundUuid } from '../wireGuards'

export const PAINT_MEMBERSHIP_CAPABILITY = 'paint-memberships.manage'
export const PAINT_MEMBERSHIP_ROUTE = '/boya-urunleri'
export const PAINT_SOURCE = 'product.paint-memberships.v1'
export const PAINT_LIMITS = { fileBytes: 2_097_152, rows: 5000, column: 200, sourceLabel: 200, reason: 4000 } as const
export const DEFAULT_PAINT_COLUMN = 'Ürün kodu'

export interface PaintEntry {
  readonly productId: string
  readonly productVersion: string
  readonly membershipVersion: string
  readonly productCode: string
  readonly productName: string
  readonly sourceVersion: string
}
export interface PaintCatalog {
  readonly outcome: 1 | 2 | 3
  readonly companyId: string
  readonly version: string
  readonly entries: readonly PaintEntry[]
  readonly sourceVersion: string
}
export interface PaintPreviewRow {
  readonly rowNumber: number
  readonly productId: string
  readonly productVersion: string
  readonly membershipVersion: string
  readonly productCode: string
  readonly productName: string
  readonly alreadyMember: boolean
}
export type PaintCandidate = Omit<PaintPreviewRow, 'rowNumber'>
export interface PaintCandidates {
  readonly outcome: 1 | 2 | 3
  readonly companyId: string
  readonly version: string
  readonly q: string | null
  readonly limit: number
  readonly items: readonly PaintCandidate[]
  readonly searchWindowFull: boolean
  readonly sourceVersion: string
}
export const PAINT_CANDIDATE_LIMIT = 25
export const PAINT_CANDIDATE_QUERY_LIMIT = 100
export interface PaintImportError {
  readonly rowNumber: number | null
  readonly field: string
  readonly code: string
  readonly message: string
}
export interface PaintPreview {
  readonly companyId: string
  readonly version: string
  readonly fileSha256: string
  readonly productCodeColumn: string
  readonly previewToken: string
  readonly sourceRowCount: number
  readonly rows: readonly PaintPreviewRow[]
  readonly errors: readonly PaintImportError[]
  readonly ready: boolean
}
export interface PaintMutation {
  readonly companyId: string
  readonly version: string
  readonly changedEntries: readonly PaintEntry[]
  readonly isReplay: boolean
  readonly originalCorrelationId: string
  readonly removedProductIds: readonly string[] | null
}
export interface PaintValidateRequest {
  readonly fileName: string
  readonly contentBase64: string
  readonly productCodeColumn: string
}
export interface PaintApplyRequest extends PaintValidateRequest {
  readonly expectedVersion: string
  readonly fileSha256: string
  readonly previewToken: string
  readonly sourceLabel: string
  readonly reason: string
}
export interface PaintSetRequest {
  readonly expectedVersion: string
  readonly expectedProductVersion: string
  readonly expectedMembershipVersion: string
  readonly isMember: boolean
  readonly reason: string
}
export type PaintCommand =
  /** Local response identity proof: only rows that the preview says will be newly added; not part of the wire payload. */
  | { readonly kind: 'import'; readonly payload: PaintApplyRequest; readonly expectedProductIds: readonly string[] }
  | { readonly kind: 'set'; readonly productId: string; readonly payload: PaintSetRequest }

type Json = Record<string, unknown>
const record = (value: unknown): value is Json => typeof value === 'object' && value !== null && !Array.isArray(value)
const exact = (value: Json, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every((key) => Object.hasOwn(value, key))
const text = (value: unknown, maximum: number): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= maximum
const count = (value: unknown, maximum: number): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value <= maximum
const rowNumber = (value: unknown): value is number => count(value, 1_048_576) && value >= 2
export const paintVersion = (value: unknown): value is string => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) && BigInt(value) <= 9_223_372_036_854_775_807n
const positiveVersion = (value: unknown): value is string => paintVersion(value) && value !== '0'
export const samePaintId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()
const uniqueIds = (values: readonly string[]) => new Set(values.map((id) => id.toLowerCase())).size === values.length
const ENTRY_KEYS = ['productId', 'productVersion', 'membershipVersion', 'productCode', 'productName', 'sourceVersion'] as const

function entry(value: unknown): PaintEntry | null {
  if (!record(value) || !exact(value, ENTRY_KEYS) || !isBoundUuid(value.productId) || !positiveVersion(value.productVersion)
    || !positiveVersion(value.membershipVersion) || !text(value.productCode, 64) || !text(value.productName, 2000)
    || value.sourceVersion !== PAINT_SOURCE) return null
  return value as unknown as PaintEntry
}
function entries(value: unknown): readonly PaintEntry[] | null {
  if (!Array.isArray(value) || value.length > PAINT_LIMITS.rows) return null
  const result = value.map(entry)
  return result.some((item) => item === null) || !uniqueIds((result as PaintEntry[]).map((item) => item.productId))
    ? null : result as PaintEntry[]
}
export function decodePaintCatalog(value: unknown, companyId: string): PaintCatalog | null {
  if (!record(value) || !exact(value, ['outcome', 'companyId', 'version', 'entries', 'sourceVersion'])
    || !isBoundUuid(value.companyId) || !samePaintId(value.companyId, companyId) || !paintVersion(value.version)
    || (value.outcome !== 1 && value.outcome !== 2 && value.outcome !== 3) || value.sourceVersion !== PAINT_SOURCE) return null
  const items = entries(value.entries)
  if (items === null || (value.outcome === 1 ? value.version === '0' : value.version !== '0' || items.length !== 0)
    || items.some((item) => BigInt(item.membershipVersion) > BigInt(value.version as string))) return null
  return { outcome: value.outcome, companyId: value.companyId, version: value.version, entries: items, sourceVersion: value.sourceVersion }
}
export function decodePaintCandidates(value: unknown, companyId: string, expectedQ: string | null,
  expectedLimit = PAINT_CANDIDATE_LIMIT): PaintCandidates | null {
  if (!record(value) || !exact(value, ['outcome', 'companyId', 'version', 'q', 'limit', 'items', 'searchWindowFull', 'sourceVersion'])
    || !isBoundUuid(value.companyId) || !samePaintId(value.companyId, companyId) || !paintVersion(value.version)
    || (value.outcome !== 1 && value.outcome !== 2 && value.outcome !== 3) || value.sourceVersion !== PAINT_SOURCE
    || value.q !== expectedQ || !(value.q === null || typeof value.q === 'string' && value.q.length <= PAINT_CANDIDATE_QUERY_LIMIT
      && value.q.trim() === value.q && value.q !== '' && !/[\u0000-\u001f\u007f-\u009f]/.test(value.q))
    || !count(value.limit, 50) || value.limit < 1 || value.limit !== expectedLimit
    || !Array.isArray(value.items) || value.items.length > value.limit || typeof value.searchWindowFull !== 'boolean') return null
  const result: PaintCandidate[] = []
  for (const item of value.items) {
    if (!record(item) || !exact(item, ['productId', 'productVersion', 'membershipVersion', 'productCode', 'productName', 'alreadyMember'])
      || !isBoundUuid(item.productId) || !positiveVersion(item.productVersion) || !paintVersion(item.membershipVersion)
      || !text(item.productCode, 64) || !text(item.productName, 250)
      || /[\u0000-\u001f\u007f-\u009f]/.test(item.productCode) || /[\u0000-\u001f\u007f-\u009f]/.test(item.productName)
      || typeof item.alreadyMember !== 'boolean' || item.alreadyMember !== (item.membershipVersion !== '0')
      || BigInt(item.membershipVersion) > BigInt(value.version)) return null
    result.push(item as unknown as PaintCandidate)
  }
  if (!uniqueIds(result.map((item) => item.productId)) || (value.outcome === 1 ? value.version === '0'
    : value.version !== '0' || result.length !== 0 || value.searchWindowFull)
    || value.searchWindowFull && result.length !== value.limit) return null
  return { outcome: value.outcome, companyId: value.companyId, version: value.version, q: value.q,
    limit: value.limit, items: result, searchWindowFull: value.searchWindowFull, sourceVersion: value.sourceVersion }
}
function previewRow(value: unknown): PaintPreviewRow | null {
  if (!record(value) || !exact(value, ['rowNumber', 'productId', 'productVersion', 'membershipVersion', 'productCode', 'productName', 'alreadyMember'])
    || !rowNumber(value.rowNumber) || !isBoundUuid(value.productId) || !positiveVersion(value.productVersion)
    || !paintVersion(value.membershipVersion) || !text(value.productCode, 64) || !text(value.productName, 2000)
    || typeof value.alreadyMember !== 'boolean' || value.alreadyMember !== (value.membershipVersion !== '0')) return null
  return value as unknown as PaintPreviewRow
}
function importError(value: unknown): PaintImportError | null {
  if (!record(value) || !exact(value, ['rowNumber', 'field', 'code', 'message'])
    || !(value.rowNumber === null || rowNumber(value.rowNumber)) || !text(value.field, 200)
    || !text(value.code, 200) || !text(value.message, 8000)) return null
  return value as unknown as PaintImportError
}
export function decodePaintPreview(value: unknown, companyId: string, expected: { readonly sha256: string; readonly column: string }): PaintPreview | null {
  if (!record(value) || !exact(value, ['companyId', 'version', 'fileSha256', 'productCodeColumn', 'previewToken', 'sourceRowCount', 'rows', 'errors', 'ready'])
    || !isBoundUuid(value.companyId) || !samePaintId(value.companyId, companyId) || !paintVersion(value.version)
    || typeof value.fileSha256 !== 'string' || !/^[0-9a-f]{64}$/.test(value.fileSha256) || value.fileSha256 !== expected.sha256
    || value.productCodeColumn !== expected.column || !text(value.previewToken, 4096) || !count(value.sourceRowCount, PAINT_LIMITS.rows)
    || !Array.isArray(value.rows) || value.rows.length > PAINT_LIMITS.rows || !Array.isArray(value.errors) || value.errors.length > PAINT_LIMITS.rows + 1
    || typeof value.ready !== 'boolean') return null
  const rows = value.rows.map(previewRow)
  const errors = value.errors.map(importError)
  if (rows.some((item) => item === null) || errors.some((item) => item === null)) return null
  const typedRows = rows as PaintPreviewRow[]
  if (typedRows.length > value.sourceRowCount || !uniqueIds(typedRows.map((item) => item.productId))
    || new Set(typedRows.map((item) => item.rowNumber)).size !== typedRows.length
    || new Set(typedRows.map((item) => item.productCode)).size !== typedRows.length
    || typedRows.some((item) => BigInt(item.membershipVersion) > BigInt(value.version as string))
    || value.ready !== (value.sourceRowCount > 0 && typedRows.length === value.sourceRowCount && errors.length === 0)) return null
  return { companyId: value.companyId, version: value.version, fileSha256: value.fileSha256, productCodeColumn: expected.column,
    previewToken: value.previewToken, sourceRowCount: value.sourceRowCount, rows: typedRows, errors: errors as PaintImportError[], ready: value.ready }
}
export function decodePaintMutation(value: unknown, companyId: string, command: PaintCommand): PaintMutation | null {
  if (!record(value) || !exact(value, ['companyId', 'version', 'changedEntries', 'isReplay', 'originalCorrelationId', 'removedProductIds'])
    || !isBoundUuid(value.companyId) || !samePaintId(value.companyId, companyId) || !positiveVersion(value.version)
    || typeof value.isReplay !== 'boolean' || !text(value.originalCorrelationId, 200)
    || !(value.removedProductIds === null || (Array.isArray(value.removedProductIds) && value.removedProductIds.length <= PAINT_LIMITS.rows
      && value.removedProductIds.every(isBoundUuid) && uniqueIds(value.removedProductIds)))) return null
  const changed = entries(value.changedEntries)
  const removed = (value.removedProductIds ?? []) as string[]
  if (changed === null || changed.some((item) => removed.some((id) => samePaintId(id, item.productId))
    || BigInt(item.membershipVersion) > BigInt(value.version as string))) return null
  if (command.kind === 'import') {
    if (removed.length !== 0 || changed.length !== command.expectedProductIds.length
      || changed.some((item) => !command.expectedProductIds.some((id) => samePaintId(id, item.productId)))) return null
  } else if (command.payload.isMember) {
    const request = command.payload
    if (removed.length !== 0 || changed.length !== (request.expectedMembershipVersion === '0' ? 1 : 0)
      || changed.some((item) => !samePaintId(item.productId, command.productId) || item.productVersion !== request.expectedProductVersion)) return null
  } else if (changed.length !== 0 || removed.length !== (command.payload.expectedMembershipVersion === '0' ? 0 : 1)
    || removed.some((id) => !samePaintId(id, command.productId))) return null
  const expectedVersion = command.payload.expectedVersion
  if (!paintVersion(expectedVersion) || value.version !== (changed.length + removed.length === 0
    ? expectedVersion : (BigInt(expectedVersion) + 1n).toString())) return null
  return { companyId: value.companyId, version: value.version, changedEntries: changed, isReplay: value.isReplay,
    originalCorrelationId: value.originalCorrelationId, removedProductIds: value.removedProductIds as string[] | null }
}
