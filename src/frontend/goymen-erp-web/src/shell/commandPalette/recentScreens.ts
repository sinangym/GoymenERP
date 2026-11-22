/**
 * W1-B — the palette's "Son açılanlar": the last ten screens this actor opened in this company, kept ONLY in this
 * browser tab's session storage. It is a convenience, never a source of truth:
 *
 * - the key carries the session epoch, the actor and the company, so another user or company on the same browser never
 *   sees this list;
 * - only command ids are stored (never a record id, a query text or a search term);
 * - a stored id is shown only while it is still one of the actor's granted commands, so a revoked screen disappears;
 * - every read and write is wrapped: storage that throws (private window, blocked site data) or holds anything but the
 *   exact shape below reads as an empty list and is never surfaced as an error.
 *
 * There is no module-level state (TEKNIK_TUZAKLAR §7): callers pass the storage accessor.
 */

export const RECENT_LIMIT = 10
const RECENT_ID = /^(screen|create):[a-z0-9][a-z0-9.-]{0,127}$/

/** Returns the storage to use, or throws when the host refuses it. */
export type StorageAccessor = () => Pick<Storage, 'getItem' | 'setItem'>

/**
 * Tab-scoped browser storage. Persistent local storage is a reviewed-files-only surface (LF-11, EK-46 baseline only
 * shrinks), and a per-tab list leaks less on a shared computer; closing the tab forgets the list.
 */
export const browserStorage: StorageAccessor = () => window.sessionStorage

export function recentKey(sessionEpoch: number, actorId: string | null, companyId: string | null): string | null {
  return actorId === null || companyId === null ? null : `goymen.shell.recent-screens.v1:${sessionEpoch}:${actorId}:${companyId}`
}

/** Strict decode: a JSON array of at most ten distinct, well-formed command ids; anything else is null (fail-closed). */
export function decodeRecent(raw: string | null): string[] | null {
  if (raw === null) return []
  let value: unknown
  try {
    value = JSON.parse(raw)
  } catch {
    return null
  }
  if (!Array.isArray(value) || value.length > RECENT_LIMIT) return null
  const ids: string[] = []
  for (const item of value) {
    if (typeof item !== 'string' || !RECENT_ID.test(item) || ids.includes(item)) return null
    ids.push(item)
  }
  return ids
}

export function readRecent(storage: StorageAccessor, key: string | null): string[] {
  if (key === null) return []
  try {
    return decodeRecent(storage().getItem(key)) ?? []
  } catch {
    return []
  }
}

/** Puts `id` first, drops its older copy and keeps ten. A write failure is silent; the returned list is what was meant. */
export function rememberRecent(storage: StorageAccessor, key: string | null, id: string): string[] {
  if (key === null || !RECENT_ID.test(id)) return []
  const next = [id, ...readRecent(storage, key).filter((existing) => existing !== id)].slice(0, RECENT_LIMIT)
  try {
    storage().setItem(key, JSON.stringify(next))
  } catch {
    // Storage refused: the list simply is not remembered.
  }
  return next
}
