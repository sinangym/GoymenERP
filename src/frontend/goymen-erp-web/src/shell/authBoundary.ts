/**
 * SHELL-AUTH-BOUNDARY-001 — the central security boundary for authenticated
 * business reads (Issue #165, SAB-001…011).
 *
 * THE PROBLEM THIS EXISTS FOR: the shell already reacts centrally to a 401 from
 * the CAPABILITY call — SESSION_RESET plus a cache clear. A 401 or 403 from a
 * business read did not do that; the feature component just failed closed
 * locally. The session and the query namespace stayed open, so a route return, a
 * window focus or a refetch could bring the old rows back. A screen that hides
 * its table has not ended a session.
 *
 * SAB-001…011 are not eleven behaviours. They are the proof obligations of ONE
 * mechanism:
 *
 *     an immutable snapshot taken when the request STARTS
 *   + a current/stale decision made against that snapshot
 *   + 401 -> session reset,  403 -> capability re-fence
 *   + applied exactly once, on every read path
 *
 * Deriving the decision from response-time state instead would be the bug: by
 * the time a slow 401 lands, the actor may already be someone else, and
 * resetting THEIR session is a denial of service that looks like security.
 */

import type { ShellState } from './shellState'

/**
 * The bindings captured when a business read begins. Immutable by construction —
 * every field is a value, so nothing here can drift under a later render.
 */
export interface SecuritySnapshot {
  /** Identity of the authenticated actor at request start; null = unresolved. */
  readonly actorId: string | null
  /** Session identity. Advances on SESSION_RESET (logout / actor change). */
  readonly sessionEpoch: number
  /** The bound company, or null in group scope. */
  readonly companyId: string | null
  /** Cache-namespace binding. Advances only on an ATOMIC-COMMIT. */
  readonly committedContextRevision: number
  /** The single authoritative request-generation counter. */
  readonly requestFenceGeneration: number
  /** Opaque capability generation the read was authorized under. */
  readonly capabilityGeneration: string | null
  /** The query namespace this read belongs to (contextKeyString, or null). */
  readonly namespace: string | null
}

/** Capture the security bindings for a read that is about to start (SAB-001). */
export function captureSecuritySnapshot(
  state: ShellState,
  actorId: string | null,
  namespace: string | null,
): SecuritySnapshot {
  return {
    actorId,
    sessionEpoch: state.sessionEpoch,
    companyId: state.activeContext?.companyId ?? null,
    committedContextRevision: state.committedContextRevision,
    requestFenceGeneration: state.requestFenceGeneration,
    capabilityGeneration: state.activeContext?.capabilityGeneration ?? null,
    namespace,
  }
}

/**
 * Is this snapshot still the current security context? (SAB-008, SAB-009)
 *
 * A late 401 from actor A must not reset actor B's session, and a late 403 from
 * company A must not revoke company B's capability. Both are the same check:
 * the world moved on, so the answer is about a context that no longer exists.
 *
 * `requestFenceGeneration` is deliberately NOT compared. It advances on every
 * transition attempt including failed ones, so comparing it would discard
 * genuinely current results after an unrelated retry. Identity is carried by
 * actor + session epoch + company + committed revision; the fence is a
 * request-ordering device, not an identity.
 */
export function isSnapshotCurrent(snapshot: SecuritySnapshot, state: ShellState, actorId: string | null): boolean {
  if (snapshot.actorId !== actorId) return false
  if (snapshot.sessionEpoch !== state.sessionEpoch) return false
  if (snapshot.committedContextRevision !== state.committedContextRevision) return false
  return snapshot.companyId === (state.activeContext?.companyId ?? null)
}

/** What the central boundary decided to do with a business-read outcome. */
export type BoundaryAction =
  /** Not current any more — discard silently, no security effect (SAB-008/009). */
  | { readonly kind: 'stale-discard' }
  /** Current 401 — end the session centrally and clear everything (SAB-002/003). */
  | { readonly kind: 'session-reset' }
  /** Current 403 — the capability we read under is no longer trustworthy (SAB-004/005). */
  | { readonly kind: 'capability-refence'; readonly namespace: string | null }
  /** Not a security outcome; the caller renders it locally as before. */
  | { readonly kind: 'none' }

/**
 * Decide what a business-read HTTP status means for the security boundary.
 * Pure: the caller performs the effect. Same decision for the first page, a
 * continuation and a refetch — there is no weaker per-path variant (SAB-006).
 */
export function decideBoundaryAction(
  status: number,
  snapshot: SecuritySnapshot,
  state: ShellState,
  actorId: string | null,
): BoundaryAction {
  if (status !== 401 && status !== 403) return { kind: 'none' }
  if (!isSnapshotCurrent(snapshot, state, actorId)) return { kind: 'stale-discard' }
  return status === 401
    ? { kind: 'session-reset' }
    : { kind: 'capability-refence', namespace: snapshot.namespace }
}

/**
 * Identity of one security outcome, for exactly-once application (SAB-010).
 *
 * Several observers can see the same 401 — two components on a page, or a query
 * with more than one subscriber. Each would otherwise dispatch its own reset and
 * advance the fence again, which is both a render storm and a fence that has
 * moved further than the facts justify.
 */
export function boundaryEffectKey(action: BoundaryAction, snapshot: SecuritySnapshot): string | null {
  if (action.kind !== 'session-reset' && action.kind !== 'capability-refence') return null
  return JSON.stringify([
    action.kind,
    snapshot.actorId,
    snapshot.sessionEpoch,
    snapshot.companyId,
    snapshot.committedContextRevision,
    snapshot.capabilityGeneration,
  ])
}
