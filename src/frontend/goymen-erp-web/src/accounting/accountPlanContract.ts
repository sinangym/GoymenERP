/**
 * M-05 Hesap Planı (FIN-S16): the strict decoder of `AccountPlanViewV1`.
 *
 * The server releases the company's ACTIVE approved account plan exactly as the approved mirror names it:
 * the source provenance (package, catalog version, account plan source version, SHA-256, activation time,
 * count) and the accounts ordered by code (ordinal). `source: null` with no accounts means "no active plan";
 * anything else that does not hold together — an unknown key, a nil or malformed id, a duplicate code or id,
 * a count that is not the list length, rows out of the server's code order — fails closed to `null`, never to
 * a partial list. No classification, hierarchy or fallback name is derived here.
 */

export interface AccountPlanSource {
  readonly packageId: string
  readonly catalogVersion: number | null
  readonly accountPlanSourceVersion: string
  readonly accountsSourceSha256: string
  readonly activatedAtUtc: string | null
  readonly accountCount: number
}

export interface AccountPlanAccount {
  readonly accountId: string
  readonly code: string
  readonly name: string
}

export interface AccountPlanView {
  readonly source: AccountPlanSource | null
  readonly accounts: readonly AccountPlanAccount[]
}

type Json = Record<string, unknown>

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const SHA256 = /^[0-9a-f]{64}$/i
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

const VIEW_KEYS = ['source', 'accounts'] as const
const SOURCE_KEYS = [
  'packageId',
  'catalogVersion',
  'accountPlanSourceVersion',
  'accountsSourceSha256',
  'activatedAtUtc',
  'accountCount',
] as const
const ACCOUNT_KEYS = ['accountId', 'code', 'name'] as const

const record = (value: unknown): Json | null =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Json) : null

const exact = (value: Json, keys: readonly string[]): boolean => {
  const actual = Object.keys(value)
  return actual.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

const isGuid = (value: unknown): value is string =>
  typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0

const isInstant = (value: unknown): value is string =>
  typeof value === 'string' && INSTANT.test(value) && !Number.isNaN(Date.parse(value))

const isPositiveInteger = (value: unknown): value is number =>
  typeof value === 'number' && Number.isSafeInteger(value) && value > 0

function decodeSource(value: unknown): AccountPlanSource | null {
  const source = record(value)
  if (source === null || !exact(source, SOURCE_KEYS)) return null
  if (!isGuid(source.packageId)
    || !(source.catalogVersion === null || isPositiveInteger(source.catalogVersion))
    || !isText(source.accountPlanSourceVersion)
    || typeof source.accountsSourceSha256 !== 'string' || !SHA256.test(source.accountsSourceSha256)
    || !(source.activatedAtUtc === null || isInstant(source.activatedAtUtc))
    || !isPositiveInteger(source.accountCount)) return null
  return {
    packageId: source.packageId,
    catalogVersion: source.catalogVersion,
    accountPlanSourceVersion: source.accountPlanSourceVersion,
    accountsSourceSha256: source.accountsSourceSha256,
    activatedAtUtc: source.activatedAtUtc,
    accountCount: source.accountCount,
  }
}

export function decodeAccountPlanView(value: unknown): AccountPlanView | null {
  const view = record(value)
  if (view === null || !exact(view, VIEW_KEYS) || !Array.isArray(view.accounts)) return null

  if (view.source === null) {
    return view.accounts.length === 0 ? { source: null, accounts: [] } : null
  }

  const source = decodeSource(view.source)
  if (source === null || source.accountCount !== view.accounts.length) return null

  const accounts: AccountPlanAccount[] = []
  const ids = new Set<string>()
  let previousCode: string | null = null
  for (const raw of view.accounts) {
    const account = record(raw)
    if (account === null || !exact(account, ACCOUNT_KEYS)) return null
    if (!isGuid(account.accountId) || !isText(account.code) || !isText(account.name)) return null
    const id = account.accountId.toLowerCase()
    // The server orders by code with an ordinal comparer; JavaScript's `<` compares the same UTF-16 code
    // units, so a strictly ascending code also proves there is no duplicate code.
    if (ids.has(id) || (previousCode !== null && !(previousCode < account.code))) return null
    ids.add(id)
    previousCode = account.code
    accounts.push({ accountId: account.accountId, code: account.code, name: account.name })
  }

  return { source, accounts }
}

/**
 * The client-side search narrows an already released, already decoded list; it never asks the server for
 * anything and never changes what an account is. Turkish-locale, case-insensitive substring on code or name.
 */
export function filterAccountPlan(accounts: readonly AccountPlanAccount[], search: string): readonly AccountPlanAccount[] {
  const needle = search.trim().toLocaleLowerCase('tr-TR')
  if (needle === '') return accounts
  return accounts.filter((account) =>
    account.code.toLocaleLowerCase('tr-TR').includes(needle)
    || account.name.toLocaleLowerCase('tr-TR').includes(needle))
}
