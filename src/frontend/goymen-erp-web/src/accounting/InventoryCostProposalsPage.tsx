import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { journalReversalApprovalPath } from './capabilities'
import { INVENTORY_COST_TEXT as T } from './inventoryCostText'
import { fetchInventoryCosts, fetchInventoryCostOptions, sendInventoryCost, type InventoryCostAttempt,
  type InventoryCostKind, type InventoryCostOptions, type InventoryCostOutcome, type InventoryCostProposal } from './inventoryCostApi'
import styles from './Accounting.module.css'

interface Props { readonly kind: InventoryCostKind; readonly canPrepare: boolean; readonly canApprove: boolean; readonly canProposeReversal: boolean; readonly canApproveCancellation?: boolean }
const pending = new Map<string, InventoryCostAttempt>()

export function InventoryCostProposalsPage(props: Props) {
  const shell = useShellState(); const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  return <Page key={`${namespace}:${shell.routeEntryGeneration}:${props.kind}`} {...props} namespace={namespace} />
}

function Page({ kind, canPrepare, canApprove, canProposeReversal, canApproveCancellation = false, namespace }: Props & { readonly namespace: string | null }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const actor = shell.activeContext?.userId ?? ''
  const company = shell.activeContext?.companyId ?? ''; const single = namespace !== null && company !== '' && shell.activeContext?.scopeMode === 'single'
  // Authority/cache generations may change while a command's outcome is unknown. Keep its immutable retry
  // identity within this session, actor and company; the query namespace still fences every fresh read.
  const slot = JSON.stringify([shell.sessionEpoch, actor, company, kind]); const held = pending.get(slot) ?? null
  const [selected, setSelected] = useState<string | null>(held?.id ?? null)
  const [attempt, setAttempt] = useState<InventoryCostAttempt | null>(held)
  const [outcome, setOutcome] = useState<InventoryCostOutcome | null>(held ? { type: 'unknown' } : null)
  const [sending, setSending] = useState(false); const inFlight = useRef(false); const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'inventory-cost-proposals', kind], enabled: single,
    queryFn: async ({ signal }) => { const security = boundary.capture(); const value = await fetchInventoryCosts(company, kind, signal)
      if (value.type === 'http' && [401, 403].includes(value.status)) boundary.report(value.status as 401 | 403, security)
      return value } })
  const frozen = sending || outcome?.type === 'unknown'
  const maySend = (operation: InventoryCostAttempt['operation']) => operation === 'prepare' ? canPrepare : operation === 'approve' ? canApprove
    : operation === 'approve-cancellation' ? canApproveCancellation : canProposeReversal
  const send = async (next: InventoryCostAttempt, retry = false) => {
    if (inFlight.current || !maySend(next.operation) || outcome?.type === 'unknown' && !retry) return
    inFlight.current = true; pending.set(slot, next); setAttempt(next); setSending(true); setOutcome(null)
    try {
      const security = boundary.capture(); const response = await sendInventoryCost(company, next)
      if (response.type === 'refused' && [401, 403].includes(response.status)) boundary.report(response.status as 401 | 403, security)
      const result = retry && response.type === 'refused' && [401, 403].includes(response.status) ? { type: 'unknown' as const } : response
      if (result.type !== 'unknown') pending.delete(slot)
      if (mounted.current) { setOutcome(result); if (result.type === 'applied' || result.type === 'refused' && result.status === 409) void query.refetch() }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const state = (!single ? { kind: 'no-authorized-company' } : query.isPending ? { kind: 'loading' }
    : query.isError ? { kind: 'network-error' } : classifyOutcome(query.data)) as RemoteViewState<readonly InventoryCostProposal[]>
  const row = state.kind === 'ready' ? state.data.find(value => value.proposalId === selected) : undefined
  return <section className={styles.page} aria-labelledby="inventory-cost-title">
    <h1 id="inventory-cost-title">{kind === 1 ? T.salesTitle : T.differenceTitle}</h1><p>{T.intro}</p>
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} /> : <>
      <button disabled={frozen} onClick={() => void query.refetch()}>{T.refresh}</button>
      {state.data.length === 0 ? <p>{T.empty}</p> : <div className={styles.tableWrap}><table className={styles.table}>
        <thead><tr><th>{T.invoice}</th><th>{T.line}</th><th>{T.amount}</th><th>{T.status}</th><th>{T.choose}</th></tr></thead>
        <tbody>{state.data.map(value => <tr key={value.proposalId}>
          <td>{value.officialInvoiceNumber ?? T.sourceNumberMissing}<br />{value.invoiceDate}</td><td>{value.component.invoiceLineNumber ?? '—'}</td>
          <td>{value.exactTlAmount}</td><td>{T.statuses[value.status]}</td><td><button disabled={frozen}
            onClick={() => { setSelected(value.proposalId); setOutcome(null); setAttempt(null) }}>{T.choose}</button></td>
        </tr>)}</tbody></table></div>}
      {selected !== null && row === undefined ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} /> : null}
      {row !== undefined ? <>
        <h2>{row.officialInvoiceNumber ?? T.sourceNumberMissing} · {T.statuses[row.status]}</h2><p>{T.amount}: {row.exactTlAmount}</p>
        {row.unpostedTlAmountText !== undefined && <p>{T.unposted}: {row.unpostedTlAmountText}</p>}
        {row.component.claimSuccession && <p>{T.succession}</p>}
        {row.component.recordedRounding ? <><p>{T.ownerRounded}</p><dl>
          <dt>{T.beforeRounding}</dt><dd>{row.component.recordedRounding.rounding.exactSignedAmountText}</dd>
          <dt>{T.roundingResidue}</dt><dd>{row.component.recordedRounding.rounding.exactSignedResidueText}</dd>
          <dt>{T.moneyScale}</dt><dd>{row.component.recordedRounding.moneyScale}</dd>
          <dt>{T.moneyProfileVersion}</dt><dd>{row.component.recordedRounding.moneyProfileVersion}</dd></dl></> : null}
        {row.component.alreadyPostedComponents ? <><p>{['AttributedCancellationPending', 'MixedCancellationPending'].includes(row.status) ? T.attributedPending : T.attributed}</p>
          <ul>{row.component.alreadyPostedComponents.map(share => <li key={share.fingerprint}>
            {T.attributedJournal}: {share.journalId} · {T.attributedAmount}: {share.signedAmount}</li>)}</ul></> : null}
        {row.component.alreadyPostedPurchaseComponents ? <><p>{T.purchaseAttributed}</p>
          <ul>{row.component.alreadyPostedPurchaseComponents.map(share => <li key={share.fingerprint}>
            {T.attributedJournal}: {share.approval.journalId ?? T.purchaseRoundedZero} · {T.attributedAmount}: {share.signedAmount}</li>)}</ul></> : null}
        {row.component.signedTlAmount.numerator.startsWith('-') ? <p role="note">{T.negative}</p> : null}
        {row.preparation !== null ? <dl><dt>{T.classification}</dt><dd>{row.preparation.classification.classification === 1 ? T.trade : T.manufactured}</dd>
          <dt>{T.counter}</dt><dd>{row.preparation.counterAccount.code} · {row.preparation.counterAccount.name}</dd>
          <dt>{T.preparedDate}</dt><dd>{row.preparation.accountingDate}</dd><dt>{T.reason}</dt><dd>{row.preparation.reason}</dd>
          <dt>{T.plan}</dt><dd>{row.preparation.accountPlanSourceVersion}</dd>
          <dt>{T.postingAmount}</dt><dd>{row.preparation.signedAmount}</dd>
          {row.preparation.rounding ? <><dt>{T.beforeRounding}</dt><dd>{row.preparation.rounding.exactSignedAmountText}</dd>
            <dt>{T.roundingResidue}</dt><dd>{row.preparation.rounding.exactSignedResidueText}</dd>
            <dt>{T.moneyScale}</dt><dd>{row.preparation.moneyScale}</dd><dt>{T.moneyProfileVersion}</dt><dd>{row.preparation.moneyProfileVersion}</dd></> : null}</dl> : null}
        {row.status === 'RoundedNoCost' || row.status === 'RoundedNoCostCancelled' ? <p>{T.roundedZero}</p> : null}
        {canPrepare && ['AwaitingPreparation', 'Prepared'].includes(row.status) ? <Preparation key={`${row.proposalId}:${row.version}:prepare`}
          row={row} company={company} namespace={namespace!} actor={actor} frozen={frozen} reverse={false} send={next => void send(next)} /> : null}
        {canApprove && row.status === 'Prepared' ? row.preparation?.preparedBy.toLowerCase() === actor.toLowerCase() ? <p>{T.own}</p>
          : <button disabled={frozen} onClick={() => void send({ id: row.proposalId, kind, actor, operation: 'approve', key: crypto.randomUUID(), request: { expectedVersion: row.version } })}>{T.approve}</button> : null}
        {row.status === 'CancellationReversalPending' ? <><p>{T.cancellation}</p>
          {row.reversalProposal !== null && row.reversalProposal.status !== 'Rejected' ? <>
            <p>{T.existingReversal}</p><Link to={journalReversalApprovalPath(row.reversalProposal.proposalId)}>{T.reversalApproval}</Link>
          </> : canProposeReversal ? <Preparation key={`${row.proposalId}:${row.version}:reverse`} row={row} company={company}
            namespace={namespace!} actor={actor} frozen={frozen} reverse send={next => void send(next)} /> : <p>{T.denied}</p>}
        </> : null}
        {['ComponentCancellationPending', 'ComponentCancellationPrepared', 'CancelledByComponentReversal'].includes(row.status) ? <>
          <p>{T.componentCancellation}</p>
          {row.componentCancellation ? <><p>{T.preparedDate}: {row.componentCancellation.accountingDate} · {T.reason}: {row.componentCancellation.reason}</p>
            <ul>{row.componentCancellation.components.map(part => <li key={part.economicKey}>
              {T.attributedAmount}: {part.signedAmount} · {part.sourceJournalId ? `${T.attributedJournal}: ${part.sourceJournalId}` : T.purchaseRoundedZero}
              {part.reversingJournalId ? ` · ${T.componentReverseJournal}: ${part.reversingJournalId}` : ''}</li>)}</ul></> : null}
          {canProposeReversal && row.status !== 'CancelledByComponentReversal' ? <Preparation
            key={`${row.proposalId}:${row.componentCancellation?.version ?? 0}:component-reverse`} row={row} company={company}
            namespace={namespace!} actor={actor} frozen={frozen} reverse send={next => void send(next)} /> : null}
          {canApproveCancellation && row.status === 'ComponentCancellationPrepared' && row.componentCancellation ?
            row.componentCancellation.preparedBy.toLowerCase() === actor.toLowerCase() ? <p>{T.own}</p>
              : <button disabled={frozen} onClick={() => void send({ id: row.proposalId, kind, actor, operation: 'approve-cancellation', key: crypto.randomUUID(),
                request: { expectedVersion: row.version, expectedCancellationVersion: row.componentCancellation!.version } })}>{T.componentApprove}</button> : null}
        </> : null}
      </> : null}
    </>}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button disabled={sending || attempt === null || !maySend(attempt.operation)}
      onClick={() => attempt !== null && void send(attempt, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{outcome.code !== null && T.codes[outcome.code] || T.refused} ({outcome.status})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}

function Preparation({ row, company, namespace, actor, frozen, reverse, send }: { readonly row: InventoryCostProposal
  readonly company: string; readonly namespace: string; readonly actor: string; readonly frozen: boolean; readonly reverse: boolean
  readonly send: (attempt: InventoryCostAttempt) => void }) {
  const boundary = useAuthBoundary()
  const [form, setForm] = useState({ periodId: '', accountingDate: '', transactionDate: '', counterAccountId: '', reason: '' })
  const [invalid, setInvalid] = useState(false)
  const options = useQuery({ queryKey: [namespace, 'inventory-cost-options', row.kind, row.proposalId, row.version],
    queryFn: async ({ signal }) => { const security = boundary.capture(); const value = await fetchInventoryCostOptions(company, row.kind, signal)
      if (value.type === 'http' && [401, 403].includes(value.status)) boundary.report(value.status as 401 | 403, security)
      return value } })
  const state = (options.isPending ? { kind: 'loading' } : options.isError ? { kind: 'network-error' }
    : classifyOutcome(options.data)) as RemoteViewState<InventoryCostOptions>
  const submit = (event: FormEvent) => {
    event.preventDefault(); if (frozen || state.kind !== 'ready') return
    const period = state.data.openPeriods.find(value => value.periodId === form.periodId)
    if (form.transactionDate === '' || form.accountingDate === '' || !period || form.accountingDate < period.startsOn || form.accountingDate > period.endsOn
      || !reverse && !state.data.accounts.some(value => value.accountId === form.counterAccountId) || form.reason.trim() === '') { setInvalid(true); return }
    const common = { id: row.proposalId, kind: row.kind, actor, key: crypto.randomUUID() }
    const request = { expectedVersion: row.version, periodId: form.periodId, accountingDate: form.accountingDate, transactionDate: form.transactionDate, reason: form.reason.trim() }
    send(reverse ? { ...common, operation: 'reverse', request: { ...request, proposalId: crypto.randomUUID(), expectedCancellationVersion: row.componentCancellation?.version ?? 0 } }
      : { ...common, operation: 'prepare', request: { ...request, counterAccountId: form.counterAccountId } })
  }
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.period} onRetry={() => void options.refetch()} />
  return <form className={styles.form} onSubmit={submit}>
    {!reverse ? <p>{T.mapping}</p> : null}
    <label>{T.transactionDate}<input type="date" disabled={frozen} value={form.transactionDate} onChange={e => setForm({ ...form, transactionDate: e.target.value })} /></label>
    <label>{T.date}<input type="date" disabled={frozen} value={form.accountingDate} onChange={e => setForm({ ...form, accountingDate: e.target.value, periodId: '' })} /></label>
    <label>{T.period}<select disabled={frozen} value={form.periodId} onChange={e => setForm({ ...form, periodId: e.target.value })}>
      <option value="">{T.select}</option>{state.data.openPeriods.filter(p => form.accountingDate >= p.startsOn && form.accountingDate <= p.endsOn)
        .map(p => <option key={p.periodId} value={p.periodId}>{p.startsOn} – {p.endsOn}</option>)}</select></label>
    {state.data.openPeriods.length === 0 ? <p>{T.noPeriods}</p> : null}
    {!reverse ? <label>{T.counter}<select disabled={frozen} value={form.counterAccountId} onChange={e => setForm({ ...form, counterAccountId: e.target.value })}>
      <option value="">{T.select}</option>{state.data.accounts.map(a => <option key={a.accountId} value={a.accountId}>{a.code} · {a.name}</option>)}</select></label> : null}
    <label>{T.reason}<textarea disabled={frozen} maxLength={1000} value={form.reason} onChange={e => setForm({ ...form, reason: e.target.value })} /></label>
    {invalid ? <p role="alert">{T.invalid}</p> : null}<button disabled={frozen || state.data.openPeriods.length === 0} type="submit">{reverse ? T.reverse : T.prepare}</button>
  </form>
}
