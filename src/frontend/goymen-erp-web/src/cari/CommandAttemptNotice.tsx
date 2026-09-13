/**
 * The alerts a command attempt can leave behind, shared by C-02 create and the
 * CARI-LINK workspace:
 *
 *  - UNKNOWN outcome: the snapshot is frozen. The only ways forward are the same
 *    attempt with the same key, or an explicit abandon. There is no "submit
 *    again" here because that would be a new key under an unchanged payload.
 *  - ABANDONED while unknown: the form is editable again, but the attempt is
 *    still unresolved. While the form holds the SAME payload the owner passes
 *    `repeatsUnresolved`, and this box explains that the unchanged payload is
 *    not re-sent under a new key: retry the kept attempt (same key) or change
 *    the payload. The box is focusable so a refused submit can land on it.
 *  - DEFINITE failure: the attempt is closed. The user acknowledges it (or edits
 *    a field, which the owning form treats the same way) before a NEW attempt
 *    can start. Nothing resends on its own.
 *
 * 401 and 403 are not rendered here; the owning screen closes onto the shared
 * non-disclosing state blocks for those.
 */

import type { ReactNode, Ref } from 'react'
import type { AttemptFailure, AttemptState } from './commandAttempt'
import { CARI_COMMAND_TEXT as TEXT } from './cariCommandText'
import styles from './CommandAttemptNotice.module.css'

export type AttemptFailureMessages = Partial<
  Record<AttemptFailure, { readonly title: string; readonly detail: string }>
>

export function CommandAttemptNotice<P, R>({
  state,
  onRetry,
  onAbandon,
  onAcknowledge,
  extra,
  repeatsUnresolved = false,
  unresolvedRef,
  messages,
}: {
  readonly state: AttemptState<P, R>
  readonly onRetry: () => void
  readonly onAbandon: () => void
  readonly onAcknowledge: () => void
  /** Owner-supplied follow-up for a failure (e.g. a fresh-read link). */
  readonly extra?: ReactNode
  /** The owner's current payload repeats the abandoned, still-unresolved attempt. */
  readonly repeatsUnresolved?: boolean
  /** Lets the owner move focus onto the abandoned-attempt box after a refused submit. */
  readonly unresolvedRef?: Ref<HTMLDivElement>
  /**
   * Per-failure wording for the OWNER's command, where the shared text describes another
   * command's situation — the shared `not-found`, for one, says a candidate list was
   * refreshed, which is true for a link and false for a status change.
   */
  readonly messages?: AttemptFailureMessages
}) {
  if (state.phase === 'abandoned') {
    if (!repeatsUnresolved) return null
    return (
      <div
        ref={unresolvedRef}
        tabIndex={-1}
        role="alert"
        className={styles.unknown}
        data-attempt="abandoned"
      >
        <p className={styles.title}>{TEXT.abandoned.title}</p>
        <p className={styles.detail}>{TEXT.abandoned.detail}</p>
        <div className={styles.actions}>
          <button type="button" className={styles.primary} onClick={onRetry}>
            {TEXT.abandoned.retry}
          </button>
        </div>
      </div>
    )
  }

  if (state.phase === 'unknown') {
    return (
      <div role="alert" className={styles.unknown} data-attempt="unknown">
        <p className={styles.title}>{TEXT.unknown.title}</p>
        <p className={styles.detail}>
          {TEXT.unknownReason[state.reason]} {TEXT.unknown.detail}
        </p>
        <div className={styles.actions}>
          <button type="button" className={styles.primary} onClick={onRetry}>
            {TEXT.unknown.retry}
          </button>
          <button type="button" className={styles.secondary} onClick={onAbandon}>
            {TEXT.unknown.abandon}
          </button>
        </div>
      </div>
    )
  }

  if (state.phase === 'failed' && state.failure !== 'unauthenticated' && state.failure !== 'forbidden') {
    const message = messages?.[state.failure] ?? TEXT.failure[state.failure]
    return (
      <div role="alert" className={styles.failed} data-attempt="failed" data-failure={state.failure}>
        <p className={styles.title}>{message.title}</p>
        <p className={styles.detail}>{message.detail}</p>
        {extra}
        <div className={styles.actions}>
          <button type="button" className={styles.secondary} onClick={onAcknowledge}>
            {TEXT.acknowledge}
          </button>
        </div>
      </div>
    )
  }

  return null
}
