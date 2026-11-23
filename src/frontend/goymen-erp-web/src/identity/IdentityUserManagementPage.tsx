import { useState, type FormEvent } from 'react'
import { EditedAttemptNotice } from '../settings/EditedAttemptNotice'
import { Link } from 'react-router'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { stateOf, useBoundaryRead } from '../settings/SalesPolicyParts'
import { useCommandAttempt } from '../settings/useCommandAttempt'
import { IDENTITY_CAPABILITY, IDENTITY_ACCESS_CONTROL, accessListPath } from './capabilities'
import { fetchAccessRoles } from './api'
import { AssignmentForm } from './AccessGrantPanel'
import { readManagedUsers, sendUserManagement, type ManagedUser } from './userManagementApi'
import { USER_MANAGEMENT_TEXT as T } from './userManagementText'
import type { AccessGrantOutcome } from './accessGrantContract'
import styles from './identity.module.css'
import type { RemoteViewState } from '../shell/uiState'
import type { AccessRoleSummary } from './contract'

export function IdentityUserManagementPage() {
  const shell = useShellState(), context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  return <Users key={`${namespace}:${shell.routeEntryGeneration}`} namespace={namespace} />
}
function Users({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(), company = shell.activeContext?.companyId ?? ''
  const ready = namespace !== null && company !== '' && (shell.activeContext?.scopeMode ?? 'single') === 'single' && hasCapability(shell, IDENTITY_CAPABILITY.userManagement)
  const users = useBoundaryRead([namespace, shell.routeEntryGeneration, 'managed-users'], ready, (signal) => readManagedUsers(company, signal))
  const [edit, setEdit] = useState<{ readonly user: ManagedUser | null; readonly action: string } | null>(null), [notice, setNotice] = useState('')
  const finish = (message: string | null) => { setEdit(null); if (message !== null) { setNotice(message); void users.refetch() } }
  const state = stateOf(users.isPending, users.data)
  if (!ready) return <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
  return <section className={styles.page}><h1>{T.title}</h1><p>{T.intro}</p><p role="status">{notice}</p>
    {edit !== null ? <UserForm company={company} user={edit.user} action={edit.action} finish={finish} /> : <Button type="button" onClick={() => setEdit({ user: null, action: 'create' })}>{T.create}</Button>}
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => void users.refetch()} /> : <>
      {state.data.length === 0 && <p>{T.empty}</p>}{state.data.map((user) => <article key={user.userId} className={styles.panel}>
        <h2>{user.displayName}</h2><p>{user.userName} · {user.isActive ? T.active : T.inactive}{user.isLocked ? ` · ${T.locked}` : ''}</p>
        {user.isActive ? <>{(['password-reset', 'unlock', 'deactivate'] as const).map((action) => <Button type="button" key={action} disabled={edit !== null} onClick={() => setEdit({ user, action })}>{action === 'password-reset' ? T.reset : action === 'unlock' ? T.unlock : T.deactivate}</Button>)}</> : <p>{T.noActivation}</p>}
        {hasCapability(shell, IDENTITY_CAPABILITY.accessList) && <Link to={accessListPath(user.userId)}>{T.assignments}</Link>}
        {user.isActive && hasCapability(shell, IDENTITY_ACCESS_CONTROL.manage) && <ManagementAssignment company={company} user={user} />}
      </article>)}</>}
  </section>
}
function ManagementAssignment({ company, user }: { readonly company: string; readonly user: ManagedUser }) {
  const [open, setOpen] = useState(false), [notice, setNotice] = useState<string | null>(null)
  const roles = useBoundaryRead([company, user.userId, 'management-assignable-roles'], open, (signal) => fetchAccessRoles(company, signal))
  const state = stateOf(roles.isPending, roles.data) as RemoteViewState<readonly AccessRoleSummary[]>
  return <section><p role="status">{notice}</p>{!open ? <Button type="button" onClick={() => setOpen(true)}>{T.assign}</Button>
    : state.kind !== 'ready' ? <><StateBlock state={state} subject={T.roleSubject} onRetry={() => void roles.refetch()} /><Button type="button" onClick={() => setOpen(false)}>{T.cancel}</Button></>
      : <AssignmentForm company={company} user={user} roles={state.data} finish={(message) => { setOpen(false); setNotice(message) }} />}</section>
}
function UserForm({ company, user, action, finish }: { readonly company: string; readonly user: ManagedUser | null; readonly action: string; readonly finish: (message: string | null) => void }) {
  const attempt = useCommandAttempt<AccessGrantOutcome>(), boundary = useAuthBoundary()
  const [id] = useState(() => crypto.randomUUID()), [displayName, setName] = useState(''), [userName, setUserName] = useState(''), [password, setPassword] = useState(''), [reason, setReason] = useState('')
  const [frozen, setFrozen] = useState<object | null>(null), [error, setError] = useState('')
  const busy = attempt.status.kind === 'busy', passwordRequired = user === null || action === 'password-reset'
  const valid = reason.trim() !== '' && (!passwordRequired || password !== '') && (user !== null || (displayName.trim() !== '' && userName.trim() !== ''))
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (busy || (!valid && frozen === null)) return
    // Credentials live in form memory only. No storage, URL, log, analytics or persisted fingerprint.
    const body = frozen ?? (user === null ? { userId: id, expectedVersion: '0', displayName, userName, password, reason } : { expectedVersion: user.version, password: passwordRequired ? password : null, reason })
    const capture = boundary.capture(), result = await attempt.run(body, (key) => sendUserManagement(company, user?.userId ?? null, action, body, key))
    if (result.type === 'unknown') { setFrozen(body); return }
    setFrozen(null); setPassword('')
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, capture)
    setError(result.code === 'identity.last_admin_required' ? T.lastAdmin : result.code === 'identity.user.company_assignment_required' ? T.assignment : result.code === 'identity.user.password_policy_violation' ? T.policy : T.refused)
  }
  return <form onSubmit={(event) => void submit(event)} aria-busy={busy}><fieldset disabled={busy || frozen !== null}>
    {user === null && <><Field label={T.name}>{(p) => <input {...p} required maxLength={200} value={displayName} onChange={(e) => setName(e.target.value)} />}</Field><Field label={T.userName}>{(p) => <input {...p} required maxLength={256} autoComplete="off" value={userName} onChange={(e) => setUserName(e.target.value)} />}</Field></>}
    {passwordRequired && <Field label={T.password}>{(p) => <input {...p} required type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />}</Field>}
    <Field label={T.reason}>{(p) => <textarea {...p} required maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field></fieldset>
    {frozen !== null && <p role="alert">{T.unknown}</p>}<EditedAttemptNotice status={attempt.status} />{error && <p role="alert">{error}</p>}
    <Button type="submit" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.submit}</Button><Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
