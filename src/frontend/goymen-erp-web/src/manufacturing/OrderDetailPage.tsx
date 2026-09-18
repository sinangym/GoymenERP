/**
 * One production order: each parent roll's mass balance, and — for a holder of the
 * facts capability — the four fact forms (MFG-UI card, coordinator-approved).
 *
 * A technical, functional sub-view (card D1): staged on the server until the browser
 * suite accepts it, and not a locked visual reference for the canonical production
 * screens (PR-09/13/14/15 are `DECISION_ONLY_REFERENCE_MISSING`).
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - The table is always the server's READ. A command's 200 body is the order as that
 *    command first saw it, and a replay repeats it verbatim; so after an accepted fact
 *    the order is re-read, and nothing is predicted locally (no `version + 1`, no sums).
 *  - Kilograms stay text end to end (Ö2). The client checks only the plain-decimal
 *    grammar; the company's kilogram profile decides the scale on the server, which
 *    refuses — never rounds — more decimals.
 *  - The unexplained difference is shown with its sign and is never called fire; the
 *    cost is a status ("Hesaplanmadı"), never 0.
 *  - A fact whose outcome is unknown freezes the forms until it is re-sent unchanged
 *    (./factAttempt.ts, ./pendingFactStore.ts).
 *  - Without `manufacturing.orders.facts` no form is drawn (card D3); the server
 *    enforces either way.
 */

import { useCallback, useEffect, useReducer, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { fetchOrder } from './api'
import { recordFact } from './commandApi'
import {
  FACT_KINDS,
  OUTPUT_CLASSES,
  isKilogramText,
  type FactKind,
  type FactSnapshot,
  type OutputClass,
  type ProductionOrderView,
  type RollBalance,
} from './contract'
import {
  factAttemptReducer,
  initialFactAttempt,
  isUnresolved,
  restoredFactAttempt,
  type FactAttempt,
} from './factAttempt'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import {
  pendingFactKey,
  recallPendingFact,
  releasePendingFact,
  reservePendingFact,
} from './pendingFactStore'
import {
  APPLIED_DETAIL,
  APPLIED_TITLE,
  CLASS_REQUIRED,
  CLOSED_ORDER_TEXT,
  COST_STATUS_TEXT,
  FACT_KIND_HELP,
  FACT_KIND_LABEL,
  KG_FORMAT_ERROR,
  KG_FORMAT_HELP,
  ORDER_STATUS_LABEL,
  OUTPUT_CLASS_LABEL,
  REMAINING_UNAVAILABLE,
  REPLAYED_DETAIL,
  REPLAYED_TITLE,
  ROLL_REQUIRED,
  TARGET_BASIS_LABEL,
  TIME_REQUIRED,
  UNKNOWN_ACTION_HELP,
  failureText,
  unknownText,
} from './manufacturingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './OrderDetailPage.module.css'

/** Registered with the shell so a company switch with an unsent fact warns first. */
const DIRTY_SCOPE = 'manufacturing.order.fact'

function newId(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  // Fallback only where randomUUID is missing; still a v4-shaped id the server accepts.
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16)
  const s = hex.join('')
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-${s.slice(12, 16)}-${s.slice(16, 20)}-${s.slice(20)}`
}

export function OrderDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { orderId } = useParams()
  // Remounted per context AND per order: a stale render must never put one company's
  // order, or a frozen expectedVersion, under another context.
  return (
    <OrderDetail key={`${namespace ?? 'no-context'}::${orderId ?? ''}`} namespace={namespace} />
  )
}

function OrderDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const { orderId } = useParams()
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const singleCompanyContext =
    (shell.activeContext?.scopeMode ?? 'single') === 'single' && activeCompanyId !== null
  const canRecord = hasCapability(shell, MANUFACTURING_CAPABILITY.orderFacts)
  const storeKey = pendingFactKey(
    shell.sessionEpoch,
    shell.activeContext?.userId ?? null,
    activeCompanyId,
    orderId ?? '',
  )

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'manufacturing-order', orderId ?? ''],
    enabled: namespace !== null && singleCompanyContext && (orderId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchOrder(activeCompanyId ?? '', orderId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const refetch = query.refetch
  const refresh = useCallback(async () => {
    await refetch()
  }, [refetch])

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<ProductionOrderView>

  if (!singleCompanyContext) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="üretim emri için tek şirket bağlamı" />
      </Frame>
    )
  }

  if (state.kind !== 'ready') {
    return (
      <Frame>
        <StateBlock state={state} subject="üretim emri" onRetry={() => void query.refetch()} />
        {recallPendingFact(storeKey) !== null ? (
          <div className={styles.noticeWarn} role="alert" data-outcome="outstanding">
            <p className={styles.noticeTitle}>Sonucu bilinmeyen bir olgunuz var</p>
            <p>
              Gönderdiğiniz olgunun sonucu hâlâ belirsiz ve emir şu an okunamıyor. Olgu kendiliğinden
              tekrarlanmadı. Emir yeniden okunduğunda aynı işlem anahtarıyla devam edebilirsiniz.
            </p>
          </div>
        ) : null}
      </Frame>
    )
  }

  const order = state.data
  return (
    <Frame>
      <OrderSummary order={order} />
      <RollTable rolls={order.rolls} />
      {canRecord ? (
        <FactSection
          order={order}
          companyId={activeCompanyId ?? ''}
          storeKey={storeKey}
          refreshing={query.isFetching}
          onRefresh={refresh}
        />
      ) : null}
    </Frame>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="mfg-order-heading" className={styles.page}>
      <h1 id="mfg-order-heading">Üretim emri</h1>
      {children}
    </section>
  )
}

function OrderSummary({ order }: { readonly order: ProductionOrderView }) {
  const remaining = order.remainingTarget
  return (
    <dl className={styles.card} aria-label="Emir özeti">
      <div className={styles.field}>
        <dt>Emir</dt>
        <dd className={styles.mono}>{order.orderId}</dd>
      </div>
      <div className={styles.field}>
        <dt>Durum</dt>
        <dd>
          <span
            className={order.status === 'Open' ? styles.badgeOpen : styles.badgeClosed}
            data-status={order.status}
          >
            {ORDER_STATUS_LABEL[order.status]}
          </span>
        </dd>
      </div>
      <div className={styles.field}>
        <dt>Plan</dt>
        <dd className={styles.mono}>{order.planId}</dd>
      </div>
      <div className={styles.field}>
        <dt>İş merkezi</dt>
        <dd className={styles.mono}>{order.workCenterId}</dd>
      </div>
      <div className={styles.field}>
        <dt>Hedef</dt>
        <dd>
          {order.target === null
            ? 'Beyan edilmedi'
            : `${TARGET_BASIS_LABEL[order.target.basis] ?? order.target.basis}: ${order.target.kg} kg`}
        </dd>
      </div>
      <div className={styles.field}>
        <dt>Kalan hedef</dt>
        <dd data-testid="remaining-target">
          {remaining.basis === null
            ? REMAINING_UNAVAILABLE
            : `${remaining.remainingKg} kg (gerçekleşen ${remaining.achievedKg} / ${remaining.targetKg} kg)`}
        </dd>
      </div>
      <div className={styles.field}>
        <dt>Maliyet</dt>
        <dd data-testid="cost-status">{COST_STATUS_TEXT}</dd>
      </div>
    </dl>
  )
}

function RollTable({ rolls }: { readonly rolls: readonly RollBalance[] }) {
  return (
    <section aria-labelledby="mfg-rolls-heading" className={styles.section}>
      <h2 id="mfg-rolls-heading">Ana rulo dengesi (kg)</h2>
      <p className={styles.help}>
        Her rulo kendi başına mutabık olur: çıkan − iade = çıktılar + WIP + açıklanamayan fark.
      </p>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Ana rulo</th>
              <th scope="col">Planlanan</th>
              <th scope="col">Çıkan</th>
              <th scope="col">İade</th>
              <th scope="col">Net çıkan</th>
              {OUTPUT_CLASSES.map((outputClass) => (
                <th scope="col" key={outputClass}>
                  {OUTPUT_CLASS_LABEL[outputClass]}
                </th>
              ))}
              <th scope="col">WIP</th>
              <th scope="col">Açıklanamayan fark</th>
              <th scope="col">Mutabakat</th>
            </tr>
          </thead>
          <tbody>
            {rolls.map((roll) => (
              <tr key={roll.parentRollId} data-roll={roll.parentRollId}>
                <th scope="row" className={styles.mono}>
                  {roll.parentRollId}
                </th>
                <td>{roll.plannedKg}</td>
                <td>{roll.issuedKg}</td>
                <td>{roll.returnedKg}</td>
                <td>{roll.netIssuedKg}</td>
                {OUTPUT_CLASSES.map((outputClass) => (
                  <td key={outputClass}>{roll.outputsKg[outputClass]}</td>
                ))}
                <td>{roll.workInProgressKg}</td>
                <td data-testid="unexplained">{roll.unexplainedKg}</td>
                <td>
                  <span
                    className={roll.isFullyAccounted ? styles.badgeBalanced : styles.badgeDifference}
                    data-accounted={roll.isFullyAccounted ? 'yes' : 'no'}
                  >
                    {roll.isFullyAccounted ? 'Mutabık' : 'Fark var'}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

interface FormValues {
  readonly kind: FactKind
  readonly parentRollId: string
  readonly kg: string
  /** `datetime-local` value, read as the browser's local time and sent as UTC. */
  readonly occurredAt: string
  readonly outputClass: OutputClass | ''
}

const EMPTY_FORM: FormValues = {
  kind: 'input-issues',
  parentRollId: '',
  kg: '',
  occurredAt: '',
  outputClass: '',
}

function formFromSnapshot(snapshot: FactSnapshot): FormValues {
  const at = new Date(snapshot.occurredAtUtc)
  const local = new Date(at.getTime() - at.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
  return {
    kind: snapshot.kind,
    parentRollId: snapshot.parentRollId,
    kg: snapshot.kg,
    occurredAt: local,
    outputClass: snapshot.outputClass ?? '',
  }
}

function restored(storeKey: string | null): FactAttempt {
  const pending = recallPendingFact(storeKey)
  return pending === null ? initialFactAttempt : restoredFactAttempt(pending.snapshot, pending.resends)
}

function FactSection({
  order,
  companyId,
  storeKey,
  refreshing,
  onRefresh,
}: {
  readonly order: ProductionOrderView
  readonly companyId: string
  readonly storeKey: string | null
  readonly refreshing: boolean
  readonly onRefresh: () => Promise<void>
}) {
  const shellDispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const [attempt, dispatch] = useReducer(factAttemptReducer, storeKey, restored)
  // A restored snapshot shows the values it was sent with, not an empty form.
  const [form, setForm] = useState<FormValues>(() => {
    const pending = recallPendingFact(storeKey)
    return pending === null ? EMPTY_FORM : formFromSnapshot(pending.snapshot)
  })
  const [error, setError] = useState<string | null>(null)
  const noticeRef = useRef<HTMLDivElement>(null)

  const unresolved = isUnresolved(attempt)
  const busy = attempt.phase === 'submitting'
  const closed = order.status === 'Closed'

  const dirty = unresolved || form.kg.length > 0 || form.parentRollId.length > 0 || form.occurredAt.length > 0
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: DIRTY_SCOPE }), [shellDispatch])

  // Settled snapshots release exactly their own slot.
  useEffect(() => {
    if (attempt.phase === 'applied' || attempt.phase === 'failed') {
      releasePendingFact(storeKey, attempt.snapshot.idempotencyKey)
    } else if (attempt.phase === 'unknown') {
      reservePendingFact(storeKey, { snapshot: attempt.snapshot, resends: attempt.resends })
    }
  }, [attempt, storeKey])

  const phase = attempt.phase
  useEffect(() => {
    if (phase === 'applied' || phase === 'failed' || phase === 'unknown') noticeRef.current?.focus()
  }, [phase])

  const send = useCallback(
    async (snapshot: FactSnapshot, resends: number) => {
      // RESERVE FIRST, synchronously, before the request can be in flight.
      reservePendingFact(storeKey, { snapshot, resends })
      const security = boundary.capture()
      const outcome = await recordFact(companyId, order.orderId, snapshot)
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }
      dispatch({ type: 'SETTLE', outcome, idempotencyKey: snapshot.idempotencyKey })
      if (outcome.type === 'applied') {
        setForm((current) => ({ ...current, kg: '' }))
        await onRefresh()
      } else if (
        outcome.type === 'failed' &&
        (outcome.failure.kind === 'version-conflict' || outcome.failure.kind === 'concurrent-change')
      ) {
        // The order moved on: read it again; the typed values stay for a new submit.
        await onRefresh()
      }
    },
    [boundary, companyId, onRefresh, order.orderId, storeKey],
  )

  const submit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault()
      if (busy || unresolved || closed) return
      // A submit refused here mints NO id and NO key: nothing was attempted.
      if (form.parentRollId.length === 0) return setError(ROLL_REQUIRED)
      if (!isKilogramText(form.kg)) return setError(KG_FORMAT_ERROR)
      if (form.kind === 'outputs' && form.outputClass === '') return setError(CLASS_REQUIRED)
      const at = form.occurredAt.length === 0 ? Number.NaN : new Date(form.occurredAt).getTime()
      if (Number.isNaN(at)) return setError(TIME_REQUIRED)
      setError(null)
      const snapshot: FactSnapshot = {
        kind: form.kind,
        factId: newId(),
        idempotencyKey: newId(),
        expectedVersion: order.version,
        parentRollId: form.parentRollId,
        kg: form.kg,
        occurredAtUtc: new Date(at).toISOString(),
        outputClass: form.kind === 'outputs' ? (form.outputClass as OutputClass) : null,
      }
      dispatch({ type: 'SUBMIT', snapshot })
      void send(snapshot, 0)
    },
    [busy, closed, form, order.version, send, unresolved],
  )

  const resend = useCallback(() => {
    if (attempt.phase !== 'unknown') return
    const { snapshot, resends } = attempt
    dispatch({ type: 'RESEND' })
    void send(snapshot, resends + 1)
  }, [attempt, send])

  const frozen = busy || unresolved || closed
  const update = (patch: Partial<FormValues>) => {
    if (attempt.phase === 'applied' || attempt.phase === 'failed') dispatch({ type: 'RESET' })
    setForm((current) => ({ ...current, ...patch }))
  }

  return (
    <section aria-labelledby="mfg-facts-heading" className={styles.section}>
      <h2 id="mfg-facts-heading">Gerçekleşme kaydı</h2>
      {closed ? <p className={styles.help}>{CLOSED_ORDER_TEXT}</p> : null}

      <AttemptNotice attempt={attempt} refreshing={refreshing} noticeRef={noticeRef} />

      <form className={styles.section} onSubmit={submit} noValidate aria-describedby="mfg-fact-help">
        <fieldset className={styles.kinds} disabled={frozen}>
          <legend className={styles.help}>Olgu türü</legend>
          {FACT_KINDS.map((kind) => (
            <label key={kind}>
              <input
                type="radio"
                name="fact-kind"
                value={kind}
                checked={form.kind === kind}
                onChange={() => update({ kind })}
              />
              {FACT_KIND_LABEL[kind]}
            </label>
          ))}
        </fieldset>
        <p id="mfg-fact-help" className={styles.help}>
          {FACT_KIND_HELP[form.kind]}
        </p>
        <div className={styles.form}>
          <label>
            Ana rulo
            <select
              value={form.parentRollId}
              disabled={frozen}
              onChange={(event) => update({ parentRollId: event.target.value })}
            >
              <option value="">Seçin</option>
              {order.rolls.map((roll) => (
                <option key={roll.parentRollId} value={roll.parentRollId}>
                  {roll.parentRollId}
                </option>
              ))}
            </select>
          </label>
          {form.kind === 'outputs' ? (
            <label>
              Çıktı sınıfı
              <select
                value={form.outputClass}
                disabled={frozen}
                onChange={(event) => update({ outputClass: event.target.value as OutputClass | '' })}
              >
                <option value="">Seçin</option>
                {OUTPUT_CLASSES.map((outputClass) => (
                  <option key={outputClass} value={outputClass}>
                    {OUTPUT_CLASS_LABEL[outputClass]}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
          <label>
            Kütle (kg)
            <input
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={form.kg}
              disabled={frozen}
              aria-describedby="mfg-kg-help"
              onChange={(event) => update({ kg: event.target.value })}
            />
            <span id="mfg-kg-help" className={styles.help}>
              {KG_FORMAT_HELP}
            </span>
          </label>
          <label>
            Zaman
            <input
              type="datetime-local"
              value={form.occurredAt}
              disabled={frozen}
              onChange={(event) => update({ occurredAt: event.target.value })}
            />
          </label>
        </div>
        {error !== null ? (
          <p className={styles.error} role="alert">
            {error}
          </p>
        ) : null}
        <div className={styles.actions}>
          {attempt.phase === 'unknown' ? (
            <button type="button" onClick={resend}>
              Aynı olguyu yeniden gönder
            </button>
          ) : (
            <button type="submit" disabled={frozen}>
              {busy ? 'Gönderiliyor…' : 'Kaydet'}
            </button>
          )}
        </div>
      </form>
    </section>
  )
}

function AttemptNotice({
  attempt,
  refreshing,
  noticeRef,
}: {
  readonly attempt: FactAttempt
  readonly refreshing: boolean
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
}) {
  if (attempt.phase === 'applied') {
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeOk} role="status" data-outcome={attempt.replayed ? 'replayed' : 'applied'}>
        <p className={styles.noticeTitle}>{attempt.replayed ? REPLAYED_TITLE : APPLIED_TITLE}</p>
        <p>{attempt.replayed ? REPLAYED_DETAIL : APPLIED_DETAIL}</p>
        {refreshing ? <p>Emir yeniden okunuyor…</p> : null}
      </div>
    )
  }
  if (attempt.phase === 'failed') {
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeError} role="alert" data-outcome="failed" data-failure={attempt.failure.kind}>
        <p className={styles.noticeTitle}>Kaydedilmedi</p>
        <p>{failureText(attempt.failure)}</p>
      </div>
    )
  }
  if (attempt.phase === 'unknown') {
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeWarn} role="alert" data-outcome="unknown">
        <p className={styles.noticeTitle}>Sonuç bilinmiyor</p>
        <p>{unknownText(attempt.unknown)}</p>
        {attempt.lastRefusal !== null ? <p>Son yeniden gönderim reddedildi: {failureText(attempt.lastRefusal)}</p> : null}
        <p>{UNKNOWN_ACTION_HELP}</p>
      </div>
    )
  }
  return null
}
