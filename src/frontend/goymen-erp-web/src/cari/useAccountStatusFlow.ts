/**
 * The status action's state machine — the ONE logical attempt, the window, the reason
 * and the dirty scope — owned at the CARD level, not by the ready branch.
 *
 * Why here and not in the control that renders the button: the control lives only
 * while the detail read is `ready`. The same-key retry of an unknown outcome begins
 * with a fresh read, and if THAT read fails the card leaves the ready branch — which
 * unmounted the control, and with it the frozen key, body and reason of an attempt the
 * server may already have processed. The recovery was lost at exactly the moment it
 * was being used (Dirac #250 P2-1). The state now outlives the branches; the branches
 * only decide what to render from it.
 */

import { useCallback, useEffect, useRef, useState, type RefObject } from 'react'
// `useRef` is still used for the completion ledger below; the focus refs are inputs.
import { useShellDispatch } from '../shell/shellContext'
import { setCompanyAccountActiveState, type CommandOutcome } from './commandApi'
import { API_BASE, contractReadSeq, isCommandContractReady } from './clientContract'
import { useCommandAttempt, type CommandAttemptController } from './useCommandAttempt'
import { repeatsUnresolvedAttempt } from './commandAttempt'
import type { CloseConfirm } from './AccountStatusDialog'
import {
  buildAccountStatusPayload,
  intentFor,
  reasonProblem,
  targetIsActive,
  type AccountStatusIntent,
  type ReasonProblem,
} from './accountStatusContract'
import type { CounterpartyDetail } from './detailContract'

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

export interface OpenDialog {
  readonly intent: AccountStatusIntent
  /** The account as it was SHOWN when the window opened (or re-seated) — the fence. */
  readonly openedOnVersion: string
  readonly openedOnIsActive: boolean
}

export type OpenBlockedReason = 'unresolved' | 'contract-not-ready' | 'adopting' | null

export interface AccountStatusFlowInput {
  readonly partyId: string
  readonly companyId: string | null
  readonly namespace: string | null
  /** The latest READY detail, or null while the read is not ready. */
  readonly detail: CounterpartyDetail | null
  /** `dataUpdatedAt` of the detail query: moves only when a read actually lands. */
  readonly readStamp: number
  readonly refetch: () => Promise<unknown>
  /** `isCommandContractReady` for this context, computed by the owner each render. */
  readonly contractReady: boolean
  /** The owner is still adopting a fresh read after a success. */
  readonly disabled: boolean
  readonly onApplied: (applied: AccountStatusApplied) => void
  readonly onBeginNewAttempt: () => void
  /**
   * Focus targets, created by the owner and shared with the renderer. They are inputs
   * rather than outputs so the flow object carries no ref of its own — an object that
   * holds refs is treated as one by the render-purity lint, and every read of it during
   * render would then count as reading a ref.
   */
  readonly triggerRef: RefObject<HTMLButtonElement | null>
  readonly summaryRef: RefObject<HTMLParagraphElement | null>
  readonly unresolvedRef: RefObject<HTMLDivElement | null>
}

export interface AccountStatusFlow {
  readonly attempt: CommandAttemptController<AccountStatusAttemptPayload, null>
  readonly dialog: OpenDialog | null
  readonly reason: string
  readonly problem: ReasonProblem | null
  readonly showErrors: boolean
  readonly closeConfirm: CloseConfirm
  readonly changedSinceOpen: boolean
  readonly refreshing: boolean
  readonly refreshedNote: boolean
  readonly lastReplayed: boolean
  readonly repeatsUnresolved: boolean
  readonly currentIntent: AccountStatusIntent | null
  readonly openBlocked: OpenBlockedReason
  readonly busy: boolean
  readonly locked: boolean
  readonly contractReady: boolean
  readonly summaryFocusRequest: number
  readonly open: () => void
  readonly setReason: (value: string) => void
  readonly submit: () => void
  readonly requestClose: () => void
  readonly keepOpen: () => void
  readonly confirmClose: () => void
  readonly retry: () => void
  readonly abandon: () => void
  readonly refreshRecord: () => void
  readonly adoptFresh: () => void
}

export function useAccountStatusFlow({
  partyId,
  companyId,
  namespace,
  detail,
  readStamp,
  refetch,
  contractReady,
  disabled,
  onApplied,
  onBeginNewAttempt,
  triggerRef,
  summaryRef,
  unresolvedRef,
}: AccountStatusFlowInput): AccountStatusFlow {
  const dispatch = useShellDispatch()
  const attempt = useCommandAttempt<AccountStatusAttemptPayload, null>()
  const [dialog, setDialog] = useState<OpenDialog | null>(null)
  const [reason, setReasonState] = useState('')
  const [showErrors, setShowErrors] = useState(false)
  const [closeConfirm, setCloseConfirm] = useState<CloseConfirm>(null)
  const [summaryFocusRequest, setSummaryFocusRequest] = useState(0)
  /** Whether the last DEFINITE answer was a replay — read off the transport. */
  const [lastReplayed, setLastReplayed] = useState(false)
  /**
   * A read requested to re-seat the open window on the record's current version —
   * after a conflict, or after a read that landed underneath. The reason is NOT touched
   * by it; only the fence and the shown state move, once the read has really landed.
   */
  const [refresh, setRefresh] = useState<{ readonly baseline: number } | null>(null)
  const [refreshedNote, setRefreshedNote] = useState(false)

  const operation = `counterparty.account-status:${partyId}`
  const submitting = attempt.state.phase === 'submitting'
  const unknown = attempt.state.phase === 'unknown'
  const frozen = submitting || unknown
  const completed = attempt.state.phase === 'completed' ? attempt.state : null
  const failed = attempt.state.phase === 'failed' ? attempt.state : null

  const currentIntent = detail === null ? null : intentFor(detail.account.isActive)
  const changedSinceOpen =
    dialog !== null && detail !== null && refresh === null && detail.account.version !== dialog.openedOnVersion
  const problem = reasonProblem(reason)

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
    setReasonState('')
    setShowErrors(false)
    setCloseConfirm(null)
    setRefresh(null)
    setRefreshedNote(false)
    onApplied({ intent, replayed: completed.replayed })
    attempt.abandon()
    triggerRef.current?.focus()
  }, [completed, onApplied, attempt, triggerRef])

  /**
   * Re-seating the open window once the requested read has LANDED — `readStamp` moved
   * past the baseline taken when the read was asked for. A read that never completes
   * (offline, failed) leaves the window on its old fence, with the reason intact.
   */
  useEffect(() => {
    if (refresh === null || detail === null || readStamp <= refresh.baseline) return
    // oxlint-disable-next-line react/set-state-in-effect -- adopts the fresh read the user asked for, once
    setDialog((current) =>
      current === null
        ? null
        : {
            intent: intentFor(detail.account.isActive),
            openedOnVersion: detail.account.version,
            openedOnIsActive: detail.account.isActive,
          },
    )
    setRefreshedNote(true)
    setRefresh(null)
  }, [refresh, detail, readStamp])

  useEffect(() => {
    if (summaryFocusRequest === 0) return
    summaryRef.current?.focus()
  }, [summaryFocusRequest, summaryRef])

  const openBlocked: OpenBlockedReason = unknown
    ? 'unresolved'
    : !contractReady
      ? 'contract-not-ready'
      : disabled
        ? 'adopting'
        : null

  const open = useCallback(() => {
    if (detail === null || openBlocked !== null || frozen || attempt.revalidating) return
    // A definite failure is acknowledged by starting over — the same rule the editors
    // apply to "edits a field".
    if (failed !== null) attempt.abandon()
    onBeginNewAttempt()
    setLastReplayed(false)
    setReasonState('')
    setShowErrors(false)
    setCloseConfirm(null)
    setRefresh(null)
    setRefreshedNote(false)
    setDialog({
      intent: intentFor(detail.account.isActive),
      openedOnVersion: detail.account.version,
      openedOnIsActive: detail.account.isActive,
    })
  }, [detail, openBlocked, frozen, attempt, failed, onBeginNewAttempt])

  const closeNow = useCallback(() => {
    setDialog(null)
    setReasonState('')
    setShowErrors(false)
    setCloseConfirm(null)
    setRefresh(null)
    setRefreshedNote(false)
    triggerRef.current?.focus()
  }, [triggerRef])

  const requestClose = useCallback(() => {
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
  }, [submitting, unknown, reason, closeNow])

  const setReason = useCallback(
    (value: string) => {
      setReasonState(value)
      if (showErrors && reasonProblem(value) === null) setShowErrors(false)
    },
    [showErrors],
  )

  const submit = useCallback(() => {
    if (dialog === null || frozen || attempt.revalidating || changedSinceOpen || refresh !== null) return
    if (!contractReady || companyId === null || namespace === null) return
    if (problem !== null) {
      // Client-invalid: nothing is sent and NO key is minted.
      setShowErrors(true)
      setSummaryFocusRequest((count) => count + 1)
      return
    }
    const payload = buildAccountStatusPayload(dialog.intent, reason)
    if (payload === null) return
    setShowErrors(false)
    setRefreshedNote(false)
    const expectedVersion = dialog.openedOnVersion
    const target = partyId
    const context = { companyId, idempotencyKey: '', contract: { origin: API_BASE, namespace } }
    const send = (idempotencyKey: string, signal: AbortSignal): Promise<CommandOutcome<null>> =>
      setCompanyAccountActiveState({ ...context, idempotencyKey }, target, expectedVersion, payload, signal).then(
        (outcome) => {
          if (outcome.type === 'http') setLastReplayed(outcome.replayed ?? false)
          return outcome
        },
      )
    const result = attempt.submit(
      operation,
      { expectedVersion, isActive: payload.isActive, reason: payload.reason },
      send,
    )
    if (result === 'unchanged') unresolvedRef.current?.focus()
  }, [
    dialog,
    frozen,
    attempt,
    changedSinceOpen,
    refresh,
    contractReady,
    companyId,
    namespace,
    problem,
    reason,
    partyId,
    operation,
    unresolvedRef,
  ])

  /**
   * The same-key retry's precondition: a fresh, authorized, acknowledged read in THIS
   * context — not the grant the first send rode on. A refetch can settle from cache and
   * still resolve, so the recorded-read counter has to have MOVED.
   */
  const revalidate = useCallback(async () => {
    const before = contractReadSeq()
    await refetch()
    return (
      contractReadSeq() > before &&
      namespace !== null &&
      isCommandContractReady({ origin: API_BASE, namespace })
    )
  }, [refetch, namespace])

  const retry = useCallback(() => attempt.retry(revalidate), [attempt, revalidate])

  /**
   * After a conflict: a READ, with the reason kept. The failed attempt is closed (its
   * fence is stale and will never be resent), the window stays open, and once the read
   * lands the window is re-seated on the current version — a NEW attempt, on review.
   */
  const refreshRecord = useCallback(() => {
    if (failed !== null) attempt.abandon()
    setRefreshedNote(false)
    setRefresh({ baseline: readStamp })
    void refetch()
  }, [failed, attempt, readStamp, refetch])

  /** A read that landed underneath the open window: move the fence to it, keep the reason. */
  const adoptFresh = useCallback(() => {
    if (detail === null) return
    setDialog((current) =>
      current === null
        ? null
        : {
            intent: intentFor(detail.account.isActive),
            openedOnVersion: detail.account.version,
            openedOnIsActive: detail.account.isActive,
          },
    )
    setRefreshedNote(true)
  }, [detail])

  return {
    attempt,
    dialog,
    reason,
    problem,
    showErrors,
    closeConfirm,
    changedSinceOpen,
    refreshing: refresh !== null,
    refreshedNote,
    lastReplayed,
    repeatsUnresolved,
    currentIntent,
    openBlocked,
    busy: submitting || attempt.revalidating,
    locked: unknown,
    contractReady,
    summaryFocusRequest,
    open,
    setReason,
    submit,
    requestClose,
    keepOpen: () => setCloseConfirm(null),
    confirmClose: closeNow,
    retry,
    abandon: attempt.abandon,
    refreshRecord,
    adoptFresh,
  }
}
