/**
 * Wire contract of the plan's termin SUGGESTION (URT-S10, F-010b, B-15):
 * `GET /api/v1/manufacturing/plans/{planId}/due-date-suggestion` (`PlanDueDateSuggestionViewV1`).
 *
 * The server computes the day from the work center's shift calendar (Organization) and the hourly capacity (the plan's
 * Settings parameter profile); the screen computes nothing. It is only a proposal: the user takes or changes it through
 * the existing hand-entered termin command, which keeps every server check.
 *
 * Decoding fails CLOSED and is strict: exactly the contract's keys, every kilogram and kg/h figure is the server's
 * lossless invariant text, and the answer must be the plan that was asked for. `suggested` carries a day, the finishing
 * moment and the inputs it used, and no reason; `unavailable` carries no day and names its reason. A status this screen
 * does not know is not this contract.
 */

import { calendarDate } from './contract'

export const DUE_DATE_SUGGESTION_CONTRACT_VERSION = 1

export const DUE_DATE_SUGGESTION_STATUSES = ['suggested', 'unavailable'] as const
export type DueDateSuggestionStatus = (typeof DUE_DATE_SUGGESTION_STATUSES)[number]

export interface DueDateSuggestionView {
  readonly contractVersion: number
  readonly methodCode: string
  readonly planId: string
  readonly planVersion: number
  readonly dueDateRevision: number
  readonly workCenterId: string
  readonly status: DueDateSuggestionStatus
  /** The suggested termin day (`yyyy-MM-dd`); null unless `suggested`. */
  readonly suggestedDueDate: string | null
  /** The local wall-clock moment (`yyyy-MM-ddTHH:mm`, the calendar's zone) the work would finish. */
  readonly completesAtLocal: string | null
  readonly requiredKg: string
  readonly requiredWorkingMinutes: number | null
  readonly capacityKgPerHour: string | null
  readonly capacityProfileVersion: string | null
  readonly capacitySource: string | null
  readonly calendarVersion: number | null
  readonly calendarSource: string | null
  readonly timeZoneId: string | null
  readonly reasonCode: string | null
  readonly businessDate: string
  readonly calculatedAtUtc: string
}

const VIEW_KEYS = [
  'contractVersion',
  'methodCode',
  'planId',
  'planVersion',
  'dueDateRevision',
  'workCenterId',
  'status',
  'suggestedDueDate',
  'completesAtLocal',
  'requiredKg',
  'requiredWorkingMinutes',
  'capacityKgPerHour',
  'capacityProfileVersion',
  'capacitySource',
  'calendarVersion',
  'calendarSource',
  'timeZoneId',
  'reasonCode',
  'businessDate',
  'calculatedAtUtc',
] as const

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent, no trailing fractional zero; never negative. */
const AMOUNT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
/** An ISO instant with its offset, as the server writes a `DateTimeOffset`. */
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64
/** The local finishing moment, minutes precision, no offset (it is the calendar zone's wall clock). */
const LOCAL_MOMENT = /^(\d{4}-\d{2}-\d{2})T([01]\d|2[0-3]):[0-5]\d$/
/** A stable machine code: lower-case words joined by `_`, `-` or `.`. */
const CODE = /^[a-z][a-z0-9]*([._-][a-z0-9]+)*$/
const CODE_MAXIMUM_LENGTH = 128
/** A source name as the server writes it (`Settings`, `Catalog`, `WorkCenter`, `Default`). */
const ENUM_NAME = /^[A-Z][A-Za-z0-9]*$/
const ENUM_NAME_MAXIMUM_LENGTH = 64
/** A profile version as text (`0` for the catalog, a revision number for Settings). */
const PROFILE_VERSION = /^(0|[1-9][0-9]*)$/
const PROFILE_VERSION_MAXIMUM_LENGTH = 20
/** An IANA (or host) time zone id. */
const TIME_ZONE_ID = /^[A-Za-z][A-Za-z0-9_+\-/ ]*$/
const TIME_ZONE_ID_MAXIMUM_LENGTH = 64

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

function amount(value: unknown): string | null {
  return typeof value === 'string' && AMOUNT.test(value) ? value : null
}

function count(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null
}

function instant(value: unknown): string | null {
  return typeof value === 'string' &&
    value.length <= INSTANT_MAXIMUM_LENGTH &&
    INSTANT.test(value) &&
    !Number.isNaN(Date.parse(value))
    ? value
    : null
}

function code(value: unknown): string | null {
  return typeof value === 'string' && value.length <= CODE_MAXIMUM_LENGTH && CODE.test(value) ? value : null
}

function token(value: unknown, pattern: RegExp, maximumLength: number): string | null {
  return typeof value === 'string' && value.length <= maximumLength && pattern.test(value) ? value : null
}

function localMoment(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const match = LOCAL_MOMENT.exec(value)
  return match !== null && calendarDate(match[1]) ? value : null
}

/** `undefined` marks a value that is neither null nor valid by `decode`. */
function optional<T>(value: unknown, decode: (raw: unknown) => T | null): T | null | undefined {
  if (value === null) return null
  return decode(value) ?? undefined
}

function isStatus(value: unknown): value is DueDateSuggestionStatus {
  return typeof value === 'string' && (DUE_DATE_SUGGESTION_STATUSES as readonly string[]).includes(value)
}

/** The suggestion for `requestedPlanId`, version 1, or `null` when any part of it is not this contract. */
export function decodeDueDateSuggestion(value: unknown, requestedPlanId: string): DueDateSuggestionView | null {
  if (!isObject(value) || !hasExactKeys(value, VIEW_KEYS)) return null
  if (value.contractVersion !== DUE_DATE_SUGGESTION_CONTRACT_VERSION || !isStatus(value.status)) return null
  const status = value.status
  const methodCode = code(value.methodCode)
  const planId = nonNilGuid(value.planId)
  const planVersion = count(value.planVersion)
  const dueDateRevision = count(value.dueDateRevision)
  const workCenterId = nonNilGuid(value.workCenterId)
  const suggestedDueDate = optional(value.suggestedDueDate, (raw) => (calendarDate(raw) ? raw : null))
  const completesAtLocal = optional(value.completesAtLocal, localMoment)
  const requiredKg = amount(value.requiredKg)
  const requiredWorkingMinutes = optional(value.requiredWorkingMinutes, count)
  const capacityKgPerHour = optional(value.capacityKgPerHour, amount)
  const capacityProfileVersion = optional(value.capacityProfileVersion, (raw) =>
    token(raw, PROFILE_VERSION, PROFILE_VERSION_MAXIMUM_LENGTH))
  const capacitySource = optional(value.capacitySource, (raw) => token(raw, ENUM_NAME, ENUM_NAME_MAXIMUM_LENGTH))
  const calendarVersion = optional(value.calendarVersion, count)
  const calendarSource = optional(value.calendarSource, (raw) => token(raw, ENUM_NAME, ENUM_NAME_MAXIMUM_LENGTH))
  const timeZoneId = optional(value.timeZoneId, (raw) => token(raw, TIME_ZONE_ID, TIME_ZONE_ID_MAXIMUM_LENGTH))
  const reasonCode = optional(value.reasonCode, code)
  const businessDate = calendarDate(value.businessDate) ? value.businessDate : null
  const calculatedAtUtc = instant(value.calculatedAtUtc)
  if (
    methodCode === null ||
    planId === null ||
    planId.toLowerCase() !== requestedPlanId.toLowerCase() ||
    planVersion === null ||
    planVersion < 1 ||
    dueDateRevision === null ||
    workCenterId === null ||
    suggestedDueDate === undefined ||
    completesAtLocal === undefined ||
    requiredKg === null ||
    requiredWorkingMinutes === undefined ||
    capacityKgPerHour === undefined ||
    capacityProfileVersion === undefined ||
    capacitySource === undefined ||
    calendarVersion === undefined ||
    calendarSource === undefined ||
    timeZoneId === undefined ||
    reasonCode === undefined ||
    businessDate === null ||
    calculatedAtUtc === null
  ) {
    return null
  }
  // A capacity is never zero, and it always names the profile it came from.
  if (capacityKgPerHour === '0') return null
  if (capacityKgPerHour !== null && (capacityProfileVersion === null || capacitySource === null)) return null
  if ((capacityProfileVersion === null) !== (capacitySource === null)) return null
  // The calendar is named whole or not at all.
  if ((calendarVersion === null) !== (calendarSource === null) || (calendarVersion === null) !== (timeZoneId === null)) {
    return null
  }
  if (status === 'suggested') {
    if (
      suggestedDueDate === null ||
      completesAtLocal === null ||
      requiredWorkingMinutes === null ||
      capacityKgPerHour === null ||
      calendarVersion === null ||
      reasonCode !== null ||
      // URT-D66 (1): never earlier than the server's business day (ISO days compare as text).
      suggestedDueDate < businessDate
    ) {
      return null
    }
  } else if (suggestedDueDate !== null || completesAtLocal !== null || reasonCode === null) {
    return null
  }
  return {
    contractVersion: DUE_DATE_SUGGESTION_CONTRACT_VERSION,
    methodCode,
    planId,
    planVersion,
    dueDateRevision,
    workCenterId,
    status,
    suggestedDueDate,
    completesAtLocal,
    requiredKg,
    requiredWorkingMinutes,
    capacityKgPerHour,
    capacityProfileVersion,
    capacitySource,
    calendarVersion,
    calendarSource,
    timeZoneId,
    reasonCode,
    businessDate,
    calculatedAtUtc,
  }
}
