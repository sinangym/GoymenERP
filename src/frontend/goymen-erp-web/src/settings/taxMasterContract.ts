/**
 * MOD-03 tax master wire contract (SAT-D73/D74/D82/D83; `SettingsTaxMasterEndpoints`, `SettingsTaxMasterV1.cs`; pinned to
 * `contracts/openapi/goymen-erp-v1.json` by ./taxMasterOpenapiContract.test.ts):
 *
 *   GET  /settings/tax-master                                                -> TaxMasterViewV1
 *   POST /settings/tax-master/categories                                     -> 201 TaxCategoryCreatedV1
 *   POST /settings/tax-master/vat-classes/{classId}/rates                    -> 204
 *   POST /settings/tax-master/withholding-classes/{classId}/rates            -> 204
 *   POST /settings/tax-master/rate-records/{recordId}/withdrawal             -> 204
 *   POST /settings/tax-master/code-lists/{listKind}/versions                 -> 204
 *   POST /settings/tax-master/code-lists/{listKind}/versions/{v}/withdrawal  -> 204
 *
 * Every decoder fails CLOSED (one bad row invalidates the whole answer); rates stay the lossless decimal TEXT the server
 * sends and the screen never computes a tax. A missing value is "kaynak yok", never 0.
 */

import { isDateText, isUuid } from './discountLimitContract'

export const TAX_MASTER_PATH = '/settings/tax-master'
export const TAX_CATEGORIES_PATH = `${TAX_MASTER_PATH}/categories`

export function vatRatePath(classId: string): string {
  return `${TAX_MASTER_PATH}/vat-classes/${encodeURIComponent(classId)}/rates`
}

export function withholdingRatePath(classId: string): string {
  return `${TAX_MASTER_PATH}/withholding-classes/${encodeURIComponent(classId)}/rates`
}

export function rateWithdrawalPath(recordId: string): string {
  return `${TAX_MASTER_PATH}/rate-records/${encodeURIComponent(recordId)}/withdrawal`
}

export function codeListPath(kind: CodeListKind): string {
  return `${TAX_MASTER_PATH}/code-lists/${kind}/versions`
}

export function codeListWithdrawalPath(kind: CodeListKind, listVersion: string): string {
  return `${codeListPath(kind)}/${encodeURIComponent(listVersion)}/withdrawal`
}

export const TAX_PROBLEM = {
  requestInvalid: 'settings_tax_master_request_invalid',
  commandMetadataInvalid: 'settings_tax_master_command_metadata_invalid',
  reasonRequired: 'settings_tax_master_reason_required',
  reasonTooLong: 'settings_tax_master_reason_too_long',
  idempotencyConflict: 'settings_tax_master_idempotency_conflict',
  versionConflict: 'settings_tax_master_version_conflict',
  categoryCodeTaken: 'settings_tax_master_category_code_taken',
  categoryNameTaken: 'settings_tax_master_category_name_taken',
  categoryKindMismatch: 'settings_tax_master_category_kind_mismatch',
  buyerCategoryNotFound: 'settings_tax_master_buyer_category_not_found',
  withholdingCodeUnknown: 'settings_tax_master_withholding_code_unknown',
  effectiveFromPast: 'settings_tax_master_effective_from_past',
  recordInEffect: 'settings_tax_master_record_in_effect',
  recordWithdrawn: 'settings_tax_master_record_withdrawn',
  providerUnavailable: 'settings_provider_unavailable',
} as const

export const TAX_INPUT = {
  categoryCodeMaximumLength: 32,
  categoryNameMaximumLength: 100,
  listCodeMaximumLength: 16,
  listEntryNameMaximumLength: 200,
  listMaximumEntries: 500,
  reasonMaximumLength: 4000,
} as const

export const CATEGORY_KINDS = ['VatClass', 'WithholdingClass', 'BuyerCategory'] as const
export type CategoryKind = (typeof CATEGORY_KINDS)[number]
export const CODE_LIST_KINDS = ['Exemption', 'Withholding'] as const
export type CodeListKind = (typeof CODE_LIST_KINDS)[number]
export type RateKind = 'Vat' | 'Withholding'

export interface TaxCategory {
  readonly id: string
  readonly kind: CategoryKind
  readonly code: string
  readonly name: string
  readonly version: string
}

export interface TaxRateRecord {
  readonly recordId: string
  readonly recordVersion: string
  readonly effectiveFrom: string
  readonly applies: boolean
  readonly ratePercent: string | null
  readonly withholdingCode: string | null
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface TaxRateSeries {
  readonly kind: RateKind
  readonly classId: string
  readonly buyerCategoryId: string | null
  readonly inForce: TaxRateRecord | null
  readonly scheduled: readonly TaxRateRecord[]
}

export interface TaxCodeListEntry {
  readonly code: string
  readonly name: string
}

export interface TaxCodeListVersion {
  readonly listVersion: string
  readonly effectiveFrom: string
  readonly entries: readonly TaxCodeListEntry[]
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface TaxCodeList {
  readonly kind: CodeListKind
  readonly version: string
  readonly inForce: TaxCodeListVersion | null
  readonly scheduled: readonly TaxCodeListVersion[]
}

export interface TaxMaster {
  readonly companyId: string
  readonly businessDate: string
  readonly categories: readonly TaxCategory[]
  readonly rateSeries: readonly TaxRateSeries[]
  readonly codeLists: readonly TaxCodeList[]
}

export interface CreateCategoryBody {
  readonly kind: CategoryKind
  readonly code: string
  readonly name: string
  readonly reason: string
}

export interface RecordVatRateBody {
  readonly expectedVersion: string
  readonly ratePercent: string
  readonly reason: string
  readonly effectiveFrom?: string
}

export interface RecordWithholdingRateBody {
  readonly expectedVersion: string
  readonly buyerCategoryId: string
  readonly applies: boolean
  readonly ratePercent: string | null
  readonly withholdingCode: string | null
  readonly reason: string
  readonly effectiveFrom?: string
}

export interface WithdrawBody {
  readonly expectedVersion: string
  readonly reason: string
}

export interface PublishCodeListBody {
  readonly expectedVersion: string
  readonly entries: readonly TaxCodeListEntry[]
  readonly reason: string
  readonly effectiveFrom?: string
}

const VERSION = /^(0|[1-9]\d*)$/
const PERCENT = /^(0|[1-9]\d{0,2})(\.\d{1,4})?$/
const CATEGORY_CODE = /^[A-Z0-9][A-Z0-9_-]{0,31}$/
const LIST_CODE = /^[A-Z0-9]{1,16}$/

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isVersion(value: unknown): value is string {
  return typeof value === 'string' && VERSION.test(value)
}

/** 0…100 with at most 4 decimals, as text (never a JS number on the wire). */
export function isRatePercentText(value: unknown): value is string {
  return typeof value === 'string' && PERCENT.test(value) && Number(value) >= 0 && Number(value) <= 100
}

export function normalizeTaxCode(value: string): string {
  return value.trim().toLocaleUpperCase('en-US')
}

export function isCategoryCode(value: string): boolean {
  return CATEGORY_CODE.test(value)
}

export function isListCode(value: string): boolean {
  return LIST_CODE.test(value)
}

function decodeCategory(raw: unknown): TaxCategory | null {
  if (!isRecord(raw) || !isUuid(raw.id) || !CATEGORY_KINDS.includes(raw.kind as CategoryKind) || typeof raw.code !== 'string'
    || !isCategoryCode(raw.code) || !isText(raw.name) || !isVersion(raw.version)) return null
  return { id: raw.id, kind: raw.kind as CategoryKind, code: raw.code, name: raw.name, version: raw.version }
}

function decodeRecord(raw: unknown, kind: RateKind): TaxRateRecord | null {
  if (!isRecord(raw) || !isUuid(raw.recordId) || !isVersion(raw.recordVersion) || !isDateText(raw.effectiveFrom)
    || typeof raw.applies !== 'boolean' || !isText(raw.recordedBy) || typeof raw.recordedAtUtc !== 'string'
    || typeof raw.reason !== 'string') return null
  const rate = raw.ratePercent
  const code = raw.withholdingCode
  if (kind === 'Vat' && (!raw.applies || !isRatePercentText(rate) || code !== null)) return null
  if (kind === 'Withholding') {
    if (raw.applies && (!isRatePercentText(rate) || typeof code !== 'string' || !isListCode(code))) return null
    if (!raw.applies && (rate !== null || code !== null)) return null
  }
  return {
    recordId: raw.recordId,
    recordVersion: raw.recordVersion,
    effectiveFrom: raw.effectiveFrom,
    applies: raw.applies,
    ratePercent: (rate as string | null) ?? null,
    withholdingCode: (code as string | null) ?? null,
    recordedBy: raw.recordedBy,
    recordedAtUtc: raw.recordedAtUtc,
    reason: raw.reason,
  }
}

function decodeSeries(raw: unknown): TaxRateSeries | null {
  if (!isRecord(raw) || (raw.kind !== 'Vat' && raw.kind !== 'Withholding') || !isUuid(raw.classId)) return null
  const kind = raw.kind
  if (kind === 'Vat' ? raw.buyerCategoryId !== null : !isUuid(raw.buyerCategoryId)) return null
  const inForce = raw.inForce === null ? null : decodeRecord(raw.inForce, kind)
  if (raw.inForce !== null && inForce === null) return null
  if (!Array.isArray(raw.scheduled)) return null
  const scheduled = raw.scheduled.map((record) => decodeRecord(record, kind))
  if (scheduled.some((record) => record === null)) return null
  return {
    kind,
    classId: raw.classId,
    buyerCategoryId: (raw.buyerCategoryId as string | null) ?? null,
    inForce,
    scheduled: scheduled as TaxRateRecord[],
  }
}

function decodeListVersion(raw: unknown): TaxCodeListVersion | null {
  if (!isRecord(raw) || !isVersion(raw.listVersion) || !isDateText(raw.effectiveFrom) || !Array.isArray(raw.entries)
    || !isText(raw.recordedBy) || typeof raw.recordedAtUtc !== 'string' || typeof raw.reason !== 'string') return null
  const entries = raw.entries.map((entry) =>
    isRecord(entry) && typeof entry.code === 'string' && isListCode(entry.code) && isText(entry.name)
      ? { code: entry.code, name: entry.name }
      : null,
  )
  if (entries.some((entry) => entry === null)) return null
  return {
    listVersion: raw.listVersion,
    effectiveFrom: raw.effectiveFrom,
    entries: entries as TaxCodeListEntry[],
    recordedBy: raw.recordedBy,
    recordedAtUtc: raw.recordedAtUtc,
    reason: raw.reason,
  }
}

function decodeList(raw: unknown): TaxCodeList | null {
  if (!isRecord(raw) || !CODE_LIST_KINDS.includes(raw.kind as CodeListKind) || !isVersion(raw.version)
    || !Array.isArray(raw.scheduled)) return null
  const inForce = raw.inForce === null ? null : decodeListVersion(raw.inForce)
  if (raw.inForce !== null && inForce === null) return null
  const scheduled = raw.scheduled.map(decodeListVersion)
  if (scheduled.some((version) => version === null)) return null
  return { kind: raw.kind as CodeListKind, version: raw.version, inForce, scheduled: scheduled as TaxCodeListVersion[] }
}

/** The whole master, or null when any part is off-contract or the company is not the one asked for. */
export function decodeTaxMaster(raw: unknown, companyId: string): TaxMaster | null {
  if (!isRecord(raw) || raw.companyId !== companyId || !isDateText(raw.businessDate) || !Array.isArray(raw.categories)
    || !Array.isArray(raw.rateSeries) || !Array.isArray(raw.codeLists)) return null
  const categories = raw.categories.map(decodeCategory)
  const series = raw.rateSeries.map(decodeSeries)
  const lists = raw.codeLists.map(decodeList)
  if (categories.some((item) => item === null) || series.some((item) => item === null) || lists.some((item) => item === null)) {
    return null
  }

  const kinds = (lists as TaxCodeList[]).map((list) => list.kind)
  if (kinds.length !== CODE_LIST_KINDS.length || CODE_LIST_KINDS.some((kind) => !kinds.includes(kind))) return null
  return {
    companyId,
    businessDate: raw.businessDate,
    categories: categories as TaxCategory[],
    rateSeries: series as TaxRateSeries[],
    codeLists: lists as TaxCodeList[],
  }
}

export function decodeCategoryCreated(raw: unknown): { readonly id: string; readonly version: string } | null {
  return isRecord(raw) && isUuid(raw.id) && isVersion(raw.version) ? { id: raw.id, version: raw.version } : null
}

/** "KOD;Ad" per line → entries (codes normalized); null when a line is malformed or a code repeats. */
export function parseCodeListText(text: string): TaxCodeListEntry[] | null {
  const lines = text.split('\n').map((line) => line.trim()).filter((line) => line.length > 0)
  if (lines.length === 0 || lines.length > TAX_INPUT.listMaximumEntries) return null
  const entries: TaxCodeListEntry[] = []
  for (const line of lines) {
    const separator = line.indexOf(';')
    if (separator <= 0) return null
    const code = normalizeTaxCode(line.slice(0, separator))
    const name = line.slice(separator + 1).trim()
    if (!isListCode(code) || name.length === 0 || name.length > TAX_INPUT.listEntryNameMaximumLength) return null
    entries.push({ code, name })
  }
  return new Set(entries.map((entry) => entry.code)).size === entries.length ? entries : null
}
