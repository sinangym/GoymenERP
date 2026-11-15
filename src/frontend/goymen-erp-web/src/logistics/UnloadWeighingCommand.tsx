/**
 * LOJ-D17 — the manual unload weighing (indirme tartısı) of a CANCELLED loading instruction on L-02-DETAIL,
 * `POST /api/v1/logistics/loading-instructions/{id}/unload-weighings`.
 *
 *  - `UnloadWeighingFacts` is shown on a cancelled instruction only. With no record and loaded goods the backend marks the
 *    instruction `Missing`, shown as "İndirme kaydı yok" (the cancel stays valid). A recorded weighing is shown exactly as the
 *    backend calculated it: the two weighings, tare / gross / net, the kilograms loaded when it was cancelled and the SIGNED
 *    difference net − loaded. The warning is the backend's `differenceWarning` (no threshold is configured, so every difference
 *    warns); it never blocks the record and nothing here recomputes it. No stock movement is created, and the order line's
 *    allocation stays where it is (LOJ-D16).
 *  - `UnloadWeighingForm` is offered on a cancelled instruction marked `Missing`. Like the weighbridge form it is shown to every
 *    holder of the detail: no shell control exists for it, and the server measures `logistics.weighings.record` afresh,
 *    answering a code-less 403 to anyone else. One weighing id is minted per attempt snapshot and travels with it, so a retry
 *    never records a second weighing. A second record is refused by the server (its correction is not decided).
 */

import { useLayoutEffect, useRef, useState } from 'react'
import { recordUnloadWeighing } from './api'
import {
  INSTRUCTION_STATUS,
  UNLOAD_RECORD,
  WEIGHING_DIRECTION,
  parseQuantityInput,
  type InstructionDetail,
  type InstructionMutation,
  type RecordUnloadWeighingRequest,
  type WeighingDirection,
} from './contract'
import { formatInstant } from './format'
import {
  abandon,
  applyOutcome,
  attemptBinding,
  beginSend,
  decideSubmit,
  generateAttemptKey,
  generateClientId,
  isOutcomeStillUnknown,
  liveSnapshot,
  type AttemptSnapshot,
  type AttemptState,
} from './instructionAttempt'
import { commandNotice, refusalMovesRecord } from './instructionAttemptNotice'
import { recallInstructionAttempt, rememberInstructionAttempt } from './pendingInstructionAttempts'
import { INSTRUCTION_DETAIL_TEXT, UNLOAD_WEIGHING_TEXT as T } from './logisticsText'
import type { ShellState } from '../shell/shellState'
import styles from './LogisticsPages.module.css'

export function UnloadWeighingFacts({ detail }: { readonly detail: InstructionDetail }) {
  const { instruction, unloadWeighing } = detail
  if (instruction.status !== INSTRUCTION_STATUS.cancelled) return null
  return (
    <section className={styles.section} aria-labelledby="logistics-unload-weighing-heading" data-testid="logistics-unload-weighing">
      <h2 id="logistics-unload-weighing-heading">{T.heading}</h2>
      {instruction.unloadRecord === UNLOAD_RECORD.missing ? (
        <p className={styles.muted} data-testid="logistics-unload-missing">
          <span className={`${styles.badge} ${styles.blocked}`}>{T.missing}</span> {T.missingNote}
        </p>
      ) : null}
      {unloadWeighing === null ? (
        <p className={styles.muted} data-testid="logistics-unload-weighing-none">
          {T.none}
        </p>
      ) : (
        <>
          {unloadWeighing.differenceWarning ? (
            <p className={styles.notice} data-tone="warning" role="status" data-testid="logistics-unload-weighing-warning">
              {T.warning} {T.noThreshold}
            </p>
          ) : null}
          <dl className={styles.facts}>
            <div>
              <dt>{T.direction}</dt>
              <dd data-testid="logistics-unload-weighing-direction">{T.directionShort[unloadWeighing.direction]}</dd>
            </div>
            <div>
              <dt>{T.first}</dt>
              <dd className={styles.numericValue}>{unloadWeighing.firstWeighingKg}</dd>
            </div>
            <div>
              <dt>{T.second}</dt>
              <dd className={styles.numericValue}>{unloadWeighing.secondWeighingKg}</dd>
            </div>
            <div>
              <dt>{T.tare}</dt>
              <dd className={styles.numericValue} data-testid="logistics-unload-weighing-tare">{unloadWeighing.tareKg}</dd>
            </div>
            <div>
              <dt>{T.gross}</dt>
              <dd className={styles.numericValue} data-testid="logistics-unload-weighing-gross">{unloadWeighing.grossKg}</dd>
            </div>
            <div>
              <dt>{T.net}</dt>
              <dd className={styles.numericValue} data-testid="logistics-unload-weighing-net">{unloadWeighing.netKg}</dd>
            </div>
            <div>
              <dt>{T.loadedAtCancel}</dt>
              <dd className={styles.numericValue} data-testid="logistics-unload-weighing-loaded">{unloadWeighing.loadedKgAtCancel}</dd>
            </div>
            <div>
              <dt>{T.difference}</dt>
              <dd className={styles.numericValue} data-testid="logistics-unload-weighing-difference">{unloadWeighing.differenceKg}</dd>
            </div>
            <div>
              <dt>{T.recordedBy}</dt>
              <dd className={styles.identifier}>{unloadWeighing.recordedBy}</dd>
            </div>
            <div>
              <dt>{T.recordedAt}</dt>
              <dd>{formatInstant(unloadWeighing.recordedAtUtc)}</dd>
            </div>
          </dl>
          <p className={styles.muted}>{T.differenceNote}</p>
        </>
      )}
    </section>
  )
}

const unloadWeighingScope = (instructionId: string) => `logistics.instruction.unload-weighing:${instructionId}`

interface UnloadWeighingFormValues {
  readonly direction: WeighingDirection
  readonly first: string
  readonly second: string
}

export function UnloadWeighingForm({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: InstructionDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const { instruction } = detail
  const slot = attemptBinding(shell, unloadWeighingScope(instruction.id))
  const [initial] = useState(() => recallInstructionAttempt<RecordUnloadWeighingRequest, UnloadWeighingFormValues>(slot))
  const [direction, setDirection] = useState<WeighingDirection>(() => initial?.values.direction ?? WEIGHING_DIRECTION.grossFirst)
  const [first, setFirst] = useState(() => initial?.values.first ?? '')
  const [second, setSecond] = useState(() => initial?.values.second ?? '')
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<AttemptState<RecordUnloadWeighingRequest, InstructionMutation>>(
    () => initial?.attempt ?? { phase: 'idle' })
  const attemptRef = useRef(attempt)
  useLayoutEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  // Only a cancelled instruction marked "indirme kaydı yok" takes one; once recorded no second one is offered.
  const open = instruction.status === INSTRUCTION_STATUS.cancelled && instruction.unloadRecord === UNLOAD_RECORD.missing
    && detail.unloadWeighing === null
  if (!open && attempt.phase === 'idle') return null

  async function send(snapshot: AttemptSnapshot<RecordUnloadWeighingRequest>, values: UnloadWeighingFormValues) {
    const previous = attemptRef.current
    const inFlight = beginSend<RecordUnloadWeighingRequest, InstructionMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    rememberInstructionAttempt(snapshot.binding, inFlight, values)
    const outcome = await recordUnloadWeighing(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      instructionId: instruction.id,
    })
    const next = applyOutcome(inFlight, snapshot, attemptBinding(shell, unloadWeighingScope(instruction.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberInstructionAttempt(snapshot.binding, next, values)
    if (next.phase === 'completed' || refusalMovesRecord(next)) onRecorded()
  }

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    const firstKg = parseQuantityInput(first)
    const secondKg = parseQuantityInput(second)
    if (!firstKg.ok || !secondKg.ok) {
      setError(INSTRUCTION_DETAIL_TEXT.command.validation)
      return
    }
    setError(null)
    const binding = attemptBinding(shell, unloadWeighingScope(instruction.id))
    if (binding === null) return
    const live = liveSnapshot(attemptRef.current)
    // One weighing id per snapshot: a retry of the same values resends the same id; changed values mint a new one.
    const payload: RecordUnloadWeighingRequest =
      live !== null && live.payload.direction === direction && live.payload.firstWeighingKg === firstKg.value
        && live.payload.secondWeighingKg === secondKg.value
        ? live.payload
        : {
            expectedVersion: instruction.version,
            weighingId: generateClientId(),
            direction,
            firstWeighingKg: firstKg.value,
            secondWeighingKg: secondKg.value,
          }
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${instruction.id}/unload-weighings`, binding, companyId, payload },
      generateAttemptKey,
    )
    if (decision.type !== 'send') return
    void send(decision.snapshot, { direction, first: firstKg.value, second: secondKg.value })
  }

  function retry() {
    const live = liveSnapshot(attemptRef.current)
    if (live === null || attemptRef.current.phase === 'submitting') return
    void send(live, { direction: live.payload.direction, first: live.payload.firstWeighingKg, second: live.payload.secondWeighingKg })
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase !== 'idle' && slot !== null) rememberInstructionAttempt(slot, next, { direction, first, second })
  }

  const notice = commandNotice(attempt)
  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const inconsistent = attempt.phase === 'failed' && attempt.problem?.code === 'logistics_weighing_direction_inconsistent'
  return (
    <section className={styles.section} aria-labelledby="logistics-unload-weighing-form-heading" data-testid="logistics-unload-weighing-form">
      <h2 id="logistics-unload-weighing-form-heading">{T.record}</h2>
      <p className={styles.muted}>{T.note}</p>
      <p className={styles.muted}>{T.onlyOnce}</p>
      {notice === null ? null : (
        <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'} aria-live={notice.live}>
          <p data-testid="logistics-unload-weighing-notice">{notice.message}</p>
          {notice.actions.length > 0 ? (
            <div className={styles.actions}>
              <button type="button" className={styles.secondaryButton} onClick={retry}>
                {INSTRUCTION_DETAIL_TEXT.command.retry}
              </button>
              <button type="button" className={styles.linkButton} onClick={leave}>
                {INSTRUCTION_DETAIL_TEXT.command.abandon}
              </button>
            </div>
          ) : null}
        </div>
      )}
      {open ? (
        <form onSubmit={onSubmit} noValidate>
          <fieldset disabled={frozen} className={styles.fieldset}>
            <legend>{T.heading}</legend>
            <div className={styles.grid}>
              <label htmlFor="logistics-unload-weighing-direction">{T.direction}</label>
              <select id="logistics-unload-weighing-direction" value={direction}
                onChange={(event) => setDirection(event.target.value as WeighingDirection)}>
                {Object.values(WEIGHING_DIRECTION).map((value) => (
                  <option key={value} value={value}>
                    {T.directionOption[value]}
                  </option>
                ))}
              </select>
              <label htmlFor="logistics-unload-weighing-first">{T.first}</label>
              <input id="logistics-unload-weighing-first" type="text" inputMode="decimal" autoComplete="off" value={first}
                aria-invalid={error !== null || inconsistent ? true : undefined} onChange={(event) => setFirst(event.target.value)} />
              <label htmlFor="logistics-unload-weighing-second">{T.second}</label>
              <input id="logistics-unload-weighing-second" type="text" inputMode="decimal" autoComplete="off" value={second}
                aria-invalid={error !== null || inconsistent ? true : undefined}
                aria-describedby={inconsistent ? 'logistics-unload-weighing-direction-error' : undefined}
                onChange={(event) => setSecond(event.target.value)} />
              {inconsistent ? (
                <p id="logistics-unload-weighing-direction-error" className={styles.fieldError}>
                  {T.invalidDirection}
                </p>
              ) : null}
            </div>
            {error === null ? null : (
              <p role="alert" className={styles.fieldError}>
                {error}
              </p>
            )}
            <div className={styles.actions}>
              <button type="submit" className={styles.primaryButton} disabled={frozen} data-testid="logistics-unload-weighing-button">
                {T.record}
              </button>
            </div>
          </fieldset>
        </form>
      ) : null}
    </section>
  )
}
