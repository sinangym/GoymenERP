import { useEffect, useId, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftRemainderChanges, sendRemainderChangeDecision, sendRemainderChangeRequest } from './api'
import { APPROVAL_STATE, isTerminalState, type DraftDetail, type DraftMutation } from './contract'
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
import styles from './SalesPages.module.css'

const TEXT = {
  heading: 'Kısmi teslimat kalanı',
  note: 'Teslim edilen ve yüklenen bölüm korunur. Yalnız kalan miktar onayla iptal edilir veya revize edilir.',
  unreadable: 'Lojistik miktarları şu anda okunamıyor; sıfır varsayılmadı ve değişiklik gönderilemez.',
  empty: 'Kalan değişikliği kaydı yok.',
  line: 'Sipariş satırı', kind: 'İşlem', cancel: 'Kalanı iptal et', revise: 'Kalan miktarı revize et',
  quantity: 'Yeni sipariş miktarı', reason: 'Gerekçe', request: 'Onaya gönder',
  approve: 'Onayla', reject: 'Reddet', decisionReason: 'Ret nedeni',
  retry: 'Aynı işlemi tekrar dene', submitting: 'İşlem kaydediliyor…', completed: 'İşlem kaydedildi; güncel sipariş yeniden okunuyor.',
  unknown: 'Sonuç alınamadı; kayıt yapılmış olabilir. Aynı anahtarla tekrar deneyin.',
  failed: 'İşlem uygulanmadı.', invalid: 'Satır, miktar ve gerekçeyi kontrol edin.',
  authorityLost: 'Bu işlem için güncel yetki bulunmuyor; kayıt gönderilmedi.',
  stateUnavailable: 'Yalnız onaylı ve iptal edilmemiş siparişin kısmi teslimat kalanı değiştirilebilir.',
} as const

export function SalesDraftRemainderChangePanel({
  detail, companyId, namespace, actorId, routeEntryGeneration, attemptBinding, canRequest, canDecide, onRecorded,
}: {
  readonly detail: DraftDetail
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
    setNotice(`${TEXT.failed}${outcome.type === 'http' && outcome.problem?.code ? ` (${outcome.problem.code})` : ''}`)
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
          {selected === null ? null : <p className={styles.muted}>Yüklenen: {selected.loaded ?? 'okunamadı'} · Sevk: {selected.shipped ?? 'okunamadı'} · Teslim: {selected.delivered ?? 'okunamadı'} · Alt sınır: {selected.minimumQuantity ?? 'okunamadı'}</p>}
          {canRequest ? (
            <div className={styles.actions} data-testid="sales-remainder-request">
              <label htmlFor="sales-remainder-line">{TEXT.line}</label>
              <select id="sales-remainder-line" value={lineId} disabled={busy || pending !== null} onChange={(event) => setLineId(event.target.value)}>
                {changes?.lines.map((line) => <option key={line.lineId} value={line.lineId}>{line.lineNo}. satır · {line.quantity}</option>)}
              </select>
              <label htmlFor="sales-remainder-kind">{TEXT.kind}</label>
              <select id="sales-remainder-kind" value={kind} disabled={busy || pending !== null} onChange={(event) => setKind(event.target.value as typeof kind)}>
                <option value={REMAINDER_KIND.cancel}>{TEXT.cancel}</option><option value={REMAINDER_KIND.revise}>{TEXT.revise}</option>
              </select>
              {kind === REMAINDER_KIND.revise ? <><label htmlFor="sales-remainder-quantity">{TEXT.quantity}</label><input id="sales-remainder-quantity" inputMode="decimal" value={quantity} disabled={busy || pending !== null} onChange={(event) => setQuantity(event.target.value)} /></> : null}
              <label htmlFor="sales-remainder-reason">{TEXT.reason}</label>
              <textarea id="sales-remainder-reason" maxLength={4000} value={reason} disabled={busy || pending !== null} onChange={(event) => setReason(event.target.value)} />
              <button type="button" className={styles.primaryButton} disabled={busy || pending !== null || !stateAllowsRequest} onClick={requestChange}>{TEXT.request}</button>
              {!stateAllowsRequest ? <p className={styles.muted}>{TEXT.stateUnavailable}</p> : null}
            </div>
          ) : null}
          {pending !== null && !busy ? <button type="button" className={styles.secondaryButton}
            disabled={pending.kind === 'request' ? !canRequest : !canDecide}
            onClick={() => void execute(pending)}>{TEXT.retry}</button> : null}
          {(changes?.items.length ?? 0) === 0 ? <p>{TEXT.empty}</p> : changes?.items.map((item) => (
            <article key={item.changeNo} className={styles.line} data-testid={`sales-remainder-change-${item.changeNo}`}>
              <h3>{item.changeNo}. değişiklik · {item.kind === REMAINDER_KIND.cancel ? TEXT.cancel : TEXT.revise}</h3>
              <p>{item.lineNo}. satır · {item.orderedQuantity} → {item.requestedQuantity ?? item.loadedAtRequest} · {item.state}</p>
              <p className={styles.muted}>{item.reason} · {item.requestedBy}</p>
              {item.state === REMAINDER_STATE.pending && canDecide ? <div className={styles.actions}>
                <label htmlFor={`sales-remainder-reject-${item.changeNo}`}>{TEXT.decisionReason}</label>
                <input id={`sales-remainder-reject-${item.changeNo}`} value={decisionReasons[item.changeNo] ?? ''} disabled={busy || pending !== null}
                  onChange={(event) => setDecisionReasons((current) => ({ ...current, [item.changeNo]: event.target.value }))} />
                <button type="button" className={styles.primaryButton} disabled={busy || pending !== null} onClick={() => decide(item.changeNo, 'approval')}>{TEXT.approve}</button>
                <button type="button" className={styles.secondaryButton} disabled={busy || pending !== null} onClick={() => decide(item.changeNo, 'rejection')}>{TEXT.reject}</button>
              </div> : null}
            </article>
          ))}
        </>
      ) : <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data)} subject={TEXT.heading} onRetry={() => void query.refetch()} />}
    </section>
  )
}
