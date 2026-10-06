import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { fetchPlan, fetchPlanRevisions } from './api'
import { MANUFACTURING_CAPABILITY, orderDetailPath, orderListPath, planListPath } from './capabilities'
import {
  approvePlan,
  createOrder,
  revisePlan,
  pendingPlanLifecycleKey,
  recallPendingPlanLifecycle,
  releasePendingPlanLifecycle,
  reservePendingPlanLifecycle,
  type PendingPlanLifecycle,
  type PlanLifecycleOutcome,
  type PlanLifecycleSnapshot,
  type RevisePlanRequest,
} from './planCommandApi'
import type { ProductionPlanView } from './contract'
import { TARGET_BASIS_LABEL } from './manufacturingText'
import { PLAN_EDIT_TEXT, revisionFailureText } from './planEditText'
import { currentRevision } from './planRevisionContract'
import { PlanEditForm } from './PlanEditForm'
import { PlanHistory } from './PlanHistory'
import { PlanCalculationDetails } from './PlanPreviewPanel'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './PlanPage.module.css'

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly pending: PendingPlanLifecycle }
  | { readonly phase: 'unknown'; readonly pending: PendingPlanLifecycle; readonly message: string }
  | { readonly phase: 'failed'; readonly message: string }
  | { readonly phase: 'refresh-needed'; readonly refresh: RefreshExpectation; readonly message: string }
  | { readonly phase: 'applied'; readonly message: string; readonly orderId?: string }

type RefreshExpectation =
  | { readonly kind: 'approved'; readonly approved: ProductionPlanView }
  | { readonly kind: 'revised'; readonly revised: ProductionPlanView }
  | { readonly kind: 'conflict'; readonly failure: string }

function newId(): string {
  const bytes = new Uint8Array(16)
  globalThis.crypto.getRandomValues(bytes)
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

function failureText(
  kind: PlanLifecycleSnapshot['kind'],
  outcome: Extract<PlanLifecycleOutcome<unknown>, { type: 'failed' }>,
  process: string,
): string {
  if (kind === 'revise') return revisionFailureText(outcome.status, outcome.code, outcome.field, process)
  const subject = kind === 'approve' ? 'Plan onayı' : 'Üretim emri'
  if (outcome.status === 401 || outcome.status === 403) return `${subject} yetkiniz artık geçerli değil.`
  if (outcome.status === 404) return `${subject} için kayıt bu şirket bağlamında bulunamadı.`
  if (outcome.status === 409) return `${subject} uygulanmadı (${outcome.code ?? 'conflict'}).`
  if (outcome.status === 422) return `${subject} iş kuralı tarafından reddedildi (${outcome.code ?? 'rule_refusal'}).`
  return `${subject} uygulanmadı (${outcome.code ?? outcome.status}).`
}

function unknownText(kind: PlanLifecycleSnapshot['kind']): string {
  if (kind === 'revise') return PLAN_EDIT_TEXT.unknown
  return `${kind === 'approve' ? 'Plan onayının' : 'Emir oluşturmanın'} sonucu bilinmiyor. Aynı istek ve anahtarla yeniden gönderin.`
}

export function PlanDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { planId = '' } = useParams<{ planId: string }>()
  return <PlanDetail key={`${namespace ?? 'no-context'}:${planId}`} namespace={namespace} planId={planId} />
}

function PlanDetail({ namespace, planId }: { readonly namespace: string | null; readonly planId: string }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const canApprove = hasCapability(shell, MANUFACTURING_CAPABILITY.planApprove)
  const canCreateOrder = hasCapability(shell, MANUFACTURING_CAPABILITY.planOrderCreate)
  // MFG-PLAN-EDIT: the Draft edit is its own control on the plan save's grant; the server decides again either way.
  const canEdit = hasCapability(shell, MANUFACTURING_CAPABILITY.planEdit)
  const canOpenOrder = hasCapability(shell, MANUFACTURING_CAPABILITY.orderDetail)
  // MFG-PLAN-LIST: back to the saved plans, only where the list itself is published to this actor.
  const canOpenPlanList = hasCapability(shell, MANUFACTURING_CAPABILITY.planList)
  const storeKey = pendingPlanLifecycleKey(shell.sessionEpoch, actorId, companyId, planId)
  const recalled = recallPendingPlanLifecycle(storeKey)
  const [attempt, setAttempt] = useState<Attempt>(() => recalled === null
    ? { phase: 'idle' }
    : { phase: 'unknown', pending: recalled, message: unknownText(recalled.snapshot.kind) })
  const [editing, setEditing] = useState(false)
  const generation = useRef(0)
  const inFlight = useRef(false)
  const queryKey = useMemo(
    () => [namespace ?? 'no-context', shell.routeEntryGeneration, 'manufacturing-plan', planId] as const,
    [namespace, planId, shell.routeEntryGeneration],
  )
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && planId !== '',
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPlan(companyId ?? '', planId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const plan = query.data?.type === 'ok-single' ? query.data.data : null
  // MFG-PLAN-EDIT: the plan's history, under the same read grant; its last entry is where an edit starts.
  const historyKey = useMemo(
    () => [namespace ?? 'no-context', shell.routeEntryGeneration, 'manufacturing-plan-revisions', planId] as const,
    [namespace, planId, shell.routeEntryGeneration],
  )
  const historyQuery = useQuery({
    queryKey: historyKey,
    enabled: namespace !== null && companyId !== null && planId !== '',
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPlanRevisions(companyId ?? '', planId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const history = historyQuery.data?.type === 'ok-single' ? historyQuery.data.data : null

  useEffect(() => () => {
    generation.current += 1
  }, [])

  const planProcess = plan?.process ?? ''
  const allowed = useCallback((kind: PlanLifecycleSnapshot['kind']) =>
    kind === 'approve' ? canApprove : kind === 'revise' ? canEdit : canCreateOrder,
  [canApprove, canCreateOrder, canEdit])

  const refreshPlan = useCallback(async (expectation: RefreshExpectation) => {
    const security = boundary.capture()
    const refreshed = await fetchPlan(companyId ?? '', planId)
    if (refreshed.type === 'http' && (refreshed.status === 401 || refreshed.status === 403)) {
      boundary.report(refreshed.status, security)
    }
    const fresh = refreshed.type === 'ok-single' ? refreshed.data : null
    const isCurrent = fresh?.planId === planId && fresh.companyId === companyId
    if (isCurrent && expectation.kind === 'approved' && fresh.status === 'Approved' && fresh.version === expectation.approved.version) {
      queryClient.setQueryData(queryKey, refreshed)
      setAttempt({ phase: 'applied', message: 'Plan onaylandı ve güncel kayıt yeniden okundu.' })
      return
    }
    if (isCurrent && expectation.kind === 'revised' && fresh.status === 'Draft' && fresh.version === expectation.revised.version) {
      // The history is read again too: the reopened plan shows the change and where it came from.
      queryClient.setQueryData(queryKey, refreshed)
      await queryClient.invalidateQueries({ queryKey: historyKey })
      setAttempt({ phase: 'applied', message: PLAN_EDIT_TEXT.applied(fresh.version) })
      return
    }
    if (isCurrent && expectation.kind === 'conflict') {
      queryClient.setQueryData(queryKey, refreshed)
      await queryClient.invalidateQueries({ queryKey: historyKey })
      setAttempt({ phase: 'failed', message: `${expectation.failure} Güncel plan yeniden okundu.` })
      return
    }
    setAttempt({
      phase: 'refresh-needed',
      refresh: expectation,
      message: expectation.kind === 'approved'
        ? 'Plan onaylandı; güncel kayıt henüz yeniden okunamadı.'
        : expectation.kind === 'revised'
          ? PLAN_EDIT_TEXT.appliedRefreshPending
          : `${expectation.failure} Güncel plan henüz yeniden okunamadı.`,
    })
  }, [boundary, companyId, historyKey, planId, queryClient, queryKey])

  const send = useCallback(async (pending: PendingPlanLifecycle) => {
    if (companyId === null || actorId === null || inFlight.current) return
    if (pending.companyId !== companyId || pending.actorId !== actorId || pending.planId !== planId) return
    if (!allowed(pending.snapshot.kind)) return
    inFlight.current = true
    const commandGeneration = ++generation.current
    reservePendingPlanLifecycle(storeKey, pending)
    setAttempt({ phase: 'sending', pending })
    const security = boundary.capture()
    const outcome = pending.snapshot.kind === 'approve'
      ? await approvePlan(companyId, planId, pending.snapshot)
      : pending.snapshot.kind === 'revise'
        ? await revisePlan(companyId, planId, pending.snapshot)
        : await createOrder(companyId, planId, pending.snapshot)
    if (commandGeneration !== generation.current) return
    inFlight.current = false
    if (outcome.type === 'failed' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, security)
    }
    if (outcome.type === 'unknown') {
      setAttempt({ phase: 'unknown', pending, message: unknownText(pending.snapshot.kind) })
      return
    }
    releasePendingPlanLifecycle(storeKey, pending.snapshot.idempotencyKey)
    if (outcome.type === 'failed') {
      const message = failureText(pending.snapshot.kind, outcome, planProcess)
      if (outcome.status === 409) {
        // A stale edit is not continued on stale content: the form closes and reopens from the re-read plan.
        if (pending.snapshot.kind === 'revise') setEditing(false)
        await refreshPlan({ kind: 'conflict', failure: message })
        return
      }
      setAttempt({ phase: 'failed', message })
      return
    }
    if (pending.snapshot.kind === 'approve') {
      await refreshPlan({ kind: 'approved', approved: outcome.resource as ProductionPlanView })
      return
    }
    if (pending.snapshot.kind === 'revise') {
      setEditing(false)
      await refreshPlan({ kind: 'revised', revised: outcome.resource as ProductionPlanView })
      return
    }
    const orderId = pending.snapshot.orderId
    if (canOpenOrder) {
      navigate(orderDetailPath(orderId))
      return
    }
    setAttempt({
      phase: 'applied',
      orderId,
      message: outcome.replayed
        ? 'Önceki emir oluşturma sonucu yeniden gösterildi.'
        : 'Üretim emri oluşturuldu.',
    })
  }, [actorId, allowed, boundary, canOpenOrder, companyId, navigate, planId, planProcess, refreshPlan, storeKey])

  const start = (snapshot: PlanLifecycleSnapshot) => {
    if (companyId === null || actorId === null) return
    void send({ actorId, companyId, planId, snapshot })
  }

  if (companyId === null || namespace === null) {
    return (
      <main className={styles.page}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="üretim planı için tek şirket bağlamı" />
      </main>
    )
  }
  if (plan === null) {
    const state: RemoteViewState<unknown> =
      query.data === undefined
        ? { kind: 'initial-loading' }
        : (classifyOutcome(query.data) as RemoteViewState<unknown>)
    return (
      <main className={styles.page}>
        <StateBlock state={state} subject="üretim planı" onRetry={() => void query.refetch()} />
      </main>
    )
  }

  const busy = attempt.phase === 'sending'
  const unresolved = attempt.phase === 'unknown' && allowed(attempt.pending.snapshot.kind)
  // The edit starts from the plan's current content: the last history entry, of exactly the version shown.
  const current = history === null ? null : currentRevision(history)
  const editable = plan.status === 'Draft' && canEdit
  const editReady = current !== null && current.planVersion === plan.version
  const startRevision = (request: RevisePlanRequest) =>
    start({ kind: 'revise', idempotencyKey: newId(), request })

  return (
    <main className={styles.page} aria-labelledby="manufacturing-plan-heading">
      <header className={styles.header}>
        <div>
          <p className={styles.eyebrow}>ÜRETİM PLANI</p>
          <h1 id="manufacturing-plan-heading">Plan yeniden açıldı</h1>
        </div>
        <div>
          {canOpenPlanList ? <Link to={planListPath()}>← Üretim planları</Link> : null}{' '}
          <Link to={orderListPath()}>← Üretim emirleri</Link>
        </div>
      </header>
      <dl className={styles.facts}>
        <div><dt>Plan</dt><dd className={styles.mono}>{plan.planId}</dd></div>
        <div><dt>Durum</dt><dd>{plan.status === 'Draft' ? 'Taslak' : 'Onaylı'}</dd></div>
        <div><dt>Sürüm</dt><dd>{plan.version}</dd></div>
        <div><dt>İş merkezi</dt><dd className={styles.mono}>{plan.workCenterId}</dd></div>
        <div><dt>Proses</dt><dd>{plan.process}</dd></div>
        <div>
          <dt>Hedef</dt>
          <dd>
            {plan.target === null
              ? 'Hedef yok'
              : `${TARGET_BASIS_LABEL[plan.target.basis] ?? plan.target.basis}: ${plan.target.kg} kg`}
          </dd>
        </div>
      </dl>
      <section aria-labelledby="manufacturing-plan-inputs-heading">
        <h2 id="manufacturing-plan-inputs-heading">Planlanan ana rulolar</h2>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead><tr><th scope="col">Ana rulo etiketi</th><th scope="col">Planlanan kg</th><th scope="col">Yakalanan teknik kaynak</th></tr></thead>
            <tbody>
              {plan.inputs.map((input) => (
                <tr key={input.parentRollId}>
                  <th scope="row" className={styles.mono}>{input.parentRollId}</th>
                  <td>{input.plannedKg}</td>
                  <td>
                    {input.technicalSnapshot === null ? (
                      <span className={styles.muted}>Yok — eski plan, güncel üründen tamamlanmaz</span>
                    ) : (
                      <ul>
                        <li>Ürün <span className={styles.mono}>{input.technicalSnapshot.productId}</span> · sürüm {input.technicalSnapshot.productVersion}</li>
                        <li>Aile {input.technicalSnapshot.family.familyCode} · {input.technicalSnapshot.family.sourceRevision}</li>
                        <li>Şablon {input.technicalSnapshot.template.templateCode} · {input.technicalSnapshot.template.templateVersion}</li>
                        <li>{input.technicalSnapshot.thicknessMm} mm × {input.technicalSnapshot.widthMm} mm</li>
                        <li>kg profili {input.technicalSnapshot.unitKg.profileVersion} · {input.technicalSnapshot.effectiveOn}</li>
                      </ul>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className={styles.commandCard} aria-labelledby="manufacturing-stored-plan-calculation-heading">
        <h2 id="manufacturing-stored-plan-calculation-heading">Kayıtlı plan hesaplaması</h2>
        {plan.calculation === null ? (
          <p className={styles.note}>Bu eski komut yanıtında kalıcı hesaplama projeksiyonu bulunmuyor.</p>
        ) : (
          <PlanCalculationDetails preview={plan.calculation} labelOf={(parentRollId) => parentRollId} />
        )}
      </section>

      {editing && editable && current !== null && editReady && !unresolved ? (
        <PlanEditForm
          key={plan.version}
          companyId={companyId}
          plan={plan}
          current={current}
          busy={busy}
          onSubmit={startRevision}
          onCancel={() => setEditing(false)}
        />
      ) : null}

      <section className={styles.commandCard} aria-labelledby="manufacturing-plan-next-heading">
        <h2 id="manufacturing-plan-next-heading">Sonraki işlem</h2>
        {unresolved ? (
          <>
            <p role="alert" className={styles.error}>{attempt.message}</p>
            <div className={styles.actions}>
              <button type="button" onClick={() => void send(attempt.pending)}>
                Aynı istek ve anahtarla yeniden gönder
              </button>
            </div>
          </>
        ) : attempt.phase === 'refresh-needed' ? (
          <>
            <p role="alert" className={styles.error}>{attempt.message}</p>
            <div className={styles.actions}>
              <button type="button" onClick={() => void refreshPlan(attempt.refresh)}>
                Güncel planı yeniden oku
              </button>
            </div>
          </>
        ) : (
          <>
            {attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}
            {attempt.phase === 'applied' ? (
              <p role="status" className={styles.success}>
                {attempt.message}{attempt.orderId === undefined ? '' : ` Emir: ${attempt.orderId}`}
              </p>
            ) : null}
            {editable && !editing ? (
              editReady ? (
                <div className={styles.actions}>
                  <button type="button" disabled={busy} onClick={() => setEditing(true)}>
                    {PLAN_EDIT_TEXT.editButton}
                  </button>
                </div>
              ) : (
                <p className={styles.note}>
                  {history === null ? PLAN_EDIT_TEXT.historyNotReady : PLAN_EDIT_TEXT.historyStale}
                </p>
              )
            ) : null}
            {plan.status === 'Approved' && canEdit ? <p className={styles.note}>{PLAN_EDIT_TEXT.approvedNote}</p> : null}
            {plan.status === 'Draft' && canApprove && !editing ? (
              <div className={styles.actions}>
                <button
                  type="button"
                  disabled={busy}
                  aria-busy={busy}
                  onClick={() => start({ kind: 'approve', idempotencyKey: newId(), expectedVersion: plan.version })}
                >
                  {busy ? 'Plan onaylanıyor…' : 'Planı onayla'}
                </button>
              </div>
            ) : null}
            {plan.status === 'Approved' && canCreateOrder ? (
              <div className={styles.actions}>
                <button
                  type="button"
                  disabled={busy}
                  aria-busy={busy}
                  onClick={() => start({ kind: 'create-order', idempotencyKey: newId(), orderId: newId() })}
                >
                  {busy ? 'Üretim emri oluşturuluyor…' : 'Üretim emri oluştur'}
                </button>
              </div>
            ) : null}
            {plan.status === 'Approved' && !canCreateOrder ? (
              <p className={styles.note}>Onaylı plan bu ekranda salt okunurdur.</p>
            ) : null}
            {plan.status === 'Draft' && !canApprove && !canEdit ? (
              <p className={styles.note}>Taslak plan bu ekranda salt okunurdur.</p>
            ) : null}
          </>
        )}
      </section>

      {history === null ? (
        historyQuery.data !== undefined && historyQuery.data.type !== 'ok-single' ? (
          <p className={styles.muted} role="status">Plan geçmişi okunamadı.</p>
        ) : null
      ) : <PlanHistory history={history} />}
    </main>
  )
}
