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
import { fetchFinanceOpeningPeriod, recordFinanceOpeningPeriod } from './financeOpeningPeriodApi'
import type { FinanceOpeningPeriod, RecordFinanceOpeningPeriodBody } from './financeOpeningPeriodContract'
import { FINANCE_OPENING_PERIOD_TEXT as T } from './financeOpeningPeriodText'
import styles from './settings.module.css'
const stateLabel = (state: 0 | 1 | 2) => state === 0 ? T.unrecorded : state === 1 ? T.open : T.closed

export function FinanceOpeningPeriodPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}
function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, SETTINGS_CAPABILITY.financeOpeningPeriod)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'finance-opening-period'], ready, (signal) => fetchFinanceOpeningPeriod(company ?? '', signal))
  const state = stateOf<FinanceOpeningPeriod>(query.isPending, query.data)
  if (!single) return <Frame title={T.title} intro={T.intro}><StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /></Frame>
  if (!ready) return <Frame title={T.title} intro={T.intro}><StateBlock state={{ kind: 'not-found' }} subject={T.subject} /></Frame>
  return <Frame title={T.title} intro={T.intro}>{state.kind === 'ready' ? <Period data={state.data} changed={() => void query.refetch()} />
    : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}</Frame>
}
function Period({ data, changed }: { readonly data: FinanceOpeningPeriod; readonly changed: () => void }) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  return <section><p role="status">{notice}</p><p>{T.state}: {stateLabel(data.state)}</p>
    <table className={styles.table}><caption>{T.history}</caption><thead><tr><th scope="col">{T.version}</th><th scope="col">{T.state}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
      <tbody>{data.versions.map((version) => <tr key={version.version}><td>{version.version}</td><td>{version.open ? T.open : T.closed}</td><td>{version.recordedBy}</td><td>{version.recordedAtUtc}</td><td>{version.reason}</td></tr>)}</tbody></table>
    {editing ? <Editor data={data} finish={(message) => { setEditing(false); setNotice(message); if (message !== null) changed() }} /> : <Button type="button" onClick={() => setEditing(true)}>{T.edit}</Button>}
  </section>
}
function Editor({ data, finish }: { readonly data: FinanceOpeningPeriod; readonly finish: (message: string | null) => void }) {
  const [open, setOpen] = useState('')
  const [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<RecordFinanceOpeningPeriodBody | null>(null)
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const busy = attempt.status.kind === 'busy'
  const valid = (open === 'open' || open === 'closed') && reason.trim().length > 0
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? { expectedVersion: data.version, open: open === 'open', reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => recordFinanceOpeningPeriod({ companyId: data.companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}><fieldset disabled={busy || frozen !== null}>
    <Field label={T.newState}>{(control) => <select {...control} required value={open} onChange={(event) => setOpen(event.target.value)}><option value="">{T.choose}</option><option value="open">{T.open}</option><option value="closed">{T.closed}</option></select>}</Field>
    <section aria-label={T.preview}><h3>{T.preview}</h3><p>{stateLabel(data.state)} → {open === '' ? T.choose : open === 'open' ? T.open : T.closed}</p><p>{T.effect}</p></section>
    <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
    </fieldset>{attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}<EditedAttemptNotice status={attempt.status} />{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
