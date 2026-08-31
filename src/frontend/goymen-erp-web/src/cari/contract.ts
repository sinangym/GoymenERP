/**
 * Counterparty (Cari) list contract — runtime decoding and validation.
 *
 * Mirrors the merged backend contract `GetCounterpartiesV1`
 * (`contracts/openapi/goymen-erp-v1.json`, PR #141).
 *
 * WHY THIS SURVIVES THE GENERATED CLIENT (Issue #122): a generated client gives
 * compile-time types. It does NOT validate a response at runtime. Codex #139/#145
 * review asks for exactly that: reject a malformed page instead of letting an
 * out-of-range enum, a duplicate id or an inconsistent cursor reach the screen.
 * When the generated client lands, the `interface`s below are replaced by generated
 * types; the decoders stay.
 */

/** `PartyKind` is an integer on the wire: 1 = LegalEntity, 2 = NaturalPerson. */
export const PARTY_KIND = { LegalEntity: 1, NaturalPerson: 2 } as const
export type PartyKind = (typeof PARTY_KIND)[keyof typeof PARTY_KIND]

/**
 * `PartyRole` is a BIT FLAG integer (1|2|4|8, max 15) — one account can carry several
 * roles at once. Codex #145 review: there is NOT a separate account row per role for
 * the same (CompanyId, PartyId); it is one account with combined flags.
 */
export const PARTY_ROLE_FLAG = {
  Customer: 1,
  Supplier: 2,
  Tolling: 4,
  Service: 8,
} as const
export type PartyRoleName = keyof typeof PARTY_ROLE_FLAG
const ALL_ROLE_FLAGS = 1 | 2 | 4 | 8

/** Decompose the flag integer into role names. Order is stable (declaration order). */
export function rolesFromFlags(flags: number): PartyRoleName[] {
  return (Object.keys(PARTY_ROLE_FLAG) as PartyRoleName[]).filter(
    (name) => (flags & PARTY_ROLE_FLAG[name]) !== 0,
  )
}

export interface CounterpartyListAccount {
  readonly id: string
  readonly companyId: string
  /** Combined role flags. Use `rolesFromFlags` — never re-derive role names in a view. */
  readonly roles: number
  readonly isActive: boolean
  /** int64 — kept as string is unnecessary here (row version fits), but never math'd on. */
  readonly version: number
}

export interface CounterpartyListItem {
  readonly id: string
  readonly code: string
  readonly kind: PartyKind
  readonly legalName: string
  readonly tradeName: string | null
  /**
   * GLOBAL Party active state. Distinct from `CounterpartyListAccount.isActive`, which
   * is the account state inside one company (Codex #145 review). A view must not
   * collapse the two into a single "Aktif/Pasif" label.
   */
  readonly isActive: boolean
  readonly version: number
  readonly companyAccounts: readonly CounterpartyListAccount[]
}

export interface CounterpartyPage {
  readonly items: readonly CounterpartyListItem[]
  readonly nextCursor: string | null
  readonly hasMore: boolean
  /**
   * int64 that may exceed the JS safe-integer range, so the contract allows a string.
   * Kept verbatim as a string for display; never parsed into a `number`.
   */
  readonly totalCount: string | null
  readonly countScope: string | null
}

const UUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_UUID = '00000000-0000-0000-0000-000000000000'

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
 * The only count scope the C-01 slice accepts (JIT spec §3.6). A count must never be
 * read as "all parties" — it is always filtered, authorized and visible-only.
 */
export const COUNT_SCOPE_FILTERED_AUTHORIZED = 'filtered-authorized-visible-parties'

/** `totalCount` arrives as int64, string or null; normalise to a display string. */
function decodeTotalCount(value: unknown): { ok: true; value: string | null } | { ok: false } {
  if (value === null || value === undefined) return { ok: true, value: null }
  if (typeof value === 'number') {
    return Number.isSafeInteger(value) && value >= 0
      ? { ok: true, value: String(value) }
      : { ok: false }
  }
  if (typeof value === 'string' && /^(?:0|[1-9]\d*)$/.test(value)) {
    return { ok: true, value }
  }
  return { ok: false }
}

function decodeAccount(raw: unknown): CounterpartyListAccount | null {
  if (typeof raw !== 'object' || raw === null) return null
  const a = raw as Record<string, unknown>
  if (!isBoundUuid(a.id) || !isBoundUuid(a.companyId)) return null
  // Flags must be inside the declared range and carry at least one known bit.
  if (!isSafeIntish(a.roles) || a.roles < 1 || a.roles > ALL_ROLE_FLAGS) return null
  if ((a.roles & ~ALL_ROLE_FLAGS) !== 0) return null
  if (typeof a.isActive !== 'boolean') return null
  if (!isSafeIntish(a.version) || a.version < 0) return null
  return {
    id: a.id,
    companyId: a.companyId,
    roles: a.roles,
    isActive: a.isActive,
    version: a.version,
  }
}

/**
 * How the page must bind to the caller's company context (JIT spec §3.5).
 *
 * `single` is the C-01 slice: every Party must expose EXACTLY ONE account and it must
 * be the active company's. Zero accounts, a foreign company's account or a duplicate
 * for the selected company means the server returned something the shell must not
 * render — the whole page is rejected rather than filtered down, because a filtered
 * view would silently hide the fact that foreign data arrived.
 */
export type CounterpartyBinding =
  | { readonly mode: 'single'; readonly activeCompanyId: string }
  | { readonly mode: 'group' }

function decodeItem(raw: unknown, binding: CounterpartyBinding): CounterpartyListItem | null {
  if (typeof raw !== 'object' || raw === null) return null
  const i = raw as Record<string, unknown>
  if (!isBoundUuid(i.id)) return null
  if (!isNonEmptyString(i.code, 64)) return null
  if (i.kind !== PARTY_KIND.LegalEntity && i.kind !== PARTY_KIND.NaturalPerson) return null
  if (!isNonEmptyString(i.legalName, 250)) return null
  if (i.tradeName !== null && typeof i.tradeName !== 'string') return null
  if (typeof i.isActive !== 'boolean') return null
  if (!isSafeIntish(i.version) || i.version < 0) return null
  if (!Array.isArray(i.companyAccounts)) return null

  const accounts: CounterpartyListAccount[] = []
  const seenAccountIds = new Set<string>()
  const seenCompanyIds = new Set<string>()
  for (const rawAccount of i.companyAccounts) {
    const account = decodeAccount(rawAccount)
    if (account === null) return null
    // A party cannot hold two accounts in the same company, nor a duplicate account id.
    if (seenAccountIds.has(account.id) || seenCompanyIds.has(account.companyId)) return null
    seenAccountIds.add(account.id)
    seenCompanyIds.add(account.companyId)
    accounts.push(account)
  }

  if (binding.mode === 'single') {
    // Exactly one account, and it is the active company's. A foreign account here is a
    // leak, not a row to filter out.
    if (accounts.length !== 1) return null
    if (accounts[0].companyId !== binding.activeCompanyId) return null
  }

  return {
    id: i.id,
    code: i.code,
    kind: i.kind,
    legalName: i.legalName,
    tradeName: (i.tradeName as string | null) ?? null,
    isActive: i.isActive,
    version: i.version,
    companyAccounts: accounts,
  }
}

/**
 * Decode a `CounterpartyPage`. Returns null when ANYTHING is off — the caller maps
 * that to the `malformed-response` view state (fail-closed). Partial pages are never
 * returned: one bad row rejects the whole page rather than silently hiding a record.
 */
export function decodeCounterpartyPage(
  raw: unknown,
  binding: CounterpartyBinding,
): CounterpartyPage | null {
  if (typeof raw !== 'object' || raw === null) return null
  const p = raw as Record<string, unknown>
  if (!Array.isArray(p.items)) return null
  if (typeof p.hasMore !== 'boolean') return null
  if (p.nextCursor !== null && !isNonEmptyString(p.nextCursor, 4096)) return null

  const total = decodeTotalCount(p.totalCount)
  if (!total.ok) return null

  // JIT spec §3.6: count and scope travel together or not at all. A count without a
  // scope could be read as "all parties"; a scope without a count, or an unknown scope,
  // means we do not know what the number means.
  const scope = p.countScope
  if (total.value === null) {
    if (scope !== null && scope !== undefined) return null
  } else {
    if (scope !== COUNT_SCOPE_FILTERED_AUTHORIZED) return null
  }

  // hasMore <-> nextCursor must agree; an inconsistent pair means we cannot page safely.
  if (p.hasMore && p.nextCursor === null) return null
  if (!p.hasMore && p.nextCursor !== null) return null

  const items: CounterpartyListItem[] = []
  const seenIds = new Set<string>()
  for (const rawItem of p.items) {
    const item = decodeItem(rawItem, binding)
    if (item === null) return null
    if (seenIds.has(item.id)) return null
    seenIds.add(item.id)
    items.push(item)
  }

  return {
    items,
    nextCursor: (p.nextCursor as string | null) ?? null,
    hasMore: p.hasMore,
    totalCount: total.value,
    countScope: total.value === null ? null : COUNT_SCOPE_FILTERED_AUTHORIZED,
  }
}

/**
 * Stable ProblemDetails codes (`ErpOpenApiContract.KnownProblemCodePattern`). UI
 * branches on `code` only — never on the localized `title`/`detail`.
 */
export const PROBLEM_CODE = {
  invalidCursor: 'invalid_cursor',
  validationFailed: 'validation_failed',
  forbidden: 'forbidden',
  notFound: 'not_found',
  authenticationRequired: 'authentication_required',
  companyContextInvalid: 'company_context_invalid',
  serviceUnavailable: 'service_unavailable',
  internalError: 'internal_error',
} as const

export type ProblemCode = (typeof PROBLEM_CODE)[keyof typeof PROBLEM_CODE]

const KNOWN_PROBLEM_CODES = new Set<string>(Object.values(PROBLEM_CODE))

/** Extract the stable machine code from a ProblemDetails body, or null. */
export function problemCodeOf(raw: unknown): ProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_PROBLEM_CODES.has(code) ? (code as ProblemCode) : null
}
