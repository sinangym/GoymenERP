/**
 * Spec §14.2 "Aynı Party'nin şirket ilişkilerini gör" — runtime decoding for the GROUP
 * form of `GET /api/v1/counterparties/{partyId}`: the same `CounterpartyDetailView`, sent
 * WITHOUT `X-Company-Id`, which the server answers with one account per company the
 * actor holds company-wide `counterparty.parties.read` in (`SingleOrAuthorizedMultiCompanyRead`,
 * pinned by `CounterpartyCompanyRelationshipsReadAcceptanceTests`).
 *
 * WHY NOT `decodeCounterpartyDetail`: that decoder is bound to ONE company on purpose and
 * rejects a second account as a leak. Here several accounts are the point, so the binding
 * moves: every account must belong to a company the SHELL already lists for this actor.
 * An account under a company the shell does not know is not filtered out; the whole
 * response is refused, for the same reason C-03 refuses a foreign account — filtering
 * would hide exactly the disagreement a user must not act on.
 *
 * What is deliberately NOT carried: versions (this view issues no command), the Party's
 * own fields beyond the id check (C-03 already shows them from its company-bound read),
 * and anything the server may add later. No amount, balance or currency exists in this
 * response, and none is derived.
 */

const UUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_UUID = '00000000-0000-0000-0000-000000000000'
const ALL_ROLE_FLAGS = 1 | 2 | 4 | 8
const MAX_VERSION_DIGITS = 19

function isBoundUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID_RE.test(value) && value !== NIL_UUID
}

/** Structural check only; the token is never parsed or kept. */
function isLosslessVersion(value: unknown): boolean {
  if (typeof value !== 'string' || !/^[1-9][0-9]*$/.test(value)) return false
  if (value.length > MAX_VERSION_DIGITS) return false
  return value.length < MAX_VERSION_DIGITS || value <= '9223372036854775807'
}

/** One company's account under the Party, as this read-only view is allowed to know it. */
export interface CompanyRelationship {
  readonly accountId: string
  readonly companyId: string
  /** Legacy role flags; `0` is a valid, role-less account (SINAN_KARARLARI_2026-09-10 §2). */
  readonly roles: number
  /** The account's state INSIDE its company. Never the Party's global state. */
  readonly isActive: boolean
}

export interface CompanyRelationships {
  readonly partyId: string
  /** In the server's order (company id). Never empty. */
  readonly accounts: readonly CompanyRelationship[]
}

/**
 * Returns null (fail closed) unless ALL of:
 *  - the response is the requested Party (`id === partyId`);
 *  - `companyAccounts` is a non-empty array of structurally valid accounts;
 *  - no company or company-account identity appears twice;
 *  - every company is one the shell directory lists for this actor;
 *  - the ACTIVE company's account is among them. The page this panel sits on has just
 *    read that account company-bound; a group answer without it disagrees with the
 *    screen, and showing the rest would present a picture the server did not give.
 */
export function decodeCompanyRelationships(
  raw: unknown,
  partyId: string,
  activeCompanyId: string,
  knownCompanyIds: ReadonlySet<string>,
): CompanyRelationships | null {
  if (!isBoundUuid(partyId) || !isBoundUuid(activeCompanyId)) return null
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>
  if (p.id !== partyId) return null
  if (!Array.isArray(p.companyAccounts) || p.companyAccounts.length === 0) return null
  if (p.companyAccounts.length > knownCompanyIds.size) return null

  const seenCompanyIds = new Set<string>()
  const seenAccountIds = new Set<string>()
  const accounts: CompanyRelationship[] = []
  for (const rawAccount of p.companyAccounts as unknown[]) {
    if (typeof rawAccount !== 'object' || rawAccount === null) return null
    const a = rawAccount as Record<string, unknown>
    if (!isBoundUuid(a.id) || !isBoundUuid(a.companyId)) return null
    if (!knownCompanyIds.has(a.companyId)) return null
    if (seenCompanyIds.has(a.companyId) || seenAccountIds.has(a.id)) return null
    seenCompanyIds.add(a.companyId)
    seenAccountIds.add(a.id)
    if (typeof a.roles !== 'number' || !Number.isSafeInteger(a.roles)) return null
    if (a.roles < 0 || a.roles > ALL_ROLE_FLAGS || (a.roles & ~ALL_ROLE_FLAGS) !== 0) return null
    if (typeof a.isActive !== 'boolean') return null
    if (!isLosslessVersion(a.version)) return null
    // Field by field, never spread: nothing the server adds later rides along.
    accounts.push({ accountId: a.id, companyId: a.companyId, roles: a.roles, isActive: a.isActive })
  }

  if (!seenCompanyIds.has(activeCompanyId)) return null
  return { partyId, accounts }
}
