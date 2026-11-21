/**
 * Ayarlar > İş merkezi kapasitesi — URT-D89 (URT-D87 the unit, URT-D91/URT-D92 the buffer). Standing on
 * `settings.work-center-capacities.manage` (company-wide, not Critical, granted to no role here; the production manager is
 * the decided persona, a note only).
 *
 * The screen lists the company's work centers (the Organization owner's read) with the capacity the Settings owner stores
 * for each one: input kg per hour and an optional safety buffer in hours or days. Nothing is seeded and nothing is
 * pre-filled with an invented value: a work center without a capacity says so ("termin hesaplanamaz"), a missing buffer
 * is shown as zero. Recording a new version is one authorized person's act with a required reason, the work center's
 * expected version and an Idempotency-Key; the server checks the work center exists in the company and audits the change.
 *
 * The work center list needs the person's own Organization read; when that read is refused or fails, its state is shown
 * instead of a list the screen cannot name. Remounted per committed context and route entry; a group (multi-company)
 * context shows the no-company state; a version conflict or a vanished work center closes the form and re-reads; an
 * unknown outcome keeps the same Idempotency-Key for "retry".
 */

import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import type { RemoteViewState } from '../shell/uiState'
import { fetchWorkCenters } from '../organization/api'
import type { WorkCenterSummary } from '../organization/contract'
import { SETTINGS_CAPABILITY } from './capabilities'
import type { CommandOutcome } from './discountLimitApi'
import { fetchWorkCenterCapacities, recordWorkCenterCapacityVersion } from './workCenterCapacityApi'
import {
  WORK_CENTER_CAPACITY_INPUT,
  WORK_CENTER_CAPACITY_STALE_CODES,
  WORK_CENTER_SAFETY_BUFFER_UNIT,
  WORK_CENTER_SAFETY_BUFFER_UNITS,
  parseCapacityInput,
  parseSafetyBufferInput,
  type WorkCenterCapacityTimeline,
  type WorkCenterCapacityView,
  type WorkCenterSafetyBufferUnit,
} from './workCenterCapacityContract'
import {
  WORK_CENTER_CAPACITY_TEXT as T,
  decimalLabel,
  safetyBufferLabel,
  safetyBufferUnitLabel,
  workCenterCapacityRefusalText,
} from './workCenterCapacityText'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

export function WorkCenterCapacityPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <CapacityScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function CapacityScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.workCenterCapacity)
  const scope = [namespace ?? 'no-context', shell.routeEntryGeneration]
  const capacities = useBoundaryRead([...scope, 'settings-work-center-capacities'], ready, (signal) =>
    fetchWorkCenterCapacities(companyId ?? '', signal),
  )
  const workCenters = useBoundaryRead([...scope, 'settings-work-center-capacities', 'work-centers'], ready, (signal) =>
    fetchWorkCenters(companyId ?? '', signal),
  )

  if (!singleCompany) {
    return (
      <Frame title={T.title} intro={T.intro}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      </Frame>
    )
  }

  if (!ready || companyId === null) {
    return (
      <Frame title={T.title} intro={T.intro}>
        <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
      </Frame>
    )
  }

  const capacityState = stateOf<WorkCenterCapacityView>(capacities.isPending, capacities.data)
  const workCenterState = stateOf<readonly WorkCenterSummary[]>(workCenters.isPending, workCenters.data)
  const refetch = () => {
    void capacities.refetch()
    void workCenters.refetch()
  }
  return (
    <Frame title={T.title} intro={T.intro}>
      {capacityState.kind !== 'ready' ? (
        <StateBlock state={capacityState} subject={T.subject} onRetry={() => void capacities.refetch()} />
      ) : workCenterState.kind !== 'ready' ? (
        <StateBlock
          state={workCenterState as RemoteViewState<unknown>}
          subject={T.workCentersSubject}
          onRetry={() => void workCenters.refetch()}
        />
      ) : (
        <CapacitySection
          view={capacityState.data}
          workCenters={workCenterState.data}
          companyId={companyId}
          onChanged={refetch}
        />
      )}
    </Frame>
  )
}

interface Row {
  readonly workCenterId: string
  readonly code: string | null
  readonly name: string
  readonly isActive: boolean
  readonly timeline: WorkCenterCapacityTimeline | null
}

/** The Organization list in its own order, then any stored timeline whose work center the list does not hold. */
function rowsOf(view: WorkCenterCapacityView, workCenters: readonly WorkCenterSummary[]): Row[] {
  const byId = new Map(view.workCenters.map((timeline) => [timeline.workCenterId, timeline]))
  const rows: Row[] = workCenters.map((workCenter) => ({
    workCenterId: workCenter.id.toLowerCase(),
    code: workCenter.code,
    name: workCenter.name,
    isActive: workCenter.isActive,
    timeline: byId.get(workCenter.id.toLowerCase()) ?? null,
  }))
  const listed = new Set(rows.map((row) => row.workCenterId))
  for (const timeline of view.workCenters) {
    if (!listed.has(timeline.workCenterId)) {
      rows.push({ workCenterId: timeline.workCenterId, code: null, name: T.unlisted, isActive: true, timeline })
    }
  }
  return rows
}

function CapacitySection({
  view,
  workCenters,
  companyId,
  onChanged,
}: {
  readonly view: WorkCenterCapacityView
  readonly workCenters: readonly WorkCenterSummary[]
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [selected, setSelected] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const rows = rowsOf(view, workCenters)
  const selectedRow = rows.find((row) => row.workCenterId === selected) ?? null
  const finish = (text: string | null) => {
    setNotice(text)
    if (text !== null) onChanged()
  }

  return (
    <section aria-labelledby="work-center-capacity-heading" className={styles.section}>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <h2 id="work-center-capacity-heading">{T.tableLabel}</h2>
      {rows.length === 0 ? (
        <p className={styles.muted}>{T.empty}</p>
      ) : (
        <table className={styles.table}>
          <caption>{T.tableLabel}</caption>
          <thead>
            <tr>
              <th scope="col">{T.columnCode}</th>
              <th scope="col">{T.columnName}</th>
              <th scope="col">{T.columnCapacity}</th>
              <th scope="col">{T.columnBuffer}</th>
              <th scope="col">{T.columnVersion}</th>
              <th scope="col">{T.columnActions}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const current = row.timeline?.versions[0] ?? null
              return (
                <tr key={row.workCenterId}>
                  <td>{row.code ?? <code>{row.workCenterId}</code>}</td>
                  <td>
                    {row.name}
                    {row.isActive ? null : ` (${T.inactive})`}
                  </td>
                  <td>{current === null ? T.noCapacity : decimalLabel(current.inputCapacityKgPerHour)}</td>
                  <td>{current === null ? T.noVersion : safetyBufferLabel(current.safetyBufferValue, current.safetyBufferUnit)}</td>
                  <td>{row.timeline?.version ?? T.noVersion}</td>
                  <td>
                    <Button
                      type="button"
                      variant="ghost"
                      aria-label={`${row.name}: ${T.open}`}
                      aria-expanded={selected === row.workCenterId}
                      onClick={() => {
                        setNotice(null)
                        setSelected(selected === row.workCenterId ? null : row.workCenterId)
                      }}
                    >
                      {selected === row.workCenterId ? T.close : T.open}
                    </Button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
      {selectedRow !== null ? (
        <WorkCenterDetail
          key={`${selectedRow.workCenterId}::${selectedRow.timeline?.version ?? '0'}`}
          row={selectedRow}
          companyId={companyId}
          onFinished={finish}
        />
      ) : null}
    </section>
  )
}

function WorkCenterDetail({
  row,
  companyId,
  onFinished,
}: {
  readonly row: Row
  readonly companyId: string
  readonly onFinished: (notice: string | null) => void
}) {
  const [editing, setEditing] = useState(false)
  const versions = row.timeline?.versions ?? []
  const headingId = `work-center-capacity-history-${row.workCenterId}`
  const done = (text: string | null) => {
    setEditing(false)
    onFinished(text)
  }

  return (
    <section aria-labelledby={headingId} className={styles.section}>
      <h3 id={headingId}>{T.historyHeading(row.name)}</h3>
      {versions.length === 0 ? (
        <p className={styles.muted}>{T.historyEmpty}</p>
      ) : (
        <table className={styles.table}>
          <caption>{T.historyHeading(row.name)}</caption>
          <thead>
            <tr>
              <th scope="col">{T.columnVersion}</th>
              <th scope="col">{T.columnCapacity}</th>
              <th scope="col">{T.columnBuffer}</th>
              <th scope="col">{T.columnCurrent}</th>
              <th scope="col">{T.columnRecorded}</th>
              <th scope="col">{T.columnReason}</th>
            </tr>
          </thead>
          <tbody>
            {versions.map((entry) => (
              <tr key={entry.version}>
                <td>{entry.version}</td>
                <td>{decimalLabel(entry.inputCapacityKgPerHour)}</td>
                <td>{safetyBufferLabel(entry.safetyBufferValue, entry.safetyBufferUnit)}</td>
                <td>{entry.isCurrent ? T.isCurrent : T.isPrevious}</td>
                <td>
                  <code>{entry.recordedBy}</code> · <time dateTime={entry.recordedAtUtc}>{entry.recordedAtUtc}</time>
                </td>
                <td>{entry.reason}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editing ? (
        <CapacityForm row={row} companyId={companyId} onDone={done} />
      ) : (
        <div className={styles.actions}>
          <Button type="button" variant="primary" onClick={() => setEditing(true)}>
            {T.newVersion}
          </Button>
        </div>
      )}
    </section>
  )
}

type BufferChoice = 'none' | WorkCenterSafetyBufferUnit

function CapacityForm({
  row,
  companyId,
  onDone,
}: {
  readonly row: Row
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
}) {
  // The form starts from the current stored version, never from a value the screen invents (URT-D91: nothing seeded).
  const current = row.timeline?.versions[0] ?? null
  const [capacityText, setCapacityText] = useState(current === null ? '' : decimalLabel(current.inputCapacityKgPerHour))
  const [bufferChoice, setBufferChoice] = useState<BufferChoice>(current?.safetyBufferUnit ?? 'none')
  const [bufferText, setBufferText] = useState(
    current?.safetyBufferValue === null || current === null ? '' : decimalLabel(current.safetyBufferValue),
  )
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const capacity = parseCapacityInput(capacityText)
  const buffer = bufferChoice === 'none' ? null : parseSafetyBufferInput(bufferText)
  const formId = `work-center-capacity-form-${row.workCenterId}`

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (capacity === null || (bufferChoice !== 'none' && buffer === null)) return
    const body = {
      expectedVersion: row.timeline?.version ?? '0',
      inputCapacityKgPerHour: capacity,
      safetyBufferValue: bufferChoice === 'none' ? null : buffer,
      safetyBufferUnit: bufferChoice === 'none' ? null : bufferChoice,
      reason: reason.trim(),
    }
    const outcome = await attempt.run(body, (idempotencyKey) =>
      recordWorkCenterCapacityVersion({ companyId, idempotencyKey }, row.workCenterId, body),
    )
    if (outcome.type === 'applied') {
      onDone(outcome.replayed ? T.savedReplay : T.saved)
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onDone(stale)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby={`${formId}-heading`}>
      <h3 id={`${formId}-heading`}>{T.newVersion}</h3>
      <p className={styles.muted}>{T.singleDeciderNote}</p>
      <Field
        label={T.capacityLabel}
        help={T.capacityHelp}
        error={capacityText.trim().length > 0 && capacity === null ? T.capacityError : undefined}
      >
        {(control) => (
          <input
            {...control}
            type="text"
            inputMode="decimal"
            required
            autoComplete="off"
            value={capacityText}
            onChange={(event) => setCapacityText(event.target.value)}
          />
        )}
      </Field>
      <fieldset className={styles.fieldset}>
        <legend>{T.bufferLegend}</legend>
        <label className={styles.choice}>
          <input
            type="radio"
            name={`${formId}-buffer`}
            checked={bufferChoice === 'none'}
            onChange={() => setBufferChoice('none')}
          />
          {T.bufferNone}
        </label>
        {WORK_CENTER_SAFETY_BUFFER_UNITS.map((unit) => (
          <label key={unit} className={styles.choice}>
            <input
              type="radio"
              name={`${formId}-buffer`}
              checked={bufferChoice === unit}
              onChange={() => setBufferChoice(unit)}
            />
            {safetyBufferUnitLabel(unit)}
          </label>
        ))}
      </fieldset>
      {bufferChoice !== 'none' ? (
        <Field
          label={`${T.bufferValueLabel} (${safetyBufferUnitLabel(bufferChoice)})`}
          help={T.bufferHelp}
          error={bufferText.trim().length > 0 && buffer === null ? T.bufferError : undefined}
        >
          {(control) => (
            <input
              {...control}
              type="text"
              inputMode="decimal"
              required
              autoComplete="off"
              value={bufferText}
              onChange={(event) => setBufferText(event.target.value)}
            />
          )}
        </Field>
      ) : null}
      {bufferChoice === WORK_CENTER_SAFETY_BUFFER_UNIT.days ? <p className={styles.muted}>{T.bufferDaysNote}</p> : null}
      <Field label={T.reasonLabel} help={T.reasonHelp}>
        {(control) => (
          <textarea
            {...control}
            required
            rows={3}
            maxLength={WORK_CENTER_CAPACITY_INPUT.reasonMaximumLength}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        )}
      </Field>
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => onDone(null)} />
    </form>
  )
}

/** A refusal after which the screen's copy is stale (the version moved, the work center vanished): close and re-read. */
function staleRefusal<D>(outcome: CommandOutcome<D>): string | null {
  if (outcome.type !== 'refused' || outcome.code === null) return null
  return WORK_CENTER_CAPACITY_STALE_CODES.includes(outcome.code)
    ? workCenterCapacityRefusalText(outcome.status, outcome.code)
    : null
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
      {status.kind === 'refused' ? (
        <p role="alert" className={styles.error}>
          {workCenterCapacityRefusalText(status.status, status.code)}
        </p>
      ) : null}
      {status.kind === 'unknown' ? (
        <p role="alert" className={styles.error}>
          {T.unknownOutcome}
        </p>
      ) : null}
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
