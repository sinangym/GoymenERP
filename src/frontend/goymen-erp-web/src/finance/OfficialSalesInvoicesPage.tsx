import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { fetchCounterpartyDetail } from '../cari/detailApi'
import type { CounterpartyDetail } from '../cari/detailContract'
import { continueOfficialInvoice, decideOfficialInvoice, fetchOfficialInvoices, fetchOfficialInvoiceCurrencies, OFFICIAL_AMOUNT_FIELDS, OFFICIAL_INVOICE_OWNER,
  type OfficialInvoiceCreateAttempt, type OfficialInvoiceCurrency, type OfficialInvoiceDecisionAttempt, type OfficialInvoicePage, type OfficialInvoiceWriteOutcome } from './officialSalesInvoiceApi'
import { OfficialSalesInvoiceForm } from './OfficialSalesInvoiceForm'
import { OFFICIAL_SALES_INVOICE_TEXT as T } from './officialSalesInvoiceText'
import styles from './Finance.module.css'

type Attempt = { readonly kind: 'create'; readonly value: OfficialInvoiceCreateAttempt } | { readonly kind: 'decision'; readonly value: OfficialInvoiceDecisionAttempt }
// This tab retains exact bytes/keys and the uploaded file for an unresolved operation. No browser storage or new identity on retry.
const unresolved = new Map<string, { readonly attempt: Attempt; readonly waiting: boolean }>()
export function OfficialSalesInvoicesPage() {
  const shell = useShellState(); const key = contextKey(shell); const namespace = key === null ? null : contextKeyString(key)
  return <Workspace key={`${namespace}:${shell.routeEntryGeneration}`} namespace={namespace} />
}
function Workspace({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const company = shell.activeContext?.companyId ?? ''; const actor = shell.activeContext?.userId ?? ''
  const single = namespace !== null && company !== '' && actor !== '' && shell.activeContext?.scopeMode === 'single'
  const previous = namespace === null ? undefined : unresolved.get(namespace)
  const [attempt, setAttempt] = useState<Attempt | null>(previous?.attempt ?? null)
  const [outcome, setOutcome] = useState<OfficialInvoiceWriteOutcome | null>(previous ? { type: previous.waiting ? 'waiting-scan' : 'unknown' } : null)
  const [creating, setCreating] = useState(false); const [pendingOnly, setPendingOnly] = useState(false); const [cursor, setCursor] = useState<string | null>(null)
  const [selected, setSelected] = useState<string | null>(null); const [reason, setReason] = useState(''); const [sending, setSending] = useState(false)
  const mounted = useRef(true); const inFlight = useRef(false)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'official-sales-invoices', pendingOnly, cursor], enabled: single,
    queryFn: async ({ signal }) => { const stamp = boundary.capture(); const answer = await fetchOfficialInvoices(company, pendingOnly, cursor, signal)
      if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, stamp)
      return answer } })
  const frozen = sending || outcome?.type === 'unknown' || outcome?.type === 'waiting-scan'
  const send = async (next: Attempt, retry = false) => {
    if (!single || inFlight.current || frozen && !retry) return
    inFlight.current = true; setSending(true); setAttempt(next); setOutcome(null); unresolved.set(namespace!, { attempt: next, waiting: false })
    try { const stamp = boundary.capture(); const answer = next.kind === 'create' ? await continueOfficialInvoice(next.value) : await decideOfficialInvoice(company, actor, next.value)
      if (answer.type === 'refused' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, stamp)
      // A denial of a retry cannot prove that an earlier lost mutation failed.
      const stillUnknown = retry && answer.type === 'refused' && [400, 401, 403, 404, 415].includes(answer.status)
      const value: OfficialInvoiceWriteOutcome = stillUnknown ? { type: 'unknown' } : answer
      if (value.type === 'unknown' || value.type === 'waiting-scan') unresolved.set(namespace!, { attempt: next, waiting: value.type === 'waiting-scan' })
      else unresolved.delete(namespace!)
      if (mounted.current) { setOutcome(value)
        if (value.type === 'applied') { setCreating(false); setSelected(value.value.invoiceId); setReason(''); void query.refetch() }
        if (value.type === 'refused' && value.status === 409) void query.refetch()
      }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<OfficialInvoicePage>
  const row = state.kind === 'ready' ? state.data.items.find(x => x.invoiceId === selected) : undefined
  const party = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'official-invoice-party', row?.customerPartyId], enabled: single && row !== undefined,
    queryFn: async ({ signal }) => { const stamp = boundary.capture(); const answer = await fetchCounterpartyDetail(row!.customerPartyId, company, signal)
      if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, stamp)
      return answer } })
  const currency = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'official-invoice-source-currencies'], enabled: single && row !== undefined,
    queryFn: async ({ signal }) => { const stamp = boundary.capture(); const answer = await fetchOfficialInvoiceCurrencies(company, signal)
      if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, stamp)
      return answer } })
  const partyState = (party.isPending ? { kind: 'initial-loading' } : classifyOutcome(party.data ?? { type: 'malformed' })) as RemoteViewState<CounterpartyDetail>
  const currencyState = (currency.isPending ? { kind: 'initial-loading' } : classifyOutcome(currency.data ?? { type: 'malformed' })) as RemoteViewState<readonly OfficialInvoiceCurrency[]>
  const currentParty = partyState.kind === 'ready' && partyState.data.id === row?.customerPartyId && partyState.data.account.companyId === company ? partyState.data : null
  const currentCurrency = currencyState.kind === 'ready' ? currencyState.data.find(x => x.currencyId === row?.currencyId) : null
  const sourceIdentityReady = currentParty != null && currentCurrency != null
  const decide = (operation: OfficialInvoiceDecisionAttempt['operation']) => { if (row) void send({ kind: 'decision', value: {
    key: crypto.randomUUID(), operation, invoiceId: row.invoiceId, expectedVersion: row.version, ...(operation === 'rejection' ? { reason: reason.trim() } : {}) } }) }
  return <section className={styles.page} aria-labelledby="official-invoices-title"><h1 id="official-invoices-title">{T.title}</h1><p>{T.intro}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.title} /> : <>
      {hasCapability(shell, 'finance.official-sales-invoices.record') ? <button type="button" disabled={frozen} onClick={() => setCreating(x => !x)}>{creating ? T.cancel : T.create}</button> : null}
      {creating ? <OfficialSalesInvoiceForm company={company} actor={actor} namespace={namespace!} frozen={frozen} send={value => void send({ kind: 'create', value })} /> : <>
        <label><input type="checkbox" disabled={frozen} checked={pendingOnly} onChange={e => { setPendingOnly(e.target.checked); setCursor(null); setSelected(null) }} />{T.pendingOnly}</label>
        {state.kind !== 'ready' ? <StateBlock state={state} subject={T.title} onRetry={() => void query.refetch()} /> : <>
          {state.data.items.length === 0 ? <p>{T.noRows}</p> : <div className={styles.tableWrap}><table aria-labelledby="official-invoices-title" className={styles.table}><thead><tr><th scope="col">{T.officialNo}</th><th scope="col">{T.date}</th><th scope="col">{T.status}</th><th scope="col">{T.select}</th></tr></thead><tbody>
            {state.data.items.map(item => <tr key={item.invoiceId}><td>{item.officialNo}</td><td>{item.invoiceDate ?? T.sourceDateMissing}</td><td>{T.statuses[item.status]}</td><td><button type="button" disabled={frozen} onClick={() => { setSelected(item.invoiceId); setReason('') }}>{T.select}</button></td></tr>)}
          </tbody></table></div>}
          <button type="button" disabled={frozen || cursor === null} onClick={() => { setCursor(null); setSelected(null) }}>{T.first}</button>
          <button type="button" disabled={frozen || state.data.nextCursor === null} onClick={() => { setCursor(state.data.nextCursor); setSelected(null) }}>{T.next}</button>
        </>}
        {selected !== null && state.kind === 'ready' && row === undefined ? <StateBlock state={{ kind: 'not-found' }} subject={T.title} /> : null}
        {row ? <section className={styles.detail} aria-label={row.officialNo}><h2>{row.officialNo} · {T.statuses[row.status]}</h2><dl>
          <dt>{T.customerCurrent}</dt><dd>{currentParty ? `${currentParty.code} · ${currentParty.legalName}` : <StateBlock state={partyState.kind === 'ready' ? { kind: 'not-found' } : partyState} subject={T.customer} onRetry={() => void party.refetch()} />}</dd>
          <dt>{T.currency}</dt><dd>{currentCurrency ? `${currentCurrency.code} · ${currentCurrency.displayLabel}` : <StateBlock state={currencyState.kind === 'ready' ? { kind: 'not-found' } : currencyState} subject={T.currency} onRetry={() => void currency.refetch()} />}</dd>
          <dt>{T.ettn}</dt><dd>{row.ettn}</dd><dt>{T.date}</dt><dd>{row.invoiceDate ?? T.sourceDateMissing}</dd>
          {OFFICIAL_AMOUNT_FIELDS.map(k => <div key={k}><dt>{T[k]}</dt><dd>{row[k]}</dd></div>)}
        </dl>
          {row.sourceDifferences?.length ? <aside><p>{T.differences}</p><ul>{row.sourceDifferences.map((difference, index) => <li key={`${difference.importBatchId}:${index}`}>
            {difference.officialNo} · {difference.fields.map(field => T.sourceFields[field as keyof typeof T.sourceFields] ?? T.sourceDifference).join(', ')}
          </li>)}</ul></aside> : null}
          {row.taxWarning ? <aside role="note" aria-label={T.taxWarningCode}><p>{T.taxWarning}</p><dl>
            <dt>{T.taxWarningExpected}</dt><dd>{row.taxWarning.expectedPayableFunctionalAmount}</dd>
            <dt>{T.taxWarningPayable}</dt><dd>{row.taxWarning.payableFunctionalAmount}</dd>
            <dt>{T.taxWarningDifference}</dt><dd>{row.taxWarning.differenceFunctionalAmount}</dd>
            <dt>{T.taxWarningCode}</dt><dd>{row.taxWarning.code}</dd>
          </dl></aside> : null}
          <p>{T.pinnedPdf} {row.pdfDocumentVersionNo}</p>
          <DocumentsPanel ownerKind={OFFICIAL_INVOICE_OWNER} ownerId={row.invoiceId} companyId={company} namespace={namespace!} entryGeneration={shell.routeEntryGeneration} heading={T.pdf} />
          {row.status === 'PendingApproval' ? <div className={styles.actions}>
            {hasCapability(shell, 'finance.official-sales-invoices.approve') ? row.recordedBy === actor ? <p>{T.own}</p> : <>
              <button type="button" disabled={frozen || !sourceIdentityReady} onClick={() => decide('approval')}>{T.approve}</button>
              <label>{T.reason}<input maxLength={1000} disabled={frozen} value={reason} onChange={e => setReason(e.target.value)} /></label>
              <button type="button" disabled={frozen || reason.trim() === ''} onClick={() => decide('rejection')}>{T.reject}</button>
            </> : null}
            {hasCapability(shell, 'finance.official-sales-invoices.withdraw') && row.recordedBy === actor ? <button type="button" disabled={frozen} onClick={() => decide('withdrawal')}>{T.withdraw}</button> : null}
          </div> : null}
        </section> : null}
      </>}
      {outcome?.type === 'applied' ? <p role="status">{T.done}</p> : null}
      {outcome?.type === 'unknown' || outcome?.type === 'waiting-scan' ? <div role="alert"><p>{outcome.type === 'unknown' ? T.unknown : T.waitingScan}</p>
        <button type="button" disabled={sending || attempt === null} onClick={() => attempt && void send(attempt, true)}>{outcome.type === 'unknown' ? T.retry : T.scanRetry}</button></div> : null}
      {outcome?.type === 'refused' ? <div role="alert"><p>{T.refused}</p>{outcome.existingOfficialNo ? <p>{T.duplicatePdf} {outcome.existingOfficialNo}</p> : null}</div> : null}
    </>}
  </section>
}
