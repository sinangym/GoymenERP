/**
 * F-004 / KIM-D37 wire contract — the paths, request body and strict runtime decoder of the company's unit conversion
 * policy routes (`SettingsUnitConversionPolicyEndpoints`, `SettingsUnitConversionPoliciesV1.cs`):
 *
 *   GET  /settings/unit-conversion-policy            -> UnitConversionPolicyViewV1
 *   POST /settings/unit-conversion-policy/versions   -> 204
 *
 * KIM-D37: there is no seed and no compiled default. An authorized administrator enters every value per company from
 * Ayarlar (audited); until then every dependent calculation is refused (fail-closed). The density, the rounding modes and
 * the tolerance stay the lossless TEXT and the closed codes the server sends — this screen never converts, rounds or
 * compares a value (the backend calculator does).
 *
 * The decoder fails CLOSED: one bad entry invalidates the whole answer, keys must match the contract exactly, unknown
 * states, rounding modes, boundaries and bases are rejected rather than guessed, and a view for another company is not
 * this answer.
 */

import { isDateText, isUuid } from './discountLimitContract'
import { SALES_POLICY_STATE, type SalesPolicyState } from './salesPolicyContract'

export const UNIT_CONVERSION_POLICY_PATH = '/settings/unit-conversion-policy'
export const UNIT_CONVERSION_POLICY_VERSIONS_PATH = `${UNIT_CONVERSION_POLICY_PATH}/versions`

/** The stable problem codes the screen distinguishes (`UnitConversionPolicyProblemCodesV1`). */
export const UNIT_CONVERSION_POLICY_PROBLEM = {
  requestInvalid: 'settings_unit_conversion_policy_request_invalid',
  commandMetadataInvalid: 'settings_unit_conversion_policy_command_metadata_invalid',
  reasonRequired: 'settings_unit_conversion_policy_reason_required',
  reasonTooLong: 'settings_unit_conversion_policy_reason_too_long',
  idempotencyConflict: 'settings_unit_conversion_policy_idempotency_conflict',
  versionConflict: 'settings_unit_conversion_policy_version_conflict',
  effectiveFromPast: 'settings_unit_conversion_policy_effective_from_past',
  providerUnavailable: 'settings_provider_unavailable',
  accessDenied: 'settings_access_denied',
} as const

/** Refusals after which the screen's copy is stale: the form closes and the view is read again. */
export const UNIT_CONVERSION_POLICY_STALE_CODES: readonly string[] = [UNIT_CONVERSION_POLICY_PROBLEM.versionConflict]

/** Technical bounds mirrored from `UnitConversionPolicyInputLimitsV1` (storage and transport shape, not policy). */
export const UNIT_CONVERSION_POLICY_INPUT = {
  reasonMaximumLength: 4000,
  minimumRoundingDecimals: 0,
  maximumRoundingDecimals: 12,
  densityMaximumIntegerDigits: 6,
  densityMaximumScale: 6,
  percentMaximumIntegerDigits: 3,
  percentMaximumScale: 4,
  minimumPercent: 0,
  maximumPercent: 100,
} as const

/** `UnitConversionRoundingModesV1`: each is a .NET `MidpointRounding` name; nothing here prefers one. */
export const UNIT_CONVERSION_ROUNDING = {
  toEven: 'ToEven',
  awayFromZero: 'AwayFromZero',
  toZero: 'ToZero',
  toNegativeInfinity: 'ToNegativeInfinity',
  toPositiveInfinity: 'ToPositiveInfinity',
} as const
export type UnitConversionRounding = (typeof UNIT_CONVERSION_ROUNDING)[keyof typeof UNIT_CONVERSION_ROUNDING]
export const UNIT_CONVERSION_ROUNDINGS: readonly UnitConversionRounding[] = Object.values(UNIT_CONVERSION_ROUNDING)

/**
 * `UnitConversionRoundingModesV1.DecidedCoefficientRounding` (KLN-D88): the coefficient is rounded half and above away from
 * zero; only its decimals count is a company setting. The server refuses any other coefficient mode. Stored history may
 * still show another mode, so the decoder keeps accepting every listed mode.
 */
export const DECIDED_COEFFICIENT_ROUNDING: UnitConversionRounding = UNIT_CONVERSION_ROUNDING.awayFromZero

/** `UnitConversionToleranceBoundariesV1` (BS-16). */
export const UNIT_CONVERSION_BOUNDARY = { inclusive: 'Inclusive', exclusive: 'Exclusive' } as const
export type UnitConversionBoundary = (typeof UNIT_CONVERSION_BOUNDARY)[keyof typeof UNIT_CONVERSION_BOUNDARY]
export const UNIT_CONVERSION_BOUNDARIES: readonly UnitConversionBoundary[] = Object.values(UNIT_CONVERSION_BOUNDARY)

/** `UnitConversionToleranceBasesV1` (BS-17). */
export const UNIT_CONVERSION_BASE = { theoreticalKg: 'TheoreticalKg', weighedKg: 'WeighedKg' } as const
export type UnitConversionBase = (typeof UNIT_CONVERSION_BASE)[keyof typeof UNIT_CONVERSION_BASE]
export const UNIT_CONVERSION_BASES: readonly UnitConversionBase[] = Object.values(UNIT_CONVERSION_BASE)

export interface UnitConversionPolicyVersion {
  readonly profileId: string
  readonly version: string
  readonly densityGramsPerCubicCentimetre: string
  readonly coefficientDecimals: number
  readonly coefficientRounding: UnitConversionRounding
  readonly documentTotalDecimals: number
  readonly documentTotalRounding: UnitConversionRounding
  readonly wholeUnitRounding: UnitConversionRounding
  readonly weighbridgeTolerancePercent: string
  readonly toleranceBoundary: UnitConversionBoundary
  readonly toleranceBase: UnitConversionBase
  readonly effectiveFrom: string
  readonly effectiveThrough: string | null
  readonly state: SalesPolicyState
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface UnitConversionPolicyView {
  readonly companyId: string
  /** The server's business date (Europe/Istanbul): the earliest start a new version may have. */
  readonly today: string
  /** The latest stored version ("0" when none): the next record's expected version. */
  readonly version: string
  /** Empty while the company has no policy: every dependent calculation is refused until one is recorded. */
  readonly versions: readonly UnitConversionPolicyVersion[]
}

export interface RecordUnitConversionPolicyBody {
  readonly expectedVersion: string
  readonly densityGramsPerCubicCentimetre: string
  readonly coefficientDecimals: number
  readonly coefficientRounding: UnitConversionRounding
  readonly documentTotalDecimals: number
  readonly documentTotalRounding: UnitConversionRounding
  readonly wholeUnitRounding: UnitConversionRounding
  readonly weighbridgeTolerancePercent: string
  readonly toleranceBoundary: UnitConversionBoundary
  readonly toleranceBase: UnitConversionBase
  readonly reason: string
  /** Null = today (Europe/Istanbul, the server's business date). */
  readonly effectiveFrom: string | null
}

const VERSION = /^(0|[1-9][0-9]*)$/
const POSITIVE_VERSION = /^[1-9][0-9]*$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64

/** A density as typed: no sign, no exponent, a dot as the decimal separator, inside the storage shape. */
const DENSITY_INPUT = new RegExp(
  `^[0-9]{1,${UNIT_CONVERSION_POLICY_INPUT.densityMaximumIntegerDigits}}` +
    `(\\.[0-9]{1,${UNIT_CONVERSION_POLICY_INPUT.densityMaximumScale}})?$`,
)
/** A density as the server writes it: canonical (no leading zero, no trailing fraction zero). */
const DENSITY_CANONICAL = new RegExp(
  `^(0|[1-9][0-9]{0,${UNIT_CONVERSION_POLICY_INPUT.densityMaximumIntegerDigits - 1}})` +
    `(\\.[0-9]{0,${UNIT_CONVERSION_POLICY_INPUT.densityMaximumScale - 1}}[1-9])?$`,
)
const PERCENT_INPUT = new RegExp(
  `^[0-9]{1,${UNIT_CONVERSION_POLICY_INPUT.percentMaximumIntegerDigits}}` +
    `(\\.[0-9]{1,${UNIT_CONVERSION_POLICY_INPUT.percentMaximumScale}})?$`,
)
const PERCENT_CANONICAL = new RegExp(
  `^(0|[1-9][0-9]{0,${UNIT_CONVERSION_POLICY_INPUT.percentMaximumIntegerDigits - 1}})` +
    `(\\.[0-9]{0,${UNIT_CONVERSION_POLICY_INPUT.percentMaximumScale - 1}}[1-9])?$`,
)
const WHOLE_NUMBER = /^[0-9]{1,2}$/

const VERSION_FIELDS = [
  'profileId',
  'version',
  'densityGramsPerCubicCentimetre',
  'coefficientDecimals',
  'coefficientRounding',
  'documentTotalDecimals',
  'documentTotalRounding',
  'wholeUnitRounding',
  'weighbridgeTolerancePercent',
  'toleranceBoundary',
  'toleranceBase',
  'effectiveFrom',
  'effectiveThrough',
  'state',
  'recordedBy',
  'recordedAtUtc',
  'reason',
] as const
const VIEW_FIELDS = ['companyId', 'today', 'version', 'versions'] as const

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

function isState(value: unknown): value is SalesPolicyState {
  return Object.values(SALES_POLICY_STATE).includes(value as SalesPolicyState)
}

function isDecimals(value: unknown): value is number {
  return (
    typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= UNIT_CONVERSION_POLICY_INPUT.minimumRoundingDecimals &&
    value <= UNIT_CONVERSION_POLICY_INPUT.maximumRoundingDecimals
  )
}

export function isRounding(value: unknown): value is UnitConversionRounding {
  return UNIT_CONVERSION_ROUNDINGS.includes(value as UnitConversionRounding)
}

export function isBoundary(value: unknown): value is UnitConversionBoundary {
  return UNIT_CONVERSION_BOUNDARIES.includes(value as UnitConversionBoundary)
}

export function isBase(value: unknown): value is UnitConversionBase {
  return UNIT_CONVERSION_BASES.includes(value as UnitConversionBase)
}

/** A positive density in g/cm³ the server will accept as typed; the server judges it again. */
export function isDensityInput(text: string): boolean {
  return DENSITY_INPUT.test(text) && /[1-9]/.test(text)
}

/** A tolerance percent (0…100, at most four decimals) as typed; the server judges it again. */
export function isPercentInput(text: string): boolean {
  return (
    PERCENT_INPUT.test(text) &&
    Number(text) >= UNIT_CONVERSION_POLICY_INPUT.minimumPercent &&
    Number(text) <= UNIT_CONVERSION_POLICY_INPUT.maximumPercent
  )
}

/** A typed number of decimals inside the technical bounds; otherwise null. */
export function parseDecimals(text: string): number | null {
  const trimmed = text.trim()
  if (!WHOLE_NUMBER.test(trimmed)) return null
  const value = Number(trimmed)
  return isDecimals(value) ? value : null
}

function isDensityText(value: unknown): value is string {
  return typeof value === 'string' && DENSITY_CANONICAL.test(value) && /[1-9]/.test(value)
}

function isPercentText(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    PERCENT_CANONICAL.test(value) &&
    Number(value) >= UNIT_CONVERSION_POLICY_INPUT.minimumPercent &&
    Number(value) <= UNIT_CONVERSION_POLICY_INPUT.maximumPercent
  )
}

function decodeVersion(raw: unknown): UnitConversionPolicyVersion | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VERSION_FIELDS)) return null
  const { profileId, version, densityGramsPerCubicCentimetre, coefficientDecimals, coefficientRounding } = raw
  const { documentTotalDecimals, documentTotalRounding, wholeUnitRounding, weighbridgeTolerancePercent } = raw
  const { toleranceBoundary, toleranceBase, effectiveFrom, effectiveThrough, state } = raw
  const { recordedBy, recordedAtUtc, reason } = raw
  if (!isGuid(profileId) || typeof version !== 'string' || !POSITIVE_VERSION.test(version)) return null
  if (!isDensityText(densityGramsPerCubicCentimetre)) return null
  if (!isDecimals(coefficientDecimals) || !isRounding(coefficientRounding)) return null
  if (!isDecimals(documentTotalDecimals) || !isRounding(documentTotalRounding) || !isRounding(wholeUnitRounding)) {
    return null
  }
  if (!isPercentText(weighbridgeTolerancePercent) || !isBoundary(toleranceBoundary) || !isBase(toleranceBase)) {
    return null
  }
  if (!isDateText(effectiveFrom)) return null
  if (effectiveThrough !== null && (!isDateText(effectiveThrough) || effectiveThrough < effectiveFrom)) return null
  if (!isState(state) || !isNonEmpty(recordedBy) || !isInstant(recordedAtUtc)) return null
  if (!isNonEmpty(reason) || reason.length > UNIT_CONVERSION_POLICY_INPUT.reasonMaximumLength) return null
  return {
    profileId,
    version,
    densityGramsPerCubicCentimetre,
    coefficientDecimals,
    coefficientRounding,
    documentTotalDecimals,
    documentTotalRounding,
    wholeUnitRounding,
    weighbridgeTolerancePercent,
    toleranceBoundary,
    toleranceBase,
    effectiveFrom,
    effectiveThrough: effectiveThrough as string | null,
    state,
    recordedBy,
    recordedAtUtc,
    reason,
  }
}

/**
 * The unit conversion policy timeline of the company the request named. One version number names one entry; at most one
 * entry is in force; an empty list carries version "0". The screen shows the entries in the server's order and derives
 * nothing from the dates.
 */
export function decodeUnitConversionPolicyView(raw: unknown, companyId: string): UnitConversionPolicyView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_FIELDS)) return null
  const { today, version, versions } = raw
  if (raw.companyId !== companyId || !isDateText(today)) return null
  if (typeof version !== 'string' || !VERSION.test(version) || !Array.isArray(versions)) return null
  const decoded: UnitConversionPolicyVersion[] = []
  for (const entry of versions) {
    const item = decodeVersion(entry)
    if (item === null) return null
    decoded.push(item)
  }
  if (new Set(decoded.map((entry) => entry.version)).size !== decoded.length) return null
  if (decoded.filter((entry) => entry.state === SALES_POLICY_STATE.inForce).length > 1) return null
  if (decoded.length === 0 && version !== '0') return null
  // The view's version is the latest stored one: it names an entry and no entry is newer.
  if (decoded.length > 0) {
    const latest = decoded.reduce((max, entry) => (BigInt(entry.version) > BigInt(max) ? entry.version : max), '0')
    if (latest !== version) return null
  }
  return { companyId, today, version, versions: decoded }
}

/** The entry in force today, as the server marked it. */
export function inForceUnitConversionPolicy(
  versions: readonly UnitConversionPolicyVersion[],
): UnitConversionPolicyVersion | null {
  return versions.find((entry) => entry.state === SALES_POLICY_STATE.inForce) ?? null
}
