/**
 * F-025 (B-11) exchange rate wire contract (KUR_GUNU; SAT-D55, SAT-D169…D172, SAT-D188, EK31 SAT-D198;
 * `SettingsExchangeRateEndpoints`, `SettingsExchangeRatesV1.cs`; pinned to `contracts/openapi/goymen-erp-v1.json` by
 * ./exchangeRateOpenapiContract.test.ts):
 *
 *   GET  /settings/exchange-rates?from=&to=                     -> ExchangeRateDayListV1 (at most 62 days)
 *   GET  /settings/exchange-rates/resolution?rateDay=&currencyCode= -> ExchangeRateResolutionV1 (the one calculator)
 *   POST /settings/exchange-rates/days/{date}/corrections       -> 204 (reasoned manual rate, published day only)
 *   POST /settings/exchange-rates/days/{date}/half-day-marks    -> 204 (manual full/half day mark)
 *   POST /settings/exchange-rates/days/{date}/reverification    -> 204 (reopen a final day for the official source)
 *   POST /settings/exchange-rates/imports                       -> ExchangeRateImportSummaryV1 (at most 31 days)
 *
 * Every decoder fails CLOSED (one bad row invalidates the whole answer). Rates stay the lossless decimal TEXT the server
 * sends: the screen never computes, rounds or derives a rate, and a rate the server does not resolve is never replaced by
 * an older or default one ("kur yoksa işlem durur").
 */

import { isDateText, isUuid } from './discountLimitContract'

export const EXCHANGE_RATES_PATH = '/settings/exchange-rates'
export const EXCHANGE_RATE_IMPORTS_PATH = `${EXCHANGE_RATES_PATH}/imports`

export function exchangeRateListPath(from: string, to: string): string {
  return `${EXCHANGE_RATES_PATH}?${new URLSearchParams({ from, to }).toString()}`
}

export function exchangeRateResolutionPath(rateDay: string, currencyCode: string): string {
  return `${EXCHANGE_RATES_PATH}/resolution?${new URLSearchParams({ rateDay, currencyCode }).toString()}`
}

function dayPath(date: string, action: string): string {
  return `${EXCHANGE_RATES_PATH}/days/${encodeURIComponent(date)}/${action}`
}

export function exchangeRateCorrectionPath(date: string): string {
  return dayPath(date, 'corrections')
}

export function exchangeRateHalfDayMarkPath(date: string): string {
  return dayPath(date, 'half-day-marks')
}

export function exchangeRateReverificationPath(date: string): string {
  return dayPath(date, 'reverification')
}

export const EXCHANGE_RATE_PROBLEM = {
  requestInvalid: 'exchange_rate_request_invalid',
  commandMetadataInvalid: 'exchange_rate_command_metadata_invalid',
  reasonRequired: 'exchange_rate_reason_required',
  reasonTooLong: 'exchange_rate_reason_too_long',
  idempotencyConflict: 'exchange_rate_idempotency_conflict',
  versionConflict: 'exchange_rate_version_conflict',
  dayNotPublished: 'exchange_rate_day_not_published',
  halfDayDetermined: 'exchange_rate_half_day_determined',
  markFutureDay: 'exchange_rate_mark_future_day',
  dayNotFinal: 'exchange_rate_day_not_final',
  sourceDisabled: 'exchange_rate_source_disabled',
  providerUnavailable: 'settings_provider_unavailable',
} as const

/** The codes an Unavailable resolution names (BuildingBlocks `RateDayExchangeRate` / `PreviousBusinessDayExchangeRate`). */
export const EXCHANGE_RATE_UNAVAILABLE = {
  halfDayUndetermined: 'exchange_rate_half_day_undetermined',
  dayUnconfirmed: 'exchange_rate_day_unconfirmed',
  currencyMissing: 'exchange_rate_currency_missing',
  manualConflict: 'exchange_rate_manual_conflict',
} as const

/** The server's input limits (`ExchangeRateInputLimitsV1`); the server re-checks every one. */
export const EXCHANGE_RATE_INPUT = {
  reasonMaximumLength: 1000,
  maximumListDays: 62,
  maximumImportDays: 31,
  rateMaximumIntegerDigits: 18,
  rateMaximumScale: 10,
  /** The default list window ending today (a screen convenience only; the user picks any range up to the maximum). */
  defaultListDays: 14,
} as const

export const BULLETIN_STATES = ['Published', 'NoBulletin', 'Unverified', 'NotFound', 'NotRecorded'] as const
export type BulletinState = (typeof BULLETIN_STATES)[number]
/** A record's bulletin state (a day with no record is `NotRecorded`, never a record state). */
export const BULLETIN_RECORD_STATES = ['Published', 'NoBulletin', 'Unverified', 'NotFound'] as const
export const HALF_DAY_STATES = ['FullDay', 'HalfDay', 'Undetermined'] as const
export type HalfDayState = (typeof HALF_DAY_STATES)[number]
/** The states a manual mark may set (`Undetermined` is the source's answer only). */
export const HALF_DAY_MARKS = ['FullDay', 'HalfDay'] as const
export type HalfDayMark = (typeof HALF_DAY_MARKS)[number]
export const PROVENANCES = ['Official', 'Manual'] as const
export type Provenance = (typeof PROVENANCES)[number]
export const RECORD_KINDS = ['Bulletin', 'HalfDay'] as const
export type RecordKind = (typeof RECORD_KINDS)[number]
export const IMPORT_OUTCOMES = ['Recorded', 'Unchanged', 'SourceDisabled'] as const
export type ImportOutcome = (typeof IMPORT_OUTCOMES)[number]
export const RESOLUTION_STATES = ['Resolved', 'Unavailable'] as const
export type ResolutionState = (typeof RESOLUTION_STATES)[number]
export const RESOLUTION_SOURCE = 'Tcmb'

/** A day on which the official answer is final (a reverification may reopen it). */
export const FINAL_BULLETIN_STATES: readonly BulletinState[] = ['Published', 'NoBulletin']

export interface ExchangeRateLine {
  readonly currencyCode: string
  readonly sellingRate: string
}

export interface ExchangeRateValue extends ExchangeRateLine {
  readonly provenance: Provenance
  readonly recordId: string
  readonly recordVersion: string
}

export interface ExchangeRateRecord {
  readonly recordId: string
  readonly recordVersion: string
  readonly kind: RecordKind
  readonly provenance: Provenance
  readonly state: string
  readonly rates: readonly ExchangeRateLine[]
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string | null
  readonly sourceReference: string | null
}

export interface ExchangeRateDay {
  readonly date: string
  /** The day's expected version for every command ("0" before the day has a record). */
  readonly version: string
  readonly bulletinState: BulletinState
  readonly halfDayState: HalfDayState
  readonly halfDayProvenance: Provenance | null
  readonly rates: readonly ExchangeRateValue[]
  readonly records: readonly ExchangeRateRecord[]
}

export interface ExchangeRateDayList {
  readonly companyId: string
  readonly from: string
  readonly to: string
  readonly bulletinSourceEnabled: boolean
  readonly halfDaySourceEnabled: boolean
  readonly days: readonly ExchangeRateDay[]
}

export interface ExchangeRateImportDay {
  readonly date: string
  readonly bulletinOutcome: ImportOutcome
  readonly halfDayOutcome: ImportOutcome
}

export interface ExchangeRateImportSummary {
  readonly days: readonly ExchangeRateImportDay[]
}

interface ResolutionBase {
  readonly rateDay: string
  readonly sourceCurrencyId: string
  readonly targetCurrencyId: string
  readonly currencyCode: string | null
  readonly source: typeof RESOLUTION_SOURCE
  readonly halfDayRuleApplied: boolean
  readonly previousFullBusinessDay: string | null
}

export type ExchangeRateResolution =
  | (ResolutionBase & {
      readonly state: 'Resolved'
      readonly bulletinDate: string
      readonly sellingRate: string
      readonly provenance: Provenance
      readonly recordId: string
      readonly recordVersion: string
    })
  /** No rate: the screen shows none and nothing falls back to an older or default rate. */
  | (ResolutionBase & { readonly state: 'Unavailable'; readonly code: string; readonly stoppedAt: string | null })

export interface CorrectRateBody {
  readonly expectedVersion: string
  readonly currencyCode: string
  readonly sellingRate: string
  readonly reason: string
}

export interface MarkHalfDayBody {
  readonly expectedVersion: string
  readonly state: HalfDayMark
  readonly reason: string
}

export interface ReverifyDayBody {
  readonly expectedVersion: string
  readonly reason: string
}

export interface ImportRatesBody {
  readonly from: string
  readonly to: string
}

const VERSION = /^(0|[1-9]\d*)$/
const POSITIVE_VERSION = /^[1-9]\d*$/
const CURRENCY = /^[A-Z]{3}$/
const DOMESTIC_CURRENCY = 'TRY'
const RATE = new RegExp(
  `^(0|[1-9]\\d{0,${EXCHANGE_RATE_INPUT.rateMaximumIntegerDigits - 1}})(\\.\\d{1,${EXCHANGE_RATE_INPUT.rateMaximumScale}})?$`,
)
const NON_ZERO_DIGIT = /[1-9]/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64

/** A foreign currency TCMB publishes against TRY: three upper-case letters, never TRY itself. */
export function isForeignCurrency(value: unknown): value is string {
  return typeof value === 'string' && CURRENCY.test(value) && value !== DOMESTIC_CURRENCY
}

/** A selling rate as the server accepts it: positive, at most 18 integer digits and 10 decimals, plain decimal text. */
export function isRateText(value: unknown): value is string {
  return typeof value === 'string' && RATE.test(value) && NON_ZERO_DIGIT.test(value)
}

const ISTANBUL_DAY = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/Istanbul',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

/**
 * Today's calendar day in Türkiye (the server's business day zone), as yyyy-MM-dd. Used only for the default range and
 * to hide a mark the server would refuse for a future day; the server decides both again.
 */
export function todayInIstanbul(now: Date = new Date()): string {
  const parts = ISTANBUL_DAY.formatToParts(now)
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((item) => item.type === type)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')}`
}

/** Whole calendar days from `from` to `to`, both included (0 or less when `to` is before `from`). */
export function inclusiveDayCount(from: string, to: string): number {
  return (dayNumber(to) - dayNumber(from)) + 1
}

function dayNumber(date: string): number {
  const [year, month, day] = date.split('-').map(Number)
  return Date.UTC(year, month - 1, day) / 86_400_000
}

/** `date` shifted by whole days, as yyyy-MM-dd (calendar arithmetic only; no time zone involved). */
export function addDays(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isNullableText(value: unknown): value is string | null {
  return value === null || isText(value)
}

function isInstant(value: unknown): value is string {
  return typeof value === 'string' && value.length <= INSTANT_MAXIMUM_LENGTH && INSTANT.test(value) && !Number.isNaN(Date.parse(value))
}

function isOneOf<T extends string>(values: readonly T[], value: unknown): value is T {
  return typeof value === 'string' && (values as readonly string[]).includes(value)
}

/** The wire members of each schema (the OpenAPI schemas'). */
const KEYS = {
  list: ['companyId', 'from', 'to', 'bulletinSourceEnabled', 'halfDaySourceEnabled', 'days'],
  day: ['date', 'version', 'bulletinState', 'halfDayState', 'halfDayProvenance', 'rates', 'records'],
  value: ['currencyCode', 'sellingRate', 'provenance', 'recordId', 'recordVersion'],
  line: ['currencyCode', 'sellingRate'],
  record: [
    'recordId',
    'recordVersion',
    'kind',
    'provenance',
    'state',
    'rates',
    'recordedBy',
    'recordedAtUtc',
    'reason',
    'sourceReference',
  ],
  importSummary: ['days'],
  importDay: ['date', 'bulletinOutcome', 'halfDayOutcome'],
  resolution: [
    'rateDay',
    'sourceCurrencyId',
    'targetCurrencyId',
    'currencyCode',
    'state',
    'source',
    'halfDayRuleApplied',
    'previousFullBusinessDay',
    'bulletinDate',
    'sellingRate',
    'provenance',
    'recordId',
    'recordVersion',
    'code',
    'stoppedAt',
  ],
} as const

function decodeLine(raw: unknown): ExchangeRateLine | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.line)) return null
  if (!isForeignCurrency(raw.currencyCode) || !isRateText(raw.sellingRate)) return null
  return { currencyCode: raw.currencyCode, sellingRate: raw.sellingRate }
}

function decodeLines(raw: unknown): ExchangeRateLine[] | null {
  if (!Array.isArray(raw)) return null
  const lines: ExchangeRateLine[] = []
  for (const item of raw) {
    const line = decodeLine(item)
    if (line === null || lines.some((existing) => existing.currencyCode === line.currencyCode)) return null
    lines.push(line)
  }
  return lines
}

function decodeValue(raw: unknown): ExchangeRateValue | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.value)) return null
  if (!isForeignCurrency(raw.currencyCode) || !isRateText(raw.sellingRate)) return null
  if (!isOneOf(PROVENANCES, raw.provenance) || !isUuid(raw.recordId)) return null
  if (typeof raw.recordVersion !== 'string' || !POSITIVE_VERSION.test(raw.recordVersion)) return null
  return {
    currencyCode: raw.currencyCode,
    sellingRate: raw.sellingRate,
    provenance: raw.provenance,
    recordId: raw.recordId,
    recordVersion: raw.recordVersion,
  }
}

function decodeRecord(raw: unknown): ExchangeRateRecord | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.record)) return null
  if (!isUuid(raw.recordId) || typeof raw.recordVersion !== 'string' || !POSITIVE_VERSION.test(raw.recordVersion)) return null
  if (!isOneOf(RECORD_KINDS, raw.kind) || !isOneOf(PROVENANCES, raw.provenance)) return null
  const stateValid = raw.kind === 'Bulletin' ? isOneOf(BULLETIN_RECORD_STATES, raw.state) : isOneOf(HALF_DAY_STATES, raw.state)
  if (!stateValid || typeof raw.state !== 'string') return null
  const rates = decodeLines(raw.rates)
  if (rates === null || (rates.length > 0 && !(raw.kind === 'Bulletin' && raw.state === 'Published'))) return null
  if (!isText(raw.recordedBy) || !isInstant(raw.recordedAtUtc)) return null
  if (!isNullableText(raw.reason) || !isNullableText(raw.sourceReference)) return null
  return {
    recordId: raw.recordId,
    recordVersion: raw.recordVersion,
    kind: raw.kind,
    provenance: raw.provenance,
    state: raw.state,
    rates,
    recordedBy: raw.recordedBy,
    recordedAtUtc: raw.recordedAtUtc,
    reason: raw.reason,
    sourceReference: raw.sourceReference,
  }
}

function decodeDay(raw: unknown): ExchangeRateDay | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.day)) return null
  if (!isDateText(raw.date) || typeof raw.version !== 'string' || !VERSION.test(raw.version)) return null
  if (!isOneOf(BULLETIN_STATES, raw.bulletinState) || !isOneOf(HALF_DAY_STATES, raw.halfDayState)) return null
  if (raw.halfDayProvenance !== null && !isOneOf(PROVENANCES, raw.halfDayProvenance)) return null
  if (!Array.isArray(raw.rates) || !Array.isArray(raw.records)) return null
  const rates: ExchangeRateValue[] = []
  for (const item of raw.rates) {
    const value = decodeValue(item)
    if (value === null || rates.some((existing) => existing.currencyCode === value.currencyCode)) return null
    rates.push(value)
  }
  // Rates are answered for a published day only; anything else would be a rate the day does not have.
  if (rates.length > 0 && raw.bulletinState !== 'Published') return null
  const records: ExchangeRateRecord[] = []
  for (const item of raw.records) {
    const record = decodeRecord(item)
    if (record === null || records.some((existing) => existing.recordId === record.recordId)) return null
    records.push(record)
  }
  // A day with no record has nothing to show but its date: no state from a record, version "0".
  if (records.length === 0 && (raw.bulletinState !== 'NotRecorded' || raw.version !== '0')) return null
  return {
    date: raw.date,
    version: raw.version,
    bulletinState: raw.bulletinState,
    halfDayState: raw.halfDayState,
    halfDayProvenance: raw.halfDayProvenance,
    rates,
    records,
  }
}

/** The day list of the requested range in the selected company; another company, range or a day outside it is malformed. */
export function decodeExchangeRateDayList(
  raw: unknown,
  companyId: string,
  from: string,
  to: string,
): ExchangeRateDayList | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.list)) return null
  if (raw.companyId !== companyId || raw.from !== from || raw.to !== to) return null
  if (typeof raw.bulletinSourceEnabled !== 'boolean' || typeof raw.halfDaySourceEnabled !== 'boolean') return null
  if (!Array.isArray(raw.days)) return null
  const days: ExchangeRateDay[] = []
  for (const item of raw.days) {
    const day = decodeDay(item)
    if (day === null || day.date < from || day.date > to || days.some((existing) => existing.date === day.date)) return null
    days.push(day)
  }
  return {
    companyId,
    from,
    to,
    bulletinSourceEnabled: raw.bulletinSourceEnabled,
    halfDaySourceEnabled: raw.halfDaySourceEnabled,
    days,
  }
}

/** What an import did per day of the requested range; a day outside it is malformed. */
export function decodeImportSummary(raw: unknown, from: string, to: string): ExchangeRateImportSummary | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.importSummary) || !Array.isArray(raw.days)) return null
  const days: ExchangeRateImportDay[] = []
  for (const item of raw.days) {
    if (!isRecord(item) || !hasExactKeys(item, KEYS.importDay) || !isDateText(item.date)) return null
    if (item.date < from || item.date > to || days.some((existing) => existing.date === item.date)) return null
    if (!isOneOf(IMPORT_OUTCOMES, item.bulletinOutcome) || !isOneOf(IMPORT_OUTCOMES, item.halfDayOutcome)) return null
    days.push({ date: item.date, bulletinOutcome: item.bulletinOutcome, halfDayOutcome: item.halfDayOutcome })
  }
  return { days }
}

/**
 * The calculator's answer for the requested rate day and currency. Resolved carries the five rate members and no code;
 * Unavailable carries a code and none of the five — so an Unavailable answer can never be shown with a rate.
 */
export function decodeResolution(raw: unknown, rateDay: string, currencyCode: string): ExchangeRateResolution | null {
  if (!isRecord(raw) || !hasExactKeys(raw, KEYS.resolution)) return null
  if (raw.rateDay !== rateDay || !isUuid(raw.sourceCurrencyId) || !isUuid(raw.targetCurrencyId)) return null
  if (raw.currencyCode !== null && raw.currencyCode !== currencyCode) return null
  if (raw.source !== RESOLUTION_SOURCE || typeof raw.halfDayRuleApplied !== 'boolean') return null
  if (raw.previousFullBusinessDay !== null && !isDateText(raw.previousFullBusinessDay)) return null
  const base: ResolutionBase = {
    rateDay,
    sourceCurrencyId: raw.sourceCurrencyId,
    targetCurrencyId: raw.targetCurrencyId,
    currencyCode: raw.currencyCode,
    source: RESOLUTION_SOURCE,
    halfDayRuleApplied: raw.halfDayRuleApplied,
    previousFullBusinessDay: raw.previousFullBusinessDay,
  }
  if (raw.state === 'Resolved') {
    if (!isDateText(raw.bulletinDate) || !isRateText(raw.sellingRate) || !isOneOf(PROVENANCES, raw.provenance)) return null
    if (!isUuid(raw.recordId) || typeof raw.recordVersion !== 'string' || !POSITIVE_VERSION.test(raw.recordVersion)) return null
    if (raw.code !== null || raw.stoppedAt !== null) return null
    return {
      ...base,
      state: 'Resolved',
      bulletinDate: raw.bulletinDate,
      sellingRate: raw.sellingRate,
      provenance: raw.provenance,
      recordId: raw.recordId,
      recordVersion: raw.recordVersion,
    }
  }
  if (raw.state === 'Unavailable') {
    const rateMembers = [raw.bulletinDate, raw.sellingRate, raw.provenance, raw.recordId, raw.recordVersion]
    if (rateMembers.some((member) => member !== null) || !isText(raw.code)) return null
    if (raw.stoppedAt !== null && !isDateText(raw.stoppedAt)) return null
    return { ...base, state: 'Unavailable', code: raw.code, stoppedAt: raw.stoppedAt }
  }
  return null
}

/** Upper-case, trimmed currency input (the server receives exactly what is shown). */
export function normalizeCurrency(value: string): string {
  return value.trim().toUpperCase()
}
