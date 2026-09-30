/**
 * AT-055 — the dispatch note (irsaliye) on L-02-DETAIL (master v3 §7.8 "fark kontrolü → irsaliye taslağı → onay/kesim", §27.8
 * L-08 Satış İrsaliyesi Hazırlığı: "gerçekleşmeden belgeye"), under
 * `POST /api/v1/logistics/loading-instructions/{id}/dispatch-notes` and `…/dispatch-notes/{noteId}/approve | cancel | e-dispatch-number`.
 *
 *  - `DispatchNoteFacts` shows every note of the instruction to every reader, plainly on screen: its state (Taslak / Onaylandı /
 *    İptal edildi), the ERP number the server assigned on approval (company series, gap-free), the separately entered
 *    e-İrsaliye number, who approved / cancelled it and when, the cancel reason, and its lines — each one definite shipment and
 *    its SHIPPED quantity, as the server answered it (nothing is recomputed). A cancelled note stays visible: a note is never
 *    deleted. Each note carries the anchor `irsaliye-{id}` so the Sales order view can link straight to it.
 *  - `DispatchNoteLifecycles` (one panel per note) offers "Onayla (numara ver)" on a Draft, "İptal et" with a required reason on
 *    a Draft or Approved note (its shipments are released: correction = cancel + new draft) and the e-İrsaliye number on an
 *    Approved note without one. Like the other commands (ASSUMPTION-LOG4B-2) it is shown to every holder of the detail; the
 *    server measures `logistics.dispatch-notes.approve` / `.cancel` afresh. One Idempotency-Key per attempt snapshot.
 *  - `DispatchNoteForm` is offered on an Issued instruction with at least one definite shipment not on a live (non-cancelled)
 *    note. A shipment on a live note is shown as linked and cannot be picked (AT-055); the server refuses a second link anyway.
 *    One draft id is minted per attempt snapshot and travels with it, so a retry never makes a second draft.
 *  - Not here: a printable template (OPEN-019 / DEC-DOC-010), the official e-İrsaliye submission (DEC-INT-001 — the number is
 *    entered by hand), and one note combining several instructions (a follow-up).
 */

import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router'
import { approveDispatchNote, cancelDispatchNote, createDispatchNote, setEDispatchNumber } from './api'
import {
  CANCEL_REASON_MAX,
  DISPATCH_NOTE_STATUS,
  E_DISPATCH_NUMBER_PATTERN,
  INSTRUCTION_STATUS,
  SHARE_KIND,
  dispatchNoteOf,
  type ApproveDispatchNoteRequest,
  type CancelDispatchNoteRequest,
  type CreateDispatchNoteRequest,
  type DispatchNote,
  type SetEDispatchNumberRequest,
  type InstructionDetail,
  type InstructionMutation,
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
import { DISPATCH_NOTE_TEXT as T, INSTRUCTION_DETAIL_TEXT } from './logisticsText'
import type { ShellState } from '../shell/shellState'
import styles from './LogisticsPages.module.css'

/** The anchor of one draft on L-02-DETAIL; the Sales order view links to it. */
const dispatchNoteAnchor = (dispatchNoteId: string) => `irsaliye-${dispatchNoteId}`

const shortReference = (id: string) => `…${id.slice(-8)}`

const statusText = (note: DispatchNote) =>
  note.status === DISPATCH_NOTE_STATUS.approved ? T.approved : note.status === DISPATCH_NOTE_STATUS.cancelled ? T.cancelled : T.draft

/** A note is named by its ERP number once it has one, else by its short reference. */
const noteName = (note: DispatchNote) => (note.number === null ? `${T.draft} ${shortReference(note.dispatchNoteId)}` : `${T.number} ${note.number}`)

export function DispatchNoteFacts({ detail }: { readonly detail: InstructionDetail }) {
  const lineNo = (lineId: string) => detail.lines.find((line) => line.lineId === lineId)?.lineNo ?? '?'
  const { hash } = useLocation()
  // A link from the Sales order view (#irsaliye-{id}) lands on that draft once the detail is shown.
  useEffect(() => {
    if (!hash.startsWith('#irsaliye-')) return
    const target = document.getElementById(decodeURIComponent(hash.slice(1)))
    if (target !== null && typeof target.scrollIntoView === 'function') target.scrollIntoView({ block: 'start' })
  }, [hash, detail.dispatchNotes.length])
  return (
    <section className={styles.section} aria-labelledby="logistics-dispatch-notes-heading" data-testid="logistics-dispatch-notes">
      <h2 id="logistics-dispatch-notes-heading">{T.heading}</h2>
      <p className={styles.muted}>{T.note}</p>
      <p className={styles.muted} data-testid="logistics-dispatch-notes-undecided">{T.undecided}</p>
      {detail.dispatchNotes.length === 0 ? (
        <p className={styles.muted} data-testid="logistics-dispatch-notes-none">
          {T.none}
        </p>
      ) : (
        detail.dispatchNotes.map((note) => <DispatchNoteBlock key={note.dispatchNoteId} note={note} lineNo={lineNo} />)
      )}
    </section>
  )
}

function DispatchNoteBlock({ note, lineNo }: { readonly note: DispatchNote; readonly lineNo: (lineId: string) => number | string }) {
  return (
    <article id={dispatchNoteAnchor(note.dispatchNoteId)} className={styles.section} data-testid={`logistics-dispatch-note-${note.dispatchNoteId}`}
      aria-label={noteName(note)} data-status={note.status}>
      <dl className={styles.facts}>
        <div>
          <dt>{T.reference}</dt>
          <dd className={styles.identifier} data-testid="logistics-dispatch-note-reference">{note.dispatchNoteId}</dd>
        </div>
        <div>
          <dt>{INSTRUCTION_DETAIL_TEXT.status}</dt>
          <dd><span className={note.status === DISPATCH_NOTE_STATUS.cancelled ? `${styles.badge} ${styles.blocked}` : styles.badge}
            data-testid="logistics-dispatch-note-status">{statusText(note)}</span></dd>
        </div>
        {note.number === null ? null : (
          <div>
            <dt>{T.number}</dt>
            <dd className={styles.identifier} data-testid="logistics-dispatch-note-number">{note.number}</dd>
          </div>
        )}
        {note.eDispatchNumber === null ? null : (
          <div>
            <dt>{T.eDispatchNumber}</dt>
            <dd className={styles.identifier} data-testid="logistics-dispatch-note-e-number">{note.eDispatchNumber}</dd>
          </div>
        )}
        <div>
          <dt>{T.createdBy}</dt>
          <dd className={styles.identifier}>{note.createdBy}</dd>
        </div>
        <div>
          <dt>{T.createdAt}</dt>
          <dd>{formatInstant(note.createdAtUtc)}</dd>
        </div>
        {note.approvedBy === null || note.approvedAtUtc === null ? null : (
          <>
            <div>
              <dt>{T.approvedBy}</dt>
              <dd className={styles.identifier}>{note.approvedBy}</dd>
            </div>
            <div>
              <dt>{T.approvedAt}</dt>
              <dd>{formatInstant(note.approvedAtUtc)}</dd>
            </div>
          </>
        )}
        {note.cancelledBy === null || note.cancelledAtUtc === null ? null : (
          <>
            <div>
              <dt>{T.cancelledBy}</dt>
              <dd className={styles.identifier}>{note.cancelledBy}</dd>
            </div>
            <div>
              <dt>{T.cancelledAt}</dt>
              <dd>{formatInstant(note.cancelledAtUtc)}</dd>
            </div>
            <div>
              <dt>{T.cancelReason}</dt>
              <dd data-testid="logistics-dispatch-note-cancel-reason">{note.cancelReason}</dd>
            </div>
          </>
        )}
      </dl>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.muted}>{T.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{T.lineNo}</th>
              <th scope="col">{T.instructionLine}</th>
              <th scope="col">{T.order}</th>
              <th scope="col">{T.shipment}</th>
              <th scope="col" className={styles.numeric}>{T.quantity}</th>
            </tr>
          </thead>
          <tbody>
            {note.lines.map((line) => (
              <tr key={line.shipmentId} data-testid={`logistics-dispatch-note-line-${line.lineNo}`}>
                <th scope="row">{line.lineNo}</th>
                <td>{lineNo(line.instructionLineId)}</td>
                <td className={styles.identifier}>{line.sourceOrderId} / {line.sourceOrderLineId}</td>
                <td className={styles.identifier}>{line.shipmentId}</td>
                <td className={styles.numeric} data-testid={`logistics-dispatch-note-line-${line.lineNo}-quantity`}>{line.quantity}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </article>
  )
}

const dispatchNoteScope = (instructionId: string) => `logistics.instruction.dispatch-note:${instructionId}`

interface DispatchNoteFormValues {
  readonly shipmentIds: readonly string[]
}

export function DispatchNoteForm({
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
  const shipments = detail.shares.filter((share) => share.kind === SHARE_KIND.shipped)
  const free = shipments.filter((share) => dispatchNoteOf(detail, share.shareId) === null)
  const slot = attemptBinding(shell, dispatchNoteScope(instruction.id))
  const [initial] = useState(() => recallInstructionAttempt<CreateDispatchNoteRequest, DispatchNoteFormValues>(slot))
  const [chosen, setChosen] = useState<readonly string[]>(() => initial?.values.shipmentIds ?? [])
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<AttemptState<CreateDispatchNoteRequest, InstructionMutation>>(() => initial?.attempt ?? { phase: 'idle' })
  const attemptRef = useRef(attempt)
  useLayoutEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  if (instruction.status !== INSTRUCTION_STATUS.issued) {
    return (
      <p className={styles.muted} data-testid="logistics-dispatch-note-issued-only">
        {T.issuedOnly}
      </p>
    )
  }

  const lineNo = (lineId: string) => detail.lines.find((line) => line.lineId === lineId)?.lineNo ?? '?'
  const selectable = free.map((share) => share.shareId)
  const picked = chosen.filter((id) => selectable.includes(id))

  async function send(snapshot: AttemptSnapshot<CreateDispatchNoteRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<CreateDispatchNoteRequest, InstructionMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const values: DispatchNoteFormValues = { shipmentIds: snapshot.payload.shipmentIds }
    rememberInstructionAttempt(snapshot.binding, inFlight, values)
    const outcome = await createDispatchNote(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      instructionId: instruction.id,
    })
    const next = applyOutcome(inFlight, snapshot, attemptBinding(shell, dispatchNoteScope(instruction.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberInstructionAttempt(snapshot.binding, next, values)
    if (next.phase === 'completed') setChosen([])
    if (next.phase === 'completed' || refusalMovesRecord(next)) onRecorded()
  }

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (picked.length === 0) {
      setError(T.chooseOne)
      return
    }
    setError(null)
    const binding = attemptBinding(shell, dispatchNoteScope(instruction.id))
    if (binding === null) return
    // A completed draft never blocks the next one: a new submit starts a fresh attempt (new key, new draft id).
    const current: AttemptState<CreateDispatchNoteRequest, InstructionMutation> =
      attemptRef.current.phase === 'completed' ? { phase: 'idle' } : attemptRef.current
    const live = liveSnapshot(current)
    const sameChoice = (ids: readonly string[]) => ids.length === picked.length && ids.every((id) => picked.includes(id))
    // One draft id per snapshot: a retry of the same choice resends the same id; a changed choice mints a new one.
    const payload: CreateDispatchNoteRequest =
      live !== null && sameChoice(live.payload.shipmentIds)
        ? live.payload
        : { expectedVersion: instruction.version, dispatchNoteId: generateClientId(), shipmentIds: picked }
    const decision = decideSubmit(current, { path: `${instruction.id}/dispatch-notes`, binding, companyId, payload }, generateAttemptKey)
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
    if (next.phase !== 'idle' && slot !== null) rememberInstructionAttempt(slot, next, { shipmentIds: chosen })
  }

  function toggle(id: string, on: boolean) {
    setChosen((previous) => (on ? [...previous.filter((value) => value !== id), id] : previous.filter((value) => value !== id)))
  }

  const notice = commandNotice(attempt)
  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  if (shipments.length === 0 && attempt.phase === 'idle') return null
  return (
    <section className={styles.section} aria-labelledby="logistics-dispatch-note-form-heading" data-testid="logistics-dispatch-note-form">
      <h2 id="logistics-dispatch-note-form-heading">{T.formHeading}</h2>
      {notice === null ? null : (
        <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'} aria-live={notice.live}>
          <p data-testid="logistics-dispatch-note-notice">{notice.message}</p>
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
      <form onSubmit={onSubmit} noValidate>
        <fieldset disabled={frozen} className={styles.fieldset}>
          <legend>{T.choose}</legend>
          <ul className={styles.plainList} data-testid="logistics-dispatch-note-shipments">
            {shipments.map((share) => {
              const linkedTo = dispatchNoteOf(detail, share.shareId)
              const id = `logistics-dispatch-note-pick-${share.shareId}`
              return (
                <li key={share.shareId} data-testid={`logistics-dispatch-note-shipment-${share.shareId}`}>
                  <input id={id} type="checkbox" disabled={linkedTo !== null} checked={linkedTo === null && picked.includes(share.shareId)}
                    onChange={(event) => toggle(share.shareId, event.target.checked)} />{' '}
                  <label htmlFor={id}>
                    {INSTRUCTION_DETAIL_TEXT.shareLine} {lineNo(share.lineId)} · {share.quantity} · {formatInstant(share.recordedAtUtc)} ·{' '}
                    <span className={styles.identifier}>{shortReference(share.shareId)}</span> —{' '}
                    {linkedTo === null ? T.free : `${T.linked} (${shortReference(linkedTo.dispatchNoteId)})`}
                  </label>
                </li>
              )
            })}
          </ul>
          {free.length === 0 ? (
            <p className={styles.muted} data-testid="logistics-dispatch-note-no-free">
              {T.noFreeShipment}
            </p>
          ) : null}
          {error === null ? null : (
            <p role="alert" className={styles.fieldError}>
              {error}
            </p>
          )}
          <div className={styles.actions}>
            <button type="submit" className={styles.primaryButton} disabled={frozen || free.length === 0} data-testid="logistics-dispatch-note-button">
              {T.create}
            </button>
          </div>
        </fieldset>
      </form>
    </section>
  )
}

/* ── Lifecycle: approve (numbering), cancel (with reason), the e-İrsaliye number ─────────────────────────────────────── */

type LifecyclePayload = ApproveDispatchNoteRequest | CancelDispatchNoteRequest | SetEDispatchNumberRequest

interface LifecycleValues {
  readonly reason: string
  readonly eDispatchNumber: string
}

const lifecycleScope = (instructionId: string, dispatchNoteId: string) =>
  `logistics.instruction.dispatch-note-lifecycle:${instructionId}:${dispatchNoteId}`

/** One lifecycle panel per note, keyed by the note: its notice survives the re-read that follows a recorded command. */
export function DispatchNoteLifecycles({
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
  if (detail.instruction.status !== INSTRUCTION_STATUS.issued || detail.dispatchNotes.length === 0) return null
  return (
    <section className={styles.section} aria-labelledby="logistics-dispatch-note-lifecycle-heading" data-testid="logistics-dispatch-note-lifecycles">
      <h2 id="logistics-dispatch-note-lifecycle-heading">{T.actionsLabel}</h2>
      {detail.dispatchNotes.map((note) => (
        <DispatchNoteLifecycle key={note.dispatchNoteId} shell={shell} detail={detail} note={note} companyId={companyId} onRecorded={onRecorded} />
      ))}
    </section>
  )
}

function DispatchNoteLifecycle({
  shell,
  detail,
  note,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: InstructionDetail
  readonly note: DispatchNote
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const { instruction } = detail
  const scope = lifecycleScope(instruction.id, note.dispatchNoteId)
  const slot = attemptBinding(shell, scope)
  const [initial] = useState(() => recallInstructionAttempt<LifecyclePayload, LifecycleValues>(slot))
  const [reason, setReason] = useState(() => initial?.values.reason ?? '')
  const [eNumber, setENumber] = useState(() => initial?.values.eDispatchNumber ?? '')
  const [cancelling, setCancelling] = useState(false)
  const [error, setError] = useState<{ readonly field: 'reason' | 'eNumber'; readonly message: string } | null>(null)
  const [attempt, setAttempt] = useState<AttemptState<LifecyclePayload, InstructionMutation>>(() => initial?.attempt ?? { phase: 'idle' })
  const attemptRef = useRef(attempt)
  useLayoutEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  async function send(snapshot: AttemptSnapshot<LifecyclePayload>) {
    const previous = attemptRef.current
    const inFlight = beginSend<LifecyclePayload, InstructionMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const values: LifecycleValues = { reason, eDispatchNumber: eNumber }
    rememberInstructionAttempt(snapshot.binding, inFlight, values)
    const context = { idempotencyKey: snapshot.key, companyId: snapshot.companyId, instructionId: instruction.id }
    const payload = snapshot.payload
    const outcome = snapshot.path.endsWith('/approve')
      ? await approveDispatchNote(note.dispatchNoteId, payload as ApproveDispatchNoteRequest, context)
      : snapshot.path.endsWith('/cancel')
        ? await cancelDispatchNote(note.dispatchNoteId, payload as CancelDispatchNoteRequest, context)
        : await setEDispatchNumber(note.dispatchNoteId, payload as SetEDispatchNumberRequest, context)
    const next = applyOutcome(inFlight, snapshot, attemptBinding(shell, scope), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberInstructionAttempt(snapshot.binding, next, values)
    if (next.phase === 'completed') {
      setCancelling(false)
      setReason('')
      setENumber('')
    }
    if (next.phase === 'completed' || refusalMovesRecord(next)) onRecorded()
  }

  function submit(suffix: 'approve' | 'cancel' | 'e-dispatch-number', payload: LifecyclePayload) {
    const binding = attemptBinding(shell, scope)
    if (binding === null) return
    // A completed command never blocks the next one on the same note: it starts a fresh attempt (new key).
    const current: AttemptState<LifecyclePayload, InstructionMutation> =
      attemptRef.current.phase === 'completed' ? { phase: 'idle' } : attemptRef.current
    const path = `${instruction.id}/dispatch-notes/${note.dispatchNoteId}/${suffix}`
    const decision = decideSubmit(current, { path, binding, companyId, payload }, generateAttemptKey)
    if (decision.type !== 'send') return
    void send(decision.snapshot)
  }

  function approve() {
    setError(null)
    submit('approve', { expectedVersion: instruction.version })
  }

  function cancel(event: React.FormEvent) {
    event.preventDefault()
    const trimmed = reason.trim()
    if (trimmed.length === 0 || trimmed.length > CANCEL_REASON_MAX) {
      setError({ field: 'reason', message: T.cancelReasonRequired })
      return
    }
    setError(null)
    submit('cancel', { expectedVersion: instruction.version, reason: trimmed })
  }

  function saveENumber(event: React.FormEvent) {
    event.preventDefault()
    const value = eNumber.trim().toUpperCase()
    if (!E_DISPATCH_NUMBER_PATTERN.test(value)) {
      setError({ field: 'eNumber', message: T.eDispatchNumberInvalid })
      return
    }
    setError(null)
    submit('e-dispatch-number', { expectedVersion: instruction.version, eDispatchNumber: value })
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
    if (next.phase !== 'idle' && slot !== null) rememberInstructionAttempt(slot, next, { reason, eDispatchNumber: eNumber })
  }

  const notice = commandNotice(attempt)
  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const isDraft = note.status === DISPATCH_NOTE_STATUS.draft
  const isApproved = note.status === DISPATCH_NOTE_STATUS.approved
  if (!isDraft && !isApproved && notice === null) return null
  const testId = `logistics-dispatch-note-lifecycle-${note.dispatchNoteId}`
  const reasonId = `${testId}-reason`
  const eNumberId = `${testId}-e-number`
  return (
    <div className={styles.line} data-testid={testId} role="group" aria-label={`${noteName(note)} — ${statusText(note)}`}>
      <h3 className={styles.lineTitle}>
        {noteName(note)} · {statusText(note)}
      </h3>
      {notice === null ? null : (
        <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'} aria-live={notice.live}>
          <p data-testid="logistics-dispatch-note-lifecycle-notice">{notice.message}</p>
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
      {isDraft || isApproved ? (
        <div className={styles.actions}>
          {isDraft ? (
            <button type="button" className={styles.primaryButton} disabled={frozen} onClick={approve} data-testid="logistics-dispatch-note-approve">
              {T.approve}
            </button>
          ) : null}
          {cancelling ? null : (
            <button type="button" className={styles.secondaryButton} disabled={frozen} onClick={() => setCancelling(true)}
              data-testid="logistics-dispatch-note-cancel">
              {T.cancel}
            </button>
          )}
        </div>
      ) : null}
      {(isDraft || isApproved) && cancelling ? (
        <form onSubmit={cancel} noValidate data-testid="logistics-dispatch-note-cancel-form">
          <fieldset disabled={frozen} className={styles.fieldset}>
            <legend>{T.cancel}</legend>
            <label htmlFor={reasonId}>{T.cancelReasonLabel}</label>
            <br />
            <textarea id={reasonId} value={reason} maxLength={CANCEL_REASON_MAX} rows={2} cols={48}
              aria-invalid={error?.field === 'reason'} onChange={(event) => setReason(event.target.value)}
              data-testid="logistics-dispatch-note-cancel-reason-input" />
            {error?.field === 'reason' ? <p role="alert" className={styles.fieldError}>{error.message}</p> : null}
            <div className={styles.actions}>
              <button type="submit" className={styles.primaryButton} data-testid="logistics-dispatch-note-cancel-confirm">{T.cancelConfirm}</button>
              <button type="button" className={styles.linkButton} onClick={() => { setCancelling(false); setError(null) }}>
                {T.cancelDismiss}
              </button>
            </div>
          </fieldset>
        </form>
      ) : null}
      {isApproved && note.eDispatchNumber === null ? (
        <form onSubmit={saveENumber} noValidate data-testid="logistics-dispatch-note-e-number-form">
          <fieldset disabled={frozen} className={styles.fieldset}>
            <legend>{T.eDispatchNumber}</legend>
            <label htmlFor={eNumberId}>{T.eDispatchNumberLabel}</label>{' '}
            <input id={eNumberId} value={eNumber} maxLength={16} autoComplete="off" aria-invalid={error?.field === 'eNumber'}
              onChange={(event) => setENumber(event.target.value)} data-testid="logistics-dispatch-note-e-number-input" />{' '}
            <button type="submit" className={styles.secondaryButton} data-testid="logistics-dispatch-note-e-number-save">{T.eDispatchNumberSave}</button>
            {error?.field === 'eNumber' ? <p role="alert" className={styles.fieldError}>{error.message}</p> : null}
          </fieldset>
        </form>
      ) : null}
    </div>
  )
}
