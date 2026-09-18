/**
 * Value shapes the Sales draft contract fixes before any DTO is pinned.
 *
 * Source: `docs/features/SALES_ORDER_DRAFT_JIT_SPEC_v1.md` §2 (#254 @ 290624d):
 *  - a business date is date-only `YYYY-MM-DD` and must be a real calendar date;
 *  - a quantity is a positive, finite, LOSSLESS decimal;
 *  - `version` is a lossless string, separate from the commercial revision.
 *
 * Deliberately NOT here: a scale or precision bound. §2 ties money/measure precision
 * to an approved profile that does not exist yet, so a digit limit invented here
 * would be exactly the "teknik limit diye uydurulmuş" value the spec forbids.
 */

const DATE_ONLY_RE = /^(\d{4})-(\d{2})-(\d{2})$/

/** `YYYY-MM-DD` naming a day that exists. 2026-02-29 does not. */
export function isDateOnly(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const match = DATE_ONLY_RE.exec(value)
  if (match === null) return false
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1 || month < 1 || month > 12 || day < 1) return false
  // Day 0 of the next month is the last day of this one. UTC, so no local offset can
  // move the answer across midnight.
  return day <= new Date(Date.UTC(year, month, 0)).getUTCDate()
}

/** Canonical wire decimal: no sign, no exponent, no leading zeros, no trailing fraction zeros. */
const CANONICAL_DECIMAL_RE = /^(0|[1-9]\d*)(\.\d*[1-9])?$/

/** A positive decimal in canonical form, as the client sends it. */
export function isPositiveDecimalString(value: unknown): value is string {
  return typeof value === 'string' && CANONICAL_DECIMAL_RE.test(value) && /[1-9]/.test(value)
}

export type QuantityInputError = 'required' | 'invalid' | 'ambiguous-separator' | 'not-positive'

export type QuantityInput =
  | { readonly ok: true; readonly value: string }
  | { readonly ok: false; readonly error: QuantityInputError }

/**
 * Read what a person typed into a quantity field, without guessing.
 *
 * The number never passes through a JS `number`: a 20-digit quantity would silently
 * lose digits there. The text is canonicalized as text.
 *
 * `,` and `.` are both accepted as THE decimal separator, because both reach this
 * field (a Turkish keyboard and a pasted spreadsheet cell). What is refused is the
 * one input whose meaning depends on the reader: `1.000` is one thousand to a
 * Turkish reader and one to the wire format. A factor-of-1000 error on a steel
 * order is not a typo to correct silently, so the person is asked to write it
 * unambiguously (`1000`, or `1,5`).
 */
export function parseQuantityInput(raw: string): QuantityInput {
  const text = raw.trim()
  if (text.length === 0) return { ok: false, error: 'required' }
  const match = /^([0-9]+)(?:([.,])([0-9]+))?$/.exec(text)
  if (match === null) return { ok: false, error: 'invalid' }
  const integerDigits = match[1]
  const separator = match[2]
  const fractionDigits = match[3] ?? ''
  if (separator === '.' && fractionDigits.length === 3 && integerDigits.length <= 3) {
    return { ok: false, error: 'ambiguous-separator' }
  }
  const integer = integerDigits.replace(/^0+(?=\d)/, '')
  const fraction = fractionDigits.replace(/0+$/, '')
  const value = fraction.length > 0 ? `${integer}.${fraction}` : integer
  if (!/[1-9]/.test(value)) return { ok: false, error: 'not-positive' }
  return { ok: true, value }
}

/**
 * A concurrency version: a lossless positive integer carried as a string.
 *
 * A JSON number is refused on purpose. §2 pins the string form, and a number above
 * 2^53 would already have been rounded by `JSON.parse` before any check could see it.
 */
export function isVersionString(value: unknown): value is string {
  return typeof value === 'string' && /^[1-9][0-9]*$/.test(value)
}

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** A non-empty UUID. The all-zero UUID is not a reference to anything. */
export function isReferenceId(value: unknown): value is string {
  return typeof value === 'string' && UUID_RE.test(value) && /[1-9a-f]/i.test(value)
}
