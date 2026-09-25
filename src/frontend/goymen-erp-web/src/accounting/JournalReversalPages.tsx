/**
 * ACC-V1 journal reversal screens (Business #66/5827281417).
 *
 *  - The PROPOSE control lives on the M-04 journal detail: it stores a pending proposal only. The original journal
 *    is unchanged and no reversal journal exists until another person approves.
 *  - The APPROVAL screen reopens one proposal by id. Maker ≠ checker is enforced twice: the screen never offers the
 *    approve action to the proposer (the proposal's `proposedBy` is the signed-in user), and the server refuses a
 *    self-approval with 403 whatever grants the proposer holds.
 *
 * Command discipline (ADR-011, the Accounting/Finance write-page pattern): one frozen snapshot per attempt with its
 * own proposal id and `Idempotency-Key`; while it is in flight every input and the action are frozen; an UNKNOWN
 * outcome offers only "send the same snapshot again"; a definite refusal unfreezes the form and a new attempt mints
 * a new key. Nothing is retried automatically and nothing is guessed from a partial answer.
 */

import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import {
  ACCOUNTING_CAPABILITY,
  ACCOUNTING_ROUTE,
  journalDetailPath,
  journalReversalApprovalPath,
} from './capabilities'
import type { JournalSummary } from './contract'
import {
  JOURNAL_REVERSAL_REASON_MAX,
  isReversalGuid,
  proposalRequest,
  sameGuid,
  validateProposalForm,
  type JournalReversalProposal,
  type ProposalForm,
  type ProposalFormIssue,
} from './journalReversalContract'
import {
  fetchJournalReversalProposal,
  sendJournalReversalCommand,
  type JournalReversalOutcome,
  type JournalReversalSnapshot,
} from './journalReversalApi'
import {
  ALL_JOURNALS,
  RESEND,
  REVERSAL_ACCOUNTING_DATE,
  REVERSAL_APPROVAL_TITLE,
  REVERSAL_APPROVE,
  REVERSAL_APPROVED,
  REVERSAL_FORM_ISSUE,
  REVERSAL_INTRO,
  REVERSAL_OPEN_APPROVAL,
  REVERSAL_OPEN_REVERSAL,
  REVERSAL_OPEN_SOURCE,
  REVERSAL_OWN_PROPOSAL,
  REVERSAL_PERIOD,
  REVERSAL_PROPOSED,
  REVERSAL_REASON,
  REVERSAL_REREAD,
  REVERSAL_REPLAYED,
  REVERSAL_STATUS_LABEL,
  REVERSAL_SUBMIT,
  REVERSAL_TITLE,
  REVERSAL_TRANSACTION_DATE,
  REVERSAL_VALUE_DATE,
  UNKNOWN_ACTION_HELP,
  UNKNOWN_TITLE,
  reversalFailureText,
  shortRef,
} from './accountingText'
import styles from './Accounting.module.css'

const newId = () => crypto.randomUUID()

function useMounted() {
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])
  return mounted
}

/**
 * Sends one frozen snapshot at a time. `retry` re-sends the SAME snapshot (same key, same body) after an unknown
 * outcome; a fresh attempt is only possible once the previous outcome is definite.
 */
function useReversalCommand(companyId: string | null) {
  const boundary = useAuthBoundary()
  const mounted = useMounted()
  const inFlight = useRef(false)
  const [snapshot, setSnapshot] = useState<JournalReversalSnapshot | null>(null)
  const [outcome, setOutcome] = useState<JournalReversalOutcome | null>(null)
  const sending = snapshot !== null && outcome === null
  const unresolved = outcome?.type === 'unknown'

  const send = async (next: JournalReversalSnapshot, retry = false) => {
    if (companyId === null || inFlight.current) return
    if (outcome?.type === 'unknown' && !retry) return
    inFlight.current = true
    setSnapshot(next)
    setOutcome(null)
    try {
      const security = boundary.capture()
      const answer = await sendJournalReversalCommand(companyId, next)
      if (answer.type === 'failed') {
        if (answer.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (answer.failure.kind === 'forbidden') boundary.report(403, security)
      }
      if (mounted.current) setOutcome(answer)
    } finally {
      inFlight.current = false
    }
  }
  const reset = () => {
    if (inFlight.current) return
    setSnapshot(null)
    setOutcome(null)
  }
  return { snapshot, outcome, sending, unresolved, send, reset, inFlight }
}

function Facts({ proposal }: { readonly proposal: JournalReversalProposal }) {
  return (
    <dl className={styles.form} data-proposal={proposal.proposalId} data-status={proposal.status}>
      <div className={styles.control}>
        <dt>Durum</dt>
        <dd>{REVERSAL_STATUS_LABEL[proposal.status]}</dd>
      </div>
      <div className={styles.control}>
        <dt>Sürüm</dt>
        <dd>{proposal.version}</dd>
      </div>
      <div className={styles.control}>
        <dt>Öneri</dt>
        <dd className={styles.mono} title={proposal.proposalId}>
          {shortRef(proposal.proposalId)}
        </dd>
      </div>
      <div className={styles.control}>
        <dt>Muhasebe tarihi</dt>
        <dd>{proposal.accountingDate}</dd>
      </div>
      <div className={styles.control}>
        <dt>İşlem tarihi</dt>
        <dd>{proposal.transactionDate}</dd>
      </div>
      <div className={styles.control}>
        <dt>Valör</dt>
        <dd>{proposal.valueDate ?? '—'}</dd>
      </div>
      <div className={styles.control}>
        <dt>{REVERSAL_REASON}</dt>
        <dd>{proposal.reason}</dd>
      </div>
      <div className={styles.control}>
        <dt>Öneren</dt>
        <dd className={styles.mono} title={proposal.proposedBy}>
          {shortRef(proposal.proposedBy)}
        </dd>
      </div>
      {proposal.decidedBy !== null ? (
        <div className={styles.control}>
          <dt>Onaylayan</dt>
          <dd className={styles.mono} title={proposal.decidedBy}>
            {shortRef(proposal.decidedBy)}
          </dd>
        </div>
      ) : null}
    </dl>
  )
}

function UnknownNotice({ onResend, disabled }: { readonly onResend: () => void; readonly disabled: boolean }) {
  return (
    <div role="alert" className={styles.noticeWarn} data-outcome="unknown">
      <strong>{UNKNOWN_TITLE}</strong>
      <p>{UNKNOWN_ACTION_HELP}</p>
      <button type="button" disabled={disabled} onClick={onResend}>
        {RESEND}
      </button>
    </div>
  )
}

// ---------------------------------------------------------------------------------
// Propose (a control of the journal detail)
// ---------------------------------------------------------------------------------

export function JournalReversalProposePanel({ journal }: { readonly journal: JournalSummary }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const command = useReversalCommand(companyId)
  const [form, setForm] = useState<ProposalForm>({
    periodId: journal.periodId,
    transactionDate: '',
    accountingDate: '',
    valueDate: '',
    reason: '',
  })
  const [issue, setIssue] = useState<ProposalFormIssue | null>(null)
  const frozen = command.sending || command.unresolved

  // A reversal journal is never reversed again (the server refuses it too); the control is not offered there.
  if (journal.kind === 'Reversal') return null

  const outcome = command.outcome
  if (outcome?.type === 'applied') {
    return (
      <section aria-labelledby="journal-reversal-title" className={styles.page} data-reversal="proposed">
        <h2 id="journal-reversal-title">{REVERSAL_TITLE}</h2>
        <p role="status" className={styles.noticeOk}>
          {outcome.replayed ? REVERSAL_REPLAYED : REVERSAL_PROPOSED}
        </p>
        <Facts proposal={outcome.proposal} />
        <div className={styles.actions}>
          {hasCapability(shell, ACCOUNTING_CAPABILITY.journalReversalApprove) ? (
            <Link to={journalReversalApprovalPath(outcome.proposal.proposalId)}>{REVERSAL_OPEN_APPROVAL}</Link>
          ) : null}
        </div>
      </section>
    )
  }

  const update = (key: keyof ProposalForm) => (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm((current) => ({ ...current, [key]: event.target.value }))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (frozen || command.inFlight.current) return
    const found = validateProposalForm(form)
    setIssue(found)
    if (found !== null) return
    void command.send({
      operation: 'propose',
      idempotencyKey: newId(),
      request: proposalRequest(newId(), journal.journalId, form),
    })
  }

  return (
    <section aria-labelledby="journal-reversal-title" className={styles.page} data-reversal="form">
      <h2 id="journal-reversal-title">{REVERSAL_TITLE}</h2>
      <p className={styles.help}>{REVERSAL_INTRO}</p>
      {outcome?.type === 'unknown' && command.snapshot !== null ? (
        <UnknownNotice disabled={command.sending} onResend={() => void command.send(command.snapshot!, true)} />
      ) : null}
      {outcome?.type === 'failed' ? (
        <p role="alert" className={styles.error} data-failure={outcome.failure.kind}>
          {reversalFailureText(outcome.failure)}
        </p>
      ) : null}
      <form noValidate onSubmit={submit}>
        <div className={styles.form}>
          <label className={styles.control}>
            {REVERSAL_PERIOD}
            <input value={form.periodId} disabled={frozen} onChange={update('periodId')} />
          </label>
          <label className={styles.control}>
            {REVERSAL_TRANSACTION_DATE}
            <input type="date" value={form.transactionDate} disabled={frozen} onChange={update('transactionDate')} />
          </label>
          <label className={styles.control}>
            {REVERSAL_ACCOUNTING_DATE}
            <input type="date" value={form.accountingDate} disabled={frozen} onChange={update('accountingDate')} />
          </label>
          <label className={styles.control}>
            {REVERSAL_VALUE_DATE}
            <input type="date" value={form.valueDate} disabled={frozen} onChange={update('valueDate')} />
          </label>
          <label className={styles.control}>
            {REVERSAL_REASON}
            <textarea
              value={form.reason}
              maxLength={JOURNAL_REVERSAL_REASON_MAX}
              disabled={frozen}
              onChange={update('reason')}
            />
          </label>
        </div>
        {issue !== null ? (
          <p role="alert" className={styles.error} data-issue={issue}>
            {REVERSAL_FORM_ISSUE[issue]}
          </p>
        ) : null}
        <div className={styles.actions}>
          <button type="submit" disabled={frozen}>
            {REVERSAL_SUBMIT}
          </button>
        </div>
      </form>
    </section>
  )
}

// ---------------------------------------------------------------------------------
// Approval (its own route; reopens a proposal by id)
// ---------------------------------------------------------------------------------

export function JournalReversalApprovalPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { proposalId = '' } = useParams()
  return (
    <Approval
      key={`${namespace ?? 'no-context'}:${shell.routeEntryGeneration}:${proposalId}`}
      namespace={namespace}
      proposalId={proposalId}
    />
  )
}

function Approval({ namespace, proposalId }: { readonly namespace: string | null; readonly proposalId: string }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const actorId = shell.activeContext?.userId ?? null
  const single = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const command = useReversalCommand(companyId)
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting-journal-reversal', proposalId],
    enabled: namespace !== null && single && isReversalGuid(proposalId),
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const outcome = await fetchJournalReversalProposal(companyId ?? '', proposalId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, captured)
      }
      return outcome
    },
  })

  if (!single) {
    return (
      <ApprovalFrame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="ters kayıt önerisi için tek şirket bağlamı" />
      </ApprovalFrame>
    )
  }
  if (!isReversalGuid(proposalId)) {
    return (
      <ApprovalFrame>
        <StateBlock state={{ kind: 'not-found' }} subject="ters kayıt önerisi" />
      </ApprovalFrame>
    )
  }

  const outcome = command.outcome
  const remote = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<JournalReversalProposal>
  if (remote.kind !== 'ready' && outcome?.type !== 'applied') {
    return (
      <ApprovalFrame>
        <StateBlock state={remote} subject="ters kayıt önerisi" onRetry={() => void query.refetch()} />
      </ApprovalFrame>
    )
  }

  const proposal =
    outcome?.type === 'applied' ? outcome.proposal : (remote as { kind: 'ready'; data: JournalReversalProposal }).data
  // Maker ≠ checker in the UI: the proposer never sees the approve action. The server enforces it regardless.
  const ownProposal = sameGuid(proposal.proposedBy, actorId)
  const canApprove =
    hasCapability(shell, ACCOUNTING_CAPABILITY.journalReversalApprove) && proposal.status === 'Pending' && !ownProposal
  const canOpenJournal = hasCapability(shell, ACCOUNTING_CAPABILITY.journalsDetail)

  const approve = () => {
    if (!canApprove || command.sending || command.unresolved || command.inFlight.current) return
    void command.send({
      operation: 'approve',
      idempotencyKey: newId(),
      proposalId: proposal.proposalId,
      expectedVersion: proposal.version,
    })
  }

  return (
    <ApprovalFrame>
      {outcome?.type === 'applied' ? (
        <p role="status" className={styles.noticeOk}>
          {outcome.replayed ? REVERSAL_REPLAYED : REVERSAL_APPROVED}
        </p>
      ) : null}
      <Facts proposal={proposal} />
      {ownProposal && proposal.status === 'Pending' ? (
        <p className={styles.help} data-maker-checker="own-proposal">
          {REVERSAL_OWN_PROPOSAL}
        </p>
      ) : null}
      {outcome?.type === 'unknown' && command.snapshot !== null ? (
        <UnknownNotice disabled={command.sending} onResend={() => void command.send(command.snapshot!, true)} />
      ) : null}
      {outcome?.type === 'failed' ? (
        <p role="alert" className={styles.error} data-failure={outcome.failure.kind}>
          {reversalFailureText(outcome.failure)}
        </p>
      ) : null}
      <div className={styles.actions}>
        {canApprove && outcome?.type !== 'applied' ? (
          <button type="button" disabled={command.sending || command.unresolved} onClick={approve}>
            {REVERSAL_APPROVE}
          </button>
        ) : null}
        {outcome?.type === 'failed' && (outcome.failure.kind === 'version-conflict' || outcome.failure.kind === 'not-pending') ? (
          <button
            type="button"
            onClick={() => {
              command.reset()
              void query.refetch()
            }}
          >
            {REVERSAL_REREAD}
          </button>
        ) : null}
        {canOpenJournal ? <Link to={journalDetailPath(proposal.sourceJournalId)}>{REVERSAL_OPEN_SOURCE}</Link> : null}
        {canOpenJournal && proposal.reversalJournalId !== null ? (
          <Link to={journalDetailPath(proposal.reversalJournalId)}>{REVERSAL_OPEN_REVERSAL}</Link>
        ) : null}
        <Link to={ACCOUNTING_ROUTE.journalsList}>{ALL_JOURNALS}</Link>
      </div>
    </ApprovalFrame>
  )
}

function ApprovalFrame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="journal-reversal-approval-title" className={styles.page}>
      <h1 id="journal-reversal-approval-title">{REVERSAL_APPROVAL_TITLE}</h1>
      {children}
    </section>
  )
}
