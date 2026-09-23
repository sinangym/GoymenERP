import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchPlan } from './api'
import { orderListPath } from './capabilities'
import { TARGET_BASIS_LABEL } from './manufacturingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './PlanPage.module.css'

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
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'manufacturing-plan', planId],
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

  return (
    <main className={styles.page} aria-labelledby="manufacturing-plan-heading">
      <header className={styles.header}>
        <div>
          <p className={styles.eyebrow}>ÜRETİM PLANI</p>
          <h1 id="manufacturing-plan-heading">Plan yeniden açıldı</h1>
        </div>
        <Link to={orderListPath()}>← Üretim emirleri</Link>
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
            <thead><tr><th scope="col">Ana rulo etiketi</th><th scope="col">Planlanan kg</th></tr></thead>
            <tbody>
              {plan.inputs.map((input) => (
                <tr key={input.parentRollId}>
                  <th scope="row" className={styles.mono}>{input.parentRollId}</th>
                  <td>{input.plannedKg}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
      {plan.status === 'Approved' ? (
        <p className={styles.note}>Onaylı plan bu ekranda salt okunurdur.</p>
      ) : null}
    </main>
  )
}
