/**
 * Wire contract of the monthly scrap price (URT-S13, URT-D47, URT-D65, URT-D72):
 * `GET /api/v1/manufacturing/scrap-prices?year=` (`MonthlyScrapPriceListViewV1`) and the answer of
 * `POST /api/v1/manufacturing/scrap-prices` and `POST /api/v1/manufacturing/scrap-prices/{year}/{month}/corrections`
 * (`MonthlyScrapPriceViewV1`). Each listed month is its effective (latest) version: version 1 is the first entry and
 * carries no reason, every later version is a correction and carries its reason.
 *
 * Decoding fails CLOSED and is strict: every object carries exactly the contract's keys, the price is the server's
 * lossless invariant text (never a JSON number, never converted here), the month is a calendar month, and a list must
 * be the year that was asked for, each month once, the latest month first, as the server orders it. A list with one
 * row that is not this contract is `null` — a half-trusted list would show a price beside the wrong month.
 */

export interface ScrapPriceView {
  readonly scrapPriceId: string
  readonly year: number
  readonly month: number
  /** TL per kg as the server's canonical text (no trailing fractional zero); shown as received, never computed. */
  readonly tlPerKg: string
  /** URT-D72: 1 for the first entry, one more for each correction. */
  readonly version: number
  /** The latest correction's reason; null exactly on version 1. */
  readonly reason: string | null
  readonly recordedBy: string
  readonly recordedAtUtc: string
}

export interface ScrapPriceList {
  readonly year: number
  /** The recorded months, the latest first. A month that is not listed has no price. */
  readonly items: readonly ScrapPriceView[]
}

export const SCRAP_PRICE_FIRST_MONTH = 1
export const SCRAP_PRICE_LAST_MONTH = 12
/** The calendar years the server accepts (`DateOnly.MinValue.Year`..`DateOnly.MaxValue.Year`). */
export const SCRAP_PRICE_FIRST_YEAR = 1
export const SCRAP_PRICE_LAST_YEAR = 9999

/** `MonthlyScrapPrice.FirstVersion`. */
export const SCRAP_PRICE_FIRST_VERSION = 1
/** `MonthlyScrapPrice.ReasonMaximumLength`: a correction's reason, trimmed. */
export const SCRAP_PRICE_REASON_MAXIMUM_LENGTH = 256

const VIEW_KEYS = [
  'scrapPriceId',
  'year',
  'month',
  'tlPerKg',
  'version',
  'reason',
  'recordedBy',
  'recordedAtUtc',
] as const
const LIST_KEYS = ['year', 'items'] as const

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent, no trailing fractional zero. */
const PRICE_OUT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
/** An ISO instant with its offset, as the server writes a `DateTimeOffset`. */
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64
/** `MonthlyScrapPrice.RecordedByMaximumLength`. */
const RECORDED_BY_MAXIMUM_LENGTH = 128

/**
 * The plain notation the server's entry grammar accepts for a price (`^(0|[1-9][0-9]{0,17})(\.[0-9]{1,20})?$`). Only
 * the shape is checked before sending; whether the value is positive, within four decimals and under the bound is the
 * server's decision and comes back as its answer.
 */
const PRICE_IN = /^(0|[1-9][0-9]{0,17})(\.[0-9]{1,20})?$/

type Json = Record<string, unknown>

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Json, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function nonNilGuid(value: unknown): string | null {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID ? value : null
}

export function isScrapPriceYear(value: unknown): value is number {
  return (
    typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= SCRAP_PRICE_FIRST_YEAR &&
    value <= SCRAP_PRICE_LAST_YEAR
  )
}

export function isScrapPriceMonth(value: unknown): value is number {
  return (
    typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= SCRAP_PRICE_FIRST_MONTH &&
    value <= SCRAP_PRICE_LAST_MONTH
  )
}

/** A recorded price is always greater than zero; "0" is a valid decimal text but never a recorded price. */
function price(value: unknown): string | null {
  return typeof value === 'string' && PRICE_OUT.test(value) && value !== '0' ? value : null
}

function version(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= SCRAP_PRICE_FIRST_VERSION ? value : null
}

/** A correction reason as the server stores it: trimmed, not blank, within the bound. */
function storedReason(value: unknown): string | null {
  return typeof value === 'string' &&
    value.trim().length > 0 &&
    value === value.trim() &&
    value.length <= SCRAP_PRICE_REASON_MAXIMUM_LENGTH
    ? value
    : null
}

function instant(value: unknown): string | null {
  return typeof value === 'string' &&
    value.length <= INSTANT_MAXIMUM_LENGTH &&
    INSTANT.test(value) &&
    !Number.isNaN(Date.parse(value))
    ? value
    : null
}

function actor(value: unknown): string | null {
  return typeof value === 'string' &&
    value.trim().length > 0 &&
    value === value.trim() &&
    value.length <= RECORDED_BY_MAXIMUM_LENGTH
    ? value
    : null
}

export function decodeScrapPrice(value: unknown): ScrapPriceView | null {
  if (!isObject(value) || !hasExactKeys(value, VIEW_KEYS)) return null
  const scrapPriceId = nonNilGuid(value.scrapPriceId)
  const tlPerKg = price(value.tlPerKg)
  const recordedBy = actor(value.recordedBy)
  const recordedAtUtc = instant(value.recordedAtUtc)
  const priceVersion = version(value.version)
  // Version 1 carries no reason and every correction carries one (the server's check constraint); anything else is
  // not this contract.
  let reason: string | null | undefined
  if (priceVersion === SCRAP_PRICE_FIRST_VERSION) reason = value.reason === null ? null : undefined
  else reason = storedReason(value.reason) ?? undefined
  if (
    scrapPriceId === null ||
    priceVersion === null ||
    reason === undefined ||
    !isScrapPriceYear(value.year) ||
    !isScrapPriceMonth(value.month) ||
    tlPerKg === null ||
    recordedBy === null ||
    recordedAtUtc === null
  ) {
    return null
  }
  return {
    scrapPriceId,
    year: value.year,
    month: value.month,
    tlPerKg,
    version: priceVersion,
    reason,
    recordedBy,
    recordedAtUtc,
  }
}

/** The list of `requestedYear`: the same year, every row of it, each month once and strictly latest first. */
export function decodeScrapPriceList(value: unknown, requestedYear: number): ScrapPriceList | null {
  if (!isObject(value) || !hasExactKeys(value, LIST_KEYS)) return null
  if (!isScrapPriceYear(value.year) || value.year !== requestedYear || !Array.isArray(value.items)) return null
  if (value.items.length > SCRAP_PRICE_LAST_MONTH) return null
  const items: ScrapPriceView[] = []
  for (const raw of value.items) {
    const item = decodeScrapPrice(raw)
    if (item === null || item.year !== requestedYear) return null
    const previous = items.at(-1)
    if (previous !== undefined && item.month >= previous.month) return null
    items.push(item)
  }
  return { year: value.year, items }
}

/** The shape the server's entry grammar accepts, checked before sending (never the value's policy). */
export function isScrapPriceEntryText(value: string): boolean {
  return PRICE_IN.test(value)
}

/** A correction reason the server's grammar accepts once trimmed: not blank and within the bound. */
export function isScrapPriceReasonText(value: string): boolean {
  const trimmed = value.trim()
  return trimmed.length > 0 && trimmed.length <= SCRAP_PRICE_REASON_MAXIMUM_LENGTH
}

/** The server's canonical text of an entered price: the insignificant trailing fractional zeroes dropped. */
export function canonicalScrapPrice(value: string): string {
  if (!value.includes('.')) return value
  return value.replace(/0+$/, '').replace(/\.$/, '')
}

/** The calendar months of a year that the list does not name: their scrap cost stays pending. */
export function monthsWithoutPrice(list: ScrapPriceList): readonly number[] {
  const recorded = new Set(list.items.map((item) => item.month))
  const missing: number[] = []
  for (let month = SCRAP_PRICE_FIRST_MONTH; month <= SCRAP_PRICE_LAST_MONTH; month += 1) {
    if (!recorded.has(month)) missing.push(month)
  }
  return missing
}
