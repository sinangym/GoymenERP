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

import { useEffect, type ReactNode } from 'react'
import { useLocation } from 'react-router'
import { hasCapability } from './shellState'
import { useShellDispatch, useShellState } from './shellContext'
import { StateBlock } from './StateBlock'
import { fetchShellCapabilities } from './api'
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
  const entryKey = companyId === null ? null : `${companyId}|${pathname}`
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

  useEffect(() => {
    if (companyId === null || verified >= entry) return
    const controller = new AbortController()
    void fetchShellCapabilities(companyId, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      if (outcome.type !== 'ok-single') return
      dispatch({
        type: 'ROUTE_ENTRY_VERIFIED',
        forEntry: entry,
        capabilities: outcome.data.capabilities,
        generation: outcome.data.generation,
      })
    })
    return () => controller.abort()
  }, [companyId, dispatch, entry, verified])

  // Not yet verified for this entry: neutral loading, never cached content.
  //
  // Deliberately NOT the not-found block — a revisit that is still revalidating
  // has not been denied, and showing denial would be both wrong and a disclosure
  // of a different fact than the one we hold.
  if (companyId !== null && (pending || verified < entry)) {
    return <StateBlock state={{ kind: 'initial-loading' }} subject="sayfa" />
  }

  if (!hasCapability(state, capability)) {
    return <StateBlock state={{ kind: 'not-found' }} subject="sayfa" />
  }
  return <>{children}</>
}
