import { useState } from 'react'
import { useQueries } from '@tanstack/react-query'
import { Button } from '../ui/Button'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { fetchAccessRole } from './api'
import { IDENTITY_CAPABILITY } from './capabilities'
import type { AccessRoleDetail, AccessRoleSummary, AccessUserDetail } from './contract'
import { ACCESS_GRANT_TEXT as T } from './identityText'

/** Read-only selection basket over the live owner catalogue, never a persona seed or a batch command. */
export function RoleSelectionPreview({ company, user, roles, disabled, recordedRoles, prepare }: {
  readonly company: string; readonly user: Pick<AccessUserDetail, 'userId' | 'displayName'> & { readonly effectivePermissions?: AccessUserDetail['effectivePermissions'] }; readonly roles: readonly AccessRoleSummary[]; readonly disabled: boolean
  readonly recordedRoles: readonly string[]; readonly prepare: (roleId: string) => void
}) {
  const shell = useShellState(); const boundary = useAuthBoundary()
  const companyEntry = useCompanyDirectory().find(item => item.id.toLowerCase() === company.toLowerCase())
  const context = contextKey(shell); const namespace = context === null ? null : contextKeyString(context)
  const [selectedIds, setSelectedIds] = useState<readonly string[]>([])
  const selected = roles.filter(role => role.isActive && selectedIds.includes(role.roleId))
  const canRead = namespace !== null && hasCapability(shell, IDENTITY_CAPABILITY.roleDetail)
  const queries = useQueries({ queries: selected.map(role => ({
    queryKey: [namespace, shell.routeEntryGeneration, 'identity-role-selection-preview', company, user.userId, role.roleId],
    enabled: canRead,
    queryFn: async ({ signal }: { readonly signal: AbortSignal }) => {
      const snapshot = boundary.capture(); const result = await fetchAccessRole(company, role.roleId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })) })
  const states = queries.map(query => (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<AccessRoleDetail>)
  const complete = canRead && selected.length > 0 && states.every((state, index) => state.kind === 'ready'
    && state.data.isActive && state.data.roleId === selected[index].roleId)
  const permissions = new Map<string, { readonly code: string; readonly isCritical: boolean }>()
  if (complete) for (const state of states) if (state.kind === 'ready') for (const permission of state.data.permissions) permissions.set(permission.code, permission)
  const remaining = selected.filter(role => !recordedRoles.includes(role.roleId))
  return <section aria-label={T.selectionTitle}>
    <h4>{T.selectionTitle}</h4><p>{T.company}: {companyEntry ? `${companyEntry.code} · ${companyEntry.legalName}` : company}</p><p>{T.selectionNotice}</p>
    <fieldset disabled={disabled}><legend>{T.selectRoles}</legend>
      {roles.filter(role => role.isActive).map(role => <label key={role.roleId}><input type="checkbox" checked={selectedIds.includes(role.roleId)}
        onChange={event => setSelectedIds(previous => event.target.checked ? [...previous, role.roleId] : previous.filter(id => id !== role.roleId))} />{role.name}{role.isCritical ? ` · ${T.requiresApproval}` : ''}</label>)}
    </fieldset>
    <p>{T.remainingRoles}: {remaining.length}</p>
    {selected.map((role, index) => <section key={role.roleId}><h5>{role.name}</h5>
      {recordedRoles.includes(role.roleId) ? <p role="status">{T.roleStepRecorded}</p> : null}
      {role.isCritical ? <p>{T.critical}</p> : null}
      {!canRead ? <p>{T.previewUnavailable}</p> : states[index].kind !== 'ready'
        ? <StateBlock state={states[index]} subject={T.selectedPermissions} onRetry={() => void queries[index].refetch()} /> : null}
      <Button type="button" disabled={disabled || !complete || recordedRoles.includes(role.roleId)} onClick={() => prepare(role.roleId)}>{T.prepareRole} · {role.name}</Button>
    </section>)}
    {complete ? <section aria-label={T.selectedPermissions}><h5>{T.selectedPermissions}</h5>
      <ul>{[...permissions.values()].sort((left, right) => left.code.localeCompare(right.code)).map(permission => <li key={permission.code}>{permission.code}{permission.isCritical ? ` · ${T.requiresApproval}` : ''}</li>)}</ul>
    </section> : null}
    <section aria-label={T.currentPermissions}><h5>{T.currentPermissions}</h5>
      {user.effectivePermissions === undefined ? <p>{T.currentPermissionsUnavailable}</p> : <><p>{T.currentPermissionsNotice}</p>
        {user.effectivePermissions.length === 0 ? <p>{T.noCurrentPermissions}</p> : <ul>{user.effectivePermissions.map(permission => <li key={permission.code}>{permission.code}{permission.isCritical ? ` · ${T.requiresApproval}` : ''}</li>)}</ul>}</>}
    </section>
  </section>
}
