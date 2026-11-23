import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchCogsHistory, fetchCogsOptions, findCogsSource, sendCogsCorrection, type CogsAttempt, type CogsCommandOutcome, type CogsHistory, type CogsOptions, type CogsSourceSelection } from './invoiceLineCogsApi'
import { invoiceLineCogsPath } from './capabilities'
import { INVOICE_LINE_COGS_TEXT as T } from './invoiceLineCogsText'
import styles from './Accounting.module.css'

export function InvoiceLineCogsCorrectionPage({ mode }: { readonly mode: 'prepare' | 'approve' }) {
  const shell = useShellState()
  const [parameters] = useSearchParams()
  const invoiceId = parameters.get('invoiceId') ?? ''
  const invoiceLineId = parameters.get('invoiceLineId') ?? ''
  const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  if (invoiceId === '' && invoiceLineId === '') return <SourcePicker key={`${namespace}:${shell.routeEntryGeneration}:${mode}`} namespace={namespace} mode={mode} />
  return <Page key={`${namespace}:${shell.routeEntryGeneration}:${invoiceId}:${invoiceLineId}:${mode}`}
    namespace={namespace} invoiceId={invoiceId} lineId={invoiceLineId} mode={mode} />
}
function SourcePicker({ namespace, mode }: { readonly namespace: string | null; readonly mode: 'prepare' | 'approve' }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const company = shell.activeContext?.companyId ?? ''
  const single = namespace !== null && company !== '' && shell.activeContext?.scopeMode === 'single'
  const [number, setNumber] = useState('')
  const [submitted, setSubmitted] = useState('')
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'invoice-line-cogs-source', submitted], enabled: single && submitted !== '',
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const result = await findCogsSource(company, submitted, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
      return result
    } })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<CogsSourceSelection>
  return <section className={styles.page}><h1>{T.title}</h1><p>{T.intro}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /> : <>
      <form onSubmit={event => { event.preventDefault(); if (number.trim() !== '') setSubmitted(number.trim()) }}>
        <label>{T.officialNumber}<input required maxLength={64} value={number} onChange={event => setNumber(event.target.value)} /></label>
        <button type="submit" disabled={number.trim() === '' || query.isFetching}>{T.find}</button></form>
      {submitted === '' ? null : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
        : <><p>{state.data.officialInvoiceNumber} · {state.data.ettn}</p><ol>{state.data.lines.map(line => <li key={line.invoiceLineId}>
          {T.line} {line.invoiceLineNo} · <Link to={invoiceLineCogsPath(state.data.invoiceId, line.invoiceLineId, mode)}>{T.open}</Link>
        </li>)}</ol></>}
    </>}
  </section>
}
function Page({ namespace, invoiceId, lineId, mode }: { readonly namespace: string | null; readonly invoiceId: string; readonly lineId: string; readonly mode: 'prepare' | 'approve' }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const company = shell.activeContext?.companyId ?? ''
  const actor = shell.activeContext?.userId ?? ''
  const single = namespace !== null && company !== '' && shell.activeContext?.scopeMode === 'single'
  const [attempt, setAttempt] = useState<CogsAttempt | null>(null)
  const [outcome, setOutcome] = useState<CogsCommandOutcome | null>(null)
  const [sending, setSending] = useState(false)
  const busy = useRef(false)
  const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'invoice-line-cogs', invoiceId, lineId], enabled: single,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const answer = await fetchCogsHistory(company, invoiceId, lineId, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, security)
      return answer
    } })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<CogsHistory>
  const frozen = sending || outcome?.type === 'unknown'
  async function send(next: CogsAttempt, retry = false) {
    if (!single || busy.current || outcome?.type === 'unknown' && !retry) return
    busy.current = true; setSending(true); setAttempt(next); setOutcome(null)
    try {
      const security = boundary.capture()
      const answer = await sendCogsCorrection(company, next)
      if (answer.type === 'refused' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, security)
      if (mounted.current) {
        setOutcome(answer)
        if (answer.type === 'applied' || answer.type === 'refused' && answer.status === 409) void query.refetch()
      }
    } finally { busy.current = false; if (mounted.current) setSending(false) }
  }
  return <section className={styles.page} aria-labelledby="invoice-line-cogs-title">
    <h1 id="invoice-line-cogs-title">{T.title}</h1><p>{T.intro}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
        : <>
          <button type="button" disabled={frozen} onClick={() => void query.refetch()}>{T.refresh}</button>
          <dl><dt>{T.source}</dt><dd>{state.data.source.officialInvoiceNumber} · {state.data.source.invoiceDate} · {state.data.source.ettn}</dd>
            <dt>{T.version}</dt><dd>{state.data.lineVersion}</dd><dt>{T.product}</dt>
            <dd>{state.data.productReference.classification === null ? T.sourceUnknown : T.classes[state.data.productReference.classification]}</dd></dl>
          <p role="note">{state.data.postingEvidenceState === 'NoPostedCogs' ? T.noPosting : state.data.postingEvidenceState === 'Unreadable' ? T.unreadable
            : state.data.postingEvidenceState === 'AttributedPostedCogs' ? T.attributed
            : state.data.postingEvidenceState === 'MixedPostedCogs' ? `${T.mixed} ${T.posted}: ${state.data.postedEvidence?.amount}`
            : `${T.posted}: ${state.data.postedEvidence?.amount} · ${state.data.postedEvidence?.currencyId}`}</p>
          {state.data.attributedEvidence ? <ul>{state.data.attributedEvidence.map(item => <li key={item.source.fingerprint}>
            {T.posted}: {item.source.signedAmount} · {item.currentJournalId}</li>)}</ul> : null}
          {state.data.purchaseAttributedEvidence ? <ul>{state.data.purchaseAttributedEvidence.map(item => <li key={item.source.fingerprint}>
            {T.purchasePosted}: {item.source.signedAmount} · {item.currentJournalId ?? T.approvedWithoutJournal}</li>)}</ul> : null}
          <h2>{T.history}</h2>{state.data.revisions.length === 0 ? <p>{T.noHistory}</p> : <ol>{state.data.revisions.map(row => <li key={row.version}>
            {row.version} · {T.classes[row.classification]} · {row.accountPlan.account.code} · {row.accountPlan.account.name} · {row.accountingDate} · {row.reason}
          </li>)}</ol>}
          {mode === 'prepare' ? <Preparation key={state.data.lineVersion} history={state.data} company={company} namespace={namespace!}
            frozen={frozen || state.data.postingEvidenceState === 'Unreadable'} send={next => void send(next)} /> : null}
          <h2>{T.proposals}</h2>{state.data.proposals.length === 0 ? <p>{T.noProposals}</p> : state.data.proposals.map(row => <article key={row.proposalId}>
            <p>{T.statuses[row.status]} · {T.classes[row.classification]} · {row.accountPlan.account.code} · {row.accountPlan.account.name}</p>
            <p>{row.transactionDate} / {row.accountingDate} · {row.reason}</p>
            {mode === 'approve' && row.status === 'Prepared' ? row.preparedBy.toLowerCase() === actor.toLowerCase() ? <p>{T.own}</p>
              : <button type="button" disabled={frozen || state.data.postingEvidenceState === 'Unreadable' || row.expectedLineVersion !== state.data.lineVersion}
                onClick={() => void send({ key: crypto.randomUUID(), invoiceId, lineId, proposalId: row.proposalId, operation: 'approve',
                  request: { expectedVersion: row.version } })}>{T.approve}</button> : null}
          </article>)}
        </>}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button type="button" disabled={sending || attempt === null}
      onClick={() => attempt !== null && void send(attempt, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{T.refused} ({outcome.status}{outcome.code ? ` · ${outcome.code}` : ''})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
function Preparation({ history, company, namespace, frozen, send }: { readonly history: CogsHistory; readonly company: string
  readonly namespace: string; readonly frozen: boolean; readonly send: (attempt: CogsAttempt) => void }) {
  const boundary = useAuthBoundary()
  const [kind, setKind] = useState<'' | '1' | '2'>(history.current ? String(history.current.classification) as '1' | '2' : '')
  const [account, setAccount] = useState(history.current?.accountPlan.account.accountId ?? '')
  const [period, setPeriod] = useState('')
  const [transactionDate, setTransactionDate] = useState('')
  const [accountingDate, setAccountingDate] = useState('')
  const [valueDate, setValueDate] = useState('')
  const [reason, setReason] = useState('')
  const query = useQuery({ queryKey: [namespace, 'invoice-line-cogs-options', history.lineVersion], queryFn: async ({ signal }) => {
    const security = boundary.capture()
    const result = await fetchCogsOptions(company, signal)
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    return result
  } })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<CogsOptions>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.account} onRetry={() => void query.refetch()} />
  if (state.data.accounts.length === 0 || state.data.openPeriods.length === 0) return <p>{T.noOptions}</p>
  const valid = kind !== '' && state.data.accounts.some(row => row.accountId === account) && transactionDate !== '' && accountingDate !== ''
    && state.data.openPeriods.some(row => row.periodId === period && row.startsOn <= accountingDate && row.endsOn >= accountingDate) && reason.trim() !== ''
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (frozen || !valid) return
    const proposalId = crypto.randomUUID()
    send({ key: crypto.randomUUID(), invoiceId: history.invoiceId, lineId: history.invoiceLineId, proposalId, operation: 'prepare', request: {
      proposalId, expectedFinalizedVersion: history.source.finalizedVersion, expectedLineVersion: history.lineVersion,
      classification: kind === '1' ? 1 : 2, cogsAccountId: account, periodId: period, transactionDate, accountingDate,
      valueDate: valueDate || null, reason: reason.trim(),
    } })
  }
  return <form onSubmit={submit}><fieldset className={styles.form} disabled={frozen}>
    <label>{T.classification}<select value={kind} required onChange={event => setKind(event.target.value as '' | '1' | '2')}>
      <option value="">{T.choose}</option><option value="1">{T.classes[1]}</option><option value="2">{T.classes[2]}</option></select></label>
    <label>{T.account}<select value={account} required onChange={event => setAccount(event.target.value)}><option value="">{T.choose}</option>
      {state.data.accounts.map(row => <option key={row.accountId} value={row.accountId}>{row.code} · {row.name}</option>)}</select></label>
    <label>{T.period}<select value={period} required onChange={event => setPeriod(event.target.value)}><option value="">{T.choose}</option>
      {state.data.openPeriods.map(row => <option key={row.periodId} value={row.periodId}>{row.startsOn} — {row.endsOn}</option>)}</select></label>
    <label>{T.transactionDate}<input type="date" required value={transactionDate} onChange={event => setTransactionDate(event.target.value)} /></label>
    <label>{T.accountingDate}<input type="date" required value={accountingDate} onChange={event => setAccountingDate(event.target.value)} /></label>
    <label>{T.valueDate}<input type="date" value={valueDate} onChange={event => setValueDate(event.target.value)} /></label>
    <label>{T.reason}<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    <button type="submit" disabled={!valid}>{T.prepare}</button>
  </fieldset></form>
}
