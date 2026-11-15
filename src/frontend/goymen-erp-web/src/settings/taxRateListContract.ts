/**
 * F-029 S9 (SAT-D181, EK26) rate list wire contract — the company's VAT rate and withholding ratio lists
 * (`SettingsTaxMasterEndpoints`, `SettingsTaxRateListAdministrationV1.cs`):
 *
 *   GET  /settings/tax-master/rate-lists                                       -> TaxRateListsViewV1
 *   POST /settings/tax-master/rate-lists/{listKind}/versions                   -> 204 (whole list version)
 *   POST /settings/tax-master/rate-lists/{listKind}/versions/{v}/withdrawal    -> 204 (not yet effective only)
 *
 * The lists start EMPTY (SAT-D148: nothing is seeded) — an empty list is "kaynak yok", never a guessed rate. The decoder
 * fails CLOSED (one bad entry invalidates the whole answer); a percent stays the lossless decimal TEXT the server sends and
 * the screen never computes a tax. Kept in sync with the OpenAPI schemas `TaxRateListsViewV1`, `TaxRateListViewV1`,
 * `TaxRateListVersionViewV1` and `TaxRateListEntryWireViewV1` (the canonical document is regenerated with the backend).
 */

import { isDateText, isUuid } from './discountLimitContract'
import { TAX_INPUT, TAX_MASTER_PATH, isListCode, normalizeTaxCode } from './taxMasterContract'

export const RATE_LISTS_PATH = `${TAX_MASTER_PATH}/rate-lists`

/** `TaxRateListKindsV1`: KDV oranı listesi and tevkifat oranı listesi. */
export const RATE_LIST_KINDS = ['VatRate', 'WithholdingRatio'] as const
export type RateListKind = (typeof RATE_LIST_KINDS)[number]

export function rateListVersionsPath(kind: RateListKind): string {
  return `${RATE_LISTS_PATH}/${kind}/versions`
}

export function rateListWithdrawalPath(kind: RateListKind, listVersion: string): string {
  return `${rateListVersionsPath(kind)}/${encodeURIComponent(listVersion)}/withdrawal`
}

/** `TaxRateListInputLimitsV1` (transport bounds, not tax policy); the name bound is the code lists'. */
export const RATE_LIST_INPUT = {
  codeMaximumLength: 16,
  maximumEntries: 100,
  nameMaximumLength: TAX_INPUT.listEntryNameMaximumLength,
} as const

export interface RateListEntry {
  readonly code: string
  readonly name: string
  readonly percent: string
  /** Withholding ratio only: the GİB withholding code it is invoiced under (null = not mapped yet). */
  readonly taxCode: string | null
}

export interface RateListVersion {
  readonly listVersion: string
  readonly effectiveFrom: string
  readonly entries: readonly RateListEntry[]
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface RateList {
  readonly kind: RateListKind
  /** The list's edit fence ("0" while the company has entered none). */
  readonly version: string
  readonly inForce: RateListVersion | null
  readonly scheduled: readonly RateListVersion[]
}

export interface RateLists {
  readonly companyId: string
  readonly businessDate: string
  readonly lists: readonly RateList[]
}

export interface PublishRateListBody {
  readonly expectedVersion: string
  readonly entries: readonly RateListEntry[]
  readonly reason: string
  readonly effectiveFrom?: string
}

export interface WithdrawRateListBody {
  readonly expectedVersion: string
  readonly reason: string
}

/** The wire members of each part (the OpenAPI schemas'): exactly these, no more and no fewer. */
const KEYS = {
  view: ['companyId', 'businessDate', 'lists'],
  list: ['kind', 'version', 'inForce', 'scheduled'],
  version: ['listVersion', 'effectiveFrom', 'entries', 'recordedBy', 'recordedAtUtc', 'reason'],
  entry: ['code', 'name', 'percent', 'taxCode'],
} as const

const MAX_INT64 = 9223372036854775807n
const LIST_FENCE = /^(0|[1-9]\d*)$/
const POSITIVE = /^[1-9]\d*$/
const RATE_CODE = /^[A-Z0-9/]{1,16}$/
const PERCENT = /^(0|[1-9]\d{0,2})(\.\d{1,4})?$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isInt64Text(value: unknown, pattern: RegExp): value is string {
  return typeof value === 'string' && pattern.test(value) && BigInt(value) <= MAX_INT64
}

function isInstant(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && INSTANT.test(value) && !Number.isNaN(Date.parse(value))
}

export function isRateListKind(value: unknown): value is RateListKind {
  return typeof value === 'string' && (RATE_LIST_KINDS as readonly string[]).includes(value)
}

/** A rate code: upper-case ASCII letters, digits and '/', 1…16 (e.g. "20", "5/10"). */
export function isRateCode(value: string): boolean {
  return RATE_CODE.test(value)
}

/**
 * A percent of the given kind as lossless text (at most 4 decimals): a VAT rate 0…100, a withholding ratio above 0 up to
 * 100. A format check only; the server decides and the screen never computes with it.
 */
export function isRateListPercent(kind: RateListKind, value: unknown): value is string {
  if (typeof value !== 'string' || !PERCENT.test(value)) return false
  const [whole, fraction = ''] = value.split('.')
  const isZero = /^0+$/.test(whole) && /^0*$/.test(fraction)
  const isAboveHundred = Number(whole) > 100 || (Number(whole) === 100 && !/^0*$/.test(fraction))
  return !isAboveHundred && (kind === 'VatRate' || !isZero)
}

function decodeEntry(kind: RateListKind, raw: unknown): RateListEntry | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.entry) || typeof raw.code !== 'string' || !isRateCode(raw.code)
    || !isText(raw.name) || raw.name.length > RATE_LIST_INPUT.nameMaximumLength || !isRateListPercent(kind, raw.percent)) {
    return null
  }
  // A VAT rate never carries a withholding code; a withholding ratio's code is optional ("not mapped yet").
  const taxCodeValid = raw.taxCode === null
    || (kind === 'WithholdingRatio' && typeof raw.taxCode === 'string' && isListCode(raw.taxCode))
  if (!taxCodeValid) return null
  return { code: raw.code, name: raw.name, percent: raw.percent, taxCode: (raw.taxCode as string | null) ?? null }
}

function decodeVersion(kind: RateListKind, raw: unknown): RateListVersion | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.version) || !isInt64Text(raw.listVersion, POSITIVE)
    || !isDateText(raw.effectiveFrom) || !Array.isArray(raw.entries) || raw.entries.length === 0
    || raw.entries.length > RATE_LIST_INPUT.maximumEntries || !isText(raw.recordedBy) || !isInstant(raw.recordedAtUtc)
    || !isText(raw.reason)) return null
  const entries = raw.entries.map((entry) => decodeEntry(kind, entry))
  if (entries.some((entry) => entry === null)) return null
  const codes = (entries as RateListEntry[]).map((entry) => entry.code)
  if (new Set(codes).size !== codes.length) return null
  return {
    listVersion: raw.listVersion,
    effectiveFrom: raw.effectiveFrom,
    entries: entries as RateListEntry[],
    recordedBy: raw.recordedBy,
    recordedAtUtc: raw.recordedAtUtc,
    reason: raw.reason,
  }
}

function decodeList(raw: unknown, businessDate: string): RateList | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.list) || !isRateListKind(raw.kind) || !isInt64Text(raw.version, LIST_FENCE)
    || !Array.isArray(raw.scheduled)) return null
  const kind = raw.kind
  const inForce = raw.inForce === null ? null : decodeVersion(kind, raw.inForce)
  if (raw.inForce !== null && inForce === null) return null
  const scheduled = raw.scheduled.map((version) => decodeVersion(kind, version))
  if (scheduled.some((version) => version === null)) return null
  const versions = scheduled as RateListVersion[]
  // The server's timeline: the version in force started on or before the business date; the scheduled ones start later,
  // one per date, in date order; a list never entered (fence "0") has no version at all.
  if (inForce !== null && inForce.effectiveFrom > businessDate) return null
  if (versions.some((version, index) => version.effectiveFrom <= businessDate
    || (index > 0 && version.effectiveFrom <= versions[index - 1].effectiveFrom))) return null
  if (raw.version === '0' && (inForce !== null || versions.length > 0)) return null
  return { kind, version: raw.version, inForce, scheduled: versions }
}

/** Both lists of the company asked for, or null when any part is off-contract or the answer is another company's. */
export function decodeRateLists(raw: unknown, companyId: string): RateLists | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.view) || !isUuid(raw.companyId) || raw.companyId !== companyId
    || !isDateText(raw.businessDate) || !Array.isArray(raw.lists)) return null
  const businessDate = raw.businessDate
  const lists = raw.lists.map((list) => decodeList(list, businessDate))
  if (lists.some((list) => list === null)) return null
  const kinds = (lists as RateList[]).map((list) => list.kind)
  if (kinds.length !== RATE_LIST_KINDS.length || RATE_LIST_KINDS.some((kind) => !kinds.includes(kind))) return null
  return { companyId, businessDate, lists: lists as RateList[] }
}

/**
 * The editor text of a list → entries: one entry per line, "KOD;Ad;Oran" (a VAT rate) or "KOD;Ad;Oran;GİB kodu" (a
 * withholding ratio, the code optional). Codes are upper-cased; null when a line is malformed, a code repeats or the list
 * is empty or longer than the transport bound. The server checks again (and whether a GİB code is in the list in force).
 */
export function parseRateListText(kind: RateListKind, text: string): RateListEntry[] | null {
  const lines = text.split('\n').map((line) => line.trim()).filter((line) => line.length > 0)
  if (lines.length === 0 || lines.length > RATE_LIST_INPUT.maximumEntries) return null
  const entries: RateListEntry[] = []
  for (const line of lines) {
    const parts = line.split(';').map((part) => part.trim())
    const allowed = kind === 'VatRate' ? parts.length === 3 : parts.length === 3 || parts.length === 4
    if (!allowed) return null
    const [rawCode, name, percent, rawTaxCode = ''] = parts
    const code = normalizeTaxCode(rawCode)
    const taxCode = rawTaxCode.length === 0 ? null : normalizeTaxCode(rawTaxCode)
    if (!isRateCode(code) || name.length === 0 || name.length > RATE_LIST_INPUT.nameMaximumLength
      || !isRateListPercent(kind, percent) || (taxCode !== null && !isListCode(taxCode))) return null
    entries.push({ code, name, percent, taxCode })
  }
  return new Set(entries.map((entry) => entry.code)).size === entries.length ? entries : null
}

/** The editor text of a version (the inverse of {@link parseRateListText}). */
export function rateListText(entries: readonly RateListEntry[]): string {
  return entries
    .map((entry) => [entry.code, entry.name, entry.percent, ...(entry.taxCode === null ? [] : [entry.taxCode])].join(';'))
    .join('\n')
}
