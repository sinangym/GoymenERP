import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { useShellDispatch } from '../shell/shellContext'
import { isDateOnly } from './dateOnly'
import {
  useFinanceBinding,
  useFinanceRead,
  usePaymentReversalCommand,
  type PaymentReversalCommand,
} from './financePort'
import { ABANDON, REFUSAL_TITLE, RESEND_SAME, issueText } from './financeText'
import type { ManualBankPaymentView } from './manualBankPaymentContract'
import { decodePaymentReversalProposalPage, type PaymentReversalProposalView } from './paymentReversalContract'
import { PAYMENT_REVERSAL as T } from './paymentReversalText'
import styles from './Finance.module.css'

/**
 * API-23: maker-checker reversal of a recorded manual bank payment. The maker proposes (date +
 * reason); another authorised user approves or rejects. The screen never decides eligibility —
 * period, allocations and "already reversed" are the server's refusals, shown by issue code.
 */
export function PaymentReversalPanel({
  payment,
  canPropose,
  canApprove,
}: {
  readonly payment: ManualBankPaymentView
  readonly canPropose: boolean
  readonly canApprove: boolean
}) {
  const pending = useFinanceRead(
    'payment-reversal-proposals-pending',
    [],
    (port, scope, signal) => port.pendingPaymentReversals(scope, signal),
    (resource) => decodePaymentReversalProposalPage(resource),
  )
  const [settled, setSettled] = useState<PaymentReversalProposalView | null>(null)
  const { refetch } = pending
  const onSettled = useCallback((result: PaymentReversalProposalView) => {
    setSettled(result)
    refetch()
  }, [refetch])

  const mine = pending.state.kind === 'ready'
    ? pending.state.data.items.filter((proposal) => proposal.paymentId === payment.id)
    : []

  return (
    <section className={styles.command} aria-label={T.title}>
      <h2>{T.title}</h2>
      <p className={styles.notice}>{T.notice}</p>
      {settled === null ? null : (
        <p role="status">
          {settled.status === 'Approved' ? T.approved : settled.status === 'Rejected' ? T.rejected : T.proposed}
        </p>
      )}
      {pending.state.kind !== 'ready' ? (
        <StateBlock state={pending.state} subject={T.subject} onRetry={refetch} />
      ) : mine.length === 0 ? (
        <>
          <p role="status">{T.pendingNone}</p>
          {canPropose ? <ProposeForm payment={payment} onSettled={onSettled} /> : null}
        </>
      ) : (
        mine.map((proposal) => (
          <ProposalDecision key={proposal.id} proposal={proposal} canApprove={canApprove} onSettled={onSettled} />
        ))
      )}
    </section>
  )
}

function ProposeForm({
  payment,
  onSettled,
}: {
  readonly payment: ManualBankPaymentView
  readonly onSettled: (result: PaymentReversalProposalView) => void
}) {
  const command = usePaymentReversalCommand('propose', payment.id)
  const [open, setOpen] = useState(false)
  const [occurredOn, setOccurredOn] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<{ occurredOn?: string; reason?: string }>({})
  const frozen = frozenCommand(command)
  useDirty(`finance.payment-reversal.${payment.id}`,
    frozen || (open && (occurredOn.trim().length > 0 || reason.trim().length > 0)))
  useReported(command, onSettled)

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    const next: { occurredOn?: string; reason?: string } = {}
    if (occurredOn.trim().length === 0) next.occurredOn = T.dateRequired
    else if (!isDateOnly(occurredOn.trim())) next.occurredOn = T.dateFormat
    if (reason.trim().length === 0) next.reason = T.reasonRequired
    setErrors(next)
    if (Object.keys(next).length > 0) return
    command.submit({
      operation: 'propose',
      body: { paymentId: payment.id, expectedVersion: payment.version, occurredOn: occurredOn.trim(), reason: reason.trim() },
    })
  }

  if (command.state.phase === 'succeeded') return null
  return (
    <div>
      <AttemptState command={command} />
      {!open && !frozen ? (
        <button type="button" disabled={!command.available} onClick={() => setOpen(true)}>{T.open}</button>
      ) : (
        <form onSubmit={onSubmit} noValidate className={styles.form}>
          <div className={styles.field}>
            <label htmlFor="finance-payment-reverse-date">{T.date}</label>
            <input
              id="finance-payment-reverse-date"
              type="date"
              value={occurredOn}
              disabled={frozen}
              aria-invalid={errors.occurredOn !== undefined}
              aria-describedby={errors.occurredOn ? 'finance-payment-reverse-date-error' : undefined}
              onChange={(event) => setOccurredOn(event.target.value)}
            />
            {errors.occurredOn ? (
              <p id="finance-payment-reverse-date-error" className={styles.error}>{errors.occurredOn}</p>
            ) : null}
          </div>
          <div className={styles.field}>
            <label htmlFor="finance-payment-reverse-reason">{T.reason}</label>
            <textarea
              id="finance-payment-reverse-reason"
              value={reason}
              disabled={frozen}
              rows={3}
              aria-invalid={errors.reason !== undefined}
              aria-describedby={errors.reason ? 'finance-payment-reverse-reason-error' : undefined}
              onChange={(event) => setReason(event.target.value)}
            />
            {errors.reason ? (
              <p id="finance-payment-reverse-reason-error" className={styles.error}>{errors.reason}</p>
            ) : null}
          </div>
          <div className={styles.actions}>
            <button type="submit" disabled={frozen || !command.available}>{T.submit}</button>
            {frozen ? null : (
              <button type="button" onClick={() => {
                setOpen(false)
                setOccurredOn('')
                setReason('')
                setErrors({})
              }}>{T.cancel}</button>
            )}
          </div>
        </form>
      )}
    </div>
  )
}

function ProposalDecision({
  proposal,
  canApprove,
  onSettled,
}: {
  readonly proposal: PaymentReversalProposalView
  readonly canApprove: boolean
  readonly onSettled: (result: PaymentReversalProposalView) => void
}) {
  const binding = useFinanceBinding()
  const approve = usePaymentReversalCommand('approve', proposal.id)
  const reject = usePaymentReversalCommand('reject', proposal.id)
  const [reason, setReason] = useState('')
  const ownProposal = binding?.binding.actorId === proposal.proposedBy
  const decide = canApprove && !ownProposal
  const frozen = frozenCommand(approve) || frozenCommand(reject)
  useDirty(`finance.payment-reversal-decision.${proposal.id}`, decide && (frozen || reason.trim().length > 0))
  useReported(approve, onSettled)
  useReported(reject, onSettled)

  return (
    <article className={styles.command} aria-label={T.proposalLabel(proposal.id)}>
      <h3>{T.occurredOn(proposal.occurredOn)}</h3>
      <p>{proposal.reason}</p>
      {ownProposal ? <p role="status">{T.ownProposal}</p> : null}
      <AttemptState command={approve} />
      <AttemptState command={reject} />
      {!decide ? null : (
        <div className={styles.form}>
          <div className={styles.actions}>
            <button
              type="button"
              disabled={frozen || !approve.available}
              onClick={() => approve.submit({
                operation: 'approve', body: { proposalId: proposal.id, expectedVersion: proposal.version },
              })}
            >
              {T.approve}
            </button>
          </div>
          <label htmlFor={`finance-payment-reversal-reject-${proposal.id}`}>{T.rejectReason}</label>
          <textarea
            id={`finance-payment-reversal-reject-${proposal.id}`}
            value={reason}
            disabled={frozen}
            rows={2}
            onChange={(event) => setReason(event.target.value)}
          />
          <button
            type="button"
            disabled={frozen || !reject.available || reason.trim().length === 0}
            onClick={() => reject.submit({
              operation: 'reject',
              body: { proposalId: proposal.id, expectedVersion: proposal.version, reason: reason.trim() },
            })}
          >
            {T.reject}
          </button>
        </div>
      )}
    </article>
  )
}

function frozenCommand(command: PaymentReversalCommand): boolean {
  return command.state.phase === 'submitting' || command.state.phase === 'unknown'
    || command.state.phase === 'conflicted'
}

function AttemptState({ command }: { readonly command: PaymentReversalCommand }) {
  const state = command.state
  if (state.phase === 'idle' || state.phase === 'succeeded') return null
  if (state.phase === 'submitting') return <p role="status">{T.sending}</p>
  if (state.phase === 'unknown') {
    return (
      <div className={styles.attempt} role="status">
        <p>{T.unknown}</p>
        <button type="button" onClick={command.resend}>{RESEND_SAME}</button>
        <button type="button" onClick={command.abandon}>{ABANDON}</button>
      </div>
    )
  }
  if (state.phase === 'conflicted') {
    return (
      <div className={styles.attempt} role="status">
        <p>{T.conflicted}</p>
        <button type="button" onClick={command.abandon}>{ABANDON}</button>
      </div>
    )
  }
  return (
    <div className={styles.attempt} role="status">
      <p>{REFUSAL_TITLE[state.reason]}</p>
      {state.issues.map((issue, index) => (
        <p key={`${issue.field}-${issue.code}-${index}`}>{issueText(issue.code)}</p>
      ))}
    </div>
  )
}

/** Reports a succeeded command once, so the panel re-reads the pending proposals. */
function useReported(
  command: PaymentReversalCommand,
  onSettled: (result: PaymentReversalProposalView) => void,
) {
  const reported = useRef(false)
  const result = command.state.phase === 'succeeded' ? command.state.resource : null
  useEffect(() => {
    if (result !== null && !reported.current) {
      reported.current = true
      onSettled(result)
    }
  }, [result, onSettled])
}

function useDirty(scope: string, dirty: boolean) {
  const dispatch = useShellDispatch()
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope })
  }, [dirty, dispatch, scope])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope }), [dispatch, scope])
}
