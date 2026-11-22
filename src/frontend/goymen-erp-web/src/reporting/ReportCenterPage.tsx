import { Link } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useReportCatalog } from './useReportCatalog'
import { REPORTING_ROUTE, reportRoute } from './reportingContract'
import { REPORTING_TEXT as T } from './reportingText'
import styles from './Reporting.module.css'

export function ReportCenterPage() {
  const shell = useShellState()
  const context = contextKey(shell)
  return <ReportCenterBody key={context === null ? 'no-context' : contextKeyString(context)} />
}

function ReportCenterBody() {
  const { shell, company, query, catalog } = useReportCatalog()
  if (capabilityFailClosed(shell) || company === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
  return <section className={styles.page} aria-labelledby="report-center-heading">
    <h1 id="report-center-heading">{T.heading}</h1>
    <p className={styles.note}>{T.scope}</p>
    {catalog === null
      ? <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.subject}
          onRetry={() => void query.refetch()} />
      : catalog.reports.length === 0 && !catalog.canBuildAuditorPackage
        ? <><StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} /><p>{T.noReports}</p></>
        : <ul className={styles.list}>
          {catalog.reports.map((report) => <li key={report.id} className={styles.card}>
            <Link to={reportRoute(report.id)}>{T.reports[report.id] ?? report.id}</Link>
            <span className={styles.note}> · {T.owner}: {report.ownerModule}</span>
          </li>)}
          {catalog.canBuildAuditorPackage ? <li className={styles.card}><Link to={REPORTING_ROUTE.auditor}>{T.auditorOpen}</Link></li> : null}
        </ul>}
  </section>
}
