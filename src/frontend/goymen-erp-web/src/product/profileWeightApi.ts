/**
 * URN-D13…D16 (STK-S37R): the company's kg/m table of profiles and round tubes, pinned to
 * `GET /api/v1/product-profile-weight-tables/current` and `POST /api/v1/product-profile-weight-tables`
 * (`ProductProfileWeightTableEndpoints`). The table is company-scoped: reads and the import send the selected company only, the
 * server resolves it. The import sends the WHOLE file (base64) with a mandatory source label and reason, the expected version
 * and one caller-owned `Idempotency-Key` (kept for a retry after an unknown outcome); the server reads the rows, keeps the file
 * in the document vault and refuses fail-closed (503) while no vault or scanner can take it. Nothing is parsed or rounded here.
 * KLN-D87 (STK-SC33-b): a table may also hold structural profile rows keyed by the standard series and size designation (no
 * measure); the source and version of that standard table are not chosen here — whichever source-labelled file is imported.
 */

import { PRODUCT_SECTION_KINDS, isTextValue, type ProductSectionKind } from './dimensionApi'

/** The kinds the table serves (`ProductProfileWeightRowV1.SectionKind`; the ERW kinds read the profile / round tube rows). */
export const TABLE_SECTION_KINDS = ['Profile', 'RoundTube', 'StructuralProfile'] as const satisfies readonly ProductSectionKind[]
export type TableSectionKind = (typeof TABLE_SECTION_KINDS)[number]

/** `ProductProfileWeightInputLimitsV1` / `ProductProfileWeightFileFormatV1` (reversible details, KLN-D54). */
export const PROFILE_WEIGHT_LIMITS = {
  sourceLabelMaximumLength: 200,
  reasonMaximumLength: 4000,
  fileNameMaximumLength: 255,
  maximumFileBytes: 1_048_576,
  fileExtension: '.csv',
  header: 'sectionKind;widthMm;heightMm;outerDiameterMm;wallThicknessMm;kgPerMetre',
  /** KLN-D87: the header with the structural profile series and size designation. */
  extendedHeader: 'sectionKind;widthMm;heightMm;outerDiameterMm;wallThicknessMm;kgPerMetre;profileSeries;profileSize',
} as const

export const PROFILE_WEIGHT_PROBLEM = {
  requestInvalid: 'product_profile_weight_request_invalid',
  commandMetadataInvalid: 'product_profile_weight_command_metadata_invalid',
  fileInvalid: 'product_profile_weight_file_invalid',
  fileRejected: 'product_profile_weight_file_rejected',
  sourceLabelRequired: 'product_profile_weight_source_label_required',
  sourceLabelTooLong: 'product_profile_weight_source_label_too_long',
  reasonRequired: 'product_profile_weight_reason_required',
  reasonTooLong: 'product_profile_weight_reason_too_long',
  idempotencyConflict: 'product_profile_weight_idempotency_conflict',
  versionConflict: 'product_profile_weight_version_conflict',
  fileVaultUnavailable: 'product_profile_weight_file_vault_unavailable',
  providerUnavailable: 'product_profile_weight_provider_unavailable',
} as const

export interface ProfileWeightRow {
  readonly sectionKind: TableSectionKind
  readonly widthMm: number | null
  readonly heightMm: number | null
  readonly outerDiameterMm: number | null
  /** Null exactly on a structural profile row. */
  readonly wallThicknessMm: number | null
  readonly kgPerMetre: number
  /** KLN-D87: set exactly on a structural profile row. */
  readonly profileSeries: string | null
  readonly profileSize: string | null
}

export interface ProfileWeightImported {
  readonly sourceLabel: string
  readonly fileName: string
  readonly fileSha256: string
  readonly documentId: string
  readonly importedAtUtc: string
  readonly importedBy: string
}

export interface ProfileWeightTableView {
  /** "0" while no table was imported. */
  readonly version: string
  /** Null exactly when the version is "0". */
  readonly imported: ProfileWeightImported | null
  readonly rows: readonly ProfileWeightRow[]
}

export type ProfileWeightRead =
  | { readonly type: 'ok'; readonly data: ProfileWeightTableView }
  /** 401: the session ended — not the same as lacking the permission. */
  | { readonly type: 'unauthenticated' }
  /** 403: no `product.profile-weights.import` here (granted to no role). */
  | { readonly type: 'forbidden' }
  /** 503: the provider is not wired in this host. */
  | { readonly type: 'unavailable' }
  | { readonly type: 'failed' }

export type ProfileWeightCommand =
  | { readonly type: 'applied'; readonly replayed: boolean }
  /** `detail` is the server's Turkish reason (for a refused file it names the line); shown only for the file refusal. */
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly detail: string | null }
  | { readonly type: 'unknown' }

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export const PROFILE_WEIGHT_TABLE_PATH = '/product-profile-weight-tables'
export const PROFILE_WEIGHT_CURRENT_PATH = `${PROFILE_WEIGHT_TABLE_PATH}/current`

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

function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== ''
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SHA256_HEX = /^[0-9a-f]{64}$/i

/** `ProductProfileWeightTableViewV1`'s members. */
const TABLE_VIEW_KEYS = [
  'companyId', 'version', 'sourceLabel', 'fileName', 'fileSha256', 'documentId', 'importedAtUtc', 'importedBy', 'rows',
] as const
const IMPORTED_KEYS = ['sourceLabel', 'fileName', 'fileSha256', 'documentId', 'importedAtUtc', 'importedBy'] as const

/** `ProductProfileWeightRowV1`'s members. */
const ROW_KEYS = [
  'sectionKind', 'widthMm', 'heightMm', 'outerDiameterMm', 'wallThicknessMm', 'kgPerMetre', 'profileSeries', 'profileSize',
] as const

function decodeRow(raw: unknown): ProfileWeightRow | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ROW_KEYS)) return null
  const kind = raw.sectionKind
  if (!TABLE_SECTION_KINDS.includes(kind as TableSectionKind) || !PRODUCT_SECTION_KINDS.includes(kind as ProductSectionKind)) {
    return null
  }
  if (!isPositiveNumber(raw.kgPerMetre)) return null
  const { widthMm, heightMm, outerDiameterMm, wallThicknessMm, profileSeries, profileSize } = raw
  const structural = kind === 'StructuralProfile'
  const textsFit = structural
    ? typeof profileSeries === 'string' && isTextValue('profileSeries', profileSeries)
      && typeof profileSize === 'string' && isTextValue('profileSize', profileSize)
    : profileSeries === null && profileSize === null
  const shapeFits = structural
    ? widthMm === null && heightMm === null && outerDiameterMm === null && wallThicknessMm === null
    : isPositiveNumber(wallThicknessMm) && (kind === 'Profile'
      ? isPositiveNumber(widthMm) && isPositiveNumber(heightMm) && outerDiameterMm === null
      : isPositiveNumber(outerDiameterMm) && widthMm === null && heightMm === null)
  if (!shapeFits || !textsFit) return null
  return {
    sectionKind: kind as TableSectionKind,
    widthMm: widthMm as number | null,
    heightMm: heightMm as number | null,
    outerDiameterMm: outerDiameterMm as number | null,
    wallThicknessMm: wallThicknessMm as number | null,
    kgPerMetre: raw.kgPerMetre,
    profileSeries: profileSeries as string | null,
    profileSize: profileSize as string | null,
  }
}

export function decodeProfileWeightTable(raw: unknown, companyId: string): ProfileWeightTableView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, TABLE_VIEW_KEYS) || raw.companyId !== companyId
    || typeof raw.version !== 'string' || !/^(0|[1-9]\d*)$/.test(raw.version) || !Array.isArray(raw.rows)) {
    return null
  }
  if (raw.version === '0') {
    if (IMPORTED_KEYS.some((key) => raw[key] !== null) || raw.rows.length !== 0) return null
    return { version: '0', imported: null, rows: [] }
  }
  if (!isNonEmptyString(raw.sourceLabel) || !isNonEmptyString(raw.fileName) || typeof raw.fileSha256 !== 'string'
    || !SHA256_HEX.test(raw.fileSha256) || typeof raw.documentId !== 'string' || !UUID.test(raw.documentId)
    || !isNonEmptyString(raw.importedAtUtc) || !isNonEmptyString(raw.importedBy) || raw.rows.length === 0) {
    return null
  }
  const rows: ProfileWeightRow[] = []
  for (const candidate of raw.rows) {
    const row = decodeRow(candidate)
    if (row === null) return null
    rows.push(row)
  }
  return {
    version: raw.version,
    imported: {
      sourceLabel: raw.sourceLabel,
      fileName: raw.fileName,
      fileSha256: raw.fileSha256,
      documentId: raw.documentId,
      importedAtUtc: raw.importedAtUtc,
      importedBy: raw.importedBy,
    },
    rows,
  }
}

export async function fetchProfileWeightTable(companyId: string, signal?: AbortSignal): Promise<ProfileWeightRead> {
  try {
    const response = await fetch(`${API_BASE}${PROFILE_WEIGHT_CURRENT_PATH}`, { headers: headers(companyId), signal })
    if (response.status === 401) return { type: 'unauthenticated' }
    if (response.status === 403) return { type: 'forbidden' }
    if (response.status === 503) return { type: 'unavailable' }
    if (response.status !== 200) return { type: 'failed' }
    const data = decodeProfileWeightTable(await response.json(), companyId)
    return data === null ? { type: 'failed' } : { type: 'ok', data }
  } catch {
    return { type: 'failed' }
  }
}

export interface ImportProfileWeightBody {
  readonly expectedVersion: string
  readonly sourceLabel: string
  readonly reason: string
  readonly fileName: string
  readonly contentBase64: string
}

export async function importProfileWeightTable(
  companyId: string,
  idempotencyKey: string,
  body: ImportProfileWeightBody,
): Promise<ProfileWeightCommand> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${PROFILE_WEIGHT_TABLE_PATH}`, {
      method: 'POST',
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(body),
    })
  } catch {
    return { type: 'unknown' }
  }
  if (response.status === 204) {
    return { type: 'applied', replayed: response.headers.get('Idempotency-Replayed') === 'true' }
  }
  let raw: unknown = null
  try {
    raw = await response.json()
  } catch {
    raw = null
  }
  const code = isRecord(raw) && typeof raw.code === 'string' && raw.code.length > 0 ? raw.code : null
  const detail = isRecord(raw) && isNonEmptyString(raw.detail) ? raw.detail : null
  // 503 with the vault's code is a DEFINITE refusal (nothing was imported); any other 5xx is an unknown outcome.
  if (response.status === 503 && code === PROFILE_WEIGHT_PROBLEM.fileVaultUnavailable) {
    return { type: 'refused', status: 503, code, detail }
  }
  if (response.status >= 500 || response.status < 400) return { type: 'unknown' }
  return { type: 'refused', status: response.status, code, detail }
}

export function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
  }
  return btoa(binary)
}

export type FileCheck = 'ok' | 'not-csv' | 'too-large' | 'empty' | 'name-too-long'

/** Helps the person before sending: the server reads and refuses the file itself. */
export function checkFile(file: { readonly name: string; readonly size: number }): FileCheck {
  if (!file.name.toLowerCase().endsWith(PROFILE_WEIGHT_LIMITS.fileExtension)) return 'not-csv'
  if (file.name.length > PROFILE_WEIGHT_LIMITS.fileNameMaximumLength) return 'name-too-long'
  if (file.size === 0) return 'empty'
  if (file.size > PROFILE_WEIGHT_LIMITS.maximumFileBytes) return 'too-large'
  return 'ok'
}
