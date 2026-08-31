/**
 * Shell CompanyContext state machine (framework-independent reducer).
 *
 * Design source: docs/delivery/CRITICAL_ANALYSIS/shell-companycontext-fence/claude.md
 *   §A.1  INV-* invariants
 *   §A.2  two counters: requestFenceGeneration (fence) vs committedContextRevision (binding)
 *   §A.3  two-phase transition: activeContext + pendingTarget; activeContext only
 *         changes on a successful ATOMIC-COMMIT
 *
 * Scope of this module: the deterministic reducer only. No React, no fetch, no
 * router, no npm package beyond what is already in package.json. Server-backed
 * capability/company-directory shapes are consumed via fixtures until the
 * generated client from SHELL-PILOT-01A / Issue #122 lands (see ./fixtures.ts).
 *
 * NOT in scope here: mock KPIs/balances/routes (forbidden), business rules,
 * permission/ABAC inventory (the shell only sees coarse feature availability).
 */

/** A verified, server-reauthorized single-company or authorized group-read context. */
export interface CompanyContext {
  readonly userId: string
  readonly scopeMode: 'single' | 'group'
  /** null only when scopeMode === 'group' and a concrete owner route declares group-read. */
  readonly companyId: string | null
  /**
   * Opaque server-backed capability generation marker. `null` => unresolved =>
   * fail-closed: base shell + company directory only, no capability-gated mounts.
   * The shell never interprets its contents, only whether it changed.
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
  /** INV-FENCE: advances at the START of switch / logout / capability revoke. */
  readonly requestFenceGeneration: number
  /** Binding for contextKey / company-scoped cache. Advances only on ATOMIC-COMMIT. */
  readonly committedContextRevision: number
  /** Open scopes that block a silent switch (dirty form, edit workspace, approval preview). */
  readonly dirtyScopes: readonly string[]
}

export type ShellEvent =
  | { type: 'SESSION_READY'; context: CompanyContext }
  | { type: 'REQUEST_SWITCH'; target: SwitchTarget }
  | { type: 'CONFIRM_SWITCH_DISCARD' }
  | { type: 'CONFIRM_SWITCH_STAY' }
  | { type: 'REAUTH_OK'; context: CompanyContext }
  | { type: 'REAUTH_FAIL'; restored: CompanyContext | null }
  | { type: 'CAPABILITY_REVOKED' }
  | { type: 'SESSION_RESET' }
  | { type: 'DIRTY_SET'; scope: string }
  | { type: 'DIRTY_CLEAR'; scope: string }

export const initialShellState: ShellState = {
  phase: 'no-active-company',
  activeContext: null,
  pendingTarget: null,
  requestFenceGeneration: 0,
  committedContextRevision: 0,
  dirtyScopes: [],
}

/** Begin a transition: fence up, drop the active context into the neutral state. */
function beginSwitching(state: ShellState, target: SwitchTarget): ShellState {
  return {
    ...state,
    phase: 'switching',
    activeContext: null,
    pendingTarget: target,
    requestFenceGeneration: state.requestFenceGeneration + 1,
  }
}

export function shellReducer(state: ShellState, event: ShellEvent): ShellState {
  switch (event.type) {
    case 'SESSION_READY': {
      // Login / first bound context. ATOMIC-COMMIT of the initial context.
      return {
        ...state,
        phase: 'idle',
        activeContext: event.context,
        pendingTarget: null,
        committedContextRevision: state.committedContextRevision + 1,
      }
    }

    case 'REQUEST_SWITCH': {
      if (state.phase !== 'idle' && state.phase !== 'no-active-company') {
        return state
      }
      if (state.dirtyScopes.length > 0) {
        return { ...state, phase: 'confirming-switch', pendingTarget: event.target }
      }
      return beginSwitching(state, event.target)
    }

    case 'CONFIRM_SWITCH_DISCARD': {
      if (state.phase !== 'confirming-switch' || state.pendingTarget === null) {
        return state
      }
      return beginSwitching({ ...state, dirtyScopes: [] }, state.pendingTarget)
    }

    case 'CONFIRM_SWITCH_STAY': {
      if (state.phase !== 'confirming-switch') return state
      return { ...state, phase: 'idle', pendingTarget: null }
    }

    case 'REAUTH_OK': {
      if (state.phase !== 'switching') return state
      // ATOMIC-COMMIT. Fence already advanced at beginSwitching.
      return {
        ...state,
        phase: 'idle',
        activeContext: event.context,
        pendingTarget: null,
        committedContextRevision: state.committedContextRevision + 1,
      }
    }

    case 'REAUTH_FAIL': {
      if (state.phase !== 'switching') return state
      // Advance the fence again so in-flight target-attempt responses are fenced too.
      const fenced = state.requestFenceGeneration + 1
      if (event.restored !== null) {
        return {
          ...state,
          phase: 'idle',
          activeContext: event.restored,
          pendingTarget: null,
          requestFenceGeneration: fenced,
        }
      }
      return {
        ...state,
        phase: 'no-active-company',
        activeContext: null,
        pendingTarget: null,
        requestFenceGeneration: fenced,
      }
    }

    case 'CAPABILITY_REVOKED': {
      // Fail-closed: fence up, capability marker cleared. Active company identity is
      // kept but every capability-gated mount must fail closed until re-resolved.
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
      // Logout / user change. Everything context-bound is dropped.
      return {
        ...initialShellState,
        requestFenceGeneration: state.requestFenceGeneration + 1,
        committedContextRevision: state.committedContextRevision,
      }
    }

    case 'DIRTY_SET': {
      if (state.dirtyScopes.includes(event.scope)) return state
      return { ...state, dirtyScopes: [...state.dirtyScopes, event.scope] }
    }

    case 'DIRTY_CLEAR': {
      if (!state.dirtyScopes.includes(event.scope)) return state
      return {
        ...state,
        dirtyScopes: state.dirtyScopes.filter((scope) => scope !== event.scope),
      }
    }

    default:
      return state
  }
}

/**
 * INV-NS: the context-owned cache namespace key. Includes committedContextRevision,
 * NOT requestFenceGeneration (that is the request-lifecycle fence, not a cache binding).
 * Returns null when there is no bound context — callers must not issue company-scoped
 * reads in that state.
 */
export function contextKey(state: ShellState): string | null {
  const ctx = state.activeContext
  if (ctx === null || state.phase !== 'idle') return null
  return [
    ctx.userId,
    ctx.scopeMode,
    ctx.companyId ?? 'group',
    ctx.capabilityGeneration ?? 'no-capability',
    String(state.committedContextRevision),
  ].join('|')
}

/** True when capability-gated feature mounts must be closed (fail-closed). */
export function capabilityFailClosed(state: ShellState): boolean {
  return state.phase !== 'idle' || state.activeContext?.capabilityGeneration == null
}
