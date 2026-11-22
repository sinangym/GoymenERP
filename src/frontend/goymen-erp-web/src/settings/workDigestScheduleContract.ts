/**
 * YOL-D25 (AI-D29, WP2) — `GET /api/v1/settings/company-policies/work-digest-schedule`: the three daily digest times of
 * "özet" notifications, each a versioned company policy (minutes after local midnight in the company's business time zone),
 * released on business days only. A slot never recorded has version "0", no history and the decided default (09:00,
 * 13:00, 17:00). Strict decoder: exact keys and order, anything else is `null` (fail closed).
 */

export type WorkDigestSlot = 'morning' | 'midday' | 'evening'
export const WORK_DIGEST_SLOTS: readonly WorkDigestSlot[] = ['morning', 'midday', 'evening']
export const WORK_DIGEST_SCHEDULE_PATH = '/settings/company-policies/work-digest-schedule'
export const workDigestTimeVersionsPath = (slot: WorkDigestSlot) => `${WORK_DIGEST_SCHEDULE_PATH}/${slot}/versions`
/** YOL-D25 decided defaults (`SettingsWorkDigestScheduleLimitsV1.DecidedDefault*Minute`). */
export const WORK_DIGEST_DEFAULT: Readonly<Record<WorkDigestSlot, number>> = { morning: 540, midday: 780, evening: 1020 }
export const WORK_DIGEST_MAXIMUM_MINUTE = 1439

export interface WorkDigestTimeVersion {
  readonly version: string; readonly value: number; readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string
}
export interface WorkDigestTime {
  readonly slot: WorkDigestSlot; readonly minute: number; readonly version: string; readonly versions: readonly WorkDigestTimeVersion[]
}
export interface WorkDigestSchedule {
  readonly companyId: string; readonly businessDaysOnly: boolean; readonly times: readonly WorkDigestTime[]
}
export interface RecordWorkDigestTimeBody { readonly expectedVersion: string; readonly minute: number; readonly reason: string }

const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const nonempty = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const version = (raw: unknown): raw is string =>
  typeof raw === 'string' && /^(0|[1-9]\d{0,18})$/.test(raw) && BigInt(raw) <= 9223372036854775807n
export const isMinute = (raw: unknown): raw is number =>
  typeof raw === 'number' && Number.isInteger(raw) && raw >= 0 && raw <= WORK_DIGEST_MAXIMUM_MINUTE
const instant = (raw: unknown): raw is string =>
  typeof raw === 'string' && raw.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(raw) && Number.isFinite(Date.parse(raw))

const ROOT_KEYS = ['companyId', 'businessDaysOnly', 'times'] as const
const TIME_KEYS = ['slot', 'minute', 'version', 'versions'] as const
const VERSION_KEYS = ['version', 'value', 'recordedBy', 'recordedAtUtc', 'reason'] as const

function decodeTime(raw: unknown, slot: WorkDigestSlot): WorkDigestTime | null {
  if (!object(raw) || !exact(raw, TIME_KEYS) || raw.slot !== slot || !isMinute(raw.minute) || !version(raw.version)
    || !Array.isArray(raw.versions)) return null
  const history: WorkDigestTimeVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !exact(item, VERSION_KEYS) || !version(item.version) || item.version === '0' || !isMinute(item.value)
      || !nonempty(item.recordedBy) || !instant(item.recordedAtUtc) || !nonempty(item.reason)
      || (history.length > 0 && BigInt(history[history.length - 1].version) <= BigInt(item.version))) return null
    history.push({ version: item.version, value: item.value, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  // Version "0" is the decided default with no history; otherwise the newest history row is the value in force.
  if (raw.version === '0'
    ? raw.minute !== WORK_DIGEST_DEFAULT[slot] || history.length !== 0
    : history[0]?.version !== raw.version || history[0]?.value !== raw.minute) return null
  return { slot, minute: raw.minute, version: raw.version, versions: history }
}

export function decodeWorkDigestSchedule(raw: unknown, company: string): WorkDigestSchedule | null {
  if (!object(raw) || !exact(raw, ROOT_KEYS) || typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== company.toLowerCase()
    || raw.businessDaysOnly !== true || !Array.isArray(raw.times) || raw.times.length !== WORK_DIGEST_SLOTS.length) return null
  const times: WorkDigestTime[] = []
  for (const [index, slot] of WORK_DIGEST_SLOTS.entries()) {
    const time = decodeTime(raw.times[index], slot)
    if (time === null) return null
    times.push(time)
  }
  return { companyId: company, businessDaysOnly: true, times }
}

/** `HH:mm` of a minute after midnight. */
export const clockText = (minute: number) =>
  `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`
/** `HH:mm` (00:00–23:59) to a minute after midnight, or null. */
export function parseClock(text: string): number | null {
  const match = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(text.trim())
  return match === null ? null : Number(match[1]) * 60 + Number(match[2])
}
