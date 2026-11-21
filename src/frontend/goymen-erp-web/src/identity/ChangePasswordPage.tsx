import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { LogoutControl } from './LogoutControl'
import { PASSWORD_TEXT as T } from './identityText'
import { changePassword } from './passwordApi'
import type { PasswordChangeOutcome, PasswordChangeRequest } from './passwordContract'
import styles from './login.module.css'

type Attempt = { readonly key: string; readonly request: PasswordChangeRequest }

export function ChangePasswordPage({ expectedVersion, onChanged, onSignedOut }: {
  readonly expectedVersion: string
  readonly onChanged: () => void
  readonly onSignedOut: (outcome: 'signed-out' | 'already-ended') => void
}) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [visible, setVisible] = useState(false)
  const [pending, setPending] = useState(false)
  const [outcome, setOutcome] = useState<PasswordChangeOutcome | 'missing' | 'mismatch' | null>(null)
  const attempt = useRef<Attempt | null>(null)
  const mounted = useRef(true)
  const notice = useRef<HTMLDivElement>(null)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; attempt.current = null } }, [])
  useEffect(() => { if (outcome !== null) notice.current?.focus() }, [outcome])

  const send = async (saved: Attempt) => {
    setPending(true)
    setOutcome(null)
    const result = await changePassword(saved.request, saved.key)
    if (!mounted.current) return
    setPending(false)
    if (result.kind !== 'unknown') {
      attempt.current = null
      setCurrentPassword('')
      setNewPassword('')
      setConfirmation('')
    }
    if (result.kind === 'changed') { onChanged(); return }
    if (result.kind === 'unauthenticated') { onSignedOut('already-ended'); return }
    setOutcome(result)
  }

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (pending || attempt.current !== null) return
    if (!currentPassword || !newPassword || !confirmation) { setOutcome('missing'); return }
    if (newPassword !== confirmation) { setOutcome('mismatch'); return }
    const saved = { key: crypto.randomUUID(), request: { expectedVersion, currentPassword, newPassword } }
    attempt.current = saved
    void send(saved)
  }

  const failureText = outcome === 'missing' ? T.missing : outcome === 'mismatch' ? T.mismatch
    : outcome?.kind === 'refused' ? (outcome.code === 'current_password_invalid' ? T.currentInvalid : T.refused)
    : outcome?.kind === 'conflict' ? T.conflict : outcome?.kind === 'forbidden' ? T.forbidden
    : outcome?.kind === 'not-found' ? T.notFound : outcome?.kind === 'unknown' ? T[outcome.reason] : null

  return <div className={styles.page}><section className={styles.card} aria-labelledby="password-heading">
    <h1 id="password-heading">{T.heading}</h1><p className={styles.lead}>{T.lead}</p>
    <div aria-live="assertive">{failureText !== null && <div ref={notice} role="alert" tabIndex={-1}>{failureText}</div>}</div>
    <form className={styles.form} onSubmit={submit} noValidate aria-busy={pending}>
      <Field label={T.current}>{(control) => <input {...control} className={styles.input} type={visible ? 'text' : 'password'} autoComplete="current-password" value={currentPassword} disabled={pending || attempt.current !== null} onChange={(event) => setCurrentPassword(event.target.value)} />}</Field>
      <Field label={T.next}>{(control) => <input {...control} className={styles.input} type={visible ? 'text' : 'password'} autoComplete="new-password" value={newPassword} disabled={pending || attempt.current !== null} onChange={(event) => setNewPassword(event.target.value)} />}</Field>
      <Field label={T.confirm}>{(control) => <input {...control} className={styles.input} type={visible ? 'text' : 'password'} autoComplete="new-password" value={confirmation} disabled={pending || attempt.current !== null} onChange={(event) => setConfirmation(event.target.value)} />}</Field>
      <Button type="button" variant="secondary" onClick={() => setVisible(!visible)}>{visible ? T.hide : T.show}</Button>
      <Button type="submit" disabled={pending || attempt.current !== null}>{pending ? T.pending : T.submit}</Button>
    </form>
    {outcome !== null && typeof outcome === 'object' && outcome.kind === 'unknown' && <Button type="button" disabled={pending} onClick={() => { if (attempt.current) void send(attempt.current) }}>{T.retry}</Button>}
    {outcome !== null && typeof outcome === 'object' && outcome.kind === 'conflict' && <Button type="button" onClick={onChanged}>{T.reload}</Button>}
    <LogoutControl onSignedOut={onSignedOut} />
  </section></div>
}
