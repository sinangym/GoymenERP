/**
 * B-15 — the work centre's shift/calendar section on the ORG-04 card.
 *
 * Reads `GET /organization/work-centers/{id}/calendar` on the card's own grant and shows the weekly shifts, the
 * closed dates and whether the calendar is the work centre's own or the URT-D11 default. Under the server's
 * `organization.work-centers.calendar.edit` control it offers the whole-calendar edit (`PUT …/calendar`), after
 * which the calendar is re-read; the saved content and the next version are never predicted here.
 *
 * THE RULES THAT SHAPE THIS FILE (the master edit's, ./MasterRecordForm.tsx):
 *
 *  - ONE KEY PER SUBMITTED SNAPSHOT. A submit mints a key and freezes the exact command; a definite refusal closes
 *    the attempt and keeps what was typed; an UNKNOWN outcome keeps the key and the frozen command, so the only ways
 *    forward are the same-key resend (the server replays rather than writing twice) or re-reading the calendar.
 *  - The expected version is the one READ (0 on the default) at submit time, never a predicted one.
 *  - A 401/403 goes to the shared auth boundary, captured before the request.
 *  - Typed work is dirty work: the shell warns before a company switch discards it.
 *  - URT-D29: nothing here computes capacity, load or a termin from the calendar.
 */

import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchWorkCenterCalendar } from './api'
import type { CommandFailure, CommandUnknown } from './commandApi'
import {
  CALENDAR_DAYS,
  CALENDAR_LIMITS,
  CALENDAR_SOURCE,
  isCalendarDay,
  isDateToken,
  endsNextDay,
  isTimeToken,
  shiftsOverlap,
  sortShifts,
  type CalendarDay,
  type CalendarShift,
  type WorkCenterCalendar,
} from './workCenterCalendarContract'
import {
  sendWorkCenterCalendar,
  type CalendarCommandOutcome,
  type SetWorkCenterCalendarCommand,
} from './workCenterCalendarApi'
import {
  MASTER_TEXT,
  REASON_MAX_LENGTH,
  REASON_REQUIRED,
  REASON_TOO_LONG,
  WORK_CENTER_CALENDAR_TEXT,
  calendarDayLabel,
  calendarFailureText,
  calendarSavedNotice,
  conflictVersionsText,
  masterRetryRefusedText,
  unknownText,
} from './organizationText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellDispatch } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { MasterFormLauncher } from './MasterRecordForm'
import { refreshOrganizationReads } from './refreshOrganizationReads'
import formStyles from './MasterRecordForm.module.css'
import styles from './WorkCenterCalendarSection.module.css'

const DIRTY_SCOPE = 'organization.work-center.calendar.edit'

export function WorkCenterCalendarSection({
  workCenterId,
  companyId,
  namespace,
  entryGeneration,
  canEdit,
}: {
  readonly workCenterId: string
  readonly companyId: string
  readonly namespace: string
  readonly entryGeneration: number
  readonly canEdit: boolean
}) {
  const boundary = useAuthBoundary()
  const headingId = useId()
  const query = useQuery({
    queryKey: [namespace, entryGeneration, 'organization-work-center-calendar', workCenterId],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchWorkCenterCalendar(companyId, workCenterId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const queryClient = useQueryClient()
  const refetch = query.refetch
  const refresh = useCallback(async () => {
    await refetch()
    await refreshOrganizationReads(queryClient, namespace)
  }, [namespace, queryClient, refetch])

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<WorkCenterCalendar>

  return (
    <section className={styles.section} aria-labelledby={headingId} data-section="work-center-calendar">
      <h2 id={headingId} className={styles.heading}>
        {WORK_CENTER_CALENDAR_TEXT.heading}
      </h2>
      <p className={styles.lead}>{WORK_CENTER_CALENDAR_TEXT.lead}</p>
      {state.kind === 'ready' ? (
        <>
          <CalendarView calendar={state.data} />
          {canEdit ? (
            <CalendarEdit calendar={state.data} companyId={companyId} onRefresh={refresh} />
          ) : null}
        </>
      ) : (
        <StateBlock state={state} subject={WORK_CENTER_CALENDAR_TEXT.subject} onRetry={() => void query.refetch()} />
      )}
    </section>
  )
}

/** KIM-D33: an overnight window is shown with its next-day marker, so 22:00–06:00 is never read as same-day. */
function shiftWindowLabel(shift: CalendarShift): string {
  return WORK_CENTER_CALENDAR_TEXT.shiftWindow(shift.start, shift.end, endsNextDay(shift))
}

function CalendarView({ calendar }: { readonly calendar: WorkCenterCalendar }) {
  const byDay = new Map<CalendarDay, CalendarShift[]>()
  for (const shift of sortShifts(calendar.shifts)) {
    byDay.set(shift.day, [...(byDay.get(shift.day) ?? []), shift])
  }
  return (
    <div className={styles.view} data-source={calendar.source}>
      <p className={styles.source}>
        {calendar.source === CALENDAR_SOURCE.default
          ? WORK_CENTER_CALENDAR_TEXT.sourceDefault
          : WORK_CENTER_CALENDAR_TEXT.sourceWorkCenter}
      </p>
      <p className={styles.meta}>
        {WORK_CENTER_CALENDAR_TEXT.timeZone}: <span>{calendar.timeZoneId}</span>
      </p>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">{WORK_CENTER_CALENDAR_TEXT.day}</th>
            <th scope="col">{WORK_CENTER_CALENDAR_TEXT.shifts}</th>
          </tr>
        </thead>
        <tbody>
          {CALENDAR_DAYS.map((day) => {
            const shifts = byDay.get(day) ?? []
            return (
              <tr key={day} data-day={day}>
                <th scope="row">{calendarDayLabel(day)}</th>
                <td>
                  {shifts.length === 0 ? (
                    <span className={styles.closed}>{WORK_CENTER_CALENDAR_TEXT.closed}</span>
                  ) : (
                    shifts.map(shiftWindowLabel).join(', ')
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
      <h3 className={styles.subheading}>{WORK_CENTER_CALENDAR_TEXT.closedDates}</h3>
      {calendar.closedDates.length === 0 ? (
        <p className={styles.meta}>{WORK_CENTER_CALENDAR_TEXT.noClosedDates}</p>
      ) : (
        <ul className={styles.dates}>
          {calendar.closedDates.map((date) => (
            <li key={date}>
              <time dateTime={date}>{date}</time>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function CalendarEdit({
  calendar,
  companyId,
  onRefresh,
}: {
  readonly calendar: WorkCenterCalendar
  readonly companyId: string
  readonly onRefresh: () => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [saved, setSaved] = useState<null | { readonly replayed: boolean }>(null)
  if (!open) {
    return (
      <MasterFormLauncher
        label={WORK_CENTER_CALENDAR_TEXT.edit}
        saved={saved === null ? null : calendarSavedNotice(saved.replayed)}
        onOpen={() => {
          setSaved(null)
          setOpen(true)
        }}
      />
    )
  }
  return (
    <CalendarForm
      calendar={calendar}
      companyId={companyId}
      onUpdated={async (replayed) => {
        await onRefresh()
        setSaved({ replayed })
        setOpen(false)
      }}
      onReload={onRefresh}
      onCancel={() => setOpen(false)}
    />
  )
}

interface ShiftRow {
  readonly rowId: number
  readonly day: string
  readonly start: string
  readonly end: string
}

interface DateRow {
  readonly rowId: number
  readonly value: string
}

type Phase =
  | { readonly kind: 'editing' }
  | { readonly kind: 'submitting'; readonly key: string; readonly command: SetWorkCenterCalendarCommand }
  | {
      readonly kind: 'unknown'
      readonly key: string
      readonly command: SetWorkCenterCalendarCommand
      readonly unknown: CommandUnknown
      readonly lastRefusal?: CommandFailure
    }
  | { readonly kind: 'failed'; readonly failure: CommandFailure }

type FormErrors = Partial<Record<'shifts' | 'dates' | 'reason', string>>

/** One key per snapshot. A UUID is inside the server's 1..128 canonical length and has no comma. */
function newIdempotencyKey(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  return `org-calendar-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

/**
 * The form's own check of the server's definition rules, so a refusal it can foresee mints no key. The server stays
 * the authority: it refuses the same shapes with a 400.
 */
export function validateCalendar(
  shifts: readonly { readonly day: string; readonly start: string; readonly end: string }[],
  dates: readonly string[],
): { readonly shifts?: string; readonly dates?: string } {
  const errors: { shifts?: string; dates?: string } = {}
  if (shifts.length === 0) {
    errors.shifts = WORK_CENTER_CALENDAR_TEXT.noShift
  } else if (shifts.length > CALENDAR_LIMITS.shifts) {
    errors.shifts = WORK_CENTER_CALENDAR_TEXT.tooManyShifts
  } else if (
    shifts.some((shift) => !isCalendarDay(shift.day) || !isTimeToken(shift.start) || !isTimeToken(shift.end))
    || shifts.some((shift) => shift.end === shift.start)
  ) {
    errors.shifts = WORK_CENTER_CALENDAR_TEXT.shiftInvalid
  } else {
    const valid = shifts as readonly CalendarShift[]
    const perDay = new Map<string, number>()
    for (const shift of valid) perDay.set(shift.day, (perDay.get(shift.day) ?? 0) + 1)
    if (shiftsOverlap(valid)) {
      errors.shifts = WORK_CENTER_CALENDAR_TEXT.shiftsOverlap
    } else if ([...perDay.values()].some((count) => count > CALENDAR_LIMITS.shiftsPerDay)) {
      errors.shifts = WORK_CENTER_CALENDAR_TEXT.tooManyShifts
    }
  }

  if (dates.length > CALENDAR_LIMITS.closedDates) {
    errors.dates = WORK_CENTER_CALENDAR_TEXT.tooManyClosedDates
  } else if (dates.some((date) => !isDateToken(date))) {
    errors.dates = WORK_CENTER_CALENDAR_TEXT.dateInvalid
  } else if (new Set(dates).size !== dates.length) {
    errors.dates = WORK_CENTER_CALENDAR_TEXT.duplicateClosedDate
  }
  return errors
}

function CalendarForm({
  calendar,
  companyId,
  onUpdated,
  onReload,
  onCancel,
}: {
  readonly calendar: WorkCenterCalendar
  readonly companyId: string
  readonly onUpdated: (replayed: boolean) => Promise<void>
  /** Re-reads the calendar. Never touches an unresolved attempt. */
  readonly onReload: () => Promise<void>
  readonly onCancel: () => void
}) {
  const shellDispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const baseId = useId()
  const nextRowId = useRef(0)
  const mintRowId = () => {
    nextRowId.current += 1
    return nextRowId.current
  }
  const initialShifts = useRef<ShiftRow[]>(
    calendar.shifts.map((shift) => ({ rowId: mintRowId(), day: shift.day, start: shift.start, end: shift.end })),
  )
  const initialDates = useRef<DateRow[]>(calendar.closedDates.map((value) => ({ rowId: mintRowId(), value })))
  const [shifts, setShifts] = useState<ShiftRow[]>(initialShifts.current)
  const [dates, setDates] = useState<DateRow[]>(initialDates.current)
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<FormErrors>({})
  const [phase, setPhase] = useState<Phase>({ kind: 'editing' })
  const [conflictReloaded, setConflictReloaded] = useState(false)
  const noticeRef = useRef<HTMLDivElement>(null)

  const busy = phase.kind === 'submitting'
  const frozen = phase.kind === 'unknown' || busy

  const dirty =
    frozen ||
    shifts !== initialShifts.current ||
    dates !== initialDates.current ||
    reason.trim().length > 0
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: DIRTY_SCOPE }), [shellDispatch])

  useEffect(() => {
    if (phase.kind === 'failed' || phase.kind === 'unknown') noticeRef.current?.focus()
  }, [phase])

  const settle = useCallback(
    async (outcome: CalendarCommandOutcome, key: string, command: SetWorkCenterCalendarCommand, previous?: Phase) => {
      if (outcome.type === 'updated') {
        setPhase({ kind: 'editing' })
        shellDispatch({ type: 'DIRTY_CLEAR', scope: DIRTY_SCOPE })
        await onUpdated(outcome.replayed)
        return
      }
      if (outcome.type === 'unknown') {
        setPhase({ kind: 'unknown', key, command, unknown: outcome.unknown })
        return
      }
      // A non-replayed refusal of a RESEND is about that request, not the first one: the attempt stays unknown.
      if (previous?.kind === 'unknown' && !outcome.replayed) {
        setPhase({ ...previous, lastRefusal: outcome.failure })
        return
      }
      setPhase({ kind: 'failed', failure: outcome.failure })
    },
    [onUpdated, shellDispatch],
  )

  const send = useCallback(
    async (key: string, command: SetWorkCenterCalendarCommand, previous?: Phase) => {
      setPhase({ kind: 'submitting', key, command })
      const security = boundary.capture()
      const outcome = await sendWorkCenterCalendar(companyId, key, command)
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }
      await settle(outcome, key, command, previous)
    },
    [boundary, companyId, settle],
  )

  const submit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault()
      if (frozen) return
      const values = shifts.map(({ day, start, end }) => ({ day, start, end }))
      const dateValues = dates.map((row) => row.value)
      const next: FormErrors = { ...validateCalendar(values, dateValues) }
      if (reason.trim().length === 0) next.reason = REASON_REQUIRED
      else if (reason.trim().length > REASON_MAX_LENGTH) next.reason = REASON_TOO_LONG
      setErrors(next)
      // A submit refused here mints NO key: nothing was attempted.
      if (Object.keys(next).length > 0) return
      const command: SetWorkCenterCalendarCommand = {
        workCenterId: calendar.workCenterId,
        shifts: values as CalendarShift[],
        closedDates: dateValues,
        // The version READ — after a conflict reload, the fresh one.
        expectedVersion: calendar.version,
        reason,
      }
      setConflictReloaded(false)
      void send(newIdempotencyKey(), command)
    },
    [calendar.version, calendar.workCenterId, dates, frozen, reason, send, shifts],
  )

  const resend = useCallback(() => {
    if (phase.kind !== 'unknown') return
    void send(phase.key, phase.command, phase)
  }, [phase, send])

  const reloadAfterRefusal = useCallback(async () => {
    const afterConflict = phase.kind === 'failed' && phase.failure.kind === 'version-conflict'
    setPhase({ kind: 'editing' })
    await onReload()
    if (afterConflict) setConflictReloaded(true)
  }, [onReload, phase])

  const updateShift = (rowId: number, patch: Partial<Omit<ShiftRow, 'rowId'>>) =>
    setShifts((rows) => rows.map((row) => (row.rowId === rowId ? { ...row, ...patch } : row)))
  const updateDate = (rowId: number, value: string) =>
    setDates((rows) => rows.map((row) => (row.rowId === rowId ? { ...row, value } : row)))

  const fieldId = (field: string) => `${baseId}-${field}`

  return (
    <section className={formStyles.section} aria-labelledby={fieldId('heading')}>
      <h3 id={fieldId('heading')} className={formStyles.heading}>
        {WORK_CENTER_CALENDAR_TEXT.edit}
      </h3>
      <p className={formStyles.help} id={fieldId('help')}>
        {phase.kind === 'unknown' ? MASTER_TEXT.frozenHelp : WORK_CENTER_CALENDAR_TEXT.help}
      </p>

      <Notice
        phase={phase}
        noticeRef={noticeRef}
        onResend={resend}
        onReloadRecord={onReload}
        onReloadAfterRefusal={reloadAfterRefusal}
      />

      {conflictReloaded ? (
        <div className={formStyles.noticeWarn} role="status" data-outcome="conflict-reloaded">
          <p className={formStyles.noticeTitle}>{WORK_CENTER_CALENDAR_TEXT.conflictReloadedTitle}</p>
          <p>{WORK_CENTER_CALENDAR_TEXT.conflictReloadedDetail}</p>
        </div>
      ) : null}

      <form className={styles.form} onSubmit={submit} noValidate aria-describedby={fieldId('help')}>
        <fieldset
          className={styles.fieldset}
          aria-invalid={errors.shifts !== undefined}
          aria-describedby={errors.shifts === undefined ? undefined : fieldId('shifts-error')}
        >
          <legend className={formStyles.label}>{WORK_CENTER_CALENDAR_TEXT.shifts}</legend>
          {shifts.map((row, index) => {
            const rowLabel = WORK_CENTER_CALENDAR_TEXT.shiftRow(index + 1)
            return (
              <div key={row.rowId} className={styles.row} role="group" aria-label={rowLabel} data-shift-row={index}>
                <label className={styles.cell}>
                  <span className={styles.cellLabel}>{WORK_CENTER_CALENDAR_TEXT.day}</span>
                  <select
                    className={formStyles.select}
                    value={row.day}
                    disabled={frozen}
                    onChange={(event) => updateShift(row.rowId, { day: event.target.value })}
                  >
                    {CALENDAR_DAYS.map((day) => (
                      <option key={day} value={day}>
                        {calendarDayLabel(day)}
                      </option>
                    ))}
                  </select>
                </label>
                <label className={styles.cell}>
                  <span className={styles.cellLabel}>{WORK_CENTER_CALENDAR_TEXT.start}</span>
                  <input
                    type="time"
                    step={60}
                    className={formStyles.input}
                    value={row.start}
                    readOnly={frozen}
                    onChange={(event) => updateShift(row.rowId, { start: event.target.value })}
                  />
                </label>
                <label className={styles.cell}>
                  <span className={styles.cellLabel}>{WORK_CENTER_CALENDAR_TEXT.end}</span>
                  <input
                    type="time"
                    step={60}
                    className={formStyles.input}
                    value={row.end}
                    readOnly={frozen}
                    onChange={(event) => updateShift(row.rowId, { end: event.target.value })}
                  />
                </label>
                {isTimeToken(row.start) && isTimeToken(row.end) && row.end < row.start ? (
                  <span className={styles.cellLabel} data-next-day="true">
                    {WORK_CENTER_CALENDAR_TEXT.nextDay}
                  </span>
                ) : null}
                <button
                  type="button"
                  className={formStyles.secondary}
                  disabled={frozen}
                  aria-label={`${rowLabel}: ${WORK_CENTER_CALENDAR_TEXT.removeShift}`}
                  onClick={() => setShifts((rows) => rows.filter((candidate) => candidate.rowId !== row.rowId))}
                >
                  {WORK_CENTER_CALENDAR_TEXT.removeShift}
                </button>
              </div>
            )
          })}
          <button
            type="button"
            className={formStyles.secondary}
            disabled={frozen || shifts.length >= CALENDAR_LIMITS.shifts}
            onClick={() =>
              setShifts((rows) => [
                ...rows,
                { rowId: mintRowId(), day: CALENDAR_DAYS[0], start: '', end: '' },
              ])
            }
          >
            {WORK_CENTER_CALENDAR_TEXT.addShift}
          </button>
          <FieldError id={fieldId('shifts-error')} text={errors.shifts} />
        </fieldset>

        <fieldset
          className={styles.fieldset}
          aria-invalid={errors.dates !== undefined}
          aria-describedby={errors.dates === undefined ? undefined : fieldId('dates-error')}
        >
          <legend className={formStyles.label}>{WORK_CENTER_CALENDAR_TEXT.closedDates}</legend>
          {dates.map((row, index) => {
            const rowLabel = WORK_CENTER_CALENDAR_TEXT.closedDateRow(index + 1)
            return (
              <div key={row.rowId} className={styles.row} role="group" aria-label={rowLabel} data-date-row={index}>
                <label className={styles.cell}>
                  <span className={styles.cellLabel}>{WORK_CENTER_CALENDAR_TEXT.closedDate}</span>
                  <input
                    type="date"
                    className={formStyles.input}
                    value={row.value}
                    readOnly={frozen}
                    onChange={(event) => updateDate(row.rowId, event.target.value)}
                  />
                </label>
                <button
                  type="button"
                  className={formStyles.secondary}
                  disabled={frozen}
                  aria-label={`${rowLabel}: ${WORK_CENTER_CALENDAR_TEXT.removeClosedDate}`}
                  onClick={() => setDates((rows) => rows.filter((candidate) => candidate.rowId !== row.rowId))}
                >
                  {WORK_CENTER_CALENDAR_TEXT.removeClosedDate}
                </button>
              </div>
            )
          })}
          <button
            type="button"
            className={formStyles.secondary}
            disabled={frozen || dates.length >= CALENDAR_LIMITS.closedDates}
            onClick={() => setDates((rows) => [...rows, { rowId: mintRowId(), value: '' }])}
          >
            {WORK_CENTER_CALENDAR_TEXT.addClosedDate}
          </button>
          <FieldError id={fieldId('dates-error')} text={errors.dates} />
        </fieldset>

        <div className={formStyles.field}>
          <label className={formStyles.label} htmlFor={fieldId('reason')}>
            {MASTER_TEXT.reason}
          </label>
          <textarea
            id={fieldId('reason')}
            className={formStyles.textarea}
            value={reason}
            rows={2}
            maxLength={REASON_MAX_LENGTH}
            readOnly={frozen}
            aria-invalid={errors.reason !== undefined}
            aria-describedby={errors.reason === undefined ? undefined : fieldId('reason-error')}
            onChange={(event) => setReason(event.target.value)}
          />
          <FieldError id={fieldId('reason-error')} text={errors.reason} />
        </div>

        <div className={formStyles.actions}>
          {phase.kind === 'unknown' ? null : (
            <button type="submit" className={formStyles.primary} disabled={busy}>
              {MASTER_TEXT.save}
            </button>
          )}
          <button type="button" className={formStyles.secondary} onClick={onCancel} disabled={frozen}>
            {MASTER_TEXT.cancel}
          </button>
        </div>
      </form>
    </section>
  )
}

function FieldError({ id, text }: { readonly id: string; readonly text: string | undefined }) {
  if (text === undefined) return null
  return (
    <p id={id} className={formStyles.fieldError} role="alert">
      {text}
    </p>
  )
}

function Notice({
  phase,
  noticeRef,
  onResend,
  onReloadRecord,
  onReloadAfterRefusal,
}: {
  readonly phase: Phase
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
  readonly onResend: () => void
  readonly onReloadRecord: () => Promise<void>
  readonly onReloadAfterRefusal: () => Promise<void>
}) {
  if (phase.kind === 'failed') {
    const { title, detail } = calendarFailureText(phase.failure)
    return (
      <div ref={noticeRef} tabIndex={-1} className={formStyles.noticeError} role="alert" data-outcome={phase.failure.kind}>
        <p className={formStyles.noticeTitle}>{title}</p>
        <p>{detail}</p>
        {phase.failure.kind === 'version-conflict' ? (
          <>
            <p className={formStyles.conflict}>
              {conflictVersionsText(
                phase.failure.conflict.expectedVersion,
                phase.failure.conflict.currentVersion,
                phase.failure.conflictIsCurrent,
              )}
            </p>
            {/* Closes the attempt and re-reads the calendar, KEEPING what the user typed. */}
            <button type="button" className={formStyles.secondary} onClick={() => void onReloadAfterRefusal()}>
              {MASTER_TEXT.reload}
            </button>
          </>
        ) : null}
      </div>
    )
  }

  if (phase.kind !== 'unknown') return null
  const { title, detail } = unknownText(phase.unknown)
  return (
    <div ref={noticeRef} tabIndex={-1} className={formStyles.noticeWarn} role="alert" data-outcome="unknown">
      <p className={formStyles.noticeTitle}>{title}</p>
      <p>{detail}</p>
      {phase.lastRefusal === undefined ? null : (
        <p data-refusal={phase.lastRefusal.kind}>{masterRetryRefusedText(phase.lastRefusal)}</p>
      )}
      <div className={formStyles.actions}>
        {/* Same snapshot, same key — the server replays rather than writing twice. */}
        <button type="button" className={formStyles.primary} onClick={onResend}>
          {MASTER_TEXT.resend}
        </button>
        {/* Re-reads the calendar ONLY; the unresolved key and command are kept. */}
        <button type="button" className={formStyles.secondary} onClick={() => void onReloadRecord()}>
          {MASTER_TEXT.reload}
        </button>
      </div>
    </div>
  )
}
