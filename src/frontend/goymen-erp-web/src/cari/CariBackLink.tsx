/**
 * The safe return (`cari.list`) for the C-02 and CARI-LINK screens.
 *
 * A distinct accessible name on purpose: the nav already has a link called
 * "Cari Kartlar", and two links with the same name on one page are ambiguous to
 * anyone cycling through links rather than looking at where they sit.
 *
 * GUARDED, because a return is only safe if there is somewhere to return TO. C-02 is published
 * ahead of C-01 (the normal-runtime slice publishes create and detail only), and this component is
 * drawn from a PUBLISHED page, so an unguarded link would send a normal user to a route the shell
 * has not mounted — the neutral not-found block, from a control that promises the opposite. The
 * same defect the M-04 row link had, and the same answer: offer nothing rather than offer a dead
 * end. When `cari.parties.list` is published the link reappears with no further change here.
 */

import { Link } from 'react-router'
import { CAPABILITY } from '../shell/capabilities'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import styles from './CariBackLink.module.css'

export function CariBackLink({ label }: { readonly label: string }) {
  const shell = useShellState()
  if (!hasCapability(shell, CAPABILITY.cariPartiesList)) return null
  return (
    <Link className={styles.back} to="/cari">
      {label}
    </Link>
  )
}
