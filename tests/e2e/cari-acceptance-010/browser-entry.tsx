/** Test-only composition: unchanged production provider/router, plus a passive state/cache probe.
 * The only driver actions invalidate existing queries; no fabricated reducer outcomes or cache data.
 * QueryClient options match ShellProvider's internal defaults. No production module is transformed.
 */
import { StrictMode, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, useQueryClient } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { ShellProvider } from '../../../src/frontend/goymen-erp-web/src/shell/ShellProvider'
import { shellRouter } from '../../../src/frontend/goymen-erp-web/src/shell/router'
import { useShellState } from '../../../src/frontend/goymen-erp-web/src/shell/shellContext'
import { contextKey, contextKeyString } from '../../../src/frontend/goymen-erp-web/src/shell/shellState'
import '../../../src/frontend/goymen-erp-web/src/index.css'
import '../../../src/frontend/goymen-erp-web/src/App.css'

const client = new QueryClient({
  defaultOptions: { queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false } },
})
export interface ProbeState {
  phase: string
  actor: string | null
  company: string | null
  epoch: number
  revision: number
  namespace: string | null
  sessionInvalid: boolean
  hasLink: boolean
  lastSecurityEffect: string | null
}
export interface CandidateCacheEntry {
  namespace: string
  key: string
  cursor: string | null
  status: string
  fetchStatus: string
  hasData: boolean
  dataUpdatedAt: number
}
declare global {
  interface Window {
    c010: {
      read(): ProbeState
      refetchIdentity(): void
      refetchCandidates(): void
      candidateCache(): CandidateCacheEntry[]
    }
    c010Transport: { events: { event: string; status?: number; aborted?: boolean }[] }
  }
}
function Probe() {
  const state = useShellState()
  const queryClient = useQueryClient()
  useEffect(() => {
    const key = contextKey(state)
    window.c010 = {
      read: () => ({
        phase: state.phase, actor: state.activeContext?.userId ?? null,
        company: state.activeContext?.companyId ?? null, epoch: state.sessionEpoch,
        revision: state.committedContextRevision, namespace: key ? contextKeyString(key) : null,
        sessionInvalid: state.sessionInvalid,
        hasLink: state.activeContext?.capabilities.includes('cari.accounts.link') ?? false,
        lastSecurityEffect: state.lastSecurityEffect?.kind ?? null,
      }),
      refetchIdentity: () => { void queryClient.invalidateQueries({ queryKey: ['shell', 'identity'] }) },
      refetchCandidates: () => {
        void queryClient.invalidateQueries({ predicate: (q) => q.queryKey[2] === 'link-candidates' })
      },
      // Read-only view of the candidate queries: key, cursor and state fields only; never the data.
      candidateCache: () => queryClient.getQueryCache().getAll()
        .filter((q) => q.queryKey[2] === 'link-candidates')
        .map((q) => {
          const params = q.queryKey[3]
          const cursor = typeof params === 'object' && params !== null && 'cursor' in params && typeof params.cursor === 'string' ? params.cursor : null
          return {
            namespace: String(q.queryKey[0]), key: JSON.stringify(q.queryKey), cursor,
            status: q.state.status, fetchStatus: q.state.fetchStatus, hasData: q.state.data !== undefined, dataUpdatedAt: q.state.dataUpdatedAt,
          }
        }),
    }
  }, [state, queryClient])
  return null
}
createRoot(document.getElementById('root')!).render(
  <StrictMode><ShellProvider queryClient={client}><Probe /><RouterProvider router={shellRouter} /></ShellProvider></StrictMode>,
)
