/**
 * AT-053 — the vehicle's manual weighbridge record on L-02-DETAIL (master v3 §27.8 L-06 "Kantar Oturumu": brüt, dara,
 * net ve fark), `POST /api/v1/logistics/loading-instructions/{id}/weighings`.
 *
 *  - `WeighingFacts` shows the record to every reader of the detail exactly as the backend calculated it: the two
 *    weighings, tare / gross / net (WGT-001), the kilograms loaded when it was weighed and the SIGNED difference
 *    net − loaded (WGT-002). A fact, never a verdict: the tolerance thresholds are OPEN-004, so no colour, badge or
 *    word here says "accepted" or "out of tolerance", and nothing is recomputed on the screen.
 *  - `WeighingForm` is offered on an Issued instruction without a record. Like the record-load form (ASSUMPTION-LOG4B-2)
 *    it is shown to every holder of the detail: no shell control exists for it, and the server measures
 *    `logistics.weighings.record` afresh, answering a code-less 403 to anyone else. One weighing id is minted per
 *    attempt snapshot and travels with it, so a retry never records a second weighing. A second record is refused by
 *    the server (its correction is not decided), so after a recorded weighing the form is gone.
 */

import { useLayoutEffect, useRef, useState } from 'react'
import { recordWeighing } from './api'
import {
  INSTRUCTION_STATUS,
  WEIGHING_DIRECTION,
  parseQuantityInput,
  type InstructionDetail,
  type InstructionMutation,
  type InstructionWeighing,
  type RecordWeighingRequest,
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
import { INSTRUCTION_DETAIL_TEXT, WEIGHING_TEXT as T } from './logisticsText'
import type { ShellState } from '../shell/shellState'
import styles from './LogisticsPages.module.css'

export function WeighingFacts({ weighing }: { readonly weighing: InstructionWeighing | null }) {
  return (
    <section className={styles.section} aria-labelledby="logistics-weighing-heading" data-testid="logistics-weighing">
      <h2 id="logistics-weighing-heading">{T.heading}</h2>
      {weighing === null ? (
        <p className={styles.muted} data-testid="logistics-weighing-none">
          {T.none}
        </p>
      ) : (
        <>
          <dl className={styles.facts}>
            <div>
              <dt>{T.direction}</dt>
              <dd data-testid="logistics-weighing-direction">{T.directionShort[weighing.direction]}</dd>
            </div>
            <div>
              <dt>{T.first}</dt>
              <dd className={styles.numericValue}>{weighing.firstWeighingKg}</dd>
            </div>
            <div>
              <dt>{T.second}</dt>
              <dd className={styles.numericValue}>{weighing.secondWeighingKg}</dd>
            </div>
            <div>
              <dt>{T.tare}</dt>
              <dd className={styles.numericValue} data-testid="logistics-weighing-tare">{weighing.tareKg}</dd>
            </div>
            <div>
              <dt>{T.gross}</dt>
              <dd className={styles.numericValue} data-testid="logistics-weighing-gross">{weighing.grossKg}</dd>
            </div>
            <div>
              <dt>{T.net}</dt>
              <dd className={styles.numericValue} data-testid="logistics-weighing-net">{weighing.netKg}</dd>
            </div>
            <div>
              <dt>{T.loadedAtWeighing}</dt>
              <dd className={styles.numericValue} data-testid="logistics-weighing-loaded">{weighing.loadedKgAtWeighing}</dd>
            </div>
            <div>
              <dt>{T.difference}</dt>
              <dd className={styles.numericValue} data-testid="logistics-weighing-difference">{weighing.differenceKg}</dd>
            </div>
            <div>
              <dt>{T.recordedBy}</dt>
              <dd className={styles.identifier}>{weighing.recordedBy}</dd>
            </div>
            <div>
              <dt>{T.recordedAt}</dt>
              <dd>{formatInstant(weighing.recordedAtUtc)}</dd>
            </div>
          </dl>
          <p className={styles.muted}>{T.differenceNote}</p>
        </>
      )}
    </section>
  )
}

const weighingScope = (instructionId: string) => `logistics.instruction.weighing:${instructionId}`

interface WeighingFormValues {
  readonly direction: WeighingDirection
  readonly first: string
  readonly second: string
}

export function WeighingForm({
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
  const slot = attemptBinding(shell, weighingScope(instruction.id))
  const [initial] = useState(() => recallInstructionAttempt<RecordWeighingRequest, WeighingFormValues>(slot))
  const [direction, setDirection] = useState<WeighingDirection>(() => initial?.values.direction ?? WEIGHING_DIRECTION.tareFirst)
  const [first, setFirst] = useState(() => initial?.values.first ?? '')
  const [second, setSecond] = useState(() => initial?.values.second ?? '')
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<AttemptState<RecordWeighingRequest, InstructionMutation>>(() => initial?.attempt ?? { phase: 'idle' })
  const attemptRef = useRef(attempt)
  useLayoutEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  // The record itself is shown by WeighingFacts; once it exists no second one is offered (the server refuses it).
  const open = instruction.status === INSTRUCTION_STATUS.issued && detail.weighing === null
  if (!open && attempt.phase === 'idle') {
    return instruction.status === INSTRUCTION_STATUS.issued ? null : (
      <p className={styles.muted} data-testid="logistics-weighing-issued-only">
        {T.issuedOnly}
      </p>
    )
  }

  async function send(snapshot: AttemptSnapshot<RecordWeighingRequest>, values: WeighingFormValues) {
    const previous = attemptRef.current
    const inFlight = beginSend<RecordWeighingRequest, InstructionMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    rememberInstructionAttempt(snapshot.binding, inFlight, values)
    const outcome = await recordWeighing(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      instructionId: instruction.id,
    })
    const next = applyOutcome(inFlight, snapshot, attemptBinding(shell, weighingScope(instruction.id)), outcome, previous)
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
    const binding = attemptBinding(shell, weighingScope(instruction.id))
    if (binding === null) return
    const live = liveSnapshot(attemptRef.current)
    // One weighing id per snapshot: a retry of the same values resends the same id; changed values mint a new one.
    const payload: RecordWeighingRequest =
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
      { path: `${instruction.id}/weighings`, binding, companyId, payload },
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
    <section className={styles.section} aria-labelledby="logistics-weighing-form-heading" data-testid="logistics-weighing-form">
      <h2 id="logistics-weighing-form-heading">{T.record}</h2>
      <p className={styles.muted}>{T.note}</p>
      <p className={styles.muted}>{T.onlyOnce}</p>
      {notice === null ? null : (
        <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'} aria-live={notice.live}>
          <p data-testid="logistics-weighing-notice">{notice.message}</p>
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
              <label htmlFor="logistics-weighing-direction">{T.direction}</label>
              <select id="logistics-weighing-direction" value={direction} onChange={(event) => setDirection(event.target.value as WeighingDirection)}>
                {Object.values(WEIGHING_DIRECTION).map((value) => (
                  <option key={value} value={value}>
                    {T.directionOption[value]}
                  </option>
                ))}
              </select>
              <label htmlFor="logistics-weighing-first">{T.first}</label>
              <input id="logistics-weighing-first" type="text" inputMode="decimal" autoComplete="off" value={first}
                aria-invalid={error !== null || inconsistent ? true : undefined} onChange={(event) => setFirst(event.target.value)} />
              <label htmlFor="logistics-weighing-second">{T.second}</label>
              <input id="logistics-weighing-second" type="text" inputMode="decimal" autoComplete="off" value={second}
                aria-invalid={error !== null || inconsistent ? true : undefined}
                aria-describedby={inconsistent ? 'logistics-weighing-direction-error' : undefined} onChange={(event) => setSecond(event.target.value)} />
              {inconsistent ? (
                <p id="logistics-weighing-direction-error" className={styles.fieldError}>
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
              <button type="submit" className={styles.primaryButton} disabled={frozen} data-testid="logistics-weighing-button">
                {T.record}
              </button>
            </div>
          </fieldset>
        </form>
      ) : null}
    </section>
  )
}
