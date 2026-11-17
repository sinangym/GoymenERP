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
import { fetchOperationalPolicy, recordOperationalPolicy } from './operationalPolicyApi'
import type { OperationalPolicy, OperationalPolicyKind, RecordOperationalPolicyBody } from './operationalPolicyContract'
import { OPERATIONAL_POLICY_TEXT as T } from './operationalPolicyText'
import styles from './settings.module.css'
const label = (value: boolean | number) => typeof value === 'boolean' ? value ? T.enabled : T.disabled : `${value} ${T.days}`
export function OperationalPolicyPage({ kind }: { readonly kind: OperationalPolicyKind }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}::${kind}`} kind={kind} namespace={namespace} />
}
function Screen({ kind, namespace }: { readonly kind: OperationalPolicyKind; readonly namespace: string | null }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, kind === 'scrap-cost' ? SETTINGS_CAPABILITY.manufacturingScrapCost : SETTINGS_CAPABILITY.notificationRetention)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, kind], ready, (signal) => fetchOperationalPolicy(kind, company ?? '', signal))
  const state = stateOf<OperationalPolicy>(query.isPending, query.data)
  const content = !single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /> : !ready ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
    : state.kind === 'ready' ? <Policy data={state.data} kind={kind} changed={() => void query.refetch()} /> : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
  return <Frame title={kind === 'scrap-cost' ? T.scrapTitle : T.notificationTitle} intro={kind === 'scrap-cost' ? T.scrapIntro : T.notificationIntro}>{content}</Frame>
}
function Policy({ data, kind, changed }: { readonly data: OperationalPolicy; readonly kind: OperationalPolicyKind; readonly changed: () => void }) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  return <section><p role="status">{notice}</p><p>{T.current}: {label(data.value)}</p>
    {data.versions.length === 0 ? <p>{T.empty}</p> : <table className={styles.table}><caption>{T.history}</caption><thead><tr><th scope="col">{T.version}</th><th scope="col">{T.value}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead><tbody>{data.versions.map((item) => <tr key={item.version}><td>{item.version}</td><td>{label(item.value)}</td><td>{item.recordedBy}</td><td>{item.recordedAtUtc}</td><td>{item.reason}</td></tr>)}</tbody></table>}
    {editing ? <Editor data={data} kind={kind} finish={(message) => { setEditing(false); setNotice(message); if (message !== null) changed() }} /> : <Button type="button" onClick={() => setEditing(true)}>{T.edit}</Button>}
  </section>
}
function Editor({ data, kind, finish }: { readonly data: OperationalPolicy; readonly kind: OperationalPolicyKind; readonly finish: (message: string | null) => void }) {
  const [value, setValue] = useState('')
  const [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<RecordOperationalPolicyBody | null>(null)
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const busy = attempt.status.kind === 'busy'
  const valid = reason.trim().length > 0 && (kind === 'scrap-cost' ? value === 'include' || value === 'exclude' : /^(0|[1-9]\d{0,9})$/.test(value) && Number(value) <= 2147483647)
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? { expectedVersion: data.version, ...(kind === 'scrap-cost' ? { includeScrapCost: value === 'include' } : { activeDays: Number(value) }), reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => recordOperationalPolicy(kind, { companyId: data.companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}><fieldset disabled={busy || frozen !== null}>
    <Field label={kind === 'scrap-cost' ? T.value : T.activeDays}>{(control) => kind === 'scrap-cost'
      ? <select {...control} required value={value} onChange={(event) => setValue(event.target.value)}><option value="">{T.choose}</option><option value="include">{T.include}</option><option value="exclude">{T.exclude}</option></select>
      : <input {...control} required inputMode="numeric" value={value} onChange={(event) => setValue(event.target.value)} />}</Field>
    <section aria-label={T.preview}><h3>{T.preview}</h3><p>{label(data.value)} → {valid ? label(kind === 'scrap-cost' ? value === 'include' : Number(value)) : T.choose}</p><p>{T.effect}</p></section>
    <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
    </fieldset>{attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button><Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
