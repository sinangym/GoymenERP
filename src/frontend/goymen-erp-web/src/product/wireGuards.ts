/**
 * Wire-level guards for the Product (Ürün) decoders.
 *
 * These mirror `src/cari/wireGuards.ts` rule for rule — same UUID rule, same
 * lossless-version rule. They are a local copy rather than an import on purpose:
 * `src/cari/**` belongs to the Cari single writer, and a cross-feature import
 * would make a Product decode change require an edit in someone else's lane.
 * A later consolidation into a shared module is a move, not a behaviour change.
 */

export const UUID_RE =
  /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
export const NIL_UUID = '00000000-0000-0000-0000-000000000000'

/** Int64.MaxValue is 19 digits. */
export const MAX_VERSION_DIGITS = 19

export function isBoundUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID_RE.test(value) && value !== NIL_UUID
}

export function isNonEmptyString(value: unknown, max: number): value is string {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= max
}

/** A nullable string field: either null, or a bounded non-empty string. Blank is drift. */
export function isNullableText(value: unknown, max: number): value is string | null {
  return value === null || isNonEmptyString(value, max)
}

/**
 * An int64 revision token on the wire: a decimal STRING, no leading zeros, at most
 * 19 digits. A JSON number is rejected — a revision past 2^53 cannot survive an
 * IEEE-754 double, and JIT spec §14 makes the revision mandatory for every Product
 * edit. A silently rounded concurrency token is worse than a rejected response.
 * The token is carried verbatim and never parsed.
 */
export function isLosslessVersion(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= MAX_VERSION_DIGITS &&
    /^[1-9][0-9]*$/.test(value)
  )
}

/**
 * A UTC instant the server owns (JIT spec §14: audit actor and server time are never
 * taken from the client). Accepted only as an ISO-8601 string that `Date` can parse
 * AND that carries an explicit zone — a local-looking timestamp would be rendered in
 * the reader's zone as if it were theirs.
 */
export function isInstant(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > 64) return false
  if (!/(?:Z|[+-]\d{2}:\d{2})$/.test(value)) return false
  return !Number.isNaN(Date.parse(value))
}

export function isNullableInstant(value: unknown): value is string | null {
  return value === null || isInstant(value)
}

/**
 * Characters no machine-generated token can legitimately contain: C0/C1 controls,
 * and the bidi overrides and invisible formatting marks in U+200B..U+200F /
 * U+202A..U+202E / U+2066..U+2069.
 *
 * The bidi ones are the reason this exists. A right-to-left override inside a value
 * that is printed verbatim makes the RENDERED text differ from the STORED one — the
 * operator reads a code, a unit or a status the server never sent. A length bound
 * cannot close that; only rejecting the characters can.
 */
// eslint-disable-next-line no-control-regex -- matching control characters is the point
const UNPRINTABLE_RE = /[\u0000-\u001F\u007F-\u009F\u200B-\u200F\u202A-\u202E\u2066-\u2069]/

/**
 * A MACHINE-GENERATED token: a product code, a retired alias, a unit code, a pinned
 * template version, a lifecycle state.
 *
 * Stricter than `isNonEmptyString`, and deliberately NOT used for human text.
 * Where the line falls, and why:
 *
 *  - a token the SERVER mints from a sequence or a controlled vocabulary is never
 *    free text, so an unprintable character means the field is broken. Rejecting it
 *    costs nothing real and closes a display-spoofing path on the value an operator
 *    uses to identify the record;
 *  - a product NAME, a class/group/family label, a tracking policy, a deactivation
 *    reason or an actor's display name is text a PERSON wrote. Applying this to
 *    those would make the client stricter than the server's own validator: a name
 *    the server accepts would become a record this screen refuses to show, which is
 *    a worse failure than an oddly-rendered name the person typed themselves.
 *
 * That boundary is the same one the Cari lane drew between operator data and a
 * system-owned token; it is recorded here because it is easy to widen by accident.
 */
export function isMachineToken(value: unknown, max: number): value is string {
  return isNonEmptyString(value, max) && !UNPRINTABLE_RE.test(value)
}

/** Nullable form of `isMachineToken`. */
export function isNullableMachineToken(value: unknown, max: number): value is string | null {
  return value === null || isMachineToken(value, max)
}
