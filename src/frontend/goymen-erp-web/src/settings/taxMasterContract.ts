/**
 * MOD-03 tax master wire contract (EK19 SAT-D133…D139; `SettingsTaxMasterEndpoints`, `SettingsTaxMasterV1.cs`; pinned to
 * `contracts/openapi/goymen-erp-v1.json` by ./taxMasterOpenapiContract.test.ts):
 *
 *   GET  /settings/tax-master                                                -> TaxMasterViewV1
 *   POST /settings/tax-master/item-type-defaults/{itemType}                  -> 204 (SAT-D139)
 *   POST /settings/tax-master/item-type-defaults/records/{id}/withdrawal     -> 204
 *   POST /settings/tax-master/code-lists/{listKind}/versions                 -> 204
 *   POST /settings/tax-master/code-lists/{listKind}/versions/{v}/withdrawal  -> 204
 *   POST /settings/tax-master/withholding-threshold                          -> 204 (SAT-D135, VUK 232)
 *   POST /settings/tax-master/withholding-threshold/records/{id}/withdrawal  -> 204
 *
 * Every decoder fails CLOSED (one bad row invalidates the whole answer); rates stay the lossless decimal TEXT the server
 * sends and the screen never computes a tax. A missing value is "kaynak yok", never 0.
 */

import { isDateText, isUuid } from './discountLimitContract'

export const TAX_MASTER_PATH = '/settings/tax-master'
export const ITEM_TYPES = ['Steel', 'Scrap', 'Service'] as const
export type ItemType = (typeof ITEM_TYPES)[number]

export function itemTypeDefaultPath(itemType: ItemType): string {
  return `${TAX_MASTER_PATH}/item-type-defaults/${itemType}`
}

export function itemTypeDefaultWithdrawalPath(recordId: string): string {
  return `${TAX_MASTER_PATH}/item-type-defaults/records/${encodeURIComponent(recordId)}/withdrawal`
}

export const WITHHOLDING_THRESHOLD_PATH = `${TAX_MASTER_PATH}/withholding-threshold`

export function thresholdWithdrawalPath(recordId: string): string {
  return `${WITHHOLDING_THRESHOLD_PATH}/records/${encodeURIComponent(recordId)}/withdrawal`
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
  exemptionCodeUnknown: 'settings_tax_master_exemption_code_unknown',
  withholdingCodeUnknown: 'settings_tax_master_withholding_code_unknown',
  effectiveFromPast: 'settings_tax_master_effective_from_past',
  recordInEffect: 'settings_tax_master_record_in_effect',
  recordWithdrawn: 'settings_tax_master_record_withdrawn',
  providerUnavailable: 'settings_provider_unavailable',
} as const

export const TAX_INPUT = {
  listCodeMaximumLength: 16,
  listEntryNameMaximumLength: 200,
  listMaximumEntries: 500,
  reasonMaximumLength: 4000,
} as const

export const CODE_LIST_KINDS = ['Exemption', 'Withholding'] as const
export type CodeListKind = (typeof CODE_LIST_KINDS)[number]
export type DefaultSource = 'Catalog' | 'Record'

/** SAT-D139: one default-tax record of an item type (the reviewed catalog entry or a Settings record). */
export interface TaxDefaultRecord {
  readonly recordId: string
  readonly source: DefaultSource
  readonly recordVersion: string
  readonly effectiveFrom: string
  readonly vatApplies: boolean
  readonly vatPercent: string | null
  readonly exemptionCode: string | null
  readonly withholdingApplies: boolean
  readonly withholdingPercent: string | null
  readonly withholdingCode: string | null
  readonly recordedBy: string | null
  readonly recordedAtUtc: string | null
  readonly reason: string
}

export interface TaxDefaultSeries {
  readonly itemType: ItemType
  readonly version: string
  readonly inForce: TaxDefaultRecord | null
  readonly scheduled: readonly TaxDefaultRecord[]
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

/** SAT-D93 VUK 232 withholding amount threshold record: lossless amount text in an entered ISO 4217 currency. */
export interface TaxThresholdRecord {
  readonly recordId: string
  readonly recordVersion: string
  readonly effectiveFrom: string
  readonly amount: string
  readonly currencyCode: string
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface TaxThreshold {
  readonly version: string
  readonly inForce: TaxThresholdRecord | null
  readonly scheduled: readonly TaxThresholdRecord[]
}

export interface TaxMaster {
  readonly companyId: string
  readonly businessDate: string
  readonly itemTypeDefaults: readonly TaxDefaultSeries[]
  readonly codeLists: readonly TaxCodeList[]
  readonly withholdingThreshold: TaxThreshold
}

export interface RecordThresholdBody {
  readonly expectedVersion: string
  readonly amount: string
  readonly currencyCode: string
  readonly reason: string
  readonly effectiveFrom?: string
}

/** SAT-D139: a new default record of an item type (rates as text; codes optional — empty stays "kaynak yok"). */
export interface RecordDefaultBody {
  readonly expectedVersion: string
  readonly vatApplies: boolean
  readonly vatPercent: string | null
  readonly exemptionCode: string | null
  readonly withholdingApplies: boolean
  readonly withholdingPercent: string | null
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
const LIST_CODE = /^[A-Z0-9]{1,16}$/
const AMOUNT = /^(0|[1-9]\d{0,14})(\.\d{1,4})?$/
const CURRENCY = /^[A-Z]{3}$/

/** Above 0, at most 15 integer digits and 4 decimals, as text (never a JS number on the wire). */
export function isAmountText(value: unknown): value is string {
  return typeof value === 'string' && AMOUNT.test(value) && Number(value) > 0
}

export function isCurrencyCode(value: string): boolean {
  return CURRENCY.test(value)
}

function decodeThresholdRecord(raw: unknown): TaxThresholdRecord | null {
  if (!isRecord(raw) || !isUuid(raw.recordId) || !isVersion(raw.recordVersion) || !isDateText(raw.effectiveFrom)
    || !isAmountText(raw.amount) || typeof raw.currencyCode !== 'string' || !isCurrencyCode(raw.currencyCode)
    || !isText(raw.recordedBy) || typeof raw.recordedAtUtc !== 'string' || typeof raw.reason !== 'string') return null
  return {
    recordId: raw.recordId,
    recordVersion: raw.recordVersion,
    effectiveFrom: raw.effectiveFrom,
    amount: raw.amount,
    currencyCode: raw.currencyCode,
    recordedBy: raw.recordedBy,
    recordedAtUtc: raw.recordedAtUtc,
    reason: raw.reason,
  }
}

function decodeThreshold(raw: unknown): TaxThreshold | null {
  if (!isRecord(raw) || !isVersion(raw.version) || !Array.isArray(raw.scheduled)) return null
  const inForce = raw.inForce === null ? null : decodeThresholdRecord(raw.inForce)
  if (raw.inForce !== null && inForce === null) return null
  const scheduled = raw.scheduled.map(decodeThresholdRecord)
  if (scheduled.some((record) => record === null)) return null
  return { version: raw.version, inForce, scheduled: scheduled as TaxThresholdRecord[] }
}

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

/** A VAT rate that applies: above 0 up to 100 (zero VAT is only "KDV yok" with its exemption code gate). */
export function isVatPercentText(value: unknown): value is string {
  return isRatePercentText(value) && Number(value) > 0
}

export function normalizeTaxCode(value: string): string {
  return value.trim().toLocaleUpperCase('en-US')
}

export function isListCode(value: string): boolean {
  return LIST_CODE.test(value)
}

function decodeDefault(raw: unknown): TaxDefaultRecord | null {
  if (!isRecord(raw) || !isUuid(raw.recordId) || (raw.source !== 'Catalog' && raw.source !== 'Record')
    || !isVersion(raw.recordVersion) || !isDateText(raw.effectiveFrom) || typeof raw.vatApplies !== 'boolean'
    || typeof raw.withholdingApplies !== 'boolean' || typeof raw.reason !== 'string') return null
  const optionalCode = (value: unknown) => value === null || (typeof value === 'string' && isListCode(value))
  if (raw.vatApplies ? !isVatPercentText(raw.vatPercent) || raw.exemptionCode !== null
    : raw.vatPercent !== null || !optionalCode(raw.exemptionCode)) return null
  if (raw.withholdingApplies
    ? !raw.vatApplies || !isRatePercentText(raw.withholdingPercent) || Number(raw.withholdingPercent) <= 0
      || !optionalCode(raw.withholdingCode)
    : raw.withholdingPercent !== null || raw.withholdingCode !== null) return null
  const isCatalog = raw.source === 'Catalog'
  if (isCatalog ? raw.recordedBy !== null || raw.recordedAtUtc !== null
    : !isText(raw.recordedBy) || typeof raw.recordedAtUtc !== 'string') return null
  return {
    recordId: raw.recordId,
    source: raw.source,
    recordVersion: raw.recordVersion,
    effectiveFrom: raw.effectiveFrom,
    vatApplies: raw.vatApplies,
    vatPercent: (raw.vatPercent as string | null) ?? null,
    exemptionCode: (raw.exemptionCode as string | null) ?? null,
    withholdingApplies: raw.withholdingApplies,
    withholdingPercent: (raw.withholdingPercent as string | null) ?? null,
    withholdingCode: (raw.withholdingCode as string | null) ?? null,
    recordedBy: (raw.recordedBy as string | null) ?? null,
    recordedAtUtc: (raw.recordedAtUtc as string | null) ?? null,
    reason: raw.reason,
  }
}

function decodeSeries(raw: unknown): TaxDefaultSeries | null {
  if (!isRecord(raw) || !ITEM_TYPES.includes(raw.itemType as ItemType) || !isVersion(raw.version)
    || !Array.isArray(raw.scheduled)) return null
  const inForce = raw.inForce === null ? null : decodeDefault(raw.inForce)
  if (raw.inForce !== null && inForce === null) return null
  const scheduled = raw.scheduled.map(decodeDefault)
  if (scheduled.some((record) => record === null)) return null
  return { itemType: raw.itemType as ItemType, version: raw.version, inForce, scheduled: scheduled as TaxDefaultRecord[] }
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
  if (!isRecord(raw) || raw.companyId !== companyId || !isDateText(raw.businessDate) || !Array.isArray(raw.itemTypeDefaults)
    || !Array.isArray(raw.codeLists)) return null
  const series = raw.itemTypeDefaults.map(decodeSeries)
  const lists = raw.codeLists.map(decodeList)
  if (series.some((item) => item === null) || lists.some((item) => item === null)) return null
  const types = (series as TaxDefaultSeries[]).map((item) => item.itemType)
  if (types.length !== ITEM_TYPES.length || ITEM_TYPES.some((type) => !types.includes(type))) return null
  const threshold = decodeThreshold(raw.withholdingThreshold)
  if (threshold === null) return null
  const kinds = (lists as TaxCodeList[]).map((list) => list.kind)
  if (kinds.length !== CODE_LIST_KINDS.length || CODE_LIST_KINDS.some((kind) => !kinds.includes(kind))) return null
  return {
    companyId,
    businessDate: raw.businessDate,
    itemTypeDefaults: series as TaxDefaultSeries[],
    codeLists: lists as TaxCodeList[],
    withholdingThreshold: threshold,
  }
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
