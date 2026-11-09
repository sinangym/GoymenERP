/**
 * UI-64 (API-33): the invoice draft's submit / approve / reject / cancel controls and its decision history, inside the invoice
 * panel. A command shows only when the SERVER's state offers it AND the caller holds the control capability
 * (`sales.invoices.write` for submit / cancel, `sales.invoices.approve` for approve / reject); the server re-measures both and
 * decides who may approve (never the preparer or the submitter, SAT-D88 / SAT-D204). Each send carries the read version as
 * `expectedVersion` and one Idempotency-Key per attempt; a conflict re-reads the invoice, an unknown outcome offers only
 * "retry the same" or "leave it". A reason is optional, as on the server; its length is checked against the server's bound.
 */
import { useId, useState } from 'react'
import { LIMITS } from './contract'
import { INVOICE_DRAFT_COMMAND, commandTakesReason, type InvoiceDraftCommand } from './invoiceDraftApi'
import { invoiceDraftCommandNotice, offeredDraftCommands, type InvoiceDraftCommandController } from './invoiceDraft'
import { INVOICE_DRAFT_ACTIONS_TEXT as A } from './invoiceDraftText'
import type { InvoiceDecision, SalesInvoiceDetail } from './invoiceFinalizationContract'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { isOutcomeStillUnknown } from './orderAttempt'
import { formatInstant } from './format'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

const WRITE_COMMANDS: readonly InvoiceDraftCommand[] = [INVOICE_DRAFT_COMMAND.submit, INVOICE_DRAFT_COMMAND.cancel]
const APPROVE_COMMANDS: readonly InvoiceDraftCommand[] = [INVOICE_DRAFT_COMMAND.approve, INVOICE_DRAFT_COMMAND.reject]

export function InvoiceDraftAttemptNotice({ controller }: { readonly controller: InvoiceDraftCommandController }) {
  const { attempt } = controller
  const notice = invoiceDraftCommandNotice(attempt)
  return (
    <>
      {notice === null ? null : (
        <p
          role={attempt.phase === 'failed' || attempt.phase === 'unknown' ? 'alert' : 'status'}
          className={styles.notice}
          data-testid="sales-invoice-draft-notice"
        >
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={controller.retry} data-testid="sales-invoice-draft-retry">{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={controller.leave} data-testid="sales-invoice-draft-abandon">
            {T.abandon}
          </Button>
        </div>
      ) : null}
    </>
  )
}

export function InvoiceDecisionFacts({ decision }: { readonly decision: InvoiceDecision | null }) {
  if (decision === null) return null
  const rows: { readonly label: string; readonly value: string | null }[] = [
    { label: A.submittedBy, value: decision.submittedBy },
    { label: A.submittedAt, value: decision.submittedAtUtc === null ? null : formatInstant(decision.submittedAtUtc) },
    { label: A.decidedBy, value: decision.decidedBy },
    { label: A.decidedAt, value: decision.decidedAtUtc === null ? null : formatInstant(decision.decidedAtUtc) },
    { label: A.decisionReason, value: decision.decisionReason },
    { label: A.cancelledBy, value: decision.cancelledBy },
    { label: A.cancelledAt, value: decision.cancelledAtUtc === null ? null : formatInstant(decision.cancelledAtUtc) },
    { label: A.cancellationReason, value: decision.cancellationReason },
  ]
  const shown = rows.filter((row) => row.value !== null)
  if (shown.length === 0) return null
  return (
    <>
      <h4>{A.decisionHeading}</h4>
      <dl className={styles.facts} data-testid="sales-invoice-draft-decision">
        {shown.map((row) => (
          <div key={row.label}><dt>{row.label}</dt><dd>{row.value}</dd></div>
        ))}
      </dl>
    </>
  )
}

export function SalesInvoiceDraftActions({
  detail,
  canWrite,
  canApprove,
  controller,
}: {
  readonly detail: SalesInvoiceDetail
  readonly canWrite: boolean
  readonly canApprove: boolean
  readonly controller: InvoiceDraftCommandController
}) {
  const headingId = useId()
  const reasonId = useId()
  const hintId = useId()
  const [reasonFor, setReasonFor] = useState<InvoiceDraftCommand | null>(null)
  const [reason, setReason] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)
  const offered = offeredDraftCommands(detail)
  if (offered.length === 0) return null
  const { attempt } = controller
  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const allowed = (kind: InvoiceDraftCommand) =>
    offered.includes(kind) && (WRITE_COMMANDS.includes(kind) ? canWrite : canApprove)
  const writeHidden = offered.some((kind) => WRITE_COMMANDS.includes(kind)) && !canWrite
  const approveHidden = offered.some((kind) => APPROVE_COMMANDS.includes(kind)) && !canApprove
  const formOpen = reasonFor !== null && allowed(reasonFor) && attempt.phase !== 'completed'

  function start(kind: InvoiceDraftCommand) {
    setLocalError(null)
    if (commandTakesReason(kind)) {
      setReasonFor(kind)
      return
    }
    setReasonFor(null)
    controller.submit(kind, detail, null)
  }

  function confirm() {
    if (reasonFor === null) return
    const trimmed = reason.trim()
    if (trimmed.length > LIMITS.note) {
      setLocalError(A.reasonTooLong(LIMITS.note))
      return
    }
    setLocalError(null)
    controller.submit(reasonFor, detail, trimmed.length === 0 ? null : trimmed)
  }

  const button = (kind: InvoiceDraftCommand, variant: 'primary' | 'secondary') =>
    allowed(kind) ? (
      <Button
        key={kind}
        type="button"
        variant={variant}
        disabled={frozen}
        aria-pressed={commandTakesReason(kind) ? reasonFor === kind && formOpen : undefined}
        onClick={() => start(kind)}
        data-testid={`sales-invoice-draft-${kind}`}
      >
        {A[kind]}
      </Button>
    ) : null

  return (
    <div className={styles.section} aria-labelledby={headingId} role="group" data-testid="sales-invoice-draft-actions">
      <h4 id={headingId}>{A.heading}</h4>
      {detail.state === 'Draft' && canWrite ? <p className={styles.muted}>{A.submitNote}</p> : null}
      {detail.state === 'PendingApproval' && canApprove ? <p className={styles.muted}>{A.approveNote}</p> : null}
      {allowed(INVOICE_DRAFT_COMMAND.cancel) ? <p className={styles.muted}>{A.cancelNote}</p> : null}
      {writeHidden ? <p className={styles.muted} data-testid="sales-invoice-draft-write-unavailable">{A.writeUnavailable}</p> : null}
      {approveHidden ? <p className={styles.muted} data-testid="sales-invoice-draft-approve-unavailable">{A.approveUnavailable}</p> : null}
      <div className={styles.actions}>
        {button(INVOICE_DRAFT_COMMAND.submit, 'primary')}
        {button(INVOICE_DRAFT_COMMAND.approve, 'primary')}
        {button(INVOICE_DRAFT_COMMAND.reject, 'secondary')}
        {button(INVOICE_DRAFT_COMMAND.cancel, 'secondary')}
      </div>
      {formOpen ? (
        <div className={styles.actions} data-testid="sales-invoice-draft-reason-form">
          {localError === null ? null : <p role="alert" className={styles.notice} data-testid="sales-invoice-draft-reason-error">{localError}</p>}
          <label htmlFor={reasonId}>{A.reasonLabel}</label>
          <textarea
            id={reasonId}
            aria-describedby={hintId}
            value={reason}
            disabled={frozen}
            onChange={(event) => setReason(event.target.value)}
            data-testid="sales-invoice-draft-reason"
          />
          <span id={hintId} className={styles.muted}>{A.reasonHint(LIMITS.note)}</span>
          <Button type="button" variant="primary" disabled={frozen} onClick={confirm} data-testid="sales-invoice-draft-confirm">
            {reasonFor === INVOICE_DRAFT_COMMAND.reject ? A.rejectConfirm : A.cancelConfirm}
          </Button>
          <Button type="button" variant="ghost" className={styles.linkButton} disabled={frozen} onClick={() => setReasonFor(null)}>
            {A.back}
          </Button>
        </div>
      ) : null}
    </div>
  )
}
