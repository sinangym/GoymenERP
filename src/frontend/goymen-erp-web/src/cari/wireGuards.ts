/**
 * Wire-level guards shared by the Cari decoders that landed with the C-02 create
 * and CARI-LINK slice (mutationContract.ts, candidateContract.ts).
 *
 * The list and detail decoders keep their own private copies on purpose: they
 * were accepted as-is and this slice does not edit them. Anything here mirrors
 * those copies exactly — same UUID rule, same lossless-version rule, same role
 * flag range — so a later consolidation is a move, not a behaviour change.
 */

export const UUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
export const NIL_UUID = '00000000-0000-0000-0000-000000000000'

/** Int64.MaxValue is 19 digits; the contract pins `maxLength: 19`. */
export const MAX_VERSION_DIGITS = 19

/** Customer | Supplier | Tolling | Service. */
export const ALL_ROLE_FLAGS = 1 | 2 | 4 | 8

export function isBoundUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID_RE.test(value) && value !== NIL_UUID
}

export function isNonEmptyString(value: unknown, max: number): value is string {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= max
}

export function isSafeIntish(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value)
}

/**
 * An int64 row version on the wire: a decimal STRING, no leading zeros, at most
 * 19 digits (contract pattern ^[1-9][0-9]*$). A JSON number is rejected — the
 * server moved to a string precisely because a version past 2^53 cannot survive
 * an IEEE-754 double, and a silently rounded concurrency token is worse than a
 * rejected response. The token is carried verbatim and never parsed.
 */
export function isLosslessVersion(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= MAX_VERSION_DIGITS &&
    /^[1-9][0-9]*$/.test(value)
  )
}

/** Role flags inside the declared range with at least one known bit. */
export function isKnownRoleFlags(value: unknown): value is number {
  return (
    isSafeIntish(value) &&
    value >= 1 &&
    value <= ALL_ROLE_FLAGS &&
    (value & ~ALL_ROLE_FLAGS) === 0
  )
}
