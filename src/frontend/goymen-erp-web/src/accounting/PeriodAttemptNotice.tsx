/**
 * The notice of one period command: applied (or replayed), a definite refusal by its code, or an
 * UNKNOWN outcome with the only safe action — re-sending the same snapshot.
 */

import { useEffect, useRef } from 'react'
import type { PeriodAttempt } from './periodAttempt'
import {
  FAILED_TITLE,
  REPLAYED_NOTICE,
  RESEND,
  UNKNOWN_ACTION_HELP,
  UNKNOWN_TITLE,
  failureText,
  unknownText,
} from './accountingText'
import styles from './Accounting.module.css'

export function PeriodAttemptNotice({
  attempt,
  appliedText,
  onResend,
}: {
  readonly attempt: PeriodAttempt
  readonly appliedText: string
  readonly onResend: () => void
}) {
  const noticeRef = useRef<HTMLDivElement>(null)
  const phase = attempt.phase
  useEffect(() => {
    if (phase === 'applied' || phase === 'failed' || phase === 'unknown') noticeRef.current?.focus()
  }, [phase])

  if (attempt.phase === 'applied') {
    return (
      <div
        ref={noticeRef}
        tabIndex={-1}
        className={styles.noticeOk}
        role="status"
        data-outcome={attempt.replayed ? 'replayed' : 'applied'}
      >
        <p>{attempt.replayed ? REPLAYED_NOTICE : appliedText}</p>
      </div>
    )
  }
  if (attempt.phase === 'failed') {
    return (
      <div
        ref={noticeRef}
        tabIndex={-1}
        className={styles.noticeError}
        role="alert"
        data-outcome="failed"
        data-failure={attempt.failure.kind}
      >
        <p className={styles.noticeTitle}>{FAILED_TITLE}</p>
        <p>{failureText(attempt.failure)}</p>
      </div>
    )
  }
  if (attempt.phase === 'unknown') {
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeWarn} role="alert" data-outcome="unknown">
        <p className={styles.noticeTitle}>{UNKNOWN_TITLE}</p>
        <p>{unknownText(attempt.unknown)}</p>
        {attempt.lastRefusal !== null ? <p>Son yeniden gönderim reddedildi: {failureText(attempt.lastRefusal)}</p> : null}
        <p>{UNKNOWN_ACTION_HELP}</p>
        <div className={styles.actions}>
          <button type="button" onClick={onResend}>
            {RESEND}
          </button>
        </div>
      </div>
    )
  }
  return null
}
