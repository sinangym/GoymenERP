/**
 * One role's read-only card (AT-004, SY-02 read subset): its permissions (group-wide definition) and its holders in
 * the active company only, each with validity, scopes and approver. Reached from the access list; the back link
 * returns to the same selected user (`?kullanici=` is carried through and honoured only when well-formed).
 *
 * Read-only, like the list: no assign, revoke or edit control (NEEDS-SINAN #66 5738113623, OPEN-013).
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router'
import { fetchAccessRole } from './api'
import type { AccessRoleDetail } from './contract'
import { IDENTITY_CAPABILITY, SELECTED_USER_PARAM, accessListPath, isWellFormedId } from './capabilities'
import { ACCESS_TEXT, ROLE_TEXT } from './identityText'
import { Approver, Badge, ScopeList, ValidityBadge, ValidityRange } from './AccessParts'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './identity.module.css'

export function IdentityRoleDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { roleId } = useParams()
  return (
    <RoleScreen
      key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}::${roleId ?? ''}`}
      namespace={namespace}
    />
  )
}

function RoleScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const { roleId = '' } = useParams()
  const [searchParams] = useSearchParams()
  const rawSelected = searchParams.get(SELECTED_USER_PARAM)
  const selectedUserId = isWellFormedId(rawSelected) ? rawSelected : null
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const canRead = hasCapability(shell, IDENTITY_CAPABILITY.roleDetail)
  const idIsWellFormed = isWellFormedId(roleId)

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'identity-access-role', roleId],
    enabled: namespace !== null && singleCompany && canRead && idIsWellFormed,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchAccessRole(companyId ?? '', roleId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!singleCompany) {
    return (
      <Frame selectedUserId={selectedUserId}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={ROLE_TEXT.subject} />
      </Frame>
    )
  }

  if (namespace === null || !canRead || !idIsWellFormed) {
    return (
      <Frame selectedUserId={selectedUserId}>
        <StateBlock state={{ kind: 'not-found' }} subject={ROLE_TEXT.subject} />
      </Frame>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<AccessRoleDetail>

  if (state.kind !== 'ready') {
    return (
      <Frame selectedUserId={selectedUserId}>
        <StateBlock state={state} subject={ROLE_TEXT.subject} onRetry={() => void query.refetch()} />
      </Frame>
    )
  }

  return (
    <Frame selectedUserId={selectedUserId} role={state.data}>
      <RoleCard role={state.data} />
    </Frame>
  )
}

function Frame({
  selectedUserId,
  role,
  children,
}: {
  readonly selectedUserId: string | null
  readonly role?: AccessRoleDetail
  readonly children: React.ReactNode
}) {
  const shell = useShellState()
  // The list is its own grant; without it the way back is stated, not offered as a link that would only 404.
  const canGoBack = hasCapability(shell, IDENTITY_CAPABILITY.accessList)
  return (
    <section aria-labelledby="identity-role-heading" className={styles.page}>
      <nav aria-label="Geri" className={styles.breadcrumb}>
        {canGoBack ? (
          <Link to={accessListPath(selectedUserId)}>← {ROLE_TEXT.back}</Link>
        ) : (
          <span>← {ROLE_TEXT.back}</span>
        )}
      </nav>
      <div className={styles.header}>
        <h1 id="identity-role-heading">{role === undefined ? ACCESS_TEXT.columnRole : role.name}</h1>
        {role?.isSystem ? <Badge>{ACCESS_TEXT.systemRole}</Badge> : null}
        {role !== undefined && !role.isActive ? <Badge>{ACCESS_TEXT.passiveRole}</Badge> : null}
      </div>
      {role === undefined ? null : (
        <p className={styles.lead}>
          {ROLE_TEXT.codeLabel}: <span className={styles.code}>{role.code}</span>
        </p>
      )}
      {children}
    </section>
  )
}

function RoleCard({ role }: { readonly role: AccessRoleDetail }) {
  const shell = useShellState()
  const canOpenList = hasCapability(shell, IDENTITY_CAPABILITY.accessList)
  return (
    <>
      <section aria-labelledby="identity-role-permissions" className={styles.panel}>
        <h2 id="identity-role-permissions">{ROLE_TEXT.permissionsHeading}</h2>
        {role.permissions.length === 0 ? (
          <p className={styles.muted}>{ROLE_TEXT.noPermissions}</p>
        ) : (
          <ul className={styles.permissionList} aria-label={ROLE_TEXT.permissionsHeading}>
            {role.permissions.map((permission) => (
              <li key={permission.code} data-permission={permission.code}>
                <span className={styles.code}>{permission.code}</span>
                {permission.isCritical ? <Badge tone="warn">{ACCESS_TEXT.critical}</Badge> : null}
              </li>
            ))}
          </ul>
        )}
      </section>
      <section aria-labelledby="identity-role-holders" className={styles.panel}>
        <h2 id="identity-role-holders">{ROLE_TEXT.holdersHeading}</h2>
        {role.holders.length === 0 ? (
          <p className={styles.muted}>{ROLE_TEXT.noHolders}</p>
        ) : (
          <div className={styles.tableWrap}>
            <table className={styles.table} aria-label={ROLE_TEXT.holdersHeading}>
              <thead>
                <tr>
                  <th scope="col">{ACCESS_TEXT.columnUser}</th>
                  <th scope="col">{ACCESS_TEXT.columnValidity}</th>
                  <th scope="col">{ACCESS_TEXT.columnFrom}</th>
                  <th scope="col">{ACCESS_TEXT.columnUntil}</th>
                  <th scope="col">{ACCESS_TEXT.columnScopes}</th>
                  <th scope="col">{ACCESS_TEXT.columnApprover}</th>
                </tr>
              </thead>
              <tbody>
                {role.holders.map((holder) => (
                  <tr key={holder.assignmentId} data-holder={holder.userId}>
                    <th scope="row">
                      {canOpenList ? (
                        <Link className={styles.link} to={accessListPath(holder.userId)}>
                          {holder.displayName}
                        </Link>
                      ) : (
                        <span>{holder.displayName}</span>
                      )}
                      {holder.userIsActive ? null : <Badge>{ACCESS_TEXT.passiveUser}</Badge>}
                    </th>
                    <td>
                      <ValidityBadge validity={holder.validity} />
                    </td>
                    <ValidityRange from={holder.validFromUtc} until={holder.validUntilUtc} />
                    <td>
                      <ScopeList scopes={holder.scopes} />
                    </td>
                    <td>
                      <Approver person={holder.approvedBy} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  )
}
