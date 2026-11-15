/**
 * B-25 Work queue — `/onaylarim/kuyruk`: the pending approvals of every connected source the signed-in actor may see in
 * the committed company, in ONE list, oldest first (`GET /api/v1/work/queue`). Each source is its owner's own read
 * through the native approval queue adapter; the owner decides who sees a record, so a source that refuses the actor is
 * simply absent, and a source that cannot answer is named in a notice while the others are still shown.
 *
 * Read only. There is no approve button here: each row opens its owner's own screen, which reads the record afresh and
 * owns every decision (authority, separation of duties, versions). The route opens on the same owners' approve
 * capabilities as "Onaylarım"; the server re-authorizes every source regardless.
 */

import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchWorkQueue } from './workQueueApi'
import { QUEUE_SOURCE_READY, QUEUE_SOURCE_UNAVAILABLE, type WorkQueue, type WorkQueueSource } from './workQueueContract'
import {
  APPROVAL_KIND_TEXT,
  WORK_QUEUE_MODULE_TEXT,
  WORK_QUEUE_TEXT as T,
  WORK_ROUTE,
  formatInstant,
} from './workText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './Work.module.css'

const kindText = (kind: string): string => APPROVAL_KIND_TEXT[kind] ?? kind
const moduleText = (module: string): string => WORK_QUEUE_MODULE_TEXT[module] ?? module
const sourceLabels = (sources: readonly WorkQueueSource[]): string[] => sources.map((source) => kindText(source.kind))

export function WorkQueuePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'work-queue'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchWorkQueue(companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const queue = query.data?.type === 'ok-single' ? query.data.data : null
  return <section className={styles.page} aria-labelledby="work-queue-heading" data-testid="work-queue">
    <Link className={styles.backLink} to={WORK_ROUTE.approvals}>← {T.backToInbox}</Link>
    <header className={styles.header}>
      <h1 id="work-queue-heading">{T.heading}</h1>
      <div className={styles.scope}>
        <p>{T.scope}</p>
        <p>{T.nativeOnly}</p>
      </div>
    </header>
    {queue === null
      ? <StateBlock
        state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>}
        subject={T.subject}
        onRetry={() => void query.refetch()} />
      : <QueueBody queue={queue} />}
  </section>
}

function QueueBody({ queue }: { readonly queue: WorkQueue }) {
  const unavailable = queue.sources.filter((source) => source.state === QUEUE_SOURCE_UNAVAILABLE)
  const more = queue.sources.filter((source) => source.state === QUEUE_SOURCE_READY && source.hasMore)
  return <>
    {unavailable.length > 0
      ? <p role="alert" data-testid="work-queue-unavailable">{T.unavailable(sourceLabels(unavailable))}</p>
      : null}
    {more.length > 0
      ? <p role="note" className={styles.muted} data-testid="work-queue-has-more">{T.hasMore(sourceLabels(more))}</p>
      : null}
    {queue.items.length === 0
      ? unavailable.length === 0
        ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />
        : <p className={styles.muted} data-testid="work-queue-empty-readable">{T.emptyReadable}</p>
      : <div className={styles.tableWrap} tabIndex={0} role="region" aria-label={T.caption}>
        <table className={styles.table} data-testid="work-queue-table">
          <caption className={styles.caption}>{T.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{T.kind}</th>
              <th scope="col">{T.module}</th>
              <th scope="col">{T.createdAt}</th>
              <th scope="col"><span className={styles.muted}>{T.open}</span></th>
            </tr>
          </thead>
          <tbody>
            {queue.items.map((item) => <tr key={`${item.kind}::${item.itemId}`} data-kind={item.kind}>
              <td>{kindText(item.kind)}</td>
              <td>{moduleText(item.module)}</td>
              <td><time dateTime={item.createdAtUtc}>{formatInstant(item.createdAtUtc)}</time></td>
              <td className={styles.actions}>
                <Link to={item.link} aria-label={T.openLabel(kindText(item.kind))}>{T.open}</Link>
              </td>
            </tr>)}
          </tbody>
        </table>
      </div>}
  </>
}
