import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { PLAN_EXCESS_CAPABILITY, approvePlanExcessPath, fetchPlanExcess } from './planExcessApi'
import { PlanExcessCommandNotice, usePlanExcessCommand } from './PlanExcessPreparationPanel'
import { PLAN_EXCESS_TEXT as T } from './planExcessText'
import { formatInstant } from './format'
import styles from './LogisticsPages.module.css'

/** Purpose-specific approval, independent of general instruction/load/read capabilities. */
export function PlanExcessApprovalPage() {
  const shell = useShellState(), key = contextKey(shell), context = shell.activeContext
  return <section className={styles.page}><h1>{T.title}</h1><p>{T.explanation}</p><p role="note">{T.warning}</p>
    {key === null || context?.scopeMode !== 'single' || context.companyId === null ? <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
      : !hasCapability(shell, PLAN_EXCESS_CAPABILITY.approve) ? <StateBlock subject={T.subject} state={{ kind: 'forbidden' }} />
        : <Approval key={`${contextKeyString(key)}|${shell.requestFenceGeneration}|${shell.routeEntryGeneration}`} company={context.companyId} namespace={contextKeyString(key)} />}
  </section>
}
function Approval({ company, namespace }: { readonly company: string; readonly namespace: string }) {
  const boundary = useAuthBoundary(), [cursor, setCursor] = useState<string | null>(null)
  const command = usePlanExcessCommand('plan-excess.approve', () => { void query.refetch() })
  const query = useQuery({ queryKey: ['logistics-plan-excess', namespace, 'approve', cursor], retry: false, queryFn: async ({ signal }) => {
    const capture = boundary.capture(), answer = await fetchPlanExcess(company, 'approve', undefined, cursor, signal)
    if (answer.type === 'http') boundary.report(answer.status, capture)
    return answer
  } })
  const page = query.data?.type === 'ok-single' ? query.data.data : null
  return <><PlanExcessCommandNotice command={command} /><Button type="button" disabled={command.frozen} onClick={() => void query.refetch()}>{T.refresh}</Button>
    {page === null || query.isFetching ? <StateBlock subject={T.subject} state={query.isFetching ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'loading' })} onRetry={() => void query.refetch()} />
      : page.items.length === 0 ? <StateBlock subject={T.subject} state={{ kind: cursor === null ? 'empty-no-data' : 'empty-filtered' }} />
        : <table><caption>{T.title}</caption><thead><tr><th scope="col">{T.instruction}</th><th scope="col">{T.line}</th><th scope="col">{T.instructed}</th><th scope="col">{T.quantity}</th><th scope="col">{T.reason}</th><th scope="col">{T.proposer}</th><th scope="col">{T.proposedAt}</th><th scope="col">{T.approve}</th></tr></thead>
          <tbody>{page.items.map(item => <tr key={item.exceptionId}><td>{item.instructionId}</td><td>{item.lineId}</td><td>{item.instructed}</td><td>{item.quantity}</td><td>{item.reason}</td><td>{item.proposedBy}</td><td>{formatInstant(item.proposedAtUtc)}</td>
            <td>{item.status === 'Pending' ? <Button type="button" disabled={command.frozen} onClick={() => command.submit(approvePlanExcessPath(item.exceptionId), { expectedVersion: item.version, expectedInstructionVersion: item.instructionVersion })}>{T.approve}</Button> : T.approved}</td></tr>)}</tbody></table>}
    {page?.nextCursor !== null && page?.nextCursor !== undefined ? <Button type="button" disabled={command.frozen || query.isFetching} onClick={() => setCursor(page.nextCursor)}>{T.next}</Button> : null}
    {cursor !== null ? <Button type="button" disabled={command.frozen || query.isFetching} onClick={() => setCursor(null)}>{T.previous}</Button> : null}
  </>
}
