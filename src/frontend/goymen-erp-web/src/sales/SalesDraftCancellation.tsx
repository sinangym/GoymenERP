/**
 * DEC-SAL-005 (KILITLI): "Sipariş iptalinde aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme, faturalanmamış
 * irsaliye ve eşleşmemiş avans/dekont etkisi gösterilir." — "Etki önizlemesi olmadan iptal yok." AT-007: "İptal ve ters
 * işlem geçmişi silmez."
 *
 * On S-04 of an APPROVED order, behind the `sales.orders.cancel` control the shell publishes only to an actor holding the
 * cancel name beside the read:
 *  - "İptal etkisini göster" reads the preview afresh from the server, which reads every category from its OWNER (Inventory
 *    allocations, Logistics loading); a category no module on main records reads "kaynak yok" and an owner that could not
 *    be read reads "okunamadı" — neither is ever shown as 0;
 *  - only a preview the server calls cancellable offers the command, with a required reason; the command carries the
 *    preview's token, so an order is never cancelled on an impact the user did not see (a changed impact is a 409 and the
 *    preview is read again);
 *  - one idempotent attempt per snapshot (the delivery schedule's rules): an unknown outcome keeps its key, a retry resends
 *    the same snapshot, and a recorded cancellation re-reads the detail.
 * A cancelled order shows its recorded cancellation (who, when, why, the preview it was decided on) under the draft read.
 * Nothing here releases or reverses another module's record.
 */
import { useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchCancellationImpact, fetchDraftCancellation, sendDraftCancel } from './api'
import {
  APPROVAL_STATE,
  CANCELLED_STATE,
  SALES_PROBLEM_CODE,
  type CancellationImpact,
  type CancellationImpactSource,
  type DraftCancelRequest,
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
import { SALES_CANCEL_CAPABILITY, SALES_CAPABILITY } from './capabilities'
import { formatInstant } from './format'
import { ORDER_DETAIL_TEXT, problemCodeText } from './salesText'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'

type CancelAttempt = OrderAttemptState<DraftCancelRequest, DraftMutation>

const T = ORDER_DETAIL_TEXT.cancellation
const scopeOf = (draftId: string) => `sales.draft.cancellation:${draftId}`

export function SalesDraftCancellation({
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
  if (detail.draft.state === CANCELLED_STATE) return <CancellationRecordView shell={shell} detail={detail} companyId={companyId} />
  if (!hasCapability(shell, SALES_CANCEL_CAPABILITY) || detail.draft.approvalState !== APPROVAL_STATE.approved) return null
  return <CancellationPreview shell={shell} detail={detail} companyId={companyId} onRecorded={onRecorded} />
}

function CancellationPreview({
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
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<CancelAttempt>({ phase: 'idle' })
  const attemptRef = useRef(attempt)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-cancellation-impact', detail.draft.id, detail.draft.version],
    enabled: open && namespace !== null && companyId === detail.draft.companyId,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCancellationImpact(detail, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })

  async function send(snapshot: OrderAttemptSnapshot<DraftCancelRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftCancelRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const outcome = await sendDraftCancel(snapshot.payload, {
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
      // The order or its owners moved: the detail and the preview are read again, never guessed.
      onRecorded()
      void query.refetch()
    }
  }

  function start(impact: CancellationImpact) {
    setLocalError(null)
    const typed = reason.trim()
    if (typed === '') {
      setLocalError(T.reasonRequired)
      return
    }

    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const payload: DraftCancelRequest = { expectedVersion: detail.draft.version, reason: typed, impactToken: impact.impactToken }
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/cancellation`, binding, companyId, payload },
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

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  const notice = cancelNotice(attempt)
  const outcome = query.data

  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-cancellation">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={styles.muted}>{T.description}</p>
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-cancellation-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <button type="button" className={styles.secondaryButton} onClick={retry}>{T.retry}</button>
          <button type="button" className={styles.linkButton} onClick={leave}>{T.abandon}</button>
        </div>
      ) : null}
      {!open ? (
        <button type="button" className={styles.secondaryButton} onClick={() => setOpen(true)} data-testid="sales-cancellation-open">
          {T.showImpact}
        </button>
      ) : outcome?.type === 'ok-single' ? (
        <div data-testid="sales-cancellation-impact">
          <ImpactView impact={outcome.data} />
          <button type="button" className={styles.linkButton} disabled={frozen} onClick={() => void query.refetch()}>
            {T.refresh}
          </button>
          {outcome.data.cancellable ? (
            <div className={styles.actions}>
              {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
              <label htmlFor="sales-cancellation-reason">{T.reasonLabel}</label>
              <textarea
                id="sales-cancellation-reason"
                maxLength={4000}
                value={reason}
                disabled={frozen}
                onChange={(event) => setReason(event.target.value)}
              />
              <button type="button" className={styles.primaryButton} disabled={frozen} onClick={() => start(outcome.data)}
                data-testid="sales-cancellation-submit">
                {T.submit}
              </button>
            </div>
          ) : (
            <p className={styles.notice} role="status" data-testid="sales-cancellation-blocked">{T.blocked}</p>
          )}
        </div>
      ) : (
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={() => void query.refetch()}
        />
      )}
    </section>
  )
}

function SourcesTable({ sources, testId }: { readonly sources: readonly CancellationImpactSource[]; readonly testId: string }) {
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table} data-testid={testId}>
        <caption className={styles.caption}>{T.sourcesCaption}</caption>
        <thead><tr><th scope="col">{T.category}</th><th scope="col">{T.status}</th></tr></thead>
        <tbody>{sources.map((entry) => (
          <tr key={entry.source} data-testid={`${testId}-${entry.source}`} data-status={entry.status}>
            <th scope="row">{T.source[entry.source] ?? entry.source}</th>
            <td>{T.statusText[entry.status] ?? entry.status}</td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

function ImpactView({ impact }: { readonly impact: CancellationImpact }) {
  const orUnreadable = (value: string | number | null) => (value === null ? T.unreadable : String(value))
  return (
    <>
      <SourcesTable sources={impact.sources} testId="sales-cancellation-source" />
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{T.linesCaption}</caption>
          <thead><tr>
            <th scope="col">{T.lineNo}</th><th scope="col">{T.ordered}</th><th scope="col">{T.activeAllocations}</th>
            <th scope="col">{T.allocatedKg}</th><th scope="col">{T.loadingLines}</th><th scope="col">{T.instructed}</th>
            <th scope="col">{T.loaded}</th><th scope="col">{T.shipped}</th><th scope="col">{T.delivered}</th>
          </tr></thead>
          <tbody>{impact.lines.map((line) => (
            <tr key={line.lineId} data-testid={`sales-cancellation-line-${line.lineNo}`}>
              <th scope="row">{line.lineNo}</th>
              <td>{line.ordered}</td>
              <td data-testid={`sales-cancellation-line-${line.lineNo}-allocations`}>{orUnreadable(line.activeAllocations)}</td>
              <td>{orUnreadable(line.allocatedKg)}</td>
              <td data-testid={`sales-cancellation-line-${line.lineNo}-loading`}>{orUnreadable(line.loadingInstructionLines)}</td>
              <td>{orUnreadable(line.instructed)}</td>
              <td>{orUnreadable(line.loaded)}</td>
              <td>{orUnreadable(line.shipped)}</td>
              <td>{orUnreadable(line.delivered)}</td>
            </tr>
          ))}</tbody>
        </table>
      </div>
    </>
  )
}

/** AT-007: the recorded cancellation of a cancelled order, read afresh under the draft read on every open. */
function CancellationRecordView({ shell, detail, companyId }: { readonly shell: ShellState; readonly detail: DraftDetail; readonly companyId: string }) {
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const canRead = namespace !== null && companyId === detail.draft.companyId && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-cancellation', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftCancellation(detail.draft.id, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const outcome = query.data
  const record = outcome?.type === 'ok-single' ? outcome.data.cancellation : undefined
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-cancellation-record">
      <h2 id={headingId}>{T.recordHeading}</h2>
      <p className={styles.muted}>{T.recordNote}</p>
      {record !== undefined && record !== null ? (
        <>
          <dl className={styles.facts}>
            <div><dt>{T.cancelledBy}</dt><dd data-testid="sales-cancellation-by">{record.cancelledBy}</dd></div>
            <div><dt>{T.cancelledAt}</dt><dd>{formatInstant(record.cancelledAtUtc)}</dd></div>
            <div><dt>{T.revision}</dt><dd>{record.revision}</dd></div>
            <div><dt>{T.reason}</dt><dd data-testid="sales-cancellation-reason-text">{record.reason}</dd></div>
          </dl>
          <h3>{T.recordSources}</h3>
          <SourcesTable sources={record.sources} testId="sales-cancellation-record-source" />
        </>
      ) : record === null ? (
        <p role="alert" className={styles.notice}>{T.recordMissing}</p>
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

function cancelNotice(attempt: CancelAttempt): string | null {
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
