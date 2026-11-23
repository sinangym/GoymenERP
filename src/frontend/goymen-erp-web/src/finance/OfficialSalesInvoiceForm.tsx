import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchCounterparties } from '../cari/api'
import type { CounterpartyListItem, CounterpartyPage } from '../cari/contract'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { isBoundUuid } from './contract'
import { parseNonNegativeAmountInput } from './decimalText'
import { fetchOfficialInvoiceCurrencies, OFFICIAL_AMOUNT_FIELDS, type OfficialAmountField, type OfficialInvoiceCreateAttempt, type OfficialInvoiceCurrency } from './officialSalesInvoiceApi'
import { OFFICIAL_SALES_INVOICE_TEXT as T } from './officialSalesInvoiceText'
import styles from './Finance.module.css'

export function OfficialSalesInvoiceForm({ company, actor, namespace, frozen, send }: {
  readonly company: string; readonly actor: string; readonly namespace: string; readonly frozen: boolean
  readonly send: (attempt: OfficialInvoiceCreateAttempt) => void
}) {
  const boundary = useAuthBoundary()
  const [search, setSearch] = useState(''); const [queryText, setQueryText] = useState(''); const [cursor, setCursor] = useState<string | null>(null)
  const [party, setParty] = useState<CounterpartyListItem | null>(null); const [currency, setCurrency] = useState('')
  const [officialNo, setOfficialNo] = useState(''); const [ettn, setEttn] = useState(''); const [invoiceDate, setInvoiceDate] = useState('')
  const [amounts, setAmounts] = useState<Record<OfficialAmountField, string>>({ payableAmount: '', payableFunctionalAmount: '', taxBaseFunctionalAmount: '', vatFunctionalAmount: '', withholdingFunctionalAmount: '' })
  const [file, setFile] = useState<File | null>(null)
  const parties = useQuery({ queryKey: [namespace, 'official-invoice-parties', queryText, cursor], queryFn: async ({ signal }) => {
    const stamp = boundary.capture(); const answer = await fetchCounterparties({ mode: 'single', companyId: company },
      { q: queryText, partyState: 'all', accountState: 'all', pageSize: 30, ...(cursor === null ? {} : { cursor }) }, signal)
    if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, stamp)
    return answer
  } })
  const currencies = useQuery({ queryKey: [namespace, 'official-invoice-currencies'], queryFn: async ({ signal }) => {
    const stamp = boundary.capture(); const answer = await fetchOfficialInvoiceCurrencies(company, signal)
    if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, stamp)
    return answer
  } })
  const partyState = (parties.isPending ? { kind: 'initial-loading' } : classifyOutcome(parties.data ?? { type: 'malformed' })) as RemoteViewState<CounterpartyPage>
  const currencyState = (currencies.isPending ? { kind: 'initial-loading' } : classifyOutcome(currencies.data ?? { type: 'malformed' })) as RemoteViewState<readonly OfficialInvoiceCurrency[]>
  const valid = party?.companyAccounts.some(x => x.companyId === company) && currencyState.kind === 'ready'
    && currencyState.data.some(x => x.currencyId === currency) && officialNo.trim() !== '' && isBoundUuid(ettn.trim()) && invoiceDate !== ''
    && OFFICIAL_AMOUNT_FIELDS.every(k => parseNonNegativeAmountInput(amounts[k]).ok) && file !== null && file.type === 'application/pdf' && file.size > 0
  const submit = (event: FormEvent) => {
    event.preventDefault(); if (frozen || !valid || party === null || file === null) return
    const parsed = OFFICIAL_AMOUNT_FIELDS.map(k => [k, parseNonNegativeAmountInput(amounts[k])] as const)
    if (parsed.some(([, value]) => !value.ok)) return
    const sourceAmounts = Object.fromEntries(parsed.map(([key, value]) => [key, value.ok ? value.wire : ''])) as Record<OfficialAmountField, string>
    send({ company, actor, input: { ...sourceAmounts, officialNo: officialNo.trim(), ettn: ettn.trim(), customerPartyId: party.id, currencyId: currency, invoiceDate },
      file, reserveKey: crypto.randomUUID(), uploadKey: crypto.randomUUID(), recordKey: crypto.randomUUID(), invoiceId: null, documentId: null, phase: 'reserve' })
  }
  return <form onSubmit={submit}><fieldset disabled={frozen} className={styles.form}>
    <legend>{T.create}</legend>
    <label>{T.search}<input value={search} onChange={e => setSearch(e.target.value)} /></label>
    <button type="button" onClick={() => { setQueryText(search); setCursor(null); setParty(null); void parties.refetch() }}>{T.searchButton}</button>
    {partyState.kind !== 'ready' ? <StateBlock state={partyState} subject={T.customer} onRetry={() => void parties.refetch()} /> : <>
      <label>{T.customer}<select required value={party?.id ?? ''} onChange={e => setParty(partyState.data.items.find(x => x.id === e.target.value) ?? null)}>
        <option value="">{T.select}</option>{party && !partyState.data.items.some(x => x.id === party.id) ? <option value={party.id}>{party.code} · {party.legalName}</option> : null}
        {partyState.data.items.map(x => <option key={x.id} value={x.id}>{x.code} · {x.legalName}</option>)}</select></label>
      {partyState.data.hasMore ? <button type="button" onClick={() => setCursor(partyState.data.nextCursor)}>{T.next}</button> : null}
    </>}
    {currencyState.kind !== 'ready' ? <StateBlock state={currencyState} subject={T.currency} onRetry={() => void currencies.refetch()} /> : <label>{T.currency}
      <select required value={currency} onChange={e => setCurrency(e.target.value)}><option value="">{T.select}</option>{currencyState.data.map(x => <option key={x.currencyId} value={x.currencyId}>{x.code} · {x.displayLabel}</option>)}</select></label>}
    <label>{T.officialNo}<input required maxLength={128} value={officialNo} onChange={e => setOfficialNo(e.target.value)} /></label>
    <label>{T.ettn}<input required maxLength={36} value={ettn} onChange={e => setEttn(e.target.value)} /></label>
    <label>{T.date}<input required type="date" value={invoiceDate} onChange={e => setInvoiceDate(e.target.value)} /></label>
    {OFFICIAL_AMOUNT_FIELDS.map(key => <label key={key}>{T[key]}<input required inputMode="decimal" maxLength={80} value={amounts[key]}
      onChange={e => setAmounts(previous => ({ ...previous, [key]: e.target.value }))} /></label>)}
    <label>{T.pdf}<input type="file" required accept="application/pdf,.pdf" onChange={e => setFile(e.target.files?.[0] ?? null)} /></label>
    <button type="submit" disabled={!valid}>{T.save}</button>
  </fieldset></form>
}
