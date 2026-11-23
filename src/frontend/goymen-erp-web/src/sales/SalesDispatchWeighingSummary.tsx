import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDispatchWeighing } from './dispatchWeighing'
import { DISPATCH_WEIGHING_TEXT as T } from './dispatchWeighingText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'

/** Explicit expansion avoids one background owner request per list row. */
export function SalesDispatchWeighingSummary({ id, companyId }: { readonly id: string; readonly companyId: string }) {
  const [expanded, setExpanded] = useState(false)
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const sameCompany = shell.activeContext?.scopeMode === 'single' && shell.activeContext.companyId === companyId
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'sales-dispatch-weighing', id],
    enabled: expanded && namespace !== null && sameCompany, retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const value = await fetchDispatchWeighing(id, companyId, signal)
      if (value.type === 'http') boundary.report(value.status, snapshot)
      return value
    } })
  if (!sameCompany || namespace === null) return null
  const state = query.data === undefined ? { kind: 'initial-loading' as const } : classifyOutcome(query.data)
  const value = query.data?.type === 'ok-single' ? query.data.data : null
  return <section aria-label={T.heading}>
    <Button type="button" aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>{expanded ? T.hide : T.show}</Button>
    {!expanded ? null : value === null
      ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
      : value.state === 'NotRecorded' ? <p role="status">{T.missing}</p>
        : <><dl>
          <dt>{T.theoretical}</dt><dd>{value.theoreticalKg}</dd>
          <dt>{T.scale}</dt><dd>{value.scaleKg}</dd>
          <dt>{T.difference}</dt><dd>{value.differenceKg}</dd>
          <dt>{T.excess}</dt><dd>{value.totalExcessKg}</dd>
          <dt>{T.state}</dt><dd>{T.states[value.recordState!]}</dd>
          <dt>{T.decision}</dt><dd>{T.decisions[value.decision!]}</dd>
          <dt>{T.band}</dt><dd>{T.bands[value.bandLevel!]}</dd>
        </dl><p role="status">{value.invoiceWait === null ? T.proceed : T.waits[value.invoiceWait]}</p></>}
  </section>
}
