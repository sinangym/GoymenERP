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
  /** Open scopes that block a silent switch (dirty form, edit workspace, approval preview). */
  readonly dirtyScopes: readonly string[]
}

export type ShellEvent =
  | { type: 'SESSION_READY'; context: CompanyContext }
  | { type: 'REQUEST_SWITCH'; target: SwitchTarget }
  | { type: 'CONFIRM_SWITCH_DISCARD' }
  | { type: 'CONFIRM_SWITCH_STAY' }
  | { type: 'REAUTH_OK'; forGeneration: number; context: CompanyContext }
  | { type: 'REAUTH_FAIL'; forGeneration: number }
  | { type: 'CAPABILITY_REVOKED' }
  | { type: 'SESSION_RESET' }
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
  dirtyScopes: [],
}

/**
 * True while this session still needs an initial company context. Codex #139 P1-1:
 * this is keyed on `sessionEpoch`, NOT on `committedContextRevision`, so a new actor
 * after SESSION_RESET bootstraps even though the revision counter keeps climbing.
 */
export function needsBootstrap(state: ShellState): boolean {
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
        activeContext: { ...state.activeContext, capabilityGeneration: null },
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

/** True when capability-gated feature mounts must be closed (fail-closed). */
export function capabilityFailClosed(state: ShellState): boolean {
  return state.phase !== 'idle' || state.activeContext?.capabilityGeneration == null
}
