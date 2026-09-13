/**
 * The status action inside C-03's account section: "Bu şirkette pasife al" /
 * "Bu şirkette aktifleştir", the modal, and the attempt notice — RENDERED from the
 * flow the card owns (`useAccountStatusFlow`), never owning any of it.
 *
 * Offered only where the server granted the status control (`CARI_CONTROL_CAPABILITY`),
 * which today it never does — so the button is not rendered and no PUT exists. The API
 * re-authorizes `counterparty.accounts.status.update` on every request regardless; the
 * gate decides what is offered, never what is allowed.
 *
 * Two render sites share the flow: this control in the ready branch, and
 * `AccountStatusRecoveryNotice` in every other branch — so an unresolved attempt's
 * same-key retry and its abandon stay reachable while the record cannot be read.
 */

import type { RefObject } from 'react'
import { CommandAttemptNotice } from './CommandAttemptNotice'
import { AccountStatusDialog } from './AccountStatusDialog'
import type { AccountStatusFlow } from './useAccountStatusFlow'
import { CARI_ACCOUNT_STATUS_TEXT as TEXT } from './cariCommandText'
import type { CounterpartyDetail } from './detailContract'
import styles from './CariDetailPage.module.css'

/** The attempt's notice: unknown (same-key retry / abandon), abandoned, or a definite failure. */
export function AccountStatusRecoveryNotice({
  flow,
  unresolvedRef,
}: {
  readonly flow: AccountStatusFlow
  readonly unresolvedRef: RefObject<HTMLDivElement | null>
}) {
  const failed = flow.attempt.state.phase === 'failed' ? flow.attempt.state : null
  return (
    <CommandAttemptNotice
      state={flow.attempt.state}
      repeatsUnresolved={flow.repeatsUnresolved}
      onRetry={flow.retry}
      onAbandon={flow.abandon}
      onAcknowledge={flow.abandon}
      unresolvedRef={unresolvedRef}
      messages={TEXT.failure}
      extra={
        failed !== null && failed.failure === 'version-conflict' ? (
          <div>
            {flow.lastReplayed ? <p data-state="replayed-conflict">{TEXT.replayedConflict}</p> : null}
            <p>
              <button type="button" className={styles.action} onClick={flow.refreshRecord}>
                {TEXT.refreshRecord}
              </button>
            </p>
          </div>
        ) : undefined
      }
    />
  )
}

export function AccountStatusControl({
  flow,
  detail,
  companyLabel,
  onReadAgain,
  triggerRef,
  summaryRef,
  unresolvedRef,
}: {
  readonly flow: AccountStatusFlow
  readonly detail: CounterpartyDetail
  readonly companyLabel: string
  /** The owner's fresh read, offered when the action is closed for lack of an acknowledged read. */
  readonly onReadAgain: () => void
  readonly triggerRef: RefObject<HTMLButtonElement | null>
  readonly summaryRef: RefObject<HTMLParagraphElement | null>
  readonly unresolvedRef: RefObject<HTMLDivElement | null>
}) {
  const intent = flow.currentIntent ?? 'deactivate'
  const blocked = flow.openBlocked !== null || flow.busy || flow.locked
  const blockedId = 'cari-account-status-blocked'
  const blockedText =
    flow.openBlocked === 'unresolved'
      ? TEXT.actionBlockedUnresolved
      : flow.openBlocked === 'contract-not-ready'
        ? TEXT.contractNotReady
        : null

  return (
    <div data-control="account-status">
      <button
        ref={triggerRef}
        type="button"
        className={styles.action}
        onClick={flow.open}
        disabled={blocked}
        aria-describedby={blockedText !== null ? blockedId : undefined}
        data-intent={intent}
      >
        {TEXT.action[intent]}
      </button>
      {blockedText !== null && flow.dialog === null ? (
        <div id={blockedId} data-state={`action-blocked-${flow.openBlocked}`}>
          <p className={styles.blockNote}>{blockedText}</p>
          {flow.openBlocked === 'contract-not-ready' ? (
            <p>
              <button type="button" className={styles.action} onClick={onReadAgain}>
                {TEXT.readAgain}
              </button>
            </p>
          ) : null}
        </div>
      ) : null}

      {flow.dialog === null ? <AccountStatusRecoveryNotice flow={flow} unresolvedRef={unresolvedRef} /> : null}

      {flow.dialog !== null ? (
        <AccountStatusDialog
          intent={flow.dialog.intent}
          targetLabel={`${detail.code} — ${detail.legalName}`}
          companyLabel={companyLabel}
          currentIsActive={flow.dialog.openedOnIsActive}
          reason={flow.reason}
          onReasonChange={flow.setReason}
          showErrors={flow.showErrors}
          problem={flow.problem}
          busy={flow.busy}
          locked={flow.locked}
          refreshing={flow.refreshing}
          refreshedNote={flow.refreshedNote}
          contractReady={flow.contractReady}
          changedSinceOpen={flow.changedSinceOpen}
          onAdoptFresh={flow.adoptFresh}
          onSubmit={flow.submit}
          onRequestClose={flow.requestClose}
          closeConfirm={flow.closeConfirm}
          onKeepOpen={flow.keepOpen}
          onConfirmClose={flow.confirmClose}
          notice={<AccountStatusRecoveryNotice flow={flow} unresolvedRef={unresolvedRef} />}
          summaryRef={summaryRef}
        />
      ) : null}
    </div>
  )
}
