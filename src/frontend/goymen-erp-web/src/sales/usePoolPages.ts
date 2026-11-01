/**
 * The S-01 lists' paging, the order draft list's rules (S-02): the first page under the committed context and the route
 * entry, a state filter that is the SERVER's (changing it restarts the list), `nextCursor = null` is the end, and a
 * failed next page keeps the rows already shown and offers an explicit retry of the SAME cursor — never an automatic
 * resend. Rows are never sorted, filtered or counted on the client (LF-01).
 */

import { useCallback, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'

export interface PoolPageData<T> {
  readonly items: readonly T[]
  readonly nextCursor: string | null
}

export type PoolPageFetch<T, S> = (
  cursor: string | null,
  companyId: string,
  signal: AbortSignal | undefined,
  filter: S | null,
) => Promise<ViewOutcome<PoolPageData<T>>>

export function usePoolPages<P extends PoolPageData<{ readonly id: string }>, S extends string>(
  namespace: string | null,
  name: string,
  fetchPage: (cursor: string | null, companyId: string, signal: AbortSignal | undefined, filter: S | null) => Promise<ViewOutcome<P>>,
  filter: S | null,
) {
  type T = P['items'][number]
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null

  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, name, 'first', filter ?? 'all'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPage(null, companyId as string, signal, filter)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  // The continuation belongs to one filter: a page read for another one is never appended.
  const [more, setMore] = useState<{ readonly filter: S | null; readonly rows: readonly T[]; readonly cursor: string | null | undefined }>({
    filter,
    rows: [],
    cursor: undefined,
  })
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loadingRef = useRef(false)

  const current = more.filter === filter ? more : { filter, rows: [] as readonly T[], cursor: undefined }
  const firstPage = first.data?.type === 'ok-single' ? first.data.data : null
  const firstItems: readonly T[] = useMemo(() => firstPage?.items ?? [], [firstPage])
  const nextCursor = current.cursor === undefined ? (firstPage?.nextCursor ?? null) : current.cursor

  const loadMore = useCallback(async () => {
    if (loadingRef.current || nextCursor === null || companyId === null) return
    loadingRef.current = true
    setContinuation('loading')
    const snapshot = boundary.capture()
    const outcome = await fetchPage(nextCursor, companyId, undefined, filter)
    loadingRef.current = false
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    if (outcome.type === 'ok-single') {
      // A page that repeats a row already shown is not appended twice.
      const seen = new Set([...firstItems, ...current.rows].map((row) => row.id))
      setMore({ filter, rows: [...current.rows, ...outcome.data.items.filter((row) => !seen.has(row.id))], cursor: outcome.data.nextCursor })
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, current.rows, fetchPage, filter, firstItems, nextCursor])

  const firstState: RemoteViewState<unknown> | null =
    firstPage !== null ? null : first.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(first.data) as RemoteViewState<unknown>)

  return {
    rows: firstPage === null ? null : [...firstItems, ...current.rows],
    /** The first page as decoded: a screen may show page-level facts of it (a report's disclosure), never re-derive rows. */
    firstPage,
    firstState,
    retryFirst: () => void first.refetch(),
    nextCursor,
    continuation,
    loadMore,
    resetContinuation: () => setContinuation('idle'),
  }
}
