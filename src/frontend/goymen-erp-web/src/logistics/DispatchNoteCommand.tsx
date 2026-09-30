/**
 * AT-055 — the dispatch-note (irsaliye) DRAFT on L-02-DETAIL (master v3 §7.8 "fark kontrolü → irsaliye taslağı", §27.8 L-08
 * Satış İrsaliyesi Hazırlığı: "gerçekleşmeden belgeye"), `POST /api/v1/logistics/loading-instructions/{id}/dispatch-notes`.
 *
 *  - `DispatchNoteFacts` shows every draft of the instruction to every reader, plainly on screen: its reference, preparer,
 *    time and lines — each line one definite shipment and its SHIPPED quantity, as the server answered it (nothing is
 *    recomputed). Each draft carries the anchor `irsaliye-{id}` so the Sales order view can link straight to it. No number,
 *    print layout or submission exists: numbering, finalising, cancelling, a template (OPEN-019 / DEC-DOC-010) and
 *    e-İrsaliye (DEC-INT-001) are not decided, and the screen says so.
 *  - `DispatchNoteForm` is offered on an Issued instruction with at least one definite shipment not yet on a draft. Like the
 *    record-load and weighing forms (ASSUMPTION-LOG4B-2) it is shown to every holder of the detail; the server measures
 *    `logistics.dispatch-notes.create` afresh and answers a code-less 403 to anyone else. A shipment already on a draft is
 *    shown as linked and cannot be picked (AT-055); the server refuses a second link anyway. One draft id is minted per
 *    attempt snapshot and travels with it, so a retry never makes a second draft.
 */

import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router'
import { createDispatchNote } from './api'
import {
  INSTRUCTION_STATUS,
  SHARE_KIND,
  dispatchNoteOf,
  type CreateDispatchNoteRequest,
  type DispatchNote,
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
      aria-label={`${T.draft} ${shortReference(note.dispatchNoteId)}`}>
      <dl className={styles.facts}>
        <div>
          <dt>{T.reference}</dt>
          <dd className={styles.identifier} data-testid="logistics-dispatch-note-reference">{note.dispatchNoteId}</dd>
        </div>
        <div>
          <dt>{INSTRUCTION_DETAIL_TEXT.status}</dt>
          <dd><span className={styles.badge} data-testid="logistics-dispatch-note-status">{T.draft}</span></dd>
        </div>
        <div>
          <dt>{T.createdBy}</dt>
          <dd className={styles.identifier}>{note.createdBy}</dd>
        </div>
        <div>
          <dt>{T.createdAt}</dt>
          <dd>{formatInstant(note.createdAtUtc)}</dd>
        </div>
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
