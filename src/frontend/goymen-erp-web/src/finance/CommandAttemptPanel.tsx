/**
 * The one place a Finance command's unresolved/refused state is told to the user.
 * Shared by the draft form and the detail actions so the unknown-outcome wording and
 * its two exits (same-key resend, abandon) cannot drift between screens.
 */

import {
  ABANDON,
  CONFLICT_TEXT,
  CONFLICT_TITLE,
  REFUSAL_AFTER_UNKNOWN,
  REFUSAL_TITLE,
  RESEND_SAME,
  UNKNOWN_FROZEN,
  UNKNOWN_REASON,
  UNKNOWN_TITLE,
  issueText,
} from './financeText'
import type { ReceiptCommand } from './financePort'
import styles from './Finance.module.css'

export function CommandAttemptPanel({ command }: { readonly command: ReceiptCommand }) {
  const { state } = command
  if (state.phase === 'unknown') {
    return (
      <div className={styles.attempt} role="alert" data-attempt="unknown">
        <p className={styles.attemptTitle}>{UNKNOWN_TITLE}</p>
        <p>{UNKNOWN_REASON[state.reason]}</p>
        {state.lastRefusal === undefined ? null : <p>{REFUSAL_AFTER_UNKNOWN}</p>}
        <p>{UNKNOWN_FROZEN}</p>
        <div className={styles.actions}>
          <button type="button" onClick={() => command.resend()}>
            {RESEND_SAME}
          </button>
          <button type="button" onClick={() => command.abandon()}>
            {ABANDON}
          </button>
        </div>
      </div>
    )
  }
  if (state.phase === 'conflicted') {
    return (
      <div className={styles.attempt} role="alert" data-attempt="conflicted">
        <p className={styles.attemptTitle}>{CONFLICT_TITLE}</p>
        <p>{CONFLICT_TEXT}</p>
        <div className={styles.actions}>
          <button type="button" onClick={() => command.abandon()}>
            {ABANDON}
          </button>
        </div>
      </div>
    )
  }
  if (state.phase === 'refused') {
    return (
      <div className={styles.attempt} role="alert" data-attempt="refused">
        <p className={styles.attemptTitle}>{REFUSAL_TITLE[state.reason]}</p>
        {state.issues.length === 0 ? null : (
          <ul>
            {state.issues.map((issue) => (
              <li key={`${issue.field}:${issue.code}`}>{issueText(issue.code)}</li>
            ))}
          </ul>
        )}
      </div>
    )
  }
  return null
}
