/**
 * URT-D89 wire contract — the paths, request body and strict runtime decoder of the work center capacity routes
 * (`SettingsWorkCenterCapacityEndpoints`, `SettingsWorkCenterCapacitiesV1.cs`):
 *
 *   GET  /settings/work-center-capacities                         -> WorkCenterCapacityViewV1
 *   POST /settings/work-center-capacities/{workCenterId}/versions -> 204
 *
 * Each work center of the company may carry a capacity in input kg per hour (URT-D87) and an optional due-date safety
 * buffer in hours or days. A work center absent from the view has no capacity: the due-date suggestion answers
 * "hesaplanamadı" for it (fail-closed) and its next record's expected version is "0". No capacity or buffer is seeded
 * (URT-D91). Names and the list of work centers come from the Organization owner, not from this contract.
 *
 * Decimals travel as invariant text ("1250.5"), never as JSON numbers, so a value is shown exactly as stored. The decoder
 * fails CLOSED: one bad entry invalidates the whole answer, keys must match the contract exactly, a buffer is either fully
 * absent or a positive value with a known unit, and in each timeline exactly the newest entry is the current one.
 */

import { isUuid } from './discountLimitContract'

export const WORK_CENTER_CAPACITY_PATH = '/settings/work-center-capacities'

export function workCenterCapacityVersionsPath(workCenterId: string): string {
  return `${WORK_CENTER_CAPACITY_PATH}/${encodeURIComponent(workCenterId)}/versions`
}

/** The capacity unit the server publishes (`WorkCenterCapacityKeysV1.InputCapacityUnit`). */
export const WORK_CENTER_CAPACITY_UNIT = 'kg/h'

/** The stable problem codes the screen distinguishes (`WorkCenterCapacityProblemCodesV1`); others are a generic refusal. */
export const WORK_CENTER_CAPACITY_PROBLEM = {
  requestInvalid: 'settings_work_center_capacity_request_invalid',
  commandMetadataInvalid: 'settings_work_center_capacity_command_metadata_invalid',
  reasonRequired: 'settings_work_center_capacity_reason_required',
  reasonTooLong: 'settings_work_center_capacity_reason_too_long',
  workCenterNotFound: 'settings_work_center_capacity_work_center_not_found',
  idempotencyConflict: 'settings_work_center_capacity_idempotency_conflict',
  versionConflict: 'settings_work_center_capacity_version_conflict',
  providerUnavailable: 'settings_provider_unavailable',
  profileInvalid: 'settings_profile_invalid',
  accessDenied: 'settings_access_denied',
} as const

/** Refusals after which the screen's copy is stale: the form closes and the view is read again. */
export const WORK_CENTER_CAPACITY_STALE_CODES: readonly string[] = [
  WORK_CENTER_CAPACITY_PROBLEM.versionConflict,
  WORK_CENTER_CAPACITY_PROBLEM.workCenterNotFound,
]

/** Technical bounds mirrored from `WorkCenterCapacityInputLimitsV1` (storage and transport shape, not business policy). */
export const WORK_CENTER_CAPACITY_INPUT = {
  reasonMaximumLength: 4000,
  capacityMaximumIntegerDigits: 12,
  capacityMaximumScale: 6,
  safetyBufferMaximumIntegerDigits: 6,
  safetyBufferMaximumScale: 3,
} as const

/** The closed list of buffer units (`WorkCenterSafetyBufferUnitsV1`). */
export const WORK_CENTER_SAFETY_BUFFER_UNIT = {
  hours: 'Hours',
  days: 'Days',
} as const
export type WorkCenterSafetyBufferUnit =
  (typeof WORK_CENTER_SAFETY_BUFFER_UNIT)[keyof typeof WORK_CENTER_SAFETY_BUFFER_UNIT]
export const WORK_CENTER_SAFETY_BUFFER_UNITS: readonly WorkCenterSafetyBufferUnit[] = [
  WORK_CENTER_SAFETY_BUFFER_UNIT.hours,
  WORK_CENTER_SAFETY_BUFFER_UNIT.days,
]

export interface WorkCenterCapacityVersion {
  readonly profileId: string
  readonly version: string
  /** Lossless decimal text, greater than zero. */
  readonly inputCapacityKgPerHour: string
  /** Lossless decimal text greater than zero, or null when no buffer is recorded (counted as zero, URT-D89). */
  readonly safetyBufferValue: string | null
  readonly safetyBufferUnit: WorkCenterSafetyBufferUnit | null
  readonly isCurrent: boolean
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface WorkCenterCapacityTimeline {
  readonly workCenterId: string
  /** The latest stored version: the next record's expected version. */
  readonly version: string
  /** Newest first; never empty. */
  readonly versions: readonly WorkCenterCapacityVersion[]
}

export interface WorkCenterCapacityView {
  readonly capacityUnit: typeof WORK_CENTER_CAPACITY_UNIT
  /** Only the work centers with a recorded capacity; at most one timeline per work center. */
  readonly workCenters: readonly WorkCenterCapacityTimeline[]
}

export interface RecordWorkCenterCapacityBody {
  readonly expectedVersion: string
  readonly inputCapacityKgPerHour: string
  readonly safetyBufferValue: string | null
  readonly safetyBufferUnit: WorkCenterSafetyBufferUnit | null
  readonly reason: string
}

const VERSION = /^(0|[1-9][0-9]*)$/
const POSITIVE_VERSION = /^[1-9][0-9]*$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64

const VERSION_FIELDS = [
  'profileId',
  'version',
  'inputCapacityKgPerHour',
  'safetyBufferValue',
  'safetyBufferUnit',
  'isCurrent',
  'recordedBy',
  'recordedAtUtc',
  'reason',
] as const
const TIMELINE_FIELDS = ['workCenterId', 'version', 'versions'] as const
const VIEW_FIELDS = ['capacityUnit', 'workCenters'] as const

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, fields: readonly string[]): boolean {
  const keys = Object.keys(value)
  return keys.length === fields.length && fields.every((field) => Object.prototype.hasOwnProperty.call(value, field))
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isInstant(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= INSTANT_MAXIMUM_LENGTH &&
    INSTANT.test(value) &&
    !Number.isNaN(Date.parse(value))
  )
}

function isGuid(value: unknown): value is string {
  return isUuid(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)
}

function decimalPattern(maximumIntegerDigits: number, maximumScale: number): RegExp {
  return new RegExp(`^(0|[1-9][0-9]{0,${maximumIntegerDigits - 1}})(\\.[0-9]{1,${maximumScale}})?$`)
}

const CAPACITY_TEXT = decimalPattern(
  WORK_CENTER_CAPACITY_INPUT.capacityMaximumIntegerDigits,
  WORK_CENTER_CAPACITY_INPUT.capacityMaximumScale,
)
const BUFFER_TEXT = decimalPattern(
  WORK_CENTER_CAPACITY_INPUT.safetyBufferMaximumIntegerDigits,
  WORK_CENTER_CAPACITY_INPUT.safetyBufferMaximumScale,
)

/** True when the decimal text (already shape-checked) is not zero ("0", "0.000"). */
function isAboveZero(text: string): boolean {
  return /[1-9]/.test(text)
}

export function isCapacityText(value: unknown): value is string {
  return typeof value === 'string' && CAPACITY_TEXT.test(value) && isAboveZero(value)
}

export function isSafetyBufferText(value: unknown): value is string {
  return typeof value === 'string' && BUFFER_TEXT.test(value) && isAboveZero(value)
}

export function isSafetyBufferUnit(value: unknown): value is WorkCenterSafetyBufferUnit {
  return WORK_CENTER_SAFETY_BUFFER_UNITS.includes(value as WorkCenterSafetyBufferUnit)
}

/**
 * The typed text as the server's lossless decimal: surrounding spaces dropped and a single Turkish decimal comma read as
 * the point ("1250,5" → "1250.5"); null when the result is not a valid value of the shape. Nothing is rounded.
 */
function normalizeDecimal(text: string, accepts: (value: unknown) => value is string): string | null {
  const trimmed = text.trim()
  const normalized = (trimmed.match(/,/g)?.length ?? 0) === 1 && !trimmed.includes('.') ? trimmed.replace(',', '.') : trimmed
  return accepts(normalized) ? normalized : null
}

export function parseCapacityInput(text: string): string | null {
  return normalizeDecimal(text, isCapacityText)
}

export function parseSafetyBufferInput(text: string): string | null {
  return normalizeDecimal(text, isSafetyBufferText)
}

function decodeVersion(raw: unknown): WorkCenterCapacityVersion | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VERSION_FIELDS)) return null
  const { profileId, version, inputCapacityKgPerHour, safetyBufferValue, safetyBufferUnit, isCurrent } = raw
  const { recordedBy, recordedAtUtc, reason } = raw
  if (!isGuid(profileId) || typeof version !== 'string' || !POSITIVE_VERSION.test(version)) return null
  if (!isCapacityText(inputCapacityKgPerHour)) return null
  const noBuffer = safetyBufferValue === null && safetyBufferUnit === null
  const buffer = isSafetyBufferText(safetyBufferValue) && isSafetyBufferUnit(safetyBufferUnit)
  if (!noBuffer && !buffer) return null
  if (typeof isCurrent !== 'boolean') return null
  if (!isNonEmpty(recordedBy) || !isInstant(recordedAtUtc)) return null
  if (!isNonEmpty(reason) || reason.length > WORK_CENTER_CAPACITY_INPUT.reasonMaximumLength) return null
  return {
    profileId,
    version,
    inputCapacityKgPerHour,
    safetyBufferValue: noBuffer ? null : (safetyBufferValue as string),
    safetyBufferUnit: noBuffer ? null : (safetyBufferUnit as WorkCenterSafetyBufferUnit),
    isCurrent,
    recordedBy,
    recordedAtUtc,
    reason,
  }
}

/** One work center's timeline: newest first, strictly descending versions, exactly the first entry current. */
function decodeTimeline(raw: unknown): WorkCenterCapacityTimeline | null {
  if (!isRecord(raw) || !hasExactKeys(raw, TIMELINE_FIELDS)) return null
  const { workCenterId, version, versions } = raw
  if (!isGuid(workCenterId) || typeof version !== 'string' || !VERSION.test(version) || !Array.isArray(versions)) {
    return null
  }
  const decoded: WorkCenterCapacityVersion[] = []
  for (const entry of versions) {
    const item = decodeVersion(entry)
    if (item === null) return null
    decoded.push(item)
  }
  // A listed work center has a recorded capacity: an empty timeline is another contract.
  if (decoded.length === 0 || decoded[0].version !== version) return null
  for (let index = 0; index < decoded.length; index += 1) {
    if (decoded[index].isCurrent !== (index === 0)) return null
    if (index > 0 && BigInt(decoded[index].version) >= BigInt(decoded[index - 1].version)) return null
  }
  return { workCenterId: workCenterId.toLowerCase(), version, versions: decoded }
}

/** The company's capacity view. Unknown unit, a duplicated work center or one bad timeline fails the whole answer. */
export function decodeWorkCenterCapacityView(raw: unknown): WorkCenterCapacityView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_FIELDS)) return null
  const { capacityUnit, workCenters } = raw
  if (capacityUnit !== WORK_CENTER_CAPACITY_UNIT || !Array.isArray(workCenters)) return null
  const decoded: WorkCenterCapacityTimeline[] = []
  const seen = new Set<string>()
  for (const entry of workCenters) {
    const timeline = decodeTimeline(entry)
    if (timeline === null || seen.has(timeline.workCenterId)) return null
    seen.add(timeline.workCenterId)
    decoded.push(timeline)
  }
  return { capacityUnit: WORK_CENTER_CAPACITY_UNIT, workCenters: decoded }
}
