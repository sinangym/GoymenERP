/**
 * The status action inside C-03's account section: "Bu şirkette pasife al" /
 * "Bu şirkette aktifleştir", the modal, and the ONE logical attempt behind them.
 *
 * Offered only where the server granted the status control (`CARI_CONTROL_CAPABILITY`),
 * which today it never does — so the button is not rendered and no PUT exists. The API
 * re-authorizes `counterparty.accounts.status.update` on every request regardless; the
 * gate decides what is offered, never what is allowed.
 *
 * What the attempt guarantees, and why it lives HERE rather than in the modal:
 *
 *  - ONE key per logical attempt, minted at submit for a frozen payload
 *    `{ expectedVersion, isActive, reason }`. An unknown outcome (timeout, 5xx, silent
 *    server) keeps that key; the only ways forward are the same attempt under the same
 *    key after a fresh acknowledged read, or an explicit abandon. Closing the window
 *    is neither — the notice stays in the section, so the recovery stays reachable.
 *  - A NEW intent (activate after a deactivate, or a different reason) is a new
 *    payload and therefore a new key. An unchanged payload after an abandon is refused
 *    as the same unresolved attempt, not re-sent under a fresh key.
 *  - The fence is the ACCOUNT version the user was SHOWN when the window opened. A read
 *    that lands underneath the open window and moves the version blocks the send; the
 *    user reopens on what the record now is. A stale fence the server sees anyway comes
 *    back as `version_conflict` and is never retried under the same key.
 *  - A 204 says applied and nothing else. The owner is told, drops the cached reads and
 *    waits for the fresh authorized GET; the state on screen is that read's, never the
 *    request's. A same-state command with a new key is the server's `NoChange` audit —
 *    also a 204, also followed by the read, and the read is what shows nothing moved.
 *  - The dirty scope is registered while a reason is typed or a write may be in flight,
 *    so a company switch asks first; the switch then remounts the card and the attempt
 *    hook aborts and drops everything bound to the former context.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useShellDispatch } from '../shell/shellContext'
import { setCompanyAccountActiveState, type CommandOutcome } from './commandApi'
import { API_BASE, contractReadSeq, isCommandContractReady } from './clientContract'
import { useCommandAttempt } from './useCommandAttempt'
import { repeatsUnresolvedAttempt } from './commandAttempt'
import { CommandAttemptNotice } from './CommandAttemptNotice'
import { AccountStatusDialog, type CloseConfirm } from './AccountStatusDialog'
import {
  buildAccountStatusPayload,
  intentFor,
  reasonProblem,
  targetIsActive,
  type AccountStatusIntent,
} from './accountStatusContract'
import { CARI_ACCOUNT_STATUS_TEXT as TEXT } from './cariCommandText'
import type { CounterpartyDetail } from './detailContract'
import styles from './CariDetailPage.module.css'

export const CARI_ACCOUNT_STATUS_DIRTY_SCOPE = 'cari.account-status'

/** The frozen payload of one attempt — the three things the server's fingerprint sees. */
export interface AccountStatusAttemptPayload {
  readonly expectedVersion: string
  readonly isActive: boolean
  readonly reason: string
}

export interface AccountStatusApplied {
  readonly intent: AccountStatusIntent
  readonly replayed: boolean
}

interface OpenDialog {
  readonly intent: AccountStatusIntent
  /** The account as it was SHOWN when the window opened — the fence and the label. */
  readonly openedOnVersion: string
  readonly openedOnIsActive: boolean
}

export function AccountStatusControl({
  partyId,
  detail,
  companyLabel,
  companyId,
  namespace,
  refetch,
  onApplied,
  onBeginNewAttempt,
  disabled = false,
}: {
  readonly partyId: string
  readonly detail: CounterpartyDetail
  readonly companyLabel: string
  readonly companyId: string
  readonly namespace: string
  /** The owner's fresh authorized detail read in THIS context. */
  readonly refetch: () => Promise<unknown>
  /** A definite 204 landed; the owner owes a fresh read and shows its result. */
  readonly onApplied: (applied: AccountStatusApplied) => void
  /** A new window opened; the owner clears any earlier "saved" notice. */
  readonly onBeginNewAttempt: () => void
  /** The owner is still adopting a fresh read after a success. */
  readonly disabled?: boolean
}) {
  const dispatch = useShellDispatch()
  const attempt = useCommandAttempt<AccountStatusAttemptPayload, null>()
  const [dialog, setDialog] = useState<OpenDialog | null>(null)
  const [reason, setReason] = useState('')
  const [showErrors, setShowErrors] = useState(false)
  const [closeConfirm, setCloseConfirm] = useState<CloseConfirm>(null)
  const [summaryFocusRequest, setSummaryFocusRequest] = useState(0)
  const triggerRef = useRef<HTMLButtonElement | null>(null)
  const summaryRef = useRef<HTMLParagraphElement | null>(null)
  const unresolvedRef = useRef<HTMLDivElement | null>(null)
  /** Whether the last DEFINITE answer was a replay — read off the transport, kept here. */
  const [lastReplayed, setLastReplayed] = useState(false)

  const operation = `counterparty.account-status:${partyId}`
  const submitting = attempt.state.phase === 'submitting'
  const unknown = attempt.state.phase === 'unknown'
  const frozen = submitting || unknown
  const completed = attempt.state.phase === 'completed' ? attempt.state : null
  const failed = attempt.state.phase === 'failed' ? attempt.state : null

  const currentIntent = intentFor(detail.account.isActive)
  const changedSinceOpen = dialog !== null && detail.account.version !== dialog.openedOnVersion
  const problem = reasonProblem(reason)

  /**
   * The abandoned-but-unresolved attempt whose payload the window holds again. Without
   * this the recovery disappears: the unchanged payload is refused as `unchanged` with
   * nothing on screen to say why, and the same-key retry is unreachable.
   */
  const draftPayload: AccountStatusAttemptPayload | null =
    dialog === null || problem !== null
      ? null
      : {
          expectedVersion: dialog.openedOnVersion,
          isActive: targetIsActive(dialog.intent),
          reason: buildAccountStatusPayload(dialog.intent, reason)?.reason ?? '',
        }
  const repeatsUnresolved =
    draftPayload !== null && repeatsUnresolvedAttempt(attempt.state, operation, draftPayload)

  // Unsaved work is unsaved work, valid or not; a frozen attempt counts too.
  const dirty = frozen || (dialog !== null && reason.trim().length > 0)
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: CARI_ACCOUNT_STATUS_DIRTY_SCOPE })
  }, [dirty, dispatch])
  useEffect(
    () => () => dispatch({ type: 'DIRTY_CLEAR', scope: CARI_ACCOUNT_STATUS_DIRTY_SCOPE }),
    [dispatch],
  )

  /**
   * A completion is applied once per snapshot, replay included. The window closes, the
   * reason is released, the owner is told — and the attempt is CLOSED here, because a
   * completed attempt can begin no new one and the next intent must be a new key.
   */
  const completedKeyRef = useRef<string | null>(null)
  useEffect(() => {
    if (completed === null || completedKeyRef.current === completed.snapshot.key) return
    completedKeyRef.current = completed.snapshot.key
    const intent: AccountStatusIntent = completed.snapshot.payload.isActive ? 'activate' : 'deactivate'
    // oxlint-disable-next-line react/set-state-in-effect -- reconciles a definite server outcome, once per snapshot
    setDialog(null)
    setReason('')
    setShowErrors(false)
    setCloseConfirm(null)
    onApplied({ intent, replayed: completed.replayed })
    attempt.abandon()
    triggerRef.current?.focus()
  }, [completed, onApplied, attempt])

  useEffect(() => {
    if (summaryFocusRequest === 0) return
    summaryRef.current?.focus()
  }, [summaryFocusRequest])

  const open = () => {
    if (disabled || frozen || attempt.revalidating) return
    // A definite failure is acknowledged by starting over — the same rule the editors
    // apply to "edits a field".
    if (failed !== null) attempt.abandon()
    onBeginNewAttempt()
    setLastReplayed(false)
    setReason('')
    setShowErrors(false)
    setCloseConfirm(null)
    setDialog({
      intent: currentIntent,
      openedOnVersion: detail.account.version,
      openedOnIsActive: detail.account.isActive,
    })
  }

  const closeNow = () => {
    setDialog(null)
    setReason('')
    setShowErrors(false)
    setCloseConfirm(null)
    triggerRef.current?.focus()
  }

  const requestClose = () => {
    if (submitting) return
    if (unknown) {
      setCloseConfirm('unresolved')
      return
    }
    if (reason.trim().length > 0) {
      setCloseConfirm('unsaved-reason')
      return
    }
    closeNow()
  }

  const submit = () => {
    if (dialog === null || frozen || attempt.revalidating || changedSinceOpen) return
    if (problem !== null) {
      // Client-invalid: nothing is sent and NO key is minted.
      setShowErrors(true)
      setSummaryFocusRequest((count) => count + 1)
      return
    }
    const payload = buildAccountStatusPayload(dialog.intent, reason)
    if (payload === null) return
    setShowErrors(false)
    const expectedVersion = dialog.openedOnVersion
    const target = partyId
    const send = (idempotencyKey: string, signal: AbortSignal): Promise<CommandOutcome<null>> =>
      setCompanyAccountActiveState(
        { companyId, idempotencyKey, contract: { origin: API_BASE, namespace } },
        target,
        expectedVersion,
        payload,
        signal,
      ).then((outcome) => {
        if (outcome.type === 'http') setLastReplayed(outcome.replayed ?? false)
        return outcome
      })
    const result = attempt.submit(
      operation,
      { expectedVersion, isActive: payload.isActive, reason: payload.reason },
      send,
    )
    if (result === 'unchanged') unresolvedRef.current?.focus()
  }

  /**
   * The same-key retry's precondition: a fresh, authorized, acknowledged read in THIS
   * context — not the grant the first send rode on. A refetch can settle from cache and
   * still resolve, so the recorded-read counter has to have MOVED.
   */
  const revalidate = useCallback(async () => {
    const before = contractReadSeq()
    await refetch()
    return contractReadSeq() > before && isCommandContractReady({ origin: API_BASE, namespace })
  }, [refetch, namespace])

  const refreshRecord = () => {
    attempt.abandon()
    closeNow()
    void refetch()
  }

  const notice = (
    <CommandAttemptNotice
      state={attempt.state}
      repeatsUnresolved={repeatsUnresolved}
      onRetry={() => attempt.retry(revalidate)}
      onAbandon={attempt.abandon}
      onAcknowledge={attempt.abandon}
      unresolvedRef={unresolvedRef}
      messages={TEXT.failure}
      extra={
        failed !== null && failed.failure === 'version-conflict' ? (
          <div>
            {lastReplayed ? <p data-state="replayed-conflict">{TEXT.replayedConflict}</p> : null}
            <p>
              <button type="button" className={styles.action} onClick={refreshRecord}>
                {TEXT.refreshRecord}
              </button>
            </p>
          </div>
        ) : undefined
      }
    />
  )

  const targetLabel = `${detail.code} — ${detail.legalName}`
  const blocked = disabled || frozen || attempt.revalidating

  return (
    <div data-control="account-status">
      <button
        ref={triggerRef}
        type="button"
        className={styles.action}
        onClick={open}
        disabled={blocked}
        aria-describedby={unknown ? 'cari-account-status-blocked' : undefined}
        data-intent={currentIntent}
      >
        {TEXT.action[currentIntent]}
      </button>
      {unknown && dialog === null ? (
        <p id="cari-account-status-blocked" className={styles.blockNote}>
          {TEXT.actionBlockedUnresolved}
        </p>
      ) : null}

      {dialog === null ? notice : null}

      {dialog !== null ? (
        <AccountStatusDialog
          intent={dialog.intent}
          targetLabel={targetLabel}
          companyLabel={companyLabel}
          currentIsActive={dialog.openedOnIsActive}
          reason={reason}
          onReasonChange={(value) => {
            setReason(value)
            if (showErrors && reasonProblem(value) === null) setShowErrors(false)
          }}
          showErrors={showErrors}
          problem={problem}
          busy={submitting || attempt.revalidating}
          locked={unknown}
          changedSinceOpen={changedSinceOpen}
          onSubmit={submit}
          onRequestClose={requestClose}
          closeConfirm={closeConfirm}
          onKeepOpen={() => setCloseConfirm(null)}
          onConfirmClose={closeNow}
          notice={notice}
          summaryRef={summaryRef}
        />
      ) : null}
    </div>
  )
}
