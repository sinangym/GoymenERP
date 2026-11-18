import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchAccountPlan } from './accountPlanApi'
import type { AccountPlanView } from './accountPlanContract'
import { NormalJournalApprovalPeriod } from './NormalJournalApprovalPeriod'
import { COST_DIFFERENCE_TEXT as T } from './manufacturingCostDifferenceText'
import { fetchCostDifferences, sendCostDifference, type CostAttempt, type CostCommandOutcome, type CostDifference } from './manufacturingCostDifferenceApi'
import styles from './Accounting.module.css'

export function ManufacturingCostDifferencesPage({ mode }: { readonly mode: 'prepare' | 'approve' }) {
  const shell = useShellState()
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  return <Page key={`${namespace}:${shell.routeEntryGeneration}:${mode}`} namespace={namespace} mode={mode} />
}

function Page({ namespace, mode }: { readonly namespace: string | null; readonly mode: 'prepare' | 'approve' }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const company = shell.activeContext?.companyId ?? ''
  const single = namespace !== null && company !== '' && shell.activeContext?.scopeMode === 'single'
  const actor = shell.activeContext?.userId ?? ''
  const [selected, setSelected] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<CostAttempt | null>(null)
  const [outcome, setOutcome] = useState<CostCommandOutcome | null>(null)
  const [sending, setSending] = useState(false)
  const inFlight = useRef(false)
  const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'manufacturing-cost-differences'], enabled: single,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const answer = await fetchCostDifferences(company, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, security)
      return answer
    },
  })
  const frozen = sending || outcome?.type === 'unknown'
  const send = async (next: CostAttempt, retry = false) => {
    if (!single || inFlight.current || outcome?.type === 'unknown' && !retry) return
    inFlight.current = true
    setSending(true); setAttempt(next); setOutcome(null)
    try {
      const security = boundary.capture()
      const answer = await sendCostDifference(company, next)
      if (answer.type === 'refused' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, security)
      if (mounted.current) {
        setOutcome(answer)
        if (answer.type === 'applied' || answer.type === 'refused' && answer.status === 409) void query.refetch()
      }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<readonly CostDifference[]>
  const row = state.kind === 'ready' ? state.data.find(item => item.proposalId === selected) : undefined
  return <section className={styles.page} aria-labelledby="manufacturing-cost-title">
    <h1 id="manufacturing-cost-title">{T.title}</h1><p>{T.intro}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
        : <>
          <button type="button" disabled={frozen} onClick={() => void query.refetch()}>{T.refresh}</button>
          {state.data.length === 0 ? <p>{T.empty}</p> : <div className={styles.tableWrap}><table aria-labelledby="manufacturing-cost-title" className={styles.table}>
            <thead><tr><th scope="col">{T.source}</th><th scope="col">{T.amount}</th><th scope="col">{T.status}</th><th scope="col">{T.select}</th></tr></thead>
            <tbody>{state.data.map(item => <tr key={item.proposalId}>
              <td className={styles.mono}>{item.manufacturingOrderId}</td><td>{item.signedTlDifference} TL</td><td>{T.statuses[item.status]}</td>
              <td><button type="button" disabled={frozen} onClick={() => { setSelected(item.proposalId); setOutcome(null); setAttempt(null) }}>{T.select}</button></td>
            </tr>)}</tbody></table></div>}
          {selected !== null && row === undefined ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} /> : null}
          {row !== undefined ? <>
            <h2>{T.statuses[row.status]}</h2>
            <p className={styles.mono}>{row.proposalId}</p><p>{T.amount}: {row.signedTlDifference} TL</p>
            {row.signedTlDifference.startsWith('-') ? <p role="note">{T.warning}</p> : null}
            <div className={styles.tableWrap}><table aria-label={T.output} className={styles.table}><thead><tr><th scope="col">{T.output}</th><th scope="col">{T.kg}</th><th scope="col">{T.original}</th><th scope="col">{T.amount}</th></tr></thead>
              <tbody>{row.outputs.map(output => <tr key={output.outputId}><td className={styles.mono}>{output.labelId}</td>
                <td>{output.netQuantityKg} / {output.remainingQuantityKg} / {output.consumedQuantityKg}</td><td>{output.originalTlAmount}</td><td>{output.consumedTlDifference}</td></tr>)}</tbody></table></div>
            {row.periodId ? <dl><dt>{T.date}</dt><dd>{row.accountingDate}</dd><dt>{T.transactionDate}</dt><dd>{row.transactionDate}</dd>
              <dt>{T.debit}</dt><dd>{row.accountPlan?.debit.code} · {row.accountPlan?.debit.name}</dd>
              <dt>{T.credit}</dt><dd>{row.accountPlan?.credit.code} · {row.accountPlan?.credit.name}</dd><dt>{T.reason}</dt><dd>{row.reason}</dd>
              <dt>{T.planSource}</dt><dd>{row.accountPlan?.sourceVersion} · {row.accountPlan?.sha256}</dd></dl> : null}
            {mode === 'prepare' && ['AwaitingPreparation', 'Prepared'].includes(row.status)
              ? <Preparation key={`${row.proposalId}:${row.version}`} row={row} company={company} namespace={namespace!} frozen={frozen} send={next => void send(next)} /> : null}
            {mode === 'approve' && row.status === 'Prepared' ? row.preparedBy?.toLowerCase() === actor.toLowerCase()
              ? <p>{T.own}</p> : <button type="button" disabled={frozen} onClick={() => void send({ operation: 'approve', id: row.proposalId,
                key: crypto.randomUUID(), request: { expectedVersion: row.version } })}>{T.approve}</button> : null}
          </> : null}
        </>}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button type="button" disabled={sending || attempt === null}
      onClick={() => attempt !== null && void send(attempt, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{T.refused} ({outcome.status}{outcome.code ? ` · ${outcome.code}` : ''})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}

function Preparation({ row, company, namespace, frozen, send }: {
  readonly row: CostDifference; readonly company: string; readonly namespace: string; readonly frozen: boolean
  readonly send: (attempt: CostAttempt) => void
}) {
  const boundary = useAuthBoundary()
  const [period, setPeriod] = useState(row.periodId ?? '')
  const [date, setDate] = useState(row.accountingDate ?? '')
  const [transactionDate, setTransactionDate] = useState(row.transactionDate ?? '')
  const [debit, setDebit] = useState(row.debitAccountId ?? '')
  const [credit, setCredit] = useState(row.creditAccountId ?? '')
  const [reason, setReason] = useState(row.reason ?? '')
  const [periodReload, setPeriodReload] = useState(0)
  const plan = useQuery({ queryKey: [namespace, 'cost-difference-plan', row.proposalId, row.version], queryFn: async ({ signal }) => {
    const security = boundary.capture()
    const answer = await fetchAccountPlan(company, signal)
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, security)
    return answer
  } })
  const state = (plan.isPending ? { kind: 'initial-loading' } : classifyOutcome(plan.data ?? { type: 'malformed' })) as RemoteViewState<AccountPlanView>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.debit} onRetry={() => void plan.refetch()} />
  if (state.data.source === null) return <p role="note">{T.noPlan}</p>
  const accounts = state.data.accounts
  const valid = period !== '' && date !== '' && transactionDate !== '' && debit !== credit
    && accounts.some(account => account.accountId === debit) && accounts.some(account => account.accountId === credit) && reason.trim() !== ''
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (!valid || frozen) return
    send({ operation: 'prepare', id: row.proposalId, key: crypto.randomUUID(), request: { proposalId: row.proposalId,
      expectedVersion: row.version, periodId: period, accountingDate: date, transactionDate, debitAccountId: debit, creditAccountId: credit, reason: reason.trim() } })
  }
  return <form onSubmit={submit}><button type="button" disabled={frozen}
    onClick={() => { setPeriodReload(value => value + 1); setPeriod(''); void plan.refetch() }}>{T.refresh}</button>
    <fieldset disabled={frozen} className={styles.form}>
    <label>{T.date}<input type="date" required value={date} onChange={event => setDate(event.target.value)} /></label>
    <label>{T.transactionDate}<input type="date" required value={transactionDate} onChange={event => setTransactionDate(event.target.value)} /></label>
    <NormalJournalApprovalPeriod key={periodReload} namespace={namespace} companyId={company} accountingDate={date} value={period} onChange={setPeriod} disabled={frozen} reloadToken={periodReload} />
    <label>{T.debit}<select value={debit} required onChange={event => setDebit(event.target.value)}><option value="">{T.choose}</option>
      {accounts.map(account => <option key={account.accountId} value={account.accountId}>{account.code} · {account.name}</option>)}</select></label>
    <label>{T.credit}<select value={credit} required onChange={event => setCredit(event.target.value)}><option value="">{T.choose}</option>
      {accounts.map(account => <option key={account.accountId} value={account.accountId}>{account.code} · {account.name}</option>)}</select></label>
    <label>{T.reason}<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    <button type="submit" disabled={!valid}>{T.prepare}</button>
  </fieldset></form>
}
