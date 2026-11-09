/**
 * UI-64b / API-35 (PR #981): match an approved sales invoice to the official e-invoice — the form under the invoice's match
 * facts (S-10 "Fatura Taslağı / Eşleştirme Workspace", S-04 "Faturalar" tab).
 *
 *  - The difference, the tolerance and who approved an excess are the server's text, shown verbatim; the screen never compares,
 *    rounds or recomputes an amount or a tolerance (SAT-D229, AGENTS.md "Fiyat/formül/toleransı ekran içinde yeniden yazma").
 *  - The form shows only when the server's state allows a match (Approved, no match) AND the caller may use it. The shell
 *    publishes no capability for `sales.invoices.match` yet, so the routed tab mounts it with `canMatch` false and says so.
 *  - The difference-approval fields appear only after the server asked for an approval for exactly this number and ETTN, and
 *    only when `canApproveDifference` (SAT-D106 / SAT-D204); otherwise the need for a separate approval is said neutrally.
 *  - 401/403 go to the shell's auth boundary; an unknown outcome keeps the same key until it is retried or left.
 */
import { useId, useState } from 'react'
import type { SalesInvoiceDetail } from './invoiceFinalizationContract'
import {
  DIFFERENCE_REASON_MAX_LENGTH,
  OFFICIAL_INVOICE_NO_MAX_LENGTH,
  invoiceMatchNotice,
  normalizedIdentity,
  offerMatch,
  useInvoiceMatch,
  validateMatchForm,
  type InvoiceMatchController,
  type InvoiceMatchForm,
  type InvoiceMatchFormErrors,
} from './invoiceMatch'
import { isOutcomeStillUnknown } from './orderAttempt'
import { INVOICE_FINALIZATION_TEXT as F } from './invoiceText'
import { INVOICE_MATCH_TEXT as T } from './invoiceMatchText'
import { useShellState } from '../shell/shellContext'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './SalesPages.module.css'

const EMPTY_FORM: InvoiceMatchForm = { officialInvoiceNo: '', ettn: '', approveDifference: false, differenceReason: '' }

export function SalesInvoiceMatchPanel({
  detail,
  companyId,
  canMatch,
  canApproveDifference,
  onRecorded,
}: {
  readonly detail: SalesInvoiceDetail
  readonly companyId: string
  /** The caller may use the match form (a shell capability); the server's state decides the rest. */
  readonly canMatch: boolean
  /** The caller may approve a difference above tolerance (a shell capability); the server still checks the authority. */
  readonly canApproveDifference: boolean
  /** Called after a recorded match or a state/version conflict, so the invoice is read again. */
  readonly onRecorded: () => void
}) {
  const shell = useShellState()
  const controller = useInvoiceMatch({ shell, invoiceId: detail.id, companyId, onRecorded })
  const offered = offerMatch(detail)
  const { attempt } = controller
  if (!offered && attempt.phase === 'idle') return null
  return (
    <div className={styles.section} data-testid="sales-invoice-match-panel">
      <MatchNotice controller={controller} />
      {offered ? (
        canMatch ? (
          <MatchForm detail={detail} controller={controller} canApproveDifference={canApproveDifference} />
        ) : (
          <p className={styles.muted} data-testid="sales-invoice-match-unavailable">{T.formUnavailable}</p>
        )
      ) : null}
    </div>
  )
}

function MatchNotice({ controller }: { readonly controller: InvoiceMatchController }) {
  const { attempt } = controller
  const notice = invoiceMatchNotice(attempt)
  const match = attempt.phase === 'completed' ? attempt.result.match : null
  return (
    <>
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-invoice-match-notice">
          {notice}
        </p>
      )}
      {match === null ? null : (
        <dl className={styles.facts} aria-label={T.resultCaption} data-testid="sales-invoice-match-result">
          <div><dt>{T.resultDifference}</dt><dd className={styles.numericValue} data-testid="sales-invoice-match-result-difference">{match.difference}</dd></div>
          <div><dt>{T.resultTolerance}</dt><dd className={styles.numericValue} data-testid="sales-invoice-match-result-tolerance">{match.tolerance}</dd></div>
          {match.differenceApprovedBy === null ? null : (
            <div><dt>{T.resultApprovedBy}</dt><dd data-testid="sales-invoice-match-result-approved-by">{match.differenceApprovedBy}</dd></div>
          )}
          {match.differenceReason === null ? null : (
            <div><dt>{T.resultReason}</dt><dd>{match.differenceReason}</dd></div>
          )}
        </dl>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={controller.retry} data-testid="sales-invoice-match-retry">{F.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={controller.leave} data-testid="sales-invoice-match-leave">
            {F.abandon}
          </Button>
        </div>
      ) : null}
    </>
  )
}

function MatchForm({
  detail,
  controller,
  canApproveDifference,
}: {
  readonly detail: SalesInvoiceDetail
  readonly controller: InvoiceMatchController
  readonly canApproveDifference: boolean
}) {
  const headingId = useId()
  const [form, setForm] = useState<InvoiceMatchForm>(EMPTY_FORM)
  const [errors, setErrors] = useState<InvoiceMatchFormErrors>({})
  const { attempt, approvalAskedFor } = controller
  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const identity = normalizedIdentity(form)
  const approvalAsked = approvalAskedFor !== null
    && approvalAskedFor.officialInvoiceNo === identity.officialInvoiceNo
    && approvalAskedFor.ettn === identity.ettn
  const approvalOffered = approvalAsked && canApproveDifference

  function update(patch: Partial<InvoiceMatchForm>) {
    setForm((current) => ({ ...current, ...patch }))
  }

  function start() {
    const result = validateMatchForm(form, approvalOffered)
    if (!result.ok) {
      setErrors(result.errors)
      return
    }
    setErrors({})
    controller.submit(detail, result.body)
  }

  return (
    <form
      className={styles.section}
      aria-labelledby={headingId}
      data-testid="sales-invoice-match-form"
      noValidate
      onSubmit={(event) => {
        event.preventDefault()
        start()
      }}
    >
      <h4 id={headingId}>{T.formHeading}</h4>
      <p className={styles.muted}>{T.formNote}</p>
      <Field label={T.officialInvoiceNo} help={T.officialInvoiceNoHelp(OFFICIAL_INVOICE_NO_MAX_LENGTH)} error={errors.officialInvoiceNo}>
        {(control) => (
          <input
            {...control}
            type="text"
            autoComplete="off"
            maxLength={OFFICIAL_INVOICE_NO_MAX_LENGTH}
            value={form.officialInvoiceNo}
            disabled={frozen}
            onChange={(event) => update({ officialInvoiceNo: event.target.value })}
            data-testid="sales-invoice-match-official-no"
          />
        )}
      </Field>
      <Field label={T.ettn} help={T.ettnHelp} error={errors.ettn}>
        {(control) => (
          <input
            {...control}
            type="text"
            autoComplete="off"
            spellCheck={false}
            value={form.ettn}
            disabled={frozen}
            onChange={(event) => update({ ettn: event.target.value })}
            data-testid="sales-invoice-match-ettn"
          />
        )}
      </Field>
      {approvalAsked && !canApproveDifference ? (
        <p className={styles.notice} role="status" data-testid="sales-invoice-match-approval-unavailable">{T.approvalNotAvailable}</p>
      ) : null}
      {approvalOffered ? (
        <fieldset className={styles.fieldset} data-testid="sales-invoice-match-approval">
          <p className={styles.muted}>{T.approvalNeeded}</p>
          <label>
            <input
              type="checkbox"
              checked={form.approveDifference}
              disabled={frozen}
              onChange={(event) => update({ approveDifference: event.target.checked })}
              data-testid="sales-invoice-match-approve"
            />{' '}
            {T.approveDifference}
          </label>
          {form.approveDifference ? (
            <Field label={T.differenceReason} help={T.differenceReasonHelp} error={errors.differenceReason}>
              {(control) => (
                <textarea
                  {...control}
                  maxLength={DIFFERENCE_REASON_MAX_LENGTH}
                  value={form.differenceReason}
                  disabled={frozen}
                  onChange={(event) => update({ differenceReason: event.target.value })}
                  data-testid="sales-invoice-match-reason"
                />
              )}
            </Field>
          ) : null}
        </fieldset>
      ) : null}
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={frozen} data-testid="sales-invoice-match-submit">
          {approvalOffered && form.approveDifference ? T.submitApproval : T.submit}
        </Button>
      </div>
    </form>
  )
}
