/**
 * B-15 work-centre calendar — wire contract and strict decoders.
 *
 *   GET /api/v1/organization/work-centers/{id}/calendar -> WorkCenterCalendarDetail | 404
 *   PUT /api/v1/organization/work-centers/{id}/calendar {shifts, closedDates, expectedVersion, reason} -> 204
 *
 * (`OrganizationWorkCenterCalendarContracts.cs`.) What the shapes mean:
 *
 *  - `source` is `Default` while the work centre has no saved calendar and therefore works on the URT-D11 default
 *    (one 08:00–16:00 shift on weekdays, weekends closed); then `version` is 0 — the expected version of the FIRST
 *    save. After a save it is `WorkCenter` and `version` is the saved revision (>= 1). So unlike every other
 *    Organization version, 0 is a legal value here, and the decoders below accept it only in that pairing.
 *  - `shifts` are weekly windows `{day, start, end}`: `day` is the English weekday token (`Monday` … `Sunday`),
 *    `start`/`end` are `HH:mm` in `timeZoneId`, on the same day (no overnight window in v1).
 *  - `closedDates` are `yyyy-MM-dd` business dates, ascending.
 *  - URT-D29: no capacity or termin is computed from this; the screen shows and edits the master, nothing more.
 *
 * Every decoder fails closed: one unreadable field makes the whole answer malformed, never a partly shown calendar.
 */

import type { VersionConflict } from './contract'

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const TIME = /^([01][0-9]|2[0-3]):[0-5][0-9]$/
const DATE = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/

/** The weekday tokens, in the order the screen lists them (the business week starts on Monday). */
export const CALENDAR_DAYS = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
] as const

export type CalendarDay = (typeof CALENDAR_DAYS)[number]

export const CALENDAR_SOURCE = { default: 'Default', workCenter: 'WorkCenter' } as const

export type CalendarSource = (typeof CALENDAR_SOURCE)[keyof typeof CALENDAR_SOURCE]

/**
 * The server's own limits (`WorkCenterCalendarDefinition`), repeated here only so the form can say so before it
 * sends; the server stays the authority and refuses the same shapes with a 400.
 */
export const CALENDAR_LIMITS = {
  shiftsPerDay: 8,
  shifts: 56,
  closedDates: 1000,
} as const

export interface CalendarShift {
  readonly day: CalendarDay
  readonly start: string
  readonly end: string
}

export interface WorkCenterCalendar {
  readonly workCenterId: string
  readonly source: CalendarSource
  /** 0 exactly when `source` is `Default`; the saved revision (>= 1) otherwise. */
  readonly version: number
  readonly timeZoneId: string
  readonly shifts: readonly CalendarShift[]
  readonly closedDates: readonly string[]
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

export function isCalendarDay(value: unknown): value is CalendarDay {
  return typeof value === 'string' && (CALENDAR_DAYS as readonly string[]).includes(value)
}

export function isTimeToken(value: unknown): value is string {
  return typeof value === 'string' && TIME.test(value)
}

/** A real calendar date in `yyyy-MM-dd` — `2026-02-30` is not one. */
export function isDateToken(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const match = DATE.exec(value)
  if (match === null) return false
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1 || month < 1 || month > 12 || day < 1) return false
  const date = new Date(Date.UTC(year, month - 1, day))
  // Date.UTC maps years 0..99 to 1900..1999; setUTCFullYear keeps the year as written.
  date.setUTCFullYear(year)
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
}

function isCalendarVersion(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

function decodeShift(raw: unknown): CalendarShift | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ['day', 'start', 'end'])) return null
  const { day, start, end } = raw
  if (!isCalendarDay(day) || !isTimeToken(start) || !isTimeToken(end)) return null
  // Same-day windows only; an end that is not after its start is not a window the server could have stored.
  if (end <= start) return null
  return { day, start, end }
}

/**
 * The calendar read. Besides the field rules it carries the card's cross-field rule (the answer must be the work
 * centre the route asked for) and the source/version pairing above.
 */
export function decodeWorkCenterCalendar(raw: unknown, requestedId: string): WorkCenterCalendar | null {
  if (!isRecord(raw)) return null
  if (!hasExactKeys(raw, ['workCenterId', 'source', 'version', 'timeZoneId', 'shifts', 'closedDates'])) return null
  const { workCenterId, source, version, timeZoneId, shifts, closedDates } = raw
  if (typeof workCenterId !== 'string' || !UUID.test(workCenterId)) return null
  if (workCenterId.toLowerCase() !== requestedId.toLowerCase()) return null
  if (source !== CALENDAR_SOURCE.default && source !== CALENDAR_SOURCE.workCenter) return null
  if (!isCalendarVersion(version)) return null
  if ((source === CALENDAR_SOURCE.default) !== (version === 0)) return null
  if (typeof timeZoneId !== 'string' || timeZoneId.trim().length === 0) return null
  if (!Array.isArray(shifts) || shifts.length > CALENDAR_LIMITS.shifts) return null
  if (!Array.isArray(closedDates) || closedDates.length > CALENDAR_LIMITS.closedDates) return null

  const decodedShifts: CalendarShift[] = []
  for (const item of shifts) {
    const shift = decodeShift(item)
    if (shift === null) return null
    decodedShifts.push(shift)
  }

  const decodedDates: string[] = []
  for (const item of closedDates) {
    if (!isDateToken(item)) return null
    decodedDates.push(item)
  }

  return {
    workCenterId,
    source,
    version,
    timeZoneId,
    shifts: decodedShifts,
    closedDates: decodedDates,
  }
}

/**
 * The 409 `conflict` extension of THIS command. It differs from the shared decoder only in accepting 0, which is
 * the legal version of a work centre still on the default (a stale first save meets `currentVersion` >= 1; a save
 * that expected a revision after the calendar was never saved meets `currentVersion` 0).
 */
export function decodeCalendarVersionConflict(raw: unknown): VersionConflict | null {
  if (!isRecord(raw)) return null
  const conflict = raw.conflict
  if (!isRecord(conflict)) return null
  const { expectedVersion, currentVersion } = conflict
  if (!isCalendarVersion(expectedVersion) || !isCalendarVersion(currentVersion)) return null
  return { expectedVersion, currentVersion }
}

/** The order the server stores and answers: by weekday (Monday first), then start time. */
export function sortShifts(shifts: readonly CalendarShift[]): CalendarShift[] {
  return [...shifts].sort((left, right) => {
    const byDay = CALENDAR_DAYS.indexOf(left.day) - CALENDAR_DAYS.indexOf(right.day)
    if (byDay !== 0) return byDay
    return left.start < right.start ? -1 : left.start > right.start ? 1 : 0
  })
}
