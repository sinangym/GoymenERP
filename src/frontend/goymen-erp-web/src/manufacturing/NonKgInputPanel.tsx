import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { newCommandId } from './commandId'
import { clearNonKgAttempt, decodeNonKgAttempt, fetchNonKgInputs, persistNonKgAttempt, readNonKgAttempt, recordNonKgInput, type NonKgAttempt, type NonKgFact, type NonKgHistory } from './nonKgInputApi'
import { NON_KG_INPUT_TEXT as T } from './nonKgInputText'
import styles from './OrderDetailPage.module.css'

export function NonKgInputPanel({ namespace, routeEntryGeneration, companyId, orderId, orderVersion, canRecord, onRefresh }: {
  readonly namespace: string; readonly routeEntryGeneration: number; readonly companyId: string; readonly orderId: string
  readonly orderVersion: number; readonly canRecord: boolean; readonly onRefresh: () => Promise<void>
}) {
  const storage = `goymen.manufacturing.non-kg.v1:${namespace}:${companyId}:${orderId}`; const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState(() => readNonKgAttempt(storage, companyId, orderId)); const [sending, setSending] = useState(false)
  const [kind, setKind] = useState<'Paint' | 'Other'>('Paint'); const [unit, setUnit] = useState<'Litre' | 'Metre' | 'Piece'>('Litre')
  const [item, setItem] = useState(''); const [quantity, setQuantity] = useState(''); const [occurred, setOccurred] = useState('')
  const [reason, setReason] = useState(''); const [replaces, setReplaces] = useState<string | null>(null); const [message, setMessage] = useState<string | null>(null)
  const flight = useRef(false); const mounted = useRef(false); useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, routeEntryGeneration, 'manufacturing-non-kg', orderId, orderVersion], queryFn: async ({ signal }) => {
    const snapshot = boundary.capture(); const result = await fetchNonKgInputs(companyId, orderId, signal)
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
    return result
  } })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<NonKgHistory>
  const send = async (value: NonKgAttempt) => {
    if (!canRecord || flight.current || attempt === 'blocked') return
    if (!persistNonKgAttempt(storage, value)) { setAttempt('blocked'); return }
    flight.current = true; setAttempt(value); setSending(true); const snapshot = boundary.capture(); const result = await recordNonKgInput(value)
    flight.current = false; if (!mounted.current) return; setSending(false)
    if (result === 'unknown') { setMessage(T.unknown); return }
    if (!clearNonKgAttempt(storage)) { setAttempt('blocked'); return }
    setAttempt(null); setMessage(result === 'applied' ? T.applied : T.refused)
    if (typeof result === 'object' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
    if (result === 'applied') { setItem(''); setQuantity(''); setReplaces(null); setReason('') }
    await query.refetch(); await onRefresh()
  }
  const correct = (fact: NonKgFact) => {
    setReplaces(fact.factId); setKind(fact.kind === 1 ? 'Paint' : 'Other'); setUnit(fact.unit === 1 ? 'Litre' : fact.unit === 2 ? 'Metre' : 'Piece')
    setItem(fact.item); setQuantity(fact.quantity); setOccurred(fact.occurredAtUtc); setReason('')
  }
  return <section className={styles.section} aria-label={T.heading}><h2>{T.heading}</h2><p>{T.help}</p><p>{T.history}</p>
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} /> : <>
      {state.data.facts.length === 0 ? <p>{T.empty}</p> : <div className={styles.tableWrap}><table className={styles.table}><caption>{T.subject}</caption>
        <thead><tr>{[T.revision, T.item, T.quantity, T.unit, T.occurred, T.replacement, T.reason].map(label => <th key={label} scope="col">{label}</th>)}<th scope="col">{T.correction}</th></tr></thead>
        <tbody>{state.data.facts.map(fact => <tr key={fact.factId}><td>{fact.revision}</td><th scope="row">{fact.item}</th><td>{fact.quantity}</td><td>{fact.unit === 1 ? T.litre : fact.unit === 2 ? T.metre : T.piece}</td><td>{fact.occurredAtUtc}</td><td>{fact.replacesFactId ?? '—'}</td><td>{fact.reason}</td>
          <td>{canRecord && attempt === null && !state.data.facts.some(row => row.replacesFactId === fact.factId) ? <Button type="button" onClick={() => correct(fact)}>{T.correct}</Button> : null}</td></tr>)}</tbody></table></div>}
      {canRecord ? <form onSubmit={event => {
        event.preventDefault(); if (attempt !== null || flight.current) return
        const candidate = decodeNonKgAttempt({ companyId, orderId, key: newCommandId(), body: { factId: newCommandId(), expectedVersion: state.data.orderVersion,
          expectedRevision: state.data.revision, kind, item: item.trim(), unit, quantity, occurredAtUtc: occurred, replacesFactId: replaces, reason: reason.trim() } }, companyId, orderId)
        if (!candidate) { setMessage(T.invalid); return }; void send(candidate)
      }}><fieldset disabled={attempt !== null || sending}><legend>{replaces ? T.correction : T.newEntry}</legend>
        <label>{T.kind}<select value={kind} disabled={replaces !== null} onChange={event => { setKind(event.target.value as 'Paint' | 'Other'); setUnit('Litre') }}><option value="Paint">{T.paint}</option><option value="Other">{T.other}</option></select></label>
        <label>{T.item}<input value={item} maxLength={200} onChange={event => setItem(event.target.value)} /></label>
        <label>{T.unit}<select value={unit} disabled={kind === 'Paint' || replaces !== null} onChange={event => setUnit(event.target.value as 'Litre' | 'Metre' | 'Piece')}><option value="Litre">{T.litre}</option><option value="Metre">{T.metre}</option><option value="Piece">{T.piece}</option></select></label>
        <label>{T.quantity}<input inputMode="decimal" value={quantity} onChange={event => setQuantity(event.target.value)} /></label>
        <label>{T.occurred}<input placeholder="2026-11-17T09:00:00Z" value={occurred} onChange={event => setOccurred(event.target.value)} /></label>
        <label>{T.reason}<textarea value={reason} maxLength={1000} onChange={event => setReason(event.target.value)} /></label>
        <Button type="submit">{T.submit}</Button></fieldset>
      </form> : null}</>}
    {attempt === 'blocked' ? <p role="alert">{T.blocked}</p> : attempt !== null ? <><p role="status">{T.unknown}</p><p>{attempt.body.item} · {attempt.body.quantity} · {attempt.body.occurredAtUtc}</p>
      {canRecord ? <Button type="button" disabled={sending} onClick={() => void send(attempt)}>{T.resend}</Button> : null}</> : null}
    {message ? <p role="status">{message}</p> : null}
  </section>
}
