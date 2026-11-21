/**
 * ALS-D111 + ALS-D113 — the Cari card's official legal name (resmi/hukuki unvan), changed by maker-checker.
 *
 * What the user can do here, and nothing more:
 *  - READ the legal name of the card, the pending proposal (if any) and the last decided one.
 *  - PROPOSE a new legal name with a reason, when the server says this actor holds the SEPARATE propose permission and
 *    nothing is pending. The name does NOT change then. A blank name, the current name or a blank reason is refused
 *    on the client before sending; the server decides again.
 *  - APPROVE or REJECT the pending proposal (with an optional note), when the server says this actor holds the SEPARATE
 *    approve permission AND is not its proposer. The proposer's own request shows no decision buttons; the server
 *    refuses a self-decision anyway. One approval is enough; only the approval writes the new name.
 *
 * Every command is fenced on the party version the panel read. What is offered comes from the read's `viewer` block,
 * measured by the server for this actor and company; the server re-authorizes every command. The read is a side read
 * of the card and never moves its command-readiness slot; the commands use the card's own contract context. The trade
 * name stays on its own panel (ALS-D97).
 */

import { useCallback, useId, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContractContext } from './clientContract'
import {
  LEGAL_NAME_MAX,
  LEGAL_NAME_NOTE_MAX,
  LEGAL_NAME_REASON_MAX,
  LEGAL_NAME_TEXT as T,
  checkLegalName,
  checkLegalNameNote,
  checkLegalNameReason,
  type LegalNameChange,
  type LegalNameDecision,
  type LegalNameView,
} from './legalName'
import { decideLegalNameChange, fetchLegalName, proposeLegalNameChange } from './legalNameApi'
import { useLegalNameCommand, type LegalNameCommand } from './useLegalNameCommand'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariTradeName.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
  /** Called after an applied command so the card's own read and the names panel (which show the name too) refresh. */
  readonly onChanged?: () => void
}

function formatInstant(iso: string): string {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString('tr-TR')
}

export function CariLegalNamePanel({ partyId, companyId, namespace, contractContext, onChanged }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const queryKey = useMemo(() => [namespace, 'counterparty-legal-name', partyId, companyId], [
    namespace,
    partyId,
    companyId,
  ])

  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchLegalName(partyId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })

  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey })
    onChanged?.()
  }, [onChanged, queryClient, queryKey])
  const command = useLegalNameCommand(companyId, contractContext, `cari.legal-name:${partyId}`, refresh)

  if (read.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-legal-name" data-state="loading">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.muted} role="status">
          {T.loading}
        </p>
      </div>
    )
  }

  const outcome = read.data
  if (outcome === undefined || outcome.type !== 'ok') {
    const forbidden = outcome?.type === 'http' && outcome.status === 403
    const notFound = outcome?.type === 'http' && outcome.status === 404
    const state = forbidden ? 'forbidden' : notFound ? 'not-found' : 'error'
    return (
      <div className={styles.panel} data-testid="cari-legal-name" data-state={state}>
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.error} role="alert">
          {forbidden ? T.forbidden : notFound ? T.notFound : T.loadError}
        </p>
        {forbidden || notFound ? null : (
          <div className={styles.actions}>
            <button type="button" className={styles.button} onClick={() => void read.refetch()}>
              {T.readAgain}
            </button>
          </div>
        )}
      </div>
    )
  }

  const view = outcome.data
  const { viewer } = view
  const pending = view.pendingChange
  const canDecide = pending !== null && viewer.canApprove && !viewer.isRequesterOfPending
  const canPropose = pending === null && viewer.canPropose
  const readOnly = !viewer.canPropose && !viewer.canApprove

  return (
    <div className={styles.panel} data-testid="cari-legal-name" data-state="ready">
      <h3 className={styles.title}>{T.heading}</h3>
      <dl className={styles.facts}>
        <dt>{T.legalNameLabel}</dt>
        <dd data-legal-name>{view.legalName}</dd>
      </dl>
      <p className={styles.muted}>
        {T.sharedNote} {T.makerCheckerNote}
      </p>

      {pending === null ? null : (
        <PendingChange
          change={pending}
          isOwn={viewer.isRequesterOfPending}
          decision={
            canDecide ? (
              <DecisionForm
                key={`${pending.id}:${view.version}`}
                partyId={partyId}
                view={view}
                change={pending}
                command={command}
              />
            ) : null
          }
        />
      )}
      {pending !== null && viewer.canPropose ? (
        <p className={styles.muted} data-legal-name-propose-blocked>
          {T.pendingBlocksPropose}
        </p>
      ) : null}

      {canPropose ? <ProposeForm key={view.version} partyId={partyId} view={view} command={command} /> : null}

      {view.lastDecidedChange === null ? null : <LastDecidedChange change={view.lastDecidedChange} />}

      {readOnly ? (
        <p className={styles.muted} data-legal-name-read-only>
          {T.readOnlyNote}
        </p>
      ) : null}

      <div aria-live="polite">
        {command.notice === null ? null : (
          <p
            className={command.notice.tone === 'ok' ? styles.ok : styles.error}
            role={command.notice.tone === 'ok' ? 'status' : 'alert'}
            data-notice={command.notice.tone}
          >
            {command.notice.text}
          </p>
        )}
      </div>
      {command.unresolved && !command.busy ? (
        <div className={styles.actions}>
          <button type="button" className={styles.button} onClick={() => void command.retry()}>
            {T.retrySame}
          </button>
        </div>
      ) : null}
    </div>
  )
}

function PendingChange({
  change,
  isOwn,
  decision,
}: {
  readonly change: LegalNameChange
  readonly isOwn: boolean
  readonly decision: ReactNode
}) {
  return (
    <section data-legal-name-pending={change.id} aria-label={T.pendingHeading}>
      <h4 className={styles.title}>
        {T.pendingHeading} — {T.statusLabel[change.status]}
      </h4>
      <dl className={styles.facts}>
        <dt>{T.pendingCurrent}</dt>
        <dd>{change.currentLegalName}</dd>
        <dt>{T.pendingProposed}</dt>
        <dd data-legal-name-proposed>{change.proposedLegalName}</dd>
        <dt>{T.pendingReason}</dt>
        <dd>{change.reason}</dd>
        <dt>{T.pendingRequestedBy}</dt>
        <dd>{change.requestedBy}</dd>
        <dt>{T.pendingRequestedAt}</dt>
        <dd>
          <time dateTime={change.requestedAtUtc}>{formatInstant(change.requestedAtUtc)}</time>
        </dd>
      </dl>
      {isOwn ? (
        <p className={styles.muted} data-legal-name-own-pending>
          {T.ownPendingNote}
        </p>
      ) : decision === null ? (
        <p className={styles.muted} data-legal-name-waiting>
          {T.waitingNote}
        </p>
      ) : (
        decision
      )}
    </section>
  )
}

function LastDecidedChange({ change }: { readonly change: LegalNameChange }) {
  return (
    <section data-legal-name-last-decided={change.status} aria-label={T.lastDecidedHeading}>
      <h4 className={styles.title}>
        {T.lastDecidedHeading} — {T.statusLabel[change.status]}
      </h4>
      <dl className={styles.facts}>
        <dt>{T.pendingCurrent}</dt>
        <dd>{change.currentLegalName}</dd>
        <dt>{T.pendingProposed}</dt>
        <dd>{change.proposedLegalName}</dd>
        <dt>{T.pendingReason}</dt>
        <dd>{change.reason}</dd>
        <dt>{T.pendingRequestedBy}</dt>
        <dd>{change.requestedBy}</dd>
        <dt>{T.decidedBy}</dt>
        <dd>{change.decidedBy}</dd>
        {change.decidedAtUtc === null ? null : (
          <>
            <dt>{T.decidedAt}</dt>
            <dd>
              <time dateTime={change.decidedAtUtc}>{formatInstant(change.decidedAtUtc)}</time>
            </dd>
          </>
        )}
        {change.decisionNote === null ? null : (
          <>
            <dt>{T.decisionNote}</dt>
            <dd>{change.decisionNote}</dd>
          </>
        )}
      </dl>
    </section>
  )
}

function ProposeForm({
  partyId,
  view,
  command,
}: {
  readonly partyId: string
  readonly view: LegalNameView
  readonly command: LegalNameCommand
}) {
  const nameId = useId()
  const nameHintId = useId()
  const reasonId = useId()
  const reasonHintId = useId()
  const errorId = useId()
  const [name, setName] = useState('')
  const [reason, setReason] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const locked = command.busy || command.unresolved

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (locked) return
    const nameCheck = checkLegalName(name)
    if (!nameCheck.ok) {
      setFieldError(nameCheck.message)
      return
    }
    if (nameCheck.value === view.legalName) {
      setFieldError(T.sameAsCurrent)
      return
    }
    const reasonCheck = checkLegalNameReason(reason)
    if (!reasonCheck.ok) {
      setFieldError(reasonCheck.message)
      return
    }
    setFieldError(null)
    const proposed = nameCheck.value
    const why = reasonCheck.value
    const ok = await command.run(
      `propose:${partyId}`,
      (context) => proposeLegalNameChange(context, partyId, view.version, proposed, why),
      T.proposed,
    )
    if (ok) {
      setName('')
      setReason('')
    }
  }

  const describedBy = (hint: string) => (fieldError === null ? hint : `${hint} ${errorId}`)

  return (
    <form
      className={styles.form}
      onSubmit={(event) => void submit(event)}
      aria-busy={command.busy}
      aria-label={T.proposeHeading}
      data-legal-name-propose
      noValidate
    >
      <h4 className={styles.title}>{T.proposeHeading}</h4>
      <label htmlFor={nameId}>{T.proposedLabel}</label>
      <input
        id={nameId}
        type="text"
        value={name}
        maxLength={LEGAL_NAME_MAX}
        required
        disabled={locked}
        aria-describedby={describedBy(nameHintId)}
        aria-invalid={fieldError !== null}
        onChange={(event) => {
          setName(event.target.value)
          setFieldError(null)
        }}
      />
      <p id={nameHintId} className={styles.muted}>
        {T.proposedHint}
      </p>
      <label htmlFor={reasonId}>{T.reasonLabel}</label>
      <textarea
        id={reasonId}
        value={reason}
        maxLength={LEGAL_NAME_REASON_MAX}
        required
        disabled={locked}
        aria-describedby={describedBy(reasonHintId)}
        onChange={(event) => {
          setReason(event.target.value)
          setFieldError(null)
        }}
      />
      <p id={reasonHintId} className={styles.muted}>
        {T.reasonHint}
      </p>
      {fieldError === null ? null : (
        <p id={errorId} className={styles.error} role="alert">
          {fieldError}
        </p>
      )}
      <div className={styles.actions}>
        <button type="submit" className={styles.button} disabled={locked}>
          {command.busy ? T.proposing : T.propose}
        </button>
      </div>
    </form>
  )
}

function DecisionForm({
  partyId,
  view,
  change,
  command,
}: {
  readonly partyId: string
  readonly view: LegalNameView
  readonly change: LegalNameChange
  readonly command: LegalNameCommand
}) {
  const noteId = useId()
  const noteHintId = useId()
  const errorId = useId()
  const [note, setNote] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const locked = command.busy || command.unresolved

  const decide = async (decision: LegalNameDecision) => {
    if (locked) return
    const noteCheck = checkLegalNameNote(note)
    if (!noteCheck.ok) {
      setFieldError(noteCheck.message)
      return
    }
    setFieldError(null)
    const text = noteCheck.value
    const ok = await command.run(
      `${decision}:${partyId}:${change.id}`,
      (context) => decideLegalNameChange(context, partyId, view.version, change.id, decision, text),
      decision === 'approve' ? T.approved : T.rejected,
    )
    if (ok) setNote('')
  }

  return (
    <div className={styles.form} data-legal-name-decision aria-busy={command.busy}>
      <label htmlFor={noteId}>{T.noteLabel}</label>
      <textarea
        id={noteId}
        value={note}
        maxLength={LEGAL_NAME_NOTE_MAX}
        disabled={locked}
        aria-describedby={fieldError === null ? noteHintId : `${noteHintId} ${errorId}`}
        aria-invalid={fieldError !== null}
        onChange={(event) => {
          setNote(event.target.value)
          setFieldError(null)
        }}
      />
      <p id={noteHintId} className={styles.muted}>
        {T.noteHint}
      </p>
      {fieldError === null ? null : (
        <p id={errorId} className={styles.error} role="alert">
          {fieldError}
        </p>
      )}
      <div className={styles.actions}>
        <button type="button" className={styles.button} disabled={locked} onClick={() => void decide('approve')}>
          {command.busy ? T.deciding : T.approve}
        </button>
        <button type="button" className={styles.button} disabled={locked} onClick={() => void decide('reject')}>
          {T.reject}
        </button>
      </div>
    </div>
  )
}
