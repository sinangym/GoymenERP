import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { fetchMyInbox, markInboxRead } from './collaborationApi'
import type { WorkInboxClass, WorkInboxItem } from './collaborationContract'
import { COLLABORATION_TEXT as T, RECORD_TYPE_TEXT } from './collaborationText'
import styles from './Work.module.css'

const kindText = (item: WorkInboxItem) => item.kind === 'mention' ? T.kindMention : item.kind === 'task-assigned' ? T.kindAssigned : T.kindOverdue

/**
 * AI-D29 in-app inbox: "şimdi" (mentions, overdue work) and "özet" (the rest, released at the digest times the server
 * reads from Settings). The class comes from the server; read state is the server's, shown as text, not colour only.
 */
export function CollaborationInbox() {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const [notificationClass, setNotificationClass] = useState<WorkInboxClass>('now')
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'work-inbox', notificationClass, unreadOnly],
    enabled: namespace !== null && companyId !== null && !capabilityFailClosed(shell),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchMyInbox(companyId!, notificationClass, unreadOnly, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    },
  })
  if (companyId === null || capabilityFailClosed(shell)) return null
  const page = query.data?.type === 'ok-single' ? query.data.data : null

  const markRead = async (item: WorkInboxItem) => {
    if (busy !== null) return
    setBusy(item.id)
    setMessage(null)
    const outcome = await markInboxRead(companyId, item.id)
    setBusy(null)
    if (outcome.type !== 'applied') { setMessage(T.readFailed); return }
    void queryClient.invalidateQueries({ queryKey: [namespace, shell.routeEntryGeneration, 'work-inbox'] })
  }

  return <section className={styles.section} aria-labelledby="work-collaboration-inbox-heading" data-testid="work-collaboration-inbox">
    <h2 id="work-collaboration-inbox-heading">{T.inboxHeading}</h2>
    <p>{T.inboxScope}</p>
    <div className={styles.tabs} role="group" aria-label={T.inboxHeading}>
      <Button type="button" size="sm" variant={notificationClass === 'now' ? 'primary' : 'secondary'} aria-pressed={notificationClass === 'now'}
        onClick={() => setNotificationClass('now')}>{T.classNow}{page === null ? null : ` (${page.unreadNowCount} ${T.unreadCount})`}</Button>
      <Button type="button" size="sm" variant={notificationClass === 'digest' ? 'primary' : 'secondary'} aria-pressed={notificationClass === 'digest'}
        onClick={() => setNotificationClass('digest')}>{T.classDigest}{page === null ? null : ` (${page.unreadDigestCount} ${T.unreadCount})`}</Button>
    </div>
    <label><input type="checkbox" checked={unreadOnly} onChange={(event) => setUnreadOnly(event.target.checked)} />{T.unreadOnly}</label>
    {page === null
      ? <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.inboxSubject} onRetry={() => void query.refetch()} />
      : page.items.length === 0
        ? (unreadOnly ? <StateBlock state={{ kind: 'empty-filtered' }} subject={T.inboxSubject} /> : <p>{notificationClass === 'now' ? T.inboxEmptyNow : T.inboxEmptyDigest}</p>)
        : <ul className={styles.noteList}>{page.items.map((item) => <li key={item.id} data-testid={`work-inbox-${item.id}`}>
          <p className={styles.noteMeta}><strong>{kindText(item)}</strong>{item.recordType === null ? null : <> · {RECORD_TYPE_TEXT[item.recordType]}</>}
            {' · '}<time dateTime={item.visibleAtUtc}>{new Date(item.visibleAtUtc).toLocaleString('tr-TR')}</time>{' · '}{item.readAtUtc === null ? T.unread : T.read}</p>
          {item.readAtUtc === null ? <Button type="button" size="sm" disabled={busy !== null} onClick={() => void markRead(item)}>{T.markRead}</Button> : null}
        </li>)}</ul>}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
