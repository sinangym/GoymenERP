/**
 * B-08 / BS-10 (STK-S12): the company's package standard table (pieces per package of a product), pinned to
 * `GET /api/v1/product-package-standards/current`, `POST /api/v1/product-package-standards/imports` and
 * `PUT /api/v1/product-package-standards/products/{productId}` (`ProductPackageStandardEndpoints`). The table is company-scoped:
 * every call sends the selected company only, the server resolves it. The import sends the WHOLE Excel file (base64) with a
 * mandatory source label and reason; the edit sets or removes one product's entry with a mandatory reason. Both send the expected
 * table version and one caller-owned `Idempotency-Key` (kept for a retry after an unknown outcome). Nothing is parsed, rounded or
 * guessed here: while no table or no entry exists the screen says "kaynak yok" (NoSource); the value is a suggestion the consumer
 * snapshots (URT-D27, AT-017).
 */

/** `ProductPackageStandardInputLimitsV2` / `ProductPackageStandardFileFormatV2` (reversible application details). */
export const PACKAGE_STANDARD_LIMITS = {
  sourceLabelMaximumLength: 200,
  reasonMaximumLength: 4000,
  fileNameMaximumLength: 255,
  maximumFileBytes: 2_097_152,
  fileExtension: '.xlsx',
  contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  productCodeHeader: 'Ürün kodu',
  piecesPerPackageHeader: 'Paket içi adet',
} as const

export const PACKAGE_STANDARD_PROBLEM = {
  requestInvalid: 'product_package_standard_request_invalid',
  commandMetadataInvalid: 'product_package_standard_command_metadata_invalid',
  fileInvalid: 'product_package_standard_file_invalid',
  sourceLabelRequired: 'product_package_standard_source_label_required',
  sourceLabelTooLong: 'product_package_standard_source_label_too_long',
  reasonRequired: 'product_package_standard_reason_required',
  reasonTooLong: 'product_package_standard_reason_too_long',
  piecesInvalid: 'product_package_standard_pieces_invalid',
  idempotencyConflict: 'product_package_standard_idempotency_conflict',
  versionConflict: 'product_package_standard_version_conflict',
  providerUnavailable: 'product_package_standard_provider_unavailable',
} as const

/** `ProductPackageStandardSourceKindsV2`. */
export const PACKAGE_STANDARD_SOURCE_KINDS = ['Import', 'Edit'] as const
export type PackageStandardSourceKind = (typeof PACKAGE_STANDARD_SOURCE_KINDS)[number]

export interface PackageStandardEntry {
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly piecesPerPackage: number
}

/** The current version's source; for an `Edit` version the label and the file are null. */
export interface PackageStandardSource {
  readonly kind: PackageStandardSourceKind
  readonly sourceLabel: string | null
  readonly fileName: string | null
  readonly fileSha256: string | null
  readonly createdAtUtc: string
  readonly createdBy: string
}

export interface PackageStandardTableView {
  /** "0" while no table exists. */
  readonly version: string
  /** Null exactly when the version is "0". */
  readonly source: PackageStandardSource | null
  readonly entries: readonly PackageStandardEntry[]
}

export type PackageStandardRead =
  | { readonly type: 'ok'; readonly data: PackageStandardTableView }
  /** 401: the session ended — not the same as lacking the permission. */
  | { readonly type: 'unauthenticated' }
  /** 403: no `product.package-standards.manage` here (granted to no role). */
  | { readonly type: 'forbidden' }
  /** 503: the provider is not wired in this host. */
  | { readonly type: 'unavailable' }
  | { readonly type: 'failed' }

export type PackageStandardCommand =
  | { readonly type: 'applied'; readonly replayed: boolean }
  /** `detail` is the server's Turkish reason (for a refused file it names the rows); shown only for the file refusal. */
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly detail: string | null }
  | { readonly type: 'unknown' }

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export const PACKAGE_STANDARD_BASE_PATH = '/product-package-standards'
export const PACKAGE_STANDARD_CURRENT_PATH = `${PACKAGE_STANDARD_BASE_PATH}/current`
export const PACKAGE_STANDARD_IMPORTS_PATH = `${PACKAGE_STANDARD_BASE_PATH}/imports`

export function packageStandardProductPath(productId: string): string {
  return `${PACKAGE_STANDARD_BASE_PATH}/products/${encodeURIComponent(productId)}`
}

function headers(companyId: string): Record<string, string> {
  return { ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': companyId }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** Exactly these members, no more and no fewer: an unknown member is off-contract. */
function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== ''
}

/** A positive whole number within the server's `int`. */
export function isPiecesPerPackage(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 && value <= 2_147_483_647
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SHA256_HEX = /^[0-9a-f]{64}$/i
const VERSION = /^(0|[1-9]\d*)$/

/** `ProductPackageStandardTableViewV2`'s members. */
const TABLE_VIEW_KEYS = [
  'companyId', 'version', 'sourceKind', 'sourceLabel', 'fileName', 'fileSha256', 'createdAtUtc', 'createdBy', 'entries',
] as const
const SOURCE_KEYS = ['sourceKind', 'sourceLabel', 'fileName', 'fileSha256', 'createdAtUtc', 'createdBy'] as const
const FILE_KEYS = ['sourceLabel', 'fileName', 'fileSha256'] as const

/** `ProductPackageStandardEntryV2`'s members. */
const ENTRY_KEYS = ['productId', 'productCode', 'productName', 'piecesPerPackage'] as const

function decodeEntry(raw: unknown): PackageStandardEntry | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ENTRY_KEYS)) return null
  if (typeof raw.productId !== 'string' || !UUID.test(raw.productId) || !isNonEmptyString(raw.productCode)
    || typeof raw.productName !== 'string' || !isPiecesPerPackage(raw.piecesPerPackage)) {
    return null
  }
  return {
    productId: raw.productId,
    productCode: raw.productCode,
    productName: raw.productName,
    piecesPerPackage: raw.piecesPerPackage,
  }
}

export function decodePackageStandardTable(raw: unknown, companyId: string): PackageStandardTableView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, TABLE_VIEW_KEYS) || raw.companyId !== companyId
    || typeof raw.version !== 'string' || !VERSION.test(raw.version) || !Array.isArray(raw.entries)) {
    return null
  }
  if (raw.version === '0') {
    if (SOURCE_KEYS.some((key) => raw[key] !== null) || raw.entries.length !== 0) return null
    return { version: '0', source: null, entries: [] }
  }
  const kind = raw.sourceKind
  if (!PACKAGE_STANDARD_SOURCE_KINDS.includes(kind as PackageStandardSourceKind)
    || !isNonEmptyString(raw.createdAtUtc) || !isNonEmptyString(raw.createdBy)) {
    return null
  }
  if (kind === 'Import') {
    // An import names its label and file and holds at least one entry.
    if (!isNonEmptyString(raw.sourceLabel) || !isNonEmptyString(raw.fileName) || typeof raw.fileSha256 !== 'string'
      || !SHA256_HEX.test(raw.fileSha256) || raw.entries.length === 0) {
      return null
    }
  } else if (FILE_KEYS.some((key) => raw[key] !== null)) {
    return null
  }
  const entries: PackageStandardEntry[] = []
  const seen = new Set<string>()
  for (const candidate of raw.entries) {
    const entry = decodeEntry(candidate)
    if (entry === null || seen.has(entry.productId.toLowerCase())) return null
    seen.add(entry.productId.toLowerCase())
    entries.push(entry)
  }
  return {
    version: raw.version,
    source: {
      kind: kind as PackageStandardSourceKind,
      sourceLabel: (raw.sourceLabel as string | null) ?? null,
      fileName: (raw.fileName as string | null) ?? null,
      fileSha256: (raw.fileSha256 as string | null) ?? null,
      createdAtUtc: raw.createdAtUtc,
      createdBy: raw.createdBy,
    },
    entries,
  }
}

export async function fetchPackageStandardTable(companyId: string, signal?: AbortSignal): Promise<PackageStandardRead> {
  try {
    const response = await fetch(`${API_BASE}${PACKAGE_STANDARD_CURRENT_PATH}`, { headers: headers(companyId), signal })
    if (response.status === 401) return { type: 'unauthenticated' }
    if (response.status === 403) return { type: 'forbidden' }
    if (response.status === 503) return { type: 'unavailable' }
    if (response.status !== 200) return { type: 'failed' }
    const data = decodePackageStandardTable(await response.json(), companyId)
    return data === null ? { type: 'failed' } : { type: 'ok', data }
  } catch {
    return { type: 'failed' }
  }
}

async function send(
  method: 'POST' | 'PUT',
  path: string,
  companyId: string,
  idempotencyKey: string,
  body: unknown,
): Promise<PackageStandardCommand> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method,
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(body),
    })
  } catch {
    return { type: 'unknown' }
  }
  if (response.status === 204) {
    return { type: 'applied', replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  }
  // A 5xx (the unwired provider included) or an unexpected status is an unknown outcome: the key is kept for a retry.
  if (response.status >= 500 || response.status < 400) return { type: 'unknown' }
  let raw: unknown = null
  try {
    raw = await response.json()
  } catch {
    raw = null
  }
  const code = isRecord(raw) && typeof raw.code === 'string' && raw.code.length > 0 ? raw.code : null
  const detail = isRecord(raw) && isNonEmptyString(raw.detail) ? raw.detail : null
  return { type: 'refused', status: response.status, code, detail }
}

export interface ImportPackageStandardBody {
  readonly expectedVersion: string
  readonly sourceLabel: string
  readonly reason: string
  readonly fileName: string
  readonly contentBase64: string
}

export function importPackageStandardTable(
  companyId: string,
  idempotencyKey: string,
  body: ImportPackageStandardBody,
): Promise<PackageStandardCommand> {
  return send('POST', PACKAGE_STANDARD_IMPORTS_PATH, companyId, idempotencyKey, body)
}

export interface SetPackageStandardBody {
  readonly expectedVersion: string
  /** Null removes the product's entry. */
  readonly piecesPerPackage: number | null
  readonly reason: string
}

export function setPackageStandardEntry(
  companyId: string,
  productId: string,
  idempotencyKey: string,
  body: SetPackageStandardBody,
): Promise<PackageStandardCommand> {
  return send('PUT', packageStandardProductPath(productId), companyId, idempotencyKey, body)
}

export function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
  }
  return btoa(binary)
}

export type PackageFileCheck = 'ok' | 'not-xlsx' | 'too-large' | 'empty' | 'name-too-long'

/** Helps the person before sending: the server reads and refuses the file itself. */
export function checkPackageFile(file: { readonly name: string; readonly size: number }): PackageFileCheck {
  if (!file.name.toLowerCase().endsWith(PACKAGE_STANDARD_LIMITS.fileExtension)) return 'not-xlsx'
  if (file.name.length > PACKAGE_STANDARD_LIMITS.fileNameMaximumLength) return 'name-too-long'
  if (file.size === 0) return 'empty'
  if (file.size > PACKAGE_STANDARD_LIMITS.maximumFileBytes) return 'too-large'
  return 'ok'
}

/** Reads a typed pieces value: a positive whole number in plain digits, otherwise null. */
export function parsePieces(text: string): number | null {
  const trimmed = text.trim()
  if (!/^[1-9]\d*$/.test(trimmed)) return null
  const value = Number(trimmed)
  return isPiecesPerPackage(value) ? value : null
}
