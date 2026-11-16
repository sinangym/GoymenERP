/**
 * SAT-D382 / SAT-D383 / SAT-D397 / SAT-D401 (STK-SC36-c): the package standard's approval, customer override and kept import
 * files, pinned to `ProductPackageStandardEndpoints`:
 * - `GET /api/v1/product-package-standards/approval-state` (`product.package-standards.manage`) and `GET …/pending`
 *   (`product.package-standards.approve`): the head version, today's business day, the version in effect today with its customer
 *   overrides, the version waiting for approval with its changes, and the newest versions;
 * - `GET …/imports` and `GET …/imports/{importId}/report`: the kept import files and their result reports (the file itself is not
 *   served back);
 * - `PUT …/customers/{customerId}/products/{productId}`: propose a customer's override (null removes it);
 * - `POST …/versions/{version}/approve` and `…/reject`: decide the pending version.
 * Every call sends the selected company only; the server resolves and checks it. Commands send one caller-owned
 * `Idempotency-Key`, kept for a retry after an unknown outcome. Nothing is decided here: who may approve, the effective date's
 * floor and the customer's validity are the server's.
 */

import { PACKAGE_STANDARD_BASE_PATH, PACKAGE_STANDARD_SOURCE_KINDS, isPiecesPerPackage, type PackageStandardSourceKind } from './packageImportApi'

/** `ProductPackageStandardProblemCodesV3` plus the V2 codes these routes declare. */
export const PACKAGE_APPROVAL_PROBLEM = {
  requestInvalid: 'product_package_standard_request_invalid',
  commandMetadataInvalid: 'product_package_standard_command_metadata_invalid',
  piecesInvalid: 'product_package_standard_pieces_invalid',
  reasonRequired: 'product_package_standard_reason_required',
  reasonTooLong: 'product_package_standard_reason_too_long',
  idempotencyConflict: 'product_package_standard_idempotency_conflict',
  versionConflict: 'product_package_standard_version_conflict',
  providerUnavailable: 'product_package_standard_provider_unavailable',
  approvalPending: 'product_package_standard_approval_pending',
  versionNotPending: 'product_package_standard_version_not_pending',
  preparerCannotDecide: 'product_package_standard_preparer_cannot_decide',
  effectiveDateInvalid: 'product_package_standard_effective_date_invalid',
  productInactive: 'product_package_standard_product_inactive',
  customerNotFound: 'product_package_standard_customer_not_found',
  customerProviderUnavailable: 'product_package_standard_customer_provider_unavailable',
} as const

/** `ProductPackageStandardVersionStatesV3`. */
export const PACKAGE_VERSION_STATES = ['Legacy', 'Pending', 'Approved', 'Rejected'] as const
export type PackageVersionState = (typeof PACKAGE_VERSION_STATES)[number]

/** `ProductPackageStandardImportOutcomesV3`. */
export const PACKAGE_IMPORT_OUTCOMES = ['Accepted', 'Refused'] as const
export type PackageImportOutcome = (typeof PACKAGE_IMPORT_OUTCOMES)[number]

export interface PackageCustomerEntry {
  readonly customerId: string
  /** Null when the Cari owner cannot answer. */
  readonly customerCode: string | null
  readonly customerName: string | null
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  readonly piecesPerPackage: number
}

export interface PackageChange {
  /** Null for the product default. */
  readonly customerId: string | null
  readonly customerName: string | null
  readonly productId: string
  readonly productCode: string
  readonly productName: string
  /** Null = no entry. */
  readonly piecesBefore: number | null
  readonly piecesAfter: number | null
}

export interface PackageVersionSummary {
  readonly version: string
  readonly state: PackageVersionState
  readonly sourceKind: PackageStandardSourceKind
  readonly sourceLabel: string | null
  readonly fileName: string | null
  readonly editedProductId: string | null
  readonly editedCustomerId: string | null
  readonly reason: string
  readonly entryCount: number
  readonly customerEntryCount: number
  readonly createdAtUtc: string
  readonly createdBy: string
  readonly effectiveFrom: string | null
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
}

export interface PackageApprovalView {
  /** The expected version of the next command. */
  readonly headVersion: string
  /** Today's business day (`yyyy-MM-dd`): the earliest effective date an approval may name. */
  readonly today: string
  readonly effectiveVersion: string | null
  readonly effectiveFrom: string | null
  readonly customerNamesAvailable: boolean
  readonly customerEntries: readonly PackageCustomerEntry[]
  readonly pending: PackageVersionSummary | null
  readonly pendingChanges: readonly PackageChange[]
  readonly recentVersions: readonly PackageVersionSummary[]
}

export interface PackageImportFile {
  readonly importId: string
  readonly fileName: string
  readonly fileSha256: string
  readonly fileSizeBytes: number
  readonly outcome: PackageImportOutcome
  readonly problemCode: string | null
  readonly version: string | null
  readonly sourceLabel: string
  readonly rowCount: number
  readonly errorCount: number
  readonly createdAtUtc: string
  readonly createdBy: string
}

export interface PackageImportReport {
  readonly importId: string
  readonly fileName: string
  readonly fileSha256: string
  readonly outcome: PackageImportOutcome
  readonly problemCode: string | null
  readonly detail: string
  readonly rowCount: number
  readonly acceptedCount: number
  readonly errors: readonly string[]
  readonly version: string | null
  readonly createdAtUtc: string
  readonly createdBy: string
}

export type PackageApprovalRead<T> =
  | { readonly type: 'ok'; readonly data: T }
  /** 401: the session ended — not the same as lacking the permission. */
  | { readonly type: 'unauthenticated' }
  /** 403: the permission is not granted here (registered, granted to no role). */
  | { readonly type: 'forbidden' }
  /** 404: no such kept file in this company (report only). */
  | { readonly type: 'missing' }
  /** 503: the provider is not wired in this host. */
  | { readonly type: 'unavailable' }
  /** Any other status, a network failure or an off-contract answer. */
  | { readonly type: 'failed' }

export type PackageApprovalCommand =
  | { readonly type: 'applied'; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly detail: string | null }
  | { readonly type: 'unknown' }

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export const PACKAGE_APPROVAL_STATE_PATH = `${PACKAGE_STANDARD_BASE_PATH}/approval-state`
export const PACKAGE_APPROVAL_PENDING_PATH = `${PACKAGE_STANDARD_BASE_PATH}/pending`
export const PACKAGE_IMPORT_FILES_PATH = `${PACKAGE_STANDARD_BASE_PATH}/imports`

export function packageImportReportPath(importId: string): string {
  return `${PACKAGE_IMPORT_FILES_PATH}/${encodeURIComponent(importId)}/report`
}

export function packageCustomerEntryPath(customerId: string, productId: string): string {
  return `${PACKAGE_STANDARD_BASE_PATH}/customers/${encodeURIComponent(customerId)}/products/${encodeURIComponent(productId)}`
}

export function packageApprovePath(version: string): string {
  return `${PACKAGE_STANDARD_BASE_PATH}/versions/${encodeURIComponent(version)}/approve`
}

export function packageRejectPath(version: string): string {
  return `${PACKAGE_STANDARD_BASE_PATH}/versions/${encodeURIComponent(version)}/reject`
}

function headers(companyId: string): Record<string, string> {
  return { ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': companyId }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== ''
}

function isOptionalString(value: unknown): value is string | null {
  return value === null || isNonEmptyString(value)
}

function isCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SHA256_HEX = /^[0-9a-f]{64}$/i
const VERSION = /^(0|[1-9]\d*)$/
const POSITIVE_VERSION = /^[1-9]\d*$/
const BUSINESS_DATE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value)
}

function isOptionalUuid(value: unknown): value is string | null {
  return value === null || isUuid(value)
}

function isBusinessDate(value: unknown): value is string {
  return typeof value === 'string' && BUSINESS_DATE.test(value)
}

function isOptionalPieces(value: unknown): value is number | null {
  return value === null || isPiecesPerPackage(value)
}

/** `ProductPackageStandardCustomerEntryV3`'s members. */
const CUSTOMER_ENTRY_KEYS = [
  'customerId', 'customerCode', 'customerName', 'productId', 'productCode', 'productName', 'piecesPerPackage',
] as const

/** `ProductPackageStandardChangeV3`'s members. */
const CHANGE_KEYS = [
  'customerId', 'customerName', 'productId', 'productCode', 'productName', 'piecesBefore', 'piecesAfter',
] as const

/** `ProductPackageStandardVersionSummaryV3`'s members. */
const SUMMARY_KEYS = [
  'version', 'state', 'sourceKind', 'sourceLabel', 'fileName', 'editedProductId', 'editedCustomerId', 'reason', 'entryCount',
  'customerEntryCount', 'createdAtUtc', 'createdBy', 'effectiveFrom', 'decidedBy', 'decidedAtUtc', 'decisionReason',
] as const

/** `ProductPackageStandardApprovalViewV3`'s members. */
const VIEW_KEYS = [
  'companyId', 'headVersion', 'today', 'effectiveVersion', 'effectiveFrom', 'customerNamesAvailable', 'customerEntries',
  'pending', 'pendingChanges', 'recentVersions',
] as const

/** `ProductPackageStandardImportFileV3`'s members. */
const IMPORT_FILE_KEYS = [
  'importId', 'fileName', 'fileSha256', 'fileSizeBytes', 'outcome', 'problemCode', 'version', 'sourceLabel', 'rowCount',
  'errorCount', 'createdAtUtc', 'createdBy',
] as const

/** `ProductPackageStandardImportFileListV3`'s members. */
const IMPORT_LIST_KEYS = ['companyId', 'items'] as const

/** `ProductPackageStandardImportReportV3`'s members. */
const REPORT_KEYS = [
  'importId', 'fileName', 'fileSha256', 'outcome', 'problemCode', 'detail', 'rowCount', 'acceptedCount', 'errors', 'version',
  'createdAtUtc', 'createdBy',
] as const

function decodeCustomerEntry(raw: unknown): PackageCustomerEntry | null {
  if (!isRecord(raw) || !hasExactKeys(raw, CUSTOMER_ENTRY_KEYS)) return null
  if (!isUuid(raw.customerId) || !isOptionalString(raw.customerCode) || !isOptionalString(raw.customerName)
    || !isUuid(raw.productId) || !isNonEmptyString(raw.productCode) || typeof raw.productName !== 'string'
    || !isPiecesPerPackage(raw.piecesPerPackage)) {
    return null
  }
  return {
    customerId: raw.customerId,
    customerCode: raw.customerCode,
    customerName: raw.customerName,
    productId: raw.productId,
    productCode: raw.productCode,
    productName: raw.productName,
    piecesPerPackage: raw.piecesPerPackage,
  }
}

function decodeChange(raw: unknown): PackageChange | null {
  if (!isRecord(raw) || !hasExactKeys(raw, CHANGE_KEYS)) return null
  if (!isOptionalUuid(raw.customerId) || !isOptionalString(raw.customerName) || !isUuid(raw.productId)
    || !isNonEmptyString(raw.productCode) || typeof raw.productName !== 'string' || !isOptionalPieces(raw.piecesBefore)
    || !isOptionalPieces(raw.piecesAfter) || raw.piecesBefore === raw.piecesAfter
    || (raw.customerId === null && raw.customerName !== null)) {
    return null
  }
  return {
    customerId: raw.customerId,
    customerName: raw.customerName,
    productId: raw.productId,
    productCode: raw.productCode,
    productName: raw.productName,
    piecesBefore: raw.piecesBefore,
    piecesAfter: raw.piecesAfter,
  }
}

export function decodePackageVersionSummary(raw: unknown): PackageVersionSummary | null {
  if (!isRecord(raw) || !hasExactKeys(raw, SUMMARY_KEYS)) return null
  const state = raw.state
  const kind = raw.sourceKind
  if (typeof raw.version !== 'string' || !POSITIVE_VERSION.test(raw.version)
    || !PACKAGE_VERSION_STATES.includes(state as PackageVersionState)
    || !PACKAGE_STANDARD_SOURCE_KINDS.includes(kind as PackageStandardSourceKind)
    || !isOptionalString(raw.sourceLabel) || !isOptionalString(raw.fileName) || !isOptionalUuid(raw.editedProductId)
    || !isOptionalUuid(raw.editedCustomerId) || !isNonEmptyString(raw.reason) || !isCount(raw.entryCount)
    || !isCount(raw.customerEntryCount) || !isNonEmptyString(raw.createdAtUtc) || !isNonEmptyString(raw.createdBy)
    || !(raw.effectiveFrom === null || isBusinessDate(raw.effectiveFrom)) || !isOptionalString(raw.decidedBy)
    || !isOptionalString(raw.decidedAtUtc) || !isOptionalString(raw.decisionReason)) {
    return null
  }
  // An import names its label and file; an edit names neither.
  if (kind === 'Import' ? raw.sourceLabel === null || raw.fileName === null : raw.sourceLabel !== null || raw.fileName !== null) {
    return null
  }
  // Only a decided version carries a decision; only an approved one carries an effective date.
  const decided = state === 'Approved' || state === 'Rejected'
  const decisionMembers = [raw.decidedBy, raw.decidedAtUtc, raw.decisionReason]
  if (decided ? decisionMembers.some((member) => member === null) : decisionMembers.some((member) => member !== null)) return null
  if ((state === 'Approved') !== (raw.effectiveFrom !== null)) return null
  return {
    version: raw.version,
    state: state as PackageVersionState,
    sourceKind: kind as PackageStandardSourceKind,
    sourceLabel: raw.sourceLabel,
    fileName: raw.fileName,
    editedProductId: raw.editedProductId,
    editedCustomerId: raw.editedCustomerId,
    reason: raw.reason,
    entryCount: raw.entryCount,
    customerEntryCount: raw.customerEntryCount,
    createdAtUtc: raw.createdAtUtc,
    createdBy: raw.createdBy,
    effectiveFrom: raw.effectiveFrom,
    decidedBy: raw.decidedBy,
    decidedAtUtc: raw.decidedAtUtc,
    decisionReason: raw.decisionReason,
  }
}

function decodeList<T>(raw: unknown, decode: (item: unknown) => T | null): T[] | null {
  if (!Array.isArray(raw)) return null
  const items: T[] = []
  for (const candidate of raw) {
    const item = decode(candidate)
    if (item === null) return null
    items.push(item)
  }
  return items
}

export function decodePackageApprovalView(raw: unknown, companyId: string): PackageApprovalView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_KEYS) || raw.companyId !== companyId || typeof raw.headVersion !== 'string'
    || !VERSION.test(raw.headVersion) || !isBusinessDate(raw.today)
    || !(raw.effectiveVersion === null || (typeof raw.effectiveVersion === 'string' && POSITIVE_VERSION.test(raw.effectiveVersion)))
    || !(raw.effectiveFrom === null || isBusinessDate(raw.effectiveFrom)) || typeof raw.customerNamesAvailable !== 'boolean') {
    return null
  }
  const customerEntries = decodeList(raw.customerEntries, decodeCustomerEntry)
  const pendingChanges = decodeList(raw.pendingChanges, decodeChange)
  const recentVersions = decodeList(raw.recentVersions, decodePackageVersionSummary)
  const pending = raw.pending === null ? null : decodePackageVersionSummary(raw.pending)
  if (customerEntries === null || pendingChanges === null || recentVersions === null
    || (raw.pending !== null && (pending === null || pending.state !== 'Pending'))) {
    return null
  }
  // Without a version in effect there is no effective date and no override; without a pending version there is no change.
  if (raw.effectiveVersion === null && (raw.effectiveFrom !== null || customerEntries.length !== 0)) return null
  if (pending === null && pendingChanges.length !== 0) return null
  const overrides = new Set<string>()
  for (const entry of customerEntries) {
    const key = `${entry.customerId.toLowerCase()}|${entry.productId.toLowerCase()}`
    if (overrides.has(key)) return null
    overrides.add(key)
  }
  return {
    headVersion: raw.headVersion,
    today: raw.today,
    effectiveVersion: raw.effectiveVersion,
    effectiveFrom: raw.effectiveFrom,
    customerNamesAvailable: raw.customerNamesAvailable,
    customerEntries,
    pending,
    pendingChanges,
    recentVersions,
  }
}

function decodeOutcomeMembers(raw: Record<string, unknown>): boolean {
  const outcome = raw.outcome
  if (!PACKAGE_IMPORT_OUTCOMES.includes(outcome as PackageImportOutcome)) return false
  // An accepted file names the version it proposed and no problem; a refused one names its problem and no version.
  return outcome === 'Accepted'
    ? raw.problemCode === null && typeof raw.version === 'string' && POSITIVE_VERSION.test(raw.version)
    : isNonEmptyString(raw.problemCode) && raw.version === null
}

function decodeImportFile(raw: unknown): PackageImportFile | null {
  if (!isRecord(raw) || !hasExactKeys(raw, IMPORT_FILE_KEYS)) return null
  if (!isUuid(raw.importId) || !isNonEmptyString(raw.fileName) || typeof raw.fileSha256 !== 'string'
    || !SHA256_HEX.test(raw.fileSha256) || !isCount(raw.fileSizeBytes) || raw.fileSizeBytes === 0 || !decodeOutcomeMembers(raw)
    || !isNonEmptyString(raw.sourceLabel) || !isCount(raw.rowCount) || !isCount(raw.errorCount)
    || !isNonEmptyString(raw.createdAtUtc) || !isNonEmptyString(raw.createdBy)) {
    return null
  }
  return {
    importId: raw.importId,
    fileName: raw.fileName,
    fileSha256: raw.fileSha256,
    fileSizeBytes: raw.fileSizeBytes,
    outcome: raw.outcome as PackageImportOutcome,
    problemCode: raw.problemCode as string | null,
    version: raw.version as string | null,
    sourceLabel: raw.sourceLabel,
    rowCount: raw.rowCount,
    errorCount: raw.errorCount,
    createdAtUtc: raw.createdAtUtc,
    createdBy: raw.createdBy,
  }
}

export function decodePackageImportFiles(raw: unknown, companyId: string): readonly PackageImportFile[] | null {
  if (!isRecord(raw) || !hasExactKeys(raw, IMPORT_LIST_KEYS) || raw.companyId !== companyId) return null
  const items = decodeList(raw.items, decodeImportFile)
  if (items === null) return null
  const ids = new Set(items.map((item) => item.importId.toLowerCase()))
  return ids.size === items.length ? items : null
}

export function decodePackageImportReport(raw: unknown, importId: string): PackageImportReport | null {
  if (!isRecord(raw) || !hasExactKeys(raw, REPORT_KEYS)) return null
  if (!isUuid(raw.importId) || raw.importId.toLowerCase() !== importId.toLowerCase() || !isNonEmptyString(raw.fileName)
    || typeof raw.fileSha256 !== 'string' || !SHA256_HEX.test(raw.fileSha256) || !decodeOutcomeMembers(raw)
    || !isNonEmptyString(raw.detail) || !isCount(raw.rowCount) || !isCount(raw.acceptedCount)
    || !isNonEmptyString(raw.createdAtUtc) || !isNonEmptyString(raw.createdBy)) {
    return null
  }
  const errors = decodeList(raw.errors, (item) => (isNonEmptyString(item) ? item : null))
  if (errors === null) return null
  return {
    importId: raw.importId,
    fileName: raw.fileName,
    fileSha256: raw.fileSha256,
    outcome: raw.outcome as PackageImportOutcome,
    problemCode: raw.problemCode as string | null,
    detail: raw.detail,
    rowCount: raw.rowCount,
    acceptedCount: raw.acceptedCount,
    errors,
    version: raw.version as string | null,
    createdAtUtc: raw.createdAtUtc,
    createdBy: raw.createdBy,
  }
}

async function read<T>(
  path: string,
  companyId: string,
  decode: (raw: unknown) => T | null,
  signal?: AbortSignal,
): Promise<PackageApprovalRead<T>> {
  try {
    const response = await fetch(`${API_BASE}${path}`, { headers: headers(companyId), signal })
    if (response.status === 401) return { type: 'unauthenticated' }
    if (response.status === 403) return { type: 'forbidden' }
    if (response.status === 404) return { type: 'missing' }
    if (response.status === 503) return { type: 'unavailable' }
    if (response.status !== 200) return { type: 'failed' }
    const data = decode(await response.json())
    return data === null ? { type: 'failed' } : { type: 'ok', data }
  } catch {
    return { type: 'failed' }
  }
}

export function fetchPackageApprovalState(companyId: string, signal?: AbortSignal) {
  return read(PACKAGE_APPROVAL_STATE_PATH, companyId, (raw) => decodePackageApprovalView(raw, companyId), signal)
}

export function fetchPackageApprovalForApprover(companyId: string, signal?: AbortSignal) {
  return read(PACKAGE_APPROVAL_PENDING_PATH, companyId, (raw) => decodePackageApprovalView(raw, companyId), signal)
}

export function fetchPackageImportFiles(companyId: string, signal?: AbortSignal) {
  return read(PACKAGE_IMPORT_FILES_PATH, companyId, (raw) => decodePackageImportFiles(raw, companyId), signal)
}

export function fetchPackageImportReport(companyId: string, importId: string, signal?: AbortSignal) {
  return read(packageImportReportPath(importId), companyId, (raw) => decodePackageImportReport(raw, importId), signal)
}

async function send(
  method: 'POST' | 'PUT',
  path: string,
  companyId: string,
  idempotencyKey: string,
  body: unknown,
): Promise<PackageApprovalCommand> {
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
  if (response.status < 400) return { type: 'unknown' }
  let raw: unknown = null
  try {
    raw = await response.json()
  } catch {
    raw = null
  }
  const code = isRecord(raw) && typeof raw.code === 'string' && raw.code.length > 0 ? raw.code : null
  const detail = isRecord(raw) && isNonEmptyString(raw.detail) ? raw.detail : null
  // The Cari owner's refusal to answer is a definite, unstored answer (nothing was written); any other 5xx is unknown and the key
  // is kept for a retry.
  if (response.status >= 500) {
    return response.status === 503 && code === PACKAGE_APPROVAL_PROBLEM.customerProviderUnavailable
      ? { type: 'refused', status: response.status, code, detail }
      : { type: 'unknown' }
  }
  return { type: 'refused', status: response.status, code, detail }
}

export interface SetPackageCustomerEntryBody {
  readonly expectedVersion: string
  /** Null removes the customer's override. */
  readonly piecesPerPackage: number | null
  readonly reason: string
}

export function setPackageCustomerEntry(
  companyId: string,
  customerId: string,
  productId: string,
  idempotencyKey: string,
  body: SetPackageCustomerEntryBody,
): Promise<PackageApprovalCommand> {
  return send('PUT', packageCustomerEntryPath(customerId, productId), companyId, idempotencyKey, body)
}

export interface ApprovePackageVersionBody {
  /** `yyyy-MM-dd`, today's business day or later (the server checks). */
  readonly effectiveFrom: string
  readonly reason: string
}

export function approvePackageVersion(
  companyId: string,
  version: string,
  idempotencyKey: string,
  body: ApprovePackageVersionBody,
): Promise<PackageApprovalCommand> {
  return send('POST', packageApprovePath(version), companyId, idempotencyKey, body)
}

export interface RejectPackageVersionBody {
  readonly reason: string
}

export function rejectPackageVersion(
  companyId: string,
  version: string,
  idempotencyKey: string,
  body: RejectPackageVersionBody,
): Promise<PackageApprovalCommand> {
  return send('POST', packageRejectPath(version), companyId, idempotencyKey, body)
}

export function isCustomerId(text: string): boolean {
  return UUID.test(text.trim())
}

export function isEffectiveDate(text: string): boolean {
  return BUSINESS_DATE.test(text)
}
