/**
 * F-029 S1/S2 wire contract (URUN_F029 spec §5.4(4); URN-D2…D5) — the paths, request bodies and strict runtime decoder of
 * the company's unit profile routes (`SettingsUnitProfileEndpoints`, `SettingsUnitProfilesV1.cs`):
 *
 *   GET  /settings/unit-profiles                     -> UnitProfileViewV1
 *   POST /settings/unit-profiles/baseline-transfer   -> 204 (one time, values unchanged, URN-D5)
 *   POST /settings/unit-profiles/versions            -> 204 (one unit, today or later, URN-D4)
 *
 * A unit profile says which unit the company may use, from when and at what quantity scale. Until the company's one-time
 * baseline transfer the rows are the approved catalog's entries (no recorder, no reason) and no version can be recorded;
 * afterwards the rows are the stored Settings records. The screen never converts or rounds a quantity: the scale is a
 * stored value the backend applies.
 *
 * The decoder fails CLOSED: one bad entry invalidates the whole answer, keys must match the contract exactly, a scale
 * outside the published bounds (a kilogram profile above Ö2's bound included) is rejected, and a view for another company
 * is not this answer.
 */

import { isDateText, isUuid } from './discountLimitContract'
import { SALES_POLICY_STATE, decodeMeta, type SalesPolicyVersionMeta } from './salesPolicyContract'

export const UNIT_PROFILES_PATH = '/settings/unit-profiles'
export const UNIT_PROFILE_BASELINE_PATH = `${UNIT_PROFILES_PATH}/baseline-transfer`
export const UNIT_PROFILE_VERSIONS_PATH = `${UNIT_PROFILES_PATH}/versions`

/** The stable problem codes the screen distinguishes (`UnitProfileProblemCodesV1`). */
export const UNIT_PROFILE_PROBLEM = {
  requestInvalid: 'settings_unit_profile_request_invalid',
  commandMetadataInvalid: 'settings_unit_profile_command_metadata_invalid',
  reasonRequired: 'settings_unit_profile_reason_required',
  reasonTooLong: 'settings_unit_profile_reason_too_long',
  idempotencyConflict: 'settings_unit_profile_idempotency_conflict',
  versionConflict: 'settings_unit_profile_version_conflict',
  effectiveFromPast: 'settings_unit_profile_effective_from_past',
  baselineRequired: 'settings_unit_profile_baseline_required',
  alreadyTransferred: 'settings_unit_profile_already_transferred',
  unitUnknown: 'settings_unit_profile_unit_unknown',
  providerUnavailable: 'settings_provider_unavailable',
  accessDenied: 'settings_access_denied',
} as const

/** Refusals after which the screen's copy is stale (version moved, baseline state changed): close and re-read. */
export const UNIT_PROFILE_STALE_CODES: readonly string[] = [
  UNIT_PROFILE_PROBLEM.versionConflict,
  UNIT_PROFILE_PROBLEM.baselineRequired,
  UNIT_PROFILE_PROBLEM.alreadyTransferred,
]

/** Technical bounds mirrored from `UnitProfileInputLimitsV1` (storage and transport shape; Ö2's kilogram bound). */
export const UNIT_PROFILE_INPUT = {
  reasonMaximumLength: 4000,
  minimumScale: 0,
  maximumScale: 28,
  kilogramMaximumScale: 3,
} as const

export interface UnitProfileVersion extends SalesPolicyVersionMeta {
  /** The quantity decimals the company uses for this unit from the version's start. */
  readonly scale: number
}

export interface UnitProfileUnit {
  readonly unitId: string
  readonly unitCode: string
  readonly dimensionCode: string
  readonly isKilogram: boolean
  /** The unit's latest stored version ("0" before the transfer or when none): the next record's expected version. */
  readonly version: string
  readonly versions: readonly UnitProfileVersion[]
}

/** A unit identity a new profile may be recorded for, with the largest scale the server accepts for it. */
export interface UnitProfileUnitOption {
  readonly unitId: string
  readonly unitCode: string
  readonly dimensionCode: string
  readonly isKilogram: boolean
  readonly maximumScale: number
}

export interface UnitProfileView {
  readonly companyId: string
  /** The server's business date (Europe/Istanbul): the earliest start a new version may have. */
  readonly today: string
  /** False: the approved catalog still answers every read and nothing can be recorded until the one-time transfer. */
  readonly isBaselineTransferred: boolean
  readonly units: readonly UnitProfileUnit[]
  readonly unitOptions: readonly UnitProfileUnitOption[]
}

export interface TransferUnitProfileBaselineBody {
  readonly reason: string
}

export interface RecordUnitProfileBody {
  readonly expectedVersion: string
  readonly unitId: string
  readonly scale: number
  readonly isActive: boolean
  readonly reason: string
  /** Null = today (Europe/Istanbul, the server's business date). */
  readonly effectiveFrom: string | null
}

const VERSION = /^(0|[1-9][0-9]*)$/
const WHOLE_NUMBER = /^[0-9]{1,2}$/
const CODE_MAXIMUM_LENGTH = 64

const VERSION_FIELDS = [
  'profileId',
  'version',
  'scale',
  'isActive',
  'effectiveFrom',
  'effectiveThrough',
  'state',
  'sourceKind',
  'decisionReference',
  'recordedBy',
  'recordedAtUtc',
  'reason',
] as const
const UNIT_FIELDS = ['unitId', 'unitCode', 'dimensionCode', 'isKilogram', 'version', 'versions'] as const
const OPTION_FIELDS = ['unitId', 'unitCode', 'dimensionCode', 'isKilogram', 'maximumScale'] as const
const VIEW_FIELDS = ['companyId', 'today', 'isBaselineTransferred', 'units', 'unitOptions'] as const

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** The object's key set equals the contract's exactly: an extra or a missing key is another contract. */
function hasExactKeys(value: Record<string, unknown>, fields: readonly string[]): boolean {
  const keys = Object.keys(value)
  return keys.length === fields.length && fields.every((field) => Object.prototype.hasOwnProperty.call(value, field))
}

function isGuid(value: unknown): value is string {
  return isUuid(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)
}

function isCode(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= CODE_MAXIMUM_LENGTH
}

/** The largest scale the server accepts for a unit: Ö2's kilogram bound, otherwise the technical maximum. */
export function maximumScaleOf(isKilogram: boolean): number {
  return isKilogram ? UNIT_PROFILE_INPUT.kilogramMaximumScale : UNIT_PROFILE_INPUT.maximumScale
}

function isScale(value: unknown, isKilogram: boolean): value is number {
  return (
    typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= UNIT_PROFILE_INPUT.minimumScale &&
    value <= maximumScaleOf(isKilogram)
  )
}

/** A typed scale inside the unit's bound; otherwise null. The server judges it again. */
export function parseScale(text: string, maximumScale: number): number | null {
  const trimmed = text.trim()
  if (!WHOLE_NUMBER.test(trimmed)) return null
  const value = Number(trimmed)
  return Number.isInteger(value) && value >= UNIT_PROFILE_INPUT.minimumScale && value <= maximumScale ? value : null
}

function decodeVersion(raw: unknown, transferred: boolean, isKilogram: boolean): UnitProfileVersion | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VERSION_FIELDS)) return null
  const meta = decodeMeta(raw, transferred)
  if (meta === null || !isScale(raw.scale, isKilogram)) return null
  return { ...meta, scale: raw.scale }
}

function decodeUnit(raw: unknown, transferred: boolean): UnitProfileUnit | null {
  if (!isRecord(raw) || !hasExactKeys(raw, UNIT_FIELDS)) return null
  const { unitId, unitCode, dimensionCode, isKilogram, version, versions } = raw
  if (!isGuid(unitId) || !isCode(unitCode) || !isCode(dimensionCode) || typeof isKilogram !== 'boolean') return null
  if (typeof version !== 'string' || !VERSION.test(version) || !Array.isArray(versions) || versions.length === 0) {
    return null
  }
  const decoded: UnitProfileVersion[] = []
  for (const entry of versions) {
    const item = decodeVersion(entry, transferred, isKilogram)
    if (item === null) return null
    decoded.push(item)
  }
  if (new Set(decoded.map((entry) => entry.version)).size !== decoded.length) return null
  if (decoded.filter((entry) => entry.state === SALES_POLICY_STATE.inForce).length > 1) return null
  if (!transferred) {
    // Before the transfer nothing is stored: the next record's expected version is "0".
    if (version !== '0') return null
  } else {
    // The unit's version is its latest stored one: it names an entry and no entry is newer.
    const latest = decoded.reduce((max, entry) => (BigInt(entry.version) > BigInt(max) ? entry.version : max), '0')
    if (latest !== version) return null
  }
  return { unitId, unitCode, dimensionCode, isKilogram, version, versions: decoded }
}

function decodeOption(raw: unknown): UnitProfileUnitOption | null {
  if (!isRecord(raw) || !hasExactKeys(raw, OPTION_FIELDS)) return null
  const { unitId, unitCode, dimensionCode, isKilogram, maximumScale } = raw
  if (!isGuid(unitId) || !isCode(unitCode) || !isCode(dimensionCode) || typeof isKilogram !== 'boolean') return null
  if (maximumScale !== maximumScaleOf(isKilogram)) return null
  return { unitId, unitCode, dimensionCode, isKilogram, maximumScale }
}

/**
 * The unit profiles of the company the request named. One unit appears once, one version number names one entry of a
 * unit, at most one entry of a unit is in force, and one unit identity is offered once. The screen shows the entries in
 * the server's order and derives nothing from the dates.
 */
export function decodeUnitProfileView(raw: unknown, companyId: string): UnitProfileView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_FIELDS)) return null
  const { today, isBaselineTransferred, units, unitOptions } = raw
  if (raw.companyId !== companyId || !isDateText(today)) return null
  if (typeof isBaselineTransferred !== 'boolean' || !Array.isArray(units) || !Array.isArray(unitOptions)) return null
  const decodedUnits: UnitProfileUnit[] = []
  for (const entry of units) {
    const unit = decodeUnit(entry, isBaselineTransferred)
    if (unit === null) return null
    decodedUnits.push(unit)
  }
  if (new Set(decodedUnits.map((unit) => unit.unitId)).size !== decodedUnits.length) return null
  const decodedOptions: UnitProfileUnitOption[] = []
  for (const entry of unitOptions) {
    const option = decodeOption(entry)
    if (option === null) return null
    decodedOptions.push(option)
  }
  if (new Set(decodedOptions.map((option) => option.unitId)).size !== decodedOptions.length) return null
  return {
    companyId,
    today,
    isBaselineTransferred,
    units: decodedUnits,
    unitOptions: decodedOptions,
  }
}

/** The entry of a unit in force today, as the server marked it. */
export function inForceUnitProfile(versions: readonly UnitProfileVersion[]): UnitProfileVersion | null {
  return versions.find((entry) => entry.state === SALES_POLICY_STATE.inForce) ?? null
}
