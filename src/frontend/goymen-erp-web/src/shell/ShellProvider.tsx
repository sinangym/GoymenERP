/**
 * Shell React binding: the CompanyContext reducer (./shellState.ts) + a
 * TanStack Query client. Hooks live in ./shellContext.ts.
 *
 * ADR-013. The reducer, request fence and uiState taxonomy are
 * package-independent; this file is where React + Query meet them.
 *
 * The single authoritative request generation is `state.requestFenceGeneration`.
 * SHELL-PILOT-01 enforces the fence through the reducer's `forGeneration` check
 * on REAUTH_OK / REAUTH_FAIL (see ./useShellOrchestrator.ts). The `./contextFence`
 * primitive is for the generated-client transport layer that lands with Issue #122.
 */

import { useEffect, useReducer, useRef, useState, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, shellReducer, type ShellState } from './shellState'
import { ShellDispatchContext, ShellStateContext } from './shellContext'

export function ShellProvider({
  children,
  queryClient: externalClient,
}: {
  readonly children: ReactNode
  /** Test seam — production always uses the internal client. */
  readonly queryClient?: QueryClient
}) {
  const [state, dispatch] = useReducer(shellReducer, initialShellState)
  const [internalClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false },
        },
      }),
  )
  const queryClient = externalClient ?? internalClient

  return (
    <QueryClientProvider client={queryClient}>
      <SecurityEffectRunner state={state} queryClient={queryClient} />
      <ShellStateContext.Provider value={state}>
        <ShellDispatchContext.Provider value={dispatch}>{children}</ShellDispatchContext.Provider>
      </ShellStateContext.Provider>
    </QueryClientProvider>
  )
}

/**
 * Performs the cache side of a security effect the reducer decided (Issue #165).
 *
 * The QueryClient is not reducer state, so the effect itself cannot live there.
 * It runs HERE, once per provider and once per effect identity, rather than in a
 * feature hook — a hook unmounts, and the clear has to happen whether or not the
 * component that saw the rejection is still on screen.
 */
function SecurityEffectRunner({
  state,
  queryClient,
}: {
  readonly state: ShellState
  readonly queryClient: QueryClient
}) {
  const applied = useRef<string | null>(null)
  const effect = state.lastSecurityEffect

  useEffect(() => {
    if (effect === null || applied.current === effect.key) return
    applied.current = effect.key

    if (effect.kind === 'session-reset') {
      // The session is dead, so every read taken under it is dead too — feature,
      // identity, directory and capability alike.
      queryClient.clear()
      return
    }
    // Remove, not invalidate: invalidating would refetch under the very authority
    // the server just rejected.
    if (effect.namespace !== null) {
      const ns = effect.namespace
      queryClient.removeQueries({ predicate: (query) => query.queryKey[0] === ns })
    }
  }, [effect, queryClient])

  return null
}
