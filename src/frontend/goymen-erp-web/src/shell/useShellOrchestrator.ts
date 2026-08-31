/**
 * Drives the shell bootstrap and company-switch flows against the real backend.
 * Call ONCE, from <AppShell> only (it owns the effects).
 *
 *   bootstrap : identity + directory  ->  pick company  ->  capabilities  ->  SESSION_READY
 *   switch    : REQUEST_SWITCH  ->  capabilities(pendingTarget)  ->  REAUTH_OK | REAUTH_FAIL
 *
 * Codex #139 review fixes:
 *  - P1-1: when the authenticated actor changes, SESSION_RESET is dispatched and the
 *    QueryClient is cleared — old company/capability/directory cache cannot survive.
 *  - P1-2: a context is committed (SESSION_READY / REAUTH_OK) ONLY on a genuine
 *    200 + valid capability snapshot. 401 -> SESSION_RESET; 403/404/5xx/offline/
 *    malformed -> no commit (bootstrap: retryable failure state; switch: REAUTH_FAIL
 *    restoring the former context). A failed switch always leaves 'switching'.
 *  - P1-3: capability envelope is validated for exact version + non-empty generation
 *    in api.ts; anything else is 'malformed'.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
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
import { contextActorMismatch, needsBootstrap, type CompanyContext } from './shellState'

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

/** How a capability fetch should be handled by the caller. */
type CapabilityResolution =
  | {
      readonly kind: 'ready'
      readonly generation: string
      readonly capabilities: readonly string[]
    }
  | { readonly kind: 'session-invalid' } // 401
  | { readonly kind: 'denied' } // 403 / 404
  | { readonly kind: 'unavailable' } // 5xx / offline / malformed — retryable, never commits

function resolveCapability(outcome: ViewOutcome<ShellCapabilitySnapshot>): CapabilityResolution {
  const state = classifyOutcome(outcome)
  switch (state.kind) {
    case 'ready':
      return {
        kind: 'ready',
        generation: capabilityGenerationOf(outcome) ?? '',
        capabilities: outcome.type === 'ok-single' ? outcome.data.capabilities : [],
      }
    case 'unauthenticated':
      return { kind: 'session-invalid' }
    case 'forbidden':
    case 'not-found':
      return { kind: 'denied' }
    default:
      return { kind: 'unavailable' }
  }
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
  /** Identity/directory resolved, a company context is being established. */
  readonly bootstrapping: boolean
  /** Bootstrap could not commit a context (5xx / offline / malformed). Retryable. */
  readonly bootstrapFailure: boolean
  /** The most recent company switch failed and the former context was restored. */
  readonly switchFailure: boolean
  /** A 401 was seen — the session is dead. Terminal until re-login (no login flow yet). */
  readonly sessionInvalid: boolean
  /**
   * The bound context belongs to another actor than the authenticated one. Computed
   * during render (Codex #139 P1-1) so the caller can fail closed in the SAME frame
   * the new identity arrives, instead of one paint later.
   */
  readonly actorMismatch: boolean
  readonly retry: () => void
}

export function useShellOrchestrator(): ShellOrchestration {
  const state = useShellState()
  const dispatch = useShellDispatch()
  const queryClient = useQueryClient()

  const seenActorId = useRef<string | null>(null)
  const [bootstrapFailure, setBootstrapFailure] = useState(false)
  const [switchFailure, setSwitchFailure] = useState(false)
  const [sessionInvalid, setSessionInvalid] = useState(false)

  // A dead session must stop talking to the server. Without `enabled`, the P1-3
  // `clear()` below would evict these queries while their observers are still
  // mounted, and they would immediately refetch under the very session that just
  // returned 401 — a quiet request loop behind an "Oturum sonlandı" screen.
  const identityQuery = useQuery({
    queryKey: ['shell', 'identity'],
    queryFn: ({ signal }) => fetchIdentity(signal),
    enabled: !sessionInvalid,
  })
  const directoryQuery = useQuery({
    queryKey: ['shell', 'directory'],
    queryFn: ({ signal }) => fetchCompanyDirectory(signal),
    enabled: !sessionInvalid,
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
  const identityId = identity?.userId ?? null

  // --- P1-1: actor change -> full reset --------------------------------------
  useEffect(() => {
    if (identityId === null) return
    if (seenActorId.current === null) {
      seenActorId.current = identityId
      return
    }
    if (seenActorId.current !== identityId) {
      seenActorId.current = identityId
      setBootstrapFailure(false)
      setSwitchFailure(false)
      setSessionInvalid(false)
      dispatch({ type: 'SESSION_RESET' })
      queryClient.clear()
    }
  }, [identityId, dispatch, queryClient])

  // --- bootstrap -----------------------------------------------------------
  const directoryReady = directoryState.kind === 'ready'
  // Codex #139 P1-1: keyed on sessionEpoch, so a new actor after SESSION_RESET
  // bootstraps even though committedContextRevision stays monotonic.
  const neverBootstrapped = needsBootstrap(state)
  const sessionEpoch = state.sessionEpoch

  useEffect(() => {
    if (sessionInvalid || !neverBootstrapped || identityId === null || !directoryReady) return
    const list = directoryOf(directoryQuery.data)
    const preferred = readCompanyPreference()
    const company = list.find((entry) => entry.id === preferred) ?? list[0]
    if (company === undefined) return

    const controller = new AbortController()
    void fetchShellCapabilities(company.id, controller.signal)
      .then((outcome) => {
        if (controller.signal.aborted) return
        const resolution = resolveCapability(outcome)
        if (resolution.kind === 'session-invalid') {
          setBootstrapFailure(false)
          setSessionInvalid(true)
          // Codex #139 P1-3: the session is dead, so every cached read taken under it
          // is dead too — including business caches this hook never named.
          queryClient.clear()
          return
        }
        if (resolution.kind !== 'ready') {
          // denied / unavailable -> stay no-active-company, retryable.
          setBootstrapFailure(true)
          return
        }
        setBootstrapFailure(false)
        const context: CompanyContext = {
          userId: identityId,
          scopeMode: 'single',
          companyId: company.id,
          capabilityGeneration: resolution.generation,
          capabilities: resolution.capabilities,
        }
        // Fenced to the epoch this request was STARTED under; the reducer drops it
        // if a SESSION_RESET landed in between (Codex #139 P1-2).
        dispatch({ type: 'SESSION_READY', forSessionEpoch: sessionEpoch, context })
      })
      .catch(() => {
        if (controller.signal.aborted) return
        setBootstrapFailure(true)
      })
    return () => controller.abort()
  }, [
    sessionInvalid,
    neverBootstrapped,
    identityId,
    directoryReady,
    directoryQuery.data,
    sessionEpoch,
    dispatch,
    queryClient,
  ])

  // --- switch ------------------------------------------------------------
  const switchCompanyId =
    state.phase === 'switching' ? (state.pendingTarget?.companyId ?? null) : null
  const switchScopeMode = state.pendingTarget?.scopeMode ?? 'single'
  const attemptGeneration = state.requestFenceGeneration

  useEffect(() => {
    if (switchCompanyId === null || identityId === null) return
    const controller = new AbortController()
    const fail = () => {
      setSwitchFailure(true)
      dispatch({ type: 'REAUTH_FAIL', forGeneration: attemptGeneration })
    }
    void fetchShellCapabilities(switchCompanyId, controller.signal)
      .then((outcome) => {
        if (controller.signal.aborted) return
        if (classifyOutcome(outcome).kind === 'stale-rejected') return
        const resolution = resolveCapability(outcome)
        if (resolution.kind === 'session-invalid') {
          setSwitchFailure(false)
          setSessionInvalid(true)
          dispatch({ type: 'SESSION_RESET' })
          // Codex #139 P1-3: SESSION_RESET drops the shell context but left every
          // company-scoped business query sitting in the cache, ready to be served
          // to whoever authenticates next in this tab.
          queryClient.clear()
          return
        }
        if (resolution.kind !== 'ready') {
          fail()
          return
        }
        setSwitchFailure(false)
        dispatch({
          type: 'REAUTH_OK',
          forGeneration: attemptGeneration,
          context: {
            userId: identityId,
            scopeMode: switchScopeMode,
            companyId: switchCompanyId,
            capabilityGeneration: resolution.generation,
            capabilities: resolution.capabilities,
          },
        })
      })
      .catch(() => {
        if (controller.signal.aborted) return
        fail()
      })
    return () => controller.abort()
  }, [switchCompanyId, switchScopeMode, attemptGeneration, identityId, dispatch, queryClient])

  /**
   * The company preference mirrors the COMMITTED context, never an attempted target.
   *
   * It used to be written inside the switch effect just before dispatching REAUTH_OK.
   * The reducer's fence correctly rejected a superseded REAUTH_OK, but the preference
   * had already been written — so the next bootstrap silently opened a company the user
   * never successfully switched to, with no successful reauthorization behind it.
   * Deriving it from `activeContext` makes that class of leak unrepresentable.
   */
  const committedCompanyId = state.phase === 'idle' ? (state.activeContext?.companyId ?? null) : null
  useEffect(() => {
    if (committedCompanyId !== null) writeCompanyPreference(committedCompanyId)
  }, [committedCompanyId])

  const retry = useCallback(() => {
    setBootstrapFailure(false)
    setSwitchFailure(false)
    void queryClient.invalidateQueries({ queryKey: ['shell'] })
  }, [queryClient])

  return {
    identityState,
    directoryState,
    directory,
    identity,
    bootstrapping:
      neverBootstrapped && !bootstrapFailure && !sessionInvalid && directory.length > 0,
    bootstrapFailure: bootstrapFailure && state.phase === 'no-active-company',
    switchFailure: switchFailure && state.phase === 'idle',
    sessionInvalid,
    actorMismatch: contextActorMismatch(state, identityId),
    retry,
  }
}
