/**
 * ID-1F sign-out control for the shell toolbar (local-account mode only).
 *
 * A 204 ends every session of the user on the server (KIM-D30); a 401 means the session had already ended (expired,
 * or closed in another tab). Both are handed to `onSignedOut` — the shell drops everything and shows the sign-in
 * screen. Any other answer leaves the shell as it is and says plainly that the session may still be open: a failed
 * sign-out is never shown as a successful one.
 */

import { useEffect, useRef, useState } from 'react'
import { Button } from '../ui/Button'
import { LOGOUT_TEXT as T } from './identityText'
import { submitLogout } from './loginApi'
import type { LogoutOutcome } from './loginContract'
import styles from './login.module.css'

type LogoutFailure = Exclude<LogoutOutcome['kind'], 'signed-out' | 'already-ended'>

const FAILURE_TEXT: Record<LogoutFailure, string> = {
  'not-enabled': T.notEnabled,
  'request-rejected': T.failed,
  'service-unavailable': T.failed,
  offline: T.offline,
  'network-error': T.failed,
  unexpected: T.unexpected,
}

export function LogoutControl({ onSignedOut }: { readonly onSignedOut: (outcome: 'signed-out' | 'already-ended') => void }) {
  const [pending, setPending] = useState(false)
  const [failure, setFailure] = useState<LogoutFailure | null>(null)
  const mountedRef = useRef(true)

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  const signOut = async () => {
    if (pending) return
    setPending(true)
    setFailure(null)
    const outcome = await submitLogout()
    if (!mountedRef.current) return
    if (outcome.kind === 'signed-out' || outcome.kind === 'already-ended') {
      onSignedOut(outcome.kind)
      return
    }
    setPending(false)
    setFailure(outcome.kind)
  }

  return (
    <span className={styles.logout}>
      <Button type="button" variant="ghost" size="sm" disabled={pending} onClick={() => void signOut()}>
        {pending ? T.pending : T.action}
      </Button>
      <span aria-live="polite">
        {failure !== null ? (
          <span role="alert" className={styles.logoutError} data-logout-failure={failure}>
            {FAILURE_TEXT[failure]}
          </span>
        ) : null}
      </span>
    </span>
  )
}
