/**
 * CARI-LINK candidate contract — runtime decoding for
 *
 *   GET /api/v1/counterparties/company-account-link-candidates
 *
 * Mirrors `CounterpartyLinkCandidatePage` / `CounterpartyLinkCandidateItem` /
 * `CounterpartyLinkCandidateSourceAccount` in `contracts/openapi/goymen-erp-v1.json`.
 * Spec: CARI_C02_LINK_JIT_SPEC_v1 §6; handoff §3 "Liste ve candidate birbirinin
 * DTO'su değildir".
 *
 * This is NOT the list contract with a different name, and the differences are the
 * whole point of a separate decoder:
 *
 *  - an item carries `visibleSourceAccounts[]`, never `companyAccounts[]`. A
 *    candidate is by definition NOT linked to the target company, so an account
 *    shaped as the target's is a leak, not a field to ignore;
 *  - the source account `version` is an int64 DECIMAL STRING (the list sends a
 *    number). The candidate Party has NO version at all;
 *  - there is no `totalCount` / `countScope`. The server does not count candidates
 *    and the browser must not invent one (§6.3: no local re-filtering, no count);
 *  - every source account belongs to a company OTHER than the target — the target
 *    company cannot be a source for itself;
 *  - the Party is globally active (§6.2). A passive SOURCE ACCOUNT is allowed and
 *    is shown as such; a passive Party is not a candidate.
 *
 * As everywhere in this module: one bad row rejects the whole page. Filtering a
 * page down would hide the fact that something outside the contract arrived.
 */

import { PARTY_KIND, type PartyKind } from './contract'
import { isBoundUuid, isKnownRoleFlags, isLosslessVersion, isNonEmptyString } from './wireGuards'

export interface LinkCandidateSourceAccount {
  readonly id: string
  /** A company the actor is authorized to read; labelled through the directory. */
  readonly companyId: string
  readonly roles: number
  /** Source account state. Never copied to the target and never a default. */
  readonly isActive: boolean
  /** int64 concurrency token, carried verbatim as a decimal string. */
  readonly version: string
}

export interface LinkCandidate {
  readonly id: string
  readonly code: string
  readonly kind: PartyKind
  readonly legalName: string
  readonly tradeName: string | null
  /** Always true for a candidate (§6.2); kept explicit so a view cannot assume it. */
  readonly isActive: boolean
  /** At least one — a Party with no visible source is not something the actor may see. */
  readonly visibleSourceAccounts: readonly LinkCandidateSourceAccount[]
}

export interface LinkCandidatePage {
  readonly items: readonly LinkCandidate[]
  /** Opaque, server-authenticated; bound to actor, target company and query. */
  readonly nextCursor: string | null
  readonly hasMore: boolean
}

function decodeSourceAccount(raw: unknown, targetCompanyId: string): LinkCandidateSourceAccount | null {
  if (typeof raw !== 'object' || raw === null) return null
  const a = raw as Record<string, unknown>
  if (!isBoundUuid(a.id) || !isBoundUuid(a.companyId)) return null
  // The target cannot be its own source: an account in the target company means the
  // Party is already linked there and should not have been a candidate at all.
  if (a.companyId === targetCompanyId) return null
  if (!isKnownRoleFlags(a.roles)) return null
  if (typeof a.isActive !== 'boolean') return null
  if (!isLosslessVersion(a.version)) return null
  return { id: a.id, companyId: a.companyId, roles: a.roles, isActive: a.isActive, version: a.version }
}

function decodeCandidate(raw: unknown, targetCompanyId: string): LinkCandidate | null {
  if (typeof raw !== 'object' || raw === null) return null
  const i = raw as Record<string, unknown>
  if (!isBoundUuid(i.id)) return null
  if (!isNonEmptyString(i.code, 64)) return null
  if (i.kind !== PARTY_KIND.LegalEntity && i.kind !== PARTY_KIND.NaturalPerson) return null
  if (!isNonEmptyString(i.legalName, 250)) return null
  if (i.tradeName !== null && typeof i.tradeName !== 'string') return null
  // §6.2: a globally passive Party is not a candidate. Rendering one would offer a
  // link the server will answer with 404.
  if (i.isActive !== true) return null
  // A target-shaped account on a candidate is a disclosure of a relation that must
  // not exist yet. Its presence, not its content, is the defect.
  if (Object.hasOwn(i, 'companyAccounts')) return null
  if (!Array.isArray(i.visibleSourceAccounts)) return null

  const accounts: LinkCandidateSourceAccount[] = []
  const seenAccountIds = new Set<string>()
  const seenCompanyIds = new Set<string>()
  for (const rawAccount of i.visibleSourceAccounts) {
    const account = decodeSourceAccount(rawAccount, targetCompanyId)
    if (account === null) return null
    // One account per company, one id per account.
    if (seenAccountIds.has(account.id) || seenCompanyIds.has(account.companyId)) return null
    seenAccountIds.add(account.id)
    seenCompanyIds.add(account.companyId)
    accounts.push(account)
  }
  // Visible from at least one authorized source, or not visible at all.
  if (accounts.length === 0) return null

  // Field by field. The candidate Party has no `version` in this contract and none is
  // carried; `externalIntegrationCode` and anything else stays behind.
  return {
    id: i.id,
    code: i.code,
    kind: i.kind,
    legalName: i.legalName,
    tradeName: (i.tradeName as string | null) ?? null,
    isActive: true,
    visibleSourceAccounts: accounts,
  }
}

/**
 * Decode a candidate page bound to the TARGET company (`X-Company-Id`). Returns
 * null — meaning `malformed-response`, fail closed — when anything is off.
 */
export function decodeLinkCandidatePage(raw: unknown, targetCompanyId: string): LinkCandidatePage | null {
  if (!isBoundUuid(targetCompanyId)) return null
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>
  if (!Array.isArray(p.items)) return null
  if (typeof p.hasMore !== 'boolean') return null
  if (p.nextCursor !== null && !isNonEmptyString(p.nextCursor, 4096)) return null
  // No count exists on this endpoint. One that arrives counted something the
  // contract does not define, and the workspace must not label it (§6.3).
  if (Object.hasOwn(p, 'totalCount') || Object.hasOwn(p, 'countScope')) return null
  // hasMore <-> nextCursor must agree or paging is unsafe.
  if (p.hasMore && p.nextCursor === null) return null
  if (!p.hasMore && p.nextCursor !== null) return null

  const items: LinkCandidate[] = []
  const seenIds = new Set<string>()
  for (const rawItem of p.items) {
    const item = decodeCandidate(rawItem, targetCompanyId)
    if (item === null) return null
    if (seenIds.has(item.id)) return null
    seenIds.add(item.id)
    items.push(item)
  }

  return { items, nextCursor: (p.nextCursor as string | null) ?? null, hasMore: p.hasMore }
}
