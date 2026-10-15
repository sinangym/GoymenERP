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

/**
 * A held former-context 401, WITH the scope it belongs to.
 *
 * The scope is not decoration. Without it the held failure outlives the situation
 * that produced it: A→B starts, former A returns 401, the actor resets to C, C
 * becomes ready, C→D starts and its reauth fails — and the stale A failure
 * terminates C. Codex reproduced exactly that (#169 P1-2). Binding it to the
 * actor, the session epoch and the switch attempt makes the sequence expressible
 * but not applicable.
 */
export interface PendingSessionFailure {
  readonly effect: SecurityEffect
  readonly actorId: string | null
  readonly sessionEpoch: number
  /** The switch attempt this failure was captured during. */
  readonly forGeneration: number
}

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
   * The reload-stable identity of this authenticated session (./sessionBinding.ts), for anything PERSISTED across an
   * F5. `sessionEpoch` restarts at 0 in every document, so a stored command keyed on it read as absent after a reload
   * (M01.3 K2 #728/6180459736). The bootstrap establishes it and SESSION_READY carries it in; every transition that
   * advances the epoch drops it in the same step, so no render pairs a new session with the old binding. null = no
   * session yet, or none could be stored: persisted commands then fail closed.
   *
   * In-memory fences keep using `sessionEpoch`; this is only for what outlives the document.
   */
  readonly sessionBinding: string | null
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

  /**
   * A former-context 401 that arrived DURING a switch, held until the target's
   * reauth answers.
   *
   * Terminating immediately was wrong and Codex's #169 P1-1 produced the case: if
   * the switch then SUCCEEDS, the server has just demonstrated the session is
   * alive, so the former 401 cannot have been about the session. Applying it
   * anyway killed a session the server had authenticated one call later.
   *
   * Holding it is bounded and fail-closed: during `switching` no business data is
   * rendered, and the target's own capability call is the authority that resolves
   * it. REAUTH_OK discards this; REAUTH_FAIL applies it.
   */
  readonly pendingFormerSessionFailure: PendingSessionFailure | null

  /**
   * Bumped every time a capability-gated business route is ENTERED.
   *
   * A capability snapshot is taken at bootstrap and at company switch, and until
   * now nothing re-took it when a route was re-entered. Combined with the global
   * `staleTime`, that left a real hole: open /cari, leave, have the grant revoked
   * server-side, come Back within 30 seconds, and the cached rows render with no
   * capability call and no business read at all. Issue #165's boundary cannot
   * help, because it can only judge a request that was actually sent.
   *
   * Codex traced it on both C-01 (#146) and C-03 (#148). The generation is what
   * makes an entry addressable: business query keys carry it, so a new entry
   * cannot be served from the previous entry's cache.
   */
  readonly routeEntryGeneration: number

  /**
   * The entry generation for which a FRESH capability response has landed.
   *
   * While this trails `routeEntryGeneration` the gate renders a neutral loading
   * block. Rendering cached success during revalidation is exactly the frame
   * Codex asked to close, so "stale-while-revalidate" is not available here.
   */
  readonly verifiedEntryGeneration: number

  /**
   * The request fence as it stood when the current entry began.
   *
   * A capability response is only allowed to write back if nothing security
   * relevant happened while it was in flight. Without this, an entry
   * verification that started BEFORE a 403 could land after it and restore the
   * very capability the server had just refused — the entry gate undoing the
   * boundary. Same shape as the snapshot/currentness rule everywhere else here.
   */
  readonly routeEntryFence: number

  /**
   * The `companyId|pathname` the current entry belongs to, or null before any
   * entry has been registered.
   *
   * Derived from SHARED state rather than a component ref on purpose. The first
   * version marked the entry with a `useRef` read during render, which closed the
   * gate on the first paint but broke the rules of render purity — oxlint flagged
   * it and three consecutive full runs were non-deterministic. A flaky
   * authorization gate is worse than none.
   */
  readonly routeEntryKey: string | null

  /**
   * The committed-context revision whose capability snapshot came straight from
   * the server and has not been spent on a route entry yet.
   *
   * Bootstrap and a company switch both commit a freshly fetched snapshot, so the
   * FIRST entry after one of them is already verified — asking again buys no
   * freshness and costs every gated route an extra round-trip. Every entry after
   * that must ask, which is the case #146/#148 are about.
   */
  readonly freshCapabilityRevision: number | null
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
  | {
      type: 'SESSION_READY'
      forSessionEpoch: number
      context: CompanyContext
      /** The binding the bootstrap established for this session; absent = none. */
      sessionBinding?: string | null
    }
  | { type: 'REQUEST_SWITCH'; target: SwitchTarget }
  | { type: 'CONFIRM_SWITCH_DISCARD' }
  | { type: 'CONFIRM_SWITCH_STAY' }
  | { type: 'ROUTE_ENTRY'; key: string }
  | { type: 'ROUTE_EXIT' }
  | { type: 'ROUTE_ENTRY_VERIFIED'; forEntry: number; capabilities: readonly string[]; generation: string }
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
      /**
       * The actor the server authenticates, read when the outcome was REPORTED.
       * Carried on the event rather than kept in state: state can only be updated
       * by an effect, and an effect is a tick too late for this decision
       * (#169 P1-1). null = identity unresolved, which is not by itself stale.
       */
      authenticatedActorId: string | null
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
  sessionBinding: null,
  sessionInvalid: false,
  lastSecurityEffect: null,
  pendingFormerSessionFailure: null,
  routeEntryGeneration: 0,
  verifiedEntryGeneration: 0,
  routeEntryFence: 0,
  routeEntryKey: null,
  freshCapabilityRevision: null,
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
        sessionBinding: event.sessionBinding ?? null,
        committedContextRevision: state.committedContextRevision + 1,
        freshCapabilityRevision: state.committedContextRevision + 1,
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

    case 'ROUTE_ENTRY': {
      // Only a committed context can have a route entry to verify.
      if (state.activeContext === null) return state
      if (state.routeEntryKey === event.key) return state
      const generation = state.routeEntryGeneration + 1
      const spendFresh = state.freshCapabilityRevision === state.committedContextRevision
      return {
        ...state,
        routeEntryGeneration: generation,
        routeEntryFence: state.requestFenceGeneration,
        routeEntryKey: event.key,
        verifiedEntryGeneration: spendFresh ? generation : state.verifiedEntryGeneration,
        freshCapabilityRevision: spendFresh ? null : state.freshCapabilityRevision,
      }
    }

    case 'ROUTE_EXIT': {
      // Leaving clears the registration, so returning to the SAME path is a new
      // entry and re-verifies. Deduping on the path alone made a revisit a no-op,
      // which is precisely the case #146/#148 are about.
      if (state.routeEntryKey === null) return state
      return { ...state, routeEntryKey: null }
    }

    case 'ROUTE_ENTRY_VERIFIED': {
      // Must answer the CURRENT entry. A response for a superseded entry proves
      // nothing about the one being rendered now.
      if (event.forEntry !== state.routeEntryGeneration) return state
      if (state.activeContext === null) return state
      // A security effect fired while this verification was in flight, so its
      // answer predates a decision the boundary has already made. It must not
      // write capabilities back.
      //
      // But dropping it silently was a bug of my own making: the gate's effect is
      // keyed on (entry, verified), neither of which moves on a discard, so the
      // route stayed on the loading block forever. A superseded verification
      // therefore RE-ARMS the entry instead — new generation, new fence, new
      // request.
      if (state.requestFenceGeneration !== state.routeEntryFence) {
        return {
          ...state,
          routeEntryGeneration: state.routeEntryGeneration + 1,
          routeEntryFence: state.requestFenceGeneration,
        }
      }
      return {
        ...state,
        verifiedEntryGeneration: event.forEntry,
        activeContext: {
          ...state.activeContext,
          capabilityGeneration: event.generation,
          capabilities: [...event.capabilities],
        },
      }
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
        freshCapabilityRevision: state.committedContextRevision + 1,
        // The server authenticated this switch, so a former-context 401 held
        // during it cannot have been about the session. Discard it.
        pendingFormerSessionFailure: null,
        // A new session re-enters every route from scratch.
        routeEntryGeneration: 0,
        verifiedEntryGeneration: 0,
        routeEntryFence: 0,
        routeEntryKey: null,
      }
    }

    case 'REAUTH_FAIL': {
      if (state.phase !== 'switching') return state
      if (event.forGeneration !== state.requestFenceGeneration) return state
      const fenced = state.requestFenceGeneration + 1
      const former = state.verifiedFormerContext

      // The target could not be authenticated, so the held former-context 401 is
      // never contradicted. Apply it: the session ends rather than the former
      // context being restored under an authority nothing has confirmed.
      // No re-check of the pending failure's scope here, and that is deliberate.
      //
      // I wrote one, then found it was dead code: this case already returns early
      // unless `event.forGeneration === state.requestFenceGeneration`, the pending
      // was captured with that same generation, a second REQUEST_SWITCH is ignored
      // while switching, and a mid-switch 403 does not advance the fence. Every
      // term I had added was a restatement of a guard three lines above.
      //
      // Duplicating a guard is not defence in depth, it is a second place to get
      // wrong. The scope data stays ON the pending record so the held failure is
      // self-describing and testable; SESSION_RESET drops it; and the behaviour is
      // locked by "a REAUTH_FAIL for a stale attempt cannot apply the held 401".
      const pending = state.pendingFormerSessionFailure

      if (pending !== null) {
        return {
          ...initialShellState,
          requestFenceGeneration: fenced,
          committedContextRevision: state.committedContextRevision,
          sessionEpoch: state.sessionEpoch + 1,
          bootstrappedSessionEpoch: null,
          sessionInvalid: true,
          lastSecurityEffect: pending.effect,
        }
      }

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
        // NOT carried. A session reset ends the situation the held failure belonged
        // to; carrying it let a departed actor's 401 terminate a later session.
        pendingFormerSessionFailure: null,
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
      // A read is judged against the context it was AUTHORIZED under. During a
      // switch that is not activeContext — that is null, and the former context
      // waits in verifiedFormerContext to be restored if the switch fails.
      // Ignoring a rejection that lands in that window let REAUTH_FAIL bring the
      // former context back carrying the very capability the server had just
      // refused (ChatGPT #169 P1-5).
      const judgedAgainst =
        state.phase === 'switching' ? state.verifiedFormerContext : state.activeContext

      const current =
        snap.actorId === (judgedAgainst?.userId ?? null) &&
        snap.sessionEpoch === state.sessionEpoch &&
        snap.companyId === (judgedAgainst?.companyId ?? null) &&
        snap.committedContextRevision === state.committedContextRevision &&
        // The capability generation the read was authorized under must still be
        // the live one.
        //
        // Leaving this out was an inconsistency, and it was reachable: the
        // exactly-once key below DOES include the generation, so once a first 403
        // stripped the capability, a second consumer's outcome — captured under
        // the generation that had just been cleared — was judged "current" while
        // hashing to a DIFFERENT key. It applied a second time and the fence moved
        // twice for one revocation. Codex predicted this in the #169 P1-2 review;
        // `authBoundaryMultiConsumer.test.tsx` reproduces it.
        //
        // Currentness and identity now agree on what "the same authority" means.
        snap.capabilityGeneration === (judgedAgainst?.capabilityGeneration ?? null) &&
        // ...and the actor must still be the one the server authenticates, as of
        // the moment this outcome was reported (P1-4).
        (event.authenticatedActorId === null || snap.actorId === event.authenticatedActorId)
      if (!current) return state

      // A 403 captured when there was no capability to begin with carries no new
      // information: there is nothing left to revoke, and re-fencing would move the
      // fence for a revocation that has already happened.
      //
      // This is the second consumer in the SAB-010 topology. Measured, its snapshot
      // is taken AFTER the first 403 stripped the capability, so it hashes to a
      // different exactly-once key (`…,null` instead of `…,"gen-1"`) and slipped
      // past the ledger — the fence moved twice for one revocation. Deduplicating
      // by key alone could not catch it, because the two events genuinely differ;
      // what makes the second one meaningless is that it revokes nothing.
      //
      // A 401 is deliberately NOT covered: losing the session is meaningful whether
      // or not a capability was held.
      if (event.status === 403 && snap.capabilityGeneration === null) return state

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
      // A held former-context failure counts as already recorded, so a second
      // observer of the same outcome cannot buffer it twice.
      if (state.pendingFormerSessionFailure?.effect.key === key) return state
      const effect: SecurityEffect = { key, kind, namespace: snap.namespace }

      if (event.status === 401) {
        // Mid-switch, and this 401 belongs to the context we are LEAVING: hold it
        // until the target answers. See `pendingFormerSessionFailure`.
        if (state.phase === 'switching') {
          // `lastSecurityEffect` is deliberately NOT set here.
          //
          // That field drives `SecurityEffectRunner`, which calls
          // `queryClient.clear()` for a session-reset. Setting it while merely
          // HOLDING the decision ran the effect immediately — the cache was wiped
          // before the target reauth had answered, so a successful A→B switch was
          // protected only in the reducer, never in the real provider. Codex
          // caught it (#169 P1-1). Recording an effect and RUNNING one are
          // different things and this line was conflating them.
          //
          // Dedupe still holds: a second observer of the same former 401 hashes to
          // the same key and is rejected by the pending check below.
          return {
            ...state,
            pendingFormerSessionFailure: {
              effect,
              actorId: snap.actorId,
              sessionEpoch: state.sessionEpoch,
              forGeneration: state.requestFenceGeneration,
            },
          }
        }
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
      // Strip the capability from WHICHEVER context the read was authorized
      // under. If the rejection arrived mid-switch, the one that matters is the
      // former context — because REAUTH_FAIL restores exactly that, and it must
      // not come back holding authority the server has rejected.
      const stripped = (c: CompanyContext | null) =>
        c === null ? null : { ...c, capabilityGeneration: null, capabilities: [] }
      // The fence is the IDENTITY of the switch attempt in flight. Advancing it
      // here made the reducer reject that attempt's own REAUTH_OK/REAUTH_FAIL —
      // which carries the generation captured when the request started — and the
      // shell stayed in `switching` forever (ChatGPT/Codex #169 P1-2).
      //
      // Stripping the capability is what closes the door. Bumping the fence was
      // never what made a 403 safe; it just broke the transition.
      return {
        ...state,
        requestFenceGeneration:
          state.phase === 'switching'
            ? state.requestFenceGeneration
            : state.requestFenceGeneration + 1,
        activeContext: stripped(state.activeContext),
        verifiedFormerContext:
          state.phase === 'switching'
            ? stripped(state.verifiedFormerContext)
            : state.verifiedFormerContext,
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

/**
 * Phases in which `activeContext` is the server-verified, still-bound context.
 *
 * `confirming-switch` is included deliberately. REQUEST_SWITCH under a dirty
 * scope only ASKS: it does not advance the fence, does not clear or snapshot the
 * context and starts no reauthorization (see the reducer and the "dirty gate"
 * tests). Treating it as neutral unmounted every gated route the moment the
 * user touched the company picker — which destroyed the dirty form that the
 * confirmation exists to protect (ERP-000K §13.1–13.2). Nothing is committed
 * until the user answers; `switching` stays neutral exactly as before.
 */
function contextIsBound(state: ShellState): boolean {
  return state.phase === 'idle' || state.phase === 'confirming-switch'
}

export function contextKey(state: ShellState): ContextKey | null {
  const ctx = state.activeContext
  if (ctx === null || !contextIsBound(state)) return null
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
  return !contextIsBound(state) || state.activeContext?.capabilityGeneration == null
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
