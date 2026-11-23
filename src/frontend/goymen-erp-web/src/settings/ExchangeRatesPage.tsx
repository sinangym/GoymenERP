/**
 * Ayarlar > Ana Veriler > Kurlar — F-025 / B-11 exchange rate days (UI-K03; KUR_GUNU, SAT-D55, SAT-D169…D172, SAT-D188),
 * on the read name `settings.exchange-rates.read` with three command controls (correct, half-day mark, import — the
 * import name also runs a reverification). None is granted to a role here (a human gate).
 *
 *  - The days of a chosen range (at most 62): the TCMB bulletin state, the half-day state with its provenance, the
 *    selling rates as the server's decimal text (a manual correction is marked) and each day's record history.
 *  - A rate lookup shows the one backend calculator's answer. When the calculator gives no rate the screen shows
 *    "Kur yok — işlem durur" with the reason, never an older or default rate ("kur yoksa işlem durur").
 *  - Every change: a mandatory reason, the day's expected version, one Idempotency-Key (kept for a retry after an unknown
 *    outcome — a 5xx, including the code-less 503 of a disabled bulletin source). A version conflict closes the form and
 *    re-reads. The screen hides an action the server would refuse and the server re-checks every one.
 *  - Remounted per committed context and route entry; a group (multi-company) context shows the no-company state.
 */

import { EditedAttemptNotice } from './EditedAttemptNotice'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { SETTINGS_CAPABILITY, SETTINGS_EXCHANGE_RATE_CONTROL } from './capabilities'
import type { CommandOutcome } from './discountLimitApi'
import { isDateText } from './discountLimitContract'
import {
  correctExchangeRate,
  fetchExchangeRateDays,
  fetchExchangeRateResolution,
  importExchangeRates,
  markExchangeRateHalfDay,
  reverifyExchangeRateDay,
} from './exchangeRateApi'
import {
  EXCHANGE_RATE_INPUT,
  EXCHANGE_RATE_PROBLEM,
  FINAL_BULLETIN_STATES,
  HALF_DAY_MARKS,
  addDays,
  inclusiveDayCount,
  isForeignCurrency,
  isRateText,
  normalizeCurrency,
  todayInIstanbul,
  type ExchangeRateDay,
  type ExchangeRateDayList,
  type ExchangeRateImportSummary,
  type ExchangeRateResolution,
  type HalfDayMark,
  type CorrectRateBody,
  type MarkHalfDayBody,
  type ReverifyDayBody,
  type ImportRatesBody,
} from './exchangeRateContract'
import {
  BULLETIN_STATE_LABEL,
  EXCHANGE_RATE_TEXT as T,
  HALF_DAY_STATE_LABEL,
  IMPORT_OUTCOME_LABEL,
  PROVENANCE_LABEL,
  RECORD_KIND_LABEL,
  exchangeRateRefusalText,
  formatDate,
  formatInstant,
  recordStateLabel,
  unavailableReasonText,
} from './exchangeRateText'
import { hasCommandFields, useScopedCommandAttempt, type AttemptStatus, type ScopedCommandAttempt } from './useCommandAttempt'
import styles from './settings.module.css'

interface DayRange {
  readonly from: string
  readonly to: string
}

interface DayCommand<B> { readonly date: string; readonly body: B }
interface Attempts {
  readonly correct: ScopedCommandAttempt<DayCommand<CorrectRateBody>, true>
  readonly mark: ScopedCommandAttempt<DayCommand<MarkHalfDayBody>, true>
  readonly reverify: ScopedCommandAttempt<DayCommand<ReverifyDayBody>, true>
  readonly import: ScopedCommandAttempt<ImportRatesBody, ExchangeRateImportSummary>
}

function dayPayload(value: unknown): value is { date: string; body: Record<string, unknown> } {
  return hasCommandFields(value, { date: 'string', body: 'object' }) && isDateText(value.date)
}
function correctPayload(value: unknown): value is DayCommand<CorrectRateBody> {
  return dayPayload(value) && hasCommandFields(value.body,
    { expectedVersion: 'string', currencyCode: 'string', sellingRate: 'string', reason: 'string' })
    && /^(0|[1-9][0-9]*)$/.test(value.body.expectedVersion as string)
}
function markPayload(value: unknown): value is DayCommand<MarkHalfDayBody> {
  return dayPayload(value) && hasCommandFields(value.body, { expectedVersion: 'string', state: 'string', reason: 'string' })
    && /^(0|[1-9][0-9]*)$/.test(value.body.expectedVersion as string)
    && HALF_DAY_MARKS.some(entry => entry === value.body.state)
}
function reverifyPayload(value: unknown): value is DayCommand<ReverifyDayBody> {
  return dayPayload(value) && hasCommandFields(value.body, { expectedVersion: 'string', reason: 'string' })
    && /^(0|[1-9][0-9]*)$/.test(value.body.expectedVersion as string)
}
function importPayload(value: unknown): value is ImportRatesBody {
  return hasCommandFields(value, { from: 'string', to: 'string' }) && isDateText(value.from) && isDateText(value.to)
}

/** A valid range: two calendar days, `from` not after `to`, at most `maximumDays` days. */
function rangeOf(from: string, to: string, maximumDays: number): DayRange | null {
  if (!isDateText(from) || !isDateText(to)) return null
  const days = inclusiveDayCount(from, to)
  return days >= 1 && days <= maximumDays ? { from, to } : null
}

function defaultRange(): DayRange {
  const to = todayInIstanbul()
  return { from: addDays(to, 1 - EXCHANGE_RATE_INPUT.defaultListDays), to }
}

export function ExchangeRatesPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <ExchangeRatesScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}::${shell.sessionBinding ?? ''}`} namespace={namespace} />
}

function ExchangeRatesScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.exchangeRates)
  const correct = useScopedCommandAttempt<DayCommand<CorrectRateBody>, true>('exchange-rate-correct',
    (payload, idempotencyKey) => correctExchangeRate({ companyId: companyId ?? '', idempotencyKey }, payload.date, payload.body), correctPayload)
  const mark = useScopedCommandAttempt<DayCommand<MarkHalfDayBody>, true>('exchange-rate-mark',
    (payload, idempotencyKey) => markExchangeRateHalfDay({ companyId: companyId ?? '', idempotencyKey }, payload.date, payload.body), markPayload)
  const reverify = useScopedCommandAttempt<DayCommand<ReverifyDayBody>, true>('exchange-rate-reverify',
    (payload, idempotencyKey) => reverifyExchangeRateDay({ companyId: companyId ?? '', idempotencyKey }, payload.date, payload.body), reverifyPayload)
  const importing = useScopedCommandAttempt<ImportRatesBody, ExchangeRateImportSummary>('exchange-rate-import',
    (body, idempotencyKey) => importExchangeRates({ companyId: companyId ?? '', idempotencyKey }, body), importPayload)
  const attempts: Attempts = { correct, mark, reverify, import: importing }
  const blocked = Object.values(attempts).some(attempt => attempt.hasPendingRetry)
  const [range, setRange] = useState<DayRange>(() => {
    const date = correct.pendingPayload?.date ?? mark.pendingPayload?.date ?? reverify.pendingPayload?.date
    return date !== undefined && isDateText(date) ? { from: date, to: date } : defaultRange()
  })
  const list = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-exchange-rates', range.from, range.to],
    enabled: ready,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchExchangeRateDays(companyId ?? '', range.from, range.to, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  if (!singleCompany) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.contextSubject} />
      </Frame>
    )
  }

  if (!ready || companyId === null || namespace === null) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'not-found' }} subject={T.contextSubject} />
      </Frame>
    )
  }

  const controls = {
    correct: hasCapability(shell, SETTINGS_EXCHANGE_RATE_CONTROL.correct),
    markHalfDay: hasCapability(shell, SETTINGS_EXCHANGE_RATE_CONTROL.markHalfDay),
    import: hasCapability(shell, SETTINGS_EXCHANGE_RATE_CONTROL.import),
  }
  const state = (list.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(list.data ?? ({ type: 'malformed' } as ViewOutcome<ExchangeRateDayList>))) as RemoteViewState<ExchangeRateDayList>
  return (
    <Frame>
      <RangeForm range={range} disabled={blocked} onApply={next => { if (!blocked) setRange(next) }} />
      {state.kind === 'ready' ? (
        <DaysView
          key={`${state.data.from}:${state.data.to}`}
          list={state.data}
          companyId={companyId}
          controls={controls}
          attempts={attempts}
          blocked={blocked}
          onChanged={() => void list.refetch()}
        />
      ) : (
        <StateBlock state={state} subject={T.listSubject} onRetry={() => void list.refetch()} />
      )}
      <ResolutionPanel namespace={namespace} companyId={companyId} />
    </Frame>
  )
}

function Frame({ children }: { readonly children: ReactNode }) {
  return (
    <div className={styles.page}>
      <h1>{T.title}</h1>
      <p className={styles.muted}>{T.intro}</p>
      {children}
    </div>
  )
}

function RangeForm({ range, disabled, onApply }: { readonly range: DayRange; readonly disabled: boolean; readonly onApply: (range: DayRange) => void }) {
  const [from, setFrom] = useState(range.from)
  const [to, setTo] = useState(range.to)
  const [invalid, setInvalid] = useState(false)

  function submit(event: FormEvent) {
    event.preventDefault()
    if (disabled) return
    const next = rangeOf(from, to, EXCHANGE_RATE_INPUT.maximumListDays)
    setInvalid(next === null)
    if (next !== null) onApply(next)
  }

  return (
    <form className={styles.searchRow} onSubmit={submit} aria-labelledby="exchange-rate-range-heading">
      <h2 id="exchange-rate-range-heading">{T.rangeHeading}</h2>
      <Field label={T.fromLabel}>
        {(control) => <input {...control} type="date" required disabled={disabled} value={from} onChange={(event) => setFrom(event.target.value)} />}
      </Field>
      <Field label={T.toLabel} help={T.rangeHelp} error={invalid ? T.rangeInvalid : undefined}>
        {(control) => <input {...control} type="date" required disabled={disabled} value={to} onChange={(event) => setTo(event.target.value)} />}
      </Field>
      <Button type="submit" variant="primary" disabled={disabled}>
        {T.show}
      </Button>
    </form>
  )
}

interface Controls {
  readonly correct: boolean
  readonly markHalfDay: boolean
  readonly import: boolean
}

type Editor =
  | { readonly kind: 'none' }
  | { readonly kind: 'correct'; readonly day: ExchangeRateDay }
  | { readonly kind: 'mark'; readonly day: ExchangeRateDay }
  | { readonly kind: 'reverify'; readonly day: ExchangeRateDay }

/** The official half-day answer is final once the source decided it; only then is a manual mark refused (409). */
function halfDayDecidedOfficially(day: ExchangeRateDay): boolean {
  return day.halfDayProvenance === 'Official' && day.halfDayState !== 'Undetermined'
}

function DaysView({
  list,
  companyId,
  controls,
  attempts,
  blocked,
  onChanged,
}: {
  readonly list: ExchangeRateDayList
  readonly companyId: string
  readonly controls: Controls
  readonly attempts: Attempts
  readonly blocked: boolean
  readonly onChanged: () => void
}) {
  const [selectedEditor, setEditor] = useState<Editor>({ kind: 'none' })
  const pendingKind = attempts.correct.hasPendingRetry ? 'correct' : attempts.mark.hasPendingRetry ? 'mark'
    : attempts.reverify.hasPendingRetry ? 'reverify' : null
  const pendingDay = pendingKind === null ? null
    : list.days.find(day => day.date === attempts[pendingKind].pendingPayload?.date) ?? null
  const editor: Editor = pendingKind !== null && pendingDay !== null ? { kind: pendingKind, day: pendingDay } : selectedEditor
  const [notice, setNotice] = useState<string | null>(null)
  const today = todayInIstanbul()
  const close = (saved: string | null) => {
    setEditor({ kind: 'none' })
    setNotice(saved)
    if (saved !== null) onChanged()
  }
  const conflict = () => {
    setEditor({ kind: 'none' })
    setNotice(T.conflict)
    onChanged()
  }
  const formProps = { companyId, onDone: close, onConflict: conflict }

  return (
    <>
      {!list.bulletinSourceEnabled ? (
        <p role="note" className={styles.notice}>
          {T.bulletinSourceDisabled}
        </p>
      ) : null}
      {!list.halfDaySourceEnabled ? (
        <p role="note" className={styles.notice}>
          {T.halfDaySourceDisabled}
        </p>
      ) : null}
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <section aria-labelledby="exchange-rate-days-heading" className={styles.section}>
        <h2 id="exchange-rate-days-heading">{T.tableCaption(list.from, list.to)}</h2>
        {list.days.length === 0 ? (
          <StateBlock state={{ kind: 'empty-no-data' }} subject={T.listSubject} />
        ) : (
          <table className={styles.table}>
            <caption>{T.tableCaption(list.from, list.to)}</caption>
            <thead>
              <tr>
                <th scope="col">{T.columnDay}</th>
                <th scope="col">{T.columnBulletin}</th>
                <th scope="col">{T.columnHalfDay}</th>
                <th scope="col">{T.columnRates}</th>
                <th scope="col">{T.columnActions}</th>
              </tr>
            </thead>
            <tbody>
              {list.days.map((day) => (
                <DayRow
                  key={day.date}
                  day={day}
                  actions={{
                    correct: controls.correct && day.bulletinState === 'Published',
                    mark: controls.markHalfDay && day.date <= today && !halfDayDecidedOfficially(day),
                    reverify:
                      controls.import && list.bulletinSourceEnabled && FINAL_BULLETIN_STATES.includes(day.bulletinState),
                  }}
                  disabled={blocked}
                  onEdit={next => { if (!blocked) setEditor(next) }}
                />
              ))}
            </tbody>
          </table>
        )}
      </section>

      {editor.kind === 'correct' && controls.correct ? <CorrectForm key={`c:${editor.day.date}`} {...formProps} day={editor.day}
        attempt={attempts.correct} disabled={blocked && !attempts.correct.hasPendingRetry} /> : null}
      {editor.kind === 'mark' && controls.markHalfDay ? <MarkForm key={`m:${editor.day.date}`} {...formProps} day={editor.day}
        attempt={attempts.mark} disabled={blocked && !attempts.mark.hasPendingRetry} /> : null}
      {editor.kind === 'reverify' && controls.import ? <ReverifyForm key={`r:${editor.day.date}`} {...formProps} day={editor.day}
        attempt={attempts.reverify} disabled={blocked && !attempts.reverify.hasPendingRetry} /> : null}
      {pendingKind !== null && pendingDay === null
        && (pendingKind === 'correct' ? controls.correct : pendingKind === 'mark' ? controls.markHalfDay : controls.import) ? (
        <div className={styles.form}>
          <Outcome status={attempts[pendingKind].status} />
          <Button type="button" disabled={attempts[pendingKind].status.kind === 'busy'}
            onClick={() => void attempts[pendingKind].retry().then(outcome => finish(outcome, formProps))}>{T.retrySame}</Button>
        </div>
      ) : null}

      {controls.import && (list.bulletinSourceEnabled || list.halfDaySourceEnabled || attempts.import.hasPendingRetry) ? (
        <ImportPanel attempt={attempts.import} disabled={blocked && !attempts.import.hasPendingRetry} onImported={onChanged} />
      ) : null}
    </>
  )
}

function DayRow({
  day,
  actions,
  disabled,
  onEdit,
}: {
  readonly day: ExchangeRateDay
  readonly actions: { readonly correct: boolean; readonly mark: boolean; readonly reverify: boolean }
  readonly disabled: boolean
  readonly onEdit: (editor: Editor) => void
}) {
  const label = formatDate(day.date)
  return (
    <tr>
      <th scope="row">{label}</th>
      <td>{BULLETIN_STATE_LABEL[day.bulletinState]}</td>
      <td>
        {HALF_DAY_STATE_LABEL[day.halfDayState]}
        {day.halfDayProvenance === null ? null : ` ${T.provenanceOf(PROVENANCE_LABEL[day.halfDayProvenance])}`}
      </td>
      <td>
        {day.rates.length === 0 ? (
          T.noRates
        ) : (
          <ul aria-label={`${T.columnRates} — ${label}`} className={styles.plainList}>
            {day.rates.map((rate) => (
              <li key={rate.currencyCode}>
                <span>{T.rateOf(rate.currencyCode, rate.sellingRate)}</span>
                {rate.provenance === 'Manual' ? <em>{T.manualBadge}</em> : null}
              </li>
            ))}
          </ul>
        )}
        {day.records.length > 0 ? (
          <details>
            <summary>{T.historySummary(day.records.length)}</summary>
            <ul aria-label={`${T.historySummary(day.records.length)} — ${label}`} className={styles.plainList}>
              {day.records.map((record) => (
                <li key={record.recordId}>
                  <span>
                    {T.historyEntry(
                      RECORD_KIND_LABEL[record.kind],
                      recordStateLabel(record.kind, record.state),
                      PROVENANCE_LABEL[record.provenance],
                      record.recordedBy,
                      formatInstant(record.recordedAtUtc),
                    )}
                  </span>
                  {record.rates.length > 0 ? (
                    <span>{record.rates.map((rate) => T.rateOf(rate.currencyCode, rate.sellingRate)).join(', ')}</span>
                  ) : null}
                  {record.reason === null ? null : <span>{T.historyReason(record.reason)}</span>}
                  {record.sourceReference === null ? null : <span>{T.historySource(record.sourceReference)}</span>}
                </li>
              ))}
            </ul>
          </details>
        ) : null}
      </td>
      <td>
        <div className={styles.actions}>
          {actions.correct ? (
            <Button type="button" size="sm" disabled={disabled} aria-label={`${T.correct} — ${label}`} onClick={() => onEdit({ kind: 'correct', day })}>
              {T.correct}
            </Button>
          ) : null}
          {actions.mark ? (
            <Button type="button" size="sm" disabled={disabled} aria-label={`${T.markHalfDay} — ${label}`} onClick={() => onEdit({ kind: 'mark', day })}>
              {T.markHalfDay}
            </Button>
          ) : null}
          {actions.reverify ? (
            <Button
              type="button"
              size="sm"
              disabled={disabled}
              aria-label={`${T.reverify} — ${label}`}
              onClick={() => onEdit({ kind: 'reverify', day })}
            >
              {T.reverify}
            </Button>
          ) : null}
        </div>
      </td>
    </tr>
  )
}

interface FormProps {
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
  readonly onConflict: () => void
  readonly disabled?: boolean
}

function finish<D>(outcome: CommandOutcome<D>, props: FormProps) {
  if (outcome.type === 'applied') props.onDone(outcome.replayed ? T.savedReplay : T.saved)
  else if (outcome.type === 'refused' && outcome.code === EXCHANGE_RATE_PROBLEM.versionConflict) props.onConflict()
}

function Outcome<D>({ status }: { readonly status: AttemptStatus<D> }) {
  if (status.kind === 'refused') {
    return (
      <p role="alert" className={styles.error}>
        {exchangeRateRefusalText(status.status, status.code)}
      </p>
    )
  }
  if (status.kind === 'unknown') {
    return (
      <>
        <p role="alert" className={styles.error}>
          {T.unknownOutcome}
        </p>
        <EditedAttemptNotice status={status} />
      </>
    )
  }
  return null
}

function Footer<D>({
  status,
  hasPendingRetry,
  onCancel,
  disabled = false,
}: {
  readonly status: AttemptStatus<D>
  readonly hasPendingRetry: boolean
  readonly onCancel: () => void
  readonly disabled?: boolean
}) {
  return (
    <>
      <Outcome status={status} />
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={disabled || status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? T.saving : hasPendingRetry ? T.retrySame : T.save}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy' || hasPendingRetry}>
          {T.cancel}
        </Button>
      </div>
    </>
  )
}

function ReasonField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field label={T.reasonLabel} help={T.reasonHelp}>
      {(control) => (
        <textarea
          {...control}
          required
          rows={3}
          maxLength={EXCHANGE_RATE_INPUT.reasonMaximumLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

function CorrectForm(props: FormProps & { readonly day: ExchangeRateDay; readonly attempt: Attempts['correct'] }) {
  const attempt = props.attempt
  const [currencyCode, setCurrencyCode] = useState(attempt.pendingPayload?.body.currencyCode ?? props.day.rates[0]?.currencyCode ?? '')
  const [sellingRate, setSellingRate] = useState(attempt.pendingPayload?.body.sellingRate ?? '')
  const [reason, setReason] = useState(attempt.pendingPayload?.body.reason ?? '')
  const rateError = sellingRate.length > 0 && !isRateText(sellingRate) ? T.rateInvalid : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (props.disabled) return
    if (attempt.hasPendingRetry) { finish(await attempt.retry(), props); return }
    if (!isForeignCurrency(currencyCode) || !isRateText(sellingRate) || reason.trim().length === 0) return
    const body = { expectedVersion: props.day.version, currencyCode, sellingRate, reason: reason.trim() }
    finish(
      await attempt.run({ date: props.day.date, body }),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-correct-heading">
      <h3 id="exchange-rate-correct-heading">{T.correctHeading(props.day.date)}</h3>
      <fieldset className={styles.commandFields} disabled={props.disabled || attempt.status.kind === 'busy' || attempt.hasPendingRetry}>
        <Field label={T.currencyLabel} help={T.currencyHelp}>
          {(control) => (
            <select {...control} required value={currencyCode} onChange={(event) => setCurrencyCode(event.target.value)}>
              {props.day.rates.map((rate) => (
                <option key={rate.currencyCode} value={rate.currencyCode}>
                  {T.rateOf(rate.currencyCode, rate.sellingRate)}
                </option>
              ))}
            </select>
          )}
        </Field>
        <Field label={T.rateLabel} help={T.rateHelp} error={rateError}>
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              required
              value={sellingRate}
              onChange={(event) => setSellingRate(event.target.value.trim())}
            />
          )}
        </Field>
        <ReasonField value={reason} onChange={setReason} />
      </fieldset>
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} disabled={props.disabled} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function MarkForm(props: FormProps & { readonly day: ExchangeRateDay; readonly attempt: Attempts['mark'] }) {
  const attempt = props.attempt
  const [state, setState] = useState<HalfDayMark | null>(attempt.pendingPayload?.body.state ?? null)
  const [reason, setReason] = useState(attempt.pendingPayload?.body.reason ?? '')

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (props.disabled) return
    if (attempt.hasPendingRetry) { finish(await attempt.retry(), props); return }
    if (state === null || reason.trim().length === 0) return
    const body = { expectedVersion: props.day.version, state, reason: reason.trim() }
    finish(
      await attempt.run({ date: props.day.date, body }),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-mark-heading">
      <h3 id="exchange-rate-mark-heading">{T.markHeading(props.day.date)}</h3>
      <fieldset className={styles.commandFields} disabled={props.disabled || attempt.status.kind === 'busy' || attempt.hasPendingRetry}>
        <fieldset className={styles.fieldset}>
          <legend>{T.markStateLegend}</legend>
          {HALF_DAY_MARKS.map((mark) => (
            <label key={mark} className={styles.choice}>
              <input
                type="radio"
                name="exchange-rate-half-day"
                required
                checked={state === mark}
                onChange={() => setState(mark)}
              />
              {HALF_DAY_STATE_LABEL[mark]}
            </label>
          ))}
        </fieldset>
        <ReasonField value={reason} onChange={setReason} />
      </fieldset>
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} disabled={props.disabled} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function ReverifyForm(props: FormProps & { readonly day: ExchangeRateDay; readonly attempt: Attempts['reverify'] }) {
  const attempt = props.attempt
  const [reason, setReason] = useState(attempt.pendingPayload?.body.reason ?? '')

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (props.disabled) return
    if (attempt.hasPendingRetry) { finish(await attempt.retry(), props); return }
    if (reason.trim().length === 0) return
    const body = { expectedVersion: props.day.version, reason: reason.trim() }
    finish(
      await attempt.run({ date: props.day.date, body }),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-reverify-heading">
      <h3 id="exchange-rate-reverify-heading">{T.reverifyHeading(props.day.date)}</h3>
      <fieldset className={styles.commandFields} disabled={props.disabled || attempt.status.kind === 'busy' || attempt.hasPendingRetry}>
        <p className={styles.muted}>{T.reverifyIntro}</p>
        <ReasonField value={reason} onChange={setReason} />
      </fieldset>
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} disabled={props.disabled} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function ImportPanel({ attempt, disabled, onImported }: { readonly attempt: Attempts['import']; readonly disabled: boolean; readonly onImported: () => void }) {
  const today = todayInIstanbul()
  const [from, setFrom] = useState(attempt.pendingPayload?.from ?? today)
  const [to, setTo] = useState(attempt.pendingPayload?.to ?? today)
  const [invalid, setInvalid] = useState(false)
  const busy = attempt.status.kind === 'busy'

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (disabled) return
    if (attempt.hasPendingRetry) {
      if ((await attempt.retry()).type === 'applied') onImported()
      return
    }
    const range = rangeOf(from, to, EXCHANGE_RATE_INPUT.maximumImportDays)
    setInvalid(range === null)
    if (range === null) return
    const outcome = await attempt.run(range)
    if (outcome.type === 'applied') onImported()
  }

  return (
    <section aria-labelledby="exchange-rate-import-heading" className={styles.section}>
      <h2 id="exchange-rate-import-heading">{T.importHeading}</h2>
      <p className={styles.muted}>{T.importIntro}</p>
      <form className={styles.searchRow} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-import-heading">
        <Field label={T.fromLabel}>
          {(control) => <input {...control} type="date" required disabled={disabled || busy || attempt.hasPendingRetry} value={from} onChange={(event) => setFrom(event.target.value)} />}
        </Field>
        <Field label={T.toLabel} error={invalid ? T.importRangeInvalid : undefined}>
          {(control) => <input {...control} type="date" required disabled={disabled || busy || attempt.hasPendingRetry} value={to} onChange={(event) => setTo(event.target.value)} />}
        </Field>
        <Button type="submit" variant="primary" disabled={disabled || busy} aria-busy={busy}>
          {busy ? T.importing : attempt.hasPendingRetry ? T.retrySame : T.importSubmit}
        </Button>
      </form>
      <Outcome status={attempt.status} />
      {attempt.status.kind === 'applied' ? <ImportSummary summary={attempt.status.data} /> : null}
    </section>
  )
}

function ImportSummary({ summary }: { readonly summary: ExchangeRateImportSummary }) {
  if (summary.days.length === 0) return <p role="status">{T.importSummaryEmpty}</p>
  return (
    <table className={styles.table}>
      <caption>{T.importSummaryCaption}</caption>
      <thead>
        <tr>
          <th scope="col">{T.columnDay}</th>
          <th scope="col">{T.importColumnBulletin}</th>
          <th scope="col">{T.importColumnHalfDay}</th>
        </tr>
      </thead>
      <tbody>
        {summary.days.map((day) => (
          <tr key={day.date}>
            <th scope="row">{formatDate(day.date)}</th>
            <td>{IMPORT_OUTCOME_LABEL[day.bulletinOutcome]}</td>
            <td>{IMPORT_OUTCOME_LABEL[day.halfDayOutcome]}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

interface ResolutionQuery {
  readonly rateDay: string
  readonly currencyCode: string
}

function ResolutionPanel({ namespace, companyId }: { readonly namespace: string; readonly companyId: string }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const [rateDay, setRateDay] = useState(() => todayInIstanbul())
  const [currency, setCurrency] = useState('')
  const [invalid, setInvalid] = useState(false)
  const [query, setQuery] = useState<ResolutionQuery | null>(null)
  const resolution = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'settings-exchange-rate-resolution', query?.rateDay, query?.currencyCode],
    enabled: query !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchExchangeRateResolution(companyId, query?.rateDay ?? '', query?.currencyCode ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    const currencyCode = normalizeCurrency(currency)
    const valid = isDateText(rateDay) && isForeignCurrency(currencyCode)
    setInvalid(!valid)
    if (valid) setQuery({ rateDay, currencyCode })
  }

  return (
    <section aria-labelledby="exchange-rate-resolution-heading" className={styles.section}>
      <h2 id="exchange-rate-resolution-heading">{T.resolutionHeading}</h2>
      <p className={styles.muted}>{T.resolutionIntro}</p>
      <form className={styles.searchRow} onSubmit={submit} aria-labelledby="exchange-rate-resolution-heading">
        <Field label={T.rateDayLabel}>
          {(control) => <input {...control} type="date" required value={rateDay} onChange={(event) => setRateDay(event.target.value)} />}
        </Field>
        <Field label={T.resolutionCurrencyLabel} help={T.resolutionCurrencyHelp} error={invalid ? T.resolutionInputInvalid : undefined}>
          {(control) => (
            <input
              {...control}
              type="text"
              required
              maxLength={3}
              autoComplete="off"
              value={currency}
              onChange={(event) => setCurrency(event.target.value)}
            />
          )}
        </Field>
        <Button type="submit" variant="primary" disabled={query !== null && resolution.isFetching}>
          {query !== null && resolution.isFetching ? T.resolving : T.resolve}
        </Button>
      </form>
      {query === null ? null : <ResolutionAnswer pending={resolution.isPending} outcome={resolution.data} onRetry={() => void resolution.refetch()} />}
    </section>
  )
}

function ResolutionAnswer({
  pending,
  outcome,
  onRetry,
}: {
  readonly pending: boolean
  readonly outcome: ViewOutcome<ExchangeRateResolution> | undefined
  readonly onRetry: () => void
}) {
  if (!pending && outcome?.type === 'http' && outcome.status === 400) {
    return (
      <p role="alert" className={styles.error}>
        {T.resolutionRejected}
      </p>
    )
  }
  const state = (pending
    ? { kind: 'initial-loading' }
    : classifyOutcome(outcome ?? ({ type: 'malformed' } as ViewOutcome<ExchangeRateResolution>))) as RemoteViewState<ExchangeRateResolution>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.resolutionSubject} onRetry={onRetry} />
  const answer = state.data
  if (answer.state === 'Unavailable') {
    return (
      <div role="status" aria-label={T.resolutionHeading} className={styles.error}>
        <p>
          <strong>{T.unavailable}</strong>
        </p>
        <p>{unavailableReasonText(answer.code)}</p>
        {answer.stoppedAt === null ? null : <p>{T.unavailableStoppedAt(answer.stoppedAt)}</p>}
        {answer.halfDayRuleApplied ? <p>{T.halfDayRuleApplied(answer.previousFullBusinessDay)}</p> : null}
      </div>
    )
  }
  return (
    <div role="status" aria-label={T.resolutionHeading} className={styles.notice}>
      <p>
        <strong>{T.resolvedRate(answer.currencyCode ?? '', answer.sellingRate)}</strong>
      </p>
      <p>{T.resolvedBulletin(answer.bulletinDate)}</p>
      <p>{T.resolvedProvenance(PROVENANCE_LABEL[answer.provenance])}</p>
      {answer.halfDayRuleApplied ? <p>{T.halfDayRuleApplied(answer.previousFullBusinessDay)}</p> : null}
    </div>
  )
}
