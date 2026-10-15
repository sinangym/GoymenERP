/**
 * The shell's reload-stable session binding (M01.3 K2 #728/6180459736).
 *
 * `sessionEpoch` lives in the reducer and restarts at 0 in every document, so it cannot name a session across an F5:
 * a command persisted at epoch 1 read as absent after the reload, and one persisted at epoch 0 could surface in a later
 * session. Anything persisted for "this authenticated session" binds to this id instead.
 *
 * It is an opaque random id in the tab's sessionStorage, tied to the actor it was issued for:
 *  - the same actor in a reloaded document gets the same id;
 *  - another actor, a malformed record, or a boundary the provider retired gets a NEW id — never an earlier one;
 *  - a store that cannot be read, written or read back gives NO binding (null), and every consumer fails closed.
 *
 * It is not a credential and authorizes nothing: the server decides every request on its own session. It only lets a
 * persisted attempt prove it belongs to the session that is looking at it now.
 */

export const SESSION_BINDING_STORAGE_KEY = 'goymen.shell.session-binding.v1'

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

interface StoredBinding {
  readonly v: 1
  readonly id: string
  /** null = issued at a session boundary, not yet claimed by the next session's actor. */
  readonly actorId: string | null
}

/** Ids this document retired. Storage may fail to forget one; this document never hands it out again. */
const retired = new Set<string>()

function decode(raw: string): StoredBinding | null {
  let value: unknown
  try {
    value = JSON.parse(raw)
  } catch {
    return null
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const keys = Object.keys(value)
  if (keys.length !== 3 || !['v', 'id', 'actorId'].every((key) => keys.includes(key))) return null
  const { v, id, actorId } = value as Record<string, unknown>
  if (v !== 1 || typeof id !== 'string' || !UUID.test(id)) return null
  if (actorId !== null && (typeof actorId !== 'string' || actorId.length === 0)) return null
  return { v, id, actorId }
}

/**
 * A v4-shaped id from `getRandomValues`: `randomUUID` exists only in a secure context, and a plain-HTTP intranet host
 * must still get a binding rather than a shell that cannot start.
 */
function randomId(): string {
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

/** Write and read back; only a record that is provably stored counts. */
function store(binding: StoredBinding): boolean {
  const raw = JSON.stringify(binding)
  try {
    sessionStorage.setItem(SESSION_BINDING_STORAGE_KEY, raw)
    return sessionStorage.getItem(SESSION_BINDING_STORAGE_KEY) === raw
  } catch {
    return false
  }
}

/**
 * The binding of the session the shell is establishing for `actorId`, or null when none can be proven durable.
 * Called when a session's context is committed (SESSION_READY), never during render.
 */
export function establishSessionBinding(actorId: string): string | null {
  let raw: string | null
  try {
    raw = sessionStorage.getItem(SESSION_BINDING_STORAGE_KEY)
  } catch {
    return null
  }
  const current = raw === null ? null : decode(raw)
  if (current !== null && !retired.has(current.id)) {
    if (current.actorId === actorId) return current.id
    // Issued at a boundary for whoever comes next: this actor claims it.
    if (current.actorId === null) return store({ v: 1, id: current.id, actorId }) ? current.id : null
  }
  // Nothing stored, a malformed record, another actor's session or a retired id: a new session.
  const issued: StoredBinding = { v: 1, id: randomId(), actorId }
  return store(issued) ? issued.id : null
}

/**
 * A session boundary: SESSION_RESET, SESSION_INVALIDATED or any other transition that advances the epoch. The current
 * id is never issued again — not in this document, and (when storage allows) not after a reload either.
 */
export function retireSessionBinding(): void {
  try {
    const raw = sessionStorage.getItem(SESSION_BINDING_STORAGE_KEY)
    const current = raw === null ? null : decode(raw)
    if (current !== null) retired.add(current.id)
  } catch {
    // Unreadable: establishSessionBinding cannot read it either, so it issues nothing.
  }
  if (store({ v: 1, id: randomId(), actorId: null })) return
  try {
    sessionStorage.removeItem(SESSION_BINDING_STORAGE_KEY)
  } catch {
    // The retired id stays fenced in this document; see `retired`.
  }
}

/** Test seam: the document ended (an F5). Only its memory is lost; the tab's sessionStorage stays. */
export function forgetSessionBindingMemory(): void {
  retired.clear()
}
