/**
 * W1-A (YOL-D17, YOL-D18) — `GET /api/v1/settings/company-policies/work-approval-duration`: the lengths of the two
 * approval duration classes (hours, business days), each with its own version history. WP1 D3: a class that was never
 * recorded has version "0", no history and the decided YOL-D18 default as its value (4 hours / 2 business days); a recorded
 * version replaces it.
 */

export type WorkApprovalDurationClassKey = 'hours' | 'businessDays'

export const WORK_APPROVAL_DURATION_PATH = '/settings/company-policies/work-approval-duration'
export const workApprovalDurationVersionsPath = (key: WorkApprovalDurationClassKey) =>
  `${WORK_APPROVAL_DURATION_PATH}/${key === 'hours' ? 'hours' : 'business-days'}/versions`

/** The server's input range (`SettingsWorkApprovalDurationLimitsV1`). */
export const WORK_APPROVAL_DURATION_RANGE: Readonly<Record<WorkApprovalDurationClassKey, { readonly min: number; readonly max: number }>> = {
  hours: { min: 1, max: 168 },
  businessDays: { min: 1, max: 30 },
}

/** YOL-D18 (D3): the decided default in force as version "0" (`SettingsWorkApprovalDurationLimitsV1.DecidedDefault*`). */
export const WORK_APPROVAL_DURATION_DEFAULT: Readonly<Record<WorkApprovalDurationClassKey, number>> = {
  hours: 4,
  businessDays: 2,
}

export interface WorkApprovalDurationVersion {
  readonly version: string
  readonly value: number
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface WorkApprovalDurationClass {
  readonly value: number | null
  readonly version: string
  readonly versions: readonly WorkApprovalDurationVersion[]
}

export interface WorkApprovalDurationPolicy {
  readonly companyId: string
  readonly hours: WorkApprovalDurationClass
  readonly businessDays: WorkApprovalDurationClass
}

export interface RecordWorkApprovalDurationBody {
  readonly expectedVersion: string
  readonly value: number
  readonly reason: string
}

const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const nonempty = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
const version = (raw: unknown): raw is string =>
  typeof raw === 'string' && /^(0|[1-9]\d{0,18})$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const positiveInt = (raw: unknown): raw is number =>
  typeof raw === 'number' && Number.isInteger(raw) && raw >= 1 && raw <= 2147483647
const instant = (raw: unknown): raw is string =>
  typeof raw === 'string' && raw.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(raw) && Number.isFinite(Date.parse(raw))

const CLASS_KEYS = ['value', 'version', 'versions'] as const
const VERSION_KEYS = ['version', 'value', 'recordedBy', 'recordedAtUtc', 'reason'] as const
const ROOT_KEYS = ['companyId', 'hours', 'businessDays'] as const

function decodeClass(key: WorkApprovalDurationClassKey, raw: unknown): WorkApprovalDurationClass | null {
  if (!object(raw) || !exact(raw, CLASS_KEYS) || !version(raw.version) || !Array.isArray(raw.versions)) return null
  if (raw.value !== null && !positiveInt(raw.value)) return null
  const history: WorkApprovalDurationVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !exact(item, VERSION_KEYS) || !version(item.version) || item.version === '0' || !positiveInt(item.value)
      || !nonempty(item.recordedBy) || !nonempty(item.reason) || !instant(item.recordedAtUtc)
      || BigInt(item.version) > BigInt(raw.version)
      || (history.length > 0 && BigInt(history[history.length - 1].version) <= BigInt(item.version))) return null
    history.push({ version: item.version, value: item.value, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc, reason: item.reason })
  }
  // Never recorded: the decided default, no history. Recorded: the newest version is the one in force.
  if (raw.version === '0'
    ? raw.value !== WORK_APPROVAL_DURATION_DEFAULT[key] || history.length !== 0
    : history[0]?.version !== raw.version || history[0]?.value !== raw.value) return null
  return { value: raw.value as number | null, version: raw.version, versions: history }
}

export function decodeWorkApprovalDurationPolicy(raw: unknown, company: string): WorkApprovalDurationPolicy | null {
  if (!object(raw) || !exact(raw, ROOT_KEYS) || typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== company.toLowerCase()) return null
  const hours = decodeClass('hours', raw.hours)
  const businessDays = decodeClass('businessDays', raw.businessDays)
  return hours === null || businessDays === null ? null : { companyId: company, hours, businessDays }
}
