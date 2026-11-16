import { useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { stateOf, useBoundaryRead } from '../settings/SalesPolicyParts'
import { useCommandAttempt } from '../settings/useCommandAttempt'
import { IDENTITY_ACCESS_CONTROL } from './capabilities'
import type { AccessAssignment, AccessRoleSummary, AccessUserDetail } from './contract'
import { fetchAccessGrantRequests, sendAccessGrant } from './accessGrantApi'
import type { AccessGrantAction, AccessGrantOutcome, AccessGrantRequest, AssignAccessBody } from './accessGrantContract'
import { ACCESS_GRANT_TEXT as T } from './identityText'
import styles from './identity.module.css'

export function AccessGrantPanel({ company, user, roles, changed }: { readonly company: string; readonly user: AccessUserDetail; readonly roles: readonly AccessRoleSummary[]; readonly changed: () => void }) {
  const shell = useShellState()
  const [editing, setEditing] = useState<'assign' | AccessAssignment | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const finish = (message: string | null) => { setEditing(null); setNotice(message); if (message !== null) changed() }
  if (!hasCapability(shell, IDENTITY_ACCESS_CONTROL.manage)) return null
  return <section className={styles.panel}><h3>{T.title}</h3><p role="status">{notice}</p>
    {editing === 'assign' ? <AssignmentForm company={company} user={user} roles={roles} finish={finish} />
      : <Button type="button" disabled={editing !== null || !user.isActive || !roles.some((role) => role.isActive)} onClick={() => setEditing('assign')}>{T.assign}</Button>}
    {user.assignments.filter((assignment) => assignment.status === 'Active').map((assignment) => <section key={assignment.assignmentId}>
      <p>{assignment.roleName}</p>{editing !== null && editing !== 'assign' && editing.assignmentId === assignment.assignmentId
        ? <><p>{T.lastAdmin}</p>{user.assignments.filter((item) => item.validity === 'effective').length <= 1 && <p role="note">{T.lastAssignment}</p>}
          <CommandForm company={company} action="revoke" id={assignment.assignmentId} body={{ expectedVersion: assignment.version }} finish={finish} /></>
        : <Button type="button" disabled={editing !== null} onClick={() => setEditing(assignment)}>{T.revoke} · {assignment.roleName}</Button>}
    </section>)}
  </section>
}

function AssignmentForm({ company, user, roles, finish }: { readonly company: string; readonly user: AccessUserDetail; readonly roles: readonly AccessRoleSummary[]; readonly finish: Finish }) {
  const [roleId, setRole] = useState('')
  const [from, setFrom] = useState('')
  const [until, setUntil] = useState('')
  const selected = roles.find((role) => role.roleId === roleId && role.isActive)
  const instant = (value: string) => value === '' || !Number.isFinite(Date.parse(value)) ? null : new Date(value).toISOString()
  const start = instant(from), end = instant(until)
  const valid = selected !== undefined && (from === '' || start !== null) && (until === '' || (start !== null && end !== null && end > start))
  const body: AssignAccessBody = { userId: user.userId, roleId, validFromUtc: start, validUntilUtc: end, reason: null }
  return <CommandForm company={company} action={selected?.isCritical ? 'request' : 'assign'} id={null} body={body} finish={finish} valid={valid} submitLabel={selected?.isCritical ? T.request : T.assign}>
    <Field label={T.role}>{(control) => <select {...control} required value={roleId} onChange={(event) => setRole(event.target.value)}><option value="">{T.chooseRole}</option>{roles.filter((role) => role.isActive).map((role) => <option key={role.roleId} value={role.roleId}>{role.name}</option>)}</select>}</Field>
    {selected?.isCritical && <p role="note">{T.critical}</p>}
    <Field label={T.from}>{(control) => <input {...control} type="datetime-local" value={from} onChange={(event) => setFrom(event.target.value)} />}</Field>
    <Field label={T.until}>{(control) => <input {...control} type="datetime-local" min={from} value={until} onChange={(event) => setUntil(event.target.value)} />}</Field>
    <section aria-label={T.preview}><h4>{T.preview}</h4><p>{T.target}: {user.displayName} · {selected?.name}</p><p>{from || T.from} — {until || T.until}</p></section>
  </CommandForm>
}

export function AccessGrantQueue({ company, namespace, changed }: { readonly company: string; readonly namespace: string; readonly changed: () => void }) {
  const shell = useShellState()
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'identity-access-grant-requests'], true, (signal) => fetchAccessGrantRequests(company, signal))
  const state = stateOf<readonly AccessGrantRequest[]>(query.isPending, query.data)
  return <section className={styles.panel}><h2>{T.queue}</h2>{state.kind !== 'ready' ? <StateBlock state={state} subject={T.queueSubject} onRetry={() => void query.refetch()} />
    : <QueueRows requests={state.data} actor={shell.activeContext?.userId ?? ''} company={company} canManage={hasCapability(shell, IDENTITY_ACCESS_CONTROL.manage)} canDecide={hasCapability(shell, IDENTITY_ACCESS_CONTROL.decide)}
      changed={() => { void query.refetch(); changed() }} />}</section>
}

function QueueRows({ requests, actor, company, canManage, canDecide, changed }: { readonly requests: readonly AccessGrantRequest[]; readonly actor: string; readonly company: string; readonly canManage: boolean; readonly canDecide: boolean; readonly changed: () => void }) {
  const [editing, setEditing] = useState<{ readonly request: AccessGrantRequest; readonly action: 'approve' | 'reject' | 'withdraw' } | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const finish = (message: string | null) => { setEditing(null); setNotice(message); if (message !== null) changed() }
  return <><p role="status">{notice}</p>{requests.length === 0 && <p>{T.empty}</p>}{requests.map((request) => {
    const own = request.requestedBy.userId.toLowerCase() === actor.toLowerCase(), target = request.target.userId.toLowerCase() === actor.toLowerCase()
    return <article key={request.requestId}><h3>{request.target.displayName} · {request.roleName}</h3><p>{T.requester}: {request.requestedBy.displayName}</p>
      <p>{T.validity}: {request.validFromUtc ?? request.requestedAtUtc} — {request.validUntilUtc ?? '—'}</p><p>{request.reason}</p>
      {(own || target) && <p>{T.secondPerson}</p>}
      {editing?.request.requestId === request.requestId ? <CommandForm key={`${request.requestId}:${editing.action}`} company={company} action={editing.action} id={request.requestId} body={{ expectedVersion: request.version }} finish={finish} />
        : <>{canDecide && !own && !target && <><Button type="button" disabled={editing !== null} onClick={() => setEditing({ request, action: 'approve' })}>{T.approve}</Button><Button type="button" disabled={editing !== null} onClick={() => setEditing({ request, action: 'reject' })}>{T.reject}</Button></>}
          {canManage && own && <Button type="button" disabled={editing !== null} onClick={() => setEditing({ request, action: 'withdraw' })}>{T.withdraw}</Button>}</>}
    </article>
  })}</>
}

type Finish = (message: string | null) => void
function CommandForm({ company, action, id, body, finish, valid = true, submitLabel, children }: {
  readonly company: string; readonly action: AccessGrantAction; readonly id: string | null; readonly body: object; readonly finish: Finish; readonly valid?: boolean; readonly submitLabel?: string; readonly children?: ReactNode
}) {
  const attempt = useCommandAttempt<AccessGrantOutcome>()
  const boundary = useAuthBoundary()
  const [reason, setReason] = useState('')
  const [reasonCode, setReasonCode] = useState('')
  const [frozen, setFrozen] = useState<{ readonly action: AccessGrantAction; readonly body: object } | null>(null)
  const [refusal, setRefusal] = useState<string | null>(null)
  const busy = attempt.status.kind === 'busy'
  const reasoned = action === 'assign' || action === 'request' || action === 'revoke'
  const canSend = valid && (!reasoned || reason.trim().length > 0) && (action !== 'reject' || /^[a-z][a-z0-9_.-]{0,63}$/.test(reasonCode))
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (busy || (!canSend && frozen === null)) return
    const payload = frozen ?? { action, body: { ...body, ...(reasoned ? { reason: reason.trim() } : action === 'reject' ? { reasonCode } : {}) } }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (key) => sendAccessGrant(company, key, payload.action, id, payload.body))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.succeeded); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 404 || result.code === 'identity.access.version_conflict') { finish(T.versionConflict); return }
    setRefusal(result.code === 'identity.last_admin_required' ? T.lastAdmin : result.code === 'identity.assignment.second_approval_required' ? T.secondApproval
      : result.code === 'identity.grant_request.self_approval_refused' ? T.selfApproval : T.refused)
  }
  return <form onSubmit={(event) => void submit(event)} aria-busy={busy}>
    <fieldset disabled={busy || frozen !== null}>{children}
      {reasoned && <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={500} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>}
      {action === 'reject' && <><Field label={T.reasonCode}>{(control) => <input {...control} required maxLength={64} value={reasonCode} onChange={(event) => setReasonCode(event.target.value)} />}</Field><p>{T.reasonCodeHelp}</p></>}
    </fieldset>
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}{attempt.status.kind === 'refused' && <p role="alert">{refusal ?? T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!canSend && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : submitLabel ?? T.confirm}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
