/**
 * ID-1F local sign-in screen — the pre-authentication screen the shell shows in local-account mode when the server
 * answers 401 (no session yet, an expired session, or a session ended in another tab; KIM-D13/D14/D30).
 *
 * - One uniform refusal text for a wrong password, an unknown, passive or locked account (KIM-D2/D4); the lock note
 *   is general and names no duration (the values live in Settings, KIM-D5).
 * - 429 `login_rate_limited` is its own state (KIM-D29): the server did not read the credentials.
 * - Labelled fields, show/hide password, pasting allowed, `username` / `current-password` autocomplete, full
 *   keyboard use, and every notice in an `aria-live` region that receives focus when it opens (LF-06).
 * - The password lives only in this component's state; it is cleared after every answer and never stored.
 *
 * The screen decides nothing about the session: a 204 is handed to `onSignedIn`, and the shell trusts the new session
 * only once `/identity/me` answers for it.
 */

import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { LOGIN_TEXT as T } from './identityText'
import { submitLogin } from './loginApi'
import type { LoginOutcome } from './loginContract'
import styles from './login.module.css'
import { TWO_FACTOR_TEXT } from './twoFactorText'

/** Why the sign-in screen is shown: first visit (null), after a 401 on a live session, or after the user signed out. */
export type LoginNotice = 'expired' | 'signed-out' | null

type LoginProblem = Exclude<LoginOutcome['kind'], 'signed-in'> | 'missing-fields'

const PROBLEM_TEXT: Record<LoginProblem, { readonly title: string; readonly detail: string | null }> = {
  'missing-fields': { title: T.missingFields, detail: null },
  refused: { title: T.refused, detail: T.lockNote },
  'rate-limited': { title: T.rateLimitedTitle, detail: T.rateLimited },
  'not-enabled': { title: T.notEnabledTitle, detail: T.notEnabled },
  'request-rejected': { title: T.requestRejectedTitle, detail: T.requestRejected },
  'service-unavailable': { title: T.serviceUnavailableTitle, detail: T.serviceUnavailable },
  offline: { title: T.offlineTitle, detail: T.offline },
  'network-error': { title: T.networkErrorTitle, detail: T.networkError },
  unexpected: { title: T.unexpectedTitle, detail: T.unexpected },
}

const NOTICE_TEXT: Record<Exclude<LoginNotice, null>, { readonly title: string; readonly detail: string }> = {
  expired: { title: T.expiredTitle, detail: T.expired },
  'signed-out': { title: T.signedOutTitle, detail: T.signedOut },
}

export function LoginPage({
  notice,
  onSignedIn,
}: {
  readonly notice: LoginNotice
  readonly onSignedIn: () => void
}) {
  const headingId = useId()
  const passwordId = `login-password-${useId()}`
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [factorCode, setFactorCode] = useState('')
  const [passwordVisible, setPasswordVisible] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [problem, setProblem] = useState<{ readonly kind: LoginProblem; readonly attempt: number } | null>(null)
  const attemptRef = useRef(0)
  const mountedRef = useRef(true)
  const userNameRef = useRef<HTMLInputElement>(null)
  const problemRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    mountedRef.current = true
    userNameRef.current?.focus()
    return () => {
      mountedRef.current = false
    }
  }, [])

  // A notice that opens takes focus (LF-06); a repeated refusal is a new attempt, so it is announced again.
  useEffect(() => {
    if (problem !== null) problemRef.current?.focus()
  }, [problem])

  const report = (kind: LoginProblem) => {
    attemptRef.current += 1
    setProblem({ kind, attempt: attemptRef.current })
  }

  const onSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (submitting) return
    if (userName.trim() === '' || password === '') {
      report('missing-fields')
      return
    }
    setSubmitting(true)
    setProblem(null)
    const outcome = await submitLogin({ userName, password, ...(factorCode === '' ? {} : { totpCode: factorCode }) })
    if (!mountedRef.current) return
    setPassword('')
    setFactorCode('')
    if (outcome.kind === 'signed-in') {
      onSignedIn()
      return
    }
    setSubmitting(false)
    report(outcome.kind)
  }

  const shownProblem = problem === null ? null : PROBLEM_TEXT[problem.kind]
  const shownNotice = problem === null && notice !== null ? NOTICE_TEXT[notice] : null

  return (
    <div className={styles.page}>
      <section className={styles.card} aria-labelledby={headingId} data-login-state={problem?.kind ?? notice ?? 'idle'}>
        <h1 id={headingId}>{T.heading}</h1>
        <p className={styles.lead}>{T.lead}</p>

        <div aria-live="assertive" aria-atomic="true">
          {shownProblem !== null ? (
            <div
              key={problem?.attempt}
              ref={problemRef}
              tabIndex={-1}
              role="alert"
              className={`${styles.notice} ${styles.noticeError}`}
              data-problem={problem?.kind}
            >
              <p className={styles.noticeTitle}>{shownProblem.title}</p>
              {shownProblem.detail !== null ? <p className={styles.noticeDetail}>{shownProblem.detail}</p> : null}
            </div>
          ) : null}
          {shownNotice !== null ? (
            <div role="status" className={styles.notice} data-notice={notice}>
              <p className={styles.noticeTitle}>{shownNotice.title}</p>
              <p className={styles.noticeDetail}>{shownNotice.detail}</p>
            </div>
          ) : null}
        </div>

        <form className={styles.form} noValidate onSubmit={(event) => void onSubmit(event)} aria-busy={submitting}>
          <Field label={T.userNameLabel}>
            {(control) => (
              <input
                {...control}
                ref={userNameRef}
                className={styles.input}
                type="text"
                name="username"
                autoComplete="username"
                autoCapitalize="none"
                autoCorrect="off"
                spellCheck={false}
                required
                value={userName}
                onChange={(event) => setUserName(event.target.value)}
              />
            )}
          </Field>
          <Field label={T.passwordLabel} id={passwordId}>
            {(control) => (
              <div className={styles.passwordRow}>
                <input
                  {...control}
                  className={styles.input}
                  type={passwordVisible ? 'text' : 'password'}
                  name="password"
                  autoComplete="current-password"
                  autoCapitalize="none"
                  autoCorrect="off"
                  spellCheck={false}
                  required
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                />
                <Button
                  type="button"
                  variant="secondary"
                  aria-controls={passwordId}
                  onClick={() => setPasswordVisible((visible) => !visible)}
                >
                  {passwordVisible ? T.hidePassword : T.showPassword}
                </Button>
              </div>
            )}
          </Field>
          <Field label={TWO_FACTOR_TEXT.loginCode}>{(control) => <input {...control} type="text" autoComplete="one-time-code" autoCapitalize="none" maxLength={64} value={factorCode} disabled={submitting} onChange={(event) => setFactorCode(event.target.value)} />}</Field>
          <Button type="submit" variant="primary" disabled={submitting}>
            {submitting ? T.submitting : T.submit}
          </Button>
        </form>
      </section>
    </div>
  )
}
