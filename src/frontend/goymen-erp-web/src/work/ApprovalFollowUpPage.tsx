import { useState } from 'react'
import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { fetchApprovalFollowUps } from './approvalFollowUpApi'
import type { ApprovalFollowUpView } from './approvalFollowUpContract'
import { APPROVAL_FOLLOW_UP_TEXT as T } from './approvalFollowUpText'
import styles from './Work.module.css'

/**
 * WP2 "Onay takibi" (YOL-D17, YOL-D28): escalated and orphaned approvals from Work's own follow-up rows, behind Work's
 * `work.approvals.follow-up` capability (`work.approvals.escalate`). It decides nothing: each item opens the owner's screen.
 */
export function ApprovalFollowUpPage() {
  const shell = useShellState()
  const context = contextKey(shell)
  return <ApprovalFollowUpBody key={`${context === null ? 'no-context' : contextKeyString(context)}:${shell.routeEntryGeneration}`} />
}

function ApprovalFollowUpBody() {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const [view, setView] = useState<ApprovalFollowUpView>('escalated')
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'work-approval-follow-ups', view],
    enabled: namespace !== null && companyId !== null && !capabilityFailClosed(shell),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchApprovalFollowUps(companyId!, view, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    },
  })
  if (capabilityFailClosed(shell) || companyId === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
  const page = query.data?.type === 'ok-single' ? query.data.data : null
  return <section aria-labelledby="work-approval-follow-up-heading">
    <h1 id="work-approval-follow-up-heading">{T.heading}</h1>
    <p>{T.scope}</p>
    <div className={styles.tabs} role="group" aria-label={T.heading}>
      <Button type="button" size="sm" variant={view === 'escalated' ? 'primary' : 'secondary'} aria-pressed={view === 'escalated'}
        onClick={() => setView('escalated')}>{T.viewEscalated}</Button>
      <Button type="button" size="sm" variant={view === 'orphaned' ? 'primary' : 'secondary'} aria-pressed={view === 'orphaned'}
        onClick={() => setView('orphaned')}>{T.viewOrphaned}</Button>
    </div>
    {page === null
      ? <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.subject} onRetry={() => void query.refetch()} />
      : page.items.length === 0
        ? <p>{view === 'escalated' ? T.emptyEscalated : T.emptyOrphaned}</p>
        : <ul className={styles.noteList}>{page.items.map((item) => <li key={`${item.kind}|${item.itemId}|${item.waitingSinceUtc}`}
            data-testid={`approval-follow-up-${item.itemId}`}>
            <p className={styles.noteMeta}><strong>{item.documentLabel}</strong>{' · '}{T.waitingSince}{' '}
              <time dateTime={item.waitingSinceUtc}>{new Date(item.waitingSinceUtc).toLocaleString('tr-TR')}</time>
              {item.dueAtUtc === null ? null : <>{' · '}{T.due}{' '}<time dateTime={item.dueAtUtc}>{new Date(item.dueAtUtc).toLocaleString('tr-TR')}</time></>}
              {' · '}{T.awaiting}{' '}<code>{item.approverPermission}</code></p>
            <Link to={item.link}>{T.open}</Link>
          </li>)}</ul>}
    {page?.hasMore === true ? <p role="status">{T.hasMore}</p> : null}
  </section>
}
