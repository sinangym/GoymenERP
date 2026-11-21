/**
 * KIM-D34 wire contract — the paths, request body and strict runtime decoder of the group-wide login attempt limit routes
 * (`SettingsLoginRateLimitEndpoints`, `SettingsLoginRateLimitsV1.cs`):
 *
 *   GET  /settings/login-rate-limit            -> LoginRateLimitViewV1
 *   POST /settings/login-rate-limit/versions   -> 204
 *
 * The login rate limit (KIM-D29) is one group-wide Settings record: every company's sign-in uses the same value. A new
 * version takes effect when it is saved (the login path reads it on its next refresh); until the first version is recorded
 * the host's configured starting value applies, which this screen does not see and never states.
 *
 * The decoder fails CLOSED: one bad entry invalidates the whole answer, keys must match the contract exactly, the partition
 * must be one of the closed list, and exactly the newest entry is the current one.
 *
 * KIM-D42 (`SINAN_KARARLARI_2026-11-15_DALGA2_EK13.md`): the same record carries the trusted proxy list. The sign-in path
 * reads `X-Forwarded-For` only when the direct peer is on that list; an empty list (the default) means the header is never
 * read. Entries are single addresses or CIDR blocks; the server checks and canonicalizes them, the screen only checks shape.
 */

import { isUuid } from './discountLimitContract'

export const LOGIN_RATE_LIMIT_PATH = '/settings/login-rate-limit'
export const LOGIN_RATE_LIMIT_VERSIONS_PATH = `${LOGIN_RATE_LIMIT_PATH}/versions`

/** The stable problem codes the screen distinguishes (`LoginRateLimitProblemCodesV1`); anything else is a generic refusal. */
export const LOGIN_RATE_LIMIT_PROBLEM = {
  requestInvalid: 'settings_login_rate_limit_request_invalid',
  commandMetadataInvalid: 'settings_login_rate_limit_command_metadata_invalid',
  reasonRequired: 'settings_login_rate_limit_reason_required',
  reasonTooLong: 'settings_login_rate_limit_reason_too_long',
  idempotencyConflict: 'settings_login_rate_limit_idempotency_conflict',
  versionConflict: 'settings_login_rate_limit_version_conflict',
  providerUnavailable: 'settings_provider_unavailable',
  profileInvalid: 'settings_profile_invalid',
  accessDenied: 'settings_access_denied',
} as const

/** Refusals after which the screen's copy is stale: the form closes and the view is read again. */
export const LOGIN_RATE_LIMIT_STALE_CODES: readonly string[] = [LOGIN_RATE_LIMIT_PROBLEM.versionConflict]

/** Technical bounds mirrored from `LoginRateLimitInputLimitsV1` (transport and limiter shape, not business policy). */
export const LOGIN_RATE_LIMIT_INPUT = {
  reasonMaximumLength: 4000,
  minimumPermitLimit: 1,
  maximumPermitLimit: 1_000_000,
  minimumWindowSeconds: 1,
  maximumWindowSeconds: 86_400,
  maximumTrustedProxyCount: 64,
  /** The longest textual entry the decoder accepts (an IPv6 address with a /128 prefix fits well inside). */
  trustedProxyMaximumLength: 64,
} as const

/** The closed list of what one window counts per (`LoginRateLimitPartitionsV1`). */
export const LOGIN_RATE_LIMIT_PARTITION = {
  ipAddress: 'IpAddress',
  ipAddressAndUserName: 'IpAddressAndUserName',
} as const
export type LoginRateLimitPartition = (typeof LOGIN_RATE_LIMIT_PARTITION)[keyof typeof LOGIN_RATE_LIMIT_PARTITION]
export const LOGIN_RATE_LIMIT_PARTITIONS: readonly LoginRateLimitPartition[] = [
  LOGIN_RATE_LIMIT_PARTITION.ipAddress,
  LOGIN_RATE_LIMIT_PARTITION.ipAddressAndUserName,
]

export interface LoginRateLimitVersion {
  readonly profileId: string
  readonly version: string
  readonly permitLimit: number
  readonly windowSeconds: number
  readonly partitionBy: LoginRateLimitPartition
  /** Canonical addresses / CIDR blocks, ordered and distinct; empty means the forwarded header is never read. */
  readonly trustedProxies: readonly string[]
  readonly isCurrent: boolean
  readonly recordedBy: string
  readonly recordedInCompanyId: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface LoginRateLimitView {
  /** The latest stored version ("0" when none): the next record's expected version. */
  readonly version: string
  /** Newest first; empty when no version is recorded yet. */
  readonly versions: readonly LoginRateLimitVersion[]
}

export interface RecordLoginRateLimitBody {
  readonly expectedVersion: string
  readonly permitLimit: number
  readonly windowSeconds: number
  readonly partitionBy: LoginRateLimitPartition
  readonly trustedProxies: readonly string[]
  readonly reason: string
}

const VERSION = /^(0|[1-9][0-9]*)$/
const POSITIVE_VERSION = /^[1-9][0-9]*$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64
const WHOLE_NUMBER = /^[0-9]+$/
const TRUSTED_PROXY_SHAPE = /^[0-9A-Fa-f.:]+(\/[0-9]+)?$/

const VERSION_FIELDS = [
  'profileId',
  'version',
  'permitLimit',
  'windowSeconds',
  'partitionBy',
  'trustedProxies',
  'isCurrent',
  'recordedBy',
  'recordedInCompanyId',
  'recordedAtUtc',
  'reason',
] as const
const VIEW_FIELDS = ['version', 'versions'] as const

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

function isWithin(value: unknown, minimum: number, maximum: number): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= minimum && value <= maximum
}

export function isPartition(value: unknown): value is LoginRateLimitPartition {
  return LOGIN_RATE_LIMIT_PARTITIONS.includes(value as LoginRateLimitPartition)
}

/** A typed whole number inside the bounds (no sign, no decimals, no exponent); otherwise null. */
export function parseBoundedWhole(text: string, minimum: number, maximum: number): number | null {
  const trimmed = text.trim()
  if (!WHOLE_NUMBER.test(trimmed) || trimmed.length > String(maximum).length) return null
  const value = Number(trimmed)
  return isWithin(value, minimum, maximum) ? value : null
}

/** One trusted proxy entry's shape: hex digits, dots and colons with an optional "/prefix". The server checks the rest. */
export function isTrustedProxyShape(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length > 0 &&
    value.length <= LOGIN_RATE_LIMIT_INPUT.trustedProxyMaximumLength &&
    TRUSTED_PROXY_SHAPE.test(value)
  )
}

/**
 * The typed list, one entry per line (commas also separate); blank lines are skipped. Null when an entry is malformed,
 * repeated or the list is too long; the empty list is valid and means "never read the forwarded header".
 */
export function parseTrustedProxies(text: string): readonly string[] | null {
  const entries = text
    .split(/[\n,]/)
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0)
  if (entries.length > LOGIN_RATE_LIMIT_INPUT.maximumTrustedProxyCount) return null
  if (!entries.every(isTrustedProxyShape)) return null
  return new Set(entries).size === entries.length ? entries : null
}

function decodeTrustedProxies(raw: unknown): readonly string[] | null {
  if (!Array.isArray(raw) || raw.length > LOGIN_RATE_LIMIT_INPUT.maximumTrustedProxyCount) return null
  const entries: string[] = []
  for (const entry of raw) {
    if (!isTrustedProxyShape(entry) || entries.includes(entry)) return null
    entries.push(entry)
  }
  return entries
}

function decodeVersion(raw: unknown): LoginRateLimitVersion | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VERSION_FIELDS)) return null
  const { profileId, version, permitLimit, windowSeconds, partitionBy, isCurrent } = raw
  const { recordedBy, recordedInCompanyId, recordedAtUtc, reason } = raw
  if (!isGuid(profileId) || typeof version !== 'string' || !POSITIVE_VERSION.test(version)) return null
  if (!isWithin(permitLimit, LOGIN_RATE_LIMIT_INPUT.minimumPermitLimit, LOGIN_RATE_LIMIT_INPUT.maximumPermitLimit)) {
    return null
  }
  if (
    !isWithin(windowSeconds, LOGIN_RATE_LIMIT_INPUT.minimumWindowSeconds, LOGIN_RATE_LIMIT_INPUT.maximumWindowSeconds)
  ) {
    return null
  }
  if (!isPartition(partitionBy) || typeof isCurrent !== 'boolean') return null
  const trustedProxies = decodeTrustedProxies(raw.trustedProxies)
  if (trustedProxies === null) return null
  if (!isNonEmpty(recordedBy) || !isGuid(recordedInCompanyId) || !isInstant(recordedAtUtc)) return null
  if (!isNonEmpty(reason) || reason.length > LOGIN_RATE_LIMIT_INPUT.reasonMaximumLength) return null
  return {
    profileId, version, permitLimit, windowSeconds, partitionBy, trustedProxies, isCurrent,
    recordedBy, recordedInCompanyId, recordedAtUtc, reason,
  }
}

/**
 * The group-wide timeline. Newest first with strictly descending versions; exactly the first entry is current, and the
 * view's `version` is that entry's ("0" with an empty list). Anything else is another contract.
 */
export function decodeLoginRateLimitView(raw: unknown): LoginRateLimitView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_FIELDS)) return null
  const { version, versions } = raw
  if (typeof version !== 'string' || !VERSION.test(version) || !Array.isArray(versions)) return null
  const decoded: LoginRateLimitVersion[] = []
  for (const entry of versions) {
    const item = decodeVersion(entry)
    if (item === null) return null
    decoded.push(item)
  }
  if (decoded.length === 0) return version === '0' ? { version, versions: decoded } : null
  if (decoded[0].version !== version) return null
  for (let index = 0; index < decoded.length; index += 1) {
    if (decoded[index].isCurrent !== (index === 0)) return null
    if (index > 0 && BigInt(decoded[index].version) >= BigInt(decoded[index - 1].version)) return null
  }
  return { version, versions: decoded }
}
