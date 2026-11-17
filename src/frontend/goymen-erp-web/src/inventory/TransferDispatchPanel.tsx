import { useLayoutEffect, useRef, useState, useSyncExternalStore, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import type { TransferDetail } from './transferContract'
import { beginTransferDispatchAttempt, fetchTransferOutgoing, finishTransferDispatchAttempt, leaveTransferDispatchAttempt,
  sendTransferDispatch, subscribeTransferDispatchAttempt, transferDispatchAttempt, transferDispatchBinding, transferDispatchFrozen,
  transferDispatchUtc, validDispatchTransfer, TRANSFER_DISPATCH_CAPABILITIES as CAP, type TransferDispatchCommand,
  type TransferDispatchSnapshot } from './transferDispatchApi'
import { TRANSFER_DISPATCH_TEXT as T } from './transferDispatchText'
import styles from './inventory.module.css'

interface Props { readonly transfer: TransferDetail; readonly onChanged: () => void }
export function TransferDispatchPanel(props: Props) {
  const shell = useShellState(); const key = contextKey(shell)
  if (key === null || shell.activeContext?.scopeMode !== 'single' || shell.activeContext.companyId?.toLowerCase() !== props.transfer.companyId.toLowerCase())
    return <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
  if (!hasCapability(shell, CAP.read)) return <section aria-label={T.heading}><h3>{T.heading}</h3><p>{T.noRead}</p><StateBlock subject={T.subject} state={{ kind: 'forbidden' }} /></section>
  if (!validDispatchTransfer(props.transfer)) return <StateBlock subject={T.subject} state={{ kind: 'malformed-response' }} />
  const namespace = contextKeyString(key)
  return <Workspace key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration, props.transfer.transferId])} {...props} shell={shell} namespace={namespace} />
}
function Workspace({ transfer, onChanged, shell, namespace }: Props & { readonly shell: ShellState; readonly namespace: string }) {
  const boundary = useAuthBoundary(); const binding = transferDispatchBinding(shell)
  const attempt = useSyncExternalStore((callback) => subscribeTransferDispatchAttempt(binding, callback),
    () => transferDispatchAttempt(binding), () => transferDispatchAttempt(null))
  const frozen = transferDispatchFrozen(attempt); const alive = useRef(false)
  useLayoutEffect(() => { alive.current = true; return () => { alive.current = false; leaveTransferDispatchAttempt(binding) } }, [binding])
  const [reason, setReason] = useState(''); const [occurred, setOccurred] = useState(''); const [error, setError] = useState<string | null>(null)
  const documentQuery = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-outgoing-document', transfer.transferId, transfer.version, transfer.lines.map((line) => line.lineId)], retry: false,
    queryFn: async ({ signal }) => {
      const security = boundary.capture(); const result = await fetchTransferOutgoing(transfer, signal)
      if (result.type === 'http') boundary.report(result.status, security)
      return result
    },
  })
  const view = documentQuery.data?.type === 'ok-single' ? documentQuery.data.data : null
  const absent = documentQuery.data?.type === 'http' && documentQuery.data.status === 404
  const sentDispatch = attempt.phase === 'completed' && attempt.snapshot.command.kind === 'dispatch'
    && attempt.snapshot.transfer.transferId.toLowerCase() === transfer.transferId.toLowerCase() && attempt.snapshot.transfer.version === transfer.version
  const ready = !frozen && !documentQuery.isFetching && !sentDispatch
  const planned = transfer.status === 'Planned' && transfer.dispatchedAtUtc === null
  const notLinked = view?.association === 1
  const document = view?.document ?? null
  const can = (kind: TransferDispatchCommand['kind']) => hasCapability(shell, CAP[kind])
  async function execute(snapshot: TransferDispatchSnapshot) {
    const security = boundary.capture(); const outcome = await sendTransferDispatch(snapshot)
    if (outcome.type === 'refused') boundary.report(outcome.status, security)
    finishTransferDispatchAttempt(snapshot, outcome)
    if (!alive.current || outcome.type !== 'applied') return
    setError(null); setReason(''); setOccurred(''); void documentQuery.refetch(); onChanged()
  }
  function start(command: TransferDispatchCommand) {
    if (!ready || !can(command.kind) || shell.activeContext === null) return
    const snapshot = beginTransferDispatchAttempt(binding, shell.activeContext.userId, transfer, command)
    if (snapshot === null) { setError(T.invalid); return }
    void execute(snapshot)
  }
  function retry(snapshot: TransferDispatchSnapshot) {
    if (snapshot.transfer.transferId.toLowerCase() !== transfer.transferId.toLowerCase() || !can(snapshot.command.kind) || shell.activeContext?.userId !== snapshot.actorId) return
    const retrySnapshot = beginTransferDispatchAttempt(binding, snapshot.actorId, snapshot.transfer, snapshot.command)
    if (retrySnapshot !== null) void execute(retrySnapshot)
  }
  function dispatch(event: FormEvent) {
    event.preventDefault(); if (document === null || !ready || !planned || !notLinked || document.status !== 'Effective') return
    const date = occurred === '' ? null : new Date(occurred)
    const at = date !== null && Number.isFinite(date.getTime()) ? date.toISOString() : null
    if (!transferDispatchUtc(at)) { setError(T.invalid); return }
    start({ kind: 'dispatch', document, payload: { expectedTransferVersion: transfer.version, dispatchedAtUtc: at,
      outgoingDocumentId: document.documentId, expectedOutgoingDocumentVersion: String(document.version) } })
  }
  function cancel(event: FormEvent) {
    event.preventDefault(); if (document === null || !ready || document.status === 'Cancelled') return
    const trimmed = reason.trim()
    if (trimmed === '' || trimmed.length > 500 || /[\u0000-\u001f\u007f-\u009f]/.test(trimmed)) { setError(T.invalid); return }
    start({ kind: 'cancel', document, payload: { expectedVersion: document.version, reason: trimmed } })
  }
  const sameTarget = attempt.phase === 'idle' || attempt.snapshot.transfer.transferId.toLowerCase() === transfer.transferId.toLowerCase()
  return <section aria-label={T.heading} className={styles.lineCard}>
    <h3>{T.heading}</h3><p>{T.intro}</p>
    {sameTarget && attempt.phase === 'sending' ? <p role="status">{T.pending}</p> : null}
    {sameTarget && attempt.phase === 'completed' ? <p role="status">{attempt.replayed ? T.replayed : T.saved}</p> : null}
    {!sameTarget && frozen ? <p role="alert">{T.otherPending}</p> : null}
    {sameTarget && (attempt.phase === 'unknown' || attempt.phase === 'failed' && attempt.unresolved) ? <div className={styles.warningCard}>
      <p role="alert">{T.unknown}</p>
      <StateBlock subject={T.subject} state={attempt.phase === 'unknown' ? { kind: attempt.reason } : classifyOutcome({ type: 'http', status: attempt.status })} />
      <Button type="button" disabled={!can(attempt.snapshot.command.kind)} onClick={() => retry(attempt.snapshot)}>{T.retry}</Button>
    </div> : null}
    {sameTarget && attempt.phase === 'failed' && !attempt.unresolved ? attempt.status === 400 || attempt.status === 415 || attempt.status === 422
      ? <p role="alert">{T.refused}</p> : <StateBlock subject={T.subject} state={classifyOutcome({ type: 'http', status: attempt.status })} /> : null}
    {error === null ? null : <p role="alert">{error}</p>}
    <Button type="button" disabled={frozen || documentQuery.isFetching} onClick={() => { setError(null); void documentQuery.refetch() }}>{T.refresh}</Button>
    {documentQuery.isFetching ? <StateBlock subject={T.subject} state={{ kind: 'initial-loading' }} />
      : absent ? <><StateBlock subject={T.subject} state={{ kind: 'not-found' }} /><p>{T.noDocument}</p>
        {planned && can('create') ? <div><p>{T.createNote}</p><Button type="button" disabled={!ready} onClick={() => start({ kind: 'create', payload: {
          documentId: crypto.randomUUID(), transferId: transfer.transferId, orderedTransferLineIds: transfer.lines.map((line) => line.lineId),
          eDispatchNumber: null, eDispatchAssociation: 1,
        } })}>{T.create}</Button></div> : null}
      </> : view === null || document === null ? <StateBlock subject={T.subject} state={classifyOutcome(documentQuery.data ?? { type: 'malformed' })} onRetry={() => void documentQuery.refetch()} /> : <>
        <dl className={styles.facts}><div><dt>{T.document}</dt><dd>{document.documentId}</dd></div>
          <div><dt>{T.version}</dt><dd>{document.version} — {T.status[document.status]}</dd></div>
          <div><dt>{T.association}</dt><dd>{view.association === 1 ? T.notLinked : '—'}</dd></div></dl>
        {view.association === 0 ? <p role="alert">{T.legacy}</p> : view.association === 2 ? <p role="alert">{T.linked}</p> : null}
        {document.status === 'Cancelled' ? <p role="status">{T.cancelled}</p> : null}
        {planned && notLinked && document.status === 'Draft' && can('finalize') ? <div><p>{T.finalizeNote}</p>
          <Button type="button" disabled={!ready} onClick={() => start({ kind: 'finalize', document, payload: { expectedVersion: document.version } })}>{T.finalize}</Button></div> : null}
        {planned && notLinked && document.status === 'Effective' && can('dispatch') ? <form className={styles.form} onSubmit={dispatch}>
          <fieldset disabled={!ready}><legend>{T.dispatch}</legend><Field label={T.dispatchedAt}>{(control) => <input {...control} type="datetime-local" step="1"
            value={occurred} onChange={(event) => setOccurred(event.currentTarget.value)} />}</Field>
            <Button type="submit" variant="primary">{T.dispatch}</Button></fieldset></form> : null}
        {document.status !== 'Cancelled' && can('cancel') ? <form className={styles.form} onSubmit={cancel}>
          <fieldset disabled={!ready}><legend>{T.cancel}</legend><p>{T.cancelNote}</p><Field label={T.cancelReason}>{(control) => <textarea {...control} maxLength={500}
            value={reason} onChange={(event) => setReason(event.currentTarget.value)} />}</Field><Button type="submit" variant="danger">{T.cancel}</Button></fieldset></form> : null}
      </>}
    {!planned ? <p>{T.noPlan}</p> : null}
  </section>
}
