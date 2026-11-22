/**
 * AY-YST-01 `/ayarlar/yasal-sure-takvimi` (WP15, DALGA_PLANI_v2 1.16, YOL-D4 (c)): the versioned legal deadline calendar on
 * `settings.legal-deadline-calendar.manage`. Nothing is seeded and nothing is preselected: until the financial advisor
 * confirms the values the calendar is "not defined" and no counter runs. Each new version is one command with the expected
 * version and an Idempotency-Key; an unknown outcome keeps the same key and payload for the retry. The screen never computes
 * a due date (the server's calculator does, on AY-YS-01).
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
import { fetchLegalDeadlineCalendar, recordLegalDeadlineCalendar } from './legalDeadlineCalendarApi'
import {
  LEGAL_DEADLINE_LIMITS,
  LEGAL_DEADLINE_OBLIGATIONS,
  isObligation,
  isValidEntry,
  type LegalDeadlineCalendar,
  type LegalDeadlineEntry,
  type LegalDeadlineObligation,
  type RecordLegalDeadlineCalendarBody,
} from './legalDeadlineCalendarContract'
import { LEGAL_DEADLINE_TEXT as T, OBLIGATION_TEXT } from './legalDeadlineText'
import styles from './settings.module.css'

const instantText = (value: string) => {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function LegalDeadlineCalendarPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const ready = namespace !== null && single && hasCapability(shell, SETTINGS_CAPABILITY.legalDeadlineCalendar)
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'legal-deadline-calendar'], ready,
    (signal) => fetchLegalDeadlineCalendar(company ?? '', signal))
  const state = stateOf<LegalDeadlineCalendar>(query.isPending, query.data)
  return <Frame title={T.calendarTitle} intro={T.calendarIntro}>
    <p role="status">{notice}</p>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      : !ready ? <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
        : state.kind === 'ready'
          ? <Calendar key={state.data.version} data={state.data} editing={editing}
            startEditing={() => { setEditing(true); setNotice(null) }}
            finish={(message) => { setEditing(false); setNotice(message); if (message !== null) void query.refetch() }} />
          : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}
  </Frame>
}

function Calendar({ data, editing, startEditing, finish }: {
  readonly data: LegalDeadlineCalendar
  readonly editing: boolean
  readonly startEditing: () => void
  readonly finish: (message: string | null) => void
}) {
  return <>
    <p className={styles.muted}>{T.noShift}</p>
    {!data.isDefined
      ? <p role="note" data-testid="legal-deadline-calendar-undefined">{T.notDefined}</p>
      : data.entries.length === 0
        ? <p data-testid="legal-deadline-calendar-empty">{T.definedEmpty}</p>
        : <table className={styles.table}>
          <caption>{T.entriesCaption}</caption>
          <thead><tr><th scope="col">{T.obligation}</th><th scope="col">{T.periodMonths}</th><th scope="col">{T.monthsAfterPeriodEnd}</th><th scope="col">{T.dueDayOfMonth}</th></tr></thead>
          <tbody>{data.entries.map((entry) => <tr key={entry.obligation}>
            <td>{OBLIGATION_TEXT[entry.obligation]}</td><td>{T.period(entry.periodMonths)}</td>
            <td>{entry.monthsAfterPeriodEnd}</td><td>{entry.dueDayOfMonth}</td>
          </tr>)}</tbody>
        </table>}
    {data.versions.length > 0
      ? <table className={styles.table}>
        <caption>{T.history}</caption>
        <thead><tr><th scope="col">{T.version}</th><th scope="col">{T.entryCount}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
        <tbody>{data.versions.map((item) => <tr key={item.version}>
          <td>{item.version}</td><td>{item.entryCount}</td><td>{item.recordedBy}</td>
          <td><time dateTime={item.recordedAtUtc}>{instantText(item.recordedAtUtc)}</time></td><td>{item.reason}</td>
        </tr>)}</tbody>
      </table>
      : null}
    {editing ? <Editor data={data} finish={finish} /> : <Button type="button" onClick={startEditing}>{T.edit}</Button>}
  </>
}

interface Row {
  readonly id: number
  readonly obligation: LegalDeadlineObligation | ''
  readonly periodMonths: string
  readonly monthsAfterPeriodEnd: string
  readonly dueDayOfMonth: string
}

const whole = (text: string) => /^\d{1,3}$/.test(text) ? Number(text) : Number.NaN

/** Every reason the list cannot be sent yet, in the user's words; empty when it can. */
function rowProblems(rows: readonly Row[], reason: string): string[] {
  const problems: string[] = []
  const seen = new Set<string>()
  const repeated = new Set<LegalDeadlineObligation>()
  rows.forEach((row, index) => {
    if (!isObligation(row.obligation)) problems.push(T.problemObligation(index + 1))
    else if (seen.has(row.obligation)) repeated.add(row.obligation)
    else seen.add(row.obligation)
    if (!(LEGAL_DEADLINE_LIMITS.periodMonths as readonly number[]).includes(whole(row.periodMonths))) problems.push(T.problemPeriod(index + 1))
    const offset = whole(row.monthsAfterPeriodEnd)
    if (!(offset >= 0 && offset <= LEGAL_DEADLINE_LIMITS.maximumMonthsAfterPeriodEnd)) problems.push(T.problemOffset(index + 1))
    const day = whole(row.dueDayOfMonth)
    if (!(day >= 1 && day <= LEGAL_DEADLINE_LIMITS.maximumDueDayOfMonth)) problems.push(T.problemDay(index + 1))
  })
  for (const obligation of repeated) problems.push(T.problemDuplicate(OBLIGATION_TEXT[obligation]))
  if (reason.trim().length === 0) problems.push(T.problemReason)
  return problems
}

function Editor({ data, finish }: { readonly data: LegalDeadlineCalendar; readonly finish: (message: string | null) => void }) {
  // The new version starts from the calendar in force (never from assumed values).
  const [rows, setRows] = useState<Row[]>(() => data.entries.map((entry, index) => ({
    id: index, obligation: entry.obligation, periodMonths: String(entry.periodMonths),
    monthsAfterPeriodEnd: String(entry.monthsAfterPeriodEnd), dueDayOfMonth: String(entry.dueDayOfMonth),
  })))
  const [nextId, setNextId] = useState(data.entries.length)
  const [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<RecordLegalDeadlineCalendarBody | null>(null)
  const attempt = useCommandAttempt<true>()
  const boundary = useAuthBoundary()
  const busy = attempt.status.kind === 'busy'
  const problems = rowProblems(rows, reason)
  const valid = problems.length === 0
  const update = (id: number, change: Partial<Row>) => setRows((current) => current.map((row) => row.id === id ? { ...row, ...change } : row))
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!valid && frozen === null)) return
    const entries: LegalDeadlineEntry[] = rows
      .map((row) => ({ obligation: row.obligation as LegalDeadlineObligation, periodMonths: whole(row.periodMonths),
        monthsAfterPeriodEnd: whole(row.monthsAfterPeriodEnd), dueDayOfMonth: whole(row.dueDayOfMonth) }))
      .filter(isValidEntry)
      .sort((left, right) => LEGAL_DEADLINE_OBLIGATIONS.indexOf(left.obligation) - LEGAL_DEADLINE_OBLIGATIONS.indexOf(right.obligation))
    const payload = frozen ?? { expectedVersion: data.version, entries, reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) =>
      recordLegalDeadlineCalendar({ companyId: data.companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_company_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}>
    <fieldset disabled={busy || frozen !== null}>
      <legend>{T.editorLegend}</legend>
      {rows.length === 0 ? <p className={styles.muted}>{T.noRows}</p> : <ul data-testid="legal-deadline-calendar-rows">
        {rows.map((row, index) => <li key={row.id} role="group" aria-label={T.rowLabel(index + 1)}>
          <Field label={`${T.rowLabel(index + 1)} — ${T.obligation}`}>{(control) =>
            <select {...control} required value={row.obligation} onChange={(event) => update(row.id, { obligation: isObligation(event.target.value) ? event.target.value : '' })}>
              <option value="">{T.choose}</option>
              {LEGAL_DEADLINE_OBLIGATIONS.map((item) => <option key={item} value={item}>{OBLIGATION_TEXT[item]}</option>)}
            </select>}</Field>
          <Field label={`${T.rowLabel(index + 1)} — ${T.periodMonths}`}>{(control) =>
            <select {...control} required value={row.periodMonths} onChange={(event) => update(row.id, { periodMonths: event.target.value })}>
              <option value="">{T.choose}</option>
              {LEGAL_DEADLINE_LIMITS.periodMonths.map((months) => <option key={months} value={String(months)}>{T.period(months)}</option>)}
            </select>}</Field>
          <Field label={`${T.rowLabel(index + 1)} — ${T.monthsAfterPeriodEnd}`}>{(control) =>
            <input {...control} required inputMode="numeric" value={row.monthsAfterPeriodEnd}
              onChange={(event) => update(row.id, { monthsAfterPeriodEnd: event.target.value })} />}</Field>
          <Field label={`${T.rowLabel(index + 1)} — ${T.dueDayOfMonth}`}>{(control) =>
            <input {...control} required inputMode="numeric" value={row.dueDayOfMonth}
              onChange={(event) => update(row.id, { dueDayOfMonth: event.target.value })} />}</Field>
          <Button type="button" size="sm" variant="ghost" onClick={() => setRows((current) => current.filter((item) => item.id !== row.id))}>
            {T.removeRow(index + 1)}
          </Button>
        </li>)}
      </ul>}
      <Button type="button" size="sm" disabled={rows.length >= LEGAL_DEADLINE_OBLIGATIONS.length}
        onClick={() => { setRows((current) => [...current, { id: nextId, obligation: '', periodMonths: '', monthsAfterPeriodEnd: '', dueDayOfMonth: '' }]); setNextId(nextId + 1) }}>
        {T.addRow}
      </Button>
      <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
      <p>{T.effect}</p>
    </fieldset>
    {frozen === null && problems.length > 0
      ? <ul className={styles.muted} data-testid="legal-deadline-calendar-problems">{problems.map((problem) => <li key={problem}>{problem}</li>)}</ul>
      : null}
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}
    {attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
