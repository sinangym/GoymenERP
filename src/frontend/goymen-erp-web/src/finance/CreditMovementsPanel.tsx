import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import type { BankAccountMasterPage } from './bankAccountContract'
import type { CreditAccount } from './creditAccountApi'
import { fetchCreditBanks, fetchCreditMovements, sendCreditMovement, type CreditMovement, type CreditMovementInput, type CreditMovementKind, type CreditComponentKind, type MovementAttempt, type MovementOutcome } from './creditMovementApi'
import { CREDIT_MOVEMENT_TEXT as T } from './creditMovementText'
import styles from './Finance.module.css'
import { CreditAccountingProposalsPanel } from './CreditAccountingProposalsPanel'

const pending = new Map<string, MovementAttempt>()
export function CreditMovementsPanel({ account, namespace }: { readonly account: CreditAccount; readonly namespace: string }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const company = account.companyId
  const slot = `${namespace}:credit-actual:${account.id}`
  const [editing, setEditing] = useState(false); const [original, setOriginal] = useState<CreditMovement | null>(null)
  const [attempt, setAttempt] = useState<MovementAttempt | null>(() => pending.get(slot) ?? null)
  const [outcome, setOutcome] = useState<MovementOutcome | null>(() => pending.has(slot) ? { type: 'unknown' } : null)
  const [sending, setSending] = useState(false); const [rejectReason, setRejectReason] = useState('')
  const mounted = useRef(true); const busy = useRef(false)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'credit-actual', account.id], queryFn: async ({ signal }) => {
    const stamp = boundary.capture(); const result = await fetchCreditMovements(company, account.id, signal)
    if (result.type === 'http' && [401, 403].includes(result.status)) boundary.report(result.status as 401 | 403, stamp)
    return result
  } })
  const frozen = sending || outcome?.type === 'unknown'
  const send = async (next: MovementAttempt, retry = false) => {
    if (busy.current || frozen && !retry) return
    busy.current = true; setSending(true); setAttempt(next); setOutcome(null); pending.set(slot, next)
    try { const stamp = boundary.capture(); const result = await sendCreditMovement(company, account.id, next)
      if (result.type === 'refused' && [401, 403].includes(result.status)) boundary.report(result.status as 401 | 403, stamp)
      // A transport/auth refusal before the owner reads its receipt cannot settle an earlier unknown write.
      const unresolvedRetry = retry && result.type === 'refused' && [400, 401, 403, 404, 415].includes(result.status)
      if (result.type !== 'unknown' && !unresolvedRetry) pending.delete(slot)
      if (mounted.current) { setOutcome(unresolvedRetry ? { type: 'unknown' } : result); if (result.type === 'applied') { setEditing(false); setOriginal(null) }; void query.refetch() }
    } finally { busy.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<readonly CreditMovement[]>
  return <section aria-labelledby={`credit-actual-${account.id}`}><h3 id={`credit-actual-${account.id}`}>{T.title}</h3><p>{T.intro}</p>
    {hasCapability(shell, 'finance.credits.propose') ? <button disabled={frozen} onClick={() => { setEditing(value => !value); setOriginal(null) }}>{editing ? T.cancel : T.add}</button> : null}
    {editing ? <MovementForm key={original?.id ?? 'new'} account={account} namespace={namespace} original={original} frozen={frozen} submit={request => void send({ key: crypto.randomUUID(), operation: 'propose', request })} /> : null}
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.title} onRetry={() => void query.refetch()} /> : state.data.length === 0 ? <p>{T.empty}</p> : state.data.map(row => <article key={row.id}>
      <h4>{T.kinds[row.source.kind]} · {row.source.sourceDocument.reference} · {T.statuses[row.status]}</h4>
      <p>{row.source.transactionDate} · {row.source.accountingDate} · {row.source.reason}</p>
      <ul>{row.source.components.map(part => <li key={part.kind}>{T.components[part.kind]}: {part.amount} · {part.functionalAmount ?? T.unknownTl}</li>)}</ul>
      {row.source.bankEffect ? <p>{T.bankAmount}: {row.source.bankEffect.amount} · {row.source.bankEffect.sourceReference}</p> : null}
      {row.reversedByMovementId ? <p>{T.reversal}</p> : null}
      {row.status === 'Approved' && row.source.kind === 'OpeningBalance' && row.reversedByMovementId === null && hasCapability(shell, 'finance.credits.propose')
        ? <button disabled={frozen} onClick={() => { setOriginal(row); setEditing(true) }}>{T.correction}</button> : null}
      {row.status === 'Proposed' && hasCapability(shell, 'finance.credits.approve') ? row.isOwnProposal ? <p>{T.own}</p> : <fieldset disabled={frozen}>
        <button onClick={() => void send({ key: crypto.randomUUID(), operation: 'decide', id: row.id, request: { expectedVersion: row.version, approve: true, reason: null } })}>{T.approve}</button>
        <label>{T.reason}<input value={rejectReason} maxLength={1000} onChange={event => setRejectReason(event.target.value)} /></label>
        <button disabled={rejectReason.trim() === ''} onClick={() => void send({ key: crypto.randomUUID(), operation: 'decide', id: row.id, request: { expectedVersion: row.version, approve: false, reason: rejectReason.trim() } })}>{T.reject}</button>
      </fieldset> : null}
    </article>)}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button disabled={sending || attempt === null} onClick={() => attempt && void send(attempt, true)}>{T.retry}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{T.refused} ({outcome.status})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
    <CreditAccountingProposalsPanel company={company} accountId={account.id} namespace={namespace} />
  </section>
}
function MovementForm({ account, namespace, original, frozen, submit }: { readonly account: CreditAccount; readonly namespace: string; readonly original: CreditMovement | null; readonly frozen: boolean; readonly submit: (request: CreditMovementInput) => void }) {
  const [kind, setKind] = useState<CreditMovementKind | ''>(original === null ? '' : 'OpeningBalance')
  const [reference, setReference] = useState(''); const [revision, setRevision] = useState(''); const [documentDate, setDocumentDate] = useState('')
  const [date, setDate] = useState(''); const [accountingDate, setAccountingDate] = useState(''); const [reason, setReason] = useState('')
  const [parts, setParts] = useState<{ kind: CreditComponentKind | ''; amount: string; functionalAmount: string }[]>([{ kind: '', amount: '', functionalAmount: '' }])
  const [hasBank, setHasBank] = useState(false); const [bankId, setBankId] = useState(''); const [bankAmount, setBankAmount] = useState(''); const [bankReference, setBankReference] = useState('')
  const bankAllowed = account.group !== 'NonCash' && (kind === 'Drawdown' || kind === 'Repayment')
  const banks = useQuery({ queryKey: [namespace, 'credit-actual-banks'], enabled: hasBank && bankAllowed, queryFn: ({ signal }) => fetchCreditBanks(account.companyId, signal) })
  const bankState = (banks.isPending ? { kind: 'initial-loading' } : classifyOutcome(banks.data ?? { type: 'malformed' })) as RemoteViewState<BankAccountMasterPage>
  const eligible = bankState.kind === 'ready' ? bankState.data.items.filter(x => x.account.currencyId === account.currencyId && x.account.isActive && x.account.isUsableForReceipt && x.card.moneyEligibility === 'Eligible') : []
  return <form className={styles.form} onSubmit={event => { event.preventDefault(); if (frozen || kind === '' || parts.some(x => x.kind === '')) return
    if (bankAllowed && hasBank && !eligible.some(x => x.account.id === bankId)) return
    submit({ creditAccountId: account.id, expectedCreditVersion: account.version, kind, sourceDocument: { reference, revision, documentDate }, transactionDate: date,
      accountingDate: original === null ? date : accountingDate, components: parts.map(x => ({ kind: x.kind as CreditComponentKind, amount: x.amount, functionalAmount: x.functionalAmount === '' ? null : x.functionalAmount })),
      bankEffect: bankAllowed && hasBank ? { bankAccountId: bankId, amount: bankAmount, sourceReference: bankReference } : null,
      replacesOpeningMovementId: original?.id ?? null, expectedOpeningMovementVersion: original?.version ?? null, reason })
  }}><fieldset disabled={frozen}><label>{T.kind}<select required value={kind} disabled={original !== null} onChange={event => setKind(event.target.value as CreditMovementKind)}><option value="">{T.select}</option>
    {Object.entries(T.kinds).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
    <label>{T.reference}<input required maxLength={200} value={reference} onChange={event => setReference(event.target.value)} /></label>
    <label>{T.revision}<input required maxLength={100} value={revision} onChange={event => setRevision(event.target.value)} /></label>
    <label>{T.documentDate}<input required type="date" value={documentDate} onChange={event => setDocumentDate(event.target.value)} /></label>
    <label>{T.date}<input required type="date" value={date} onChange={event => setDate(event.target.value)} /></label>
    {original !== null ? <><p>{T.correctionNotice}</p><label>{T.accountingDate}<input required type="date" value={accountingDate} onChange={event => setAccountingDate(event.target.value)} /></label></> : null}
    {parts.map((part, index) => <div key={index}><label>{T.component}<select required value={part.kind} onChange={event => setParts(value => value.map((x, i) => i === index ? { ...x, kind: event.target.value as CreditComponentKind } : x))}><option value="">{T.select}</option>
      {Object.entries(T.components).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <label>{T.amount}<input required inputMode="decimal" value={part.amount} onChange={event => setParts(value => value.map((x, i) => i === index ? { ...x, amount: event.target.value } : x))} /></label>
      <label>{T.tl}<input inputMode="decimal" value={part.functionalAmount} onChange={event => setParts(value => value.map((x, i) => i === index ? { ...x, functionalAmount: event.target.value } : x))} /></label>
      <button type="button" disabled={parts.length === 1} onClick={() => setParts(value => value.filter((_, i) => i !== index))}>{T.remove}</button></div>)}
    <button type="button" disabled={parts.length >= 6} onClick={() => setParts(value => [...value, { kind: '', amount: '', functionalAmount: '' }])}>{T.addComponent}</button>
    {bankAllowed ? <><label><input type="checkbox" checked={hasBank} onChange={event => setHasBank(event.target.checked)} />{T.bank}</label>
      {hasBank ? bankState.kind !== 'ready' ? <StateBlock state={bankState} subject={T.bankAccount} onRetry={() => void banks.refetch()} /> : eligible.length === 0 ? <p>{T.noBanks}</p> : <>
        <label>{T.bankAccount}<select required value={bankId} onChange={event => setBankId(event.target.value)}><option value="">{T.select}</option>{eligible.map(item => <option key={item.account.id} value={item.account.id}>{item.account.displayName}</option>)}</select></label>
        <label>{T.bankAmount}<input required inputMode="decimal" value={bankAmount} onChange={event => setBankAmount(event.target.value)} /></label>
        <label>{T.bankReference}<input required maxLength={200} value={bankReference} onChange={event => setBankReference(event.target.value)} /></label>
      </> : null}</> : null}
    <label>{T.reason}<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    <button type="submit" disabled={hasBank && bankAllowed && eligible.length === 0}>{T.propose}</button>
  </fieldset></form>
}
