import { EditedAttemptNotice } from './EditedAttemptNotice'
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
import { fetchContractDiscountLimit, recordContractDiscountLimit } from './salesContractDiscountLimitApi'
import type { ContractDiscountLimit, RecordContractDiscountLimit } from './salesContractDiscountLimitContract'
import { CONTRACT_DISCOUNT_LIMIT_TEXT as T } from './salesContractDiscountLimitText'
import styles from './settings.module.css'

export function SalesContractDiscountLimitPage() {
  const shell = useShellState(); const key = contextKey(shell); const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}
function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(); const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, SETTINGS_CAPABILITY.salesContractDiscountLimit)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'contract-discount-limit'], ready, signal => fetchContractDiscountLimit(company ?? '', signal))
  const state = stateOf<ContractDiscountLimit>(query.isPending, query.data)
  return <Frame title={T.title} intro={T.intro}>{!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
    : !ready ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} /> : state.kind === 'ready' ? <Limit data={state.data} changed={() => void query.refetch()} />
      : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}</Frame>
}
function Limit({ data, changed }: { readonly data: ContractDiscountLimit; readonly changed: () => void }) {
  const [editing,setEditing] = useState(false); const [notice,setNotice] = useState<string | null>(null)
  return <section><p role="status">{notice}</p><p>{T.current}: {data.limitPercent ?? T.unset}</p>
    <table className={styles.table}><caption>{T.history}</caption><thead><tr>{[T.version,T.rate,T.who,T.when,T.reason].map(label => <th key={label} scope="col">{label}</th>)}</tr></thead><tbody>
      {data.versions.map(item => <tr key={item.version}><td>{item.version}</td><td>{item.limitPercent}</td><td>{item.recordedBy}</td><td>{item.recordedAtUtc}</td><td>{item.reason}</td></tr>)}</tbody></table>
    {editing ? <Editor data={data} finish={message => { setEditing(false); setNotice(message); if (message !== null) changed() }} /> : <Button type="button" onClick={() => setEditing(true)}>{T.edit}</Button>}</section>
}
function Editor({ data, finish }: { readonly data: ContractDiscountLimit; readonly finish: (message: string | null) => void }) {
  const [rate,setRate] = useState(''); const [reason,setReason] = useState(''); const [frozen,setFrozen] = useState<RecordContractDiscountLimit | null>(null)
  const attempt = useCommandAttempt<true>(); const boundary = useAuthBoundary(); const busy = attempt.status.kind === 'busy'
  const valid = rate.length > 0 && reason.trim().length > 0
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? { expectedVersion: data.version, limitPercent: rate, reason: reason.trim() }; const snapshot = boundary.capture()
    const result = await attempt.run(payload, idempotencyKey => recordContractDiscountLimit({ companyId: data.companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }; setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status,snapshot)
    if (result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={event => void submit(event)} aria-busy={busy}><fieldset disabled={busy || frozen !== null}>
    <Field label={T.percent}>{control => <input {...control} required inputMode="decimal" maxLength={64} value={rate} onChange={event => setRate(event.target.value)} />}</Field>
    <Field label={T.reason}>{control => <textarea {...control} required maxLength={4000} value={reason} onChange={event => setReason(event.target.value)} />}</Field></fieldset>
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}<EditedAttemptNotice status={attempt.status} />{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button><Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button></form>
}
