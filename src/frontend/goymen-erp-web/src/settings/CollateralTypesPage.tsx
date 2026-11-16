import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { SETTINGS_CAPABILITY } from './capabilities'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { fetchCollateralTypes, createCollateralType, recordCollateralType } from './collateralTypesApi'
import { COLLATERAL_KINDS, type CollateralKind, type CollateralTypes, type CollateralType, type CreateCollateralType, type RecordCollateralType } from './collateralTypesContract'
import { COLLATERAL_TYPES_TEXT as T, COLLATERAL_KIND_TEXT } from './collateralTypesText'
import styles from './settings.module.css'

export function CollateralTypesPage() {
  const shell = useShellState(); const key = contextKey(shell)
  return <Screen key={`${key === null ? '' : contextKeyString(key)}::${shell.routeEntryGeneration}`} namespace={key === null ? null : contextKeyString(key)} />
}
function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(); const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, SETTINGS_CAPABILITY.collateralTypes)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'collateral-types'], ready, signal => fetchCollateralTypes(company ?? '', signal))
  const state = stateOf<CollateralTypes>(query.isPending, query.data)
  return <Frame title={T.title} intro={T.intro}>{!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
    : !ready ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} /> : state.kind === 'ready' ? <List data={state.data} changed={() => void query.refetch()} />
      : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}</Frame>
}
function List({ data, changed }: { readonly data: CollateralTypes; readonly changed: () => void }) {
  const [editing, setEditing] = useState<CollateralType | 'new' | null>(null); const [notice, setNotice] = useState<string | null>(null)
  return <section><p role="status">{notice}</p>{data.entries.length === 0 ? <p>{T.empty}</p> : <table className={styles.table}><caption>{T.subject}</caption><thead><tr>{[T.code,T.name,T.kind,T.rate,T.state,T.version,T.who,T.when,T.edit].map(label => <th scope="col" key={label}>{label}</th>)}</tr></thead><tbody>
    {data.entries.map(entry => <tr key={entry.id}><td>{entry.code}</td><td>{entry.name}</td><td>{COLLATERAL_KIND_TEXT[entry.kind]}</td><td>{entry.ratePercent}</td><td>{entry.active ? T.active : T.inactive}</td><td>{entry.version}</td><td>{entry.recordedBy}</td><td>{entry.recordedAtUtc}</td><td><Button type="button" disabled={editing !== null} onClick={() => setEditing(entry)}>{T.edit}</Button></td></tr>)}</tbody></table>}
    {editing === null ? <Button type="button" onClick={() => setEditing('new')}>{T.add}</Button> : <Editor company={data.companyId} entry={editing === 'new' ? null : editing} finish={message => { setEditing(null); setNotice(message); if (message !== null) changed() }} />}</section>
}
function Editor({ company, entry, finish }: { readonly company: string; readonly entry: CollateralType | null; readonly finish: (message: string | null) => void }) {
  const [code,setCode] = useState(''); const [name,setName] = useState(entry?.name ?? ''); const [kind,setKind] = useState<CollateralKind | ''>('')
  const [active,setActive] = useState(entry?.active ?? true); const [rate,setRate] = useState(entry?.ratePercent ?? ''); const [reason,setReason] = useState('')
  const [frozen,setFrozen] = useState<CreateCollateralType | RecordCollateralType | null>(null)
  const attempt = useCommandAttempt<true>(); const boundary = useAuthBoundary(); const busy = attempt.status.kind === 'busy'
  const valid = name.trim().length > 0 && reason.trim().length > 0 && (entry === null ? code.length > 0 && kind !== '' : rate.length > 0)
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? (entry === null ? { code, name: name.trim(), kind: kind as CollateralKind, reason: reason.trim() }
      : { expectedVersion: entry.version, name: name.trim(), active, ratePercent: rate, reason: reason.trim() })
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, idempotencyKey => entry === null ? createCollateralType({ companyId: company, idempotencyKey }, payload as CreateCollateralType) : recordCollateralType({ companyId: company, idempotencyKey }, entry.id, payload as RecordCollateralType))
    if (result.type === 'unknown') { setFrozen(payload); return }; setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status,snapshot)
    if (result.status === 404 || result.code === 'settings_collateral_type_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={event => void submit(event)} aria-busy={busy}><fieldset disabled={busy || frozen !== null}>
    {entry === null ? <><Field label={T.code}>{control => <input {...control} required maxLength={32} value={code} onChange={event => setCode(event.target.value)} />}</Field><Field label={T.kind}>{control => <select {...control} required value={kind} onChange={event => setKind(event.target.value as CollateralKind | '')}><option value="">{T.choose}</option>{COLLATERAL_KINDS.map(value => <option key={value} value={value}>{COLLATERAL_KIND_TEXT[value]}</option>)}</select>}</Field><p>{T.initial}</p></> : <><p>{entry.code} — {COLLATERAL_KIND_TEXT[entry.kind]}</p><Field label={T.rate}>{control => <input {...control} required inputMode="decimal" maxLength={64} value={rate} onChange={event => setRate(event.target.value)} />}</Field><Field label={T.state}>{control => <select {...control} value={active ? 'active' : 'inactive'} onChange={event => setActive(event.target.value === 'active')}><option value="active">{T.active}</option><option value="inactive">{T.inactive}</option></select>}</Field></>}
    <Field label={T.name}>{control => <input {...control} required maxLength={200} value={name} onChange={event => setName(event.target.value)} />}</Field><Field label={T.reason}>{control => <textarea {...control} required maxLength={4000} value={reason} onChange={event => setReason(event.target.value)} />}</Field>
    </fieldset>{attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button><Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button></form>
}
