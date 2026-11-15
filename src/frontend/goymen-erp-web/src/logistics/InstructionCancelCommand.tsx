/**
 * F-020 — cancelling the loading instruction itself on L-02-DETAIL (spec 04 §F-020 item 6: "Talimatı iptal et" + gerekçe),
 * under `POST /api/v1/logistics/loading-instructions/{id}/cancel`.
 *
 *  - Offered on a Draft or Issued instruction with no recorded shipment (LOJ-D6): a required reason (1..500 characters,
 *    trimmed) and the instruction version travel, nothing else. What the cancel releases is the server's: the instruction's
 *    unshipped quantity, loaded-without-dispatch-note included, stops counting (LOJ-D15); nothing is recomputed here.
 *  - Once a shipment is recorded on the instruction (EK2: "kesinleşme" is a shipment on it) the command is not offered and the
 *    screen says why (LOJ-D8); the server refuses it anyway with a recorded terminal 409 `logistics_instruction_shipped`.
 *  - Like the other commands (ASSUMPTION-LOG4B-2) it is shown to every holder of the detail; no shell control exists for it and
 *    the server measures `logistics.loading-instructions.cancel` afresh (a code-less 403 to anyone without it; the grant is a
 *    human gate, LOJ-D15(3)). One Idempotency-Key per attempt snapshot: a lost reply is retried with the same key and body.
 *  - The section stays mounted across the re-read that shows the instruction Cancelled, so the notice of the command that
 *    cancelled it survives; on a Cancelled instruction with no notice it renders nothing.
 *  - Not here: physically unloading the vehicle or re-weighing it (LOJ-D15(4), undecided) and any stock allocation change.
 */

import { useLayoutEffect, useRef, useState } from 'react'
import { cancelInstruction } from './api'
import {
  CANCEL_REASON_MAX,
  INSTRUCTION_STATUS,
  SHARE_KIND,
  type CancelInstructionRequest,
  type InstructionDetail,
  type InstructionMutation,
} from './contract'
import {
  abandon,
  applyOutcome,
  attemptBinding,
  beginSend,
  decideSubmit,
  generateAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  type AttemptSnapshot,
  type AttemptState,
} from './instructionAttempt'
import { commandNotice, refusalMovesRecord } from './instructionAttemptNotice'
import { recallInstructionAttempt, rememberInstructionAttempt } from './pendingInstructionAttempts'
import { INSTRUCTION_CANCEL_TEXT as T, INSTRUCTION_DETAIL_TEXT } from './logisticsText'
import type { ShellState } from '../shell/shellState'
import styles from './LogisticsPages.module.css'

interface CancelValues {
  readonly reason: string
}

const cancelScope = (instructionId: string) => `logistics.instruction.cancel:${instructionId}`

export function InstructionCancelCommand({
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
  const scope = cancelScope(instruction.id)
  const slot = attemptBinding(shell, scope)
  const [initial] = useState(() => recallInstructionAttempt<CancelInstructionRequest, CancelValues>(slot))
  const [reason, setReason] = useState(() => initial?.values.reason ?? '')
  // A remembered attempt that is still open reopens the form with the reason it was sent with.
  const [open, setOpen] = useState(() => initial !== null && initial.attempt.phase !== 'completed' && initial.attempt.phase !== 'idle')
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<AttemptState<CancelInstructionRequest, InstructionMutation>>(() => initial?.attempt ?? { phase: 'idle' })
  const attemptRef = useRef(attempt)
  useLayoutEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  async function send(snapshot: AttemptSnapshot<CancelInstructionRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<CancelInstructionRequest, InstructionMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const values: CancelValues = { reason: snapshot.payload.reason }
    rememberInstructionAttempt(snapshot.binding, inFlight, values)
    const outcome = await cancelInstruction(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      instructionId: instruction.id,
    })
    const next = applyOutcome(inFlight, snapshot, attemptBinding(shell, scope), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberInstructionAttempt(snapshot.binding, next, values)
    if (next.phase === 'completed') {
      setOpen(false)
      setReason('')
    }
    if (next.phase === 'completed' || refusalMovesRecord(next)) onRecorded()
  }

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    const trimmed = reason.trim()
    if (trimmed.length === 0 || trimmed.length > CANCEL_REASON_MAX) {
      setError(T.reasonRequired)
      return
    }
    setError(null)
    const binding = attemptBinding(shell, scope)
    if (binding === null) return
    // A completed attempt never blocks a later one: it starts afresh (new key).
    const current: AttemptState<CancelInstructionRequest, InstructionMutation> =
      attemptRef.current.phase === 'completed' ? { phase: 'idle' } : attemptRef.current
    const decision = decideSubmit(
      current,
      { path: `${instruction.id}/cancel`, binding, companyId, payload: { expectedVersion: instruction.version, reason: trimmed } },
      generateAttemptKey,
    )
    if (decision.type !== 'send') return
    void send(decision.snapshot)
  }

  function retry() {
    const live = liveSnapshot(attemptRef.current)
    if (live === null || attemptRef.current.phase === 'submitting') return
    void send(live)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase !== 'idle' && slot !== null) rememberInstructionAttempt(slot, next, { reason })
  }

  const notice = commandNotice(attempt)
  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const live = instruction.status === INSTRUCTION_STATUS.draft || instruction.status === INSTRUCTION_STATUS.issued
  const shipped = detail.shares.some((share) => share.kind === SHARE_KIND.shipped)
  if (!live && notice === null) return null
  const offer = live && !shipped
  const reasonId = 'logistics-instruction-cancel-form-reason'
  return (
    <section className={styles.section} aria-labelledby="logistics-instruction-cancel-heading" data-testid="logistics-instruction-cancel">
      <h2 id="logistics-instruction-cancel-heading">{T.heading}</h2>
      {notice === null ? null : (
        <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'} aria-live={notice.live}>
          <p data-testid="logistics-instruction-cancel-notice">{notice.message}</p>
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
      {live && shipped ? (
        <p className={styles.muted} data-testid="logistics-instruction-cancel-shipped">
          {T.shipped}
        </p>
      ) : null}
      {offer ? (
        <>
          <p className={styles.muted}>{T.note}</p>
          <p className={styles.muted} data-testid="logistics-instruction-cancel-undecided">{T.undecided}</p>
          {open ? null : (
            <div className={styles.actions}>
              <button type="button" className={styles.secondaryButton} disabled={frozen} onClick={() => setOpen(true)}
                data-testid="logistics-instruction-cancel-open">
                {T.cancel}
              </button>
            </div>
          )}
        </>
      ) : null}
      {offer && open ? (
        <form onSubmit={onSubmit} noValidate data-testid="logistics-instruction-cancel-form">
          <fieldset disabled={frozen} className={styles.fieldset}>
            <legend>{T.cancel}</legend>
            <label htmlFor={reasonId}>{T.reasonLabel}</label>
            <br />
            <textarea id={reasonId} value={reason} maxLength={CANCEL_REASON_MAX} rows={2} cols={48}
              aria-invalid={error !== null} aria-describedby={error === null ? undefined : `${reasonId}-error`}
              onChange={(event) => setReason(event.target.value)} data-testid="logistics-instruction-cancel-reason-input" />
            {error === null ? null : (
              <p id={`${reasonId}-error`} role="alert" className={styles.fieldError}>
                {error}
              </p>
            )}
            <div className={styles.actions}>
              <button type="submit" className={styles.primaryButton} data-testid="logistics-instruction-cancel-confirm">
                {T.confirm}
              </button>
              <button type="button" className={styles.linkButton} onClick={() => { setOpen(false); setError(null) }}
                data-testid="logistics-instruction-cancel-dismiss">
                {T.dismiss}
              </button>
            </div>
          </fieldset>
        </form>
      ) : null}
    </section>
  )
}
