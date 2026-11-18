import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { SOURCE_VOUCHER_TEXT as T } from './sourceVoucherText'
import { approveSourceVoucher, fetchSourceVouchers, type SourceVoucher, type SourceVoucherAttempt, type SourceVoucherOutcome } from './sourceVoucherApi'
import styles from './Accounting.module.css'
import { SourceVoucherReversalPanel } from './SourceVoucherReversalPanel'

export function SourceVouchersPage({ mode }: { readonly mode: 'read' | 'approve' }) {
  const shell = useShellState(); const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  return <Page key={`${namespace}:${shell.routeEntryGeneration}:${mode}`} namespace={namespace} mode={mode} />
}
function Page({ namespace, mode }: { readonly namespace: string | null; readonly mode: 'read' | 'approve' }) {
  const shell = useShellState(); const boundary = useAuthBoundary()
  const company = shell.activeContext?.companyId ?? ''; const actor = shell.activeContext?.userId ?? ''
  const single = namespace !== null && company !== '' && shell.activeContext?.scopeMode === 'single'
  const [selected, setSelected] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<SourceVoucherAttempt | null>(null)
  const [outcome, setOutcome] = useState<SourceVoucherOutcome | null>(null)
  const [sending, setSending] = useState(false); const inFlight = useRef(false); const mounted = useRef(true)
  const [reversalFrozen, setReversalFrozen] = useState(false)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'accounting-source-vouchers'], enabled: single,
    queryFn: async ({ signal }) => { const security = boundary.capture(); const answer = await fetchSourceVouchers(company, signal)
      if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, security)
      return answer } })
  const frozen = sending || outcome?.type === 'unknown' || reversalFrozen
  const send = async (next: SourceVoucherAttempt, retry = false) => {
    if (!single || mode !== 'approve' || inFlight.current || outcome?.type === 'unknown' && !retry) return
    inFlight.current = true; setSending(true); setAttempt(next); setOutcome(null)
    try { const security = boundary.capture(); const answer = await approveSourceVoucher(company, next)
      if (answer.type === 'refused' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, security)
      if (mounted.current) { setOutcome(answer)
        if (answer.type === 'applied' || answer.type === 'refused' && answer.status === 409) void query.refetch() }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<readonly SourceVoucher[]>
  const row = state.kind === 'ready' ? state.data.find(item => item.id === selected) : undefined
  return <section className={styles.page} aria-labelledby="source-voucher-title"><h1 id="source-voucher-title">{T.title}</h1><p>{T.intro}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} /> : <>
        <button type="button" disabled={frozen} onClick={() => void query.refetch()}>{T.refresh}</button>
        {state.data.length === 0 ? <p>{T.empty}</p> : <div className={styles.tableWrap}><table aria-labelledby="source-voucher-title" className={styles.table}>
          <thead><tr><th scope="col">{T.source}</th><th scope="col">{T.invoice}</th><th scope="col">{T.date}</th><th scope="col">{T.amount}</th><th scope="col">{T.status}</th><th scope="col">{T.select}</th></tr></thead>
          <tbody>{state.data.map(item => <tr key={item.id}><td>{item.documentKind === 'sales-invoice' ? T.sale : T.purchase}</td>
            <td>{item.officialInvoiceNo ?? T.missingNumber}</td><td>{item.documentDate}</td><td>{item.functionalPayable}</td><td>{T.statuses[item.status]}</td>
            <td><button type="button" disabled={frozen} onClick={() => { setSelected(item.id); setOutcome(null); setAttempt(null) }}>{T.select}</button></td></tr>)}</tbody>
        </table></div>}
        {selected !== null && row === undefined ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} /> : null}
        {row === undefined ? null : <><h2>{row.officialInvoiceNo ?? T.missingNumber}</h2><p>{T.statuses[row.status]}</p>
          <div className={styles.tableWrap}><table aria-label={`${T.title} · ${row.officialInvoiceNo ?? T.missingNumber}`} className={styles.table}><thead><tr><th scope="col">{T.account}</th><th scope="col">{T.side}</th><th scope="col">{T.lineAmount}</th></tr></thead>
            <tbody>{row.lines.map(line => <tr key={line.lineNumber}><td>{line.accountCode === null ? T.accountMissing : `${line.accountCode} · ${line.accountName}`}</td>
              <td>{line.side === 1 ? T.debit : T.credit}</td><td>{line.functionalAmount}</td></tr>)}</tbody></table></div>
          {row.status === 'PeriodWaiting' ? <p role="note">{T.period}</p> : null}
          {row.cancellationReason !== null ? <p>{row.cancellationReason}</p> : null}
          {['CancellationReversalPending', 'CancelledByReversal'].includes(row.status) && namespace !== null
            ? <SourceVoucherReversalPanel key={row.id} row={row} namespace={namespace}
              onFrozenChange={setReversalFrozen} onChanged={() => void query.refetch()} /> : null}
          {mode === 'approve' && row.status === 'AwaitingApproval' ? row.sourceActorId.toLowerCase() === actor.toLowerCase()
            ? <p>{T.own}</p> : <button type="button" disabled={frozen} onClick={() => void send({ id: row.id, key: crypto.randomUUID(), expectedVersion: row.version })}>{T.approve}</button> : null}
        </>}
      </>}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button type="button" disabled={sending || attempt === null}
      onClick={() => attempt !== null && void send(attempt, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{T.refused} ({outcome.status})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
