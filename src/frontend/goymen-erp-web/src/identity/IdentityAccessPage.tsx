/**
 * "Kullanıcılar ve yetkiler" — AT-004 read-only access view (SY-01 read subset).
 *
 * The user flow: pick a user → see their role assignments with validity, scopes and approver, and the permissions
 * that hold now → open a role → its card → back returns to the same user. The selected user lives in the URL
 * (`?kullanici=`), so back, reload and reopening the link all show the same selection, and nothing is kept in
 * browser storage.
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - Read-only. There is no assign, revoke or edit control, and no disabled placeholder for one: those commands
 *    are blocked on a user decision (NEEDS-SINAN #66 5738113623, OPEN-013) and have no server.
 *  - Remounted per committed context and route entry: a stale render would put one company's grants under
 *    another company's label. Every query key carries the namespace.
 *  - The company is the shell's single active company; the server fences every answer to it. A group (multi-
 *    company) context shows the no-company state rather than guessing a company.
 *  - A selected id that is not a well-formed UUID is not sent; a user the server does not return for this company
 *    is the neutral not-found block, exactly like an unknown id.
 *  - Role links are drawn only when the role card capability is granted; otherwise the role is plain text.
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { fetchAccessRoles, fetchAccessUser, fetchAccessUsers } from './api'
import type { AccessRoleSummary, AccessUserDetail, AccessUserSummary } from './contract'
import {
  IDENTITY_CAPABILITY,
  SELECTED_USER_PARAM,
  accessListPath,
  isWellFormedId,
  roleDetailPath,
} from './capabilities'
import {
  ACCESS_TEXT,
  assignmentCountLabel,
  compareTurkish,
  holderCountLabel,
} from './identityText'
import { Approver, Badge, ScopeList, ValidityBadge, ValidityRange } from './AccessParts'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './identity.module.css'

export function IdentityAccessPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <AccessScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

/** Runs one read through the auth boundary, so a real 401/403 is reported like everywhere else in the shell. */
function useBoundaryRead<T>(
  queryKey: readonly unknown[],
  enabled: boolean,
  read: (signal: AbortSignal) => Promise<ViewOutcome<T>>,
) {
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey,
    enabled,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await read(signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
}

function AccessScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const [searchParams] = useSearchParams()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const canRead = hasCapability(shell, IDENTITY_CAPABILITY.accessList)
  const canOpenRole = hasCapability(shell, IDENTITY_CAPABILITY.roleDetail)
  const rawSelected = searchParams.get(SELECTED_USER_PARAM)
  const selectedUserId = isWellFormedId(rawSelected) ? rawSelected : null
  const ready = namespace !== null && singleCompany && canRead
  const generation = shell.routeEntryGeneration

  const users = useBoundaryRead(
    [namespace ?? 'no-context', generation, 'identity-access-users'],
    ready,
    (signal) => fetchAccessUsers(companyId ?? '', signal),
  )
  const roles = useBoundaryRead(
    [namespace ?? 'no-context', generation, 'identity-access-roles'],
    ready,
    (signal) => fetchAccessRoles(companyId ?? '', signal),
  )

  if (!singleCompany) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={ACCESS_TEXT.contextSubject} />
      </Frame>
    )
  }

  if (!ready) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'not-found' }} subject={ACCESS_TEXT.contextSubject} />
      </Frame>
    )
  }

  const usersState = (
    users.isPending ? { kind: 'initial-loading' } : classifyOutcome(users.data ?? { type: 'malformed' })
  ) as RemoteViewState<readonly AccessUserSummary[]>
  const rolesState = (
    roles.isPending ? { kind: 'initial-loading' } : classifyOutcome(roles.data ?? { type: 'malformed' })
  ) as RemoteViewState<readonly AccessRoleSummary[]>

  return (
    <Frame>
      <div className={styles.layout}>
        <section aria-labelledby="identity-users-heading" className={styles.panel}>
          <h2 id="identity-users-heading">{ACCESS_TEXT.usersHeading}</h2>
          {usersState.kind === 'ready' ? (
            <UserList users={usersState.data} selectedUserId={selectedUserId} />
          ) : (
            <StateBlock state={usersState} subject={ACCESS_TEXT.usersSubject} onRetry={() => void users.refetch()} />
          )}
        </section>
        <section aria-labelledby="identity-detail-heading" className={styles.panel}>
          {selectedUserId === null ? (
            <>
              <h2 id="identity-detail-heading">{ACCESS_TEXT.assignmentsHeading}</h2>
              <p className={styles.muted}>{ACCESS_TEXT.selectPrompt}</p>
            </>
          ) : (
            <UserDetail
              key={selectedUserId}
              namespace={namespace}
              companyId={companyId ?? ''}
              userId={selectedUserId}
              canOpenRole={canOpenRole}
            />
          )}
        </section>
      </div>
      <section aria-labelledby="identity-roles-heading" className={styles.panel}>
        <h2 id="identity-roles-heading">{ACCESS_TEXT.rolesHeading}</h2>
        {rolesState.kind === 'ready' ? (
          <RoleTable roles={rolesState.data} canOpenRole={canOpenRole} selectedUserId={selectedUserId} />
        ) : (
          <StateBlock state={rolesState} subject={ACCESS_TEXT.rolesSubject} onRetry={() => void roles.refetch()} />
        )}
      </section>
    </Frame>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="identity-access-heading" className={styles.page}>
      <h1 id="identity-access-heading">{ACCESS_TEXT.heading}</h1>
      <p className={styles.lead}>{ACCESS_TEXT.lead}</p>
      {children}
    </section>
  )
}

function UserList({
  users,
  selectedUserId,
}: {
  readonly users: readonly AccessUserSummary[]
  readonly selectedUserId: string | null
}) {
  const sorted = [...users].sort(
    (left, right) => compareTurkish(left.displayName, right.displayName) || left.userId.localeCompare(right.userId),
  )
  return (
    <ul className={styles.userList} aria-label={ACCESS_TEXT.usersHeading}>
      {sorted.map((user) => {
        const selected = selectedUserId !== null && user.userId.toLowerCase() === selectedUserId.toLowerCase()
        return (
          <li key={user.userId}>
            <Link
              className={styles.userLink}
              to={accessListPath(user.userId)}
              aria-current={selected ? 'true' : undefined}
              data-user={user.userId}
            >
              <span>{user.displayName}</span>
              <span className={styles.userMeta}>
                {assignmentCountLabel(user.effectiveAssignmentCount, user.assignmentCount)}
                {user.isActive ? null : <> · {ACCESS_TEXT.passiveUser}</>}
              </span>
            </Link>
          </li>
        )
      })}
    </ul>
  )
}

function UserDetail({
  namespace,
  companyId,
  userId,
  canOpenRole,
}: {
  readonly namespace: string | null
  readonly companyId: string
  readonly userId: string
  readonly canOpenRole: boolean
}) {
  const shell = useShellState()
  const query = useBoundaryRead(
    [namespace ?? 'no-context', shell.routeEntryGeneration, 'identity-access-user', userId],
    true,
    (signal) => fetchAccessUser(companyId, userId, signal),
  )
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<AccessUserDetail>

  if (state.kind !== 'ready') {
    return (
      <>
        <h2 id="identity-detail-heading">{ACCESS_TEXT.assignmentsHeading}</h2>
        <StateBlock state={state} subject={ACCESS_TEXT.detailSubject} onRetry={() => void query.refetch()} />
      </>
    )
  }

  const user = state.data
  return (
    <div data-selected-user={user.userId}>
      <div className={styles.header}>
        <h2 id="identity-detail-heading">{user.displayName}</h2>
        {user.isActive ? null : <Badge>{ACCESS_TEXT.passiveUser}</Badge>}
      </div>
      <h3>{ACCESS_TEXT.assignmentsHeading}</h3>
      <div className={styles.tableWrap}>
        <table className={styles.table} aria-label={ACCESS_TEXT.assignmentsHeading}>
          <thead>
            <tr>
              <th scope="col">{ACCESS_TEXT.columnRole}</th>
              <th scope="col">{ACCESS_TEXT.columnValidity}</th>
              <th scope="col">{ACCESS_TEXT.columnFrom}</th>
              <th scope="col">{ACCESS_TEXT.columnUntil}</th>
              <th scope="col">{ACCESS_TEXT.columnScopes}</th>
              <th scope="col">{ACCESS_TEXT.columnApprover}</th>
            </tr>
          </thead>
          <tbody>
            {user.assignments.map((assignment) => (
              <tr key={assignment.assignmentId} data-assignment={assignment.assignmentId}>
                <th scope="row">
                  {canOpenRole ? (
                    <Link className={styles.link} to={roleDetailPath(assignment.roleId, user.userId)}>
                      {assignment.roleName}
                    </Link>
                  ) : (
                    <span>{assignment.roleName}</span>
                  )}
                  <div className={styles.code}>{assignment.roleCode}</div>
                  {assignment.roleIsActive ? null : <Badge>{ACCESS_TEXT.passiveRole}</Badge>}
                </th>
                <td>
                  <ValidityBadge validity={assignment.validity} />
                </td>
                <ValidityRange from={assignment.validFromUtc} until={assignment.validUntilUtc} />
                <td>
                  <ScopeList scopes={assignment.scopes} />
                </td>
                <td>
                  <Approver person={assignment.approvedBy} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <h3>{ACCESS_TEXT.effectiveHeading}</h3>
      {user.effectivePermissions.length === 0 ? (
        <p className={styles.muted}>{ACCESS_TEXT.noEffective}</p>
      ) : (
        <ul className={styles.permissionList} aria-label={ACCESS_TEXT.effectiveHeading}>
          {user.effectivePermissions.map((permission) => (
            <li key={permission.code} data-permission={permission.code}>
              <span className={styles.code}>{permission.code}</span>
              {permission.isCritical ? <Badge tone="warn">{ACCESS_TEXT.critical}</Badge> : null}
              <span className={styles.userMeta}>
                {ACCESS_TEXT.grantedBy}: {permission.roleCodes.join(', ')}
              </span>
            </li>
          ))}
        </ul>
      )}
      <p className={styles.muted}>{ACCESS_TEXT.readOnlyNote}</p>
    </div>
  )
}

function RoleTable({
  roles,
  canOpenRole,
  selectedUserId,
}: {
  readonly roles: readonly AccessRoleSummary[]
  readonly canOpenRole: boolean
  readonly selectedUserId: string | null
}) {
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table} aria-label={ACCESS_TEXT.rolesHeading}>
        <thead>
          <tr>
            <th scope="col">{ACCESS_TEXT.columnRole}</th>
            <th scope="col">{ACCESS_TEXT.columnPermissions}</th>
            <th scope="col">{ACCESS_TEXT.columnHolders}</th>
          </tr>
        </thead>
        <tbody>
          {roles.map((role) => (
            <tr key={role.roleId} data-role={role.roleId}>
              <th scope="row">
                {canOpenRole ? (
                  <Link className={styles.link} to={roleDetailPath(role.roleId, selectedUserId)}>
                    {role.name}
                  </Link>
                ) : (
                  <span>{role.name}</span>
                )}
                <div className={styles.code}>{role.code}</div>
                {role.isActive ? null : <Badge>{ACCESS_TEXT.passiveRole}</Badge>}
              </th>
              <td>{role.permissionCount}</td>
              <td>{holderCountLabel(role.effectiveHolderCount, role.holderCount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
