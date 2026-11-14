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
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

interface DayRange {
  readonly from: string
  readonly to: string
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
  return <ExchangeRatesScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function ExchangeRatesScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.exchangeRates)
  const [range, setRange] = useState<DayRange>(defaultRange)
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
      <RangeForm range={range} onApply={setRange} />
      {state.kind === 'ready' ? (
        <DaysView
          key={`${state.data.from}:${state.data.to}`}
          list={state.data}
          companyId={companyId}
          controls={controls}
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

function RangeForm({ range, onApply }: { readonly range: DayRange; readonly onApply: (range: DayRange) => void }) {
  const [from, setFrom] = useState(range.from)
  const [to, setTo] = useState(range.to)
  const [invalid, setInvalid] = useState(false)

  function submit(event: FormEvent) {
    event.preventDefault()
    const next = rangeOf(from, to, EXCHANGE_RATE_INPUT.maximumListDays)
    setInvalid(next === null)
    if (next !== null) onApply(next)
  }

  return (
    <form className={styles.searchRow} onSubmit={submit} aria-labelledby="exchange-rate-range-heading">
      <h2 id="exchange-rate-range-heading">{T.rangeHeading}</h2>
      <Field label={T.fromLabel}>
        {(control) => <input {...control} type="date" required value={from} onChange={(event) => setFrom(event.target.value)} />}
      </Field>
      <Field label={T.toLabel} help={T.rangeHelp} error={invalid ? T.rangeInvalid : undefined}>
        {(control) => <input {...control} type="date" required value={to} onChange={(event) => setTo(event.target.value)} />}
      </Field>
      <Button type="submit" variant="primary">
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
  onChanged,
}: {
  readonly list: ExchangeRateDayList
  readonly companyId: string
  readonly controls: Controls
  readonly onChanged: () => void
}) {
  const [editor, setEditor] = useState<Editor>({ kind: 'none' })
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
                  onEdit={setEditor}
                />
              ))}
            </tbody>
          </table>
        )}
      </section>

      {editor.kind === 'correct' ? <CorrectForm key={`c:${editor.day.date}`} {...formProps} day={editor.day} /> : null}
      {editor.kind === 'mark' ? <MarkForm key={`m:${editor.day.date}`} {...formProps} day={editor.day} /> : null}
      {editor.kind === 'reverify' ? <ReverifyForm key={`r:${editor.day.date}`} {...formProps} day={editor.day} /> : null}

      {controls.import && (list.bulletinSourceEnabled || list.halfDaySourceEnabled) ? (
        <ImportPanel companyId={companyId} onImported={onChanged} />
      ) : null}
    </>
  )
}

function DayRow({
  day,
  actions,
  onEdit,
}: {
  readonly day: ExchangeRateDay
  readonly actions: { readonly correct: boolean; readonly mark: boolean; readonly reverify: boolean }
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
            <Button type="button" size="sm" aria-label={`${T.correct} — ${label}`} onClick={() => onEdit({ kind: 'correct', day })}>
              {T.correct}
            </Button>
          ) : null}
          {actions.mark ? (
            <Button type="button" size="sm" aria-label={`${T.markHalfDay} — ${label}`} onClick={() => onEdit({ kind: 'mark', day })}>
              {T.markHalfDay}
            </Button>
          ) : null}
          {actions.reverify ? (
            <Button
              type="button"
              size="sm"
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
      <p role="alert" className={styles.error}>
        {T.unknownOutcome}
      </p>
    )
  }
  return null
}

function Footer<D>({
  status,
  hasPendingRetry,
  onCancel,
}: {
  readonly status: AttemptStatus<D>
  readonly hasPendingRetry: boolean
  readonly onCancel: () => void
}) {
  return (
    <>
      <Outcome status={status} />
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? T.saving : hasPendingRetry ? T.retrySame : T.save}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy'}>
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

function CorrectForm(props: FormProps & { readonly day: ExchangeRateDay }) {
  const [currencyCode, setCurrencyCode] = useState(props.day.rates[0]?.currencyCode ?? '')
  const [sellingRate, setSellingRate] = useState('')
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const rateError = sellingRate.length > 0 && !isRateText(sellingRate) ? T.rateInvalid : undefined

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!isForeignCurrency(currencyCode) || !isRateText(sellingRate) || reason.trim().length === 0) return
    const body = { expectedVersion: props.day.version, currencyCode, sellingRate, reason: reason.trim() }
    finish(
      await attempt.run({ action: 'correct', date: props.day.date, ...body }, (idempotencyKey) =>
        correctExchangeRate({ companyId: props.companyId, idempotencyKey }, props.day.date, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-correct-heading">
      <h3 id="exchange-rate-correct-heading">{T.correctHeading(props.day.date)}</h3>
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
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function MarkForm(props: FormProps & { readonly day: ExchangeRateDay }) {
  const [state, setState] = useState<HalfDayMark | null>(null)
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (state === null || reason.trim().length === 0) return
    const body = { expectedVersion: props.day.version, state, reason: reason.trim() }
    finish(
      await attempt.run({ action: 'mark', date: props.day.date, ...body }, (idempotencyKey) =>
        markExchangeRateHalfDay({ companyId: props.companyId, idempotencyKey }, props.day.date, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-mark-heading">
      <h3 id="exchange-rate-mark-heading">{T.markHeading(props.day.date)}</h3>
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
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function ReverifyForm(props: FormProps & { readonly day: ExchangeRateDay }) {
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (reason.trim().length === 0) return
    const body = { expectedVersion: props.day.version, reason: reason.trim() }
    finish(
      await attempt.run({ action: 'reverify', date: props.day.date, ...body }, (idempotencyKey) =>
        reverifyExchangeRateDay({ companyId: props.companyId, idempotencyKey }, props.day.date, body),
      ),
      props,
    )
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-reverify-heading">
      <h3 id="exchange-rate-reverify-heading">{T.reverifyHeading(props.day.date)}</h3>
      <p className={styles.muted}>{T.reverifyIntro}</p>
      <ReasonField value={reason} onChange={setReason} />
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => props.onDone(null)} />
    </form>
  )
}

function ImportPanel({ companyId, onImported }: { readonly companyId: string; readonly onImported: () => void }) {
  const today = todayInIstanbul()
  const [from, setFrom] = useState(today)
  const [to, setTo] = useState(today)
  const [invalid, setInvalid] = useState(false)
  const attempt = useCommandAttempt<ExchangeRateImportSummary>()
  const busy = attempt.status.kind === 'busy'

  async function submit(event: FormEvent) {
    event.preventDefault()
    const range = rangeOf(from, to, EXCHANGE_RATE_INPUT.maximumImportDays)
    setInvalid(range === null)
    if (range === null) return
    const outcome = await attempt.run({ action: 'import', ...range }, (idempotencyKey) =>
      importExchangeRates({ companyId, idempotencyKey }, range),
    )
    if (outcome.type === 'applied') onImported()
  }

  return (
    <section aria-labelledby="exchange-rate-import-heading" className={styles.section}>
      <h2 id="exchange-rate-import-heading">{T.importHeading}</h2>
      <p className={styles.muted}>{T.importIntro}</p>
      <form className={styles.searchRow} onSubmit={(event) => void submit(event)} aria-labelledby="exchange-rate-import-heading">
        <Field label={T.fromLabel}>
          {(control) => <input {...control} type="date" required value={from} onChange={(event) => setFrom(event.target.value)} />}
        </Field>
        <Field label={T.toLabel} error={invalid ? T.importRangeInvalid : undefined}>
          {(control) => <input {...control} type="date" required value={to} onChange={(event) => setTo(event.target.value)} />}
        </Field>
        <Button type="submit" variant="primary" disabled={busy} aria-busy={busy}>
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
