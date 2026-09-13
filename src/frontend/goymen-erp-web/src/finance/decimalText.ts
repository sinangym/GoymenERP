/**
 * Money amounts as TEXT — the only form a Finance amount takes in this client.
 *
 * Source: `docs/features/FINANCE_MANUAL_SOURCE_JIT_SPEC_v1.md` §2 (PR #254 @290624d):
 * "Pozitif, sonlu, kayıpsız decimal metni … Binlik ayırıcı, bilimsel gösterim veya
 * floating-point ile hesap yok. `null`/boş/0 gerçekleşmiş tutar değildir." and
 * "Fazla basamak sessiz yuvarlanmaz … TRY için bile otomatik 2 basamak varsayımı yok."
 *
 * So, deliberately:
 *
 *  - an amount is NEVER converted to `number`. 5 billion TL a year fits a double, but a
 *    double cannot say whether "1000.10" and "1000.1" were the same text, and the scale
 *    the server accepted is part of the record (precision profile, spec §2);
 *  - there is NO default scale here. How many fraction digits an amount may carry is a
 *    server-owned precision profile (`profileId` + `version` + scale). Until one is
 *    supplied the client cannot call an amount valid for recording — see
 *    `checkAgainstProfile`, which returns `profile-unavailable` instead of guessing;
 *  - display formatting only regroups the digits it was given. It never pads, trims or
 *    rounds: "1000" is shown as "1.000", not "1.000,00".
 */

/** Canonical wire text: digits, optional `.fraction`; no sign, exponent, grouping or leading zero. */
const WIRE_DECIMAL_RE = /^(0|[1-9][0-9]*)(?:\.([0-9]+))?$/

/**
 * A lossless, strictly positive decimal on the wire. `"0"`, `"0.00"`, `"-5"`, `"1e3"`,
 * `"1,000"`, `" 1"` and a JSON number are all rejected.
 */
export function isPositiveDecimalText(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const match = WIRE_DECIMAL_RE.exec(value)
  if (match === null) return false
  return /[1-9]/.test(value)
}

/** Number of fraction digits exactly as written. `"1000.10"` → 2, `"1000"` → 0. */
export function fractionDigits(text: string): number {
  const dot = text.indexOf('.')
  return dot === -1 ? 0 : text.length - dot - 1
}

export type AmountInputError =
  | 'required'
  /** A `.`, a space, a sign, an exponent or any non-digit other than one comma. */
  | 'format'
  /** Parsed to zero. A zero amount is not a realised receipt (spec §2). */
  | 'not-positive'

export type AmountInputResult =
  | { readonly ok: true; readonly wire: string }
  | { readonly ok: false; readonly error: AmountInputError }

/**
 * What a person types into the Turkish form → canonical wire text.
 *
 * Input grammar (a reversible UX choice, not money policy): digits with at most ONE
 * comma as the decimal separator — the tr-TR convention. A dot is refused outright
 * instead of being interpreted, because in tr-TR "1.000" means one thousand while on
 * the wire it would mean one. Refusing is the only reading that can never turn a
 * thousand into a one. Leading zeros of the integer part are normalised away; the
 * fraction is kept digit-for-digit (trailing zeros included).
 */
export function parseAmountInput(input: string): AmountInputResult {
  const text = input.trim()
  if (text.length === 0) return { ok: false, error: 'required' }
  const match = /^([0-9]+)(?:,([0-9]+))?$/.exec(text)
  if (match === null) return { ok: false, error: 'format' }
  const integer = match[1].replace(/^0+(?=[0-9])/, '')
  const wire = match[2] === undefined ? integer : `${integer}.${match[2]}`
  if (!isPositiveDecimalText(wire)) return { ok: false, error: 'not-positive' }
  return { ok: true, wire }
}

/**
 * The server's precision profile for the account/currency/operation. Shape per spec §2
 * ("profileId+version, kabul edilmiş scale"); the values come only from the server.
 */
export interface PrecisionProfile {
  readonly profileId: string
  readonly version: string
  readonly scale: number
}

export type ProfileCheck =
  | { readonly kind: 'within-scale'; readonly profile: PrecisionProfile }
  /** More fraction digits than the profile accepts. Never rounded — the user fixes it. */
  | { readonly kind: 'scale-exceeded'; readonly profile: PrecisionProfile }
  /** No accepted profile: definitive recording stays closed (spec §2, §5). */
  | { readonly kind: 'profile-unavailable' }

export function checkAgainstProfile(wire: string, profile: PrecisionProfile | null): ProfileCheck {
  if (profile === null) return { kind: 'profile-unavailable' }
  return fractionDigits(wire) > profile.scale
    ? { kind: 'scale-exceeded', profile }
    : { kind: 'within-scale', profile }
}

/**
 * tr-TR display of canonical wire text: `.` groups thousands, `,` separates the
 * fraction. Returns `null` for anything that is not canonical wire text, so a caller
 * cannot accidentally print a malformed value as if it were an amount.
 */
export function formatDecimalTextTr(wire: string): string | null {
  const match = WIRE_DECIMAL_RE.exec(wire)
  if (match === null) return null
  const grouped = match[1].replace(/\B(?=(?:[0-9]{3})+$)/g, '.')
  return match[2] === undefined ? grouped : `${grouped},${match[2]}`
}
