import { useState } from 'react'
import { Link } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { useReportCatalog } from './useReportCatalog'
import { buildAuditorPackage } from './reportingApi'
import { REPORTING_ROUTE } from './reportingContract'
import { refusalText } from './reportingProblems'
import { REPORTING_TEXT as T } from './reportingText'
import styles from './Reporting.module.css'

export function AuditorPackagePage() {
  const shell = useShellState()
  const context = contextKey(shell)
  return <AuditorPackageBody key={context === null ? 'no-context' : contextKeyString(context)} />
}

function AuditorPackageBody() {
  const { shell, company, query, catalog } = useReportCatalog()
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  if (capabilityFailClosed(shell) || company === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
  if (catalog === null) {
    return <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>}
      subject={T.subject} onRetry={() => void query.refetch()} />
  }
  if (!catalog.canBuildAuditorPackage) return <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
  return <section className={styles.page} aria-labelledby="auditor-heading">
    <Link to={REPORTING_ROUTE.center}>{T.back}</Link>
    <h1 id="auditor-heading">{T.auditorHeading}</h1>
    <p className={styles.note}>{T.auditorScope}</p>
    <form className={styles.form} onSubmit={(event) => {
      event.preventDefault()
      setMessage(T.exporting)
      void buildAuditorPackage(company, from, to).then((outcome) => setMessage(outcome.type === 'saved' ? T.exported
        : outcome.type === 'refused' ? refusalText(outcome.status, outcome.code) : T.exportFailed))
    }}>
      <label><span>{T.auditorFrom}</span><input type="date" value={from} onChange={(event) => setFrom(event.target.value)} /></label>
      <label><span>{T.auditorTo}</span><input type="date" value={to} onChange={(event) => setTo(event.target.value)} /></label>
      <Button type="submit">{T.auditorBuild}</Button>
    </form>
    <p className={styles.note}>{T.exportNote}</p>
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
