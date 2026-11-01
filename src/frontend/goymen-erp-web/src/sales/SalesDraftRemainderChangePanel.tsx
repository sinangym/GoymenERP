import { useEffect, useId, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftRemainderChanges, sendRemainderChangeDecision, sendRemainderChangeRequest } from './api'
import { APPROVAL_STATE, isTerminalState, userDisplayFor, type DraftDetail, type DraftMutation, type UserDisplay } from './contract'
import { formatInstant } from './format'
import { orderUnitCodes } from './unitNames'
import { UnitName } from './UnitName'
import { generateOrderAttemptKey, type OrderCommandOutcome } from './orderAttempt'
import { isPositiveDecimalString } from './wirePrimitives'
import {
  LINE_REMAINDER,
  REMAINDER_FLOOR,
  REMAINDER_KIND,
  REMAINDER_STATE,
} from './remainderChange'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import {
  recallPendingRemainderAction,
  rememberPendingRemainderAction,
  settlePendingRemainderAction,
  subscribePendingRemainderAction,
  type PendingRemainderAction,
} from './pendingRemainderChanges'
import { REMAINDER_CHANGE_TEXT } from './salesText'
import styles from './SalesPages.module.css'
import { Button } from '../ui/Button'

const TEXT = REMAINDER_CHANGE_TEXT

export function SalesDraftRemainderChangePanel({
  detail, companyId, namespace, actorId, routeEntryGeneration, attemptBinding, canRequest, canDecide, onRecorded, userDisplays = [],
}: {
  readonly detail: DraftDetail
  /** MOD-08: the people the detail names (by the Identity directory); an id without an entry is shown as it is stored. */
  readonly userDisplays?: readonly UserDisplay[]
  readonly companyId: string
  readonly namespace: string
  readonly actorId: string
  readonly routeEntryGeneration: number
  readonly attemptBinding: string
  readonly canRequest: boolean
  readonly canDecide: boolean
  readonly onRecorded: () => void
}) {
  const boundary = useAuthBoundary()
  const headingId = useId()
  const [lineId, setLineId] = useState(detail.lines[0]?.lineId ?? '')
  const [kind, setKind] = useState<(typeof REMAINDER_KIND)[keyof typeof REMAINDER_KIND]>(REMAINDER_KIND.cancel)
  const [quantity, setQuantity] = useState('')
  const [reason, setReason] = useState('')
  const [decisionReasons, setDecisionReasons] = useState<Record<number, string>>({})
  const [pending, setPending] = useState<PendingRemainderAction | null>(() => recallPendingRemainderAction(attemptBinding))
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  useEffect(() => subscribePendingRemainderAction(attemptBinding, setPending), [attemptBinding])
  const query = useQuery({
    queryKey: [namespace, actorId, routeEntryGeneration, 'sales-remainder-changes', companyId, detail.draft.id, detail.draft.version],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftRemainderChanges(detail, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
    staleTime: 0, gcTime: 0, retry: false, refetchOnWindowFocus: false,
  })
  const changes = query.data?.type === 'ok-single' ? query.data.data : null
  const unitCodes = useMemo(() => orderUnitCodes(detail), [detail])
  const unitOf = (lineId: string) => changes?.lines.find((line) => line.lineId === lineId)?.orderUnitId
    ?? detail.lines.find((line) => line.lineId === lineId)?.orderUnitId ?? null
  const personName = (id: string | null) => {
    if (id === null) return null
    const person = userDisplayFor(userDisplays, id)
    return person === null ? id : person.isActiveInCompany ? person.displayName : TEXT.formerMember(person.displayName)
  }
  const selected = useMemo(() => changes?.lines.find((line) => line.lineId === lineId) ?? null, [changes, lineId])
  const stateAllowsRequest = !isTerminalState(detail.draft.state) && detail.draft.approvalState === APPROVAL_STATE.approved
    && detail.customerAcceptance.isCurrentRevisionAccepted
    && changes?.floorStatus === REMAINDER_FLOOR.readable && selected?.remainder !== LINE_REMAINDER.cancelled

  async function execute(action: PendingRemainderAction) {
    const permitted = action.kind === 'request' ? canRequest : canDecide
    if (!permitted) {
      setNotice(TEXT.authorityLost)
      return
    }
    setBusy(true)
    setNotice(TEXT.submitting)
    const snapshot = boundary.capture()
    const context = { idempotencyKey: action.key, companyId, draftId: detail.draft.id }
    const outcome = action.kind === 'request'
      ? await sendRemainderChangeRequest(action.payload, context)
      : await sendRemainderChangeDecision(action.changeNo, action.kind, action.payload, context)
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    finish(action, outcome)
  }

  function finish(action: PendingRemainderAction, outcome: OrderCommandOutcome<DraftMutation>) {
    setBusy(false)
    if (outcome.type === 'succeeded') {
      settlePendingRemainderAction(attemptBinding, action.key)
      setPending(null)
      setNotice(TEXT.completed)
      setReason('')
      setQuantity('')
      onRecorded()
      return
    }
    if (outcome.type === 'offline' || outcome.type === 'network-error' || outcome.type === 'aborted' || outcome.type === 'malformed') {
      const remembered = recallPendingRemainderAction(attemptBinding)
      setPending(remembered?.key === action.key ? remembered : null)
      if (remembered?.key === action.key) setNotice(TEXT.unknown)
      return
    }
    settlePendingRemainderAction(attemptBinding, action.key)
    setPending(null)
    setNotice(failureNotice(outcome))
    if (outcome.type === 'http' && outcome.status === 409) onRecorded()
  }

  function requestChange() {
    if (selected === null || reason.trim() === '' || (kind === REMAINDER_KIND.revise && !isPositiveDecimalString(quantity.trim()))) {
      setNotice(TEXT.invalid)
      return
    }
    const action: PendingRemainderAction = {
      kind: 'request', key: generateOrderAttemptKey(),
      payload: { expectedVersion: detail.draft.version, lineId: selected.lineId, kind,
        quantity: kind === REMAINDER_KIND.cancel ? null : quantity.trim(), reason },
    }
    const reserved = rememberPendingRemainderAction(attemptBinding, action)
    setPending(reserved)
    if (reserved.key !== action.key) {
      setNotice(TEXT.unknown)
      return
    }
    void execute(reserved)
  }

  function decide(changeNo: number, decision: 'approval' | 'rejection') {
    const action: PendingRemainderAction = {
      kind: decision, key: generateOrderAttemptKey(), changeNo,
      payload: { expectedVersion: detail.draft.version, reason: decision === 'rejection' ? (decisionReasons[changeNo] ?? '') || null : null },
    }
    const reserved = rememberPendingRemainderAction(attemptBinding, action)
    setPending(reserved)
    if (reserved.key !== action.key) {
      setNotice(TEXT.unknown)
      return
    }
    void execute(reserved)
  }

  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-remainder-changes">
      <h2 id={headingId}>{TEXT.heading}</h2>
      <p className={styles.muted}>{TEXT.note}</p>
      {query.data?.type === 'ok-single' ? (
        <>
          {changes?.floorStatus === REMAINDER_FLOOR.unreadable ? <p role="alert" className={styles.notice}>{TEXT.unreadable}</p> : null}
          {notice === null ? null : <p role="status" className={styles.notice}>{notice}</p>}
          {selected === null ? null : (
            <p className={styles.muted} data-testid="sales-remainder-line-facts">
              {TEXT.lineFacts(selected, unitCodes.get(selected.orderUnitId) ?? selected.orderUnitId)}
            </p>
          )}
          {canRequest ? (
            <div className={styles.actions} data-testid="sales-remainder-request">
              <label htmlFor="sales-remainder-line">{TEXT.line}</label>
              <select id="sales-remainder-line" value={lineId} disabled={busy || pending !== null} onChange={(event) => setLineId(event.target.value)}>
                {changes?.lines.map((line) => (
                  <option key={line.lineId} value={line.lineId}>
                    {TEXT.lineOption(line.lineNo, line.quantity, unitCodes.get(line.orderUnitId) ?? line.orderUnitId)}
                  </option>
                ))}
              </select>
              <label htmlFor="sales-remainder-kind">{TEXT.kind}</label>
              <select id="sales-remainder-kind" value={kind} disabled={busy || pending !== null} onChange={(event) => setKind(event.target.value as typeof kind)}>
                <option value={REMAINDER_KIND.cancel}>{TEXT.cancel}</option><option value={REMAINDER_KIND.revise}>{TEXT.revise}</option>
              </select>
              {kind === REMAINDER_KIND.revise ? <><label htmlFor="sales-remainder-quantity">{TEXT.quantity}</label><input id="sales-remainder-quantity" inputMode="decimal" value={quantity} disabled={busy || pending !== null} onChange={(event) => setQuantity(event.target.value)} /></> : null}
              <label htmlFor="sales-remainder-reason">{TEXT.reason}</label>
              <textarea id="sales-remainder-reason" maxLength={4000} value={reason} disabled={busy || pending !== null} onChange={(event) => setReason(event.target.value)} />
              <Button type="button" variant="primary" disabled={busy || pending !== null || !stateAllowsRequest} onClick={requestChange}>{TEXT.request}</Button>
              {!stateAllowsRequest ? <p className={styles.muted}>{TEXT.stateUnavailable}</p> : null}
            </div>
          ) : null}
          {pending !== null && !busy ? <Button type="button" variant="secondary"
            disabled={pending.kind === 'request' ? !canRequest : !canDecide}
            onClick={() => void execute(pending)}>{TEXT.retry}</Button> : null}
          {(changes?.items.length ?? 0) === 0 ? <p>{TEXT.empty}</p> : changes?.items.map((item) => {
            const unit = unitOf(item.lineId)
            const withUnit = (quantity: string) => unit === null ? quantity : <>{quantity} <UnitName id={unit} unitCodes={unitCodes} /></>
            const decidedBy = personName(item.decidedBy)
            return (
            <article key={item.changeNo} className={styles.line} data-testid={`sales-remainder-change-${item.changeNo}`}>
              <h3>{TEXT.changeHeading(item.changeNo)} · {item.kind === REMAINDER_KIND.cancel ? TEXT.cancel : TEXT.revise}</h3>
              <p data-testid={`sales-remainder-change-${item.changeNo}-facts`}>
                {TEXT.lineNo(item.lineNo)} · {withUnit(item.orderedQuantity)} → {withUnit(item.requestedQuantity ?? item.loadedAtRequest)}
                {' · '}<span data-testid={`sales-remainder-change-${item.changeNo}-state`}>{TEXT.state[item.state]}</span>
              </p>
              <p className={styles.muted} data-testid={`sales-remainder-change-${item.changeNo}-requested`}>
                {item.reason} · {TEXT.requestedBy(personName(item.requestedBy) ?? item.requestedBy, formatInstant(item.requestedAtUtc))}
              </p>
              {decidedBy === null || item.decidedAtUtc === null ? null : (
                <p className={styles.muted} data-testid={`sales-remainder-change-${item.changeNo}-decided`}>
                  {TEXT.decidedBy(decidedBy, formatInstant(item.decidedAtUtc))}
                  {item.decisionReason === null ? null : ` · ${item.decisionReason}`}
                </p>
              )}
              {item.state === REMAINDER_STATE.pending && canDecide ? <div className={styles.actions}>
                <label htmlFor={`sales-remainder-reject-${item.changeNo}`}>{TEXT.decisionReason}</label>
                <input id={`sales-remainder-reject-${item.changeNo}`} value={decisionReasons[item.changeNo] ?? ''} disabled={busy || pending !== null}
                  onChange={(event) => setDecisionReasons((current) => ({ ...current, [item.changeNo]: event.target.value }))} />
                <Button type="button" variant="primary" disabled={busy || pending !== null} onClick={() => decide(item.changeNo, 'approval')}>{TEXT.approve}</Button>
                <Button type="button" variant="secondary" disabled={busy || pending !== null} onClick={() => decide(item.changeNo, 'rejection')}>{TEXT.reject}</Button>
              </div> : null}
            </article>
            )
          })}
        </>
      ) : <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data)} subject={TEXT.heading} onRetry={() => void query.refetch()} />}
    </section>
  )
}

/** A refused command in words: the authority, the stale version and the unavailable owner each say what happened. */
function failureNotice(outcome: OrderCommandOutcome<DraftMutation>): string {
  if (outcome.type !== 'http') return TEXT.failed
  if (outcome.status === 401) return TEXT.unauthenticated
  if (outcome.status === 403) return TEXT.forbidden
  if (outcome.status === 409) return TEXT.conflict
  if (outcome.status >= 500) return TEXT.serviceUnavailable
  return outcome.problem?.code ? TEXT.failedWithCode(outcome.problem.code) : TEXT.failed
}
