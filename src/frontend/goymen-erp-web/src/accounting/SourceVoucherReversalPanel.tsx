import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { ACCOUNTING_CAPABILITY, journalReversalApprovalPath } from './capabilities'
import { validateProposalForm, type ProposalForm } from './journalReversalContract'
import { NormalJournalApprovalPeriod } from './NormalJournalApprovalPeriod'
import { proposeSourceVoucherReversal, type SourceVoucher, type SourceVoucherOutcome, type SourceVoucherReversalAttempt } from './sourceVoucherApi'
import { SOURCE_VOUCHER_TEXT as T } from './sourceVoucherText'
import styles from './Accounting.module.css'

// A context/route remount must not replace an unresolved request with a different proposal or key.
const pending = new Map<string, SourceVoucherReversalAttempt>()
export function SourceVoucherReversalPanel({ row, namespace, onFrozenChange, onChanged }: {
  readonly row: SourceVoucher; readonly namespace: string; readonly onFrozenChange: (value: boolean) => void; readonly onChanged: () => void
}) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const actor = shell.activeContext?.userId ?? ''
  const slot = `${namespace}:${row.id}`; const held = pending.get(slot) ?? null
  const [form, setForm] = useState<ProposalForm>({ periodId: '', transactionDate: '', accountingDate: '', valueDate: '', reason: '' })
  const [attempt, setAttempt] = useState<SourceVoucherReversalAttempt | null>(held)
  const [outcome, setOutcome] = useState<SourceVoucherOutcome | null>(held === null ? null : { type: 'unknown' })
  const [sending, setSending] = useState(false); const [invalid, setInvalid] = useState(false); const [reload, setReload] = useState(0)
  const inFlight = useRef(false); const mounted = useRef(true)
  const canPropose = hasCapability(shell, ACCOUNTING_CAPABILITY.journalReversalPropose)
  const frozen = sending || outcome?.type === 'unknown'
  useEffect(() => { onFrozenChange(frozen) }, [frozen, onFrozenChange])
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; onFrozenChange(false) } }, [onFrozenChange])
  const send = async (next: SourceVoucherReversalAttempt, retry = false) => {
    if (!canPropose || inFlight.current || outcome?.type === 'unknown' && !retry) return
    inFlight.current = true; pending.set(slot, next); setAttempt(next); setSending(true); setOutcome(null)
    try {
      const security = boundary.capture(); const answer = await proposeSourceVoucherReversal(row.companyId, next)
      if (answer.type === 'refused' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, security)
      // Loss of authority on retry cannot prove whether the preceding request committed.
      const resolved = retry && answer.type === 'refused' && [401, 403].includes(answer.status) ? { type: 'unknown' as const } : answer
      if (resolved.type !== 'unknown') pending.delete(slot)
      if (mounted.current) {
        setOutcome(resolved)
        if (resolved.type === 'applied' || resolved.type === 'refused' && resolved.status === 409) onChanged()
        if (resolved.type === 'refused' && resolved.code === 'source_voucher_reversal_period_not_open') {
          setForm(current => ({ ...current, periodId: '' })); setReload(value => value + 1)
        }
      }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (frozen || inFlight.current || !canPropose) return
    if (validateProposalForm(form) !== null) { setInvalid(true); return }
    setInvalid(false)
    void send({ id: row.id, key: crypto.randomUUID(), request: { expectedVersion: row.version, proposalId: crypto.randomUUID(),
      periodId: form.periodId, transactionDate: form.transactionDate, accountingDate: form.accountingDate,
      valueDate: form.valueDate || null, reason: form.reason.trim() } })
  }
  const proposal = row.reversalProposal ?? (outcome?.type === 'applied' ? outcome.value.reversalProposal : null)
  return <section aria-labelledby="source-reversal-title"><h3 id="source-reversal-title">{T.reversalTitle}</h3>
    <p>{T.reversal}</p>
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button disabled={sending || attempt === null || !canPropose}
      onClick={() => attempt !== null && void send(attempt, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{outcome.code === 'source_voucher_reversal_period_not_open' ? T.reversalPeriodRefused : T.refused} ({outcome.status})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.reversalDone}</p> : null}
    {proposal != null && proposal.status !== 'Rejected' ? <>
      <p>{T.reversalAccountingDate}: {proposal.accountingDate}</p><p>{proposal.reason}</p>
      {proposal.proposedBy.toLowerCase() === actor.toLowerCase() && proposal.status === 'Pending' ? <p>{T.reversalOwn}</p> : null}
      {row.sourceActorId.toLowerCase() === actor.toLowerCase() && proposal.status === 'Pending' ? <p>{T.own}</p> : null}
      {hasCapability(shell, ACCOUNTING_CAPABILITY.journalReversalApprove) && proposal.proposedBy.toLowerCase() !== actor.toLowerCase()
        && row.sourceActorId.toLowerCase() !== actor.toLowerCase()
        ? <Link to={journalReversalApprovalPath(proposal.proposalId)}>{T.reversalApproval}</Link> : null}
    </> : row.cancellationId == null ? <p role="note">{T.reversalMissingProof}</p>
      : !canPropose ? <p role="note">{T.reversalDenied}</p> : <form onSubmit={submit} className={styles.form}>
        <label>{T.reversalTransactionDate}<input type="date" value={form.transactionDate} disabled={frozen}
          onChange={event => setForm(current => ({ ...current, transactionDate: event.target.value }))} /></label>
        <label>{T.reversalAccountingDate}<input type="date" value={form.accountingDate} disabled={frozen}
          onChange={event => setForm(current => ({ ...current, accountingDate: event.target.value, periodId: '' }))} /></label>
        <NormalJournalApprovalPeriod key={reload} namespace={namespace} companyId={row.companyId} accountingDate={form.accountingDate}
          value={form.periodId} onChange={periodId => setForm(current => current.periodId === periodId ? current : ({ ...current, periodId }))}
          disabled={frozen} reloadToken={reload} />
        <label>{T.reversalValueDate}<input type="date" value={form.valueDate} disabled={frozen}
          onChange={event => setForm(current => ({ ...current, valueDate: event.target.value }))} /></label>
        <label>{T.reversalReason}<textarea value={form.reason} maxLength={1024} disabled={frozen}
          onChange={event => setForm(current => ({ ...current, reason: event.target.value }))} /></label>
        {invalid ? <p role="alert">{T.reversalInvalid}</p> : null}
        <button disabled={frozen} type="submit">{T.reversalPropose}</button>
      </form>}
  </section>
}
