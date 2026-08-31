/**
 * Drives the shell bootstrap and company-switch flows against the real backend.
 * Call ONCE, from <AppShell> only (it owns the effects).
 *
 *   bootstrap : identity + directory  ->  pick company  ->  capabilities  ->  SESSION_READY
 *   switch    : REQUEST_SWITCH  ->  capabilities(pendingTarget)  ->  REAUTH_OK | REAUTH_FAIL
 *
 * Fence rules (docs/delivery/CRITICAL_ANALYSIS/shell-companycontext-fence/claude.md):
 *  - the switch capability fetch carries the attempt generation
 *    (state.requestFenceGeneration); a late resolve from a superseded attempt is
 *    dropped by the reducer's REAUTH_* forGeneration check;
 *  - capability unresolved / 503 / malformed => context commits with a null
 *    capability generation => capabilityFailClosed() is true => no feature mounts.
 */

import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  fetchCompanyDirectory,
  fetchIdentity,
  fetchShellCapabilities,
  type CompanySummary,
  type IdentitySummary,
  type ShellCapabilitySnapshot,
} from './api'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from './uiState'
import { useShellDispatch, useShellState } from './shellContext'
import type { CompanyContext } from './shellState'

const COMPANY_PREFERENCE_KEY = 'goymen-company-id'

function readCompanyPreference(): string | null {
  try {
    return window.localStorage.getItem(COMPANY_PREFERENCE_KEY)
  } catch {
    return null
  }
}

function writeCompanyPreference(companyId: string): void {
  try {
    window.localStorage.setItem(COMPANY_PREFERENCE_KEY, companyId)
  } catch {
    // preference only — a storage failure never blocks the shell
  }
}

function capabilityGenerationOf(outcome: ViewOutcome<ShellCapabilitySnapshot>): string | null {
  return outcome.type === 'ok-single' ? outcome.data.generation : null
}

function directoryOf(outcome: ViewOutcome<CompanySummary> | undefined): readonly CompanySummary[] {
  return outcome?.type === 'ok' ? outcome.data : []
}

/** Cached directory read. Safe to call from any component (useQuery dedupes). */
export function useCompanyDirectory(): readonly CompanySummary[] {
  const query = useQuery({
    queryKey: ['shell', 'directory'],
    queryFn: ({ signal }) => fetchCompanyDirectory(signal),
  })
  return directoryOf(query.data)
}

export interface ShellOrchestration {
  readonly identityState: RemoteViewState<IdentitySummary>
  readonly directoryState: RemoteViewState<readonly CompanySummary[]>
  readonly directory: readonly CompanySummary[]
  readonly identity: IdentitySummary | null
}

export function useShellOrchestrator(): ShellOrchestration {
  const state = useShellState()
  const dispatch = useShellDispatch()

  const identityQuery = useQuery({
    queryKey: ['shell', 'identity'],
    queryFn: ({ signal }) => fetchIdentity(signal),
  })
  const directoryQuery = useQuery({
    queryKey: ['shell', 'directory'],
    queryFn: ({ signal }) => fetchCompanyDirectory(signal),
  })

  const identityState = (
    identityQuery.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(identityQuery.data ?? { type: 'malformed' })
  ) as RemoteViewState<IdentitySummary>
  const directoryState = (
    directoryQuery.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(directoryQuery.data ?? { type: 'malformed' })
  ) as RemoteViewState<readonly CompanySummary[]>

  const identity = identityState.kind === 'ready' ? identityState.data : null
  const directory = directoryOf(directoryQuery.data)

  // --- bootstrap -------------------------------------------------------------
  const identityId = identity?.userId ?? null
  const directoryReady = directoryState.kind === 'ready'
  const neverBootstrapped =
    state.phase === 'no-active-company' && state.committedContextRevision === 0

  useEffect(() => {
    if (!neverBootstrapped || identityId === null || !directoryReady) return
    const list = directoryOf(directoryQuery.data)
    const preferred = readCompanyPreference()
    const company = list.find((entry) => entry.id === preferred) ?? list[0]
    if (company === undefined) return

    const controller = new AbortController()
    void fetchShellCapabilities(company.id, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      const context: CompanyContext = {
        userId: identityId,
        scopeMode: 'single',
        companyId: company.id,
        capabilityGeneration: capabilityGenerationOf(outcome),
      }
      writeCompanyPreference(company.id)
      dispatch({ type: 'SESSION_READY', context })
    })
    return () => controller.abort()
  }, [neverBootstrapped, identityId, directoryReady, directoryQuery.data, dispatch])

  // --- switch --------------------------------------------------------------
  const switchCompanyId =
    state.phase === 'switching' ? (state.pendingTarget?.companyId ?? null) : null
  const switchScopeMode = state.pendingTarget?.scopeMode ?? 'single'
  const attemptGeneration = state.requestFenceGeneration

  useEffect(() => {
    if (switchCompanyId === null || identityId === null) return
    const controller = new AbortController()
    void fetchShellCapabilities(switchCompanyId, controller.signal).then((outcome) => {
      if (controller.signal.aborted) return
      const capability = classifyOutcome(outcome)
      if (capability.kind === 'stale-rejected') return
      if (
        capability.kind === 'unauthenticated' ||
        capability.kind === 'forbidden' ||
        capability.kind === 'not-found'
      ) {
        dispatch({
          type: 'REAUTH_FAIL',
          forGeneration: attemptGeneration,
          restoredCapabilityGeneration: null,
        })
        return
      }
      writeCompanyPreference(switchCompanyId)
      dispatch({
        type: 'REAUTH_OK',
        forGeneration: attemptGeneration,
        context: {
          userId: identityId,
          scopeMode: switchScopeMode,
          companyId: switchCompanyId,
          capabilityGeneration: capabilityGenerationOf(outcome),
        },
      })
    })
    return () => controller.abort()
  }, [switchCompanyId, switchScopeMode, attemptGeneration, identityId, dispatch])

  return { identityState, directoryState, directory, identity }
}
