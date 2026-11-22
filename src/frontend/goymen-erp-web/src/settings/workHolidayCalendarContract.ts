/**
 * YOL-D22 — `GET /api/v1/settings/company-policies/work-holiday-calendar/{year}`: the company's official holiday and eve
 * list of one year, versioned. A year never recorded is "not defined" (version "0", no days, no history): the Work inbox
 * then deducts only weekends from a business-day due time and says so. An explicitly recorded empty list is defined.
 */

export const WORK_HOLIDAY_CALENDAR_PATH = '/settings/company-policies/work-holiday-calendar'
export const workHolidayCalendarPath = (year: number) => `${WORK_HOLIDAY_CALENDAR_PATH}/${year}`
export const workHolidayCalendarVersionsPath = (year: number) => `${workHolidayCalendarPath(year)}/versions`

/** The server's input bounds (`SettingsWorkHolidayCalendarLimitsV1`). */
export const WORK_HOLIDAY_CALENDAR_LIMITS = { minimumYear: 2000, maximumYear: 2100, maximumDays: 60 } as const

export const HOLIDAY_KIND = 'Holiday'
export const EVE_KIND = 'Eve'
export type WorkHolidayKind = typeof HOLIDAY_KIND | typeof EVE_KIND

export interface WorkHolidayDay {
  readonly date: string
  readonly kind: WorkHolidayKind
}

export interface WorkHolidayCalendarVersion {
  readonly version: string
  readonly dayCount: number
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface WorkHolidayCalendar {
  readonly companyId: string
  readonly year: number
  readonly isDefined: boolean
  readonly version: string
  readonly days: readonly WorkHolidayDay[]
  readonly versions: readonly WorkHolidayCalendarVersion[]
}

export interface RecordWorkHolidayCalendarBody {
  readonly expectedVersion: string
  readonly days: readonly WorkHolidayDay[]
  readonly reason: string
}

const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const nonempty = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const version = (raw: unknown): raw is string =>
  typeof raw === 'string' && /^(0|[1-9]\d{0,18})$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const instant = (raw: unknown): raw is string =>
  typeof raw === 'string' && raw.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(raw) && Number.isFinite(Date.parse(raw))
export const isHolidayKind = (raw: unknown): raw is WorkHolidayKind => raw === HOLIDAY_KIND || raw === EVE_KIND

/** A real calendar day `yyyy-MM-dd` inside `year`. */
export function isDayOfYear(raw: unknown, year: number): raw is string {
  if (typeof raw !== 'string') return false
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw)
  if (match === null || Number(match[1]) !== year) return false
  const parsed = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
  return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === Number(match[2]) - 1 && parsed.getUTCDate() === Number(match[3])
}

export const isCalendarYear = (year: number) =>
  Number.isInteger(year) && year >= WORK_HOLIDAY_CALENDAR_LIMITS.minimumYear && year <= WORK_HOLIDAY_CALENDAR_LIMITS.maximumYear

const ROOT_KEYS = ['companyId', 'year', 'isDefined', 'version', 'days', 'versions'] as const
const DAY_KEYS = ['date', 'kind'] as const
const VERSION_KEYS = ['version', 'dayCount', 'recordedBy', 'recordedAtUtc', 'reason'] as const

export function decodeWorkHolidayCalendar(raw: unknown, company: string, year: number): WorkHolidayCalendar | null {
  if (!object(raw) || !exact(raw, ROOT_KEYS) || typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== company.toLowerCase()) return null
  if (raw.year !== year || typeof raw.isDefined !== 'boolean' || !version(raw.version)) return null
  if (!Array.isArray(raw.days) || raw.days.length > WORK_HOLIDAY_CALENDAR_LIMITS.maximumDays || !Array.isArray(raw.versions)) return null
  const days: WorkHolidayDay[] = []
  for (const day of raw.days) {
    // Days are unique and in date order (ISO dates compare as text).
    if (!object(day) || !exact(day, DAY_KEYS) || !isDayOfYear(day.date, year) || !isHolidayKind(day.kind)
      || (days.length > 0 && days[days.length - 1].date >= day.date)) return null
    days.push({ date: day.date, kind: day.kind })
  }
  const history: WorkHolidayCalendarVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !exact(item, VERSION_KEYS) || !version(item.version) || item.version === '0'
      || typeof item.dayCount !== 'number' || !Number.isInteger(item.dayCount) || item.dayCount < 0
      || item.dayCount > WORK_HOLIDAY_CALENDAR_LIMITS.maximumDays
      || !nonempty(item.recordedBy) || !nonempty(item.reason) || !instant(item.recordedAtUtc)
      || BigInt(item.version) > BigInt(raw.version)
      || (history.length > 0 && BigInt(history[history.length - 1].version) <= BigInt(item.version))) return null
    history.push({ version: item.version, dayCount: item.dayCount, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  // Not defined: nothing recorded. Defined: the newest version is in force and holds exactly the listed days.
  if (!raw.isDefined ? raw.version !== '0' || days.length !== 0 || history.length !== 0
    : raw.version === '0' || history[0]?.version !== raw.version || history[0]?.dayCount !== days.length) return null
  return { companyId: company, year, isDefined: raw.isDefined, version: raw.version, days, versions: history }
}
