import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchAccountPlan } from './accountPlanApi'
import { filterAccountPlan, type AccountPlanSource, type AccountPlanView } from './accountPlanContract'
import {
  ACCOUNT_PLAN_ACCOUNT_COUNT,
  ACCOUNT_PLAN_ACTIVATED_AT,
  ACCOUNT_PLAN_CATALOG_VERSION,
  ACCOUNT_PLAN_CODE,
  ACCOUNT_PLAN_COMPANY_SUBJECT,
  ACCOUNT_PLAN_FILTERED_SUBJECT,
  ACCOUNT_PLAN_INTRO,
  ACCOUNT_PLAN_NAME,
  ACCOUNT_PLAN_NO_ACTIVE_DETAIL,
  ACCOUNT_PLAN_NO_ACTIVE_TITLE,
  ACCOUNT_PLAN_SEARCH_LABEL,
  ACCOUNT_PLAN_SEARCH_PLACEHOLDER,
  ACCOUNT_PLAN_SHA256,
  ACCOUNT_PLAN_SHOWING,
  ACCOUNT_PLAN_SOURCE_TITLE,
  ACCOUNT_PLAN_SOURCE_VERSION,
  ACCOUNT_PLAN_SUBJECT,
  ACCOUNT_PLAN_TITLE,
  ACCOUNT_PLAN_UNKNOWN,
} from './accountingText'
import styles from './Accounting.module.css'

/** Presentation only: the wire instant stays in the title, unchanged. */
const formatInstant = (value: string) => {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

/**
 * M-05 Hesap Planı (FIN-S16): read-only. One request per company context and route entry; the search filters
 * the decoded list locally and never re-asks the server. Staged on the server until its acceptance, so the
 * `CapabilityRoute` gate shows the neutral not-found block until the route is published.
 */
export function AccountPlanPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <AccountPlan key={namespace ?? 'no-context'} namespace={namespace} />
}

function AccountPlan({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting-account-plan'],
    enabled: namespace !== null && singleCompany,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchAccountPlan(companyId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!singleCompany) {
    return <Frame><StateBlock state={{ kind: 'no-authorized-company' }} subject={ACCOUNT_PLAN_COMPANY_SUBJECT} /></Frame>
  }

  const state = (query.isPending
    ? { kind: 'initial-loading' }
    : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<AccountPlanView>

  return (
    <Frame>
      {state.kind !== 'ready'
        ? <StateBlock state={state} subject={ACCOUNT_PLAN_SUBJECT} onRetry={() => void query.refetch()} />
        : <AccountPlanResult view={state.data} />}
    </Frame>
  )
}

function AccountPlanResult({ view }: { readonly view: AccountPlanView }) {
  const [search, setSearch] = useState('')

  if (view.source === null) {
    return (
      <div role="status" data-state="empty-no-data">
        <p><strong>{ACCOUNT_PLAN_NO_ACTIVE_TITLE}</strong></p>
        <p className={styles.help}>{ACCOUNT_PLAN_NO_ACTIVE_DETAIL}</p>
      </div>
    )
  }

  const shown = filterAccountPlan(view.accounts, search)
  return (
    <>
      <SourceSummary source={view.source} />
      <form className={styles.form} role="search" onSubmit={(event) => event.preventDefault()}>
        <label className={styles.control}>
          <span>{ACCOUNT_PLAN_SEARCH_LABEL}</span>
          <input
            type="search"
            value={search}
            placeholder={ACCOUNT_PLAN_SEARCH_PLACEHOLDER}
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
      </form>
      {shown.length === 0
        ? <StateBlock state={{ kind: 'empty-filtered' }} subject={ACCOUNT_PLAN_FILTERED_SUBJECT} />
        : (
          <>
            <p className={styles.help} aria-live="polite">{ACCOUNT_PLAN_SHOWING(shown.length, view.accounts.length)}</p>
            <div className={styles.tableWrap}>
              <table aria-labelledby="accounting-account-plan-title" className={styles.table}>
                <thead><tr><th scope="col">{ACCOUNT_PLAN_CODE}</th><th scope="col">{ACCOUNT_PLAN_NAME}</th></tr></thead>
                <tbody>{shown.map((account) => (
                  <tr key={account.accountId}>
                    <td><span className={styles.mono}>{account.code}</span></td>
                    <td>{account.name}</td>
                  </tr>
                ))}</tbody>
              </table>
            </div>
          </>
        )}
    </>
  )
}

function SourceSummary({ source }: { readonly source: AccountPlanSource }) {
  return (
    <section aria-labelledby="accounting-account-plan-source">
      <h2 id="accounting-account-plan-source">{ACCOUNT_PLAN_SOURCE_TITLE}</h2>
      <dl className={styles.help}>
        <dt>{ACCOUNT_PLAN_SOURCE_VERSION}</dt>
        <dd>{source.accountPlanSourceVersion}</dd>
        <dt>{ACCOUNT_PLAN_CATALOG_VERSION}</dt>
        <dd>{source.catalogVersion === null ? ACCOUNT_PLAN_UNKNOWN : source.catalogVersion}</dd>
        <dt>{ACCOUNT_PLAN_SHA256}</dt>
        <dd><span className={styles.mono}>{source.accountsSourceSha256}</span></dd>
        <dt>{ACCOUNT_PLAN_ACTIVATED_AT}</dt>
        <dd>
          {source.activatedAtUtc === null
            ? ACCOUNT_PLAN_UNKNOWN
            : <time dateTime={source.activatedAtUtc} title={source.activatedAtUtc}>{formatInstant(source.activatedAtUtc)}</time>}
        </dd>
        <dt>{ACCOUNT_PLAN_ACCOUNT_COUNT}</dt>
        <dd>{source.accountCount}</dd>
      </dl>
    </section>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return <section aria-labelledby="accounting-account-plan-title" className={styles.page}>
    <h1 id="accounting-account-plan-title">{ACCOUNT_PLAN_TITLE}</h1>
    <p className={styles.help}>{ACCOUNT_PLAN_INTRO}</p>
    {children}
  </section>
}
