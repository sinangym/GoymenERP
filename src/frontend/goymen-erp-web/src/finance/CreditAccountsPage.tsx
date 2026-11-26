import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchCreditAccounts, sendCreditAccount, type CreditAccount, type CreditAttempt, type CreditCommandOutcome } from './creditAccountApi'
import { CreditAccountProposalForm } from './CreditAccountProposalForm'
import { CreditMovementsPanel } from './CreditMovementsPanel'
import { currencyShown } from './currencyCodes'
import { CREDIT_TEXT as T } from './creditAccountText'
import styles from './Finance.module.css'

// Unknown writes survive route remounts in this tab, scoped to the exact actor/session/company namespace.
const unresolved = new Map<string, CreditAttempt>()

export function CreditAccountsPage() {
  const shell = useShellState(); const context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  return <Workspace key={`${namespace}:${shell.routeEntryGeneration}`} namespace={namespace} />
}

function Workspace({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(); const boundary = useAuthBoundary()
  const company = shell.activeContext?.companyId ?? ''
  const single = namespace !== null && company !== '' && shell.activeContext?.scopeMode === 'single'
  const [creating, setCreating] = useState(false); const [selected, setSelected] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<CreditAttempt | null>(() => namespace === null ? null : unresolved.get(namespace) ?? null)
  const [outcome, setOutcome] = useState<CreditCommandOutcome | null>(() => namespace !== null && unresolved.has(namespace) ? { type: 'unknown' } : null)
  const [sending, setSending] = useState(false); const [reason, setReason] = useState('')
  const mounted = useRef(true); const inFlight = useRef(false)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'credit-accounts'], enabled: single,
    queryFn: async ({ signal }) => { const stamp = boundary.capture(); const answer = await fetchCreditAccounts(company, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, stamp)
      return answer } })
  const frozen = sending || outcome?.type === 'unknown'
  const send = async (next: CreditAttempt, retry = false) => {
    if (!single || inFlight.current || outcome?.type === 'unknown' && !retry) return
    inFlight.current = true; setSending(true); setAttempt(next); setOutcome(null); unresolved.set(namespace!, next)
    try { const stamp = boundary.capture(); const result = await sendCreditAccount(company, next)
      if (result.type === 'refused' && (result.status === 401 || result.status === 403)) boundary.report(result.status, stamp)
      const unresolvedRetry = retry && result.type === 'refused' && [400, 401, 403, 404, 415].includes(result.status)
      if (result.type !== 'unknown' && !unresolvedRetry) unresolved.delete(namespace!)
      if (mounted.current) { setOutcome(unresolvedRetry ? { type: 'unknown' } : result)
        if (result.type === 'applied') { setCreating(false); setSelected(result.value.id); setReason(''); void query.refetch() }
        if (result.type === 'refused' && result.status === 409) void query.refetch()
      }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<readonly CreditAccount[]>
  const row = state.kind === 'ready' ? state.data.find(item => item.id === selected) : undefined
  return <section className={styles.page} aria-labelledby="credits-title"><h1 id="credits-title">{T.title}</h1><p>{T.intro}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /> : <>
      {hasCapability(shell, 'finance.credits.propose') ? <button type="button" disabled={frozen} onClick={() => { setCreating(value => !value); setOutcome(null) }}>{creating ? T.list : T.create}</button> : null}
      {creating ? <CreditAccountProposalForm company={company} namespace={namespace!} frozen={frozen} send={value => void send(value)} />
        : state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} /> : <>
          <button type="button" disabled={frozen} onClick={() => void query.refetch()}>{T.refresh}</button>
          {state.data.length === 0 ? <p>{T.empty}</p> : <div className={styles.tableWrap}><table aria-label={T.list} className={styles.table}><thead><tr>
            <th scope="col">{T.contract}</th><th scope="col">{T.sourceName}</th><th scope="col">{T.group}</th><th scope="col">{T.principal}</th><th scope="col">{T.select}</th></tr></thead><tbody>
              {state.data.map(item => <tr key={item.id}><td>{item.contractReference}</td><td>{item.lenderName}</td><td>{T.groups[item.group]}</td>
                <td>{item.principalAmount} · {currencyShown(item.currencyId, null)}</td><td><button type="button" disabled={frozen} onClick={() => { setSelected(item.id); setReason(''); setOutcome(null) }}>{T.select}</button></td></tr>)}
            </tbody></table></div>}
          {selected !== null && row === undefined ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} /> : null}
          {row !== undefined ? <><h2>{row.contractReference} · {T.statuses[row.status]}</h2>
            <dl><dt>{T.group}</dt><dd>{T.groups[row.group]}</dd><dt>{T.sourceName}</dt><dd>{row.lenderName}</dd>
              <dt>{T.principal}</dt><dd>{row.principalAmount} · {currencyShown(row.currencyId, null)}</dd><dt>{T.start}</dt><dd>{row.startDate}</dd>
              {row.profile ? <><dt>{T.source}</dt><dd>{row.profile.sourceDocument.reference} · {row.profile.sourceDocument.revision}</dd>
                <dt>{T.documentDate}</dt><dd>{row.profile.sourceDocument.documentDate}</dd><dt>{T.plan}</dt><dd>{row.profile.planNature === 'Installments' ? T.installments : T.movements}</dd>
                {row.profile.cash ? <><dt>{T.kind}</dt><dd>{T.cashKinds[row.profile.cash.kind as keyof typeof T.cashKinds]}</dd><dt>{T.maturity}</dt><dd>{row.profile.cash.maturityDate ?? '—'}</dd></> : null}
                {row.profile.nonCash ? <><dt>{T.kind}</dt><dd>{T.nonCashKinds[row.profile.nonCash.kind as keyof typeof T.nonCashKinds]}</dd><dt>{T.beneficiary}</dt><dd>{row.profile.nonCash.beneficiaryReference}</dd>
                  <dt>{T.expiry}</dt><dd>{row.profile.nonCash.isIndefinite ? T.indefinite : row.profile.nonCash.expiryDate}</dd></> : null}
                {row.profile.instrumentAndFactoring ? <><dt>{T.kind}</dt><dd>{T.instrumentKinds[row.profile.instrumentAndFactoring.kind as keyof typeof T.instrumentKinds]}</dd>
                  {row.profile.instrumentAndFactoring.withRecourse !== null ? <><dt>{T.recourse}</dt><dd>{row.profile.instrumentAndFactoring.withRecourse ? T.withRecourse : T.withoutRecourse}</dd></> : null}
                  <dt>{T.receivables}</dt><dd>{row.profile.instrumentAndFactoring.receivableDocumentReferences.join(' · ')}</dd></> : null}
                {row.profile.leasing ? <><dt>{T.asset}</dt><dd>{row.profile.leasing.assetReference}</dd><dt>{T.leaseStart}</dt><dd>{row.profile.leasing.leaseStartDate}</dd><dt>{T.leaseEnd}</dt><dd>{row.profile.leasing.leaseEndDate}</dd></> : null}
              </> : null}
              {row.tolerance ? <><dt>{T.tolerance}</dt><dd>{row.tolerance.smallestCurrencyUnit}</dd></> : null}
            </dl>
            {row.profile === null ? <p>{T.legacy}</p> : null}
            {row.installments.length > 0 ? <div className={styles.tableWrap}><table aria-label={`${T.installments} · ${row.contractReference}`} className={styles.table}><thead><tr>{T.columns.map(label => <th scope="col" key={label}>{label}</th>)}<th scope="col">{T.vat}</th><th scope="col">{T.fee}</th></tr></thead>
              <tbody>{row.installments.map(line => <tr key={line.sequence}><td>{line.sequence}</td><td>{line.dueDate}</td>
                {['installment', 'principal', 'interest', 'bsmv', 'kkdf'].map(key => <td key={key}>{line[key as 'installment']}</td>)}
                <td>{line.vat ?? '—'}</td><td>{line.charges?.map((charge, index) => <p key={index}>{charge.description}: {charge.amount} · {charge.treatment === 'IncludedInInstallment' ? T.included : T.separate} · {charge.sourceDocumentReference}</p>)}</td></tr>)}</tbody></table></div> : null}
            {row.planTotals ? <p>{T.totals}: {row.planTotals.installment} / {row.planTotals.principal} / {row.planTotals.interest} / {row.planTotals.bsmv} / {row.planTotals.kkdf}
              {row.planTotals.vat !== undefined ? <> · {T.vat}: {row.planTotals.vat}</> : null}
              {row.planTotals.charges ? <> · {T.feesIncluded}: {row.planTotals.charges.includedInInstallment} · {T.feesSeparate}: {row.planTotals.charges.collectedSeparately}</> : null}</p> : null}
            {row.status === 'Proposed' && hasCapability(shell, 'finance.credits.approve') ? row.isOwnProposal ? <p>{T.own}</p> : <fieldset disabled={frozen}>
              <button type="button" disabled={row.profile === null} onClick={() => void send({ operation: 'approve', key: crypto.randomUUID(), id: row.id, request: { expectedVersion: row.version } })}>{T.approve}</button>
              <label>{T.reason}<textarea value={reason} maxLength={500} onChange={event => setReason(event.target.value)} /></label>
              <button type="button" disabled={reason.trim() === ''} onClick={() => void send({ operation: 'reject', key: crypto.randomUUID(), id: row.id, request: { expectedVersion: row.version, reason: reason.trim() } })}>{T.reject}</button>
            </fieldset> : null}
            {row.status === 'Approved' ? <CreditMovementsPanel key={row.id} account={row} namespace={namespace!} /> : null}
          </> : null}
        </>}
    </>}
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button type="button" disabled={sending || attempt === null} onClick={() => attempt && void send(attempt, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'refused' ? <div role="alert"><p>{T.refused} ({outcome.status})</p><ul>{outcome.issues.map((issue, index) => <li key={index}>{issue.field}: {issue.code}</li>)}</ul></div> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
