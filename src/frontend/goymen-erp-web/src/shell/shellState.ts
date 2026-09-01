/**
 * Shell CompanyContext state machine (framework-independent reducer).
 *
 * Design source: docs/delivery/CRITICAL_ANALYSIS/shell-companycontext-fence/claude.md
 *   §A.1  INV-* invariants
 *   §A.2  requestFenceGeneration is the SINGLE authoritative counter; it also serves
 *         as the identity of the current transition attempt (Codex #133 P1-3/P1-1)
 *   §A.3  two-phase transition: activeContext + pendingTarget; activeContext only
 *         changes on a successful ATOMIC-COMMIT
 *
 * Scope: the deterministic reducer only. No React, no fetch, no router, no npm
 * package beyond package.json. Server-backed capability/company-directory shapes
 * are consumed via fixtures until the generated client (Issue #122) lands.
 *
 * NOT in scope: mock KPIs/balances/routes, business rules, permission/ABAC
 * inventory (the shell only sees a coarse, opaque capability generation marker).
 */

/** A verified, server-reauthorized single-company or authorized group-read context. */
export interface CompanyContext {
  readonly userId: string
  readonly scopeMode: 'single' | 'group'
  /** null only when scopeMode === 'group' and a concrete owner route declares group-read. */
  readonly companyId: string | null
  /**
   * Opaque server-backed capability generation marker. `null` => unresolved =>
   * fail-closed. The shell only compares equality, never parses it, and never
   * treats it as an incrementing counter or an authorization proof.
   */
  readonly capabilityGeneration: string | null
  /**
   * Capability ids the server allows for this actor in this context. Empty until a
   * snapshot resolves. The shell NEVER derives a capability from a role name, a route
   * name or a visible control — this list is the only source (ERP-000K §8.1), and it
   * is only meaningful while `capabilityGeneration` is non-null.
   */
  readonly capabilities: readonly string[]
}

/** What the boundary did, recorded so the cache side-effect runs once per identity. */
export interface SecurityEffect {
  readonly key: string
  readonly kind: 'session-reset' | 'capability-refence'
  readonly namespace: string | null
}

export interface SwitchTarget {
  readonly scopeMode: 'single' | 'group'
  readonly companyId: string | null
}

export type ShellPhase =
  | 'idle'
  | 'confirming-switch'
  | 'switching'
  | 'no-active-company'

export interface ShellState {
  readonly phase: ShellPhase
  /** Bound, verified context. null during `switching` (neutral) and `no-active-company`. */
  readonly activeContext: CompanyContext | null
  /** Candidate, not yet verified. Set during confirming-switch / switching. */
  readonly pendingTarget: SwitchTarget | null
  /**
   * Snapshot of the verified context taken when a transition begins. REAUTH_FAIL
   * restores from THIS, never from a caller-supplied event payload (Codex #133 P1-1).
   */
  readonly verifiedFormerContext: CompanyContext | null
  /**
   * The SINGLE authoritative request-generation counter AND the identity of the
   * current transition attempt. Advances at: transition start, REAUTH_FAIL,
   * CAPABILITY_REVOKED, SESSION_RESET. REAUTH_OK / REAUTH_FAIL are ignored unless
   * their `forGeneration` equals this exact value.
   */
  readonly requestFenceGeneration: number
  /**
   * Binding for contextKey / company-scoped cache. Advances only on ATOMIC-COMMIT and
   * NEVER resets — a monotonic namespace so a cache entry from an earlier session can
   * never collide with a later one.
   */
  readonly committedContextRevision: number
  /**
   * Identity of the current authenticated session. Advances on SESSION_RESET
   * (logout / actor change). Codex #139 P1-1: `committedContextRevision` cannot be
   * used to decide "did we bootstrap yet" because it must stay monotonic across a
   * reset — that left a new actor permanently un-bootstrapped.
   */
  readonly sessionEpoch: number
  /** The sessionEpoch a context was last committed for; null = this session never bootstrapped. */
  readonly bootstrappedSessionEpoch: number | null
  /**
   * The server rejected this session (401). TERMINAL until a real re-login.
   *
   * This lives in the reducer, not in a component, because a SESSION_RESET alone
   * is not enough: it advances the epoch, which makes `needsBootstrap` true, and
   * the orchestrator immediately establishes a NEW session. The reset then looks
   * like it worked while the user is quietly signed back in — worse than doing
   * nothing, because it is invisible (Issue #165 SAB-002).
   */
  readonly sessionInvalid: boolean
  /**
   * The last business-auth security effect this reducer applied, by identity.
   *
   * Exactly-once (SAB-010) cannot live in a component: two readers hold two refs
   * and each accepts the same outcome. The reducer is the only thing there is
   * exactly one of, so the ledger belongs here.
   */
  readonly lastSecurityEffect: SecurityEffect | null
  /** Open scopes that block a silent switch (dirty form, edit workspace, approval preview). */
  readonly dirtyScopes: readonly string[]
}

export type ShellEvent =
  /**
   * `forSessionEpoch` is the fence. Codex #139 P1-2: SESSION_READY was the only commit
   * with no attempt identity — a capability response that resolved after a
   * SESSION_RESET could commit the OLD actor's context into the NEW session.
   * REAUTH_OK/REAUTH_FAIL already carry `forGeneration`; this closes the last hole.
   */
  | { type: 'SESSION_READY'; forSessionEpoch: number; context: CompanyContext }
  | { type: 'REQUEST_SWITCH'; target: SwitchTarget }
  | { type: 'CONFIRM_SWITCH_DISCARD' }
  | { type: 'CONFIRM_SWITCH_STAY' }
  | { type: 'REAUTH_OK'; forGeneration: number; context: CompanyContext }
  | { type: 'REAUTH_FAIL'; forGeneration: number }
  | { type: 'CAPABILITY_REVOKED' }
  | { type: 'SESSION_RESET' }
  /** A 401: drop everything AND refuse to bootstrap again until re-login. */
  | { type: 'SESSION_INVALIDATED' }
  /**
   * An authenticated business read came back 401/403 (Issue #165).
   *
   * The DECISION lives here, not in the calling component. A feature hook holds
   * refs that stop tracking the provider once it unmounts, and its closure would
   * then judge a departed actor's late response against frozen state — the exact
   * mistake the boundary exists to prevent (ChatGPT #169 P1-2). The reducer always
   * has current state and there is exactly one of it, which also makes the
   * exactly-once ledger real rather than per-consumer (P1-3).
   */
  | {
      type: 'BUSINESS_AUTH_OUTCOME'
      status: 401 | 403
      snapshot: {
        readonly actorId: string | null
        readonly sessionEpoch: number
        readonly companyId: string | null
        readonly committedContextRevision: number
        readonly capabilityGeneration: string | null
        readonly namespace: string | null
      }
    }
  | { type: 'DIRTY_SET'; scope: string }
  | { type: 'DIRTY_CLEAR'; scope: string }

export const initialShellState: ShellState = {
  phase: 'no-active-company',
  activeContext: null,
  pendingTarget: null,
  verifiedFormerContext: null,
  requestFenceGeneration: 0,
  committedContextRevision: 0,
  sessionEpoch: 0,
  bootstrappedSessionEpoch: null,
  sessionInvalid: false,
  lastSecurityEffect: null,
  dirtyScopes: [],
}

/**
 * True while this session still needs an initial company context. Codex #139 P1-1:
 * this is keyed on `sessionEpoch`, NOT on `committedContextRevision`, so a new actor
 * after SESSION_RESET bootstraps even though the revision counter keeps climbing.
 */
export function needsBootstrap(state: ShellState): boolean {
  if (state.sessionInvalid) return false
  return (
    state.phase === 'no-active-company' && state.bootstrappedSessionEpoch !== state.sessionEpoch
  )
}

/** Begin a transition: fence + attempt id up, snapshot the former context, go neutral. */
function beginSwitching(state: ShellState, target: SwitchTarget): ShellState {
  return {
    ...state,
    phase: 'switching',
    activeContext: null,
    pendingTarget: target,
    verifiedFormerContext: state.activeContext,
    requestFenceGeneration: state.requestFenceGeneration + 1,
  }
}

function targetMatches(context: CompanyContext, target: SwitchTarget): boolean {
  return context.scopeMode === target.scopeMode && context.companyId === target.companyId
}

export function shellReducer(state: ShellState, event: ShellEvent): ShellState {
  switch (event.type) {
    case 'SESSION_READY': {
      // Login only. Never an unconditional commit from an arbitrary phase (Codex #133 P1-1).
      if (state.phase !== 'no-active-company') return state
      // ...and only for THIS session. A response in flight across a SESSION_RESET is
      // stale by definition: it was authorized for an actor who is no longer here.
      if (event.forSessionEpoch !== state.sessionEpoch) return state
      return {
        ...state,
        phase: 'idle',
        activeContext: event.context,
        pendingTarget: null,
        verifiedFormerContext: null,
        bootstrappedSessionEpoch: state.sessionEpoch,
        committedContextRevision: state.committedContextRevision + 1,
      }
    }

    case 'REQUEST_SWITCH': {
      if (state.phase !== 'idle' && state.phase !== 'no-active-company') return state
      if (state.dirtyScopes.length > 0) {
        return { ...state, phase: 'confirming-switch', pendingTarget: event.target }
      }
      return beginSwitching(state, event.target)
    }

    case 'CONFIRM_SWITCH_DISCARD': {
      if (state.phase !== 'confirming-switch' || state.pendingTarget === null) return state
      return beginSwitching({ ...state, dirtyScopes: [] }, state.pendingTarget)
    }

    case 'CONFIRM_SWITCH_STAY': {
      if (state.phase !== 'confirming-switch') return state
      return { ...state, phase: 'idle', pendingTarget: null }
    }

    case 'REAUTH_OK': {
      if (state.phase !== 'switching' || state.pendingTarget === null) return state
      // Must be the response to THIS exact transition attempt (Codex #133 P1-1/P1-3).
      if (event.forGeneration !== state.requestFenceGeneration) return state
      // The verified context must match the pending target and keep the same actor.
      const sameActor =
        state.verifiedFormerContext === null ||
        state.verifiedFormerContext.userId === event.context.userId
      if (!targetMatches(event.context, state.pendingTarget) || !sameActor) {
        // Mismatch / injection → fail closed, do not expose the supplied context.
        return {
          ...state,
          phase: 'no-active-company',
          activeContext: null,
          pendingTarget: null,
          verifiedFormerContext: null,
          requestFenceGeneration: state.requestFenceGeneration + 1,
        }
      }
      return {
        ...state,
        phase: 'idle',
        activeContext: event.context,
        pendingTarget: null,
        verifiedFormerContext: null,
        committedContextRevision: state.committedContextRevision + 1,
      }
    }

    case 'REAUTH_FAIL': {
      if (state.phase !== 'switching') return state
      if (event.forGeneration !== state.requestFenceGeneration) return state
      const fenced = state.requestFenceGeneration + 1
      const former = state.verifiedFormerContext
      if (former !== null) {
        // Restore the SNAPSHOT taken at transition start, unchanged. It was
        // server-verified moments ago; the target was never committed.
        return {
          ...state,
          phase: 'idle',
          activeContext: former,
          pendingTarget: null,
          verifiedFormerContext: null,
          requestFenceGeneration: fenced,
        }
      }
      return {
        ...state,
        phase: 'no-active-company',
        activeContext: null,
        pendingTarget: null,
        verifiedFormerContext: null,
        requestFenceGeneration: fenced,
      }
    }

    case 'CAPABILITY_REVOKED': {
      // Fail-closed: fence up, capability marker cleared, company identity kept.
      if (state.activeContext === null) {
        return { ...state, requestFenceGeneration: state.requestFenceGeneration + 1 }
      }
      return {
        ...state,
        requestFenceGeneration: state.requestFenceGeneration + 1,
        // Revoked: drop the granted list with the generation, so nothing stays mounted.
        activeContext: { ...state.activeContext, capabilityGeneration: null, capabilities: [] },
      }
    }

    case 'SESSION_RESET': {
      // Logout / actor change. Everything context-bound is dropped. The fence and the
      // cache-namespace revision stay monotonic; `sessionEpoch` advances so the new
      // session bootstraps (Codex #139 P1-1) while old cache keys can never be reused.
      return {
        ...initialShellState,
        requestFenceGeneration: state.requestFenceGeneration + 1,
        committedContextRevision: state.committedContextRevision,
        sessionEpoch: state.sessionEpoch + 1,
        bootstrappedSessionEpoch: null,
        // A reset must never RESURRECT a session the server already rejected.
        // Spreading initialShellState silently set this back to false, so a
        // SESSION_INVALIDATED followed by a SESSION_RESET became bootstrappable
        // again — the terminal state undone one dispatch later (ChatGPT #169 P1-1).
        sessionInvalid: state.sessionInvalid,
        lastSecurityEffect: state.lastSecurityEffect,
      }
    }

    case 'SESSION_INVALIDATED': {
      // Everything SESSION_RESET does, plus the terminal flag that stops the
      // orchestrator from silently signing the user back in.
      return {
        ...initialShellState,
        requestFenceGeneration: state.requestFenceGeneration + 1,
        committedContextRevision: state.committedContextRevision,
        sessionEpoch: state.sessionEpoch + 1,
        bootstrappedSessionEpoch: null,
        sessionInvalid: true,
      }
    }

    case 'BUSINESS_AUTH_OUTCOME': {
      const snap = event.snapshot
      // Current, or nothing. A late result from a departed actor or a company we
      // have left produces NO security effect (SAB-008/009) — resetting the
      // session of whoever is here now would be a denial of service wearing
      // security's clothes.
      const current =
        snap.actorId === (state.activeContext?.userId ?? null) &&
        snap.sessionEpoch === state.sessionEpoch &&
        snap.companyId === (state.activeContext?.companyId ?? null) &&
        snap.committedContextRevision === state.committedContextRevision
      if (!current) return state

      const kind = event.status === 401 ? 'session-reset' : 'capability-refence'
      const key = JSON.stringify([
        kind,
        snap.actorId,
        snap.sessionEpoch,
        snap.companyId,
        snap.committedContextRevision,
        snap.capabilityGeneration,
      ])
      // Exactly once per identity, however many observers report it (SAB-010).
      if (state.lastSecurityEffect?.key === key) return state
      const effect: SecurityEffect = { key, kind, namespace: snap.namespace }

      if (event.status === 401) {
        return {
          ...initialShellState,
          requestFenceGeneration: state.requestFenceGeneration + 1,
          committedContextRevision: state.committedContextRevision,
          sessionEpoch: state.sessionEpoch + 1,
          bootstrappedSessionEpoch: null,
          sessionInvalid: true,
          lastSecurityEffect: effect,
        }
      }

      // 403 does not end the session, but the capability we read under is no
      // longer trustworthy: drop the generation and the granted list, advance the
      // fence, and let capabilityFailClosed shut every gated mount until a FRESH
      // snapshot arrives (SAB-004/005).
      return {
        ...state,
        requestFenceGeneration: state.requestFenceGeneration + 1,
        activeContext:
          state.activeContext === null
            ? null
            : { ...state.activeContext, capabilityGeneration: null, capabilities: [] },
        lastSecurityEffect: effect,
      }
    }

    case 'DIRTY_SET': {
      if (state.dirtyScopes.includes(event.scope)) return state
      return { ...state, dirtyScopes: [...state.dirtyScopes, event.scope] }
    }

    case 'DIRTY_CLEAR': {
      if (!state.dirtyScopes.includes(event.scope)) return state
      return { ...state, dirtyScopes: state.dirtyScopes.filter((s) => s !== event.scope) }
    }

    default:
      return state
  }
}

/**
 * INV-NS: the context-owned cache namespace key, as a STRUCTURAL tuple — not a
 * delimiter-joined string (Codex #133 P1-2: `|`-join collides
 * ("u|single","c") vs ("u","single|c")). Includes committedContextRevision, NOT
 * requestFenceGeneration. Returns null when there is no bound context — callers
 * must not issue company-scoped reads in that state.
 */
export type ContextKey = readonly [
  userId: string,
  scopeMode: 'single' | 'group',
  companyId: string,
  capabilityGeneration: string,
  committedContextRevision: string,
]

export function contextKey(state: ShellState): ContextKey | null {
  const ctx = state.activeContext
  if (ctx === null || state.phase !== 'idle') return null
  return [
    ctx.userId,
    ctx.scopeMode,
    ctx.companyId ?? 'group',
    ctx.capabilityGeneration ?? 'no-capability',
    String(state.committedContextRevision),
  ]
}

/** Canonical, collision-free string form of a ContextKey (JSON of a string array). */
export function contextKeyString(key: ContextKey): string {
  return JSON.stringify(key)
}

/**
 * True when the bound context belongs to a DIFFERENT actor than the one the server
 * currently authenticates.
 *
 * Codex #139 P1-1: the actor change was only noticed in a passive `useEffect`, so
 * React could commit one paint where the new user's name sat above the previous
 * user's company, nav and capabilities. An effect is a repair, not a fence. This is
 * evaluated DURING render so the shell fails closed in that same frame; the effect
 * that dispatches SESSION_RESET then cleans up behind it.
 *
 * `verifiedFormerContext` is included because REAUTH_FAIL restores from it.
 */
export function contextActorMismatch(
  state: ShellState,
  authenticatedUserId: string | null,
): boolean {
  if (authenticatedUserId === null) return false // identity unresolved — other gates own that
  const bound = state.activeContext ?? state.verifiedFormerContext
  return bound !== null && bound.userId !== authenticatedUserId
}

/** True when capability-gated feature mounts must be closed (fail-closed). */
export function capabilityFailClosed(state: ShellState): boolean {
  return state.phase !== 'idle' || state.activeContext?.capabilityGeneration == null
}

/**
 * Whether one server-granted capability is available right now. Fail-closed: no bound
 * context, an unresolved capability snapshot or a mid-transition phase all answer false.
 *
 * This gates what the shell MOUNTS. It is never a substitute for backend authorization —
 * every request is re-authorized server-side (ERP-000K §10.2).
 */
export function hasCapability(state: ShellState, capabilityId: string): boolean {
  if (capabilityFailClosed(state)) return false
  return state.activeContext?.capabilities.includes(capabilityId) ?? false
}
