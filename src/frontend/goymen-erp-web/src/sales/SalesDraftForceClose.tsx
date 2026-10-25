/**
 * Delegation No 30 / SAT-D12 — "Siparişi kapat" (force-close) on S-04 of an APPROVED order, behind the
 * `sales.orders.force-close` control the shell publishes only to an actor holding that name beside the read.
 *
 *  - "Kapatma etkisini göster" reads the preview afresh from the server, which reads every category from its OWNER. Each
 *    category says whether it blocks: an active allocation, open (not yet shipped) loading or an unreadable owner blocks; an
 *    uninvoiced dispatch note is a warning only (SAT-D12); "kaynak yok" and "okunamadı" are never shown as 0.
 *  - Per line the preview shows the remainder that would be closed (ordered − shipped) — the server's number, never computed
 *    here.
 *  - Only a preview the server calls closable offers the command, with a required reason; the command carries the preview's
 *    token, so an order is never closed on an impact the user did not see.
 *  - One idempotent attempt per snapshot (the cancellation's rules): an unknown outcome keeps its key, a retry resends the
 *    same snapshot, and a recorded closure re-reads the detail.
 * A closed order shows its recorded closure under the draft read. Nothing here releases another module's record.
 */
import { useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftForceClosure, fetchForceCloseImpact, sendDraftForceClose } from './api'
import {
  APPROVAL_STATE,
  CANCELLATION_STATUS,
  CLOSED_STATE,
  DRAFT_STATE,
  SALES_PROBLEM_CODE,
  type DraftDetail,
  type DraftForceCloseRequest,
  type DraftMutation,
  type ForceCloseImpact,
  type ForceCloseImpactSource,
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
import { SALES_CAPABILITY, SALES_FORCE_CLOSE_CAPABILITY } from './capabilities'
import { formatInstant } from './format'
import { FORCE_CLOSE_TEXT as T, ORDER_DETAIL_TEXT, problemCodeText } from './salesText'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

type ForceCloseAttempt = OrderAttemptState<DraftForceCloseRequest, DraftMutation>

/** The category names and statuses are the cancellation preview's own words (the same six DEC-SAL-005 categories). */
const C = ORDER_DETAIL_TEXT.cancellation
const scopeOf = (draftId: string) => `sales.draft.force-close:${draftId}`

export function SalesDraftForceClose({
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
  if (detail.draft.state === CLOSED_STATE) return <ForceClosureRecordView shell={shell} detail={detail} companyId={companyId} />
  if (
    detail.draft.state !== DRAFT_STATE ||
    detail.draft.approvalState !== APPROVAL_STATE.approved ||
    !hasCapability(shell, SALES_FORCE_CLOSE_CAPABILITY)
  ) return null
  return <ForceClosePreview shell={shell} detail={detail} companyId={companyId} onRecorded={onRecorded} />
}

function ForceClosePreview({
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
  const [attempt, setAttempt] = useState<ForceCloseAttempt>({ phase: 'idle' })
  const attemptRef = useRef(attempt)

  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-force-close-impact', detail.draft.id, detail.draft.version],
    enabled: open && namespace !== null && companyId === detail.draft.companyId,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchForceCloseImpact(detail, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })

  async function send(snapshot: OrderAttemptSnapshot<DraftForceCloseRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<DraftForceCloseRequest, DraftMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const outcome = await sendDraftForceClose(snapshot.payload, {
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

  function start(impact: ForceCloseImpact) {
    setLocalError(null)
    const typed = reason.trim()
    if (typed === '') {
      setLocalError(T.reasonRequired)
      return
    }
    const binding = orderAttemptBinding(shell, scopeOf(detail.draft.id))
    if (binding === null) return
    const payload: DraftForceCloseRequest = { expectedVersion: detail.draft.version, reason: typed, impactToken: impact.impactToken }
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${detail.draft.id}/force-close`, binding, companyId, payload },
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
  const notice = forceCloseNotice(attempt)
  const outcome = query.data
  const warned = outcome?.type === 'ok-single'
    && outcome.data.sources.some((entry) => entry.status === CANCELLATION_STATUS.present && !entry.blocks)

  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-force-close">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={styles.muted}>{T.description}</p>
      {notice === null ? null : (
        <p role={attempt.phase === 'failed' ? 'alert' : 'status'} className={styles.notice} data-testid="sales-force-close-notice">
          {notice}
        </p>
      )}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.actions}>
          <Button type="button" variant="secondary" onClick={retry}>{T.retry}</Button>
          <Button type="button" variant="ghost" className={styles.linkButton} onClick={leave}>{T.abandon}</Button>
        </div>
      ) : null}
      {!open ? (
        <Button type="button" variant="secondary" onClick={() => setOpen(true)} data-testid="sales-force-close-open">
          {T.showImpact}
        </Button>
      ) : outcome?.type === 'ok-single' ? (
        <div data-testid="sales-force-close-impact">
          <ImpactView impact={outcome.data} />
          {warned ? <p role="status" className={styles.notice} data-testid="sales-force-close-warning">{T.warning}</p> : null}
          <Button type="button" variant="ghost" className={styles.linkButton} disabled={frozen} onClick={() => void query.refetch()}>
            {T.refresh}
          </Button>
          {outcome.data.closable ? (
            <div className={styles.actions}>
              {localError === null ? null : <p role="alert" className={styles.notice}>{localError}</p>}
              <label htmlFor="sales-force-close-reason">{T.reasonLabel}</label>
              <textarea
                id="sales-force-close-reason"
                maxLength={4000}
                value={reason}
                disabled={frozen}
                onChange={(event) => setReason(event.target.value)}
              />
              <Button type="button" variant="danger" disabled={frozen} onClick={() => start(outcome.data)}
                data-testid="sales-force-close-submit">
                {T.submit}
              </Button>
            </div>
          ) : (
            <p className={styles.notice} role="status" data-testid="sales-force-close-blocked">{T.blocked}</p>
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

function SourcesTable({ sources, testId }: { readonly sources: readonly ForceCloseImpactSource[]; readonly testId: string }) {
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table} data-testid={testId}>
        <caption className={styles.caption}>{T.sourcesCaption}</caption>
        <thead><tr><th scope="col">{T.category}</th><th scope="col">{T.status}</th><th scope="col">{T.effect}</th></tr></thead>
        <tbody>{sources.map((entry) => (
          <tr key={entry.source} data-testid={`${testId}-${entry.source}`} data-status={entry.status} data-blocks={String(entry.blocks)}>
            <th scope="row">{C.source[entry.source] ?? entry.source}</th>
            <td>{STATUS_TEXT[entry.status] ?? entry.status}</td>
            <td>{entry.blocks ? T.blocks : entry.status === CANCELLATION_STATUS.present ? T.warns : T.noEffect}</td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

/** The status in neutral words: whether it blocks is the third column's (it differs from the cancellation's). */
const STATUS_TEXT: Record<string, string> = {
  None: 'Kayıt yok (sahibi okundu)',
  Present: 'Kayıt var',
  Unreadable: 'Okunamadı; sıfır varsayılmaz',
  NoSource: 'Kaynak yok; bu bağı tutan modül henüz yok',
}

function ImpactView({ impact }: { readonly impact: ForceCloseImpact }) {
  const orUnreadable = (value: string | number | null) => (value === null ? C.unreadable : String(value))
  return (
    <>
      <SourcesTable sources={impact.sources} testId="sales-force-close-source" />
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{T.linesCaption}</caption>
          <thead><tr>
            <th scope="col">{C.lineNo}</th><th scope="col">{C.ordered}</th><th scope="col">{C.activeAllocations}</th>
            <th scope="col">{C.instructed}</th><th scope="col">{C.shipped}</th><th scope="col">{C.delivered}</th>
            <th scope="col">{T.closedRemainder}</th>
          </tr></thead>
          <tbody>{impact.lines.map((line) => (
            <tr key={line.lineId} data-testid={`sales-force-close-line-${line.lineNo}`}>
              <th scope="row">{line.lineNo}</th>
              <td>{line.ordered}</td>
              <td>{orUnreadable(line.activeAllocations)}</td>
              <td>{orUnreadable(line.instructed)}</td>
              <td>{orUnreadable(line.shipped)}</td>
              <td>{orUnreadable(line.delivered)}</td>
              <td data-testid={`sales-force-close-line-${line.lineNo}-remainder`}>{orUnreadable(line.closedRemainder)}</td>
            </tr>
          ))}</tbody>
        </table>
      </div>
    </>
  )
}

/** The recorded closure of a closed order, read afresh under the draft read on every open. */
function ForceClosureRecordView({ shell, detail, companyId }: { readonly shell: ShellState; readonly detail: DraftDetail; readonly companyId: string }) {
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const canRead = namespace !== null && companyId === detail.draft.companyId && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-force-closure', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftForceClosure(detail.draft.id, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const outcome = query.data
  const record = outcome?.type === 'ok-single' ? outcome.data.forceClosure : undefined
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-force-close-record">
      <h2 id={headingId}>{T.recordHeading}</h2>
      <p className={styles.muted}>{T.recordNote}</p>
      {record !== undefined && record !== null ? (
        <>
          <dl className={styles.facts}>
            <div><dt>{T.closedBy}</dt><dd data-testid="sales-force-close-by">{record.closedBy}</dd></div>
            <div><dt>{T.closedAt}</dt><dd>{formatInstant(record.closedAtUtc)}</dd></div>
            <div><dt>{T.revision}</dt><dd>{record.revision}</dd></div>
            <div><dt>{T.reason}</dt><dd data-testid="sales-force-close-reason-text">{record.reason}</dd></div>
          </dl>
          <h3>{T.recordLines}</h3>
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <caption className={styles.caption}>{T.recordLines}</caption>
              <thead><tr>
                <th scope="col">{C.lineNo}</th><th scope="col">{C.ordered}</th><th scope="col">{C.shipped}</th>
                <th scope="col">{T.closedRemainder}</th>
              </tr></thead>
              <tbody>{record.lines.map((line) => (
                <tr key={line.lineId} data-testid={`sales-force-close-record-line-${line.lineNo}`}>
                  <th scope="row">{line.lineNo}</th><td>{line.ordered}</td><td>{line.shipped}</td><td>{line.closedRemainder}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
          <h3>{T.recordSources}</h3>
          <SourcesTable sources={record.sources} testId="sales-force-close-record-source" />
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

function forceCloseNotice(attempt: ForceCloseAttempt): string | null {
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
