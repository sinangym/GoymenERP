import { useEffect, useRef, useState } from 'react'
import { changePlanPriority, fetchPlanPriority, fetchPlanQueue, type PlanPriority, type PlanQueue, type PriorityAttempt } from './planQueueApi'
import { newCommandId } from './commandId'
import { PLAN_QUEUE_TEXT as T } from './manufacturingText'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './OrderDetailPage.module.css'

const pending = new Map<string, PriorityAttempt>()
export function PlanQueuePanel({ companyId, planId, planVersion, namespace, canApprove, onChanged }: {
  readonly companyId: string; readonly planId: string; readonly planVersion: number; readonly namespace: string
  readonly canApprove: boolean; readonly onChanged?: () => void
}) {
  const boundary = useAuthBoundary(); const boundaryRef = useRef(boundary); boundaryRef.current = boundary
  const scope = `${namespace}:${companyId}:${planId}`
  const key = `${scope}:${planVersion}`
  const currentKey = useRef(key); currentKey.current = key
  const [read, setRead] = useState<{ key: string; outcome: ViewOutcome<{ priority: PlanPriority; queue: PlanQueue }> }>({ key, outcome: { type: 'loading' } })
  const [retry, setRetry] = useState(0)
  const [score, setScore] = useState(''); const [reason, setReason] = useState('')
  const [attempt, setAttempt] = useState<PriorityAttempt | null>(() => pending.get(scope) ?? null)
  const [sending, setSending] = useState(false); const inFlight = useRef(false)
  const [message, setMessage] = useState<string | null>(() => pending.has(scope) ? T.unknown : null)
  useEffect(() => {
    const controller = new AbortController(); const captured = boundaryRef.current.capture()
    setRead({ key, outcome: { type: 'loading' } })
    void Promise.all([fetchPlanPriority(companyId, planId, planVersion, controller.signal), fetchPlanQueue(companyId, planId, planVersion, controller.signal)])
      .then(([priority, queue]) => {
        if (controller.signal.aborted || currentKey.current !== key) return
        const failure = [priority, queue].find((outcome) => outcome.type !== 'ok-single')
        if (failure?.type === 'http' && (failure.status === 401 || failure.status === 403)) boundaryRef.current.report(failure.status, captured)
        if (priority.type === 'ok-single' && queue.type === 'ok-single') {
          const target = queue.data.items.find((entry) => entry.planId.toLowerCase() === planId.toLowerCase())
          setRead({ key, outcome: target?.priorityRevision === priority.data.priorityRevision && target.priorityScore === priority.data.priorityScore
            ? { type: 'ok-single', data: { priority: priority.data, queue: queue.data } } : { type: 'http', status: 409 } })
        } else setRead({ key, outcome: failure as ViewOutcome<{ priority: PlanPriority; queue: PlanQueue }> })
      })
    return () => controller.abort()
  }, [key, companyId, planId, planVersion, retry])
  const state = classifyOutcome(read.key === key ? read.outcome : { type: 'loading' }) as RemoteViewState<{ priority: PlanPriority; queue: PlanQueue }>
  const send = async (frozen: PriorityAttempt) => {
    if (!canApprove || inFlight.current) return
    inFlight.current = true; setSending(true); setAttempt(frozen); pending.set(scope, frozen)
    const captured = boundaryRef.current.capture(); const capturedKey = key
    const outcome = await changePlanPriority(companyId, planId, frozen)
    inFlight.current = false
    if (currentKey.current !== capturedKey) return
    setSending(false)
    if (outcome.type === 'unknown') { setMessage(T.unknown); return }
    pending.delete(scope); setAttempt(null)
    if (outcome.type === 'failed') {
      if (outcome.status === 401 || outcome.status === 403) boundaryRef.current.report(outcome.status, captured)
      setMessage(outcome.status === 409 ? T.conflict : T.refused); setRetry((n) => n + 1); return
    }
    setMessage(T.applied); setScore(''); setReason(''); setRetry((n) => n + 1); onChanged?.()
  }
  return <section aria-label={T.heading} className={styles.section}>
    <h3>{T.heading}</h3><p>{T.help}</p>
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => setRetry((n) => n + 1)} /> : <>
      <p>{T.currentPriority}: {state.data.priority.priorityScore} ({T.revision}: {state.data.priority.priorityRevision})</p>
      <div className={styles.tableWrap}><table className={styles.table}><caption>{T.caption}</caption>
        <thead><tr>{[T.plan, T.due, T.priority, T.planned, T.processed, T.remaining, T.status].map((text) => <th scope="col" key={text}>{text}</th>)}</tr></thead>
        <tbody>{state.data.queue.items.map((entry) => <tr key={entry.planId}>
          <th scope="row">{entry.planId}{entry.planId.toLowerCase() === planId.toLowerCase() ? ` — ${T.target}` : ''}</th>
          <td>{entry.dueDate ?? T.noDueDate}</td><td>{entry.priorityScore}</td><td>{entry.plannedKg}</td>
          <td>{entry.processedKg}</td><td>{entry.remainingKg}</td>
          <td>{entry.isQueued ? T.queued : T.preview}{entry.isOverPlanned ? ` — ${T.overPlanned}` : ''}</td>
        </tr>)}</tbody></table></div>
      <Button type="button" onClick={() => setRetry((n) => n + 1)}>{T.refresh}</Button>
      {canApprove ? <form className={styles.form} onSubmit={(event) => {
        event.preventDefault()
        if (attempt !== null || sending) return
        const value = Number(score)
        if (!/^-?\d+$/.test(score) || !Number.isInteger(value) || value < -2147483648 || value > 2147483647 || !reason.trim() || reason.trim().length > 4000) {
          setMessage(T.invalid); return
        }
        void send({ idempotencyKey: newCommandId(), expectedVersion: planVersion,
          expectedPriorityRevision: state.data.priority.priorityRevision, priorityScore: value, reason: reason.trim() })
      }}>
        <label className={styles.control}>{T.score}<input inputMode="numeric" value={score} disabled={sending || attempt !== null}
          onChange={(event) => setScore(event.target.value)} /></label>
        <label className={styles.control}>{T.reason}<textarea value={reason} maxLength={4000} disabled={sending || attempt !== null}
          onChange={(event) => setReason(event.target.value)} /></label>
        {attempt === null ? <Button type="submit" disabled={sending}>{T.save}</Button>
          : <Button type="button" disabled={sending} onClick={() => void send(attempt)}>{T.resend}</Button>}
      </form> : null}
    </>}
    {message !== null ? <p role="status">{message}</p> : null}
  </section>
}
