/**
 * React binding for one logical command attempt (see ./commandAttempt.ts for the
 * rules). Owns the in-memory key, the in-flight guard, the abort on unmount and
 * the two fences a write needs beyond what a read needs:
 *
 *  1. the central 401/403 boundary — reported with the snapshot captured BEFORE
 *     the request started, exactly like the read paths (SAB-001/006);
 *  2. a context binding — the outcome is applied only if the actor, company,
 *     committed revision and session epoch are still the ones the attempt was
 *     made under. A late response from a company the user has discarded writes
 *     nothing: no state, no navigation, no announcement (spec §4.3, §7.1).
 *
 * The owning screen is already remounted on a context change (same `key`
 * pattern as C-01/C-03), so the binding check is defence in depth for the frame
 * between the switch and the unmount, and for a response that outlives it.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { ShellState } from '../shell/shellState'
import type { CommandOutcome } from './commandApi'
import {
  abandonAttempt,
  beginAttempt,
  canBeginAttempt,
  generateAttemptKey,
  repeatsUnresolvedAttempt,
  retryAttempt,
  settleAttempt,
  type AttemptSnapshot,
  type AttemptState,
} from './commandAttempt'

/** Sends the frozen snapshot under the given key. Called once per submit/retry. */
export type CommandSender<R> = (idempotencyKey: string, signal: AbortSignal) => Promise<CommandOutcome<R>>

/**
 * What `submit` did. `blocked`: a request is in flight, the attempt is frozen,
 * failed-but-unacknowledged or completed, or there is no committed context to
 * bind to. `unchanged`: the payload repeats an ABANDONED attempt whose outcome is
 * still unknown — nothing is sent and no key is minted; the owner offers the
 * same-key retry of that attempt instead (§4.3, C-004-P1-001).
 */
export type SubmitResult = 'started' | 'blocked' | 'unchanged'

/**
 * The authority an attempt is bound to. Null when there is no committed
 * single-company context — a write cannot be started then.
 */
export function attemptBinding(state: ShellState): string | null {
  const context = state.activeContext
  if (context === null || context.companyId === null) return null
  return JSON.stringify([
    context.userId,
    context.companyId,
    context.capabilityGeneration,
    state.committedContextRevision,
    state.sessionEpoch,
  ])
}

export interface CommandAttemptController<P, R> {
  readonly state: AttemptState<P, R>
  /**
   * Begin a NEW logical attempt. Sends nothing and returns `blocked` while one is
   * in flight, frozen, failed-but-unacknowledged, completed, or when there is no
   * committed context to bind to; returns `unchanged` (still sending nothing)
   * when the payload repeats an abandoned attempt that is still unresolved.
   */
  readonly submit: (operation: string, payload: P, send: CommandSender<R>) => SubmitResult
  /**
   * Resend the kept snapshot with the SAME key. Only from `unknown` or `abandoned`.
   *
   * `revalidate` is the caller's fresh authorized + acknowledged GET in this
   * context; it is required before a second POST, and a failed check sends nothing
   * while leaving the attempt exactly as it was.
   */
  readonly retry: (revalidate?: () => Promise<boolean>) => void
  /** True while that pre-retry check is running. */
  readonly revalidating: boolean
  /** Withdraw a pending retry check: the answer it is waiting for no longer applies. */
  readonly invalidateRetry: () => void
  /**
   * Explicitly leave the current attempt. A definite failure or a completion is
   * closed (`idle`); an UNKNOWN outcome becomes `abandoned` — unfrozen, but its
   * identity is kept so the unchanged payload is never re-sent under a new key.
   */
  readonly abandon: () => void
}

export function useCommandAttempt<P, R>(): CommandAttemptController<P, R> {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const [state, setState] = useState<AttemptState<P, R>>({ phase: 'idle' })

  // Mirrors for the async continuation. Written in an effect, never during
  // render (oxlint/react purity — the same lesson as CapabilityRoute).
  const shellRef = useRef(shell)
  const boundaryRef = useRef(boundary)
  const stateRef = useRef(state)
  useEffect(() => {
    shellRef.current = shell
    boundaryRef.current = boundary
    stateRef.current = state
  })

  const senderRef = useRef<CommandSender<R> | null>(null)
  const controllerRef = useRef<AbortController | null>(null)
  const inFlightRef = useRef(false)
  const disposedRef = useRef(false)

  useEffect(() => {
    // Re-armed on every mount: StrictMode runs the cleanup once before the real
    // mount, and a ref left `true` there would discard every outcome in dev.
    disposedRef.current = false
    return () => {
      disposedRef.current = true
      controllerRef.current?.abort()
    }
  }, [])

  const dispatchSend = useCallback((snapshot: AttemptSnapshot<P>, send: CommandSender<R>) => {
    inFlightRef.current = true
    const controller = new AbortController()
    controllerRef.current = controller
    // SAB-001: security bindings captured BEFORE the request starts.
    const security = boundaryRef.current.capture()

    void send(snapshot.key, controller.signal).then((outcome) => {
      inFlightRef.current = false
      // The reducer judges currentness and applies the effect exactly once, so a
      // 401/403 is reported whether or not this screen is still mounted.
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundaryRef.current.report(outcome.status, security)
      }
      if (disposedRef.current) return
      // A departed context: the outcome belongs to an attempt that no longer has a
      // screen. Nothing is written, nothing is announced.
      if (attemptBinding(shellRef.current) !== snapshot.binding) return
      setState((current) =>
        current.phase === 'submitting' && current.snapshot.key === snapshot.key
          ? settleAttempt(current, outcome)
          : current,
      )
    })
  }, [])

  const submit = useCallback(
    (operation: string, payload: P, send: CommandSender<R>): SubmitResult => {
      if (inFlightRef.current) return 'blocked'
      const current = stateRef.current
      if (!canBeginAttempt(current)) return 'blocked'
      const binding = attemptBinding(shell)
      if (binding === null) return 'blocked'
      // An abandoned attempt is still unresolved: the same operation with the
      // same payload IS that attempt. It is retried under its own key or not at
      // all — a new key here could duplicate a record the server already wrote.
      if (repeatsUnresolvedAttempt(current, operation, payload)) return 'unchanged'
      // The key is minted HERE, for this snapshot, and nowhere else.
      const snapshot: AttemptSnapshot<P> = { key: generateAttemptKey(), operation, binding, payload }
      senderRef.current = send
      stateRef.current = beginAttempt(snapshot)
      setState(stateRef.current)
      dispatchSend(snapshot, send)
      return 'started'
    },
    [dispatchSend, shell],
  )

  /**
   * Re-send the SAME attempt, after proving the ground it stands on is still there.
   *
   * `revalidate` is the caller's fresh, authorized, decoder-valid, acknowledged GET
   * in this context. It is REQUIRED before a second POST, because a command failure
   * does not revoke readiness: without it the retry would ride a grant earned before
   * the first send and post again to a server we have not re-checked (§4.2).
   *
   * If it does not pass, nothing is sent and the attempt stays exactly as it was —
   * same key, same payload, same uncertainty. A failed re-check is not an answer
   * about the first request either.
   *
   * Never automatic. This runs only from the user's explicit retry.
   */
  /** True while the pre-retry re-check is running; the notice shows it as busy. */
  const revalidatingRef = useRef(false)
  const [revalidating, setRevalidating] = useState(false)
  /**
   * Which retry the pending re-check belongs to.
   *
   * The check is asynchronous, so the user can act while it runs — and the action
   * that matters most is leaving the attempt. Without this, "İşlemi bırak" during a
   * pending check was overtaken by the check's own success and a POST went out after
   * the user had said to stop. The last decision has to win, so anything that
   * supersedes a retry bumps this and the continuation checks it before sending.
   */
  const retryGenerationRef = useRef(0)

  const retry = useCallback(
    (revalidate?: () => Promise<boolean>) => {
      const current = stateRef.current
      if (current.phase !== 'unknown' && current.phase !== 'abandoned') return
      if (inFlightRef.current || senderRef.current === null) return
      const sender = senderRef.current
      const snapshot = current.snapshot
      const generation = ++retryGenerationRef.current

      if (revalidate === undefined) {
        stateRef.current = retryAttempt(current)
        setState(stateRef.current)
        dispatchSend(snapshot, sender)
        return
      }

      revalidatingRef.current = true
      setRevalidating(true)
      void revalidate()
        .then((ok) => {
          revalidatingRef.current = false
          if (disposedRef.current) return
          setRevalidating(false)
          // Superseded: the user abandoned the attempt, started another retry, or
          // changed what the screen is asking for while this check was in flight.
          // A check that finishes after that answers a question already withdrawn.
          if (retryGenerationRef.current !== generation) return
          if (!ok) return
          // Re-read the state: the context may have moved while the check ran.
          const latest = stateRef.current
          if (latest.phase !== 'unknown' && latest.phase !== 'abandoned') return
          if (latest.snapshot.key !== snapshot.key) return
          stateRef.current = retryAttempt(latest)
          setState(stateRef.current)
          dispatchSend(latest.snapshot, sender)
        })
        .catch(() => {
          revalidatingRef.current = false
          if (!disposedRef.current) setRevalidating(false)
        })
    },
    [dispatchSend],
  )

  /**
   * Withdraw any pending retry check.
   *
   * Called by `abandon`, and by a screen whose own state moved under the check — a
   * changed search, a new query — because a re-check bound to what the user WAS
   * looking at cannot license a write for what they are looking at now.
   */
  const invalidateRetry = useCallback(() => {
    retryGenerationRef.current += 1
    revalidatingRef.current = false
    setRevalidating(false)
  }, [])

  const abandon = useCallback(() => {
    if (inFlightRef.current) return
    // The user's last decision wins over a check that is still running.
    invalidateRetry()
    const next = abandonAttempt(stateRef.current)
    // The sender outlives the attempt only while its snapshot may still be
    // retried under its own key (unresolved); a closed attempt drops it.
    if (next.phase !== 'abandoned') senderRef.current = null
    stateRef.current = next
    setState(next)
  }, [invalidateRetry])

  return { state, submit, retry, abandon, invalidateRetry, revalidating }
}
