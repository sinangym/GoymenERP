/**
 * AT-002 / ERP-000I — the global search wire contract.
 *
 *   GET /api/v1/search?q=<term>   → GlobalSearchResponseV1 { query, groups: [{ kind, items: [...], hasMore }] }
 *
 * The server fans the term out to each module's OWN search source; a kind the actor may not read has NO group (never an
 * empty one, never a count). This decoder takes EXACTLY the contract's members and refuses anything else — an unknown
 * kind, a repeated kind, a group out of the fixed order, more than the per-kind bound, a duplicated id, or any extra
 * member (a price, an amount, a total) — as `malformed`, so nothing the contract does not name can reach the screen.
 * The answer must also echo the term that was asked: an answer for another term is a stale answer, not this one.
 */

export const SEARCH_PATH = '/search'
export const SEARCH_QUERY_PARAM = 'q'

/** The server's term bounds (GlobalSearchApiV1): shorter or longer terms are refused there, so they are not sent. */
export const MINIMUM_TERM_LENGTH = 2
export const MAXIMUM_TERM_LENGTH = 100

/** Hits per kind (GlobalSearchApiV1.PerKindLimit). "More" sends the user to the owner's list, never a deeper page here. */
export const PER_KIND_LIMIT = 5

/** The first-release kinds, in the order the server groups them. */
export const SEARCH_KIND = {
  party: 'counterparty.party',
  product: 'product.item',
  salesOrder: 'sales.order',
} as const

export type SearchKind = (typeof SEARCH_KIND)[keyof typeof SEARCH_KIND]

export const SEARCH_KIND_ORDER: readonly SearchKind[] = [SEARCH_KIND.party, SEARCH_KIND.product, SEARCH_KIND.salesOrder]

export interface SearchItem {
  readonly id: string
  /** The owner's code or document number. */
  readonly code: string
  readonly name: string | null
  readonly secondaryName: string | null
  /** The owner's state name (e.g. "Active", "Approved"); the screen localizes the ones it knows. */
  readonly state: string
  /** A business date `YYYY-MM-DD` when the record has one. */
  readonly date: string | null
}

export interface SearchGroup {
  readonly kind: SearchKind
  readonly items: readonly SearchItem[]
  readonly hasMore: boolean
}

export interface SearchAnswer {
  readonly query: string
  readonly groups: readonly SearchGroup[]
}

const ANSWER_KEYS = ['query', 'groups'] as const
const GROUP_KEYS = ['kind', 'items', 'hasMore'] as const
const ITEM_KEYS = ['id', 'code', 'name', 'secondaryName', 'state', 'date'] as const
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const members = Object.keys(value)
  if (members.length !== keys.length || !members.every((member) => keys.includes(member))) return null
  return value as Obj
}

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isOptionalText = (value: unknown): value is string | null => value === null || typeof value === 'string'

function decodeItem(value: unknown): SearchItem | null {
  const item = exact(value, ITEM_KEYS)
  if (
    item === null ||
    typeof item.id !== 'string' ||
    !UUID.test(item.id) ||
    !isText(item.code) ||
    !isOptionalText(item.name) ||
    !isOptionalText(item.secondaryName) ||
    !isText(item.state) ||
    !(item.date === null || (typeof item.date === 'string' && DATE_ONLY.test(item.date)))
  ) {
    return null
  }
  return {
    id: item.id.toLowerCase(),
    code: item.code,
    name: item.name,
    secondaryName: item.secondaryName,
    state: item.state,
    date: item.date,
  }
}

function decodeGroup(value: unknown): SearchGroup | null {
  const group = exact(value, GROUP_KEYS)
  if (
    group === null ||
    !SEARCH_KIND_ORDER.includes(group.kind as SearchKind) ||
    !Array.isArray(group.items) ||
    group.items.length > PER_KIND_LIMIT ||
    typeof group.hasMore !== 'boolean'
  ) {
    return null
  }
  const items: SearchItem[] = []
  for (const raw of group.items) {
    const item = decodeItem(raw)
    if (item === null) return null
    items.push(item)
  }
  if (new Set(items.map((item) => item.id)).size !== items.length) return null
  return { kind: group.kind as SearchKind, items, hasMore: group.hasMore }
}

/** The answer for exactly `term`, or null (malformed / stale). */
export function decodeSearchAnswer(body: unknown, term: string): SearchAnswer | null {
  const answer = exact(body, ANSWER_KEYS)
  if (answer === null || answer.query !== term || !Array.isArray(answer.groups)) return null
  const groups: SearchGroup[] = []
  for (const raw of answer.groups) {
    const group = decodeGroup(raw)
    if (group === null) return null
    groups.push(group)
  }
  const order = groups.map((group) => SEARCH_KIND_ORDER.indexOf(group.kind))
  if (order.some((position, index) => index > 0 && position <= order[index - 1])) return null
  return { query: answer.query, groups }
}

/**
 * The term as the server will read it: trimmed and NFC-normalized, or null when it is outside the bounds (the box
 * then explains instead of sending a request the server would refuse).
 */
export function normalizeTerm(raw: string): string | null {
  const term = raw.trim().normalize('NFC')
  const length = [...term].length
  if (length < MINIMUM_TERM_LENGTH || length > MAXIMUM_TERM_LENGTH) return null
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f-\u009f]/.test(term)) return null
  return term
}
