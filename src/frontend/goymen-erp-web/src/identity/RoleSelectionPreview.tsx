import { useState } from 'react'
import { useQueries, useQuery } from '@tanstack/react-query'
import { Button } from '../ui/Button'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import { fetchAccessRole, fetchRoleRequestImpact } from './api'
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
  const [requestedImpact, setRequestedImpact] = useState<string | null>(null)
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
  const impactRole=selected.length===1?selected[0]:undefined
  const impactBinding=JSON.stringify([namespace,shell.routeEntryGeneration,company,user.userId,impactRole?.roleId,user.effectivePermissions?.map(permission=>[permission.code,permission.isCritical])])
  const impactScope=canRead && !capabilityFailClosed(shell) && shell.activeContext?.scopeMode==='single'
    && shell.activeContext.companyId?.toLowerCase()===company.toLowerCase()
  const impact=useQuery({queryKey:[namespace,shell.routeEntryGeneration,'identity-role-code-impact',impactBinding],
    enabled:impactScope && complete && impactRole!==undefined && requestedImpact===impactBinding,
    retry:false,staleTime:0,gcTime:0,refetchOnWindowFocus:false,queryFn:async({signal})=>{
      const snapshot=boundary.capture(),result=await fetchRoleRequestImpact(company,user.userId,impactRole!.roleId,signal)
      if(result.type==='http' && (result.status===401 || result.status===403))boundary.report(result.status,snapshot)
      return result
    }})
  const impactReady=complete && impactScope && requestedImpact===impactBinding && !impact.isFetching && impact.data?.type==='ok-single'?impact.data.data:null
  const impactState=impact.isFetching?{kind:'initial-loading'} as const:classifyOutcome(impact.data??{type:'malformed'})
  const assessment=(value:boolean|null)=>value===null?T.codeImpactUnknown:value?T.codeImpactYes:T.codeImpactNo
  const codes=(values:readonly string[])=>values.length===0?<p>{T.codeImpactNone}</p>:<ul>{values.map(code=><li key={code}>{code}</li>)}</ul>
  const permissionCodes=(values:readonly {readonly code:string;readonly isCritical:boolean}[])=>values.length===0?<p>{T.codeImpactNone}</p>
    :<ul>{values.map(permission=><li key={permission.code}>{permission.code}{permission.isCritical?` · ${T.requiresApproval}`:''}</li>)}</ul>
  const permissions = new Map<string, { readonly code: string; readonly isCritical: boolean }>()
  if (complete) for (const state of states) if (state.kind === 'ready') for (const permission of state.data.permissions) permissions.set(permission.code, permission)
  const remaining = selected.filter(role => !recordedRoles.includes(role.roleId))
  return <section aria-label={T.selectionTitle}>
    <h4>{T.selectionTitle}</h4><p>{T.company}: {companyEntry ? `${companyEntry.code} · ${companyEntry.legalName}` : company}</p><p>{T.selectionNotice}</p>
    <fieldset disabled={disabled}><legend>{T.selectRoles}</legend>
      {roles.filter(role => role.isActive).map(role => <label key={role.roleId}><input type="checkbox" checked={selectedIds.includes(role.roleId)}
        onChange={event => { setRequestedImpact(null); setSelectedIds(previous => event.target.checked ? [...previous, role.roleId] : previous.filter(id => id !== role.roleId)) }} />{role.name}{role.isCritical ? ` · ${T.requiresApproval}` : ''}</label>)}
    </fieldset>
    <p>{T.remainingRoles}: {remaining.length}</p>
    {selected.map((role, index) => <section key={role.roleId}><h5>{role.name}</h5>
      {recordedRoles.includes(role.roleId) ? <p role="status">{T.roleStepRecorded}</p> : null}
      {role.isCritical ? <p>{T.critical}</p> : null}
      {!canRead ? <p>{T.previewUnavailable}</p> : states[index].kind !== 'ready'
        ? <StateBlock state={states[index]} subject={T.selectedPermissions} onRetry={() => void queries[index].refetch()} /> : null}
      {selected.length===1?<>
        <Button type="button" disabled={disabled || !complete || !impactScope || impact.isFetching} onClick={()=>{setRequestedImpact(impactBinding);if(requestedImpact===impactBinding)void impact.refetch()}}>{T.codeImpactRead}</Button>
        {complete && impactScope && requestedImpact===impactBinding?<div aria-label={T.codeImpact} role="region"><h6>{T.codeImpact}</h6><p>{T.codeImpactNotice}</p>
          {impactReady===null?<>{impact.isFetching?null:<p>{T.codeImpactUnavailable}</p>}<StateBlock state={impactState} subject={T.codeImpact}/></>:<>
            <h6>{T.codeImpactCurrent}</h6>{permissionCodes(impactReady.currentPermissionCodes)}
            <h6>{T.codeImpactRequested}</h6>{permissionCodes(impactReady.requestedRolePermissionCodes)}
            <h6>{T.codeImpactAdded}</h6>{codes(impactReady.addedPermissionCodes)}<h6>{T.codeImpactHeld}</h6>{codes(impactReady.alreadyHeldCodes)}
            <dl><dt>{T.codeImpactCritical}</dt><dd>{assessment(impactReady.roleHasCriticalPermissions)}</dd>
              <dt>{T.codeImpactSoD}</dt><dd>{assessment(impactReady.hasSegregationOfDutiesConflict)}</dd><dt>{T.codeImpactScope}</dt><dd>{assessment(impactReady.expandsScope)}</dd></dl>
          </>}
        </div>:null}
      </>:<p>{T.codeImpactSingle}</p>}
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
