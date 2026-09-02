/**
 * Mounts a feature only when the server granted its capability, and only when
 * that grant was confirmed FOR THIS ROUTE ENTRY.
 *
 * ERP-000K §10.2: this is NOT authorization. A direct URL, a refresh, a new tab
 * and browser back/forward all land here, and the backend still re-authorizes
 * every request. What this guarantees is the shell-side half: an ungranted or
 * unresolved capability mounts nothing, and it does so WITHOUT disclosing whether
 * the feature or any record exists (§10.2 non-disclosure) — the same neutral
 * block either way.
 *
 * WHY THE ENTRY GATE EXISTS. Checking the in-memory snapshot was not enough, and
 * Codex traced the hole on both #146 and #148:
 *
 *   open /cari → leave → grant revoked server-side → Back within 30 seconds
 *
 * The capability snapshot was taken at bootstrap and never re-taken; the business
 * query was still fresh under the global `staleTime`. So the revisit rendered
 * cached rows having sent NO capability call and NO business read. Issue #165's
 * 401/403 boundary cannot close that path, because it can only judge a request
 * that was actually made.
 *
 * `staleTime: 0` or `refetchOnMount: 'always'` alone would not fix it either:
 * both still let cached data paint while the refetch is in flight. That frame is
 * the vulnerability, so this gate withholds the children until a fresh capability
 * response has landed for the current entry.
 */

import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { useLocation } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { hasCapability } from './shellState'
import { useShellDispatch, useShellState } from './shellContext'
import { StateBlock } from './StateBlock'
import { fetchShellCapabilities } from './api'
import { resolveCapability } from './useShellOrchestrator'
import type { CapabilityId } from './capabilities'

export function CapabilityRoute({
  capability,
  children,
}: {
  readonly capability: CapabilityId
  readonly children: ReactNode
}) {
  const state = useShellState()
  const dispatch = useShellDispatch()
  const { pathname } = useLocation()
  const companyId = state.activeContext?.companyId ?? null

  // One entry per pathname per committed context. Keyed on the pathname so
  // /cari/A → /cari/B counts as a new entry even though this component does not
  // unmount between them.
  //
  // Registered DURING RENDER, not in an effect. An effect runs after the first
  // paint, which left the gate open for exactly one render: the children mounted,
  // the business query fired under the previous entry, and only then did the gate
  // close. Measured — that produced two reads per entry and the first one was
  // unauthorized-for-this-entry by construction. The open frame is the whole
  // thing this gate exists to remove, so it cannot be opened even once.
  // Keyed on the PATH only, deliberately.
  //
  // A company switch already re-fetches capabilities through REAUTH and commits
  // them atomically, so re-verifying here duplicated that work — and the extra
  // round-trip closed the gate mid-switch, which is what made
  // `is remounted on a context change` and `is ignored after the switch` flaky.
  // The gate exists for the case the switch machinery does NOT cover: re-entering
  // a route inside one committed context.
  const entryKey = companyId === null ? null : pathname
  // Read from SHARED state, not a component ref. A ref read during render breaks
  // render purity: oxlint flags it and it made three consecutive full runs
  // non-deterministic. A flaky authorization gate is worse than none.
  const pending = entryKey !== null && state.routeEntryKey !== entryKey

  useEffect(() => {
    if (entryKey === null || !pending) return
    dispatch({ type: 'ROUTE_ENTRY', key: entryKey })
    // `pending` is a dependency, not just a guard: a session reset clears
    // `routeEntryKey` while the pathname stays the same, so keying only on the
    // path would leave the gate closed with nothing left to reopen it.
  }, [dispatch, entryKey, pending])

  // Leaving clears the registration so a return to the same path re-verifies.
  useEffect(() => () => dispatch({ type: 'ROUTE_EXIT' }), [dispatch])

  const entry = state.routeEntryGeneration
  const verified = state.verifiedEntryGeneration
  const queryClient = useQueryClient()

  // A retryable failure for a specific entry. Scoped to the entry so a stale
  // failure cannot suppress a newer attempt.
  const [attempt, setAttempt] = useState<
    { readonly kind: 'idle' } | { readonly kind: 'failed'; readonly forEntry: number }
  >({ kind: 'idle' })

  // DERIVED, not reset. A failure belongs to the entry it happened on, so a newer
  // entry simply does not match and the failure stops applying — no state update
  // during render, which is what made the first version of this non-deterministic.
  const failed = attempt.kind === 'failed' && attempt.forEntry === entry

  const retry = useCallback(() => {
    setAttempt({ kind: 'idle' })
  }, [])

  // Every outcome is classified by the SAME function the orchestrator uses.
  //
  // The first version returned early on anything that was not `ok-single`, which
  // discarded the whole failure matrix: a 401 never reached the reducer so the
  // dead session and its cache lived on, and a 500 left the user on the loading
  // block forever with no error and no retry. Codex reproduced both (#192
  // BLOCKER). Writing a second, thinner classification beside an existing one is
  // how the two drift apart, so there is only the one.
  useEffect(() => {
    if (companyId === null || verified >= entry || failed) return
    const controller = new AbortController()
    void fetchShellCapabilities(companyId, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      const resolution = resolveCapability(outcome)

      if (resolution.kind === 'session-invalid') {
        // Terminal, and central: the same event and the same cache clear the
        // orchestrator performs, so a route entry cannot become a second way to
        // survive a dead session.
        dispatch({ type: 'SESSION_INVALIDATED' })
        queryClient.clear()
        return
      }

      if (resolution.kind === 'denied') {
        // Fail closed: verify the entry with NO capabilities, so the gate opens
        // onto the same neutral not-found block any ungranted route gets. It must
        // still be a verification, or the entry would hang.
        dispatch({
          type: 'ROUTE_ENTRY_VERIFIED',
          forEntry: entry,
          capabilities: [],
          generation: '',
        })
        return
      }

      if (resolution.kind === 'unavailable') {
        // Retryable and never committing. Showing loading forever is not a safe
        // failure, it is an unusable one.
        setAttempt({ kind: 'failed', forEntry: entry })
        return
      }

      dispatch({
        type: 'ROUTE_ENTRY_VERIFIED',
        forEntry: entry,
        capabilities: resolution.capabilities,
        generation: resolution.generation,
      })
    })
    return () => controller.abort()
  }, [companyId, dispatch, entry, failed, queryClient, verified])

  // Not yet verified for this entry: neutral loading, never cached content.
  //
  // Deliberately NOT the not-found block — a revisit that is still revalidating
  // has not been denied, and showing denial would be both wrong and a disclosure
  // of a different fact than the one we hold.
  if (companyId !== null && failed) {
    // The entry could not be authorized for a retryable reason. Nothing is shown
    // and nothing is committed; the user gets a way forward instead of a spinner.
    return <StateBlock state={{ kind: 'network-error' }} subject="sayfa" onRetry={retry} />
  }

  if (companyId !== null && (pending || verified < entry)) {
    return <StateBlock state={{ kind: 'initial-loading' }} subject="sayfa" />
  }

  if (!hasCapability(state, capability)) {
    return <StateBlock state={{ kind: 'not-found' }} subject="sayfa" />
  }
  return <>{children}</>
}
