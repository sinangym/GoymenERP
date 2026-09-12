/**
 * C-03 Cari 360 detail contract — runtime decoding for `GET /api/v1/counterparties/{partyId}`.
 *
 * Mirrors `CounterpartyDetailView` / `CounterpartyDetailAccountView` in
 * `contracts/openapi/goymen-erp-v1.json`. Spec: CARI_C01_C03_JIT_SPEC_v1 §5.
 *
 * WHY THIS IS SEPARATE FROM THE LIST DECODER: two differences, both load-bearing.
 *
 * 1. `version` is an int64 the server sends as an opaque DECIMAL STRING. JSON
 *    numbers are IEEE-754 doubles, so a row version past 2^53 would arrive
 *    silently wrong — the kind of corruption that surfaces much later as a
 *    concurrency check passing when it should not. Carried verbatim, never
 *    parsed, exactly like `totalCount` in the list contract.
 *
 * 2. `externalIntegrationCode` is GONE from the detail contract. Codex narrowed
 *    the server projection so it is never emitted (#170), which is the real
 *    boundary — a client-side discard was only ever defense in depth. The
 *    field-by-field construction below stays regardless: it keeps ANY field the
 *    server adds later out of the model, and that guarantee should not depend on
 *    which fields happen to exist today.
 */

import { PARTY_KIND, type PartyKind } from './contract'

const UUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_UUID = '00000000-0000-0000-0000-000000000000'
/** Int64.MaxValue is 19 digits; the contract pins `maxLength: 19`. */
const MAX_VERSION_DIGITS = 19
const ALL_ROLE_FLAGS = 1 | 2 | 4 | 8

function isBoundUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID_RE.test(value) && value !== NIL_UUID
}

function isNonEmptyString(value: unknown, max: number): value is string {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= max
}

function isSafeIntish(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value)
}

/**
 * An int64 row version on the wire: a decimal string, no leading zeros, minimum 1,
 * at most 19 digits (contract: pattern ^[1-9][0-9]*$, maxLength 19 — Int64.MaxValue
 * is 19 digits).
 *
 * Accepting a JSON number here would defeat the point. The server moved to a
 * string precisely because a version past 2^53 cannot survive an IEEE-754 double,
 * and a silently rounded concurrency token is worse than a rejected response.
 */
function isLosslessVersion(value: unknown): value is string {
  if (typeof value !== 'string') return false
  if (!/^[1-9][0-9]*$/.test(value)) return false
  if (value.length > MAX_VERSION_DIGITS) return false
  // 19 digits is not the bound: 9223372036854775808 is 19 digits and one past
  // Int64.MaxValue. Same length, digits only, so the string comparison is the numeric
  // one and the token still never gets parsed.
  return value.length < MAX_VERSION_DIGITS || value <= '9223372036854775807'
}

/**
 * The selected company's account, as C-03 is allowed to know it.
 *
 * `externalIntegrationCode` is deliberately ABSENT from this type and never copied
 * out of the raw response (§5.2).
 *
 * The precise claim, because the loose one is false: the raw JSON DOES reach the
 * browser — it is in the fetch response and in transport memory, and nothing here
 * changes that. What this guarantees is narrower and is the part we control: the
 * field never enters the decoded business model, so it cannot reach React state,
 * props, a devtools inspection of that state, a future child component, or
 * anything that serializes the decoded value.
 *
 * "Not rendering it" would not have been enough for that.
 */
export interface CounterpartyDetailAccount {
  readonly id: string
  readonly companyId: string
  /** Combined role flags. Decompose with `rolesFromFlags` — never re-derive names. */
  readonly roles: number
  /** Account state INSIDE the selected company. Distinct from `Party.isActive`. */
  readonly isActive: boolean
  /**
   * int64, carried verbatim as a decimal string. Never parsed into a number and
   * never arithmetic'd on — it is an opaque concurrency token.
   */
  readonly version: string
}

export interface CounterpartyDetail {
  readonly id: string
  readonly code: string
  readonly kind: PartyKind
  readonly legalName: string
  readonly tradeName: string | null
  /** GLOBAL Party state. A different fact from the account state above. */
  readonly isActive: boolean
  readonly version: string
  /** Exactly one: the selected company's. Enforced by the decoder. */
  readonly account: CounterpartyDetailAccount
}

/**
 * Decode a detail response, bound to the active company context (§5.3).
 *
 * Returns null — meaning `malformed`, fail closed — unless ALL of:
 *  - every Party field is structurally valid;
 *  - `companyAccounts` holds EXACTLY ONE account;
 *  - that account's companyId equals the active company;
 *  - roles are inside the declared flag range and carry at least one known bit;
 *  - every id is a non-nil UUID.
 *
 * A partial render is never returned. Showing a Cari's name while rejecting its
 * account would leak the existence of a record the caller may not be entitled to,
 * and half a record is not a record the user can act on.
 */
export function decodeCounterpartyDetail(
  raw: unknown,
  activeCompanyId: string,
): CounterpartyDetail | null {
  if (!isBoundUuid(activeCompanyId)) return null
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>

  if (!isBoundUuid(p.id)) return null
  if (!isNonEmptyString(p.code, 64)) return null
  if (p.kind !== PARTY_KIND.LegalEntity && p.kind !== PARTY_KIND.NaturalPerson) return null
  if (!isNonEmptyString(p.legalName, 250)) return null
  if (p.tradeName !== null && typeof p.tradeName !== 'string') return null
  if (typeof p.isActive !== 'boolean') return null
  if (!isLosslessVersion(p.version)) return null
  if (!Array.isArray(p.companyAccounts)) return null

  // Exactly one account, and it is the active company's. A foreign or duplicate
  // account means the server returned something this context must not render —
  // reject the whole response rather than filtering, which would hide the leak.
  if (p.companyAccounts.length !== 1) return null
  const rawAccount: unknown = p.companyAccounts[0]
  if (typeof rawAccount !== 'object' || rawAccount === null) return null
  const a = rawAccount as Record<string, unknown>

  if (!isBoundUuid(a.id) || !isBoundUuid(a.companyId)) return null
  if (a.companyId !== activeCompanyId) return null
  /**
   * Flags must be inside the declared range and carry no unknown bit.
   *
   * `0` is VALID and means "no commercial classification" (SINAN_KARARLARI_2026-09-10
   * §2, ILK_DILIM §3.1/§4.1). It used to be rejected, which made an ordinary
   * role-less counterparty poison the WHOLE page — one such record and the operator
   * saw `malformed-response` instead of a list.
   *
   * This is the forward-compatible consumer read ILK_DILIM §6 asks for FIRST: every
   * live consumer must be able to read `0` alongside the legacy 1..15 before any
   * role-less record can be written. The accepted wire contract still pins
   * `PartyRole minimum: 1`, so today this decoder accepts a value the server cannot
   * yet emit — that is the point of the ordering, not a gap in it.
   *
   * What does NOT relax: unknown bits, negatives, non-integers and the company
   * binding are all still rejected (§7). Moving one bound is not loosening the rest.
   */
  if (!isSafeIntish(a.roles) || a.roles < 0 || a.roles > ALL_ROLE_FLAGS) return null
  if ((a.roles & ~ALL_ROLE_FLAGS) !== 0) return null
  if (typeof a.isActive !== 'boolean') return null
  if (!isLosslessVersion(a.version)) return null

  // Built field by field, never spread. `externalIntegrationCode` and anything else
  // the server may add stays behind (§5.2) — a spread would carry it silently.
  return {
    id: p.id,
    code: p.code,
    kind: p.kind,
    legalName: p.legalName,
    tradeName: (p.tradeName as string | null) ?? null,
    isActive: p.isActive,
    version: p.version,
    account: {
      id: a.id,
      companyId: a.companyId,
      roles: a.roles,
      isActive: a.isActive,
      version: a.version,
    },
  }
}
