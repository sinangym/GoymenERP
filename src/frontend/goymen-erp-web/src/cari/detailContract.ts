/**
 * C-03 Cari 360 detail contract — runtime decoding for `GET /api/v1/counterparties/{partyId}`.
 *
 * Mirrors the merged `PartyCoreView` / `CompanyPartyAccountView` schemas in
 * `contracts/openapi/goymen-erp-v1.json`. Spec: CARI_C01_C03_JIT_SPEC_v1 §5.
 *
 * WHY THIS IS SEPARATE FROM THE LIST DECODER: the two responses differ in exactly
 * the way that matters. The list's account carries five fields; the detail's account
 * carries a sixth — `externalIntegrationCode` — which the accepted spec forbids
 * showing until a dedicated read permission and a redaction owner exist (§5.2).
 * Sharing one decoder would have made that difference invisible.
 */

import { PARTY_KIND, type PartyKind } from './contract'

const UUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_UUID = '00000000-0000-0000-0000-000000000000'
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
 * The selected company's account, as C-03 is allowed to know it.
 *
 * `externalIntegrationCode` is deliberately ABSENT from this type and never copied
 * out of the raw response (§5.2). "Not rendering it" would not be enough: a field
 * present on the view model reaches React state, a devtools inspection, a future
 * child component and anything that serializes the object. The only way to keep a
 * promise about where a value does not go is to never take it.
 */
export interface CounterpartyDetailAccount {
  readonly id: string
  readonly companyId: string
  /** Combined role flags. Decompose with `rolesFromFlags` — never re-derive names. */
  readonly roles: number
  /** Account state INSIDE the selected company. Distinct from `Party.isActive`. */
  readonly isActive: boolean
  /** int64 on the wire. Read-only in C-03A; never fed to a write command until the
   *  generated client can represent it losslessly (§5.2). */
  readonly version: number
}

export interface CounterpartyDetail {
  readonly id: string
  readonly code: string
  readonly kind: PartyKind
  readonly legalName: string
  readonly tradeName: string | null
  /** GLOBAL Party state. A different fact from the account state above. */
  readonly isActive: boolean
  readonly version: number
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
  if (!isSafeIntish(p.version) || p.version < 0) return null
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
  if (!isSafeIntish(a.roles) || a.roles < 1 || a.roles > ALL_ROLE_FLAGS) return null
  if ((a.roles & ~ALL_ROLE_FLAGS) !== 0) return null
  if (typeof a.isActive !== 'boolean') return null
  if (!isSafeIntish(a.version) || a.version < 0) return null

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
