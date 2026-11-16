import { useEffect, useRef, useState } from 'react'
import type { PlanQueue } from './planQueueApi'
import { clearQueueMove, moveQueuePlan, persistQueueMove, readQueueMove, type QueueMoveAttempt } from './manualQueueApi'
import { MANUAL_QUEUE_TEXT as T } from './manualQueueText'
import { newCommandId } from './commandId'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './OrderDetailPage.module.css'

export function ManualQueueControls({ companyId, planId, namespace, queue, allowed, blocked, onRefresh, onLocked }: {
  readonly companyId: string; readonly planId: string; readonly namespace: string; readonly queue: PlanQueue
  readonly allowed: boolean; readonly blocked: boolean; readonly onRefresh: () => void; readonly onLocked: (locked: boolean) => void
}) {
  const key = `goymen.manufacturing.queue.move.v1:${namespace}:${companyId}:${planId}`
  const boundary = useAuthBoundary(); const boundaryRef = useRef(boundary); boundaryRef.current = boundary
  const currentKey = useRef(key); currentKey.current = key
  const mounted = useRef(false)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const [saved, setSaved] = useState(() => readQueueMove(key))
  const [moving, setMoving] = useState(() => saved.kind === 'pending' ? saved.attempt.movingPlanId : '')
  const [before, setBefore] = useState(() => saved.kind === 'pending' ? saved.attempt.beforePlanId ?? '' : '')
  const [reason, setReason] = useState(() => saved.kind === 'pending' ? saved.attempt.reason : '')
  const [message, setMessage] = useState<string | null>(saved.kind === 'pending' ? T.unknown : null)
  const [sending, setSending] = useState(false); const inFlight = useRef(false)
  const locked = sending || saved.kind !== 'empty'
  useEffect(() => { onLocked(locked); return () => onLocked(false) }, [locked, onLocked])
  const send = async (attempt: QueueMoveAttempt) => {
    if (!allowed || blocked || inFlight.current || saved.kind === 'blocked') return
    if (!persistQueueMove(key, attempt)) { setSaved({ kind: 'blocked' }); return }
    setSaved({ kind: 'pending', attempt }); inFlight.current = true; setSending(true)
    const captured = boundaryRef.current.capture(); const capturedKey = key
    const result = await moveQueuePlan(companyId, planId, attempt)
    inFlight.current = false
    if (!mounted.current || currentKey.current !== capturedKey) return
    setSending(false)
    if (result.type === 'unknown') { setMessage(T.unknown); return }
    if (!clearQueueMove(key)) { setSaved({ kind: 'blocked' }); return }
    setSaved({ kind: 'empty' })
    if (result.type === 'failed') {
      if (result.status === 401 || result.status === 403) boundaryRef.current.report(result.status, captured)
      setMessage(result.status === 409 ? T.conflict : T.refused)
    } else { setMessage(T.applied); setReason('') }
    onRefresh()
  }
  if (!allowed) return null
  if (saved.kind === 'blocked') return <p role="alert">{T.blocked}</p>
  const members = queue.items.filter(item => item.isQueued)
  return <section aria-label={T.heading}>
    <h4>{T.heading}</h4><p>{T.help}</p>
    {members.length === 0 && saved.kind !== 'pending' ? <p>{T.empty}</p> : <form onSubmit={event => {
      event.preventDefault()
      if (blocked || locked || !moving || before === moving || !reason.trim() || reason.trim().length > 256) { setMessage(T.invalid); return }
      void send({ idempotencyKey: newCommandId(), workCenterId: queue.workCenterId, expectedVersion: queue.targetPlanVersion,
        expectedQueueRevision: queue.queueRevision, movingPlanId: moving, beforePlanId: before || null, reason: reason.trim() })
    }}>
      <label className={styles.control}>{T.moving}<select value={moving} disabled={blocked || locked} onChange={event => setMoving(event.target.value)}>
        <option value="">—</option>{saved.kind === 'pending' && !members.some(item => item.planId === moving)
          ? <option value={moving}>{moving}</option> : null}
        {members.map(item => <option key={item.planId} value={item.planId}>{item.planId}</option>)}
      </select></label>
      <label className={styles.control}>{T.before}<select value={before} disabled={blocked || locked} onChange={event => setBefore(event.target.value)}>
        <option value="">{T.end}</option>{saved.kind === 'pending' && before && !members.some(item => item.planId === before)
          ? <option value={before}>{before}</option> : null}
        {members.filter(item => item.planId !== moving).map(item => <option key={item.planId} value={item.planId}>{item.planId}</option>)}
      </select></label>
      <label className={styles.control}>{T.reason}<textarea value={reason} maxLength={256} disabled={blocked || locked} onChange={event => setReason(event.target.value)} /></label>
      {saved.kind === 'pending' ? <Button type="button" disabled={blocked || sending} onClick={() => void send(saved.attempt)}>{T.resend}</Button>
        : <Button type="submit" disabled={blocked || sending}>{T.save}</Button>}
    </form>}
    {message !== null ? <p role="status">{message}</p> : null}
  </section>
}
