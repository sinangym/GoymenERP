/**
 * Money amounts as TEXT — the only form a Finance amount takes in this client.
 *
 * Grammar source: Settings `ExactPositiveDecimalV1.Parse` (main@f386a1d, #256), which
 * Finance `PreparationValidator` uses for `money.amount` (#258 @fb482de):
 *   digits, at most one `.` that is neither first nor last; at least one non-zero
 *   digit; no sign, exponent, group separator or whitespace; leading and trailing
 *   zeros are PRESERVED byte-for-byte. Excess scale is `settings_decimal_scale_exceeded`.
 *
 * So, deliberately:
 *
 *  - an amount is NEVER converted to `number` and never re-written. The server keeps
 *    the text byte-for-byte, so the client does too — `"007.10"` stays `"007.10"`;
 *  - the client has NO scale. The approved scale lives in the server-side money profile
 *    (`MoneyProfileV1.Scale`), which no Finance read exposes. Too many fraction digits
 *    is reported by the server as an issue; the client does not guess a limit;
 *  - display formatting only regroups the digits it was given. It never pads, trims or
 *    rounds: "1000" is shown as "1.000", not "1.000,00".
 */

/** The server's lexical grammar, sign-free. */
const WIRE_DECIMAL_RE = /^([0-9]+)(?:\.([0-9]+))?$/

/** A source amount: lexically valid and not zero (spec §2: 0 is not a realised receipt). */
export function isPositiveDecimalText(value: unknown): value is string {
  return typeof value === 'string' && WIRE_DECIMAL_RE.test(value) && /[1-9]/.test(value)
}

/**
 * An UNALLOCATED amount: the same grammar, zero allowed. A fully allocated or reversed
 * source legitimately has nothing left (spec §6: "yeniden dağıtılabilir tutar 0").
 */
export function isNonNegativeDecimalText(value: unknown): value is string {
  return typeof value === 'string' && WIRE_DECIMAL_RE.test(value)
}

export type AmountInputError =
  | 'required'
  /** A `.`, a space, a sign, an exponent or any non-digit other than one comma. */
  | 'format'
  /** Only zeros. A zero amount is not a realised receipt (spec §2). */
  | 'not-positive'

export type AmountInputResult =
  | { readonly ok: true; readonly wire: string }
  | { readonly ok: false; readonly error: AmountInputError }

/**
 * What a person types into the Turkish form → wire text.
 *
 * Input grammar (a reversible UX choice, not money policy): digits with at most ONE
 * comma as the decimal separator — the tr-TR convention. A dot is refused outright
 * instead of being interpreted, because in tr-TR "1.000" means one thousand while on
 * the wire it means one. Refusing is the only reading that can never turn a thousand
 * into a one. Apart from comma → dot, the digits are sent exactly as typed.
 */
export function parseAmountInput(input: string): AmountInputResult {
  const text = input.trim()
  if (text.length === 0) return { ok: false, error: 'required' }
  const match = /^([0-9]+)(?:,([0-9]+))?$/.exec(text)
  if (match === null) return { ok: false, error: 'format' }
  const wire = match[2] === undefined ? match[1] : `${match[1]}.${match[2]}`
  if (!isPositiveDecimalText(wire)) return { ok: false, error: 'not-positive' }
  return { ok: true, wire }
}

/** Wire text → what the person would type (`.` → `,`), for editing an existing draft. */
export function amountInputFromWire(wire: string): string {
  return wire.replace('.', ',')
}

/**
 * tr-TR display of wire text: `.` groups thousands, `,` separates the fraction.
 * Returns `null` for anything outside the grammar, so a caller cannot accidentally
 * print a malformed value as if it were an amount.
 */
export function formatDecimalTextTr(wire: string): string | null {
  const match = WIRE_DECIMAL_RE.exec(wire)
  if (match === null) return null
  const grouped = match[1].replace(/\B(?=(?:[0-9]{3})+$)/g, '.')
  return match[2] === undefined ? grouped : `${grouped},${match[2]}`
}
