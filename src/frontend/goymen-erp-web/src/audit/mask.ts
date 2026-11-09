/**
 * Display masking of audit payloads (Decision No 135, docs/decisions/SINAN_KARARLARI_2026-10-21.md §3.7:
 * "VKN/TCKN (son 4 hariç), IBAN, telefon, e-posta, adres").
 *
 * Pure and deterministic: the same JSON text always gives the same masked, pretty-printed text. This is a DISPLAY
 * rule only — the stored audit record is never changed, and the server's read authority (`identity.audit.read`) is
 * the security boundary, not this function. It only keeps the screen from showing what the decision says must be
 * masked:
 *
 *  - a value under a key whose name contains iban / vkn / tckn / taxNumber / identityNumber keeps its last 4
 *    characters only; under phone / email / address nothing of it is shown. Key names are compared case-insensitively
 *    with separators removed (`tax_number`, `E-Mail`, `homeAddress` all match), at any depth; an object or array under
 *    such a key is masked leaf by leaf;
 *  - an object whose `kind` / `type` / `channel` member names a contact channel (phone, email, address, …) is a contact
 *    channel record (e.g. the Cari contact editor's `{"kind":"phone","value":…,"extension":…}`): every other member of
 *    it is fully masked, whatever its key;
 *  - anywhere else, a TR IBAN-shaped string (with or without spaces) and an 11-digit TCKN-shaped number keep their last
 *    4 digits, and an e-mail-shaped or TR phone-shaped (leading 0 or +90) string part is fully masked; a string that is
 *    itself JSON text is parsed and masked by the same rules;
 *  - nesting deeper than {@link MAX_DEPTH} levels is not shown (`null`), so a hostile payload cannot exhaust the stack;
 *  - `null` and booleans stay as they are (they disclose nothing);
 *  - a text that is not JSON is not shown at all (`null`), because it cannot be masked reliably.
 *
 * Numbers are kept as their exact source text (never `Number(...)`), so an amount or quantity in a payload is shown
 * digit for digit; the parser below exists for that reason and preserves key order and duplicate keys as stored.
 */

export const MASK = '••••'

/** Matched as a part of the normalized key name. */
const LAST_FOUR_KEYS = ['iban', 'vkn', 'tckn', 'taxnumber', 'identitynumber', 'tckimlik', 'kimlikno', 'vergino', 'vergikimlik'] as const
const FULL_MASK_KEYS = [
  'phone', 'email', 'address', 'telefon', 'eposta', 'adres', 'password', 'parola', 'secret', 'token', 'apikey',
] as const
/** Short names matched only as the whole normalized key, so `accepted` or `stellar` are not masked. */
const FULL_MASK_EXACT_KEYS = ['tel', 'gsm', 'cep', 'mobile', 'fax', 'faks', 'mail'] as const
/** A member with one of these names whose value names a contact channel marks its object as a channel record. */
const CHANNEL_KIND_KEYS = ['kind', 'type', 'channel', 'channeltype'] as const
const CHANNEL_KINDS = [
  'phone', 'mobile', 'fax', 'email', 'address', 'telefon', 'gsm', 'cep', 'faks', 'eposta', 'adres', 'mail',
] as const

/** Deeper payloads are not shown; real audit payloads stay far below this. */
export const MAX_DEPTH = 64

/** TR + 2 check digits + 22 digits, optionally grouped by single spaces. */
const TR_IBAN = /TR\s?\d{2}(?:\s?\d){22}/gi
const EMAIL = /[^\s@"'<>()[\],;:]+@[^\s@"'<>()[\],;:]+\.[^\s@"'<>()[\],;:]+/g
/**
 * A TR phone number with its leading 0 or +90: area/operator code 2xx–5xx, then 3+2+2 digits, optionally separated by
 * spaces or hyphens. A dot is not a separator and a match may not touch a decimal point, so an exchange rate stored as
 * text (`"0.3456789012"`) or an amount stays visible.
 */
const TR_PHONE = /(?<![\w+.,])(?:\+90|0)[\s-]?\(?[2-5]\d{2}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}(?!\d|[.,]\d)/g
/** An 11-digit TCKN-shaped number standing alone: not part of a decimal, a longer word, or a hyphenated id/GUID. */
const TCKN = /(?<![\w.,-])[1-9]\d{10}(?![\w-]|[.,]\d)/g

type KeyRule = 'last-four' | 'full' | null

/** A JSON number kept as its exact source text. */
class RawNumber {
  readonly text: string
  constructor(text: string) {
    this.text = text
  }
}

type JsonValue = null | boolean | string | RawNumber | JsonValue[] | JsonObject

/** Members in stored order; duplicates are kept as they are. */
class JsonObject {
  readonly members: readonly (readonly [string, JsonValue])[]
  constructor(members: readonly (readonly [string, JsonValue])[]) {
    this.members = members
  }
}

function normalizeKey(key: string): string {
  return key.toLowerCase().replace(/[^a-z0-9]/g, '')
}

export function sensitiveKeyRule(key: string): KeyRule {
  const normalized = normalizeKey(key)
  if (FULL_MASK_KEYS.some((part) => normalized.includes(part))) return 'full'
  if ((FULL_MASK_EXACT_KEYS as readonly string[]).includes(normalized)) return 'full'
  if (LAST_FOUR_KEYS.some((part) => normalized.includes(part))) return 'last-four'
  return null
}

/** Keeps the last 4 non-space characters; a value of 4 or fewer characters is masked entirely. */
export function maskKeepLastFour(value: string): string {
  const compact = value.replace(/\s/g, '')
  return compact.length <= 4 ? MASK : `${MASK}${compact.slice(-4)}`
}

/** Masks IBAN-, TCKN-, e-mail- and TR phone-shaped parts of a free string. */
export function maskFreeText(value: string): string {
  return value
    .replace(TR_IBAN, (match) => maskKeepLastFour(match))
    .replace(EMAIL, MASK)
    .replace(TR_PHONE, MASK)
    .replace(TCKN, (match) => maskKeepLastFour(match))
}

function isChannelRecord(value: JsonObject): boolean {
  return value.members.some(
    ([key, child]) =>
      typeof child === 'string'
      && (CHANNEL_KIND_KEYS as readonly string[]).includes(normalizeKey(key))
      && (CHANNEL_KINDS as readonly string[]).includes(normalizeKey(child)),
  )
}

/** A string that is itself JSON text is masked as JSON and kept compact; any other string is masked as free text. */
function maskString(value: string): string {
  const trimmed = value.trim()
  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    try {
      return renderCompact(maskValue(parseJson(trimmed)))
    } catch {
      // not JSON (or too deep): fall through to the free-text rules
    }
  }
  return maskFreeText(value)
}

function maskUnderKey(value: JsonValue, rule: Exclude<KeyRule, null>): JsonValue {
  if (value === null || typeof value === 'boolean') return value
  if (Array.isArray(value)) return value.map((item) => maskUnderKey(item, rule))
  if (value instanceof JsonObject) return new JsonObject(value.members.map(([k, v]) => [k, maskUnderKey(v, rule)]))
  const text = value instanceof RawNumber ? value.text : value
  return rule === 'full' ? MASK : maskKeepLastFour(text)
}

function maskValue(value: JsonValue): JsonValue {
  if (typeof value === 'string') return maskString(value)
  if (Array.isArray(value)) return value.map(maskValue)
  if (value instanceof JsonObject) {
    const channel = isChannelRecord(value)
    return new JsonObject(
      value.members.map(([key, child]) => {
        if (channel && !(CHANNEL_KIND_KEYS as readonly string[]).includes(normalizeKey(key))) {
          return [key, maskUnderKey(child, 'full')]
        }
        const rule = sensitiveKeyRule(key)
        return [key, rule === null ? maskValue(child) : maskUnderKey(child, rule)]
      }),
    )
  }
  return value
}

/**
 * A strict RFC 8259 parser that keeps numbers as source text. Throws on anything that is not one JSON value and on
 * nesting deeper than {@link MAX_DEPTH}.
 */
function parseJson(text: string): JsonValue {
  let index = 0
  let depth = 0
  const fail = (): never => {
    throw new SyntaxError('not json')
  }
  const skipSpace = () => {
    while (index < text.length && ' \t\n\r'.includes(text[index])) index += 1
  }
  const parseString = (): string => {
    const start = index
    index += 1
    while (index < text.length) {
      const char = text[index]
      if (char === '"') {
        index += 1
        return JSON.parse(text.slice(start, index)) as string
      }
      if (char === '\\') index += 2
      else if (char < ' ') fail()
      else index += 1
    }
    return fail()
  }
  const parseValue = (): JsonValue => {
    skipSpace()
    const char = text[index]
    if (char === '{' || char === '[') {
      depth += 1
      if (depth > MAX_DEPTH) fail()
    }
    const value = parseContainerOrScalar(char)
    if (char === '{' || char === '[') depth -= 1
    return value
  }
  const parseContainerOrScalar = (char: string): JsonValue => {
    if (char === '{') {
      index += 1
      const members: [string, JsonValue][] = []
      skipSpace()
      if (text[index] === '}') {
        index += 1
        return new JsonObject(members)
      }
      for (;;) {
        skipSpace()
        if (text[index] !== '"') fail()
        const key = parseString()
        skipSpace()
        if (text[index] !== ':') fail()
        index += 1
        members.push([key, parseValue()])
        skipSpace()
        if (text[index] === ',') index += 1
        else if (text[index] === '}') {
          index += 1
          return new JsonObject(members)
        } else fail()
      }
    }
    if (char === '[') {
      index += 1
      const items: JsonValue[] = []
      skipSpace()
      if (text[index] === ']') {
        index += 1
        return items
      }
      for (;;) {
        items.push(parseValue())
        skipSpace()
        if (text[index] === ',') index += 1
        else if (text[index] === ']') {
          index += 1
          return items
        } else fail()
      }
    }
    if (char === '"') return parseString()
    for (const [word, value] of [['true', true], ['false', false], ['null', null]] as const) {
      if (text.startsWith(word, index)) {
        index += word.length
        return value
      }
    }
    const number = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/.exec(text.slice(index))
    if (number === null) return fail()
    index += number[0].length
    return new RawNumber(number[0])
  }
  const value = parseValue()
  skipSpace()
  if (index !== text.length) fail()
  return value
}

function renderCompact(value: JsonValue): string {
  if (value === null) return 'null'
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (typeof value === 'string') return JSON.stringify(value)
  if (value instanceof RawNumber) return value.text
  if (Array.isArray(value)) return `[${value.map(renderCompact).join(',')}]`
  return `{${value.members.map(([key, child]) => `${JSON.stringify(key)}:${renderCompact(child)}`).join(',')}}`
}

function render(value: JsonValue, depth: number): string {
  const pad = '  '.repeat(depth + 1)
  const close = '  '.repeat(depth)
  if (value === null) return 'null'
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (typeof value === 'string') return JSON.stringify(value)
  if (value instanceof RawNumber) return value.text
  if (Array.isArray(value)) {
    if (value.length === 0) return '[]'
    return `[\n${value.map((item) => pad + render(item, depth + 1)).join(',\n')}\n${close}]`
  }
  if (value.members.length === 0) return '{}'
  return `{\n${value.members.map(([key, child]) => `${pad}${JSON.stringify(key)}: ${render(child, depth + 1)}`).join(',\n')}\n${close}}`
}

/**
 * The masked, pretty-printed text of one stored JSON payload; `null` when the text is not JSON and therefore is not
 * shown. Two-space indentation, key order and number text as stored.
 */
export function maskAuditJson(text: string): string | null {
  let parsed: JsonValue
  try {
    parsed = parseJson(text)
  } catch {
    return null
  }
  return render(maskValue(parsed), 0)
}
