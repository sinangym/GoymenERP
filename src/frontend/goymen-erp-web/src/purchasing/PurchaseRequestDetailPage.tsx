import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useParams } from 'react-router'
import { fetchPurchaseRequest } from './api'
import { purchaseOrderDetailPath, PURCHASING_CAPABILITY } from './capabilities'
import { submitPurchaseRequest, type RequestSubmissionOutcome } from './requestSubmissionApi'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { pendingPurchaseRequestSubmissionKey, recallPendingPurchaseRequestSubmission, releasePendingPurchaseRequestSubmission, reservePendingPurchaseRequestSubmission, type PendingPurchaseRequestSubmission } from './pendingRequestStore'
import { Button } from '../ui/Button'
import type { PurchaseRequestLineSource } from './contract'
import { PURCHASE_REQUEST_SOURCE_LABEL, PURCHASE_REQUEST_STATUS_LABEL, REQUEST_DECISION_TEXT, REQUEST_DETAIL_TEXT as T, REQUEST_WITHDRAWAL_TEXT } from './purchasingText'
import { PurchaseRequestDecisionPanel } from './PurchaseRequestDecisionPanel'
import { PurchaseRequestWithdrawalPanel } from './PurchaseRequestWithdrawalPanel'
import styles from './purchasing.module.css'

/** Navigation hint only: backend authorization still owns both detail reads. */
function sourceOrderId(value: unknown, namespace: string | null, requestId: string | undefined): string | null {
  if (namespace === null || requestId === undefined || value === null || typeof value !== 'object' || Array.isArray(value)) return null
  const fields = Object.keys(value)
  if (fields.length !== 3 || !['ns', 'requestId', 'orderId'].every((field) => Object.prototype.hasOwnProperty.call(value, field))) return null
  const origin = value as Record<string, unknown>
  if (origin.ns !== namespace || origin.requestId !== requestId || typeof origin.orderId !== 'string') return null
  // Only a canonical non-empty Guid, never an arbitrary URL, query or returnTo.
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(origin.orderId)
    || origin.orderId === '00000000-0000-0000-0000-000000000000') return null
  return origin.orderId
}

/**
 * A1-2 (plan API-02): the line's source, read-only — the module and the opaque source reference as the server sent it.
 * Purchasing does not resolve the reference into the source module's record (no port yet); a body without the field
 * (before A1-2) is unknown, never shown as manual.
 */
function sourceCell(source: PurchaseRequestLineSource | null | undefined): ReactNode {
  if (source === undefined) return T.sourceUnknown
  if (source === null) return PURCHASE_REQUEST_SOURCE_LABEL.Manual
  return <>{PURCHASE_REQUEST_SOURCE_LABEL[source.kind]} <span className={styles.mono}>{T.sourceReference(source.documentId, source.documentLineId)}</span></>
}

export function PurchaseRequestDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { requestId } = useParams()
  const location = useLocation()
  // Keep the entry generation outside the keyed detail. A stale detail remount
  // must not re-authorize a return hint left behind by an earlier entry.
  const [entry, setEntry] = useState(() => ({
    key: location.key, state: location.state as unknown, requestId, generation: shell.routeEntryGeneration,
  }))
  const sameEntry = entry.key === location.key && entry.state === location.state && entry.requestId === requestId
  if (!sameEntry) {
    setEntry({ key: location.key, state: location.state as unknown, requestId, generation: shell.routeEntryGeneration })
  }
  const returnOrderId = sameEntry && entry.generation === shell.routeEntryGeneration
    ? sourceOrderId(location.state as unknown, namespace, requestId)
    : null
  return <PurchaseRequestDetail key={`${namespace ?? 'no-context'}::${requestId ?? ''}::${shell.routeEntryGeneration}`} namespace={namespace} returnOrderId={returnOrderId} />
}

function PurchaseRequestDetail({ namespace, returnOrderId }: { readonly namespace: string | null; readonly returnOrderId: string | null }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const { requestId } = useParams()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const canSubmit = hasCapability(shell, PURCHASING_CAPABILITY.requestSubmit)
  // A1-1: the decision control is its own child capability (purchasing.requests.approve); the backend re-measures it.
  const canDecide = hasCapability(shell, PURCHASING_CAPABILITY.requestApprove)
  // A1-1b (ALS-D46, ALS-D47): the withdrawal control is its own child capability (purchasing.requests.withdraw), shown only
  // to the recorded preparer or submitter; the backend re-measures the name and "only the preparer or the submitter".
  const canWithdraw = hasCapability(shell, PURCHASING_CAPABILITY.requestWithdraw)
  const storeKey = pendingPurchaseRequestSubmissionKey(shell.sessionEpoch, actorId, companyId, requestId ?? '')
  const recalled = recallPendingPurchaseRequestSubmission(storeKey)
  const [attempt, setAttempt] = useState<
    | { readonly phase: 'idle' }
    | { readonly phase: 'submitting'; readonly snapshot: PendingPurchaseRequestSubmission }
    | { readonly phase: 'unknown'; readonly snapshot: PendingPurchaseRequestSubmission; readonly outcome: Extract<RequestSubmissionOutcome, { type: 'unknown' }> }
    | { readonly phase: 'applied'; readonly replayed: boolean }
    | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }
  >(() => recalled === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: recalled, outcome: { type: 'unknown', reason: 'network-error' } })
  const inFlight = useRef(false); const responseGeneration = useRef(0)
  useEffect(() => { inFlight.current = false; return () => { responseGeneration.current += 1 } }, [namespace, shell.routeEntryGeneration])
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'purchase-request', requestId ?? ''],
    enabled: namespace !== null && companyId !== null && (requestId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture(); const result = await fetchPurchaseRequest(companyId ?? '', requestId ?? '', signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<import('./contract').PurchaseRequestDetail>
  const send = useCallback(async (retry?: PendingPurchaseRequestSubmission) => {
    if (state.kind !== 'ready' || companyId === null || actorId === null || requestId === undefined || inFlight.current) return
    const snapshot = retry ?? { actorId, companyId, requestId, expectedVersion: state.data.request.version, idempotencyKey: globalThis.crypto.randomUUID() }
    if (snapshot.actorId !== actorId || snapshot.companyId !== companyId || snapshot.requestId !== requestId) return
    inFlight.current = true; const generation = ++responseGeneration.current; reservePendingPurchaseRequestSubmission(storeKey, snapshot); setAttempt({ phase: 'submitting', snapshot })
    const security = boundary.capture(); const result = await submitPurchaseRequest(companyId, requestId, snapshot.expectedVersion, snapshot.idempotencyKey)
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
    if (result.type === 'unknown') { setAttempt({ phase: 'unknown', snapshot, outcome: result }); return }
    releasePendingPurchaseRequestSubmission(storeKey, snapshot.idempotencyKey)
    setAttempt(result.type === 'applied' ? { phase: 'applied', replayed: result.replayed } : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied' || result.status === 409) await query.refetch()
  }, [actorId, boundary, companyId, query, requestId, state, storeKey])

  const frame = (child: ReactNode) => <main className={styles.page}><header><p className={styles.eyebrow}>{T.eyebrow}</p><h1>{T.heading}</h1></header>{child}</main>
  if (companyId === null) return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />)
  if (state.kind !== 'ready') return frame(<StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />)
  const { request, lines, approval, rejection, withdrawal, preparedBy, submittedBy } = state.data; const unresolved = attempt.phase === 'unknown' || attempt.phase === 'submitting'
  const statusClass = request.status === 'Draft' ? styles.draft : request.status === 'Rejected' || request.status === 'Withdrawn' ? styles.rejected : styles.approved
  const withdrawalFacts = withdrawal ? <><div><dt>{REQUEST_WITHDRAWAL_TEXT.withdrawalFacts}</dt><dd>{withdrawal.decidedBy} · {new Date(withdrawal.decidedAtUtc).toLocaleString('tr-TR')}</dd></div><div><dt>{REQUEST_WITHDRAWAL_TEXT.reasonFacts}</dt><dd>{withdrawal.reason}</dd></div></> : null
  const decisionFacts = <>{approval ? <div><dt>{REQUEST_DECISION_TEXT.approvalFacts}</dt><dd>{approval.decidedBy} · {new Date(approval.decidedAtUtc).toLocaleString('tr-TR')}</dd></div> : null}{rejection ? <><div><dt>{REQUEST_DECISION_TEXT.rejectionFacts}</dt><dd>{rejection.decidedBy} · {new Date(rejection.decidedAtUtc).toLocaleString('tr-TR')}</dd></div><div><dt>{REQUEST_DECISION_TEXT.reasonFacts}</dt><dd>{rejection.reason}</dd></div></> : null}</>
  const decision = canDecide && actorId !== null && request.id === requestId && request.companyId === companyId
    ? <PurchaseRequestDecisionPanel sessionEpoch={shell.sessionEpoch} fenceKey={`${namespace ?? ''}::${shell.routeEntryGeneration}`} actorId={actorId}
      companyId={companyId} request={request} onSettled={() => query.refetch()} />
    : null
  const isMaker = actorId !== null && ((preparedBy ?? null) === actorId || (submittedBy ?? null) === actorId)
  const withdrawalControl = canWithdraw && actorId !== null && isMaker
    && request.id === requestId && request.companyId === companyId
    ? <PurchaseRequestWithdrawalPanel sessionEpoch={shell.sessionEpoch} fenceKey={`${namespace ?? ''}::${shell.routeEntryGeneration}`} actorId={actorId}
      companyId={companyId} request={request} onSettled={() => query.refetch()} />
    : null
  const notice = attempt.phase === 'applied' ? <p role="status" className={styles.success}>{attempt.replayed ? T.replayed : T.submitted}</p> : attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{T.notSubmitted(attempt.code ?? attempt.status)}</p> : null
  const showOrderReturn = returnOrderId !== null && namespace !== null && request.id === requestId && request.companyId === companyId
    && hasCapability(shell, PURCHASING_CAPABILITY.requestDetail) && hasCapability(shell, PURCHASING_CAPABILITY.orderDetail)
  return frame(<>{showOrderReturn && returnOrderId !== null ? <nav aria-label={T.returnNav}><Link to={purchaseOrderDetailPath(returnOrderId)} state={null}>{T.returnLink}</Link></nav> : null}<dl className={styles.facts}><div><dt>{T.request}</dt><dd className={styles.mono}>{request.id}</dd></div><div><dt>{T.status}</dt><dd><span className={statusClass}>{PURCHASE_REQUEST_STATUS_LABEL[request.status]}</span></dd></div><div><dt>{T.version}</dt><dd>{request.version}</dd></div><div><dt>{T.businessDate}</dt><dd>{request.effectiveOn}</dd></div>{decisionFacts}{withdrawalFacts}</dl><section><h2>{T.linesHeading}</h2><div className={styles.tableWrap}><table><thead><tr><th>{T.lineNo}</th><th>{T.product}</th><th>{T.unit}</th><th>{T.quantity}</th><th>{T.source}</th></tr></thead><tbody>{lines.map((line) => <tr key={line.lineId}><td>{line.lineNo}</td><td className={styles.mono}>…{line.productId.slice(-8)}</td><td className={styles.mono}>…{line.unitId.slice(-8)}</td><td>{line.quantity}</td><td>{sourceCell(line.source)}</td></tr>)}</tbody></table></div></section>{canSubmit ? notice : null}{canSubmit && (request.status === 'Draft' || unresolved) ? <section className={styles.actionCard}><h2>{T.submitHeading}</h2><p>{T.submitNote}</p>{attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>{T.unknown}</p> : null}<Button type="button" variant="primary" disabled={attempt.phase === 'submitting'} onClick={() => void send(attempt.phase === 'unknown' ? attempt.snapshot : undefined)}>{attempt.phase === 'submitting' ? T.sending : attempt.phase === 'unknown' ? T.retry : T.submit}</Button></section> : null}{withdrawalControl}{decision}</>)
}
