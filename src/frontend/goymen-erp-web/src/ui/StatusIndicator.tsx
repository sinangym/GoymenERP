/**
 * GOY-DS-001 StatusIndicator — a dot plus the caller's own text.
 *
 * Presentational only: it shows exactly the text it is given and the tone the caller
 * chose. It does not map lifecycle, approval or permission values to words or colours,
 * and it is not a live region — a list of statuses must not re-announce itself. The dot
 * is decorative; the text alone carries the meaning, so colour is never the only signal.
 */

import type { ReactNode } from 'react'
import styles from './primitives.module.css'

export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

export function StatusIndicator({
  tone = 'neutral',
  className,
  children,
}: {
  readonly tone?: StatusTone
  readonly className?: string
  readonly children: ReactNode
}) {
  return (
    <span className={[styles.status, className].filter(Boolean).join(' ')} data-tone={tone}>
      <span className={styles.dot} aria-hidden="true" />
      {children}
    </span>
  )
}
