import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellDispatch } from '../shell/shellContext'
import { decodeReversalProposalPage, type ReversalProposalView } from './contract'
import {
  useFinanceBinding,
  useFinanceRead,
  useReversalCommand,
  type ReversalCommand,
} from './financePort'
import { ABANDON, REFUSAL_TITLE, RESEND_SAME, issueText } from './financeText'
import { receiptDetailPath } from './capabilities'
import styles from './Finance.module.css'

const SUBJECT = 'ters kayıt önerileri'

export function ReversalApprovalsPage() {
  const pending = useFinanceRead(
    'reversal-proposals-pending',
    [],
    (port, scope, signal) => port.pendingReversals(scope, signal),
    (resource) => decodeReversalProposalPage(resource),
  )

  if (pending.state.kind !== 'ready') {
    return <StateBlock state={pending.state} subject={SUBJECT} onRetry={pending.refetch} />
  }

  return (
    <section aria-labelledby="finance-reversal-approvals-title">
      <h1 id="finance-reversal-approvals-title">Ters kayıt onayları</h1>
      <p className={styles.notice}>
        Öneriyi hazırlayan kişi karar veremez. Onay, orijinal kaydı değiştirmez; ayrı ve bağlantılı bir ters kayıt oluşturur.
      </p>
      {pending.state.data.items.length === 0 ? (
        <p role="status">Onay bekleyen ters kayıt önerisi yok.</p>
      ) : (
        <div className={styles.stack}>
          {pending.state.data.items.map((proposal) => (
            <ReversalProposalDecision key={proposal.id} proposal={proposal} onSettled={pending.refetch} />
          ))}
        </div>
      )}
    </section>
  )
}

function ReversalProposalDecision({
  proposal,
  onSettled,
}: {
  readonly proposal: ReversalProposalView
  readonly onSettled: () => void
}) {
  const binding = useFinanceBinding()
  const approve = useReversalCommand('approve', proposal.id)
  const reject = useReversalCommand('reject', proposal.id)
  const [reason, setReason] = useState('')
  const ownProposal = binding?.binding.actorId === proposal.proposedBy
  const frozen = frozenCommand(approve) || frozenCommand(reject)
  const dirty = reason.trim().length > 0 && !ownProposal
  useDecisionDirty(`finance.reversal.${proposal.id}`, dirty)

  useEffect(() => {
    if (approve.state.phase === 'succeeded' || reject.state.phase === 'succeeded') onSettled()
  }, [approve.state.phase, reject.state.phase, onSettled])

  const result = approve.state.phase === 'succeeded' ? approve.state.resource
    : reject.state.phase === 'succeeded' ? reject.state.resource : null

  return (
    <article className={styles.command} aria-label={`Ters kayıt önerisi ${proposal.id}`}>
      <h2>İşlem tarihi: {proposal.occurredOn}</h2>
      <p>{proposal.reason}</p>
      <p>
        <Link to={receiptDetailPath(proposal.originalSourceId)}>Orijinal kaydı aç</Link>
      </p>
      {ownProposal ? (
        <p role="status">Bu öneriyi siz hazırladınız; kararı başka bir yetkili vermeli.</p>
      ) : null}
      <DecisionState command={approve} />
      <DecisionState command={reject} />
      {result === null ? null : (
        <p role="status">
          {result.status === 'Approved' && result.reversalSourceId !== null ? (
            <Link to={receiptDetailPath(result.reversalSourceId)}>Oluşturulan ters kaydı aç</Link>
          ) : 'Öneri reddedildi; para olayı oluşturulmadı.'}
        </p>
      )}
      {result !== null || ownProposal ? null : (
        <div className={styles.form}>
          <div className={styles.actions}>
            <button
              type="button"
              disabled={frozen || !approve.available}
              onClick={() => approve.submit({
                operation: 'approve', body: { proposalId: proposal.id, expectedVersion: proposal.version },
              })}
            >
              Onayla ve ters kaydı oluştur
            </button>
          </div>
          <label htmlFor={`finance-reversal-reject-${proposal.id}`}>Ret gerekçesi</label>
          <textarea
            id={`finance-reversal-reject-${proposal.id}`}
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
            Reddet
          </button>
        </div>
      )}
    </article>
  )
}

function frozenCommand(command: ReversalCommand): boolean {
  return command.state.phase === 'submitting' || command.state.phase === 'unknown'
    || command.state.phase === 'conflicted'
}

function DecisionState({ command }: { readonly command: ReversalCommand }) {
  const state = command.state
  if (state.phase === 'idle' || state.phase === 'succeeded') return null
  if (state.phase === 'submitting') return <p role="status">Karar gönderiliyor…</p>
  if (state.phase === 'unknown') {
    return (
      <div className={styles.attempt} role="status">
        <p>Kararın sonucu bilinmiyor; yeni karar üretmeden aynı anahtarla yeniden gönderebilirsiniz.</p>
        <button type="button" onClick={command.resend}>{RESEND_SAME}</button>
        <button type="button" onClick={command.abandon}>{ABANDON}</button>
      </div>
    )
  }
  if (state.phase === 'conflicted') {
    return (
      <div className={styles.attempt} role="status">
        <p>İşlem anahtarı farklı içerikle çakıştı; güncel öneriyi yeniden okuyun.</p>
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

function useDecisionDirty(scope: string, dirty: boolean) {
  const dispatch = useShellDispatch()
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope })
  }, [dirty, dispatch, scope])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope }), [dispatch, scope])
}
