import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { commandFinancePolicy, postFinancePolicyRead } from './financePolicyApi'
import { fetchBankInstitutions, fetchCollateralRates } from './collateralPolicyApi'
import { decodeCollateralRatePreview, type BankInstitutionsWorkspace, type CollateralRatesWorkspace, type CollateralRateScope, type CollateralRatePreview } from './collateralPolicyContract'
import { FINANCE_POLICY_CAPS as C, type FinancePolicyHeader } from './financePolicyContract'
import { financePolicyPendingKey, recallFinancePolicy, reserveFinancePolicy, releaseFinancePolicy } from './pendingFinancePolicies'
import { FINANCE_POLICY_TEXT as T } from './financePolicyText'
import { COLLATERAL_POLICY_TEXT as K } from './collateralPolicyText'
import styles from './settings.module.css'
type Family = 'bank-institutions' | 'collateral-rates'
type BankInput = { readonly bankInstitutionId: string | null; readonly name: string; readonly eftCode: string; readonly active: boolean }
export function BankInstitutionsPage() { return <CollateralPoliciesPage family="bank-institutions" /> }
export function CollateralRatesPage() { return <CollateralPoliciesPage family="collateral-rates" /> }
export function CollateralPoliciesPage({ family }: { readonly family: Family }) {
  const shell = useShellState(); const context = contextKey(shell); const namespace = context === null ? null : contextKeyString(context)
  return <Screen key={`${namespace}:${shell.routeEntryGeneration}:${family}`} namespace={namespace} family={family} />
}
function Screen({ namespace, family }: { readonly namespace: string | null; readonly family: Family }) {
  const shell = useShellState(); const company = shell.activeContext?.companyId ?? null; const single = company !== null && shell.activeContext?.scopeMode === 'single'
  const allowed = single && namespace !== null && hasCapability(shell, C.prepare)
  const query = useBoundaryRead<BankInstitutionsWorkspace | CollateralRatesWorkspace>([namespace, shell.routeEntryGeneration, family], allowed,
    signal => family === 'bank-institutions' ? fetchBankInstitutions(company ?? '', signal) : fetchCollateralRates(company ?? '', signal))
  const state = stateOf(query.isPending, query.data)
  return <Frame title={family === 'bank-institutions' ? K.banks : K.rates} intro={K.intro}>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={K.banks} /> : !allowed ? <StateBlock state={{ kind: 'forbidden' }} subject={K.banks} />
      : state.kind === 'ready' ? <Editor data={state.data} family={family} changed={() => void query.refetch()} /> : <StateBlock state={state} subject={K.banks} onRetry={() => void query.refetch()} />}
  </Frame>
}
function Editor({ data, family, changed }: { readonly data: BankInstitutionsWorkspace | CollateralRatesWorkspace; readonly family: Family; readonly changed: () => void }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const banks = family === 'bank-institutions'; const rateData = 'types' in data ? data : null
  const scopeKey = financePolicyPendingKey(shell.sessionEpoch, shell.activeContext?.userId ?? null, data.companyId, family)
  const alive = useRef(true); useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  const [held, setHeld] = useState(() => recallFinancePolicy(scopeKey)); const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string | null>(null)
  const [from, setFrom] = useState(data.businessDate); const [through, setThrough] = useState(''); const [reason, setReason] = useState(''); const [target, setTarget] = useState<FinancePolicyHeader | null>(null)
  const [retireFrom, setRetireFrom] = useState(data.businessDate); const [entries, setEntries] = useState<readonly BankInput[]>([])
  const [scope, setScope] = useState<CollateralRateScope>('TypeBank'); const [typeId, setTypeId] = useState(''); const [bankId, setBankId] = useState(''); const [days, setDays] = useState(''); const [rate, setRate] = useState('')
  const [expiry, setExpiry] = useState(''); const [previewDate, setPreviewDate] = useState(data.businessDate)
  const [preview, setPreview] = useState<{ readonly key: string; readonly value: ViewOutcome<CollateralRatePreview> } | null>(null)
  const type = rateData?.types.entries.find(x => x.id === typeId); const bank = rateData?.banks.entries.find(x => x.bankInstitutionId === bankId)
  const previewKey = JSON.stringify([typeId, type?.version, bankId, bank?.sourceVersion, expiry, previewDate, data.versions]); const previewCurrent = useRef(previewKey); previewCurrent.current = previewKey
  const previewSequence = useRef(0)
  const frozen = busy || held !== null; const canPublish = hasCapability(shell, C.publish)
  const selectedVersions = rateData ? rateData.versions.filter(x => x.scope === scope && (scope === 'Company' || x.collateralTypeId === typeId && x.bankInstitutionId === bankId)) : data.versions
  const latest = Math.max(0, ...selectedVersions.map(x => x.version))
  async function execute(suffix: string, body: object) {
    if (busy || scopeKey === null) return
    const pending = recallFinancePolicy(scopeKey) ?? { suffix, body, key: crypto.randomUUID() }; reserveFinancePolicy(scopeKey, pending); setHeld(pending); setBusy(true)
    const captured = boundary.capture(); const result = await commandFinancePolicy({ companyId: data.companyId, idempotencyKey: pending.key }, pending.suffix, pending.body)
    if (result.type !== 'unknown' && !(result.type === 'refused' && (result.status === 401 || result.status === 403))) releaseFinancePolicy(scopeKey, pending.key)
    if (!alive.current) return
    setBusy(false)
    if (result.type === 'unknown') { setNotice(T.unknown); return }
    if (result.type === 'refused' && (result.status === 401 || result.status === 403)) { boundary.report(result.status, captured); return }
    setHeld(null); setTarget(null); setNotice(result.type === 'applied' ? result.replayed ? T.replay : T.saved : T.refused); changed()
  }
  function prepare(event: FormEvent) {
    event.preventDefault()
    const common = { expectedVersion: String(latest), validFrom: from, validThrough: through || null, reason: reason.trim() }
    void execute(family, banks ? { ...common, entries } : { ...common, scope, collateralTypeId: scope === 'Company' ? null : typeId,
      expectedCollateralTypeVersion: scope === 'Company' ? null : type?.version, bankInstitutionId: scope === 'Company' ? null : bankId,
      expectedBankSourceVersion: scope === 'Company' ? null : bank?.sourceVersion, maximumRemainingDays: scope === 'TypeBankRemainingDays' ? Number(days) : null, ratePercent: rate })
  }
  async function inspect() {
    const capturedKey = previewKey; const sequence = ++previewSequence.current; const captured = boundary.capture(); setPreview({ key: capturedKey, value: { type: 'loading' } })
    const result = await postFinancePolicyRead(data.companyId, 'collateral-rates/preview', { collateralTypeId: typeId, bankInstitutionId: bankId || null, bankSourceVersion: bank?.sourceVersion ?? null, validUntil: expiry || null, businessDate: previewDate }, raw => {
      const value = decodeCollateralRatePreview(raw)
      return value?.outcome === 'Available' && value.collateralTypeVersion !== type?.version ? null : value
    })
    if (!alive.current || previewCurrent.current !== capturedKey || sequence !== previewSequence.current) return
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, captured)
    setPreview({ key: capturedKey, value: result })
  }
  const patchBank = (i: number, patch: Partial<BankInput>) => setEntries(entries.map((entry, j) => j === i ? { ...entry, ...patch } : entry))
  const daysValid = /^[0-9]+$/.test(days) && Number.isSafeInteger(Number(days)) && Number(days) <= 2147483647
  const previewValue = preview?.key === previewKey ? preview.value : null
  return <section><p>{banks ? K.unpublished : K.priority}</p><p>{K.noDefault}</p>{notice && <p role={held ? 'alert' : 'status'}>{notice}</p>}
    {held && <Button type="button" disabled={busy} onClick={() => void execute(held.suffix, held.body)}>{busy ? T.pending : T.retry}</Button>}
    {data.versions.length === 0 && <p>{banks ? K.emptyBanks : K.noRates}</p>}
    <table className={styles.table}><caption>{T.history}</caption><thead><tr><th scope="col">{T.version}</th><th scope="col">{T.state}</th><th scope="col">{T.from}</th><th scope="col">{T.through}</th><th scope="col">{T.reason}</th><th scope="col">{T.publish}</th></tr></thead><tbody>
      {data.versions.map(row => <tr key={row.policyId}><td>{row.version}</td><td>{row.state === 'Draft' ? T.draft : row.state === 'Published' ? T.published : T.retired}</td><td>{row.validFrom}</td><td>{row.retiredFrom ?? row.validThrough ?? '—'}</td><td>{row.reason}</td><td>{row.state !== 'Retired' && canPublish && <Button type="button" disabled={frozen} onClick={() => setTarget(row)}>{row.state === 'Draft' ? T.publish : T.retire}</Button>}</td></tr>)}
    </tbody></table>
    {!rateData && (data as BankInstitutionsWorkspace).versions.map(row => <ul key={row.policyId}>{row.entries.map(e => <li key={e.bankInstitutionId}>{e.name} / {e.eftCode} / {e.active ? K.active : T.retired} / {T.source}: {e.sourceVersion}</li>)}</ul>)}
    {rateData?.versions.map(row => <p key={row.policyId}>{T.version} {row.version}: {row.scope} / {row.ratePercent}% {row.maximumRemainingDays !== null && <>/ {K.days}: {row.maximumRemainingDays}</>} / {K.typeSource}: {row.collateralTypeVersion ?? '—'} / {K.source}: {row.bankSourceVersion ?? '—'}</p>)}
    {target && <form className={styles.form} onSubmit={event => { event.preventDefault(); void execute(`${family}/${target.policyId}/${target.state === 'Draft' ? 'publish' : 'retire'}`, { expectedStateVersion: String(target.stateVersion), ...(target.state === 'Published' ? { retiredFrom: retireFrom } : {}), reason: reason.trim() }) }}><fieldset disabled={frozen}><legend>{target.state === 'Draft' ? T.publish : T.retire} {target.version}</legend>
      {target.state === 'Published' && <Field label={T.retireFrom}>{props => <input {...props} type="date" required min={data.businessDate} value={retireFrom} onChange={e => setRetireFrom(e.target.value)} />}</Field>}
      <Field label={T.reason}>{props => <textarea {...props} required maxLength={4000} value={reason} onChange={e => setReason(e.target.value)} />}</Field><Button type="submit" disabled={!reason.trim() || !canPublish}>{target.state === 'Draft' ? T.publish : T.retire}</Button>
    </fieldset></form>}
    <form className={styles.form} onSubmit={prepare}><fieldset disabled={frozen}><legend>{T.prepare}</legend>
      {banks ? <><Button type="button" disabled={data.versions.length === 0} onClick={() => { const current = (data as BankInstitutionsWorkspace).versions.toSorted((a, b) => b.version - a.version)[0]; if (current) setEntries(current.entries.map(e => ({ bankInstitutionId: e.bankInstitutionId, name: e.name, eftCode: e.eftCode, active: e.active }))) }}>{K.copy}</Button>
        <Button type="button" onClick={() => setEntries([...entries, { bankInstitutionId: null, name: '', eftCode: '', active: true }])}>{K.add}</Button>
        {entries.map((entry, i) => <fieldset key={i}><legend>{K.entries} {i + 1}</legend><Field label={K.name}>{props => <input {...props} required maxLength={200} value={entry.name} onChange={e => patchBank(i, { name: e.target.value })} />}</Field><Field label={K.eft}>{props => <input {...props} required maxLength={32} value={entry.eftCode} onChange={e => patchBank(i, { eftCode: e.target.value })} />}</Field><label><input type="checkbox" checked={entry.active} onChange={e => patchBank(i, { active: e.target.checked })} />{K.active}</label></fieldset>)}</>
        : <><Field label={K.scope}>{props => <select {...props} value={scope} onChange={e => setScope(e.target.value as CollateralRateScope)}><option value="Company">{K.company}</option><option value="TypeBank">{K.typeBank}</option><option value="TypeBankRemainingDays">{K.typeBankDays}</option></select>}</Field>
          <Field label={K.type}>{props => <select {...props} value={typeId} onChange={e => setTypeId(e.target.value)}><option value="">{T.choose}</option>{rateData?.types.entries.filter(e => e.active).map(e => <option key={e.id} value={e.id}>{e.name} ({e.code})</option>)}</select>}</Field>{type && <p>{K.typeSource}: {type.version}</p>}
          {rateData?.banks.outcome === 'Unavailable' && <p role="status">{K.noBanks}</p>}
          <Field label={K.bank}>{props => <select {...props} value={bankId} onChange={e => setBankId(e.target.value)}><option value="">{K.noBank}</option>{rateData?.banks.entries.filter(e => e.active).map(e => <option key={e.bankInstitutionId} value={e.bankInstitutionId}>{e.name} ({e.eftCode})</option>)}</select>}</Field>{bank && <p>{K.source}: {bank.sourceVersion}</p>}
          {scope === 'TypeBankRemainingDays' && <Field label={K.days}>{props => <input {...props} required inputMode="numeric" value={days} onChange={e => setDays(e.target.value)} />}</Field>}<p>{K.daysInfo}</p>
          <Field label={K.percentage}>{props => <input {...props} required inputMode="decimal" maxLength={64} value={rate} onChange={e => setRate(e.target.value)} />}</Field></>}
      <Field label={T.from}>{props => <input {...props} type="date" required min={data.businessDate} value={from} onChange={e => setFrom(e.target.value)} />}</Field><Field label={T.through}>{props => <input {...props} type="date" min={from} value={through} onChange={e => setThrough(e.target.value)} />}</Field>
      <Field label={T.reason}>{props => <textarea {...props} required maxLength={4000} value={reason} onChange={e => setReason(e.target.value)} />}</Field><Button type="submit" disabled={!reason.trim() || !banks && (!rate || scope !== 'Company' && (!type || !bank) || scope === 'TypeBankRemainingDays' && !daysValid)}>{T.prepare}</Button>
    </fieldset></form>
    {!banks && <fieldset disabled={frozen}><legend>{K.preview}</legend><Field label={K.expiry}>{props => <input {...props} type="date" value={expiry} onChange={e => setExpiry(e.target.value)} />}</Field><Field label={T.previewDate}>{props => <input {...props} required type="date" value={previewDate} onChange={e => setPreviewDate(e.target.value)} />}</Field><Button type="button" disabled={!type} onClick={() => void inspect()}>{K.preview}</Button></fieldset>}
    {previewValue && (previewValue.type === 'ok-single' ? <p role="status">{previewValue.data.outcome === 'Available' ? `${K.selected}: ${previewValue.data.scope} / ${previewValue.data.ratePercent}% / ${previewValue.data.sourceVersion}` : K.noSource}</p> : <StateBlock state={classifyOutcome(previewValue)} subject={K.rates} />)}
  </section>
}
