/** D-05: the home page's "Onaylarım" entry of the Finance approval pilot. */

import { Link } from 'react-router'
import { capabilityFailClosed, hasCapability } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import { APPROVALS_TEXT as T, WORK_APPROVALS_CAPABILITY, WORK_ROUTE } from './workText'
import styles from './Work.module.css'

/** Drawn only for an actor holding the native Finance approve capability. */
export function ApprovalsHomeEntry() {
  const shell = useShellState()
  if (capabilityFailClosed(shell) || !hasCapability(shell, WORK_APPROVALS_CAPABILITY)) return null
  return <section className={styles.home} aria-labelledby="home-approvals-heading" data-testid="home-approvals">
    <h2 id="home-approvals-heading">{T.homeHeading}</h2>
    <p>{T.homeText}</p>
    <p><Link to={WORK_ROUTE.approvals}>{T.homeLink}</Link></p>
  </section>
}
