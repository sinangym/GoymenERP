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

import { useReducer, useState, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { initialShellState, shellReducer } from './shellState'
import { ShellDispatchContext, ShellStateContext } from './shellContext'

export function ShellProvider({ children }: { readonly children: ReactNode }) {
  const [state, dispatch] = useReducer(shellReducer, initialShellState)
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false },
        },
      }),
  )

  return (
    <QueryClientProvider client={queryClient}>
      <ShellStateContext.Provider value={state}>
        <ShellDispatchContext.Provider value={dispatch}>{children}</ShellDispatchContext.Provider>
      </ShellStateContext.Provider>
    </QueryClientProvider>
  )
}
