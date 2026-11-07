/**
 * SAT-D32 / SAT-D168 / SAT-D215 — "Siparişi kapat" on S-04 for a "kapanmaya hazır" order, behind the `sales.orders.close`
 * control the shell publishes only to an actor holding that name beside the read.
 *
 *  - Offered only when the server's status read (API-08, the same read the status card shows) answers `readyToClose`; the
 *    screen never derives readiness. While the source, invoicing and financial-closure owners do not exist that flag is
 *    never true, so the action is not offered on main.
 *  - No reason (SAT-D215). The server recomputes the readiness inside the save; a not-ready order is its 409, after which the
 *    detail and the status are read again.
 *  - One idempotent attempt per snapshot (the force-close's rules): an unknown outcome keeps its key, a retry resends the same
 *    snapshot, and a recorded closure re-reads the detail.
 *
 * `ClosedOrderRecord` shows a closed order's record: the user closure when there is one, otherwise the caller's fallback (the
 * force-closure record). Both closures share the "Kapalı" lifecycle; the record's label tells them apart (reversible
 * implementation detail, EK37 "Sınırlar").
 */
import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftStatus, fetchDraftUserClosure, sendDraftClose } from './api'
import {
  APPROVAL_STATE,
  DRAFT_STATE,
  SALES_PROBLEM_CODE,
  type DraftCloseRequest,
  type DraftDetail,
  type DraftMutation,
} from './contract'
import {
  abandon,
  applyOutcome,
  beginSend,
  decideSubmit,
  generateOrderAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  orderAttemptBinding,
  type OrderAttemptSnapshot,
  type OrderAttemptState,
} from './orderAttempt'
import { SALES_CAPABILITY, SALES_CLOSE_CAPABILITY } from './capabilities'
import { formatInstant } from './format'
import { USER_CLOSE_TEXT as T, problemCodeText } from './salesText'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

type CloseAttempt = OrderAttemptState<DraftCloseRequest, DraftMutation>

const scopeOf = (draftId: string) => `sales.draft.close:${draftId}`

export function SalesDraftUserClose({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  if (
    detail.draft.state !== DRAFT_STATE ||
    detail.draft.approvalState !== APPROVAL_STATE.approved ||
    companyId !== detail.draft.companyId ||
    !hasCapability(shell, SALES_CLOSE_CAPABILITY) ||
    !hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  ) return null
  return <UserCloseAction shell={shell} detail={detail} companyId={companyId} onRecorded={onRecorded} />
}

function UserCloseAction({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const [attempt, setAttempt] = useState<CloseAttempt>({ phase: 'idle' })
  const attemptRef = useRef(attempt)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  // The status card's own read (same key): the readiness is the server's, read afresh on every open.
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-status', detail.draft.id, detail.draft.version],
    enabled: namespace !== null,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftStatus(detail, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })

  async function send(snapshot: OrderAttemptSnapshot<DraftCloseRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftCloseRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const outcome = await sendDraftClose(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      draftId: detail.draft.id,
    })
    const next = applyOutcome(inFlight, snapshot, orderAttemptBinding(shell, scopeOf(detail.draft.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase === 'completed') {
      onRecorded()
    } else if (next.phase === 'failed' && next.failure === 'conflict') {
      // The order or its readiness moved: the detail and the status are read again, never guessed.
      onRecorded()
      void query.refetch()
    }
  }

  function start() {
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const payload: DraftCloseRequest = { expectedVersion: detail.draft.version }
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/close`, binding, companyId, payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'send') void send(decision.snapshot)
  }

  function retry() {
    const snapshot = liveSnapshot(attemptRef.current)
    if (snapshot !== null && attemptRef.current.phase !== 'submitting') void send(snapshot)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
  }

  const outcome = query.data
  const notice = closeNotice(attempt)
  const ready = outcome?.type === 'ok-single' && outcome.data.readyToClose
  const failedRead = outcome !== undefined && outcome.type !== 'ok-single'
  // Not ready (the server's answer) and nothing to report: no action is offered and nothing is shown.
  if (!ready && !failedRead && notice === null) return null

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-user-close">
      <h2 id={headingId}>{T.heading}</h2>
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-user-close-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={retry}>{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={leave}>{T.abandon}</Button>
        </div>
      ) : null}
      {ready ? (
        <>
          <p className={styles.muted} id={`${headingId}-description`}>{T.description}</p>
          <div className={styles.actions}>
            <Button type="button" variant="primary" disabled={frozen} onClick={start}
              aria-describedby={`${headingId}-description`} data-testid="sales-user-close-submit">
              {T.submit}
            </Button>
          </div>
        </>
      ) : failedRead ? (
        <StateBlock state={classifyOutcome(outcome)} subject={T.statusSubject} onRetry={() => void query.refetch()} />
      ) : null}
    </section>
  )
}

/**
 * The record of a CLOSED order: reads the user closure under the draft read; when the order was not user-closed it renders
 * `fallback` (the force-closure record, which reports a missing record itself).
 */
export function ClosedOrderRecord({
  shell,
  detail,
  companyId,
  fallback,
}: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly fallback: ReactNode
}) {
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const canRead = namespace !== null && companyId === detail.draft.companyId && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-user-closure', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftUserClosure(detail.draft.id, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const outcome = query.data
  if (outcome?.type === 'ok-single' && outcome.data.userClosure === null) return <>{fallback}</>
  const record = outcome?.type === 'ok-single' ? outcome.data.userClosure : null
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-user-close-record">
      <h2 id={headingId}>{T.recordHeading}</h2>
      {record !== null ? (
        <>
          <p className={styles.muted}>{T.recordNote}</p>
          <dl className={styles.facts}>
            <div><dt>{T.closedBy}</dt><dd data-testid="sales-user-close-by">{record.closedBy}</dd></div>
            <div><dt>{T.closedAt}</dt><dd>{formatInstant(record.closedAtUtc)}</dd></div>
            <div><dt>{T.revision}</dt><dd>{record.revision}</dd></div>
          </dl>
        </>
      ) : (
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.recordSubject}
          onRetry={() => void query.refetch()}
        />
      )}
    </section>
  )
}

function closeNotice(attempt: CloseAttempt): string | null {
  switch (attempt.phase) {
    case 'idle': return null
    case 'submitting': return T.submitting
    case 'unknown':
    case 'abandoned': return T.unknown
    case 'completed': return attempt.result.isReplay ? T.replayed : T.completed
    case 'failed': {
      const code = attempt.problem?.code ?? null
      const codeText = code === SALES_PROBLEM_CODE.versionConflict ? T.versionConflict : problemCodeText(code)
      const base = attempt.failure === 'unauthenticated' ? T.unauthenticated
        : attempt.failure === 'forbidden' ? T.forbidden
          : attempt.failure === 'not-found' ? T.notFound
            : attempt.failure === 'conflict' ? T.conflict
              : attempt.failure === 'validation' ? T.validation
                : T.unexpected
      return codeText === null ? base : `${base} ${codeText}`
    }
    default: return null
  }
}
