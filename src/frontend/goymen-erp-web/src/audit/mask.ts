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
 *  - anywhere else, a TR IBAN-shaped string (with or without spaces) keeps its last 4 digits, and an e-mail-shaped
 *    string is fully masked;
 *  - `null` and booleans stay as they are (they disclose nothing);
 *  - a text that is not JSON is not shown at all (`null`), because it cannot be masked reliably.
 *
 * Numbers are kept as their exact source text (never `Number(...)`), so an amount or quantity in a payload is shown
 * digit for digit; the parser below exists for that reason and preserves key order and duplicate keys as stored.
 */

export const MASK = '••••'

const LAST_FOUR_KEYS = ['iban', 'vkn', 'tckn', 'taxnumber', 'identitynumber'] as const
const FULL_MASK_KEYS = ['phone', 'email', 'address'] as const

/** TR + 2 check digits + 22 digits, optionally grouped by single spaces. */
const TR_IBAN = /TR\s?\d{2}(?:\s?\d){22}/gi
const EMAIL = /[^\s@"'<>()[\],;:]+@[^\s@"'<>()[\],;:]+\.[^\s@"'<>()[\],;:]+/g

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
  if (LAST_FOUR_KEYS.some((part) => normalized.includes(part))) return 'last-four'
  return null
}

/** Keeps the last 4 non-space characters; a value of 4 or fewer characters is masked entirely. */
export function maskKeepLastFour(value: string): string {
  const compact = value.replace(/\s/g, '')
  return compact.length <= 4 ? MASK : `${MASK}${compact.slice(-4)}`
}

/** Masks IBAN- and e-mail-shaped parts of a free string. */
export function maskFreeText(value: string): string {
  return value.replace(TR_IBAN, (match) => maskKeepLastFour(match)).replace(EMAIL, MASK)
}

function maskUnderKey(value: JsonValue, rule: Exclude<KeyRule, null>): JsonValue {
  if (value === null || typeof value === 'boolean') return value
  if (Array.isArray(value)) return value.map((item) => maskUnderKey(item, rule))
  if (value instanceof JsonObject) return new JsonObject(value.members.map(([k, v]) => [k, maskUnderKey(v, rule)]))
  const text = value instanceof RawNumber ? value.text : value
  return rule === 'full' ? MASK : maskKeepLastFour(text)
}

function maskValue(value: JsonValue): JsonValue {
  if (typeof value === 'string') return maskFreeText(value)
  if (Array.isArray(value)) return value.map(maskValue)
  if (value instanceof JsonObject) {
    return new JsonObject(
      value.members.map(([key, child]) => {
        const rule = sensitiveKeyRule(key)
        return [key, rule === null ? maskValue(child) : maskUnderKey(child, rule)]
      }),
    )
  }
  return value
}

/** A strict RFC 8259 parser that keeps numbers as source text. Throws on anything that is not one JSON value. */
function parseJson(text: string): JsonValue {
  let index = 0
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
