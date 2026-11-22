/**
 * YOL-D25 (AI-D29, WP2) — the "Günlük özet saatleri" section of AY-OS-01 (`settings.work-approval-duration.manage`): three
 * versioned digest times. A slot never recorded shows the decided default (version 0); the editor never preselects a value.
 * Each new version is one command with the expected version, a reason and an Idempotency-Key; an unknown outcome keeps the
 * same key and payload for the retry. It reads on its own: its failure never hides the other sections.
 */

import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { fetchWorkDigestSchedule, recordWorkDigestTime } from './workDigestScheduleApi'
import { clockText, parseClock, type RecordWorkDigestTimeBody, type WorkDigestSchedule, type WorkDigestTime } from './workDigestScheduleContract'
import { WORK_DIGEST_SCHEDULE_TEXT as T } from './workDigestScheduleText'
import styles from './settings.module.css'

const instantText = (value: string) => {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function WorkDigestScheduleSection({ companyId, namespace, routeEntryGeneration }: {
  readonly companyId: string
  readonly namespace: string | null
  readonly routeEntryGeneration: number
}) {
  const query = useBoundaryRead([namespace, routeEntryGeneration, 'work-digest-schedule'], namespace !== null,
    (signal) => fetchWorkDigestSchedule(companyId, signal))
  const state = stateOf<WorkDigestSchedule>(query.isPending, query.data)
  return <section aria-labelledby="work-digest-schedule-heading" data-testid="work-digest-schedule">
    <h2 id="work-digest-schedule-heading">{T.heading}</h2>
    <p className={styles.muted}>{T.intro}</p>
    {state.kind === 'ready'
      ? state.data.times.map((time) => <SlotPolicy key={time.slot} time={time} companyId={state.data.companyId} changed={() => void query.refetch()} />)
      : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}
  </section>
}

function SlotPolicy({ time, companyId, changed }: { readonly time: WorkDigestTime; readonly companyId: string; readonly changed: () => void }) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const label = T.slot[time.slot]
  const headingId = `work-digest-${time.slot}`
  return <section aria-labelledby={headingId} data-testid={`work-digest-${time.slot}`}>
    <h3 id={headingId}>{label}</h3>
    <p role="status">{notice}</p>
    <p>{T.current}: {clockText(time.minute)}{time.version === '0' ? ` (${T.decidedDefault})` : null}</p>
    {time.versions.length === 0 ? <p>{T.empty}</p> : <table className={styles.table}>
      <caption>{T.history(label)}</caption>
      <thead><tr><th scope="col">{T.version}</th><th scope="col">{T.value}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
      <tbody>{time.versions.map((item) => <tr key={item.version}>
        <td>{item.version}</td><td>{clockText(item.value)}</td><td>{item.recordedBy}</td>
        <td><time dateTime={item.recordedAtUtc}>{instantText(item.recordedAtUtc)}</time></td><td>{item.reason}</td>
      </tr>)}</tbody>
    </table>}
    {editing
      ? <Editor time={time} companyId={companyId} finish={(message) => { setEditing(false); setNotice(message); if (message !== null) changed() }} />
      : <Button type="button" onClick={() => setEditing(true)}>{T.edit(label)}</Button>}
  </section>
}

function Editor({ time, companyId, finish }: {
  readonly time: WorkDigestTime
  readonly companyId: string
  readonly finish: (message: string | null) => void
}) {
  const [value, setValue] = useState('')
  const [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<RecordWorkDigestTimeBody | null>(null)
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const busy = attempt.status.kind === 'busy'
  const parsed = parseClock(value)
  const valid = reason.trim().length > 0 && parsed !== null
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? { expectedVersion: time.version, minute: parsed as number, reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => recordWorkDigestTime(time.slot, { companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}>
    <fieldset disabled={busy || frozen !== null}>
      <Field label={T.newValue}>{(control) =>
        <input {...control} required inputMode="numeric" placeholder="SS:DD" value={value} onChange={(event) => setValue(event.target.value)} />}</Field>
      <section aria-label={T.preview}>
        <h4>{T.preview}</h4>
        <p>{clockText(time.minute)} → {parsed === null ? T.choose : clockText(parsed)}</p>
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
