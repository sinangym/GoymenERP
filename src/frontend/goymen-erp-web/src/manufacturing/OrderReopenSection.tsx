import { useEffect, useRef, useState } from 'react'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import type { ProductionOrderView } from './contract'
import { newCommandId } from './commandId'
import { clearReopenAttempt, persistReopenAttempt, readReopenAttempt, reopenOrder, type OrderReopenAttempt } from './orderReopenApi'
import { ORDER_REOPEN_TEXT as T } from './orderReopenText'
import styles from './OrderDetailPage.module.css'

export function OrderReopenSection({ order, companyId, namespace, allowed, blocked, onRefresh, onLocked }: {
  readonly order: ProductionOrderView; readonly companyId: string; readonly namespace: string; readonly allowed: boolean
  readonly blocked: boolean; readonly onRefresh: () => Promise<void>; readonly onLocked: (locked: boolean) => void
}) {
  const key = `goymen.manufacturing.reopen.v1:${namespace}:${companyId}:${order.orderId}`
  const [pending, setPending] = useState(() => readReopenAttempt(key))
  const [reason, setReason] = useState(() => pending !== null && pending !== 'blocked' ? pending.reason : '')
  const [message, setMessage] = useState<string | null>(() => pending !== null ? T.unknown : null)
  const [sending, setSending] = useState(false); const flight = useRef(false); const mounted = useRef(false)
  const boundary = useAuthBoundary(); const boundaryRef = useRef(boundary); boundaryRef.current = boundary
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => { onLocked(pending !== null || sending); return () => onLocked(false) }, [pending, sending, onLocked])
  const send = async (attempt: OrderReopenAttempt) => {
    if (!allowed || blocked || flight.current || pending === 'blocked') return
    if (!persistReopenAttempt(key, attempt)) { setPending('blocked'); return }
    setPending(attempt); setSending(true); flight.current = true; const captured = boundaryRef.current.capture()
    const result = await reopenOrder(companyId, order.orderId, attempt); flight.current = false
    if (!mounted.current) return
    setSending(false)
    if (result.type === 'unknown') { setMessage(T.unknown); return }
    if (!clearReopenAttempt(key)) { setPending('blocked'); return }
    setPending(null)
    if (result.type === 'applied') { setMessage(T.applied); setReason('') }
    else {
      if (result.status === 401 || result.status === 403) boundaryRef.current.report(result.status, captured)
      setMessage(result.code === 'manufacturing_order_reopen_period_not_open' ? T.period : T.refused)
    }
    await onRefresh()
  }
  if (!allowed && (order.reopenings?.length ?? 0) === 0) return null
  return <section className={styles.section} aria-label={T.heading}>
    {(order.reopenings?.length ?? 0) > 0 ? <><h3>{T.history}</h3><ol>{order.reopenings!.map(row => <li key={row.reopenedOrderVersion}>
      {T.closed}: {row.closedOrderVersion} · {row.closedAtUtc} · {row.closedByUserId}<br />
      {T.reopened}: {row.reopenedOrderVersion} · {row.reopenedAtUtc} · {row.reopenedByUserId}<p>{row.reason}</p>
    </li>)}</ol></> : null}
    {!allowed || order.status !== 'Closed' && pending === null ? null : <><h3>{T.heading}</h3><p>{T.help}</p>
      {pending === 'blocked' ? <p role="alert">{T.blocked}</p> : <form onSubmit={event => {
        event.preventDefault(); if (pending !== null || !reason.trim() || reason.trim().length > 256) { setMessage(T.invalid); return }
        void send({ idempotencyKey: newCommandId(), expectedVersion: order.version, reason: reason.trim() })
      }}>
        <label className={styles.control}>{T.reason}<textarea value={reason} maxLength={256} disabled={blocked || pending !== null || sending} onChange={event => setReason(event.target.value)} /></label>
        {pending === null ? <Button type="submit" disabled={blocked || sending}>{T.save}</Button>
          : <Button type="button" disabled={blocked || sending} onClick={() => void send(pending)}>{T.resend}</Button>}
      </form>}
      {message === null ? null : <p role="status">{message}</p>}
    </>}
  </section>
}
