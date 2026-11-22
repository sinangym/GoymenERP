/**
 * YOL-D22 — the "Resmî tatil ve arife takvimi" section of AY-OS-01 (`settings.work-approval-duration.manage`): one
 * versioned list of official holidays and eves per year. Nothing is seeded: a year never recorded says that only weekends
 * are deducted, and an explicitly saved empty list is a valid, defined answer. Each new version is one command with the
 * expected version and an Idempotency-Key; an unknown outcome keeps the same key and payload for the retry.
 */

import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { fetchWorkHolidayCalendar, recordWorkHolidayCalendar } from './workHolidayCalendarApi'
import {
  EVE_KIND,
  HOLIDAY_KIND,
  WORK_HOLIDAY_CALENDAR_LIMITS,
  isCalendarYear,
  isDayOfYear,
  isHolidayKind,
  type RecordWorkHolidayCalendarBody,
  type WorkHolidayCalendar,
  type WorkHolidayDay,
  type WorkHolidayKind,
} from './workHolidayCalendarContract'
import { WORK_HOLIDAY_CALENDAR_TEXT as T } from './workHolidayCalendarText'
import styles from './settings.module.css'

const kindText = (kind: WorkHolidayKind) => kind === HOLIDAY_KIND ? T.kindHoliday : T.kindEve
/** `yyyy-MM-dd` as Turkish `dd.MM.yyyy`, by string handling only. */
const dateText = (date: string) => { const [y, m, d] = date.split('-'); return `${d}.${m}.${y}` }
const instantText = (value: string) => {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

/** The years offered: from last year to two years ahead, inside the server's bounds. */
function yearOptions(current: number): number[] {
  return [current - 1, current, current + 1, current + 2].filter(isCalendarYear)
}

export function WorkHolidayCalendarSection({ companyId, namespace, routeEntryGeneration }: {
  readonly companyId: string
  readonly namespace: string | null
  readonly routeEntryGeneration: number
}) {
  const current = new Date().getFullYear()
  const [year, setYear] = useState(() => isCalendarYear(current) ? current : WORK_HOLIDAY_CALENDAR_LIMITS.maximumYear)
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const query = useBoundaryRead([namespace, routeEntryGeneration, 'work-holiday-calendar', year], namespace !== null,
    (signal) => fetchWorkHolidayCalendar(companyId, year, signal))
  const state = stateOf<WorkHolidayCalendar>(query.isPending, query.data)
  return <section aria-labelledby="work-holiday-calendar-heading" data-testid="work-holiday-calendar">
    <h2 id="work-holiday-calendar-heading">{T.heading}</h2>
    <p className={styles.muted}>{T.intro}</p>
    <Field label={T.year}>{(control) =>
      <select {...control} value={year} disabled={editing} onChange={(event) => { setYear(Number(event.target.value)); setNotice(null) }}>
        {yearOptions(current).map((option) => <option key={option} value={option}>{option}</option>)}
      </select>}</Field>
    <p role="status">{notice}</p>
    {state.kind === 'ready'
      ? <Calendar
        key={`${state.data.year}::${state.data.version}`}
        data={state.data}
        editing={editing}
        startEditing={() => { setEditing(true); setNotice(null) }}
        finish={(message) => { setEditing(false); setNotice(message); if (message !== null) void query.refetch() }} />
      : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}
  </section>
}

function Calendar({ data, editing, startEditing, finish }: {
  readonly data: WorkHolidayCalendar
  readonly editing: boolean
  readonly startEditing: () => void
  readonly finish: (message: string | null) => void
}) {
  return <>
    {!data.isDefined
      ? <p role="note" data-testid="work-holiday-calendar-undefined">{T.notDefined}</p>
      : data.days.length === 0
        ? <p data-testid="work-holiday-calendar-empty">{T.definedEmpty}</p>
        : <table className={styles.table}>
          <caption>{T.daysCaption(data.year)}</caption>
          <thead><tr><th scope="col">{T.date}</th><th scope="col">{T.kind}</th></tr></thead>
          <tbody>{data.days.map((day) => <tr key={day.date}>
            <td><time dateTime={day.date}>{dateText(day.date)}</time></td><td>{kindText(day.kind)}</td>
          </tr>)}</tbody>
        </table>}
    {data.versions.length > 0
      ? <table className={styles.table}>
        <caption>{T.history(data.year)}</caption>
        <thead><tr><th scope="col">{T.version}</th><th scope="col">{T.dayCount}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
        <tbody>{data.versions.map((item) => <tr key={item.version}>
          <td>{item.version}</td><td>{item.dayCount}</td><td>{item.recordedBy}</td>
          <td><time dateTime={item.recordedAtUtc}>{instantText(item.recordedAtUtc)}</time></td><td>{item.reason}</td>
        </tr>)}</tbody>
      </table>
      : null}
    {editing
      ? <Editor data={data} finish={finish} />
      : <Button type="button" onClick={startEditing}>{T.edit(data.year)}</Button>}
  </>
}

interface Row {
  readonly id: number
  readonly date: string
  readonly kind: WorkHolidayKind | ''
}

/** Every reason the list cannot be sent yet, in the user's words; empty when it can. */
function holidayProblems(rows: readonly Row[], reason: string, year: number): string[] {
  const problems: string[] = []
  if (rows.length > WORK_HOLIDAY_CALENDAR_LIMITS.maximumDays) problems.push(T.problemTooMany(WORK_HOLIDAY_CALENDAR_LIMITS.maximumDays))
  const seen = new Set<string>()
  const repeated = new Set<string>()
  rows.forEach((row, index) => {
    if (!isDayOfYear(row.date, year)) problems.push(T.problemDate(index + 1, year))
    else if (seen.has(row.date)) repeated.add(row.date)
    else seen.add(row.date)
    if (!isHolidayKind(row.kind)) problems.push(T.problemKind(index + 1))
  })
  for (const date of repeated) problems.push(T.problemDuplicate(dateText(date)))
  if (reason.trim().length === 0) problems.push(T.problemReason)
  return problems
}

function Editor({ data, finish }: { readonly data: WorkHolidayCalendar; readonly finish: (message: string | null) => void }) {
  // The new version starts from the list in force (never from a seeded calendar).
  const [rows, setRows] = useState<Row[]>(() => data.days.map((day, index) => ({ id: index, date: day.date, kind: day.kind })))
  const [nextId, setNextId] = useState(data.days.length)
  const [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<RecordWorkHolidayCalendarBody | null>(null)
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const busy = attempt.status.kind === 'busy'
  const problems = holidayProblems(rows, reason, data.year)
  const valid = problems.length === 0
  const update = (id: number, change: Partial<Row>) => setRows((current) => current.map((row) => row.id === id ? { ...row, ...change } : row))
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const days: WorkHolidayDay[] = rows
      .map((row) => ({ date: row.date, kind: row.kind as WorkHolidayKind }))
      .sort((left, right) => left.date.localeCompare(right.date))
    const payload = frozen ?? { expectedVersion: data.version, days, reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) =>
      recordWorkHolidayCalendar(data.year, { companyId: data.companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}>
    <fieldset disabled={busy || frozen !== null}>
      <legend>{T.editorLegend(data.year)}</legend>
      {rows.length === 0 ? <p className={styles.muted}>{T.noRows}</p> : <ul data-testid="work-holiday-calendar-rows">
        {rows.map((row, index) => <li key={row.id} role="group" aria-label={T.rowLabel(index + 1)}>
          <Field label={`${T.rowLabel(index + 1)} — ${T.date}`}>{(control) =>
            <input {...control} type="date" required min={`${data.year}-01-01`} max={`${data.year}-12-31`} value={row.date}
              onChange={(event) => update(row.id, { date: event.target.value })} />}</Field>
          <Field label={`${T.rowLabel(index + 1)} — ${T.kind}`}>{(control) =>
            <select {...control} required value={row.kind} onChange={(event) => update(row.id, { kind: isHolidayKind(event.target.value) ? event.target.value : '' })}>
              <option value="">{T.choose}</option>
              <option value={HOLIDAY_KIND}>{T.kindHoliday}</option>
              <option value={EVE_KIND}>{T.kindEve}</option>
            </select>}</Field>
          <Button type="button" size="sm" variant="ghost" onClick={() => setRows((current) => current.filter((item) => item.id !== row.id))}>
            {T.removeDay(index + 1)}
          </Button>
        </li>)}
      </ul>}
      <Button type="button" size="sm" disabled={rows.length >= WORK_HOLIDAY_CALENDAR_LIMITS.maximumDays}
        onClick={() => { setRows((current) => [...current, { id: nextId, date: '', kind: '' }]); setNextId(nextId + 1) }}>
        {T.addDay}
      </Button>
      <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
      <p>{T.effect}</p>
    </fieldset>
    {frozen === null && problems.length > 0
      ? <ul className={styles.muted} data-testid="work-holiday-calendar-problems">{problems.map((problem) => <li key={problem}>{problem}</li>)}</ul>
      : null}
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}
    {attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
