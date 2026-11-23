import { useEffect, useRef, useState, type FormEvent } from 'react'
import { StateBlock } from '../shell/StateBlock'
import type { RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import type { TransferDetail } from './transferContract'
import { cancelTransferDispatch } from './transferCancelApi'
import { isTransferCancelRequest, recallTransferCancel, reserveTransferCancel, settleTransferCancel,
  TRANSFER_CANCEL_REASON_LIMIT, type TransferCancelAttempt } from './transferCancelAttempt'
import { TRANSFER_TEXT as T } from './transferText'
import styles from './inventory.module.css'

interface Props {
  readonly transfer: TransferDetail; readonly binding: string | null; readonly canCancel: boolean; readonly onChanged: () => void
}
export function TransferCancelPanel(props: Props) {
  return <TransferCancelWorkspace key={JSON.stringify([props.binding, props.transfer.companyId, props.transfer.transferId, props.canCancel])} {...props} />
}
function TransferCancelWorkspace({ transfer, binding, canCancel, onChanged }: Props) {
  const boundary = useAuthBoundary()
  const [initial] = useState(() => binding === null ? null : recallTransferCancel(binding, transfer.companyId, transfer.transferId))
  const [pending, setPending] = useState<TransferCancelAttempt | null>(initial?.type === 'pending' ? initial.attempt : null)
  const [blocked, setBlocked] = useState(initial?.type === 'unavailable')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [state, setState] = useState<RemoteViewState<never> | null>(null)
  const [message, setMessage] = useState<string | null>(initial?.type === 'pending' ? T.cancelUnknown : null)
  const generation = useRef(0)
  const inFlight = useRef(false)
  useEffect(() => () => { generation.current++ }, [])
  if (!canCancel || pending === null && transfer.status !== 'InTransit') return null
  if (binding === null || blocked) return <section aria-label={T.cancelHeading}>
    <p role="alert">{T.cancelStorageUnavailable}</p>
    {binding === null ? null : <Button type="button" onClick={() => {
      const recalled = recallTransferCancel(binding, transfer.companyId, transfer.transferId)
      if (recalled.type === 'unavailable') return
      setBlocked(false); setPending(recalled.type === 'pending' ? recalled.attempt : null)
    }}>{T.cancelReadAgain}</Button>}
  </section>

  async function send(attempt: TransferCancelAttempt) {
    if (inFlight.current || attempt.binding !== binding || attempt.companyId !== transfer.companyId || attempt.transferId !== transfer.transferId) return
    if (!reserveTransferCancel(attempt)) { setPending(attempt); setMessage(T.cancelStorageUnavailable); return }
    inFlight.current = true
    const current = ++generation.current
    const snapshot = boundary.capture()
    setPending(attempt); setBusy(true); setState(null); setMessage(null)
    const outcome = await cancelTransferDispatch(attempt)
    const settled = outcome.type === 'applied' || outcome.type === 'failed' && ![401, 403].includes(outcome.status)
      ? settleTransferCancel(attempt) : false
    if (current !== generation.current) return
    inFlight.current = false; setBusy(false)
    if (outcome.type === 'unknown') { setState({ kind: outcome.reason }); setMessage(T.cancelUnknown); return }
    if (outcome.type === 'failed' && [401, 403].includes(outcome.status)) {
      boundary.report(outcome.status as 401 | 403, snapshot)
      setState({ kind: outcome.status === 401 ? 'unauthenticated' : 'forbidden' }); return
    }
    if (!settled) { setMessage(T.cancelStorageUnsettled); onChanged(); return }
    setPending(null); setReason(''); onChanged()
    if (outcome.type === 'applied') { setMessage(outcome.replayed ? T.cancelReplayed : T.cancelSaved); return }
    setState({ kind: outcome.status === 404 ? 'not-found' : 'conflict' })
    setMessage(T.cancelRefusal(outcome.code))
  }
  function submit(event: FormEvent) {
    event.preventDefault()
    if (pending !== null || busy || binding === null || transfer.status !== 'InTransit') return
    const payload = { expectedTransferVersion: transfer.version, cancelledAtUtc: new Date().toISOString(), reason: reason.trim() }
    if (!isTransferCancelRequest(payload)) { setMessage(T.cancelInvalid); return }
    void send({ binding, companyId: transfer.companyId, transferId: transfer.transferId, key: crypto.randomUUID(), payload })
  }
  return <section aria-label={T.cancelHeading}>
    <h3>{T.cancelHeading}</h3><p>{T.cancelNote}</p>
    <form className={styles.form} onSubmit={submit}><fieldset disabled={busy || pending !== null}>
      <legend>{T.cancelHeading}</legend><label>{T.cancelReason}<textarea value={reason} maxLength={TRANSFER_CANCEL_REASON_LIMIT}
        onChange={event => setReason(event.target.value)} required /></label>
      <Button type="submit" variant="danger">{T.cancelSubmit}</Button>
    </fieldset></form>
    {pending === null ? null : <Button type="button" disabled={busy} onClick={() => void send(pending)}>{busy ? T.cancelBusy : T.cancelRetry}</Button>}
    {state === null ? null : <StateBlock state={state} subject={T.cancelHeading} />}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
