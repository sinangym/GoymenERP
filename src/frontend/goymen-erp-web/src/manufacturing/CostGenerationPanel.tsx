import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { clearCostAttempt, costQuantityText, fetchCostGenerations, persistCostAttempt, readCostAttempt, recognizeCost, type CostGeneration, type CostGenerationHistory, type CostRecognitionAttempt } from './costGenerationApi'
import { COST_GENERATION_TEXT as T } from './costGenerationText'
import { newCommandId } from './commandId'
import styles from './OrderDetailPage.module.css'

export function CostGenerationPanel({ namespace, routeEntryGeneration, companyId, orderId, orderVersion, closed, canRecognize, onRefresh }: {
  readonly namespace: string; readonly companyId: string; readonly orderId: string; readonly orderVersion: number
  readonly routeEntryGeneration: number
  readonly closed: boolean; readonly canRecognize: boolean; readonly onRefresh: () => Promise<void>
}) {
  const storageKey = `goymen.manufacturing.cost-recognition.v1:${namespace}:${companyId}:${orderId}`
  const boundary = useAuthBoundary(); const [attempt, setAttempt] = useState(() => readCostAttempt(storageKey))
  const [reason, setReason] = useState(() => attempt !== null && attempt !== 'blocked' ? attempt.reason : '')
  const [message, setMessage] = useState<string | null>(null); const [sending, setSending] = useState(false); const flight = useRef(false)
  const mounted = useRef(false); useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, routeEntryGeneration, 'manufacturing-cost-generations', orderId, orderVersion], queryFn: async ({ signal }) => {
    const capture = boundary.capture(); const result = await fetchCostGenerations(companyId, orderId, signal)
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, capture)
    return result
  } })
  const state = (query.isPending ? { kind: 'initial-loading' as const } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<CostGenerationHistory>
  const send = async (value: CostRecognitionAttempt) => {
    if (!canRecognize || flight.current || attempt === 'blocked') return
    if (!persistCostAttempt(storageKey, value)) { setAttempt('blocked'); setMessage(T.unknown); return }
    flight.current = true; setSending(true); setAttempt(value); const capture = boundary.capture()
    const result = await recognizeCost(companyId, orderId, value); flight.current = false
    if (!mounted.current) return
    setSending(false)
    if (result === 'unknown') { setMessage(T.unknown); return }
    if (!clearCostAttempt(storageKey)) { setAttempt('blocked'); setMessage(T.unknown); return }
    setAttempt(null); setMessage(result === 'applied' ? T.applied : T.refused)
    if (typeof result === 'object' && (result.status === 401 || result.status === 403)) boundary.report(result.status, capture)
    await query.refetch(); await onRefresh()
  }
  return <section className={styles.section} aria-label={T.heading}><h2>{T.heading}</h2><p>{T.help}</p>
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
      : <>{state.data.generations.length === 0 ? <p>{T.empty}</p> : <ol>{state.data.generations.map(row => <li key={`${row.generationId}:${row.revision}`}><Generation row={row} /></li>)}</ol>}
        {(closed || attempt !== null) && canRecognize ? <form onSubmit={event => { event.preventDefault(); if (attempt === null && reason.trim()) void send({ idempotencyKey: newCommandId(), expectedVersion: orderVersion,
          expectedGenerationRevision: state.data.generations.filter(row => row.closedOrderVersion === orderVersion).at(-1)?.revision ?? 0, reason: reason.trim() }) }}>
          <label>{T.reason}<textarea value={reason} maxLength={256} disabled={sending || attempt !== null} onChange={event => setReason(event.target.value)} /></label>
          {attempt === 'blocked' ? <p role="alert">{T.unknown}</p> : attempt === null ? <Button type="submit" disabled={sending || !reason.trim()}>{T.recognize}</Button>
            : <Button type="button" disabled={sending} onClick={() => void send(attempt)}>{T.resend}</Button>}
        </form> : null}</>}
    {message === null ? null : <p role="status">{message}</p>}<p>{T.accounting}</p>
  </section>
}
function Generation({ row }: { readonly row: CostGeneration }) {
  const states = { Pending: T.pending, Unreadable: T.unreadable, Recognized: T.recognized, Reversed: T.reversed }
  return <><p>{T.close}: {row.closedOrderVersion} · {T.revision}: {row.revision} · {states[row.state]}</p><p>{row.recordedAtUtc} · {row.reason}</p>
    <div className={styles.tableWrap}><table className={styles.table}><caption>{T.outputs}</caption><thead><tr>{[T.label, T.net, T.remaining, T.consumed, T.originalTl, T.remainingTl, T.consumedTl, T.usd].map(label => <th key={label} scope="col">{label}</th>)}</tr></thead>
      <tbody>{row.outputs.map(output => <tr key={output.outputId}><th scope="row">{output.labelId}</th><td>{output.netKg}</td><td>{costQuantityText(output.remainingKg, output.remainingKgRatio)}</td><td>{costQuantityText(output.consumedKg, output.consumedKgRatio)}</td>
        <td>{output.originalTlAmount ?? T.missing}</td><td>{output.remainingTlValue ?? T.missing}</td><td>{output.consumedTlDifference ?? T.missing}</td><td>{output.usdUnitCostPerKg ?? T.missing}</td></tr>)}</tbody></table></div></>
}
