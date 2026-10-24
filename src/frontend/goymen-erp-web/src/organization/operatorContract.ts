/**
 * O02 location operator — hand-written runtime decoders for the two reads of PR #795
 * (`codex/o021-central-forward-exact19-20261021` @ 3f684781, `OrganizationLocationOperatorReadContractsV1.cs`):
 *
 *   GET /api/v1/organization/locations/{id}/operator               -> { locationId, operator | null }
 *   GET /api/v1/organization/locations/{id}/operator-candidates    -> { items, nextCursor, hasMore }
 *
 * The write is already on main: `PUT .../locations/{id}/operator` `{operatorPartyId, expectedVersion, reason}` → 204.
 *
 * FAILS CLOSED: exact keys, every field of its published type, the display must be for the location the route asked
 * for, candidate ids are unique on a page, and `hasMore` and `nextCursor` must agree. Nothing is passed through.
 */

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

/** The server's own bounds (`OrganizationLocationOperatorCandidateQueryParserV1`). */
export const OPERATOR_SEARCH_MAX_LENGTH = 200
const CURSOR_MAX_LENGTH = 8192
const CANDIDATE_PAGE_MAX = 25

export interface OperatorDisplay {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
  /** False when the assigned party no longer passes the server's operator eligibility (shown, never hidden). */
  readonly isCurrentlyEligible: boolean
}

export interface LocationOperatorContext {
  readonly locationId: string
  readonly operator: OperatorDisplay | null
}

export interface OperatorCandidate {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

export interface OperatorCandidatePage {
  readonly items: readonly OperatorCandidate[]
  readonly nextCursor: string | null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value)
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function decodeTradeName(value: unknown): string | null | undefined {
  if (value === null) return null
  return isNonEmptyString(value) ? value : undefined
}

function decodeParty(raw: unknown, keys: readonly string[]): OperatorCandidate | null {
  if (!isRecord(raw) || !hasExactKeys(raw, keys)) return null
  const { partyId, code, legalName } = raw
  const tradeName = decodeTradeName(raw.tradeName)
  if (!isUuid(partyId) || !isNonEmptyString(code) || !isNonEmptyString(legalName) || tradeName === undefined) {
    return null
  }
  return { partyId, code, legalName, tradeName }
}

const CANDIDATE_KEYS = ['partyId', 'code', 'legalName', 'tradeName'] as const
const DISPLAY_KEYS = [...CANDIDATE_KEYS, 'isCurrentlyEligible'] as const

/** The operator of the location the route asked for; anything else is malformed. */
export function decodeLocationOperator(raw: unknown, requestedLocationId: string): LocationOperatorContext | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ['locationId', 'operator'])) return null
  const { locationId } = raw
  if (!isUuid(locationId) || locationId.toLowerCase() !== requestedLocationId.toLowerCase()) return null
  if (raw.operator === null) return { locationId, operator: null }
  const party = decodeParty(raw.operator, DISPLAY_KEYS)
  const eligible = isRecord(raw.operator) ? raw.operator.isCurrentlyEligible : undefined
  if (party === null || typeof eligible !== 'boolean') return null
  return { locationId, operator: { ...party, isCurrentlyEligible: eligible } }
}

export function decodeOperatorCandidatePage(raw: unknown): OperatorCandidatePage | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ['items', 'nextCursor', 'hasMore'])) return null
  const { items, nextCursor, hasMore } = raw
  if (!Array.isArray(items) || items.length > CANDIDATE_PAGE_MAX || typeof hasMore !== 'boolean') return null
  if (nextCursor !== null && (!isNonEmptyString(nextCursor) || nextCursor.length > CURSOR_MAX_LENGTH)) return null
  if (hasMore !== (nextCursor !== null)) return null
  const decoded: OperatorCandidate[] = []
  const seen = new Set<string>()
  for (const entry of items) {
    const candidate = decodeParty(entry, CANDIDATE_KEYS)
    if (candidate === null) return null
    const key = candidate.partyId.toLowerCase()
    if (seen.has(key)) return null
    seen.add(key)
    decoded.push(candidate)
  }
  return { items: decoded, nextCursor }
}
