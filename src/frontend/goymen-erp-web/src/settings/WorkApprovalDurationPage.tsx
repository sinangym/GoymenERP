/**
 * AY-OS-01 `/ayarlar/onay-sureleri` (W1-A, YOL-D17/YOL-D18): the lengths of the two approval duration classes, each a
 * versioned company policy on `settings.work-approval-duration.manage`. The screen never preselects a value and never
 * assumes a default: a class that was never recorded stays "not recorded" (the inbox then says "Süre tanımlı değil").
 * Each new version is one command with an Idempotency-Key; an unknown outcome keeps the same key and payload.
 * YOL-D22 adds the yearly official holiday/eve calendar section (`WorkHolidayCalendarSection`).
 */

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
import { fetchWorkApprovalDuration, recordWorkApprovalDuration } from './workApprovalDurationApi'
import {
  WORK_APPROVAL_DURATION_RANGE,
  type RecordWorkApprovalDurationBody,
  type WorkApprovalDurationClass,
  type WorkApprovalDurationClassKey,
  type WorkApprovalDurationPolicy,
} from './workApprovalDurationContract'
import { WORK_APPROVAL_DURATION_TEXT as T } from './workApprovalDurationText'
import { WorkHolidayCalendarSection } from './WorkHolidayCalendarSection'
import styles from './settings.module.css'

const unitOf = (key: WorkApprovalDurationClassKey) => key === 'hours' ? T.hoursUnit : T.businessDaysUnit
const headingOf = (key: WorkApprovalDurationClassKey) => key === 'hours' ? T.hoursHeading : T.businessDaysHeading
const valueText = (key: WorkApprovalDurationClassKey, value: number | null) => value === null ? T.notRecorded : `${value} ${unitOf(key)}`
const instantText = (value: string) => {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function WorkApprovalDurationPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, SETTINGS_CAPABILITY.workApprovalDuration)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'work-approval-duration'], ready,
    (signal) => fetchWorkApprovalDuration(company ?? '', signal))
  const state = stateOf<WorkApprovalDurationPolicy>(query.isPending, query.data)
  const content = !single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
    : !ready ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
      : state.kind === 'ready'
        ? <>
          <ClassPolicy classKey="hours" data={state.data.hours} companyId={state.data.companyId} changed={() => void query.refetch()} />
          <ClassPolicy classKey="businessDays" data={state.data.businessDays} companyId={state.data.companyId} changed={() => void query.refetch()} />
        </>
        : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
  // YOL-D22: the holiday calendar reads on its own; a failure of either read never hides the other.
  return <Frame title={T.title} intro={T.intro}>
    {content}
    {ready && company !== null
      ? <WorkHolidayCalendarSection companyId={company} namespace={namespace} routeEntryGeneration={shell.routeEntryGeneration} />
      : null}
  </Frame>
}

function ClassPolicy({ classKey, data, companyId, changed }: {
  readonly classKey: WorkApprovalDurationClassKey
  readonly data: WorkApprovalDurationClass
  readonly companyId: string
  readonly changed: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const heading = headingOf(classKey)
  const headingId = `work-approval-duration-${classKey}`
  return <section aria-labelledby={headingId} data-testid={`work-approval-duration-${classKey}`}>
    <h2 id={headingId}>{heading}</h2>
    <p role="status">{notice}</p>
    <p>{T.current}: {valueText(classKey, data.value)}</p>
    {data.versions.length === 0 ? <p>{T.empty}</p> : <table className={styles.table}>
      <caption>{T.history(heading)}</caption>
      <thead><tr><th scope="col">{T.version}</th><th scope="col">{T.value}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
      <tbody>{data.versions.map((item) => <tr key={item.version}>
        <td>{item.version}</td><td>{valueText(classKey, item.value)}</td><td>{item.recordedBy}</td>
        <td><time dateTime={item.recordedAtUtc}>{instantText(item.recordedAtUtc)}</time></td><td>{item.reason}</td>
      </tr>)}</tbody>
    </table>}
    {editing
      ? <Editor classKey={classKey} data={data} companyId={companyId} finish={(message) => { setEditing(false); setNotice(message); if (message !== null) changed() }} />
      : <Button type="button" onClick={() => setEditing(true)}>{T.edit(heading)}</Button>}
  </section>
}

function Editor({ classKey, data, companyId, finish }: {
  readonly classKey: WorkApprovalDurationClassKey
  readonly data: WorkApprovalDurationClass
  readonly companyId: string
  readonly finish: (message: string | null) => void
}) {
  const [value, setValue] = useState('')
  const [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<RecordWorkApprovalDurationBody | null>(null)
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const range = WORK_APPROVAL_DURATION_RANGE[classKey]
  const busy = attempt.status.kind === 'busy'
  const parsed = /^[1-9]\d{0,2}$/.test(value) ? Number(value) : null
  const valid = reason.trim().length > 0 && parsed !== null && parsed >= range.min && parsed <= range.max
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? { expectedVersion: data.version, value: parsed as number, reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => recordWorkApprovalDuration(classKey, { companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}>
    <fieldset disabled={busy || frozen !== null}>
      <Field label={T.newValue(unitOf(classKey), range.min, range.max)}>{(control) =>
        <input {...control} required inputMode="numeric" value={value} onChange={(event) => setValue(event.target.value)} />}</Field>
      <section aria-label={T.preview}>
        <h3>{T.preview}</h3>
        <p>{valueText(classKey, data.value)} → {valid ? valueText(classKey, parsed) : T.choose}</p>
        <p>{T.effect}</p>
      </section>
      <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
    </fieldset>
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}
    {attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
