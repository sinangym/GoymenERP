/**
 * Cari mutation contract — runtime decoding of the `201 Created` body returned by
 *
 *   POST /api/v1/counterparties                             (C-02 create)
 *   POST /api/v1/counterparties/{partyId}/company-account   (CARI-LINK)
 *
 * Mirrors `CounterpartyMutationView` / `CounterpartyMutationAccountView` in
 * `contracts/openapi/goymen-erp-v1.json`. Spec: CARI_C02_LINK_JIT_SPEC_v1 §3.3, §7.4;
 * handoff CARI_CREATE_LIST_DETAIL_LINK_API_v1 §3.
 *
 * WHY THIS IS A SEPARATE DECODER. It looks like the detail decoder and that is not
 * an accident — the wire shapes are the same today. The binding is not:
 *
 *  - a mutation response is bound to the COMMAND that produced it. For a link it
 *    must describe the Party whose id was in the request path; a 201 that names a
 *    different Party is not a success we can navigate to, however well-formed;
 *  - a mutation carries EXACTLY ONE account and it is the company the command was
 *    sent under (`minItems: 1, maxItems: 1` in the contract). The detail decoder
 *    enforces the same today only because C-03 is single-company; the reason
 *    differs, so the check lives with the reason.
 *
 * `version` is an int64 DECIMAL STRING and stays one. `externalIntegrationCode` is
 * not in this projection and the field-by-field construction keeps any later
 * addition out of the model as well.
 *
 * A malformed 201 is NOT "the record was not created". The caller treats it as an
 * unknown outcome: the same logical attempt is retried with the same key, which
 * replays rather than duplicates (§4.3).
 */

import { PARTY_KIND, type PartyKind } from './contract'
import { isBoundUuid, isKnownRoleFlags, isLosslessVersion, isNonEmptyString } from './wireGuards'

export interface CounterpartyMutationAccount {
  readonly id: string
  readonly companyId: string
  /** Combined role flags. Decompose with `rolesFromFlags`. */
  readonly roles: number
  /** Account state inside the command's company. Distinct from `Party.isActive`. */
  readonly isActive: boolean
  /** int64 concurrency token, carried verbatim as a decimal string. */
  readonly version: string
}

export interface CounterpartyMutationResult {
  readonly id: string
  /** Server-generated (`CAR-000001`). The browser never predicted or sent it. */
  readonly code: string
  readonly kind: PartyKind
  readonly legalName: string
  readonly tradeName: string | null
  /** GLOBAL Party state. */
  readonly isActive: boolean
  readonly version: string
  /** Exactly one: the command company's account. Enforced by the decoder. */
  readonly account: CounterpartyMutationAccount
}

export interface MutationBinding {
  /** The `X-Company-Id` the command was sent under. */
  readonly activeCompanyId: string
  /**
   * For the link command: the `partyId` in the request path. A response about
   * any other Party is rejected. Absent for create, whose id is server-assigned.
   */
  readonly expectedPartyId?: string
}

/**
 * Decode a mutation response. Returns null — fail closed — unless ALL of:
 *  - every Party field is structurally valid and every id is a non-nil UUID;
 *  - `version` (Party and account) is a lossless decimal string;
 *  - `companyAccounts` holds EXACTLY ONE account, and it is `activeCompanyId`'s;
 *  - roles carry at least one known bit and nothing outside the flag range;
 *  - when `expectedPartyId` is bound, the Party id equals it.
 */
export function decodeCounterpartyMutation(
  raw: unknown,
  binding: MutationBinding,
): CounterpartyMutationResult | null {
  if (!isBoundUuid(binding.activeCompanyId)) return null
  if (binding.expectedPartyId !== undefined && !isBoundUuid(binding.expectedPartyId)) return null
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>

  if (!isBoundUuid(p.id)) return null
  if (binding.expectedPartyId !== undefined && p.id !== binding.expectedPartyId) return null
  if (!isNonEmptyString(p.code, 64)) return null
  if (p.kind !== PARTY_KIND.LegalEntity && p.kind !== PARTY_KIND.NaturalPerson) return null
  if (!isNonEmptyString(p.legalName, 250)) return null
  if (p.tradeName !== null && typeof p.tradeName !== 'string') return null
  if (typeof p.isActive !== 'boolean') return null
  if (!isLosslessVersion(p.version)) return null
  if (!Array.isArray(p.companyAccounts)) return null

  // Exactly one account, and it is the command company's. A foreign, missing or
  // duplicate account means the server answered about something this context did
  // not ask for — the whole response is rejected rather than filtered.
  if (p.companyAccounts.length !== 1) return null
  const rawAccount: unknown = p.companyAccounts[0]
  if (typeof rawAccount !== 'object' || rawAccount === null) return null
  const a = rawAccount as Record<string, unknown>

  if (!isBoundUuid(a.id) || !isBoundUuid(a.companyId)) return null
  if (a.companyId !== binding.activeCompanyId) return null
  if (!isKnownRoleFlags(a.roles)) return null
  if (typeof a.isActive !== 'boolean') return null
  if (!isLosslessVersion(a.version)) return null

  // Field by field, never spread: nothing the server adds later reaches the model.
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
