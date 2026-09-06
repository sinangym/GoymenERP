/**
 * The safe return (`cari.list`) for the C-02 and CARI-LINK screens.
 *
 * A distinct accessible name on purpose: the nav already has a link called
 * "Cari Kartlar", and two links with the same name on one page are ambiguous to
 * anyone cycling through links rather than looking at where they sit.
 */

import { Link } from 'react-router'
import styles from './CariBackLink.module.css'

export function CariBackLink({ label }: { readonly label: string }) {
  return (
    <Link className={styles.back} to="/cari">
      {label}
    </Link>
  )
}
