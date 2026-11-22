import { useState } from 'react'
import { Link } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { commandScope, completeTask, fetchMyTasks, pendingKey, settleKey } from './collaborationApi'
import { MY_TASKS_ROUTE, type WorkTask } from './collaborationContract'
import { COLLABORATION_TEXT as T, RECORD_TYPE_TEXT } from './collaborationText'
import { refusalMessage } from './collaborationOutcome'
import styles from './Work.module.css'

export function MyTasksHomeEntry() {
  const shell = useShellState()
  if (capabilityFailClosed(shell) || shell.activeContext?.scopeMode !== 'single') return null
  return <section><h2>{T.tasksHeading}</h2><Link to={MY_TASKS_ROUTE}>{T.homeTasks}</Link></section>
}

/** W1-C "Bana atanan": the actor's own tasks only; the server accepts no assignee override. */
export function MyTasksPage() {
  const shell = useShellState()
  const context = contextKey(shell)
  return <MyTasksBody key={`${context === null ? 'no-context' : contextKeyString(context)}:${shell.routeEntryGeneration}`} />
}

const statusText = (task: WorkTask) => task.status === 'open' ? T.statusOpen : task.status === 'completed' ? T.statusCompleted : T.statusCancelled

function MyTasksBody() {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const [includeClosed, setIncludeClosed] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'work-my-tasks', includeClosed],
    enabled: namespace !== null && companyId !== null && !capabilityFailClosed(shell),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const answer = await fetchMyTasks(companyId!, includeClosed, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    },
  })
  if (capabilityFailClosed(shell) || companyId === null || actorId === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.tasksSubject} />
  const page = query.data?.type === 'ok-single' ? query.data.data : null
  // "Süresi geçti" is judged at the moment the list was read, never by a clock running during render.
  const now = query.dataUpdatedAt

  const complete = async (task: WorkTask) => {
    if (busy !== null) return
    const scope = commandScope('task-complete', shell.sessionEpoch, actorId, companyId, `${task.id}:${task.version}`)
    setBusy(task.id)
    setMessage(null)
    const outcome = await completeTask(companyId, pendingKey(scope), task.id, task.version)
    setBusy(null)
    if (outcome.type === 'unknown') { setMessage(T.unknownOutcome); return }
    settleKey(scope)
    setMessage(outcome.type === 'applied' ? T.taskCompleted : refusalMessage(outcome))
    void queryClient.invalidateQueries({ queryKey: [namespace, shell.routeEntryGeneration, 'work-my-tasks'] })
  }

  return <section className={styles.page} aria-labelledby="work-my-tasks-heading">
    <h1 id="work-my-tasks-heading">{T.tasksHeading}</h1>
    <label><input type="checkbox" checked={includeClosed} onChange={(event) => setIncludeClosed(event.target.checked)} />{T.includeClosed}</label>
    {page === null
      ? <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.tasksSubject} onRetry={() => void query.refetch()} />
      : page.items.length === 0 ? <StateBlock state={{ kind: includeClosed ? 'empty-no-data' : 'empty-filtered' }} subject={T.tasksSubject} />
        : <div className={styles.tableWrap} role="region" aria-label={T.tasksHeading} tabIndex={0}><table className={styles.table}><caption>{T.tasksHeading}</caption>
          <thead><tr><th scope="col">{T.taskTitle}</th><th scope="col">{T.taskDue}</th><th scope="col">{T.taskAssigner}</th><th scope="col">{T.taskRecord}</th><th scope="col">{T.taskStatus}</th><th scope="col">{T.complete}</th></tr></thead>
          <tbody>{page.items.map((task) => <tr key={task.id} data-testid={`work-task-${task.id}`}>
            <td>{task.title}{task.details === null ? null : <><br />{task.details}</>}</td>
            <td><time dateTime={task.dueAtUtc}>{new Date(task.dueAtUtc).toLocaleString('tr-TR')}</time>{task.status === 'open' && Date.parse(task.dueAtUtc) <= now ? <> · <strong>{T.overdue}</strong></> : null}</td>
            <td>{task.assignerDisplayName}</td>
            <td>{task.recordType === null ? T.taskNoRecord : RECORD_TYPE_TEXT[task.recordType]}</td>
            <td>{statusText(task)}</td>
            <td>{task.status === 'open' ? <Button type="button" size="sm" variant="primary" disabled={busy !== null} onClick={() => void complete(task)}>{T.complete}</Button> : null}</td>
          </tr>)}</tbody></table></div>}
    {page?.hasMore ? <p>{T.moreTasks}</p> : null}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
