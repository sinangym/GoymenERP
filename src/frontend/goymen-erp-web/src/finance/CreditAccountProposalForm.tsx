import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchCounterparties } from '../cari/api'
import type { CounterpartyListItem, CounterpartyPage } from '../cari/contract'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { CustomerInstrumentPage } from './customerInstrumentContract'
import { fetchCreditCurrencies, fetchCreditInstruments, parseCreditPlanFile, type CreditAttempt, type CreditCurrency, type CreditGroup, type CreditInstallment, type CreditProfile } from './creditAccountApi'
import { CREDIT_TEXT as T } from './creditAccountText'
import styles from './Finance.module.css'

interface Props { readonly company: string; readonly namespace: string; readonly frozen: boolean; readonly send: (attempt: CreditAttempt) => void }
export function CreditAccountProposalForm(props: Props) {
  const [group, setGroup] = useState<CreditGroup | ''>('')
  return <><label>{T.group}<select disabled={props.frozen} value={group} onChange={event => setGroup(event.target.value as CreditGroup | '')}>
    <option value="">{T.choose}</option>{Object.entries(T.groups).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
    {group !== '' ? <Form key={group} {...props} group={group} /> : null}</>
}
function Form({ company, namespace, frozen, send, group }: Props & { readonly group: CreditGroup }) {
  const boundary = useAuthBoundary(); const [search, setSearch] = useState(''); const [submittedSearch, setSubmittedSearch] = useState('')
  const [cursor, setCursor] = useState<string | null>(null); const [selectedParty, setSelectedParty] = useState<CounterpartyListItem | null>(null)
  const [lenderName, setLenderName] = useState(''); const [contract, setContract] = useState(''); const [currency, setCurrency] = useState('')
  const [principal, setPrincipal] = useState(''); const [start, setStart] = useState(''); const [reference, setReference] = useState('')
  const [documentDate, setDocumentDate] = useState(''); const [revision, setRevision] = useState('')
  const [nature, setNature] = useState<CreditProfile['planNature'] | ''>(group === 'NonCash' ? 'SourceMovements' : '')
  const [kind, setKind] = useState(''); const [maturity, setMaturity] = useState(''); const [beneficiary, setBeneficiary] = useState('')
  const [indefinite, setIndefinite] = useState(false); const [expiry, setExpiry] = useState(''); const [recourse, setRecourse] = useState('')
  const [receivables, setReceivables] = useState(''); const [asset, setAsset] = useState(''); const [leaseStart, setLeaseStart] = useState(''); const [leaseEnd, setLeaseEnd] = useState('')
  const [instrumentCursor, setInstrumentCursor] = useState<string | null>(null); const [instruments, setInstruments] = useState<Readonly<Record<string, string>>>({})
  const [lines, setLines] = useState<readonly CreditInstallment[]>([]); const [fileError, setFileError] = useState(false)
  const fileGeneration = useRef(0)
  useEffect(() => { ++fileGeneration.current; return () => { ++fileGeneration.current } }, [frozen])
  const parties = useQuery({ queryKey: [namespace, 'credit-lenders', submittedSearch, cursor], queryFn: async ({ signal }) => {
    const stamp = boundary.capture(); const answer = await fetchCounterparties({ mode: 'single', companyId: company },
      { q: submittedSearch, partyState: 'active', accountState: 'active', pageSize: 30, ...(cursor === null ? {} : { cursor }) }, signal)
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, stamp)
    return answer
  } })
  const currencies = useQuery({ queryKey: [namespace, 'credit-currencies'], queryFn: async ({ signal }) => {
    const stamp = boundary.capture(); const answer = await fetchCreditCurrencies(company, signal)
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, stamp)
    return answer
  } })
  const instrumentQuery = useQuery({ queryKey: [namespace, 'credit-instruments', instrumentCursor], enabled: group === 'InstrumentAndFactoring' && kind !== 'Factoring',
    queryFn: async ({ signal }) => { const stamp = boundary.capture(); const answer = await fetchCreditInstruments(company, instrumentCursor, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, stamp)
      return answer } })
  const partyState = (parties.isPending ? { kind: 'initial-loading' } : classifyOutcome(parties.data ?? { type: 'malformed' })) as RemoteViewState<CounterpartyPage>
  const currencyState = (currencies.isPending ? { kind: 'initial-loading' } : classifyOutcome(currencies.data ?? { type: 'malformed' })) as RemoteViewState<readonly CreditCurrency[]>
  const instrumentState = (instrumentQuery.isPending ? { kind: 'initial-loading' } : classifyOutcome(instrumentQuery.data ?? { type: 'malformed' })) as RemoteViewState<CustomerInstrumentPage>
  const selectedAccount = selectedParty?.companyAccounts.find(account => account.companyId.toLowerCase() === company.toLowerCase() && account.isActive)
  const input = (label: string, value: string, change: (value: string) => void, type = 'text', required = true, maximum = 200) => <label>{label}<input type={type} required={required} maxLength={maximum} value={value} onChange={event => change(event.target.value)} /></label>
  const instrumentValid = group !== 'InstrumentAndFactoring' || (kind === 'Factoring'
    ? recourse !== '' && receivables.trim() !== '' : Object.keys(instruments).length > 0)
  const valid = selectedAccount !== undefined && currencyState.kind === 'ready' && currencyState.data.some(item => item.currencyId === currency)
    && nature !== '' && (nature !== 'Installments' || lines.length > 0)
    && instrumentValid
  const submit = (event: FormEvent) => {
    event.preventDefault(); if (!valid || frozen || !selectedParty || !selectedAccount) return
    const profile: CreditProfile = { lenderPartyId: selectedParty.id, lenderCompanyPartyAccountId: selectedAccount.id,
      sourceDocument: { reference: reference.trim(), documentDate, revision: revision.trim() }, planNature: nature,
      ...(group === 'Cash' ? { cash: { kind, maturityDate: maturity || null } } : {}),
      ...(group === 'NonCash' ? { nonCash: { kind, beneficiaryReference: beneficiary.trim(), isIndefinite: indefinite, expiryDate: indefinite ? null : expiry } } : {}),
      ...(group === 'InstrumentAndFactoring' ? { instrumentAndFactoring: { kind, withRecourse: kind === 'Factoring' ? recourse === 'yes' : null,
        instrumentIds: kind === 'Factoring' ? [] : Object.keys(instruments), receivableDocumentReferences: kind === 'Factoring' ? receivables.split('\n').map(item => item.trim()).filter(Boolean) : [] } } : {}),
      ...(group === 'Leasing' ? { leasing: { assetReference: asset.trim(), leaseStartDate: leaseStart, leaseEndDate: leaseEnd } } : {}),
    }
    send({ operation: 'propose', key: crypto.randomUUID(), request: { group, lenderName: lenderName.trim(), contractReference: contract.trim(),
      currencyId: currency, principalAmount: principal, startDate: start, installments: nature === 'Installments' ? lines : [], profile } })
  }
  const update = (index: number, patch: Partial<CreditInstallment>) => setLines(previous => previous.map((line, i) => i === index ? { ...line, ...patch } : line))
  const kindLabels = group === 'Cash' ? T.cashKinds : group === 'NonCash' ? T.nonCashKinds : T.instrumentKinds
  return <form onSubmit={submit}><fieldset disabled={frozen} className={styles.form}>
    <label>{T.search}<input value={search} onChange={event => setSearch(event.target.value)} /></label>
    <button type="button" onClick={() => { setSubmittedSearch(search); setCursor(null); setSelectedParty(null); void parties.refetch() }}>{T.searchButton}</button>
    {partyState.kind !== 'ready' ? <StateBlock state={partyState} subject={T.lender} onRetry={() => void parties.refetch()} /> : <>
      <label>{T.lender}<select required value={selectedParty?.id ?? ''} onChange={event => setSelectedParty(partyState.data.items.find(item => item.id === event.target.value) ?? null)}>
        <option value="">{T.choose}</option>{selectedParty && !partyState.data.items.some(item => item.id === selectedParty.id) ? <option value={selectedParty.id}>{selectedParty.code} · {selectedParty.legalName}</option> : null}
        {partyState.data.items.map(item => <option key={item.id} value={item.id}>{item.code} · {item.legalName}</option>)}</select></label>
      {partyState.data.hasMore ? <button type="button" onClick={() => setCursor(partyState.data.nextCursor)}>{T.more}</button> : null}
    </>}
    {input(T.sourceName, lenderName, setLenderName)}{input(T.contract, contract, setContract, 'text', true, 100)}
    {currencyState.kind !== 'ready' ? <StateBlock state={currencyState} subject={T.currency} onRetry={() => void currencies.refetch()} />
      : currencyState.data.length === 0 ? <p>{T.noCurrency}</p> : <label>{T.currency}<select value={currency} required onChange={event => setCurrency(event.target.value)}>
        <option value="">{T.choose}</option>{currencyState.data.map(item => <option key={item.currencyId} value={item.currencyId}>{item.code} · {item.displayLabel}</option>)}</select></label>}
    {input(T.principal, principal, setPrincipal)}{input(T.start, start, setStart, 'date')}
    {input(T.source, reference, setReference, 'text', true, 100)}{input(T.documentDate, documentDate, setDocumentDate, 'date')}{input(T.revision, revision, setRevision, 'text', true, 100)}
    {group !== 'Leasing' ? <label>{T.kind}<select value={kind} required onChange={event => { setKind(event.target.value); setInstruments({}) }}><option value="">{T.choose}</option>
      {Object.entries(kindLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label> : null}
    {group === 'Cash' ? input(T.maturity, maturity, setMaturity, 'date', false) : null}
    {group === 'NonCash' ? <>{input(T.beneficiary, beneficiary, setBeneficiary)}<label><input type="checkbox" checked={indefinite} onChange={event => setIndefinite(event.target.checked)} />{T.indefinite}</label>
      {!indefinite ? input(T.expiry, expiry, setExpiry, 'date') : null}</> : null}
    {group === 'Leasing' ? <>{input(T.asset, asset, setAsset)}{input(T.leaseStart, leaseStart, setLeaseStart, 'date')}{input(T.leaseEnd, leaseEnd, setLeaseEnd, 'date')}</> : null}
    {group === 'InstrumentAndFactoring' && kind === 'Factoring' ? <><label>{T.recourse}<select required value={recourse} onChange={event => setRecourse(event.target.value)}><option value="">{T.choose}</option><option value="yes">{T.withRecourse}</option><option value="no">{T.withoutRecourse}</option></select></label>
      <label>{T.receivables}<textarea required value={receivables} onChange={event => setReceivables(event.target.value)} /></label></> : null}
    {group === 'InstrumentAndFactoring' && kind !== '' && kind !== 'Factoring' ? <fieldset><legend>{T.instruments}</legend>
      {instrumentState.kind !== 'ready' ? <StateBlock state={instrumentState} subject={T.instruments} onRetry={() => void instrumentQuery.refetch()} /> : <>
        {instrumentState.data.items.length === 0 ? <p>{T.noInstruments}</p> : instrumentState.data.items.map(item => <label key={item.id}><input type="checkbox" checked={instruments[item.id] !== undefined}
          onChange={event => setInstruments(previous => { const next = { ...previous }; if (event.target.checked) next[item.id] = item.serialNumber; else delete next[item.id]; return next })} />{item.serialNumber} · {item.drawer} · {item.dueOn}</label>)}
        {instrumentState.data.nextCursor ? <button type="button" onClick={() => setInstrumentCursor(instrumentState.data.nextCursor)}>{T.instrumentMore}</button> : null}
      </>}{Object.values(instruments).map(serial => <p key={serial}>{serial}</p>)}
    </fieldset> : null}
    {group !== 'NonCash' ? <label>{T.plan}<select required value={nature} onChange={event => { setNature(event.target.value as typeof nature); setLines([]) }}><option value="">{T.choose}</option><option value="Installments">{T.installments}</option><option value="SourceMovements">{T.movements}</option></select></label> : null}
    {nature === 'Installments' ? <><p>{T.sourcePlanNotice}</p><p>{T.fileHint}</p><label>{T.file}<input type="file" accept=".csv,text/csv" onChange={async event => {
      const file = event.target.files?.[0]; const stamp = ++fileGeneration.current; if (!file) return
      try { const parsed = file.size > 1_000_000 ? null : parseCreditPlanFile(await file.text()); if (stamp !== fileGeneration.current) return
        setFileError(parsed === null); if (parsed !== null) setLines(parsed)
      } catch { if (stamp === fileGeneration.current) setFileError(true) }
    }} /></label>{fileError ? <p role="alert">{T.fileInvalid}</p> : null}
      {lines.map((line, index) => <fieldset key={index}><legend>{T.columns[0]} {line.sequence}</legend>
        {input(T.columns[1], line.dueDate, value => update(index, { dueDate: value }), 'date')}
        {(['installment', 'principal', 'interest', 'bsmv', 'kkdf'] as const).map((key, position) => <label key={key}>{T.columns[position + 2]}<input required inputMode="decimal" value={line[key]} onChange={event => update(index, { [key]: event.target.value })} /></label>)}
        {group === 'Leasing' ? <><p>{T.leasingTaxNotice}</p><label>{T.vat}<input inputMode="decimal" value={line.vat ?? ''} onChange={event => {
          const value = event.target.value
          if (value === '') setLines(previous => previous.map((item, i) => { if (i !== index) return item; const next = { ...item }; delete next.vat; return next }))
          else update(index, { vat: value })
        }} /></label></> : null}
        {line.charges?.map((charge, chargeIndex) => <fieldset key={chargeIndex}><legend>{T.fee}</legend>
          {input(T.feeDescription, charge.description, value => update(index, { charges: line.charges!.map((item, i) => i === chargeIndex ? { ...item, description: value } : item) }))}
          {input(T.feeAmount, charge.amount, value => update(index, { charges: line.charges!.map((item, i) => i === chargeIndex ? { ...item, amount: value } : item) }))}
          {input(T.source, charge.sourceDocumentReference, value => update(index, { charges: line.charges!.map((item, i) => i === chargeIndex ? { ...item, sourceDocumentReference: value } : item) }))}
          <label>{T.treatment}<select required value={charge.treatment} onChange={event => update(index, { charges: line.charges!.map((item, i) => i === chargeIndex ? { ...item, treatment: event.target.value as typeof charge.treatment } : item) })}><option value="">{T.choose}</option><option value="IncludedInInstallment">{T.included}</option><option value="CollectedSeparately">{T.separate}</option></select></label>
          <button type="button" onClick={() => update(index, { charges: line.charges!.filter((_, i) => i !== chargeIndex) })}>{T.remove}</button>
        </fieldset>)}
        <button type="button" onClick={() => update(index, { charges: [...(line.charges ?? []), { description: '', amount: '', treatment: '' as 'CollectedSeparately', sourceDocumentReference: '' }] })}>{T.addFee}</button>
        <button type="button" onClick={() => setLines(previous => previous.filter((_, i) => i !== index).map((item, i) => ({ ...item, sequence: i + 1 })))}>{T.remove}</button>
      </fieldset>)}
      <button type="button" disabled={lines.length >= 600} onClick={() => setLines(previous => [...previous, { sequence: previous.length + 1, dueDate: '', installment: '', principal: '', interest: '', bsmv: '', kkdf: '' }])}>{T.add}</button>
    </> : null}
    <button type="submit" disabled={!valid}>{T.propose}</button>
  </fieldset></form>
}
