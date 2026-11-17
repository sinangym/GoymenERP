import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import type { CreditAccount } from './creditAccountApi'
import { fetchCreditLimits, sendCreditLimit, type CreditLimitWorkspace, type LimitAttempt, type LimitOutcome } from './creditLimitApi'
import { CREDIT_LIMIT_TEXT as T } from './bankCreditLimitText'
import styles from './Finance.module.css'

const pending = new Map<string, LimitAttempt>()
export function CreditLimitsPanel({ account, namespace, refreshVersion }: { readonly account: CreditAccount; readonly namespace: string; readonly refreshVersion: number }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const slot = `${namespace}:credit-limit:${account.id}`
  const [attempt, setAttempt] = useState<LimitAttempt | null>(() => pending.get(slot) ?? null)
  const [outcome, setOutcome] = useState<LimitOutcome | null>(() => pending.has(slot) ? { type: 'unknown' } : null)
  const [sending, setSending] = useState(false); const busy = useRef(false); const mounted = useRef(true)
  const [amount, setAmount] = useState(''); const [reference, setReference] = useState(''); const [revision, setRevision] = useState('')
  const [date, setDate] = useState(''); const [reason, setReason] = useState(''); const [rejectReason, setRejectReason] = useState('')
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'credit-bank-limits', account.id, refreshVersion], queryFn: async ({ signal }) => {
    const stamp = boundary.capture(); const result = await fetchCreditLimits(account, signal)
    if (result.type === 'http' && [401, 403].includes(result.status)) boundary.report(result.status as 401 | 403, stamp)
    return result
  } })
  const frozen = sending || outcome?.type === 'unknown'
  const send = async (next: LimitAttempt, retry = false) => {
    if (busy.current || frozen && !retry) return
    busy.current = true; setSending(true); setAttempt(next); setOutcome(null); pending.set(slot, next)
    try { const stamp = boundary.capture(); const result = await sendCreditLimit(account, next)
      if (result.type === 'refused' && [401, 403].includes(result.status)) boundary.report(result.status as 401 | 403, stamp)
      const unresolved = retry && result.type === 'refused' && [400, 401, 403, 404, 415].includes(result.status)
      if (result.type !== 'unknown' && !unresolved) pending.delete(slot)
      if (mounted.current) { setOutcome(unresolved ? { type: 'unknown' } : result); void query.refetch() }
    } finally { busy.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<CreditLimitWorkspace>
  const current = state.kind === 'ready' ? state.data.items.find(x => x.status === 'Approved' && x.supersededById === null) : undefined
  const hasPending = state.kind === 'ready' && state.data.items.some(x => x.status === 'Proposed')
  return <section aria-labelledby={`credit-limit-${account.id}`}><h4 id={`credit-limit-${account.id}`}>{T.title}</h4><p>{T.intro}</p>
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.title} onRetry={() => void query.refetch()} /> : <>
      <p>{T.recorded}: {state.data.assessment.recordedPrincipalNet}</p>
      {current ? <p>{T.amount}: {current.source.amount}</p> : <p>{T.noLimit}</p>}
      {state.data.assessment.exceedsLimit === true ? <p role="alert">{T.exceeded}</p> : null}
      {state.data.assessment.proposedMovements.map(row => <p key={row.movementId}>{row.sourceReference} · {T.projected}: {row.projectedRecordedPrincipalNet}{row.exceedsLimit === true ? ` · ${T.exceeded}` : ''}</p>)}
      {state.data.items.length === 0 ? <p>{T.empty}</p> : state.data.items.map(row => <article key={row.id}><p>{T.statuses[row.status]} · {row.source.amount} · {row.source.sourceDocument.reference} · {row.source.sourceDocument.revision}</p>
        {row.supersededById ? <p>{T.superseded}</p> : null}
        {row.status === 'Proposed' && hasCapability(shell, 'finance.credits.approve') ? row.isOwnProposal ? <p>{T.own}</p> : <fieldset disabled={frozen}>
          <button onClick={() => void send({ key: crypto.randomUUID(), operation: 'decide', id: row.id, request: { expectedVersion: row.version, approve: true, reason: null } })}>{T.approve}</button>
          <label>{T.reason}<input value={rejectReason} maxLength={1000} onChange={event => setRejectReason(event.target.value)} /></label>
          <button disabled={rejectReason.trim() === ''} onClick={() => void send({ key: crypto.randomUUID(), operation: 'decide', id: row.id, request: { expectedVersion: row.version, approve: false, reason: rejectReason.trim() } })}>{T.reject}</button>
        </fieldset> : null}</article>)}
      {hasPending ? <p>{T.pending}</p> : hasCapability(shell, 'finance.credits.propose') ? <form className={styles.form} onSubmit={event => { event.preventDefault(); if (frozen) return
        void send({ key: crypto.randomUUID(), operation: 'propose', request: { creditAccountId: account.id, expectedCreditVersion: account.version, expectedCurrentLimitId: current?.id ?? null,
          expectedCurrentLimitVersion: current?.version ?? null, amount, sourceDocument: { reference, revision, documentDate: date }, reason } })
      }}><fieldset disabled={frozen}>
        <label>{T.amount}<input required inputMode="decimal" maxLength={128} value={amount} onChange={event => setAmount(event.target.value)} /></label>
        <label>{T.reference}<input required maxLength={200} value={reference} onChange={event => setReference(event.target.value)} /></label>
        <label>{T.revision}<input required maxLength={100} value={revision} onChange={event => setRevision(event.target.value)} /></label>
        <label>{T.date}<input required type="date" value={date} onChange={event => setDate(event.target.value)} /></label>
        <label>{T.reason}<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
        <button type="submit">{T.propose}</button>
      </fieldset></form> : null}
      <button onClick={() => void query.refetch()} disabled={sending}>{T.refresh}</button>
    </>}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button disabled={sending || attempt === null} onClick={() => attempt && void send(attempt, true)}>{T.retry}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{T.refused} ({outcome.status})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
