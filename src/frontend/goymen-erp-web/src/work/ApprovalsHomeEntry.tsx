/** D-05: the home page's "Onaylarım" entry of the approval inbox. */

import { Link } from 'react-router'
import { capabilityFailClosed, hasCapability } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import { APPROVAL_SOURCES, APPROVALS_TEXT as T, WORK_ROUTE } from './workText'
import styles from './Work.module.css'

/** Drawn only for an actor holding a connected owner's own approve capability; it names only those sources. */
export function ApprovalsHomeEntry() {
  const shell = useShellState()
  if (capabilityFailClosed(shell)) return null
  const sources = APPROVAL_SOURCES.filter((source) => hasCapability(shell, source.capability))
  if (sources.length === 0) return null
  return <section className={styles.home} aria-labelledby="home-approvals-heading" data-testid="home-approvals">
    <h2 id="home-approvals-heading">{T.homeHeading}</h2>
    <p>{T.homeText(sources.map((source) => source.label))}</p>
    <p><Link to={WORK_ROUTE.approvals}>{T.homeLink}</Link></p>
  </section>
}
